namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Represents a node in the visual tree.
/// </summary>
public class VisualTreeNode
{
    public string Handle { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public List<VisualTreeNode> Children { get; set; } = new();
    public int Depth { get; set; }
}

/// <summary>
/// Result of a visual tree query.
/// </summary>
public class VisualTreeResult
{
    public VisualTreeNode Root { get; set; } = new();
    public int TotalElements { get; set; }
    public bool MaxDepthReached { get; set; }
}

/// <summary>
/// Basic element information.
/// </summary>
public class ElementInfo
{
    public string Handle { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Name { get; set; }
}

/// <summary>
/// Information about a dependency property value.
/// </summary>
public class PropertyInfo
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Source { get; set; } = "Default";
    public bool IsBinding { get; set; }
}

/// <summary>
/// Result of a property query.
/// </summary>
public class ElementPropertiesResult
{
    public ElementInfo Element { get; set; } = new();
    public List<PropertyInfo> Properties { get; set; } = new();
}

/// <summary>
/// An element found by search.
/// </summary>
public class FoundElement
{
    public string Handle { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>Visible text content (own text or aggregated from shallow descendants), truncated.</summary>
    public string? Text { get; set; }

    /// <summary>AutomationProperties.AutomationId, when set.</summary>
    public string? AutomationId { get; set; }

    public bool? IsVisible { get; set; }
    public bool? IsEnabled { get; set; }

    /// <summary>On-screen bounding rect in physical (device) pixels; null when the element is not rendered.</summary>
    public ScreenBounds? ScreenBounds { get; set; }

    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Result of capturing a snapshot.
/// </summary>
public class SnapshotResult
{
    /// <summary>Label the snapshot was stored under (pass to wpf_diff).</summary>
    public string Label { get; set; } = string.Empty;
    public int ElementCount { get; set; }
}

/// <summary>
/// Result of diffing two snapshots.
/// </summary>
public class DiffResult
{
    public int ChangedCount { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }

    /// <summary>The full diff document (summary + changed/added/removed) as JSON.</summary>
    public string? Json { get; set; }
}

/// <summary>
/// Result of a live property edit.
/// </summary>
public class SetPropertyResult
{
    public string? ElementType { get; set; }

    /// <summary>The value read back after the write (the coerced result).</summary>
    public string? AppliedValue { get; set; }
    public string? ValueType { get; set; }

    /// <summary>What held the property before: "Binding", "Local", or "Unset".</summary>
    public string? PreviousSource { get; set; }
}

/// <summary>
/// Result of reverting one or all live property edits.
/// </summary>
public class RevertPropertyResult
{
    public int RevertedCount { get; set; }
    public string? RevertedHandle { get; set; }
    public string? RevertedProperty { get; set; }

    /// <summary>Live edits still pending after this revert.</summary>
    public int PendingCount { get; set; }
}

/// <summary>
/// Result of a wait-for-element poll.
/// </summary>
public class WaitForResult
{
    /// <summary>True when the condition was met before the timeout elapsed.</summary>
    public bool Matched { get; set; }

    /// <summary>Handle of the matched element (appear/enabled conditions); null otherwise.</summary>
    public string? MatchedHandle { get; set; }

    public string? ElementType { get; set; }

    /// <summary>How long the wait actually took, in milliseconds.</summary>
    public int WaitedMs { get; set; }
}

/// <summary>
/// Screen-space rectangle in physical (device) pixels.
/// </summary>
public class ScreenBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Result of a find elements query.
/// </summary>
public class FindElementsResult
{
    public List<FoundElement> Elements { get; set; } = new();
    public int Count { get; set; }
}
