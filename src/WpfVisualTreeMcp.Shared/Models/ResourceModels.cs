namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Information about a resource.
/// </summary>
public class ResourceInfo
{
    public string Key { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Source { get; set; }
    public string? TargetType { get; set; }
}

/// <summary>
/// Result of a resources query.
/// </summary>
public class ResourcesResult
{
    public List<ResourceInfo> Resources { get; set; } = new();
}

/// <summary>
/// Information about a style setter.
/// </summary>
public class SetterInfo
{
    public string Property { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
/// Information about a style.
/// </summary>
public class StyleInfo
{
    public string? Key { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public string? BasedOn { get; set; }
    public List<SetterInfo> Setters { get; set; } = new();
}

/// <summary>
/// Information about a control template.
/// </summary>
public class TemplateInfo
{
    public string Type { get; set; } = string.Empty;
    public string VisualTreeSummary { get; set; } = string.Empty;
}

/// <summary>
/// Result of a styles query.
/// </summary>
public class StylesResult
{
    public ElementInfo Element { get; set; } = new();
    public StyleInfo? Style { get; set; }
    public TemplateInfo? Template { get; set; }
}
