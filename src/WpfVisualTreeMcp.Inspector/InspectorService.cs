using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Main entry point for the inspector when loaded into a WPF application.
/// </summary>
public partial class InspectorService : IDisposable
{
    private static readonly TimeSpan FullContentCaptureTimeout = TimeSpan.FromSeconds(25);
    private readonly IpcServer _ipcServer;
    private readonly TreeWalker _treeWalker;
    private readonly WorkflowRecorder _recorder;
    private readonly PropertyReader _propertyReader;
    private readonly PropertyWriter _propertyWriter;
    private readonly Dictionary<string, Dictionary<string, SnapshotNode>> _snapshots = new();
    private int _snapshotCounter;
    private readonly BindingAnalyzer _bindingAnalyzer;
    private readonly ElementHighlighter _highlighter;
    private readonly PropertyWatcher _propertyWatcher;
    private readonly ResourceInspector _resourceInspector;
    private readonly ControlInteractor _interactor;
    private readonly System.Threading.SemaphoreSlim _interactionGate = new(1, 1);
    private readonly System.Threading.CancellationTokenSource _inputShutdown = new();
    private bool _isRunning;
    private bool _disposed;

    private static readonly object _initLock = new();
    public static InspectorService? Instance { get; private set; }

#if NETFRAMEWORK
    static InspectorService()
    {
        // Injected applications do not inherit our NuGet binding redirects.
        // Resolve version differences only for requests originating in this
        // payload directory; leave the host application's dependencies alone.
        var directory = Path.GetDirectoryName(typeof(InspectorService).Assembly.Location);
        bool IsPayloadAssembly(System.Reflection.Assembly? assembly) =>
            assembly != null && !assembly.IsDynamic &&
            string.Equals(Path.GetDirectoryName(assembly.Location), directory, StringComparison.OrdinalIgnoreCase);
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            // Runtime-generated bindings (e.g. System.Memory -> Unsafe) may
            // omit RequestingAssembly. In that case require a payload caller.
            var fromPayload = IsPayloadAssembly(args.RequestingAssembly) ||
                (args.RequestingAssembly == null && System.Linq.Enumerable.Any(
                    new System.Diagnostics.StackTrace().GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>(),
                    frame => IsPayloadAssembly(frame.GetMethod()?.Module.Assembly) &&
                        frame.GetMethod()?.DeclaringType != typeof(InspectorService) &&
                        frame.GetMethod()?.DeclaringType?.DeclaringType != typeof(InspectorService)));
            if (!fromPayload)
                return null;

            var requested = new System.Reflection.AssemblyName(args.Name);
            var path = Path.Combine(directory!, requested.Name + ".dll");
            if (!File.Exists(path)) return null;
            var available = System.Reflection.AssemblyName.GetAssemblyName(path);
            if (!string.Equals(available.Name, requested.Name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(available.CultureName, requested.CultureName, StringComparison.OrdinalIgnoreCase) ||
                !System.Linq.Enumerable.SequenceEqual(available.GetPublicKeyToken() ?? Array.Empty<byte>(),
                    requested.GetPublicKeyToken() ?? Array.Empty<byte>()) ||
                available.Version < requested.Version)
                return null;

            DebugLog($"Resolving Inspector dependency {args.Name} from {path}");
            return System.Reflection.Assembly.LoadFrom(path);
        };
    }
#endif

    /// <summary>
    /// Initialize the Inspector service with the current process ID.
    /// This overload is called by the CLR hosting API (ExecuteInDefaultAppDomain)
    /// when the Inspector is injected into a running process.
    /// </summary>
    /// <param name="processIdString">The process ID as a string.</param>
    /// <returns>0 on success, non-zero on failure.</returns>
    public static int Initialize(string processIdString)
    {
        try
        {
            DebugLog($"Inspector.Initialize(string) called with: {processIdString}");

            if (!int.TryParse(processIdString, out int processId))
            {
                DebugLog($"ERROR: Failed to parse process ID from string: {processIdString}");
                return -1;
            }

            Initialize(processId);
            return 0;
        }
        catch (Exception ex)
        {
            DebugLog($"ERROR in Initialize(string): {ex.Message}\n{ex.StackTrace}");
            return -1;
        }
    }

    /// <summary>
    /// Entry point for CoreCLR hosting via hostfxr load_assembly_and_get_function_pointer.
    /// Signature matches component_entry_point_fn: (IntPtr args, int sizeBytes) -> int.
    /// The IntPtr points to a 4-byte buffer containing the process ID as a little-endian int32.
    /// </summary>
    public static int InitializeUnmanaged(IntPtr args, int sizeBytes)
    {
        try
        {
            int processId;
            if (args == IntPtr.Zero || sizeBytes < sizeof(int))
            {
                processId = System.Diagnostics.Process.GetCurrentProcess().Id;
                DebugLog($"InitializeUnmanaged: no args, using current PID={processId}");
            }
            else
            {
                processId = System.Runtime.InteropServices.Marshal.ReadInt32(args);
                DebugLog($"InitializeUnmanaged: PID={processId} from args");
            }

            Initialize(processId);
            return 0;
        }
        catch (Exception ex)
        {
            DebugLog($"ERROR in InitializeUnmanaged: {ex.Message}\n{ex.StackTrace}");
            return -1;
        }
    }

