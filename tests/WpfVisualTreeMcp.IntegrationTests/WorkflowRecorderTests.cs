using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class WorkflowRecorderTests
{
    [Fact]
    public void Enforces_capacity_before_performing_the_next_operation_and_preserves_exportable_recording()
    {
        var recorder = new WorkflowRecorder(new TreeWalker());
        recorder.Control("start", "Bounded recording");
        using var arguments = JsonDocument.Parse("{\"elementName\":\"Save\"}");
        for (var i = 0; i < 100; i++) recorder.Commit(recorder.Prepare("FindElements", arguments.RootElement));
        var extra = () => recorder.Prepare("FindElements", arguments.RootElement);
        extra.Should().Throw<InvalidOperationException>().WithMessage("*100 steps*");
        var state = recorder.Control("stop", null);
        state.Active.Should().BeFalse();
        state.Workflow!.Steps.Should().HaveCount(100);
        var replace = () => recorder.Control("start", "New");
        replace.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
        recorder.Control("clear", null).Workflow.Should().BeNull();
        recorder.Control("start", "New").Active.Should().BeTrue();
        state.Workflow.Steps.Should().HaveCount(100, "previous snapshots must not change when the live recorder is cleared");
    }

    [Fact]
    public void Rejects_oversized_arguments_without_committing_a_partial_step()
    {
        var recorder = new WorkflowRecorder(new TreeWalker());
        recorder.Control("start", null);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { text = new string('x', 513 * 1024) }));
        var oversized = () => recorder.Prepare("FindElements", arguments.RootElement);
        oversized.Should().Throw<ArgumentException>().WithMessage("*512 KiB*");
        recorder.Control("status", null).Workflow!.Steps.Should().BeEmpty();
    }
}
