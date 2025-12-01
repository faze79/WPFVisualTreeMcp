using System.ComponentModel;
using System.Text.Json;
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
                dotnet_version = p.DotNetVersion
            })
        };
    }

    [McpServerTool]
    [Description("Attach to a WPF application by process ID or name")]
    public async Task<object> WpfAttach(int? process_id = null, string? process_name = null)
    {
        if (process_id == null && string.IsNullOrEmpty(process_name))
        {
            throw new ArgumentException("Either process_id or process_name must be provided");
        }

        var session = await _processManager.AttachToProcessAsync(process_id, process_name);
        return new
        {
            success = true,
            process_id = session.ProcessId,
            session_id = session.SessionId,
            main_window_handle = session.MainWindowHandle
        };
    }

    [McpServerTool]
    [Description("Get the visual tree hierarchy starting from a root element")]
    public async Task<object> WpfGetVisualTree(string? root_handle = null, int max_depth = 10)
    {
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
    [Description("Search for elements by type, name, or property value")]
    public async Task<object> WpfFindElements(
        string? type_name = null,
        string? element_name = null,
        JsonElement? property_filter = null)
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

        var result = await _ipcBridge.FindElementsAsync(type_name, element_name, filterDict);
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
}
