using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Main entry point for the inspector when loaded into a WPF application.
/// </summary>
public class InspectorService : IDisposable
{
    private readonly IpcServer _ipcServer;
    private readonly TreeWalker _treeWalker;
    private readonly PropertyReader _propertyReader;
    private readonly BindingAnalyzer _bindingAnalyzer;
    private readonly ElementHighlighter _highlighter;
    private readonly PropertyWatcher _propertyWatcher;
    private readonly ResourceInspector _resourceInspector;
    private readonly ControlInteractor _interactor;
    private bool _isRunning;
    private bool _disposed;

    private static readonly object _initLock = new();
    public static InspectorService? Instance { get; private set; }

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
        _propertyReader = new PropertyReader();
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
        try
        {
            DebugLog($"HandleRequestAsync: requestType={requestType}");

            if (Application.Current == null)
            {
                DebugLog("ERROR: Application.Current is NULL!");
                return new GetVisualTreeResponse { Success = false, Error = "Application.Current is null" };
            }

            // Use Task.Run to avoid blocking the named pipe thread
            var result = await Task.Run(() =>
            {
                DebugLog($"Task.Run thread {System.Threading.Thread.CurrentThread.ManagedThreadId}, calling Dispatcher.Invoke()");

                // Use synchronous Invoke instead of InvokeAsync to avoid potential deadlocks
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    DebugLog($"Inside Dispatcher callback, UI thread {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                    return HandleRequest(requestType, data);
                }, System.Windows.Threading.DispatcherPriority.Normal, System.Threading.CancellationToken.None, TimeSpan.FromSeconds(10));
            });

            DebugLog($"HandleRequest completed successfully");
            return result;
        }
        catch (TimeoutException)
        {
            DebugLog($"TIMEOUT in HandleRequestAsync: Dispatcher is busy or blocked");
            return new GetVisualTreeResponse { Success = false, Error = "Request timeout: UI thread is busy" };
        }
        catch (Exception ex)
        {
            DebugLog($"ERROR in HandleRequestAsync: {ex.Message}\n{ex.StackTrace}");
            return new GetVisualTreeResponse { Success = false, Error = ex.Message };
        }
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

    private IpcResponse HandleRequest(string requestType, JsonElement data)
    {
        return requestType switch
        {
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
            "CaptureScreenshot" => HandleCaptureScreenshot(data),
            "GetDataContext" => HandleGetDataContext(data),
            "ClearBindingErrors" => HandleClearBindingErrors(),
            "ClickElement" => HandleClickElement(data),
            _ => new GetVisualTreeResponse { Success = false, Error = $"Unknown request: {requestType}" }
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
                    Error = $"Element handle '{request.RootHandle}' not found. The handle may have expired. " +
                            "Use wpf_find_elements to get fresh handles, then use root_handle with the new handle."
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
            return new GetElementPropertiesResponse { Success = false, Error = "Element not found" };
        }

        var propsJson = _propertyReader.GetProperties(element);
        return new GetElementPropertiesResponse
        {
            RequestId = request.RequestId,
            PropertiesJson = propsJson
        };
    }

    private IpcResponse HandleFindElements(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<FindElementsRequest>(data);

        var maxResults = request?.MaxResults ?? 50;

        // If a specific root handle is given, search from there
        if (!string.IsNullOrEmpty(request?.RootHandle))
        {
            var root = _treeWalker.ResolveHandle(request.RootHandle);
            if (root == null)
            {
                return new FindElementsResponse { Success = false, Error = "Root element not found" };
            }

            var elementsJson = _treeWalker.FindElements(root, request?.TypeName, request?.ElementName, maxResults);
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
            var json = _treeWalker.FindElements(root, request?.TypeName, request?.ElementName, maxResults - totalCount);
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

        // If a specific root handle is given, search from there
        if (!string.IsNullOrEmpty(request?.RootHandle))
        {
            var root = _treeWalker.ResolveHandle(request.RootHandle);
            if (root == null)
            {
                return new FindElementsDeepResponse { Success = false, Error = "Root element not found" };
            }

            var elementsJson = _treeWalker.FindElementsDeep(root, request?.TypeName, request?.ElementName);
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
            var json = _treeWalker.FindElementsDeep(root, request?.TypeName, request?.ElementName);
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
            return new GetBindingsResponse { Success = false, Error = "Element not found" };
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
            Count = CountJsonArrayItems(errorsJson)
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

        var element = _treeWalker.ResolveHandle(request.ElementHandle) as FrameworkElement;
        if (element == null)
        {
            return new GetStylesResponse { Success = false, Error = "Element not found or not FrameworkElement" };
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

        var element = _treeWalker.ResolveHandle(request.ElementHandle) as UIElement;
        if (element == null)
        {
            return new HighlightElementResponse { Success = false, Error = "Element not found or not UIElement" };
        }

        _highlighter.Highlight(element, request.DurationMs);
        return new HighlightElementResponse { RequestId = request.RequestId };
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
            return new GetLayoutInfoResponse { Success = false, Error = "Element not found" };
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
            return new WatchPropertyResponse { Success = false, Error = "Element not found" };
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

    private IpcResponse HandleCaptureScreenshot(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(data);

        UIElement? element = null;
        if (!string.IsNullOrEmpty(request?.ElementHandle))
        {
            element = _treeWalker.ResolveHandle(request.ElementHandle) as UIElement;
            if (element == null)
            {
                return new CaptureScreenshotResponse
                {
                    Success = false,
                    Error = "Element not found or is not a UIElement"
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
            var screenshotCapture = new ScreenshotCapture();
            var (base64, width, height) = screenshotCapture.CaptureElement(
                element,
                request?.MaxWidth ?? 1920,
                request?.MaxHeight ?? 1080);

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
            return new GetDataContextResponse { Success = false, Error = "Element not found" };
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

        var element = _treeWalker.ResolveHandle(request.ElementHandle!) as UIElement;
        if (element == null)
        {
            return new ClickElementResponse { Success = false, Error = "Element not found or is not a UIElement" };
        }

        try
        {
            var outcome = _interactor.Click(element, request.Physical);
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
        Stop();
        _propertyWatcher.Dispose();
        _ipcServer.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
