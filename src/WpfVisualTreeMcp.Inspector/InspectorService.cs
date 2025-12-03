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
    private bool _isRunning;
    private bool _disposed;

    public static InspectorService? Instance { get; private set; }

    public static void Initialize(int processId)
    {
        if (Instance != null) return;

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
            DebugLog($"ERROR in Initialize: {ex.Message}\n{ex.StackTrace}");
            throw;
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
        catch (TimeoutException ex)
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
            "GetBindings" => HandleGetBindings(data),
            "GetBindingErrors" => HandleGetBindingErrors(),
            "GetResources" => HandleGetResources(data),
            "GetStyles" => HandleGetStyles(data),
            "HighlightElement" => HandleHighlightElement(data),
            "GetLayoutInfo" => HandleGetLayoutInfo(data),
            "WatchProperty" => HandleWatchProperty(data),
            "ExportTree" => HandleExportTree(data),
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
        }
        root ??= Application.Current.MainWindow;

        if (root == null)
        {
            return new GetVisualTreeResponse { Success = false, Error = "No root element found" };
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

        var element = _treeWalker.ResolveHandle(request.ElementHandle);
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
        var root = Application.Current.MainWindow;
        if (root == null)
        {
            return new FindElementsResponse { Success = false, Error = "No main window" };
        }

        var maxResults = request?.MaxResults ?? 50;
        var elementsJson = _treeWalker.FindElements(root, request?.TypeName, request?.ElementName, maxResults);
        return new FindElementsResponse
        {
            RequestId = request?.RequestId ?? "",
            ElementsJson = elementsJson,
            Count = CountJsonArrayItems(elementsJson)
        };
    }

    private IpcResponse HandleGetBindings(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetBindingsRequest>(data);
        if (string.IsNullOrEmpty(request?.ElementHandle))
        {
            return new GetBindingsResponse { Success = false, Error = "ElementHandle required" };
        }

        var element = _treeWalker.ResolveHandle(request.ElementHandle);
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

        var element = _treeWalker.ResolveHandle(request.ElementHandle);
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

        var element = _treeWalker.ResolveHandle(request.ElementHandle);
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
        var root = Application.Current.MainWindow;
        if (root == null)
        {
            return new ExportTreeResponse { Success = false, Error = "No main window" };
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
