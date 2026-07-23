using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Bridge for inter-process communication with the injected inspector DLL.
/// </summary>
public interface IIpcBridge
{
    /// <summary>
    /// Gets the visual tree from the attached process.
    /// </summary>
    Task<VisualTreeResult> GetVisualTreeAsync(string? rootHandle, int maxDepth);

    /// <summary>
    /// Gets properties of an element.
    /// </summary>
    Task<ElementPropertiesResult> GetElementPropertiesAsync(string elementHandle);

    /// <summary>
    /// Finds elements matching the specified criteria (type, name, visible text, property values, visibility).
    /// </summary>
    Task<FindElementsResult> FindElementsAsync(string? rootHandle, string? typeName, string? elementName,
        string? text, Dictionary<string, string>? propertyFilter, bool visibleOnly = false, int maxResults = 50);

    /// <summary>
    /// Finds ALL elements matching the specified criteria without limit (deep search).
    /// WARNING: This can return a large number of results. Use with caution.
    /// </summary>
    Task<FindElementsResult> FindElementsDeepAsync(string? rootHandle, string? typeName, string? elementName,
        string? text = null, Dictionary<string, string>? propertyFilter = null, bool visibleOnly = false);

    /// <summary>
    /// Gets bindings for an element.
    /// </summary>
    Task<BindingsResult> GetBindingsAsync(string elementHandle);

    /// <summary>
    /// Gets all binding errors in the application.
    /// </summary>
    Task<BindingErrorsResult> GetBindingErrorsAsync();

    /// <summary>
    /// Gets resources at the specified scope.
    /// </summary>
    Task<ResourcesResult> GetResourcesAsync(string scope, string? elementHandle);

    /// <summary>
    /// Gets styles for an element.
    /// </summary>
    Task<StylesResult> GetStylesAsync(string elementHandle);

    /// <summary>
    /// Starts watching a property for changes.
    /// </summary>
    Task<string> WatchPropertyAsync(string elementHandle, string propertyName);

    /// <summary>
    /// Highlights an element in the target application.
    /// </summary>
    Task HighlightElementAsync(string elementHandle, int durationMs);

    /// <summary>
    /// Gets layout information for an element.
    /// </summary>
    Task<LayoutInfoResult> GetLayoutInfoAsync(string elementHandle);

    /// <summary>
    /// Explains why a property has its current value: value source and, for bindings, the
    /// hop-by-hop resolution of the path against the source (where it breaks).
    /// </summary>
    Task<EvaluateBindingResult> EvaluateBindingAsync(string elementHandle, string propertyName);

    /// <summary>
    /// Captures a snapshot of an element subtree (or the main window), stored under a label
    /// for a later diff.
    /// </summary>
    Task<SnapshotResult> SnapshotAsync(string? elementHandle, string? label, int maxDepth);

    /// <summary>
    /// Diffs two previously captured snapshots, returning changed/added/removed elements.
    /// </summary>
    Task<DiffResult> DiffAsync(string before, string after);

    /// <summary>
    /// Live-edits a dependency property on an element (converting the string value to the
    /// property's type), recording an undo entry. STATE-CHANGING.
    /// </summary>
    Task<SetPropertyResult> SetPropertyAsync(string elementHandle, string propertyName, string value);

    /// <summary>
    /// Reverts live property edits: the most recent match (optionally filtered by handle
    /// and/or property) or, when <paramref name="all"/> is true, every pending edit.
    /// </summary>
    Task<RevertPropertyResult> RevertPropertyAsync(bool all, string? elementHandle, string? propertyName);

    /// <summary>
    /// Polls until an element matching the criteria satisfies the condition
    /// ("visible", "exists", "enabled", or "hidden") or the timeout elapses.
    /// </summary>
    Task<WaitForResult> WaitForElementAsync(string? rootHandle, string? typeName, string? elementName,
        string? text, string condition, int timeoutMs, int pollIntervalMs);

    /// <summary>
    /// Exports the visual tree in the specified format.
    /// </summary>
    Task<ExportResult> ExportTreeAsync(string? elementHandle, string format);

    /// <summary>
    /// Captures a screenshot of the target window or element.
    /// <paramref name="mode"/> "render" (default) re-renders the visual off-screen;
    /// "screen" captures the on-screen pixels via GDI, including open Popups,
    /// ComboBox dropdowns, context menus and tooltips.
    /// </summary>
    Task<ScreenshotResult> CaptureScreenshotAsync(string? elementHandle, int maxWidth, int maxHeight, string mode = "render");

    /// <summary>
    /// Gets the DataContext chain for an element.
    /// </summary>
    Task<DataContextResult> GetDataContextAsync(string elementHandle);

    /// <summary>
    /// Clears all captured binding errors.
    /// </summary>
    Task ClearBindingErrorsAsync();

    /// <summary>
    /// Clicks an element. Uses UI Automation by default; when <paramref name="physical"/>
    /// is true, performs a real OS mouse click at the element's screen position.
    /// <paramref name="clickType"/>: "single" (default), "double" or "right" —
    /// double/right always use the physical path.
    /// </summary>
    Task<ClickResult> ClickElementAsync(string elementHandle, bool physical, string? clickType = null);

    /// <summary>
    /// Selects an item in a Selector control (ComboBox, ListBox, ListView, TabControl)
    /// by visible text or zero-based index. Works with virtualized items.
    /// </summary>
    Task<SelectItemResult> SelectItemAsync(string elementHandle, string? itemText, int? index);

    /// <summary>
    /// Sets the text/value of an element. Uses UI Automation IValueProvider by default,
    /// with TextBox/PasswordBox/reflected fallbacks. When <paramref name="physical"/>
    /// is true, focuses the element and types via OS keyboard input.
    /// </summary>
    Task<SetTextResult> SetTextAsync(string elementHandle, string text, bool physical);

    /// <summary>
    /// Sends a keyboard shortcut or key combination (e.g. "Ctrl+S", "Enter", "F5") to
    /// the given element via OS keyboard input. When <paramref name="elementHandle"/>
    /// is null, the keys go to whatever currently has keyboard focus.
    /// </summary>
    Task<SendKeysResult> SendKeysAsync(string? elementHandle, string keys);
}
