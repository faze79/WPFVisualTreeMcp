namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Result of a click / interaction operation against a UI element.
/// </summary>
public class ClickResult
{
    /// <summary>
    /// How the click was carried out — e.g. "Invoke", "Toggle", "SelectionItem.Select",
    /// "ExpandCollapse.Expand", "SyntheticMouse", or "Physical".
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The runtime type name of the clicked element.</summary>
    public string? ElementType { get; set; }

    /// <summary>Optional extra detail, e.g. the resulting toggle state or click coordinates.</summary>
    public string? Detail { get; set; }
}
