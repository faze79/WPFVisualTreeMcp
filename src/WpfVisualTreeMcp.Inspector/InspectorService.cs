using System;
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
    private bool _isRunning;
    private bool _disposed;

    public static InspectorService? Instance { get; private set; }

    public static void Initialize(int processId)
    {
        if (Instance != null) return;

        Instance = new InspectorService(processId);
        Instance.Start();
    }

    private InspectorService(int processId)
    {
        _treeWalker = new TreeWalker();
        _propertyReader = new PropertyReader();
        _bindingAnalyzer = new BindingAnalyzer();
        _highlighter = new ElementHighlighter();
        _propertyWatcher = new PropertyWatcher();
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
            return await Application.Current.Dispatcher.InvokeAsync(() =>
                HandleRequest(requestType, data));
        }
        catch (Exception ex)
        {
            return new GetVisualTreeResponse { Success = false, Error = ex.Message };
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

        var elementsJson = _treeWalker.FindElements(root, request?.TypeName, request?.ElementName);
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
        // TODO: Implement resource enumeration
        return new GetResourcesResponse { ResourcesJson = "[]" };
    }

    private IpcResponse HandleGetStyles(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<GetStylesRequest>(data);
        // TODO: Implement style inspection
        return new GetStylesResponse { StylesJson = "{}" };
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
