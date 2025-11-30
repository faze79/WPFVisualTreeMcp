using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Main entry point for the inspector when loaded into a WPF application.
/// Manages the IPC server and coordinates inspection operations.
/// </summary>
public class InspectorService : IDisposable
{
    private readonly IpcServer _ipcServer;
    private readonly TreeWalker _treeWalker;
    private readonly PropertyReader _propertyReader;
    private readonly BindingAnalyzer _bindingAnalyzer;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Gets the singleton instance of the inspector service.
    /// </summary>
    public static InspectorService? Instance { get; private set; }

    /// <summary>
    /// Initializes the inspector service. Called when the DLL is loaded.
    /// </summary>
    /// <param name="processId">The process ID of the host application.</param>
    public static void Initialize(int processId)
    {
        if (Instance != null)
        {
            return; // Already initialized
        }

        Instance = new InspectorService(processId);
        Instance.Start();
    }

    private InspectorService(int processId)
    {
        _treeWalker = new TreeWalker();
        _propertyReader = new PropertyReader();
        _bindingAnalyzer = new BindingAnalyzer();
        _ipcServer = new IpcServer(processId, HandleRequest);
    }

    /// <summary>
    /// Starts the inspector service.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        _isRunning = true;
        _ipcServer.Start();
    }

    /// <summary>
    /// Stops the inspector service.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _ipcServer.Stop();
    }

    private async Task<string> HandleRequest(string requestType, string? parameters)
    {
        try
        {
            // All WPF operations must be done on the UI thread
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return requestType switch
                {
                    "GetVisualTree" => HandleGetVisualTree(parameters),
                    "GetElementProperties" => HandleGetElementProperties(parameters),
                    "FindElements" => HandleFindElements(parameters),
                    "GetBindings" => HandleGetBindings(parameters),
                    "GetBindingErrors" => HandleGetBindingErrors(parameters),
                    "HighlightElement" => HandleHighlightElement(parameters),
                    "GetLayoutInfo" => HandleGetLayoutInfo(parameters),
                    _ => $"{{\"error\": \"Unknown request type: {requestType}\"}}"
                };
            });
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{EscapeJson(ex.Message)}\"}}";
        }
    }

    private string HandleGetVisualTree(string? parameters)
    {
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow == null)
        {
            return "{\"error\": \"No main window found\"}";
        }

        var tree = _treeWalker.WalkVisualTree(mainWindow, maxDepth: 10);
        return tree;
    }

    private string HandleGetElementProperties(string? parameters)
    {
        // TODO: Parse element handle from parameters and get properties
        return "{\"properties\": []}";
    }

    private string HandleFindElements(string? parameters)
    {
        // TODO: Parse search criteria and find elements
        return "{\"elements\": []}";
    }

    private string HandleGetBindings(string? parameters)
    {
        // TODO: Parse element handle and get bindings
        return "{\"bindings\": []}";
    }

    private string HandleGetBindingErrors(string? parameters)
    {
        var errors = _bindingAnalyzer.GetBindingErrors();
        return errors;
    }

    private string HandleHighlightElement(string? parameters)
    {
        // TODO: Parse element handle and highlight it
        return "{\"success\": true}";
    }

    private string HandleGetLayoutInfo(string? parameters)
    {
        // TODO: Parse element handle and get layout info
        return "{\"layout\": {}}";
    }

    private static string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _ipcServer.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
