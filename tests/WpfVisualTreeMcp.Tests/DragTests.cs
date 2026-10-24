using WpfVisualTreeMcp.Automation.Infrastructure;
using FluentAssertions;
using ModelContextProtocol;
using Moq;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class DragTests
{
    [Fact]
    public void IpcRoundTripPreservesLocalPointsAndNegativeDisplacement()
    {
        var options = new DragOptions { ElementHandle = "elem_42", DeltaX = -120.5,
            StartX = 5, StartY = 7, Button = "middle", DurationMs = 1200 };
        var envelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(new DragRequest { Options = options }));
        envelope!.Value.type.Should().Be("Drag");
        IpcSerializer.DeserializeRequestData<DragRequest>(envelope.Value.data)!.Options.Should().BeEquivalentTo(options);
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("missing-destination")]
    [InlineData("ambiguous-destination")]
    [InlineData("empty-target")]
    [InlineData("zero-delta")]
    [InlineData("partial-start")]
    [InlineData("partial-target")]
    [InlineData("target-point-without-target")]
    [InlineData("negative-local")]
    [InlineData("nan")]
    [InlineData("infinite")]
    [InlineData("too-short")]
    [InlineData("too-long")]
    [InlineData("invalid-button")]
    public void RejectsInvalidOptionsBeforeAnyInput(string scenario)
    {
        var options = new DragOptions { ElementHandle = "source", DeltaX = 20 };
        switch (scenario)
        {
            case "missing-source": options.ElementHandle = " "; break;
            case "missing-destination": options.DeltaX = null; break;
            case "ambiguous-destination": options.TargetHandle = "target"; break;
            case "empty-target": options.TargetHandle = ""; options.DeltaX = null; break;
            case "zero-delta": options.DeltaX = 0; break;
            case "partial-start": options.StartX = 5; break;
            case "partial-target": options.TargetHandle = "target"; options.DeltaX = null; options.TargetY = 5; break;
            case "target-point-without-target": options.TargetX = options.TargetY = 5; break;
            case "negative-local": options.StartX = -1; options.StartY = 0; break;
            case "nan": options.DeltaX = double.NaN; break;
            case "infinite": options.DeltaY = double.PositiveInfinity; break;
            case "too-short": options.DurationMs = 99; break;
            case "too-long": options.DurationMs = 10001; break;
            case "invalid-button": options.Button = "extra"; break;
        }
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ToolRejectsAmbiguousDestinationWithoutCallingBridge()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        var tools = new WpfTools(Mock.Of<IProcessManager>(), bridge.Object);
        await Assert.ThrowsAsync<McpException>(() => tools.WpfDrag("source", "target", delta_x: 10));
        bridge.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("middle")]
    public async Task SequenceMovesWhilePressedThenReleasesAtExactDestination(string button)
    {
        var mouse = new FakeMouse();
        var plan = new MouseDragPlan(-200, 50, -50, 125, button, 100, "Thumb");
        var result = await MouseDragExecutor.RunAsync(plan, mouse, CancellationToken.None);
        mouse.Events.First().Should().Be("move:-200,50");
        mouse.Events[1].Should().Be($"press:{button}");
        mouse.Events[^2].Should().Be("move:-50,125");
        mouse.Events.Last().Should().Be($"release:{button}");
        mouse.MovesWhilePressed.Should().BeGreaterThan(2);
        mouse.Pressed.Should().BeFalse();
        result.EndX.Should().Be(-50);
    }

    [Theory]
    [InlineData("press")]
    [InlineData("move")]
    [InlineData("focus")]
    public async Task FailureAfterPressAlwaysAttemptsRelease(string failure)
    {
        var mouse = new FakeMouse { Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MouseDragExecutor.RunAsync(Plan(), mouse, CancellationToken.None));
        mouse.Events.Last().Should().Be("release:left");
        mouse.Pressed.Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDuringHoldReleasesButton()
    {
        using var cancellation = new CancellationTokenSource();
        var mouse = new FakeMouse { OnPress = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MouseDragExecutor.RunAsync(Plan(), mouse, cancellation.Token));
        mouse.Events.Last().Should().Be("release:left");
    }

    [Fact]
    public async Task InvalidDesktopDoesNotMoveOrReleaseUsersMouse()
    {
        var mouse = new FakeMouse { Failure = "validate" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MouseDragExecutor.RunAsync(Plan(), mouse, CancellationToken.None));
        mouse.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedReleaseDoesNotReportSuccess()
    {
        var mouse = new FakeMouse { Failure = "release" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MouseDragExecutor.RunAsync(Plan(), mouse, CancellationToken.None));
        mouse.Events.Last().Should().Be("release:left");
    }

    private static MouseDragPlan Plan() => new(10, 10, 100, 20, "left", 100, "Border");

    private sealed class FakeMouse : IMouseInput
    {
        public readonly List<string> Events = new();
        public string? Failure;
        public Action? OnPress;
        public bool Pressed;
        public int MovesWhilePressed;
        public void ValidateStart(MouseDragPlan plan)
        {
            if (Failure == "validate") throw new InvalidOperationException("desktop unavailable");
        }
        public void VerifyForeground()
        {
            if (Pressed && Failure == "focus") throw new InvalidOperationException("focus lost");
        }
        public void Move(int x, int y)
        {
            if (Pressed)
            {
                MovesWhilePressed++;
                if (Failure == "move") throw new InvalidOperationException("input rejected");
            }
            Events.Add($"move:{x},{y}");
        }
        public void Press(string button)
        {
            Pressed = true;
            Events.Add($"press:{button}");
            OnPress?.Invoke();
            if (Failure == "press") throw new InvalidOperationException("press failed");
        }
        public void Release(string button)
        {
            Pressed = false;
            Events.Add($"release:{button}");
            if (Failure == "release") throw new InvalidOperationException("release failed");
        }
    }
}
