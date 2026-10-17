using System.Runtime.InteropServices;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class TouchTests
{
    [Fact]
    public void IpcPreservesGestureAndOptionalGeometry()
    {
        var options = new TouchGestureOptions { ElementHandle = "elem_1", Gesture = "rotate",
            X = 100.5, Y = 80, AngleDegrees = -75, Radius = 40, DurationMs = 1200 };
        var envelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(new TouchGestureRequest { Options = options }));
        envelope!.Value.type.Should().Be("TouchGesture");
        IpcSerializer.DeserializeRequestData<TouchGestureRequest>(envelope.Value.data)!.Options.Should().BeEquivalentTo(options);
    }

    [Theory]
    [InlineData("tap", 100)]
    [InlineData("long_press", 1000)]
    [InlineData("swipe", 700)]
    [InlineData("pinch", 700)]
    [InlineData("rotate", 700)]
    public void GestureDefaultsHaveAppropriateDurations(string gesture, int duration) =>
        new TouchGestureOptions { Gesture = gesture }.EffectiveDurationMs.Should().Be(duration);

    [Theory]
    [InlineData("handle")]
    [InlineData("gesture")]
    [InlineData("half-point")]
    [InlineData("negative-point")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("zero-swipe")]
    [InlineData("wrong-delta")]
    [InlineData("missing-scale")]
    [InlineData("zero-scale")]
    [InlineData("unit-scale")]
    [InlineData("large-scale")]
    [InlineData("wrong-scale")]
    [InlineData("missing-angle")]
    [InlineData("large-angle")]
    [InlineData("wrong-angle")]
    [InlineData("wrong-radius")]
    [InlineData("small-radius")]
    [InlineData("short-hold")]
    [InlineData("long-tap")]
    [InlineData("long-gesture")]
    public void InvalidOptionsAreRejected(string scenario)
    {
        var options = new TouchGestureOptions { ElementHandle = "source" };
        switch (scenario)
        {
            case "handle": options.ElementHandle = " "; break;
            case "gesture": options.Gesture = "scroll"; break;
            case "half-point": options.X = 1; break;
            case "negative-point": options.X = options.Y = -1; break;
            case "nan": options.X = options.Y = double.NaN; break;
            case "infinity": options.Gesture = "rotate"; options.AngleDegrees = double.PositiveInfinity; break;
            case "zero-swipe": options.Gesture = "swipe"; options.DeltaX = 0; break;
            case "wrong-delta": options.DeltaX = 1; break;
            case "missing-scale": options.Gesture = "pinch"; break;
            case "zero-scale": options.Gesture = "pinch"; options.Scale = 0; break;
            case "unit-scale": options.Gesture = "pinch"; options.Scale = 1; break;
            case "large-scale": options.Gesture = "pinch"; options.Scale = 11; break;
            case "wrong-scale": options.Scale = 2; break;
            case "missing-angle": options.Gesture = "rotate"; break;
            case "large-angle": options.Gesture = "rotate"; options.AngleDegrees = 361; break;
            case "wrong-angle": options.AngleDegrees = 20; break;
            case "wrong-radius": options.Radius = 20; break;
            case "small-radius": options.Gesture = "rotate"; options.AngleDegrees = 20; options.Radius = 0; break;
            case "short-hold": options.Gesture = "long_press"; options.DurationMs = 400; break;
            case "long-tap": options.DurationMs = 600; break;
            case "long-gesture": options.Gesture = "long_press"; options.DurationMs = 10001; break;
        }
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PinchKeepsCenterAndContactIdentity()
    {
        var options = new TouchGestureOptions { Gesture = "pinch", Scale = 2 };
        var start = TouchGesturePath.Frame(options, 100, 80, 20, 0);
        var end = TouchGesturePath.Frame(options, 100, 80, 20, 1);
        start[0].X.Should().Be(80); start[1].X.Should().Be(120);
        end[0].X.Should().Be(60); end[1].X.Should().Be(140);
        end.Should().OnlyContain(point => point.Y == 80);
    }

    [Theory]
    [InlineData(90, 60, 100)]
    [InlineData(-90, 100, 60)]
    public void RotationUsesCircularPathsWithClockwisePositiveAngles(double angle, double leftY, double rightY)
    {
        var frame = TouchGesturePath.Frame(new TouchGestureOptions { Gesture = "rotate", AngleDegrees = angle }, 100, 80, 20, 1);
        frame[0].X.Should().BeApproximately(100, 0.001);
        frame[0].Y.Should().BeApproximately(leftY, 0.001);
        frame[1].Y.Should().BeApproximately(rightY, 0.001);
    }

    [Fact]
    public void Win32StructuresHaveNativeAlignment() // Executed without calling user32.
    {
        Marshal.SizeOf<NativeTouchInput.POINTER_INFO>().Should().Be(IntPtr.Size == 8 ? 96 : 88);
        Marshal.SizeOf<NativeTouchInput.POINTER_TOUCH_INFO>().Should().Be(IntPtr.Size == 8 ? 144 : 136);
        Marshal.OffsetOf<NativeTouchInput.POINTER_INFO>("PerformanceCount").ToInt32().Should().Be(IntPtr.Size == 8 ? 80 : 72);
    }

    [Fact]
    public void TwoFingerFrameUsesIdsWithinInitializedCapacity()
    {
        var points = new[] { new TouchContactPoint { X = 10, Y = 20 }, new TouchContactPoint { X = 60, Y = 20 } };
        var down = NativeTouchInput.CreateContacts(points, 0x10006);
        var up = NativeTouchInput.CreateContacts(points, 0x40000);
        down.Select(contact => contact.PointerInfo.PointerId).Should().Equal(0u, 1u);
        up.Select(contact => contact.PointerInfo.PointerId).Should().Equal(0u, 1u);
        up[1].PointerInfo.PixelLocation.X.Should().Be(down[1].PointerInfo.PixelLocation.X);
        up[1].PointerInfo.PointerFlags.Should().Be(0x40000);
    }

    [Fact]
    public async Task HoldSendsUpdatesBeforeReleasingAllContacts()
    {
        var input = new FakeTouch();
        var result = await TouchGestureExecutor.RunAsync(Plan(), input, CancellationToken.None);
        input.Events.Should().Equal("begin", "update", "update", "end");
        result.ContactCount.Should().Be(2);
        input.Frames.Should().OnlyContain(frame => frame.Length == 2);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("update")]
    [InlineData("focus")]
    [InlineData("end")]
    public async Task AnyInjectionFailureAttemptsCancellation(string failure)
    {
        var input = new FakeTouch { Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() => TouchGestureExecutor.RunAsync(Plan(), input, CancellationToken.None));
        input.Events.Last().Should().Be("cancel");
    }

    [Fact]
    public async Task CancellationAfterDownCleansUpContacts()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new FakeTouch { OnBegin = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TouchGestureExecutor.RunAsync(Plan(), input, cancellation.Token));
        input.Events.Should().Equal("begin", "cancel");
    }

    [Fact]
    public async Task FailedCleanupPreservesBothErrors()
    {
        var input = new FakeTouch { Failure = "update", FailCancellation = true };
        var error = await Assert.ThrowsAsync<AggregateException>(() => TouchGestureExecutor.RunAsync(Plan(), input, CancellationToken.None));
        error.InnerExceptions.Should().HaveCount(2);
    }

    [Fact]
    public async Task FailedPreflightNeverInjectsOrCancelsContacts()
    {
        var input = new FakeTouch { Failure = "validate" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => TouchGestureExecutor.RunAsync(Plan(), input, CancellationToken.None));
        input.Events.Should().BeEmpty();
    }

    private static TouchGesturePlan Plan()
    {
        var points = new[] { new TouchContactPoint { X = 30, Y = 50 }, new TouchContactPoint { X = 70, Y = 50 } };
        return new("long_press", 50, "Border", IntPtr.Zero, new[] { points, points, points });
    }
    private sealed class FakeTouch : ITouchInput
    {
        public readonly List<string> Events = new();
        public readonly List<TouchContactPoint[]> Frames = new();
        public string? Failure;
        public bool FailCancellation;
        public Action? OnBegin;
        public void ValidateStart(TouchGesturePlan plan) { if (Failure == "validate") throw new InvalidOperationException("preflight"); }
        public void VerifyForeground() { if (Failure == "focus") throw new InvalidOperationException("focus"); }
        private void Record(string name) { Events.Add(name); if (Failure == name) throw new InvalidOperationException(name); }
        public void Begin(TouchContactPoint[] contacts) { Record("begin"); Frames.Add(contacts); OnBegin?.Invoke(); }
        public void Update(TouchContactPoint[] contacts) { Record("update"); Frames.Add(contacts); }
        public void End() => Record("end");
        public void Cancel() { Events.Add("cancel"); if (FailCancellation) throw new InvalidOperationException("cleanup"); }
    }
}
