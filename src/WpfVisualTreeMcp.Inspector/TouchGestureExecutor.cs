using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

internal readonly struct TouchPosition
{
    public TouchPosition(double x, double y) { X = x; Y = y; }
    public double X { get; }
    public double Y { get; }
}

internal sealed class TouchGesturePlan
{
    public TouchGesturePlan(string gesture, int durationMs, string elementType, IntPtr window, TouchContactPoint[][] frames)
    {
        Gesture = gesture; DurationMs = durationMs; ElementType = elementType; Window = window; Frames = frames;
    }
    public string Gesture { get; }
    public int DurationMs { get; }
    public string ElementType { get; }
    public IntPtr Window { get; }
    public TouchContactPoint[][] Frames { get; }
}

internal static class TouchGesturePath
{
    public static TouchPosition[] Frame(TouchGestureOptions options, double x, double y, double radius, double progress)
    {
        if (options.Gesture == "swipe")
            return new[] { new TouchPosition(x + options.DeltaX.GetValueOrDefault() * progress,
                y + options.DeltaY.GetValueOrDefault() * progress) };
        if (options.Gesture is "tap" or "long_press") return new[] { new TouchPosition(x, y) };
        double distance = radius * (options.Gesture == "pinch" ? 1 + (options.Scale!.Value - 1) * progress : 1);
        double radians = options.Gesture == "rotate" ? options.AngleDegrees!.Value * Math.PI / 180 * progress : 0;
        double dx = Math.Cos(radians) * distance, dy = Math.Sin(radians) * distance;
        // Stable contact order: the initially left finger always remains id 0.
        return new[] { new TouchPosition(x - dx, y - dy), new TouchPosition(x + dx, y + dy) };
    }
}

internal interface ITouchInput
{
    void ValidateStart(TouchGesturePlan plan);
    void VerifyForeground();
    void Begin(TouchContactPoint[] contacts);
    void Update(TouchContactPoint[] contacts);
    void End();
    void Cancel();
}

internal static class TouchGestureExecutor
{
    public static async Task<TouchGestureResult> RunAsync(TouchGesturePlan plan, ITouchInput input, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        input.ValidateStart(plan);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            input.Begin(plan.Frames[0]);
            var timer = Stopwatch.StartNew();
            for (int index = 1; index < plan.Frames.Length; index++)
            {
                int due = (int)((long)plan.DurationMs * index / (plan.Frames.Length - 1));
                int delay = Math.Max(1, due - (int)timer.ElapsedMilliseconds);
                await Task.Delay(delay, cancellation).ConfigureAwait(false);
                input.VerifyForeground();
                input.Update(plan.Frames[index]);
            }
            // UP must repeat the last successful UPDATE coordinates.
            await Task.Delay(16, cancellation).ConfigureAwait(false);
            input.VerifyForeground();
            input.End();
        }
        catch (Exception failure)
        {
            try { input.Cancel(); }
            catch (Exception cleanup) { throw new AggregateException("Touch input failed and contact cancellation also failed.", failure, cleanup); }
            throw;
        }
        return new TouchGestureResult
        {
            Gesture = plan.Gesture, DurationMs = plan.DurationMs, ElementType = plan.ElementType,
            ContactCount = plan.Frames[0].Length, Start = plan.Frames[0], End = plan.Frames[plan.Frames.Length - 1]
        };
    }
}
