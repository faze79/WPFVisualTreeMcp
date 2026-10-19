namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>A bounded observation of the realized visual tree, not an application health verdict.</summary>
public class DiagnosticReport
{
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public int ProcessId { get; set; }
    public string Scope { get; set; } = "all_windows";
    public string? RootHandle { get; set; }
    public int ScannedElements { get; set; }
    public int BindingsChecked { get; set; }
    public int DisabledElements { get; set; }
    public int InvisibleElements { get; set; }
    public int ZeroSizeElements { get; set; }
    public int FindingCount { get; set; }
    public List<DiagnosticFinding> Findings { get; set; } = new();
    public List<string> TruncationReasons { get; set; } = new();
    public int CapturedBindingErrorCount { get; set; }
    public List<BindingError> RecentBindingErrors { get; set; } = new();
    public string BindingErrorScope { get; set; } = "process_since_inspector_attach";
    public string Coverage { get; set; } = "Realized visual elements only; virtualized items are not materialized. Disabled, invisible and zero-size counts describe state, not defects. Captured binding traces are process-wide history and may no longer apply.";
}

public class DiagnosticFinding
{
    public string Severity { get; set; } = "warning";
    public string Code { get; set; } = string.Empty;
    public string ElementHandle { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Property { get; set; }
    public string Message { get; set; } = string.Empty;
}
