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
    [Description("Query elements across all open windows. Filters combine with AND: type_name (partial match, e.g. 'Button' matches 'System.Windows.Controls.Button'), element_name (x:Name substring), text (visible text content — matches a Button's caption, TextBlock text, Window title, AutomationProperties.Name, ToolTip; case-insensitive substring), property_filter (object of property name → expected value substring, e.g. {\"IsEnabled\": \"True\"}), visible_only (exclude collapsed/hidden elements — recommended when looking for something the user can see). PREFER text over dumping the tree: e.g. text='Save' + type_name='Button' finds the Save button directly. Results include text, automationId, isVisible, isEnabled and screenBounds (device pixels) so you can pick the right element without extra calls. Returns up to max_results (default 50).")]
    public async Task<object> WpfFindElements(
        string? root_handle = null,
        string? type_name = null,
        string? element_name = null,
        string? text = null,
        JsonElement? property_filter = null,
        bool visible_only = false,
        int max_results = 50)
    {
        var result = await _ipcBridge.FindElementsAsync(
            root_handle, type_name, element_name, text,
            ToFilterDictionary(property_filter), visible_only, max_results);
        return result;
    }

    [McpServerTool]
    [Description("Deep search for ALL elements matching criteria (no result limit). Requires at least type_name, element_name or text to avoid returning the entire tree. Same filters as wpf_find_elements: type_name (partial match), element_name (x:Name substring), text (visible text content), property_filter, visible_only. Use root_handle to limit scope.")]
    public async Task<object> WpfFindElementsDeep(
        string? root_handle = null,
        string? type_name = null,
        string? element_name = null,
        string? text = null,
        JsonElement? property_filter = null,
        bool visible_only = false)
    {
        if (string.IsNullOrEmpty(type_name) && string.IsNullOrEmpty(element_name) && string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("At least type_name, element_name or text is required. Use wpf_get_visual_tree to browse the full tree instead.");
        }

        var result = await _ipcBridge.FindElementsDeepAsync(
            root_handle, type_name, element_name, text,
            ToFilterDictionary(property_filter), visible_only);
        return result;
    }

    private static Dictionary<string, string>? ToFilterDictionary(JsonElement? propertyFilter)
    {
        if (!propertyFilter.HasValue || propertyFilter.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var filterDict = new Dictionary<string, string>();
        foreach (var prop in propertyFilter.Value.EnumerateObject())
        {
            filterDict[prop.Name] = prop.Value.ToString();
        }
        return filterDict;
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
    [Description("List all binding errors captured since application start. Errors are detected via WPF trace listener. Use wpf_clear_binding_errors to reset the list.")]
    public async Task<object> WpfGetBindingErrors()
    {
        var result = await _ipcBridge.GetBindingErrorsAsync();
        return result;
    }

    [McpServerTool]
    [Description("Clear the captured binding errors list. Useful before testing specific scenarios.")]
    public async Task<object> WpfClearBindingErrors()
    {
        await _ipcBridge.ClearBindingErrorsAsync();
        return new { success = true, message = "Binding errors cleared" };
    }

    [McpServerTool]
    [Description("Get the DataContext for an element, including type info, properties, INPC status, and inheritance chain up the visual tree. Essential for diagnosing binding path errors.")]
    public async Task<object> WpfGetDataContext(string element_handle)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.GetDataContextAsync(element_handle);
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
    [Description("Wait until an element matching the criteria satisfies a condition, polling in the target app so you don't have to sleep-and-retry. Identify the element with type_name, element_name and/or text (same matching as wpf_find_elements). condition: 'visible' (default — element exists and is on screen), 'exists' (in the tree even if not visible), 'enabled' (visible and IsEnabled=true), 'hidden' (no matching visible element — e.g. a spinner disappeared). Returns matched (bool), waited_ms, and matched_handle/element_type when found. Use after a click that triggers async work (dialog opens, spinner clears, button enables) before the next step. timeout_ms default 10000 (max 25000); poll_interval_ms default 250.")]
    public async Task<object> WpfWaitForElement(
        string? type_name = null,
        string? element_name = null,
        string? text = null,
        string condition = "visible",
        int timeout_ms = 10000,
        int poll_interval_ms = 250,
        string? root_handle = null)
    {
        if (string.IsNullOrEmpty(type_name) && string.IsNullOrEmpty(element_name) && string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("At least type_name, element_name or text is required to identify the element to wait for.");
        }

        var result = await _ipcBridge.WaitForElementAsync(
            root_handle, type_name, element_name, text, condition, timeout_ms, poll_interval_ms);
        return new
        {
            matched = result.Matched,
            waited_ms = result.WaitedMs,
            matched_handle = result.MatchedHandle,
            element_type = result.ElementType
        };
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
    [Description("Click a UI element. By default invokes the control's action via UI Automation (works for buttons, menu items, checkboxes, radio buttons, tabs, list items, expanders) without moving the mouse or focusing the window. Set physical=true to perform a real OS mouse click at the element's on-screen position (works on any visible element but moves the cursor and brings the window forward; auto-scrolls the element into view first). click_type: 'single' (default), 'double' (open items), 'right' (context menus) — double and right are always physical. After opening a context menu or dropdown, use wpf_capture_screenshot with mode='screen' to see it. STATE-CHANGING.")]
    public async Task<object> WpfClickElement(string element_handle, bool physical = false, string? click_type = null)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.ClickElementAsync(element_handle, physical, click_type);
        return new
        {
            success = true,
            method = result.Method,
            element_type = result.ElementType,
            detail = result.Detail
        };
    }

    [McpServerTool]
    [Description("Select an item in a ComboBox, ListBox, ListView or TabControl by visible text (item_text, case-insensitive substring) or zero-based index. PREFER this over clicking for dropdowns/lists: it works even when items are virtualized (not yet in the visual tree) and raises proper selection events. On failure the error lists the available items. STATE-CHANGING.")]
    public async Task<object> WpfSelectItem(string element_handle, string? item_text = null, int? index = null)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }
        if (string.IsNullOrEmpty(item_text) && index == null)
        {
            throw new ArgumentException("Provide item_text or index to choose the item to select");
        }

        var result = await _ipcBridge.SelectItemAsync(element_handle, item_text, index);
        return new
        {
            success = true,
            method = result.Method,
            element_type = result.ElementType,
            detail = result.Detail
        };
    }

    [McpServerTool]
    [Description("Set the text/value of a UI element (TextBox, ComboBox, etc.). By default uses UI Automation IValueProvider.SetValue (clean, no focus needed, raises proper events). Falls back to setting TextBox.Text or PasswordBox.Password directly, or a reflected string 'Text' property. Set physical=true to focus the element and type via OS keyboard input (clears existing text with Ctrl+A/Delete first, then types each character; moves window focus). STATE-CHANGING.")]
    public async Task<object> WpfSetText(string element_handle, string text, bool physical = false)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.SetTextAsync(element_handle, text ?? string.Empty, physical);
        return new
        {
            success = true,
            method = result.Method,
            element_type = result.ElementType,
            detail = result.Detail
        };
    }

    [McpServerTool]
    [Description("Send a keyboard shortcut / key combination to a WPF element (or to whatever currently has keyboard focus, when element_handle is omitted). Examples: 'Ctrl+S', 'Ctrl+Shift+F', 'Enter', 'Escape', 'F5', 'Alt+F4', 'Win+R'. Modifiers: Ctrl, Shift, Alt, Win. Keys: A-Z, 0-9, F1-F12, Enter, Esc, Tab, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up, Down, Left, Right. Uses OS keyboard input - brings the target window to the foreground. STATE-CHANGING.")]
    public async Task<object> WpfSendKeys(string keys, string? element_handle = null)
    {
        if (string.IsNullOrWhiteSpace(keys))
        {
            throw new ArgumentException("keys is required (e.g. \"Ctrl+S\")");
        }

        var result = await _ipcBridge.SendKeysAsync(element_handle, keys);
        return new
        {
            success = true,
            method = result.Method,
            element_type = result.ElementType,
            detail = result.Detail
        };
    }

    [McpServerTool]
    [Description("Capture a screenshot of the WPF window or a specific element. Returns an image that can be visually analyzed. Use element_handle to capture a specific element, or omit for the entire window. mode='render' (default) re-renders the visual off-screen — works even if the window is covered, but CANNOT see open Popups, ComboBox dropdowns, context menus or tooltips. mode='screen' captures the actual on-screen pixels (GDI) and DOES include them — use it right after clicking something that opened a popup/menu; requires the window to be visible and unobstructed.")]
    public async Task<CallToolResult> WpfCaptureScreenshot(
        string? element_handle = null,
        int max_width = 1920,
        int max_height = 1080,
        string mode = "render")
    {
        if (max_width < 1) max_width = 1;
        if (max_width > 3840) max_width = 3840;
        if (max_height < 1) max_height = 1;
        if (max_height > 2160) max_height = 2160;
        if (mode != "render" && mode != "screen")
        {
            throw new ArgumentException("mode must be 'render' or 'screen'");
        }

        var result = await _ipcBridge.CaptureScreenshotAsync(element_handle, max_width, max_height, mode);

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
