using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfVisualTreeMcp.Server.Services;

namespace WpfVisualTreeMcp.Server;

/// <summary>
/// WPF Visual Tree inspection tools for MCP
/// </summary>
[McpServerToolType]
public class WpfTools
{
    private readonly IProcessManager _processManager;
    private readonly IIpcBridge _ipcBridge;

    public WpfTools(IProcessManager processManager, IIpcBridge ipcBridge)
    {
        _processManager = processManager;
        _ipcBridge = ipcBridge;
    }

    [McpServerTool]
    [Description("List all running WPF applications available for inspection")]
    public async Task<object> WpfListProcesses()
    {
        var processes = await _processManager.GetWpfProcessesAsync();
        return new
        {
            processes = processes.Select(p => new
            {
                process_id = p.ProcessId,
                process_name = p.ProcessName,
                main_window_title = p.MainWindowTitle,
                is_attached = p.IsAttached,
                dotnet_version = p.DotNetVersion,
                runtime_type = p.RuntimeType
            })
        };
    }

    [McpServerTool]
    [Description("Attach to a WPF application by process ID or name. Set auto_inject=true to automatically inject the Inspector into processes that don't have it pre-loaded.")]
    public async Task<object> WpfAttach(int? process_id = null, string? process_name = null, bool auto_inject = false)
    {
        if (process_id == null && string.IsNullOrEmpty(process_name))
        {
            throw new ArgumentException("Either process_id or process_name must be provided");
        }

        var session = await _processManager.AttachToProcessAsync(process_id, process_name, auto_inject);
        return new
        {
            success = true,
            process_id = session.ProcessId,
            session_id = session.SessionId,
            main_window_handle = session.MainWindowHandle,
            inspector_status = session.InspectorStatus
        };
    }

    [McpServerTool]
    [Description("Get the visual tree hierarchy. Use root_handle to start from a specific element (from wpf_find_elements). Use max_depth to control depth (1-100, default 25). For deep UIs like AvalonDock, increase max_depth or use root_handle to zoom into a subtree.")]
    public async Task<object> WpfGetVisualTree(string? root_handle = null, int max_depth = 25)
    {
        if (max_depth < 1) max_depth = 1;
        if (max_depth > 100) max_depth = 100;

        var result = await _ipcBridge.GetVisualTreeAsync(root_handle, max_depth);
        return result;
    }

    [McpServerTool]
    [Description("Get all dependency properties of a UI element")]
    public async Task<object> WpfGetElementProperties(string element_handle)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.GetElementPropertiesAsync(element_handle);
        return result;
    }

    [McpServerTool]
    [Description("Search for elements by type or name. Returns up to max_results (default: 50). Supports partial type matching (e.g. 'Button' matches 'System.Windows.Controls.Button'). Use root_handle to search from a specific element.")]
    public async Task<object> WpfFindElements(
        string? root_handle = null,
        string? type_name = null,
        string? element_name = null,
        JsonElement? property_filter = null,
        int max_results = 50)
    {
        Dictionary<string, string>? filterDict = null;
        if (property_filter.HasValue && property_filter.Value.ValueKind == JsonValueKind.Object)
        {
            filterDict = new Dictionary<string, string>();
            foreach (var prop in property_filter.Value.EnumerateObject())
            {
                filterDict[prop.Name] = prop.Value.ToString();
            }
        }

        var result = await _ipcBridge.FindElementsAsync(root_handle, type_name, element_name, filterDict, max_results);
        return result;
    }

    [McpServerTool]
    [Description("Deep search for ALL elements matching criteria. Requires at least type_name or element_name to avoid returning the entire tree. Use root_handle to limit scope. Supports partial type matching (e.g. 'PdfViewer' matches 'Syncfusion.Windows.PdfViewer.PdfViewerControl').")]
    public async Task<object> WpfFindElementsDeep(
        string? root_handle = null,
        string? type_name = null,
        string? element_name = null)
    {
        if (string.IsNullOrEmpty(type_name) && string.IsNullOrEmpty(element_name))
        {
            throw new ArgumentException("At least type_name or element_name is required. Use wpf_get_visual_tree to browse the full tree instead.");
        }

        var result = await _ipcBridge.FindElementsDeepAsync(root_handle, type_name, element_name);
        return result;
    }

    [McpServerTool]
    [Description("Get all data bindings for an element with their status")]
    public async Task<object> WpfGetBindings(string element_handle)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.GetBindingsAsync(element_handle);
        return result;
    }

    [McpServerTool]
    [Description("List all binding errors in the application")]
    public async Task<object> WpfGetBindingErrors()
    {
        var result = await _ipcBridge.GetBindingErrorsAsync();
        return result;
    }

    [McpServerTool]
    [Description("Enumerate resource dictionaries and their contents")]
    public async Task<object> WpfGetResources(string scope = "application", string? element_handle = null)
    {
        if (scope == "element" && string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required when scope is 'element'");
        }

        var result = await _ipcBridge.GetResourcesAsync(scope, element_handle);
        return result;
    }

    [McpServerTool]
    [Description("Get applied styles and templates for an element")]
    public async Task<object> WpfGetStyles(string element_handle)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.GetStylesAsync(element_handle);
        return result;
    }

    [McpServerTool]
    [Description("Monitor a property for changes")]
    public async Task<object> WpfWatchProperty(string element_handle, string property_name)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }
        if (string.IsNullOrEmpty(property_name))
        {
            throw new ArgumentException("property_name is required");
        }

        var result = await _ipcBridge.WatchPropertyAsync(element_handle, property_name);
        return result;
    }

    [McpServerTool]
    [Description("Visually highlight an element in the running application")]
    public async Task<object> WpfHighlightElement(string element_handle, int duration_ms = 2000)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        await _ipcBridge.HighlightElementAsync(element_handle, duration_ms);
        return new { success = true, message = "Element highlighted successfully" };
    }

    [McpServerTool]
    [Description("Get layout information (ActualWidth, ActualHeight, Margin, etc.)")]
    public async Task<object> WpfGetLayoutInfo(string element_handle)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.GetLayoutInfoAsync(element_handle);
        return result;
    }

    [McpServerTool]
    [Description("Export visual tree to XAML or JSON")]
    public async Task<object> WpfExportTree(string? element_handle = null, string format = "json")
    {
        if (format != "json" && format != "xaml")
        {
            throw new ArgumentException("format must be 'json' or 'xaml'");
        }

        var result = await _ipcBridge.ExportTreeAsync(element_handle, format);
        return result;
    }

    [McpServerTool]
    [Description("Capture a screenshot of the WPF window or a specific element. Returns an image that can be visually analyzed. Use element_handle to capture a specific element, or omit for the entire window.")]
    public async Task<CallToolResult> WpfCaptureScreenshot(
        string? element_handle = null,
        int max_width = 1920,
        int max_height = 1080)
    {
        if (max_width < 1) max_width = 1;
        if (max_width > 3840) max_width = 3840;
        if (max_height < 1) max_height = 1;
        if (max_height > 2160) max_height = 2160;

        var result = await _ipcBridge.CaptureScreenshotAsync(element_handle, max_width, max_height);

        var content = new List<ContentBlock>
        {
            new ImageContentBlock
            {
                Data = result.ImageBase64,
                MimeType = result.MimeType
            },
            new TextContentBlock
            {
                Text = $"Screenshot captured: {result.Width}x{result.Height}px, element type: {result.ElementType ?? "Window"}"
            }
        };

        return new CallToolResult { Content = content };
    }
}
