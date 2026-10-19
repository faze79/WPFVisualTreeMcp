using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;

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
            success = session.InspectorReady,
            process_id = session.ProcessId,
            session_id = session.SessionId,
            main_window_handle = session.MainWindowHandle,
            inspector_status = session.InspectorStatus,
            inspector_ready = session.InspectorReady
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
    [Description("Collect a bounded diagnostic report across open windows or a subtree: current binding/validation errors, disabled/invisible/zero-size counts and recent process-wide binding traces. State counts are observations, not defects. Includes coverage and truncation reasons; does not realize virtualized items or change the UI.")]
    public Task<DiagnosticReport> WpfReport(string? root_handle = null, int max_elements = 500, int max_depth = 25, int max_findings = 100)
    {
        if (max_elements is < 1 or > 2000 || max_depth is < 1 or > 100 || max_findings is < 1 or > 500)
            throw new ArgumentException("Report limits: max_elements 1-2000, max_depth 1-100, max_findings 1-500.");
        return _ipcBridge.ReportAsync(root_handle, max_elements, max_depth, max_findings);
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
    [Description("Explain an element's triggers and where a value comes from — for 'why doesn't this trigger fire?' and 'which style/setter set this value?'. Lists the element's Style triggers (walking BasedOn) AND its ControlTemplate triggers (where most visual-state triggers live, e.g. IsMouseOver/IsEnabled on a Button), each EVALUATED against the current state: the condition (property=expected), the property's CURRENT value, whether it matches, whether the trigger is active, and the setters it applies. This tells you a trigger isn't firing because its condition property currently holds a different value. Pass property_name to also attribute that property's effective value to its source: a specific style setter (which style, which setter) or an active trigger (which condition), or local/inherited/default.")]
    public async Task<object> WpfExplainTriggers(string element_handle, string? property_name = null)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }

        var result = await _ipcBridge.ExplainTriggersAsync(element_handle, property_name);
        return string.IsNullOrEmpty(result.Json)
            ? (object)new { success = false }
            : JsonSerializer.Deserialize<JsonElement>(result.Json!);
    }

    [McpServerTool]
    [Description("Explain WHY a property has its current value — the go-to tool for 'why is X empty/wrong/disabled?'. Reports the value source (Local, Style, Binding, Inherited, Default, ...), the effective value, and whether it's animated/coerced. When the value comes from a binding, it also resolves the binding path hop-by-hop against the source (DataContext / ElementName / RelativeSource / Source) and reports each segment's value and type, pinpointing exactly where a broken binding fails (e.g. a null intermediate, or a misspelled property that doesn't exist on the DataContext type). Complements wpf_get_bindings (which lists bindings) by actually evaluating one.")]
    public async Task<object> WpfEvaluateBinding(string element_handle, string property_name)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }
        if (string.IsNullOrEmpty(property_name))
        {
            throw new ArgumentException("property_name is required");
        }

        var result = await _ipcBridge.EvaluateBindingAsync(element_handle, property_name);
        return string.IsNullOrEmpty(result.Json)
            ? (object)new { success = false }
            : JsonSerializer.Deserialize<JsonElement>(result.Json!);
    }

    [McpServerTool]
    [Description("Capture a snapshot of an element's subtree (layout metrics, visibility, alignment, brushes, text) and store it under a label, to diff later with wpf_diff. Workflow: wpf_snapshot(label='before') → make a change (e.g. wpf_set_property) → wpf_snapshot(label='after') → wpf_diff('before','after') to see exactly what moved. element_handle sets the subtree root (omit for the whole main window); max_depth default 25. Returns the label and how many elements were captured.")]
    public async Task<object> WpfSnapshot(string? element_handle = null, string? label = null, int max_depth = 25)
    {
        var result = await _ipcBridge.SnapshotAsync(element_handle, label, max_depth);
        return new { success = true, label = result.Label, element_count = result.ElementCount };
    }

    [McpServerTool]
    [Description("Diff two snapshots captured with wpf_snapshot, to measure the effect of a change. Returns a summary (counts) plus, for each element that changed, the exact properties that changed (from → to), keyed by stable element handle; plus elements added or removed between the two snapshots. Use this to verify a planned UI tweak did what you expected (e.g. Margin 0,0,0,0 → 40,40,40,40; Visibility Collapsed → Visible; a child appeared).")]
    public async Task<object> WpfDiff(string before, string after)
    {
        if (string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after))
        {
            throw new ArgumentException("Both 'before' and 'after' snapshot labels are required.");
        }

        var result = await _ipcBridge.DiffAsync(before, after);
        object diff = string.IsNullOrEmpty(result.Json)
            ? new { }
            : JsonSerializer.Deserialize<JsonElement>(result.Json!);
        return new
        {
            success = true,
            changed_count = result.ChangedCount,
            added_count = result.AddedCount,
            removed_count = result.RemovedCount,
            diff
        };
    }

    [McpServerTool]
    [Description("Live-edit a dependency property on an element at runtime, to test whether a planned UI change is effective without rebuilding. The value is a string converted to the property's type: e.g. property_name='Margin' value='20,0,20,0', property_name='Visibility' value='Collapsed', property_name='Background' value='Red' (or '#FF0000'), property_name='Width' value='300', value='{null}' for null. Returns the coerced value read back and what previously held the property (Binding/Local/Unset). Setting a data-bound property replaces the binding with a local value; wpf_revert_property restores it. Pair with wpf_capture_screenshot to see the effect, then revert. STATE-CHANGING (reversible via wpf_revert_property).")]
    public async Task<object> WpfSetProperty(string element_handle, string property_name, string value)
    {
        if (string.IsNullOrEmpty(element_handle))
        {
            throw new ArgumentException("element_handle is required");
        }
        if (string.IsNullOrEmpty(property_name))
        {
            throw new ArgumentException("property_name is required");
        }

        var result = await _ipcBridge.SetPropertyAsync(element_handle, property_name, value ?? string.Empty);
        return new
        {
            success = true,
            element_type = result.ElementType,
            applied_value = result.AppliedValue,
            value_type = result.ValueType,
            previous_source = result.PreviousSource
        };
    }

    [McpServerTool]
    [Description("Undo live property edits made with wpf_set_property. By default reverts the most recent edit; pass element_handle and/or property_name to target a specific one, or all=true to undo every pending edit. Restores whatever held the property before (a binding, a local value, or nothing — falling back to style/inherited/default). Returns how many were reverted and how many edits remain pending.")]
    public async Task<object> WpfRevertProperty(string? element_handle = null, string? property_name = null, bool all = false)
    {
        var result = await _ipcBridge.RevertPropertyAsync(all, element_handle, property_name);
        return new
        {
            success = true,
            reverted_count = result.RevertedCount,
            reverted_handle = result.RevertedHandle,
            reverted_property = result.RevertedProperty,
            pending_count = result.PendingCount
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
    [Description("Drag using the real OS mouse: press, move gradually, release. Supply element_handle and either target_handle OR delta_x/delta_y (omitted delta axis is zero). start_x/start_y and target_x/target_y are optional coordinate pairs measured from the corresponding element's top-left in WPF DIPs; default is its center. Deltas are in the source element's local DIPs, including its transforms. button: left/right/middle. duration_ms: 100-10000, default 500. Brings the source window forward and moves the cursor. Both endpoints must be visible and unobstructed; no scrolling during the drag. Success reports input delivery, not drop acceptance: verify properties or a screenshot afterwards. STATE-CHANGING.")]
    public async Task<object> WpfDrag(
        string element_handle, string? target_handle = null,
        double? delta_x = null, double? delta_y = null,
        double? start_x = null, double? start_y = null,
        double? target_x = null, double? target_y = null,
        string button = "left", int duration_ms = 500)
    {
        var options = new DragOptions
        {
            ElementHandle = element_handle, TargetHandle = target_handle,
            DeltaX = delta_x, DeltaY = delta_y, StartX = start_x, StartY = start_y,
            TargetX = target_x, TargetY = target_y, Button = button, DurationMs = duration_ms
        };
        try { options.Validate(); }
        catch (ArgumentException ex) { throw new McpException(ex.Message); }
        var result = await _ipcBridge.DragAsync(options);
        return new
        {
            success = true, method = result.Method, element_type = result.ElementType,
            button = result.Button, duration_ms = result.DurationMs,
            start = new { x = result.StartX, y = result.StartY },
            end = new { x = result.EndX, y = result.EndY },
            coordinate_space = "screen_pixels", detail = result.Detail
        };
    }

    [McpServerTool]
    [Description("Inject native Windows touch input into a visible WPF element. gesture: tap, long_press, swipe, pinch, rotate. x/y is an optional local WPF DIP pair (default center). swipe requires delta_x/delta_y (omitted axis zero). pinch requires scale (0.1-10, excluding 1; above 1 spreads two fingers). rotate requires angle_degrees (-360 to 360, nonzero; positive clockwise). radius optionally sets initial finger distance from center in local DIPs for pinch/rotate (minimum 4); default fits the element. The complete path must stay visible inside the element. duration_ms defaults: tap 100, long_press 1000, others 700; limits: tap 50-500, long_press 500-10000, others 100-10000. Requires an interactive Windows 8+ desktop. Activates the window. Success means touch input sent, not gesture recognition: the application must support touch/manipulations. STATE-CHANGING.")]
    public async Task<object> WpfTouchGesture(string element_handle, string gesture,
        double? x = null, double? y = null, double? delta_x = null, double? delta_y = null,
        double? scale = null, double? angle_degrees = null, double? radius = null, int? duration_ms = null)
    {
        var options = new TouchGestureOptions
        {
            ElementHandle = element_handle, Gesture = gesture, X = x, Y = y, DeltaX = delta_x, DeltaY = delta_y,
            Scale = scale, AngleDegrees = angle_degrees, Radius = radius, DurationMs = duration_ms
        };
        try { options.Validate(); }
        catch (ArgumentException ex) { throw new McpException(ex.Message); }
        var result = await _ipcBridge.TouchGestureAsync(options);
        return new
        {
            success = true, method = result.Method, gesture = result.Gesture, element_type = result.ElementType,
            contact_count = result.ContactCount, duration_ms = result.DurationMs, start = result.Start, end = result.End,
            coordinate_space = "screen_pixels", detail = result.Detail
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
    [Description("Capture a screenshot of the WPF window or a specific element. Returns an image that can be visually analyzed. Use element_handle to capture a specific element, or omit for the entire window. mode='render' (default) re-renders the visual off-screen — works even if the window is covered, but CANNOT see open Popups, ComboBox dropdowns, context menus or tooltips. mode='screen' captures the actual on-screen pixels (GDI) and DOES include them — use it right after clicking something that opened a popup/menu; requires the window to be visible and unobstructed. Set full_content=true with render mode to capture all content in a ScrollViewer, including unscrolled and virtualized content; the original scroll position is restored.")]
    public async Task<CallToolResult> WpfCaptureScreenshot(
        string? element_handle = null,
        int max_width = 1920,
        int max_height = 1080,
        string mode = "render",
        bool full_content = false)
    {
        if (max_width < 1) max_width = 1;
        if (max_width > (full_content ? 16384 : 3840)) max_width = full_content ? 16384 : 3840;
        if (max_height < 1) max_height = 1;
        if (max_height > (full_content ? 16384 : 2160)) max_height = full_content ? 16384 : 2160;
        if (mode != "render" && mode != "screen")
        {
            throw new ArgumentException("mode must be 'render' or 'screen'");
        }
        if (full_content && mode == "screen")
        {
            throw new ArgumentException("full_content is supported with mode='render' only");
        }

        var result = await _ipcBridge.CaptureScreenshotAsync(element_handle, max_width, max_height, mode, full_content);

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
