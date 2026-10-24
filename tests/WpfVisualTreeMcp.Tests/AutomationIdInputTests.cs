using WpfVisualTreeMcp.Automation.Infrastructure;
using FluentAssertions;
using ModelContextProtocol;
using Moq;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class AutomationIdInputTests
{
    [Theory]
    [InlineData("move")]
    [InlineData("scroll")]
    [InlineData("drag")]
    [InlineData("touch")]
    [InlineData("select")]
    [InlineData("text")]
    [InlineData("keys")]
    [InlineData("assert")]
    [InlineData("set-property")]
    [InlineData("revert-property")]
    public async Task Every_action_accepts_an_id_and_preserves_its_options(string action)
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "surface", "scope")).ReturnsAsync("resolved");
        switch (action)
        {
            case "move": bridge.Setup(x => x.MousePointerAsync(It.Is<MousePointerOptions>(o => o.ElementHandle == "resolved" && o.Action == "move" && o.X == 10 && o.Y == 20 && o.HoverMs == 100)))
                .ReturnsAsync(new MousePointerResult()); break;
            case "scroll": bridge.Setup(x => x.MousePointerAsync(It.Is<MousePointerOptions>(o => o.ElementHandle == "resolved" && o.Action == "scroll" && o.Delta == -240 && o.Horizontal)))
                .ReturnsAsync(new MousePointerResult()); break;
            case "drag": bridge.Setup(x => x.DragAsync(It.Is<DragOptions>(o => o.ElementHandle == "resolved" && o.TargetHandle == null && o.DeltaX == -20 && o.DeltaY == 30)))
                .ReturnsAsync(new DragResult()); break;
            case "touch": bridge.Setup(x => x.TouchGestureAsync(It.Is<TouchGestureOptions>(o => o.ElementHandle == "resolved" && o.Gesture == "tap")))
                .ReturnsAsync(new TouchGestureResult()); break;
            case "select": bridge.Setup(x => x.SelectItemAsync("resolved", null, 2)).ReturnsAsync(new SelectItemResult()); break;
            case "text": bridge.Setup(x => x.SetTextAsync("resolved", "", false)).ReturnsAsync(new SetTextResult()); break;
            case "keys": bridge.Setup(x => x.SendKeysAsync("resolved", "Ctrl+A")).ReturnsAsync(new SendKeysResult()); break;
            case "assert": bridge.Setup(x => x.AssertElementAsync("resolved", "Text", "")).ReturnsAsync(""); break;
            case "set-property": bridge.Setup(x => x.SetPropertyAsync("resolved", "Tag", "test")).ReturnsAsync(new SetPropertyResult()); break;
            case "revert-property": bridge.Setup(x => x.RevertPropertyAsync(false, "resolved", "Tag")).ReturnsAsync(new RevertPropertyResult()); break;
        }
        await Invoke(new WpfTools(Mock.Of<IProcessManager>(), bridge.Object), action);
        bridge.VerifyAll();
        bridge.Invocations.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("scroll")]
    [InlineData("drag")]
    [InlineData("touch")]
    [InlineData("select")]
    [InlineData("text")]
    [InlineData("keys")]
    [InlineData("assert")]
    [InlineData("set-property")]
    [InlineData("revert-property")]
    public async Task Failed_id_resolution_never_sends_input_or_falls_back_to_focus(string action)
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "surface", "scope")).ThrowsAsync(new McpException("Ambiguous AutomationId"));
        var act = () => Invoke(new WpfTools(Mock.Of<IProcessManager>(), bridge.Object), action);
        await act.Should().ThrowAsync<McpException>().WithMessage("*Ambiguous*");
        bridge.Invocations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("tap", null, null, null, null)]
    [InlineData("long_press", null, null, null, null)]
    [InlineData("swipe", 80.0, -20.0, null, null)]
    [InlineData("pinch", null, null, 1.5, null)]
    [InlineData("rotate", null, null, null, 30.0)]
    public async Task All_touch_gestures_resolve_id_and_preserve_geometry(string gesture, double? dx, double? dy, double? scale, double? angle)
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "surface", null)).ReturnsAsync("fresh");
        bridge.Setup(x => x.TouchGestureAsync(It.Is<TouchGestureOptions>(o => o.ElementHandle == "fresh" && o.Gesture == gesture &&
            o.DeltaX == dx && o.DeltaY == dy && o.Scale == scale && o.AngleDegrees == angle && o.X == 100 && o.Y == 80)))
            .ReturnsAsync(new TouchGestureResult());
        await new WpfTools(Mock.Of<IProcessManager>(), bridge.Object).WpfTouchGesture(gesture, automation_id: "surface",
            x: 100, y: 80, delta_x: dx, delta_y: dy, scale: scale, angle_degrees: angle);
        bridge.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Drag_resolves_both_endpoints_before_sending_input(bool destinationMissing)
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "source", "left")).ReturnsAsync("source-handle");
        var destination = bridge.Setup(x => x.ResolveElementHandleAsync(null, "destination", "right"));
        if (destinationMissing) destination.ThrowsAsync(new McpException("Target not found"));
        else
        {
            destination.ReturnsAsync("destination-handle");
            bridge.Setup(x => x.DragAsync(It.Is<DragOptions>(o => o.ElementHandle == "source-handle" && o.TargetHandle == "destination-handle" &&
                o.StartX == 5 && o.StartY == 10 && o.TargetX == 15 && o.TargetY == 20 && o.Button == "right" && o.DurationMs == 600)))
                .ReturnsAsync(new DragResult());
        }
        var tools = new WpfTools(Mock.Of<IProcessManager>(), bridge.Object);
        var act = () => tools.WpfDrag(automation_id: "source", root_handle: "left", target_automation_id: "destination", target_root_handle: "right",
            start_x: 5, start_y: 10, target_x: 15, target_y: 20, button: "right", duration_ms: 600);
        if (destinationMissing) await act.Should().ThrowAsync<McpException>().WithMessage("*not found*");
        else await act();
        bridge.VerifyAll();
        bridge.Invocations.Should().HaveCount(destinationMissing ? 2 : 3);
    }

    [Theory]
    [InlineData("destination-and-delta")]
    [InlineData("two-destinations")]
    [InlineData("destination-root-only")]
    [InlineData("bad-touch")]
    [InlineData("root-only-keys")]
    [InlineData("all-with-id")]
    public async Task Invalid_combinations_fail_before_resolution(string scenario)
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        var tools = new WpfTools(Mock.Of<IProcessManager>(), bridge.Object);
        Func<Task> act = scenario switch
        {
            "destination-and-delta" => () => tools.WpfDrag(automation_id: "source", target_automation_id: "target", delta_x: 5),
            "two-destinations" => () => tools.WpfDrag(automation_id: "source", target_automation_id: "target", target_handle: "handle"),
            "destination-root-only" => () => tools.WpfDrag(automation_id: "source", target_root_handle: "root", delta_x: 5),
            "bad-touch" => () => tools.WpfTouchGesture("pinch", automation_id: "surface", scale: 1),
            "root-only-keys" => () => tools.WpfSendKeys("Enter", root_handle: "root"),
            _ => () => tools.WpfRevertProperty(automation_id: "surface", all: true)
        };
        await act.Should().ThrowAsync<McpException>();
        bridge.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Keys_without_any_target_preserve_the_existing_focus_behavior()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.SendKeysAsync(null, "Ctrl+A")).ReturnsAsync(new SendKeysResult());
        await new WpfTools(Mock.Of<IProcessManager>(), bridge.Object).WpfSendKeys("Ctrl+A");
        bridge.VerifyAll();
        bridge.Invocations.Should().ContainSingle();
    }

    private static async Task Invoke(WpfTools tools, string action)
    {
        switch (action)
        {
            case "move": await tools.WpfMoveMouse(automation_id: "surface", root_handle: "scope", x: 10, y: 20, hover_ms: 100); break;
            case "scroll": await tools.WpfScroll(-240, automation_id: "surface", root_handle: "scope", horizontal: true); break;
            case "drag": await tools.WpfDrag(automation_id: "surface", root_handle: "scope", delta_x: -20, delta_y: 30); break;
            case "touch": await tools.WpfTouchGesture("tap", automation_id: "surface", root_handle: "scope"); break;
            case "select": await tools.WpfSelectItem(automation_id: "surface", root_handle: "scope", index: 2); break;
            case "text": await tools.WpfSetText("", automation_id: "surface", root_handle: "scope"); break;
            case "keys": await tools.WpfSendKeys("Ctrl+A", automation_id: "surface", root_handle: "scope"); break;
            case "assert": await tools.WpfAssert("Text", "", automation_id: "surface", root_handle: "scope"); break;
            case "set-property": await tools.WpfSetProperty("Tag", "test", automation_id: "surface", root_handle: "scope"); break;
            case "revert-property": await tools.WpfRevertProperty(automation_id: "surface", root_handle: "scope", property_name: "Tag"); break;
        }
    }
}
