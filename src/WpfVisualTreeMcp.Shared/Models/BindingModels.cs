namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Information about a data binding.
/// </summary>
public class BindingInfo
{
    public string Property { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string Mode { get; set; } = "OneWay";
    public string? UpdateTrigger { get; set; }
    public string Status { get; set; } = "Active";
    public string? CurrentValue { get; set; }
}

/// <summary>
/// Result of a bindings query.
/// </summary>
public class BindingsResult
{
    public ElementInfo Element { get; set; } = new();
    public List<BindingInfo> Bindings { get; set; } = new();
}

/// <summary>
/// Information about a binding error.
/// </summary>
public class BindingError
{
    public string ElementHandle { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public string? ElementName { get; set; }
    public string Property { get; set; } = string.Empty;
    public string BindingPath { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of a binding errors query.
/// </summary>
public class BindingErrorsResult
{
    public List<BindingError> Errors { get; set; } = new();
    public int Count { get; set; }
}