    /// <summary>
    /// Initialize the Inspector service with the specified process ID.
    /// </summary>
    /// <param name="processId">The process ID to attach to.</param>
    public static void Initialize(int processId)
    {
        if (Instance != null) return;

        lock (_initLock)
        {
            if (Instance != null) return; // Double-check after acquiring lock

            try
            {
                DebugLog($"Inspector.Initialize called for PID={processId}");
                Instance = new InspectorService(processId);
                DebugLog("Inspector instance created, calling Start()");
                Instance.Start();
                DebugLog("Inspector started successfully");
            }
            catch (Exception ex)
            {
                Instance = null; // Reset on failure so retry is possible
                DebugLog($"ERROR in Initialize: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }

    private InspectorService(int processId)
    {
        _treeWalker = new TreeWalker();
        _recorder = new WorkflowRecorder(_treeWalker);
        _propertyReader = new PropertyReader();
        _propertyWriter = new PropertyWriter();
        _bindingAnalyzer = new BindingAnalyzer();
        _highlighter = new ElementHighlighter();
        _propertyWatcher = new PropertyWatcher();
        _resourceInspector = new ResourceInspector();
        _interactor = new ControlInteractor();
        _ipcServer = new IpcServer(processId, HandleRequestAsync);

        // Wire up property change notifications
        _propertyWatcher.PropertyChanged += OnPropertyChanged;

        // Start capturing binding errors
        _bindingAnalyzer.StartCapturingErrors();
    }

    private void OnPropertyChanged(PropertyChangedNotification notification)
    {
        // Send notification through IPC
        var json = IpcSerializer.Serialize(notification);
        _ipcServer.SendNotification(json);
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _ipcServer.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _bindingAnalyzer.StopCapturingErrors();
        _ipcServer.Stop();
    }

    private async Task<IpcResponse> HandleRequestAsync(string requestType, JsonElement data)
    {
        bool ownsInteraction = false;
        try
        {
            DebugLog($"HandleRequestAsync: requestType={requestType}");

            if (Application.Current == null)
            {
                DebugLog("ERROR: Application.Current is NULL!");
                return new GetVisualTreeResponse { Success = false, Error = "Application.Current is null" };
            }

            var fullContent = requestType == "CaptureScreenshot" &&
                IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(data)?.FullContent == true;
            // Input sequences share the OS mouse. Fail fast instead of interleaving
            // concurrent mutations or queuing them past the IPC timeout.
            if (requestType is "Drag" or "TouchGesture" or "ClickElement" or "SelectItem" or "SetText" or "SendKeys"
                or "SetProperty" or "RevertProperty" or "Record" or "Replay" or "AssertElement"
                || fullContent || (_recorder.Active && WorkflowCodec.IsRecordable(requestType)))
            {
                ownsInteraction = _interactionGate.Wait(0);
                if (!ownsInteraction)
                    return new DragResponse { Success = false, Error = "Another interaction is in progress. Retry after it completes." };
            }

            if (requestType == "Replay")
                return await HandleReplayAsync(data).ConfigureAwait(false);

            WorkflowStep? recordedStep = null;
            if (ownsInteraction && _recorder.Active && WorkflowCodec.IsRecordable(requestType))
                recordedStep = await OnDispatcher(() => _recorder.Prepare(requestType, data), _inputShutdown.Token);

            using var captureTimeout = fullContent
                ? CancellationTokenSource.CreateLinkedTokenSource(_inputShutdown.Token) : null;
            captureTimeout?.CancelAfter(FullContentCaptureTimeout);
            var captureToken = captureTimeout?.Token ?? CancellationToken.None;

            var result = await ExecuteOperationAsync(requestType, data, captureToken).ConfigureAwait(false);
            if (ownsInteraction && result.Success)
                await OnDispatcher(() => true, _inputShutdown.Token, System.Windows.Threading.DispatcherPriority.Background);
            if (recordedStep != null && result.Success && (result is not WaitForElementResponse wait || wait.Matched))
                await OnDispatcher(() => { _recorder.Commit(recordedStep); return true; }, _inputShutdown.Token);

            DebugLog($"HandleRequest completed successfully");
            return result;
        }
        catch (TimeoutException ex)
        {
            var error = GetTimeoutErrorMessage(ex);
            DebugLog($"TIMEOUT in HandleRequestAsync: {error}");
            return new GetVisualTreeResponse { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            DebugLog($"ERROR in HandleRequestAsync: {ex.Message}\n{ex.StackTrace}");
            return new GetVisualTreeResponse { Success = false, Error = ex.Message };
        }
        finally
        {
            if (ownsInteraction) _interactionGate.Release();
        }
    }

    private async Task<IpcResponse> HandleDragAsync(JsonElement data, CancellationToken cancellationToken = default)
    {
        var request = IpcSerializer.DeserializeRequestData<DragRequest>(data)
            ?? throw new ArgumentException("Drag request is required.");
        if (request.Options == null) throw new ArgumentException("Drag options are required.");
        request.Options.Validate();
        using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_inputShutdown.Token, cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var plan = await Task.Run(() => Application.Current.Dispatcher.Invoke(() =>
        {
            UIElement Resolve(string handle)
            {
                var element = _treeWalker.ResolveHandle(handle);
                if (element == null) throw new InvalidOperationException(StaleHandleError(handle));
                return element as UIElement ?? throw new InvalidOperationException(WrongElementTypeError(handle, element, "UIElement"));
            }
            return _interactor.PrepareDrag(Resolve(request.Options.ElementHandle),
                request.Options.TargetHandle == null ? null : Resolve(request.Options.TargetHandle), request.Options);
        }, System.Windows.Threading.DispatcherPriority.Normal, timeout.Token, TimeSpan.FromSeconds(5))).ConfigureAwait(false);

        var result = await MouseDragExecutor.RunAsync(plan, new NativeMouseInput(), timeout.Token).ConfigureAwait(false);
        return new DragResponse { RequestId = request.RequestId, Result = result };
    }

    private async Task<IpcResponse> HandleTouchGestureAsync(JsonElement data, CancellationToken cancellationToken = default)
    {
        var request = IpcSerializer.DeserializeRequestData<TouchGestureRequest>(data)
            ?? throw new ArgumentException("Touch gesture request is required.");
        if (request.Options == null) throw new ArgumentException("Touch gesture options are required.");
        request.Options.Validate();
        using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_inputShutdown.Token, cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var plan = await Task.Run(() => Application.Current.Dispatcher.Invoke(() =>
        {
            var handle = request.Options.ElementHandle;
            var resolved = _treeWalker.ResolveHandle(handle);
            if (resolved == null) throw new InvalidOperationException(StaleHandleError(handle));
            if (resolved is not UIElement element)
                throw new InvalidOperationException(WrongElementTypeError(handle, resolved, "UIElement"));
            return _interactor.PrepareTouch(element, request.Options);
        }, System.Windows.Threading.DispatcherPriority.Normal, timeout.Token, TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        var result = await TouchGestureExecutor.RunAsync(plan, new NativeTouchInput(), timeout.Token).ConfigureAwait(false);
        return new TouchGestureResponse { RequestId = request.RequestId, Result = result };
    }

    private static void DebugLog(string message)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "WpfInspector_Debug.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
            // Ignore logging errors
        }
    }

    internal static string GetTimeoutErrorMessage(TimeoutException exception) =>
        exception is FullContentCaptureTimeoutException ? exception.Message : "Request timeout: UI thread is busy";

    private IpcResponse HandleRequest(string requestType, JsonElement data, CancellationToken captureToken)
    {
        return requestType switch
        {
            "Record" => HandleRecord(data),
            "AssertElement" => HandleAssertElement(data),
            "Report" => HandleReport(data),
            "GetVisualTree" => HandleGetVisualTree(data),
            "GetElementProperties" => HandleGetElementProperties(data),
            "FindElements" => HandleFindElements(data),
            "FindElementsDeep" => HandleFindElementsDeep(data),
            "GetBindings" => HandleGetBindings(data),
            "GetBindingErrors" => HandleGetBindingErrors(),
            "GetResources" => HandleGetResources(data),
            "GetStyles" => HandleGetStyles(data),
            "HighlightElement" => HandleHighlightElement(data),
            "GetLayoutInfo" => HandleGetLayoutInfo(data),
            "WatchProperty" => HandleWatchProperty(data),
            "ExportTree" => HandleExportTree(data),
            "CaptureScreenshot" => HandleCaptureScreenshot(data, captureToken),
            "GetDataContext" => HandleGetDataContext(data),
            "ClearBindingErrors" => HandleClearBindingErrors(),
            "SetProperty" => HandleSetProperty(data),
            "RevertProperty" => HandleRevertProperty(data),
            "EvaluateBinding" => HandleEvaluateBinding(data),
            "ExplainTriggers" => HandleExplainTriggers(data),
            "Snapshot" => HandleSnapshot(data),
            "Diff" => HandleDiff(data),
            "ClickElement" => HandleClickElement(data),
            "SelectItem" => HandleSelectItem(data),
            "SetText" => HandleSetText(data),
            "SendKeys" => HandleSendKeys(data),
            _ => new GetVisualTreeResponse { Success = false, Error = $"Unknown request: {requestType}" }
        };
    }

    private IpcResponse HandleReport(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ReportRequest>(data)!;
        IEnumerable<DependencyObject> roots = TreeWalker.GetAllSearchRoots();
        if (request.RootHandle != null)
        {
            var root = _treeWalker.ResolveHandle(request.RootHandle)
                ?? throw new InvalidOperationException(StaleHandleError(request.RootHandle));
            roots = new[] { root };
        }
        return new ReportResponse
        {
            RequestId = request.RequestId,
            Report = DiagnosticReporter.Capture(roots, _treeWalker, _bindingAnalyzer, request.RootHandle,
                request.MaxElements, request.MaxDepth, request.MaxFindings)
        };
    }

    private IpcResponse HandleGetVisualTree(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetVisualTreeRequest>(data);
        var maxDepth = request?.MaxDepth ?? 10;

        DependencyObject? root = null;
        if (!string.IsNullOrEmpty(request?.RootHandle))
        {
            root = _treeWalker.ResolveHandle(request.RootHandle);
            if (root == null)
            {
                DebugLog($"HandleGetVisualTree: handle '{request.RootHandle}' not found in cache ({_treeWalker.HandleCacheCount} cached elements)");
                return new GetVisualTreeResponse
                {
                    Success = false,
                    Error = StaleHandleError(request.RootHandle)
                };
            }
            DebugLog($"HandleGetVisualTree: resolved handle '{request.RootHandle}' to {root.GetType().Name}");
        }
        else
        {
            root = GetDefaultRoot();
        }

        if (root == null)
        {
            return new GetVisualTreeResponse { Success = false, Error = "No root element found. Ensure the application has at least one visible window." };
        }

        var treeJson = _treeWalker.WalkVisualTree(root, maxDepth);
        return new GetVisualTreeResponse
        {
            RequestId = request?.RequestId ?? "",
            TreeJson = treeJson,
            TotalElements = CountElements(treeJson),
            MaxDepthReached = treeJson.Contains("\"maxDepthReached\":true")
        };
    }

    private IpcResponse HandleGetElementProperties(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetElementPropertiesRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new GetElementPropertiesResponse { Success = false, Error = "ElementHandle required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new GetElementPropertiesResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        var propsJson = _propertyReader.GetProperties(element);
        return new GetElementPropertiesResponse
        {
            RequestId = request.RequestId,
            PropertiesJson = propsJson
        };
    }

    /// <summary>
    /// Consistent error for expired/unknown element handles, with recovery guidance for the caller.
    /// </summary>
    private static string StaleHandleError(string handle) =>
        $"Element handle '{handle}' not found. Handles expire when the target app restarts, " +
        "the Inspector is re-injected, or the element is removed from the UI and garbage-collected. " +
        "Re-run wpf_find_elements (CLI: find) to get fresh handles.";

    private static string WrongElementTypeError(string handle, DependencyObject element, string requiredType) =>
        $"Element '{handle}' is a {element.GetType().Name}, which is not a {requiredType}; " +
        $"this operation requires a {requiredType}.";

    private static FindCriteria BuildCriteria(string? typeName, string? elementName, string? text,
        Dictionary<string, string>? propertyFilter, bool visibleOnly) => new()
    {
        TypeName = typeName,
        ElementName = elementName,
        Text = text,
        PropertyFilter = propertyFilter,
        VisibleOnly = visibleOnly
    };

    private IpcResponse HandleFindElements(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<FindElementsRequest>(data);

        var maxResults = request?.MaxResults ?? 50;
        var criteria = BuildCriteria(request?.TypeName, request?.ElementName, request?.Text,
            request?.PropertyFilter, request?.VisibleOnly ?? false);

        // If a specific root handle is given, search from there
        if (!string.IsNullOrEmpty(request?.RootHandle))
        {
            var root = _treeWalker.ResolveHandle(request.RootHandle);
            if (root == null)
            {
                return new FindElementsResponse { Success = false, Error = StaleHandleError(request.RootHandle) };
            }

            var elementsJson = _treeWalker.FindElements(root, criteria, maxResults);
            return new FindElementsResponse
            {
                RequestId = request?.RequestId ?? "",
                ElementsJson = elementsJson,
                Count = ParseJsonCount(elementsJson)
            };
        }

        // No root specified: search across ALL windows for maximum coverage
        var allRoots = TreeWalker.GetAllSearchRoots();
        if (allRoots.Count == 0)
        {
            return new FindElementsResponse { Success = false, Error = "No root element found" };
        }

        // Search each window, accumulating results
        var allResults = new System.Text.StringBuilder();
        allResults.Append("{\"elements\":[");
        int totalCount = 0;
        bool first = true;

        foreach (var root in allRoots)
        {
            if (totalCount >= maxResults) break;
            var json = _treeWalker.FindElements(root, criteria, maxResults - totalCount);
            var count = ParseJsonCount(json);
            if (count > 0)
            {
                // Extract elements array content from {"elements":[...],"count":N}
                var elemStart = json.IndexOf('[') + 1;
                var elemEnd = json.LastIndexOf(']');
                if (elemStart > 0 && elemEnd > elemStart)
                {
                    if (!first) allResults.Append(",");
                    first = false;
                    allResults.Append(json.Substring(elemStart, elemEnd - elemStart));
                    totalCount += count;
                }
            }
        }

        allResults.Append($"],\"count\":{totalCount}}}");
        var resultJson = allResults.ToString();

        return new FindElementsResponse
        {
            RequestId = request?.RequestId ?? "",
            ElementsJson = resultJson,
            Count = totalCount
        };
    }

    private IpcResponse HandleFindElementsDeep(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<FindElementsDeepRequest>(data);

        var criteria = BuildCriteria(request?.TypeName, request?.ElementName, request?.Text,
            request?.PropertyFilter, request?.VisibleOnly ?? false);

        // If a specific root handle is given, search from there
        if (!string.IsNullOrEmpty(request?.RootHandle))
        {
            var root = _treeWalker.ResolveHandle(request.RootHandle);
            if (root == null)
            {
                return new FindElementsDeepResponse { Success = false, Error = StaleHandleError(request.RootHandle) };
            }

            var elementsJson = _treeWalker.FindElementsDeep(root, criteria);
            return new FindElementsDeepResponse
            {
                RequestId = request?.RequestId ?? "",
                ElementsJson = elementsJson,
                Count = ParseJsonCount(elementsJson)
            };
        }

        // No root specified: search across ALL windows
        var allRoots = TreeWalker.GetAllSearchRoots();
        if (allRoots.Count == 0)
        {
            return new FindElementsDeepResponse { Success = false, Error = "No root element found" };
        }

        var allResults = new System.Text.StringBuilder();
        allResults.Append("{\"elements\":[");
        int totalCount = 0;
        bool first = true;

        foreach (var root in allRoots)
        {
            var json = _treeWalker.FindElementsDeep(root, criteria);
            var count = ParseJsonCount(json);
            if (count > 0)
            {
                var elemStart = json.IndexOf('[') + 1;
                var elemEnd = json.LastIndexOf(']');
                if (elemStart > 0 && elemEnd > elemStart)
                {
                    if (!first) allResults.Append(",");
                    first = false;
                    allResults.Append(json.Substring(elemStart, elemEnd - elemStart));
                    totalCount += count;
                }
            }
        }

        allResults.Append($"],\"count\":{totalCount},\"truncated\":false}}");
        var resultJson = allResults.ToString();

        return new FindElementsDeepResponse
        {
            RequestId = request?.RequestId ?? "",
            ElementsJson = resultJson,
            Count = totalCount
        };
    }

    private IpcResponse HandleGetBindings(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetBindingsRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new GetBindingsResponse { Success = false, Error = "ElementHandle required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new GetBindingsResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        var bindingsJson = _bindingAnalyzer.GetBindings(element);
        return new GetBindingsResponse
        {
            RequestId = request.RequestId,
            BindingsJson = bindingsJson
        };
    }

    private IpcResponse HandleGetBindingErrors()
    {
        var errorsJson = _bindingAnalyzer.GetBindingErrors();
        return new GetBindingErrorsResponse
        {
            ErrorsJson = errorsJson,
            // The analyzer JSON is {"errors":[...],"count":N} — read its count field;
            // CountJsonArrayItems is for arrays and overcounts on objects.
            Count = ParseJsonCount(errorsJson)
        };
    }

    private IpcResponse HandleGetResources(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetResourcesRequest>(data);
        var scope = request?.Scope ?? "all";

        FrameworkElement? element = null;
        if (!string.IsNullOrEmpty(request?.ElementHandle))
        {
            element = _treeWalker.ResolveHandle(request.ElementHandle) as FrameworkElement;
        }

        var resourcesJson = _resourceInspector.GetResources(scope, element);
        return new GetResourcesResponse
        {
            RequestId = request?.RequestId ?? "",
            ResourcesJson = resourcesJson
        };
    }

    private IpcResponse HandleGetStyles(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetStylesRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new GetStylesResponse { Success = false, Error = "ElementHandle required" };
        }

        var resolved = _treeWalker.ResolveHandle(request.ElementHandle);
        if (resolved == null)
        {
            return new GetStylesResponse { Success = false, Error = StaleHandleError(request.ElementHandle) };
        }
        if (resolved is not FrameworkElement element)
        {
            return new GetStylesResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle, resolved, "FrameworkElement") };
        }

        var stylesJson = _resourceInspector.GetStyle(element);
        return new GetStylesResponse
        {
            RequestId = request.RequestId,
            StylesJson = stylesJson
        };
    }

    private IpcResponse HandleHighlightElement(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<HighlightElementRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new HighlightElementResponse { Success = false, Error = "ElementHandle required" };
        }

        var resolved = _treeWalker.ResolveHandle(request.ElementHandle);
        if (resolved == null)
        {
            return new HighlightElementResponse { Success = false, Error = StaleHandleError(request.ElementHandle) };
        }
        if (resolved is not UIElement element)
        {
            return new HighlightElementResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle, resolved, "UIElement") };
        }

        _highlighter.Highlight(element, request.DurationMs);
        return new HighlightElementResponse { RequestId = request.RequestId };
    }

    private IpcResponse HandleExplainTriggers(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ExplainTriggersRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new ExplainTriggersResponse { Success = false, Error = "ElementHandle required" };
        }

        var resolved = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (resolved == null)
        {
            return new ExplainTriggersResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }
        if (resolved is not FrameworkElement fe)
        {
            return new ExplainTriggersResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle!, resolved, "FrameworkElement") };
        }

        var json = _resourceInspector.ExplainTriggers(fe, request.PropertyName);
        return new ExplainTriggersResponse { RequestId = request.RequestId, Json = json };
    }

    private IpcResponse HandleEvaluateBinding(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<EvaluateBindingRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new EvaluateBindingResponse { Success = false, Error = "ElementHandle required" };
        }
        if (string.IsNullOrEmpty(request.PropertyName))
        {
            return new EvaluateBindingResponse { Success = false, Error = "PropertyName required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new EvaluateBindingResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        var json = _bindingAnalyzer.EvaluateBinding(element, request.PropertyName);
        return new EvaluateBindingResponse
        {
            RequestId = request.RequestId,
            EvaluationJson = json
        };
    }

    private IpcResponse HandleSnapshot(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<SnapshotRequest>(data);

        DependencyObject? root;
        if (!string.IsNullOrEmpty(request?.ElementHandle))
        {
            root = _treeWalker.ResolveHandle(request.ElementHandle!);
            if (root == null)
            {
                return new SnapshotResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
            }
        }
        else
        {
            root = GetDefaultRoot();
            if (root == null)
            {
                return new SnapshotResponse { Success = false, Error = "No root element found." };
            }
        }

        var maxDepth = Math.Max(1, Math.Min(request?.MaxDepth ?? 25, 100));
        var nodes = _treeWalker.CaptureSnapshot(root, maxDepth);

        var label = string.IsNullOrWhiteSpace(request?.Label) ? $"snap_{_snapshotCounter++}" : request!.Label!;
        _snapshots[label] = nodes;

        return new SnapshotResponse
        {
            RequestId = request?.RequestId ?? "",
            Label = label,
            ElementCount = nodes.Count
        };
    }

    private IpcResponse HandleDiff(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<DiffRequest>(data);
        if (request == null || string.IsNullOrEmpty(request.Before) || string.IsNullOrEmpty(request.After))
        {
            return new DiffResponse { Success = false, Error = "Both 'before' and 'after' snapshot labels are required." };
        }

        if (!_snapshots.TryGetValue(request.Before, out var before))
        {
            return new DiffResponse { Success = false, Error = $"Snapshot '{request.Before}' not found. Capture it first with wpf_snapshot." };
        }
        if (!_snapshots.TryGetValue(request.After, out var after))
        {
            return new DiffResponse { Success = false, Error = $"Snapshot '{request.After}' not found. Capture it first with wpf_snapshot." };
        }

        var (json, changed, added, removed) = ComputeDiff(before, after);
        return new DiffResponse
        {
            RequestId = request.RequestId,
            DiffJson = json,
            ChangedCount = changed,
            AddedCount = added,
            RemovedCount = removed
        };
    }

    /// <summary>
    /// Structural diff of two snapshots, keyed by element handle. Returns the JSON plus counts.
    /// </summary>
    private static (string Json, int Changed, int Added, int Removed) ComputeDiff(
        Dictionary<string, SnapshotNode> before, Dictionary<string, SnapshotNode> after)
    {
        var changed = new System.Text.StringBuilder();
        var added = new System.Text.StringBuilder();
        var removed = new System.Text.StringBuilder();
        int changedN = 0, addedN = 0, removedN = 0;

        // Changed + removed: iterate 'before'.
        foreach (var kvp in before)
        {
            if (after.TryGetValue(kvp.Key, out var afterNode))
            {
                var propChanges = DiffProperties(kvp.Value.Properties, afterNode.Properties);
                if (propChanges.Count > 0)
                {
                    if (changedN++ > 0) changed.Append(",");
                    changed.Append("{");
                    changed.Append($"\"handle\":\"{kvp.Value.Handle}\"");
                    changed.Append($",\"typeName\":\"{EscapeJson(afterNode.TypeName)}\"");
                    if (!string.IsNullOrEmpty(afterNode.Name))
                        changed.Append($",\"name\":\"{EscapeJson(afterNode.Name)}\"");
                    changed.Append($",\"path\":\"{EscapeJson(afterNode.Path)}\"");
                    changed.Append(",\"changes\":{");
                    var first = true;
                    foreach (var pc in propChanges)
                    {
                        if (!first) changed.Append(",");
                        first = false;
                        changed.Append($"\"{EscapeJson(pc.Key)}\":{{\"from\":\"{EscapeJson(pc.Value.From)}\",\"to\":\"{EscapeJson(pc.Value.To)}\"}}");
                    }
                    changed.Append("}}");
                }
            }
            else
            {
                if (removedN++ > 0) removed.Append(",");
                removed.Append(SerializeIdentity(kvp.Value));
            }
        }

        // Added: in 'after' but not 'before'.
        foreach (var kvp in after)
        {
            if (!before.ContainsKey(kvp.Key))
            {
                if (addedN++ > 0) added.Append(",");
                added.Append(SerializeIdentity(kvp.Value));
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("{");
        sb.Append($"\"summary\":{{\"changed\":{changedN},\"added\":{addedN},\"removed\":{removedN}}}");
        sb.Append($",\"changed\":[{changed}]");
        sb.Append($",\"added\":[{added}]");
        sb.Append($",\"removed\":[{removed}]");
        sb.Append("}");
        return (sb.ToString(), changedN, addedN, removedN);
    }

    private static Dictionary<string, (string From, string To)> DiffProperties(
        Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var changes = new Dictionary<string, (string, string)>();
        foreach (var kvp in after)
        {
            var oldVal = before.TryGetValue(kvp.Key, out var b) ? b : "(absent)";
            if (oldVal != kvp.Value)
            {
                changes[kvp.Key] = (oldVal, kvp.Value);
            }
        }
        return changes;
    }

    private static string SerializeIdentity(SnapshotNode node)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{");
        sb.Append($"\"handle\":\"{node.Handle}\"");
        sb.Append($",\"typeName\":\"{EscapeJson(node.TypeName)}\"");
        if (!string.IsNullOrEmpty(node.Name))
            sb.Append($",\"name\":\"{EscapeJson(node.Name)}\"");
        sb.Append($",\"path\":\"{EscapeJson(node.Path)}\"");
        sb.Append("}");
        return sb.ToString();
    }

    private static string EscapeJson(string? text)
    {
        if (text == null) return string.Empty;
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private IpcResponse HandleSetProperty(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<SetPropertyRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new SetPropertyResponse { Success = false, Error = "ElementHandle required" };
        }
        if (string.IsNullOrEmpty(request.PropertyName))
        {
            return new SetPropertyResponse { Success = false, Error = "PropertyName required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new SetPropertyResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        try
        {
            var outcome = _propertyWriter.SetProperty(element, request.ElementHandle!, request.PropertyName, request.Value ?? string.Empty);
            DebugLog($"SetProperty: {element.GetType().Name}.{request.PropertyName} = {outcome.AppliedValue} (was {outcome.PreviousSource})");
            return new SetPropertyResponse
            {
                RequestId = request.RequestId,
                ElementType = element.GetType().Name,
                AppliedValue = outcome.AppliedValue,
                ValueType = outcome.ValueType,
                PreviousSource = outcome.PreviousSource
            };
        }
        catch (Exception ex)
        {
            DebugLog($"SetProperty failed: {ex.Message}");
            return new SetPropertyResponse { Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleRevertProperty(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<RevertPropertyRequest>(data);
        if (request == null)
        {
            return new RevertPropertyResponse { Success = false, Error = "Invalid RevertProperty request" };
        }

        try
        {
            if (request.All)
            {
                var count = _propertyWriter.RevertAll();
                DebugLog($"RevertProperty: reverted all ({count})");
                return new RevertPropertyResponse
                {
                    RequestId = request.RequestId,
                    RevertedCount = count,
                    PendingCount = _propertyWriter.PendingCount
                };
            }

            var reverted = _propertyWriter.RevertLast(request.ElementHandle, request.PropertyName);
            if (reverted == null)
            {
                return new RevertPropertyResponse
                {
                    Success = false,
                    Error = "No matching live-edited property to revert."
                };
            }

            DebugLog($"RevertProperty: reverted {reverted.Value.Handle}.{reverted.Value.Property}");
            return new RevertPropertyResponse
            {
                RequestId = request.RequestId,
                RevertedCount = 1,
                RevertedHandle = reverted.Value.Handle,
                RevertedProperty = reverted.Value.Property,
                PendingCount = _propertyWriter.PendingCount
            };
        }
        catch (Exception ex)
        {
            DebugLog($"RevertProperty failed: {ex.Message}");
            return new RevertPropertyResponse { Success = false, Error = ex.Message };
        }
    }

    private async Task<IpcResponse> HandleWaitForElementAsync(JsonElement data, CancellationToken cancellationToken = default)
    {
        var request = IpcSerializer.DeserializeRequestData<WaitForElementRequest>(data);
        if (request == null)
        {
            return new WaitForElementResponse { Success = false, Error = "Invalid WaitForElement request" };
        }

        var condition = (request.Condition ?? "visible").ToLowerInvariant();
        if (condition != "visible" && condition != "exists" && condition != "enabled" && condition != "hidden")
        {
            return new WaitForElementResponse
            {
                Success = false,
                Error = $"Unknown condition '{request.Condition}'. Expected: visible, exists, enabled, or hidden."
            };
        }

        if (string.IsNullOrEmpty(request.TypeName) && string.IsNullOrEmpty(request.ElementName) && string.IsNullOrEmpty(request.Text))
        {
            return new WaitForElementResponse
            {
                Success = false,
                Error = "At least type_name, element_name or text is required to identify the element to wait for."
            };
        }

        // Keep under the IPC request timeout (30s) with headroom.
        var timeoutMs = Math.Max(0, Math.Min(request.TimeoutMs, 25000));
        var pollMs = Math.Max(50, request.PollIntervalMs);

        // "enabled" reuses the property-filter machinery; all appear-conditions want visibility.
        var criteria = new FindCriteria
        {
            TypeName = request.TypeName,
            ElementName = request.ElementName,
            Text = request.Text,
            VisibleOnly = condition != "exists",
            PropertyFilter = condition == "enabled"
                ? new Dictionary<string, string> { ["IsEnabled"] = "True" }
                : null
        };

        var start = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hit = Application.Current.Dispatcher.Invoke(
                () => _treeWalker.FindFirstMatch(criteria, request.RootHandle == null ? null :
                    _treeWalker.ResolveHandle(request.RootHandle) ?? throw new InvalidOperationException(StaleHandleError(request.RootHandle))),
                System.Windows.Threading.DispatcherPriority.Normal,
                cancellationToken,
                TimeSpan.FromSeconds(10));

            var satisfied = condition == "hidden" ? hit == null : hit != null;
            var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;

            if (satisfied)
            {
                return new WaitForElementResponse
                {
                    RequestId = request.RequestId,
                    Matched = true,
                    MatchedHandle = hit?.Handle,
                    ElementType = hit?.TypeName,
                    WaitedMs = elapsed
                };
            }

            if (elapsed >= timeoutMs)
            {
                return new WaitForElementResponse
                {
                    RequestId = request.RequestId,
                    Matched = false,
                    WaitedMs = elapsed
                };
            }

            await Task.Delay(pollMs, cancellationToken);
        }
    }

    private IpcResponse HandleGetLayoutInfo(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetLayoutInfoRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new GetLayoutInfoResponse { Success = false, Error = "ElementHandle required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new GetLayoutInfoResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        var layoutJson = _propertyReader.GetLayoutInfo(element);
        return new GetLayoutInfoResponse
        {
            RequestId = request.RequestId,
            LayoutJson = layoutJson
        };
    }

    private IpcResponse HandleWatchProperty(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<WatchPropertyRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new WatchPropertyResponse { Success = false, Error = "ElementHandle required" };
        }
        if (string.IsNullOrEmpty(request.PropertyName))
        {
            return new WatchPropertyResponse { Success = false, Error = "PropertyName required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new WatchPropertyResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        try
        {
            var (watchId, initialValue) = _propertyWatcher.Watch(element, request.PropertyName);
            return new WatchPropertyResponse
            {
                RequestId = request.RequestId,
                WatchId = watchId,
                InitialValue = initialValue
            };
        }
        catch (ArgumentException ex)
        {
            return new WatchPropertyResponse { Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleExportTree(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ExportTreeRequest>(data);

        DependencyObject? root = null;
        if (!string.IsNullOrEmpty(request?.ElementHandle))
        {
            root = _treeWalker.ResolveHandle(request.ElementHandle);
        }
        root ??= GetDefaultRoot();

        if (root == null)
        {
            return new ExportTreeResponse { Success = false, Error = "No root element found" };
        }

        var format = request?.Format ?? "json";
        string content;
        int count;

        if (format == "xaml")
        {
            content = _treeWalker.ExportToXaml(root);
            count = CountElements(content);
        }
        else
        {
            content = _treeWalker.WalkVisualTree(root, 100);
            count = CountElements(content);
        }

        return new ExportTreeResponse
        {
            RequestId = request?.RequestId ?? "",
            Content = content,
            ElementCount = count
        };
    }

    private IpcResponse HandleCaptureScreenshot(JsonElement data, CancellationToken captureToken)
    {
        var request = IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(data);

        UIElement? element = null;
        if (!string.IsNullOrEmpty(request?.ElementHandle))
        {
            var resolved = _treeWalker.ResolveHandle(request.ElementHandle);
            if (resolved == null)
            {
                return new CaptureScreenshotResponse
                {
                    Success = false,
                    Error = StaleHandleError(request.ElementHandle)
                };
            }
            element = resolved as UIElement;
            if (element == null)
            {
                return new CaptureScreenshotResponse
                {
                    Success = false,
                    Error = WrongElementTypeError(request.ElementHandle, resolved, "UIElement")
                };
            }
        }
        else
        {
            element = GetDefaultRoot() as UIElement;
            if (element == null)
            {
                return new CaptureScreenshotResponse
                {
                    Success = false,
                    Error = "No root UIElement found"
                };
            }
        }

        try
        {
            var mode = request?.Mode?.ToLowerInvariant() ?? "render";
            if (mode != "render" && mode != "screen")
            {
                return new CaptureScreenshotResponse
                {
                    Success = false,
                    Error = $"Unknown screenshot mode '{request?.Mode}'. Expected 'render' or 'screen'."
                };
            }

            var screenshotCapture = new ScreenshotCapture();
            var maxWidth = request?.MaxWidth ?? 1920;
            var maxHeight = request?.MaxHeight ?? 1080;

            if (request?.FullContent == true && mode == "screen")
                throw new ArgumentException("Full-content capture is supported by render mode only.");
            var (base64, width, height) = request?.FullContent == true
                ? screenshotCapture.CaptureFullContent(element, maxWidth, maxHeight, captureToken)
                : mode == "screen"
                    ? screenshotCapture.CaptureScreen(element, maxWidth, maxHeight)
                    : screenshotCapture.CaptureElement(element, maxWidth, maxHeight);

            return new CaptureScreenshotResponse
            {
                RequestId = request?.RequestId ?? "",
                ImageBase64 = base64,
                Width = width,
                Height = height,
                ElementType = element.GetType().Name
            };
        }
        catch (Exception ex)
        {
            DebugLog($"Screenshot capture failed: {ex.Message}");
            return new CaptureScreenshotResponse
            {
                Success = false,
                Error = $"Screenshot capture failed: {ex.Message}"
            };
        }
    }

    private IpcResponse HandleGetDataContext(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetDataContextRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new GetDataContextResponse { Success = false, Error = "ElementHandle required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (element == null)
        {
            return new GetDataContextResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }

        var dcJson = _bindingAnalyzer.GetDataContext(element);
        return new GetDataContextResponse
        {
            RequestId = request.RequestId,
            DataContextJson = dcJson
        };
    }

    private IpcResponse HandleClearBindingErrors()
    {
        _bindingAnalyzer.ClearBindingErrors();
        return new ClearBindingErrorsResponse();
    }

    private IpcResponse HandleClickElement(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ClickElementRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new ClickElementResponse { Success = false, Error = "ElementHandle required" };
        }

        var resolved = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (resolved == null)
        {
            return new ClickElementResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }
        if (resolved is not UIElement element)
        {
            return new ClickElementResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle!, resolved, "UIElement") };
        }

        try
        {
            var outcome = _interactor.Click(element, request.Physical, request.ClickType);
            DebugLog($"ClickElement: {element.GetType().Name} clicked via {outcome.Method}");
            return new ClickElementResponse
            {
                RequestId = request.RequestId,
                Method = outcome.Method,
                ElementType = element.GetType().Name,
                Detail = outcome.Detail
            };
        }
        catch (Exception ex)
        {
            DebugLog($"ClickElement failed: {ex.Message}");
            return new ClickElementResponse { Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleSelectItem(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<SelectItemRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new SelectItemResponse { Success = false, Error = "ElementHandle required" };
        }

        var resolved = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (resolved == null)
        {
            return new SelectItemResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }
        if (resolved is not UIElement element)
        {
            return new SelectItemResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle!, resolved, "UIElement") };
        }

        try
        {
            var outcome = _interactor.SelectItem(element, request.ItemText, request.Index);
            DebugLog($"SelectItem: {element.GetType().Name} via {outcome.Method} ({outcome.Detail})");
            return new SelectItemResponse
            {
                RequestId = request.RequestId,
                Method = outcome.Method,
                ElementType = element.GetType().Name,
                Detail = outcome.Detail
            };
        }
        catch (Exception ex)
        {
            DebugLog($"SelectItem failed: {ex.Message}");
            return new SelectItemResponse { Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleSetText(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<SetTextRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new SetTextResponse { Success = false, Error = "ElementHandle required" };
        }

        var resolved = _treeWalker.ResolveHandle(request.ElementHandle!);
        if (resolved == null)
        {
            return new SetTextResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
        }
        if (resolved is not UIElement element)
        {
            return new SetTextResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle!, resolved, "UIElement") };
        }

        try
        {
            var outcome = _interactor.SetText(element, request.Text ?? string.Empty, request.Physical);
            DebugLog($"SetText: {element.GetType().Name} via {outcome.Method}");
            return new SetTextResponse
            {
                RequestId = request.RequestId,
                Method = outcome.Method,
                ElementType = element.GetType().Name,
                Detail = outcome.Detail
            };
        }
        catch (Exception ex)
        {
            DebugLog($"SetText failed: {ex.Message}");
            return new SetTextResponse { Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleSendKeys(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<SendKeysRequest>(data);
        if (request == null || string.IsNullOrWhiteSpace(request.Keys))
        {
            return new SendKeysResponse { Success = false, Error = "Keys required (e.g. \"Ctrl+S\")" };
        }

        UIElement? element = null;
        if (!string.IsNullOrEmpty(request.ElementHandle))
        {
            var resolved = _treeWalker.ResolveHandle(request.ElementHandle!);
            if (resolved == null)
            {
                return new SendKeysResponse { Success = false, Error = StaleHandleError(request.ElementHandle!) };
            }
            element = resolved as UIElement;
            if (element == null)
            {
                return new SendKeysResponse { Success = false, Error = WrongElementTypeError(request.ElementHandle!, resolved, "UIElement") };
            }
        }

        try
        {
            var outcome = _interactor.SendKeys(element, request.Keys!);
            DebugLog($"SendKeys: '{request.Keys}' to {element?.GetType().Name ?? "(focused)"}");
            return new SendKeysResponse
            {
                RequestId = request.RequestId,
                Method = outcome.Method,
                ElementType = element?.GetType().Name,
                Detail = outcome.Detail
            };
        }
        catch (Exception ex)
        {
            DebugLog($"SendKeys failed: {ex.Message}");
            return new SendKeysResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Gets the default root element for tree operations.
    /// Falls back to the first open window if MainWindow is null (common in multi-window apps).
    /// </summary>
    private static DependencyObject? GetDefaultRoot()
    {
        var app = Application.Current;
        if (app == null) return null;

        if (app.MainWindow != null)
            return app.MainWindow;

        // Fallback: find the first visible window
        foreach (Window window in app.Windows)
        {
            if (window.IsVisible)
                return window;
        }

        // Last resort: any window
        if (app.Windows.Count > 0)
            return app.Windows[0];

        return null;
    }

    private static int CountElements(string json)
    {
        // Simple count of element handles
        int count = 0;
        int index = 0;
        while ((index = json.IndexOf("\"handle\"", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index++;
        }
        return count;
    }

    private static int CountJsonArrayItems(string json)
    {
        // Count items in a JSON array
        if (string.IsNullOrEmpty(json) || json == "[]") return 0;
        int count = 1;
        bool inString = false;
        int depth = 0;
        foreach (char c in json)
        {
            if (c == '"' && depth > 0) inString = !inString;
            if (!inString)
            {
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 1) count++;
            }
        }
        return count;
    }

    private static int ParseJsonCount(string json)
    {
        // Parse count from {"elements":[...], "count":N} format
        if (string.IsNullOrEmpty(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("count", out var countProp))
            {
                return countProp.GetInt32();
            }
        }
        catch
        {
            // Fall back to array counting if parse fails
        }
        return CountJsonArrayItems(json);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _inputShutdown.Cancel();
        Stop();
        _propertyWatcher.Dispose();
        _ipcServer.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
