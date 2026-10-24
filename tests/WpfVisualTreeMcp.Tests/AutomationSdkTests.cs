using FluentAssertions;
using Moq;
using WpfVisualTreeMcp.Automation;
using WpfVisualTreeMcp.Automation.Infrastructure;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class AutomationSdkTests
{
    private static WpfApplication App(Mock<IIpcBridge> bridge, Mock<IProcessManager>? manager = null) =>
        new(bridge.Object, (manager ?? new Mock<IProcessManager>()).Object, new InspectionSession { ProcessId = 123, SessionId = "session" });
    private static ElementPropertiesResult Text(string? value) => new()
    {
        Element = new ElementInfo { TypeName = "TextBox" },
        Properties = new() { new PropertyInfo { Name = "Text", Value = value } }
    };
    private static WaitOptions Fast => new() { Timeout = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(5) };

    [Fact]
    public async Task Exact_wait_retries_missing_elements_and_recreated_controls()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.SetupSequence(x => x.TryResolveAutomationIdAsync("status", null)).ReturnsAsync((string?)null).ReturnsAsync("old").ReturnsAsync("fresh");
        bridge.Setup(x => x.GetElementPropertiesAsync("old")).ReturnsAsync(Text("not loaded"));
        bridge.Setup(x => x.GetElementPropertiesAsync("fresh")).ReturnsAsync(Text("loaded"));
        await using var app = App(bridge);
        await app.ByAutomationId("status").WaitForPropertyAsync("Text", "loaded", Fast);
        bridge.Verify(x => x.TryResolveAutomationIdAsync("status", null), Times.Exactly(3));
        bridge.Verify(x => x.GetElementPropertiesAsync("fresh"), Times.Once);
    }

    [Theory]
    [InlineData("not loaded")]
    [InlineData("Loaded")]
    [InlineData("loaded ")]
    public async Task Timeout_reports_exact_expected_and_last_actual_value(string actual)
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.TryResolveAutomationIdAsync("status", null)).ReturnsAsync("handle");
        bridge.Setup(x => x.GetElementPropertiesAsync("handle")).ReturnsAsync(Text(actual));
        await using var app = App(bridge);
        var act = () => app.ByAutomationId("status").WaitForTextAsync("loaded", TimeSpan.FromMilliseconds(80));
        await act.Should().ThrowAsync<TimeoutException>().WithMessage($"*123*status*loaded*last observed: '{actual}'*");
    }

    [Fact]
    public async Task Missing_scope_never_broadens_search_to_all_windows()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.TryResolveAutomationIdAsync("dialog", null)).ReturnsAsync((string?)null);
        await using var app = App(bridge);
        var act = () => app.ByAutomationId("dialog").ByAutomationId("status").WaitForTextAsync("loaded", TimeSpan.FromMilliseconds(80));
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*dialog*status*element not found*");
        bridge.Verify(x => x.TryResolveAutomationIdAsync("status", It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Ambiguous_and_transport_errors_fail_immediately_instead_of_becoming_timeouts()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.TryResolveAutomationIdAsync("status", null)).ThrowsAsync(new WpfAutomationException("Ambiguous locator"));
        await using var app = App(bridge);
        var act = () => app.ByAutomationId("status").WaitForTextAsync("loaded");
        await act.Should().ThrowAsync<WpfAutomationException>().WithMessage("Ambiguous*");
        bridge.Verify(x => x.TryResolveAutomationIdAsync("status", null), Times.Once);
    }

    [Fact]
    public async Task Cancellation_remains_cancellation_and_does_not_become_timeout()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.TryResolveAutomationIdAsync("status", null)).ReturnsAsync((string?)null);
        await using var app = App(bridge);
        using var cancel = new CancellationTokenSource(50);
        var act = () => app.ByAutomationId("status").WaitForTextAsync("loaded", cancellationToken: cancel.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Polling_releases_session_for_an_action_that_changes_the_condition()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string text = "loading";
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.TryResolveAutomationIdAsync("status", null)).ReturnsAsync("handle");
        bridge.Setup(x => x.GetElementPropertiesAsync("handle")).Returns(() => { observed.TrySetResult(); return Task.FromResult(Text(text)); });
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "status", null)).ReturnsAsync("handle");
        bridge.Setup(x => x.SetTextAsync("handle", "loaded", false)).Returns(() => { text = "loaded"; return Task.FromResult(new SetTextResult()); });
        await using var app = App(bridge);
        var element = app.ByAutomationId("status");
        var wait = element.WaitForPropertyAsync("Text", "loaded", Fast);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await element.SetTextAsync("loaded");
        await wait;
    }

    [Fact]
    public async Task Element_actions_resolve_new_handles_and_both_drag_endpoints()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.SetupSequence(x => x.ResolveElementHandleAsync(null, "source", null)).ReturnsAsync("first").ReturnsAsync("replacement");
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "target", null)).ReturnsAsync("destination");
        bridge.Setup(x => x.ClickElementAsync("first", false, "single", null, null)).ReturnsAsync(new ClickResult());
        bridge.Setup(x => x.DragAsync(It.Is<DragOptions>(o => o.ElementHandle == "replacement" && o.TargetHandle == "destination" && o.StartX == 10 && o.StartY == 20)))
            .ReturnsAsync(new DragResult());
        await using var app = App(bridge);
        var source = app.ByAutomationId("source");
        await source.ClickAsync();
        await source.DragToAsync(app.ByAutomationId("target"), new DragSettings { Start = new(10, 20) });
        bridge.VerifyAll();
    }

    [Fact]
    public async Task State_wait_uses_UIElement_properties_and_property_wait_reports_ambiguous_owners()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.TryResolveAutomationIdAsync("status", null)).ReturnsAsync("handle");
        bridge.Setup(x => x.GetElementPropertiesAsync("handle")).ReturnsAsync(new ElementPropertiesResult
        {
            Element = new ElementInfo { TypeName = "TextBox" },
            Properties = new()
            {
                new() { Name = "IsVisible", OwnerType = "System.Windows.UIElement", Value = "true" },
                new() { Name = "IsEnabled", OwnerType = "System.Windows.UIElement", Value = "true" },
                new() { Name = "IsEnabled", OwnerType = "System.Windows.ContentElement", Value = "false" }
            }
        });
        await using var app = App(bridge);
        var element = app.ByAutomationId("status");
        await element.WaitForAsync(ElementCondition.Enabled, Fast);
        await element.WaitForPropertyAsync("System.Windows.UIElement.IsEnabled", "true", Fast);
        var ambiguous = () => element.WaitForPropertyAsync("IsEnabled", "true", Fast);
        await ambiguous.Should().ThrowAsync<WpfAutomationException>().WithMessage("*ambiguous*System.Windows.UIElement.IsEnabled*");
    }

    [Fact]
    public async Task Cannot_drag_across_application_sessions_or_use_a_disposed_session()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        var manager = new Mock<IProcessManager>(MockBehavior.Strict);
        manager.Setup(x => x.DetachAsync("session")).Returns(Task.CompletedTask);
        var app = App(bridge, manager);
        await using var other = App(bridge);
        var source = app.ByAutomationId("source");
        var crossSession = () => source.DragToAsync(other.ByAutomationId("target"));
        await crossSession.Should().ThrowAsync<ArgumentException>();
        await app.DisposeAsync();
        await app.DisposeAsync();
        await source.Invoking(x => x.ClickAsync()).Should().ThrowAsync<ObjectDisposedException>();
        manager.Verify(x => x.DetachAsync("session"), Times.Once);
        bridge.VerifyNoOtherCalls();
    }
}
