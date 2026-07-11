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

/// <summary>
/// Result of setting the text/value of a UI element.
/// </summary>
public class SetTextResult
{
    /// <summary>
    /// How the value was applied — e.g. "ValueProvider.SetValue",
    /// "DirectProperty.Text", "DirectProperty.Password", "Reflected.Text", "Physical".
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The runtime type name of the target element.</summary>
    public string? ElementType { get; set; }

    /// <summary>Optional extra detail, e.g. the number of characters typed.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Result of selecting an item in a Selector control (ComboBox, ListBox, TabControl, ...).
/// </summary>
public class SelectItemResult
{
    /// <summary>How the selection was carried out (currently "Selector.SelectedIndex").</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The runtime type name of the Selector control.</summary>
    public string? ElementType { get; set; }

    /// <summary>Which item ended up selected (index and display text).</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Result of sending a keyboard shortcut / key combination.
/// </summary>
public class SendKeysResult
{
    /// <summary>How the keys were sent (currently always "Physical").</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The runtime type name of the targeted element (null if no element was specified).</summary>
    public string? ElementType { get; set; }

    /// <summary>The parsed key combo, e.g. "Ctrl+S" or "Enter".</summary>
    public string? Detail { get; set; }
}
