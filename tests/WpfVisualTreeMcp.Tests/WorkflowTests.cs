using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class WorkflowTests
{
    private static ElementSelector Selector(string name) => new() { TypeName = "System.Windows.Controls.Border", Name = name };

    [Fact]
    public void Drag_recording_preserves_local_parameters_and_replaces_both_session_handles()
    {
        var original = new DragRequest { Options = new DragOptions { ElementHandle = "elem_old", TargetHandle = "elem_old2", StartX = 12, StartY = 14, DurationMs = 600 } };
        var step = new WorkflowStep { Action = "Drag", Target = Selector("Source"), Destination = Selector("Destination"), Arguments = WorkflowCodec.Arguments(original) };
        var workflow = new RecordedWorkflow { Steps = new() { step } };
        WorkflowCodec.Validate(workflow);
        var json = IpcSerializer.Serialize(workflow);
        json.Should().NotContain("elem_old").And.NotContain("elementHandle").And.NotContain("targetHandle");
        var restored = IpcSerializer.Deserialize<RecordedWorkflow>(json)!;
        var request = (DragRequest)WorkflowCodec.ReadRequest(restored.Steps[0].Action, restored.Steps[0].Arguments);
        WorkflowCodec.BindHandles(request, "elem_fresh", "elem_fresh2", null);
        request.Options.Validate();
        request.Options.ElementHandle.Should().Be("elem_fresh");
        request.Options.TargetHandle.Should().Be("elem_fresh2");
        request.Options.StartX.Should().Be(12);
        request.Options.DurationMs.Should().Be(600);
    }

    [Fact]
    public void Touch_export_is_data_only_and_preserves_unicode_and_rotation()
    {
        var request = new TouchGestureRequest { Options = new TouchGestureOptions { ElementHandle = "elem_old", Gesture = "rotate", AngleDegrees = -90, Radius = 40 } };
        var workflow = new RecordedWorkflow { Name = "à漢字", Steps = new() { new WorkflowStep { Action = "TouchGesture", Target = Selector("Surface"), Arguments = WorkflowCodec.Arguments(request) } } };
        var files = WorkflowTestExporter.Export(new RecordingState { Workflow = workflow }, "xunit");
        files.Keys.Should().BeEquivalentTo("workflow.json", "RecordedWorkflow.Tests.csproj", "WorkflowTests.cs", "README.md");
        var restored = IpcSerializer.Deserialize<RecordedWorkflow>(files["workflow.json"])!;
        restored.Name.Should().Be("à漢字");
        var touch = (TouchGestureRequest)WorkflowCodec.ReadRequest("TouchGesture", restored.Steps[0].Arguments);
        touch.Options.AngleDegrees.Should().Be(-90);
        touch.Options.Radius.Should().Be(40);
    }

    [Theory]
    [InlineData("RunShell", "{}")]
    [InlineData("ClickElement", "{\"elementHandle\":\"elem_old\"}")]
    [InlineData("Drag", "{\"options\":null}")]
    [InlineData("WaitForElement", "{\"elementName\":\"Spinner\",\"timeoutMs\":999999}")]
    public void Rejects_invalid_steps_before_execution(string action, string arguments)
    {
        using var json = JsonDocument.Parse(arguments);
        var workflow = new RecordedWorkflow { Steps = new() { new WorkflowStep { Action = action, Target = Selector("Target"), Arguments = json.RootElement.Clone() } } };
        var validate = () => WorkflowCodec.Validate(workflow);
        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Refuses_to_export_active_or_empty_recordings()
    {
        var active = () => WorkflowTestExporter.Export(new RecordingState { Active = true, Workflow = new RecordedWorkflow() }, "xunit");
        active.Should().Throw<InvalidOperationException>().WithMessage("*Stop recording*");
        var empty = () => WorkflowTestExporter.Export(new RecordingState { Workflow = new RecordedWorkflow() }, "xunit");
        empty.Should().Throw<ArgumentException>();
    }
}
