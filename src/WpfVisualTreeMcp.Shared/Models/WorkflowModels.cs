using System.Text.Json;

namespace WpfVisualTreeMcp.Shared.Models;

public class ElementSelector
{
    public string TypeName { get; set; } = string.Empty;
    public string? AutomationId { get; set; }
    public string? Name { get; set; }
    public string? Text { get; set; }
    public ElementSelector? Ancestor { get; set; }
}

public class WorkflowStep
{
    public string Action { get; set; } = string.Empty;
    public ElementSelector? Target { get; set; }
    public ElementSelector? Destination { get; set; }
    public ElementSelector? Root { get; set; }
    public JsonElement Arguments { get; set; }
}

public class RecordedWorkflow
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Recorded workflow";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class RecordingState
{
    public bool Active { get; set; }
    public RecordedWorkflow? Workflow { get; set; }
}

public class ReplayResult
{
    public bool Success { get; set; }
    public int CompletedSteps { get; set; }
    public int? FailedStep { get; set; }
    public string? Error { get; set; }
}
