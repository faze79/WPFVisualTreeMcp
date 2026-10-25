using FluentAssertions;
using Moq;
using WpfVisualTreeMcp.Automation;
using WpfVisualTreeMcp.Automation.Infrastructure;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class AutomaticActionTests
{
    private static WpfApplication App(Mock<IIpcBridge> bridge) => new(bridge.Object, new Mock<IProcessManager>().Object,
        new InspectionSession { ProcessId = 123, SessionId = "test" })
        { DefaultActionOptions = new() { Timeout = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(5) } };
    private static ElementPropertiesResult State(bool enabled) => new()
    {
        Element = new(), Properties = new()
        {
            new() { Name = "IsVisible", OwnerType = "System.Windows.UIElement", Value = "true" },
            new() { Name = "IsEnabled", OwnerType = "System.Windows.UIElement", Value = enabled.ToString() }
        }
    };

    [Fact]
    public async Task Action_waits_for_existence_and_enabled_then_delivers_once()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.SetupSequence(b => b.TryResolveAutomationIdAsync("save", null)).ReturnsAsync((string?)null).ReturnsAsync("old").ReturnsAsync("new");
        bridge.Setup(b => b.GetElementPropertiesAsync("old")).ReturnsAsync(State(false));
        bridge.Setup(b => b.GetElementPropertiesAsync("new")).ReturnsAsync(State(true));
        bridge.Setup(b => b.ClickElementAsync("new", false, "single", null, null)).ReturnsAsync(new ClickResult());
        await using var app = App(bridge);
        await app.ByAutomationId("save").ClickAsync();
        bridge.Verify(b => b.ClickElementAsync(It.IsAny<string>(), false, "single", null, null), Times.Once);
    }

    [Fact]
    public async Task Delivered_action_failure_is_never_retried()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(b => b.TryResolveAutomationIdAsync("save", null)).ReturnsAsync("h");
        bridge.Setup(b => b.GetElementPropertiesAsync("h")).ReturnsAsync(State(true));
        bridge.Setup(b => b.ClickElementAsync("h", false, "single", null, null)).ThrowsAsync(new WpfAutomationException("Response lost after delivery"));
        await using var app = App(bridge);
        var click = () => app.ByAutomationId("save").ClickAsync();
        await click.Should().ThrowAsync<WpfAutomationException>().WithMessage("*after delivery*");
        bridge.Verify(b => b.ClickElementAsync("h", false, "single", null, null), Times.Once);
    }

    [Fact]
    public async Task Timeout_and_cancellation_do_not_send_input()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(b => b.TryResolveAutomationIdAsync("save", null)).ReturnsAsync((string?)null);
        await using var app = App(bridge);
        var element = app.ByAutomationId("save").WithActionOptions(new() { Timeout = TimeSpan.FromMilliseconds(60), PollInterval = TimeSpan.FromMilliseconds(5) });
        await element.Invoking(e => e.ClickAsync()).Should().ThrowAsync<TimeoutException>().WithMessage("*missing, hidden or disabled*");
        using var cancel = new CancellationTokenSource(20);
        await element.Invoking(e => e.ClickAsync(cancellationToken: cancel.Token)).Should().ThrowAsync<OperationCanceledException>();
        bridge.Verify(b => b.ClickElementAsync(It.IsAny<string>(), false, "single", null, null), Times.Never);
    }

    [Fact]
    public async Task Virtualized_preparation_can_retry_but_successful_edit_runs_once()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(b => b.TryResolveAutomationIdAsync("grid", null)).ReturnsAsync("grid-h");
        bridge.Setup(b => b.GetElementPropertiesAsync("grid-h")).ReturnsAsync(State(true));
        bridge.SetupSequence(b => b.ControlOperationAsync(It.Is<ControlOperationRequest>(r => r.Operation == "grid_edit" && r.Row!.KeyValue == "42" && r.Column!.Header == "Quantity")))
            .ReturnsAsync(new ControlOperationResponse { NotReady = true, Reason = "row not realized" })
            .ReturnsAsync(new ControlOperationResponse { Result = new() { Value = "7", Changed = true } });
        await using var app = App(bridge);
        var result = await app.ByAutomationId("grid").AsDataGrid().RowByKey("Id", "42").Cell("Quantity").SetTextAsync("7");
        result.Changed.Should().BeTrue();
        bridge.Verify(b => b.ControlOperationAsync(It.IsAny<ControlOperationRequest>()), Times.Exactly(2));
    }
}
