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
    /// Finds elements matching the specified criteria.
    /// </summary>
    Task<FindElementsResult> FindElementsAsync(string? rootHandle, string? typeName, string? elementName, Dictionary<string, string>? propertyFilter, int maxResults = 50);

    /// <summary>
    /// Finds ALL elements matching the specified criteria without limit (deep search).
    /// WARNING: This can return a large number of results. Use with caution.
    /// </summary>
    Task<FindElementsResult> FindElementsDeepAsync(string? rootHandle, string? typeName, string? elementName);

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
    /// Exports the visual tree in the specified format.
    /// </summary>
    Task<ExportResult> ExportTreeAsync(string? elementHandle, string format);
}
