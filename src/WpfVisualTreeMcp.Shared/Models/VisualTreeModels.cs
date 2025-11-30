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
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Result of a find elements query.
/// </summary>
public class FindElementsResult
{
    public List<FoundElement> Elements { get; set; } = new();
    public int Count { get; set; }
}
