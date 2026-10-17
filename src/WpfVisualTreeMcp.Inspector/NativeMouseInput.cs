using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Checked SendInput with virtual-desktop coordinates (including negative monitor origins).</summary>
internal sealed class NativeMouseInput : IMouseInput
{
    private readonly PhysicalInputGuard _guard = new();

    public void ValidateStart(MouseDragPlan plan)
    {
        VerifyForeground();
        _guard.EnsureMouseReleased();
        using (new PhysicalInputGuard.PhysicalCoordinates())
        {
            _guard.ValidatePoint(plan.StartX, plan.StartY, plan.SourceWindow);
            _guard.ValidatePoint(plan.EndX, plan.EndY, plan.TargetWindow);
        }
    }

    public void VerifyForeground() => _guard.VerifyForeground();

    public void Move(int x, int y)
    {
        using (new PhysicalInputGuard.PhysicalCoordinates())
        {
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            if (width <= 0 || height <= 0 || x < left || y < top ||
                (long)x >= (long)left + width || (long)y >= (long)top + height)
                throw new InvalidOperationException("Drag coordinate is outside the virtual desktop.");
            Send(0x0001 | 0x8000 | 0x4000, // MOVE | ABSOLUTE | VIRTUALDESK
                Math.Min(65535, (int)(((double)x - left + 0.5) * 65536 / width)),
                Math.Min(65535, (int)(((double)y - top + 0.5) * 65536 / height)));
        }
    }

    public void Press(string button) => Send(ButtonFlag(button, false));
    public void Release(string button) => Send(ButtonFlag(button, true));

    private static uint ButtonFlag(string button, bool up) => button switch
    {
        "left" => up ? 0x0004u : 0x0002u,
        "right" => up ? 0x0010u : 0x0008u,
        "middle" => up ? 0x0040u : 0x0020u,
        _ => throw new ArgumentException("Unsupported mouse button.")
    };

    private static void Send(uint flags, int x = 0, int y = 0)
    {
        // MOUSEINPUT is the largest member of INPUT's union on both x86 and x64.
        var inputs = new[] { new INPUT { Mouse = new MOUSEINPUT { X = x, Y = y, Flags = flags } } };
        if (SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SendInput failed. The input desktop may be unavailable or input blocked by Windows integrity restrictions.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public MOUSEINPUT Mouse; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
