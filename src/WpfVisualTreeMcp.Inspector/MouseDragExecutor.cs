using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Only immutable screen coordinates cross from the UI into the input worker.</summary>
internal sealed class MouseDragPlan
{
    public MouseDragPlan(int startX, int startY, int endX, int endY, string button, int durationMs, string elementType,
        IntPtr sourceWindow = default, IntPtr targetWindow = default)
    {
        StartX = startX; StartY = startY; EndX = endX; EndY = endY;
        Button = button; DurationMs = durationMs; ElementType = elementType;
        SourceWindow = sourceWindow; TargetWindow = targetWindow;
    }

    public int StartX { get; }
    public int StartY { get; }
    public int EndX { get; }
    public int EndY { get; }
    public string Button { get; }
    public int DurationMs { get; }
    public string ElementType { get; }
    public IntPtr SourceWindow { get; }
    public IntPtr TargetWindow { get; }
}

internal interface IMouseInput
{
    void ValidateStart(MouseDragPlan plan);
    void VerifyForeground();
    void Move(int x, int y);
    void Press(string button);
    void Release(string button);
}

/// <summary>Timed physical input must never occupy the WPF Dispatcher.</summary>
internal static class MouseDragExecutor
{
    public static async Task<DragResult> RunAsync(MouseDragPlan plan, IMouseInput input, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        input.ValidateStart(plan);
        input.Move(plan.StartX, plan.StartY);
        await Task.Delay(40, cancellation).ConfigureAwait(false);
        input.ValidateStart(plan);

        // Release even if Press fails after partially submitting input. A release
        // failure must propagate too: callers must not receive a false success.
        try
        {
            cancellation.ThrowIfCancellationRequested();
            input.Press(plan.Button);
            await Task.Delay(40, cancellation).ConfigureAwait(false);
            var timer = Stopwatch.StartNew();
            double progress;
            do
            {
                cancellation.ThrowIfCancellationRequested();
                input.VerifyForeground();
                progress = Math.Min(1.0, timer.Elapsed.TotalMilliseconds / plan.DurationMs);
                input.Move(
                    (int)Math.Round(plan.StartX + ((double)plan.EndX - plan.StartX) * progress),
                    (int)Math.Round(plan.StartY + ((double)plan.EndY - plan.StartY) * progress));
                if (progress < 1.0)
                    await Task.Delay(16, cancellation).ConfigureAwait(false);
            } while (progress < 1.0);
            // Give drop targets a frame at the final position before releasing.
            await Task.Delay(40, cancellation).ConfigureAwait(false);
            input.VerifyForeground();
        }
        finally
        {
            input.Release(plan.Button);
        }

        return new DragResult
        {
            ElementType = plan.ElementType, Button = plan.Button, DurationMs = plan.DurationMs,
            StartX = plan.StartX, StartY = plan.StartY, EndX = plan.EndX, EndY = plan.EndY
        };
    }
}
