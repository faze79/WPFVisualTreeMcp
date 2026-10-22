using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Shared desktop, focus and coordinate checks for physical mouse/touch input.</summary>
internal sealed class PhysicalInputGuard
{
    private readonly uint _processId = (uint)Process.GetCurrentProcess().Id;

    public void VerifyForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var owner);
        if (owner != _processId)
            throw new InvalidOperationException("The attached application lost foreground focus; input was stopped.");
    }

    public void EnsureMouseReleased()
    {
        foreach (int key in new[] { 1, 2, 4 })
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
                throw new InvalidOperationException("A mouse button is already held. Release it before injecting input.");
    }

    public void ValidatePoint(int x, int y, IntPtr expectedWindow)
    {
        var point = new POINT { X = x, Y = y };
        var window = WindowFromPoint(point);
        GetWindowThreadProcessId(window, out var owner);
        if (MonitorFromPoint(point, 0) == IntPtr.Zero || window == IntPtr.Zero || owner != _processId)
            throw new InvalidOperationException("Input paths and endpoints must be on a monitor and unobstructed inside the attached application.");
        if (expectedWindow != IntPtr.Zero && GetAncestor(window, 2) != GetAncestor(expectedWindow, 2))
            throw new InvalidOperationException("A different window covers an input point. Make the target unobstructed before injecting input.");
    }

    // WPF InputHitTest cannot enter an HwndHost. Accept only pixels owned by
    // that exact native window or one of its children, never an overlay/sibling.
    public static bool IsHostedPoint(IntPtr host, int x, int y)
    {
        using (new PhysicalCoordinates())
        {
            var hit = WindowFromPoint(new POINT { X = x, Y = y });
            return host != IntPtr.Zero && hit != IntPtr.Zero &&
                (hit == host || IsChild(host, hit));
        }
    }

    // DPI awareness is thread-local; never keep this scope across an await.
    internal sealed class PhysicalCoordinates : IDisposable
    {
        private readonly IntPtr _previous;
        public PhysicalCoordinates()
        {
            try { _previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
        }
        public void Dispose()
        {
            if (_previous != IntPtr.Zero) SetThreadDpiAwarenessContext(_previous);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
