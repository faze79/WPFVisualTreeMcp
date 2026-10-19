using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

public class RecordRequest : IpcRequest
{
    public override string RequestType => "Record";
    public string Action { get; set; } = "status";
    public string? Name { get; set; }
}

public class RecordResponse : IpcResponse
{
    public RecordingState? State { get; set; }
}

public class ReplayRequest : IpcRequest
{
    public override string RequestType => "Replay";
    public RecordedWorkflow Workflow { get; set; } = new();
    public int TimeoutMs { get; set; } = 120000;
}

public class ReplayResponse : IpcResponse
{
    public ReplayResult? Result { get; set; }
}

public class AssertElementRequest : IpcRequest
{
    public override string RequestType => "AssertElement";
    public string ElementHandle { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
}

public class AssertElementResponse : IpcResponse
{
    public string? ActualValue { get; set; }
}
