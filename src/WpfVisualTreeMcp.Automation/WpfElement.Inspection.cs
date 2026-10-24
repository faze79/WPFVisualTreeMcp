using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

public sealed partial class WpfElement
{
    public Task<ElementPropertiesResult> GetPropertiesAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetElementPropertiesAsync(h), cancellationToken);
    public async Task<string?> GetPropertyAsync(string propertyName, CancellationToken cancellationToken = default) =>
        PropertyValue(await GetPropertiesAsync(cancellationToken).ConfigureAwait(false), propertyName);
    private static string? PropertyValue(ElementPropertiesResult properties, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var matches = properties.Properties.Where(p => p.Name == propertyName ||
            p.OwnerType != null && p.OwnerType + "." + p.Name == propertyName).ToArray();
        if (matches.Length == 0)
            throw new WpfAutomationException($"Property '{propertyName}' was not found on {properties.Element.TypeName}.");
        if (matches.Length > 1)
            throw new WpfAutomationException($"Property '{propertyName}' is ambiguous on {properties.Element.TypeName}. " +
                "Qualify it with its owner type: " + string.Join(", ", matches.Select(p => p.OwnerType + "." + p.Name)) + ".");
        return matches[0].Value;
    }
    public Task<VisualTreeResult> GetVisualTreeAsync(int maxDepth = 25, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetVisualTreeAsync(h, maxDepth), cancellationToken);
    public Task<BindingsResult> GetBindingsAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetBindingsAsync(h), cancellationToken);
    public Task<ResourcesResult> GetResourcesAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetResourcesAsync("element", h), cancellationToken);
    public Task<ResourcesResult> GetWindowResourcesAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetResourcesAsync("window", h), cancellationToken);
    public Task<StylesResult> GetStylesAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetStylesAsync(h), cancellationToken);
    public Task<string> WatchPropertyAsync(string propertyName, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.WatchPropertyAsync(h, propertyName), cancellationToken);
    public Task HighlightAsync(int durationMs = 2000, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.HighlightElementAsync(h, durationMs), cancellationToken);
    public Task<LayoutInfoResult> GetLayoutInfoAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetLayoutInfoAsync(h), cancellationToken);
    public Task<DataContextResult> GetDataContextAsync(CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.GetDataContextAsync(h), cancellationToken);
    public Task<EvaluateBindingResult> EvaluateBindingAsync(string propertyName, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.EvaluateBindingAsync(h, propertyName), cancellationToken);
    public Task<ExplainTriggersResult> ExplainTriggersAsync(string? propertyName = null, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.ExplainTriggersAsync(h, propertyName), cancellationToken);
    public Task<SnapshotResult> SnapshotAsync(string? label = null, int maxDepth = 25, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.SnapshotAsync(h, label, maxDepth), cancellationToken);
    public Task<ScreenshotResult> CaptureScreenshotAsync(string mode = "auto", int maxWidth = 1920, int maxHeight = 1080, bool fullContent = false, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.CaptureScreenshotAsync(h, maxWidth, maxHeight, mode, fullContent), cancellationToken);
    public Task<ExportResult> ExportTreeAsync(string format = "json", CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.ExportTreeAsync(h, format), cancellationToken);
    public Task<DiagnosticReport> ReportAsync(int maxElements = 500, int maxDepth = 25, int maxFindings = 100, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.ReportAsync(h, maxElements, maxDepth, maxFindings), cancellationToken);
    public Task<FindElementsResult> FindElementsAsync(ElementQuery query, CancellationToken cancellationToken = default) =>
        Application.FindElementsAsync(query, this, cancellationToken);
    public Task<FindElementsResult> FindElementsDeepAsync(ElementQuery query, CancellationToken cancellationToken = default) =>
        Application.FindElementsDeepAsync(query, this, cancellationToken);
}
