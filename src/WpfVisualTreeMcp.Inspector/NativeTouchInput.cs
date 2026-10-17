using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Windows touch injection, with stable IDs and complete multi-contact frames.</summary>
internal sealed class NativeTouchInput : ITouchInput
{
    private const uint InRange = 0x2, InContact = 0x4, Down = 0x10000, UpdateFlag = 0x20000,
        Up = 0x40000, Canceled = 0x8000;
    private static readonly object InitializationLock = new();
    private static bool _initialized;
    private readonly PhysicalInputGuard _guard = new();
    private TouchContactPoint[]? _last;

    public void ValidateStart(TouchGesturePlan plan)
    {
        _guard.VerifyForeground();
        _guard.EnsureMouseReleased();
        using (new PhysicalInputGuard.PhysicalCoordinates())
            foreach (var frame in plan.Frames)
                foreach (var point in frame)
                    _guard.ValidatePoint(point.X, point.Y, plan.Window);
        lock (InitializationLock)
        {
            if (_initialized) return;
            try
            {
                if (!InitializeTouchInjection(2, 3)) // TOUCH_FEEDBACK_NONE
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeTouchInjection failed.");
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new PlatformNotSupportedException("Native touch injection requires Windows 8 or later.", ex);
            }
            _initialized = true;
        }
    }

    public void VerifyForeground() => _guard.VerifyForeground();

    public void Begin(TouchContactPoint[] contacts)
    {
        Send(contacts, Down | InRange | InContact);
        _last = contacts;
    }

    public void Update(TouchContactPoint[] contacts)
    {
        Send(contacts, UpdateFlag | InRange | InContact);
        _last = contacts; // Only successful frames may be used for UP.
    }

    public void End()
    {
        if (_last == null) return;
        Send(_last, Up);
        _last = null;
    }

    public void Cancel()
    {
        if (_last == null) return;
        // Even cancellation immediately after DOWN needs an UPDATE before UP.
        // Always attempt UP if the update fails (e.g. the display changed).
        try { Send(_last, UpdateFlag | InRange | InContact); }
        finally
        {
            Send(_last, Up | Canceled);
            _last = null;
        }
    }

    private static void Send(TouchContactPoint[] points, uint flags)
    {
        var contacts = CreateContacts(points, flags);
        using (new PhysicalInputGuard.PhysicalCoordinates())
        {
            // Windows may reject frames less than 0.1ms apart. Retry only that
            // transient condition; never hide real injection failures.
            for (int attempt = 0; ; attempt++)
            {
                if (InjectTouchInput((uint)contacts.Length, contacts)) return;
                int error = Marshal.GetLastWin32Error();
                if (error != 21 || attempt >= 4)
                    throw new Win32Exception(error, $"InjectTouchInput failed (Win32 {error}). The input desktop must be interactive and available.");
                Thread.Sleep(1);
            }
        }
    }

    internal static POINTER_TOUCH_INFO[] CreateContacts(TouchContactPoint[] points, uint flags)
    {
        var contacts = new POINTER_TOUCH_INFO[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            var point = points[index];
            contacts[index] = new POINTER_TOUCH_INFO
            {
                PointerInfo = new POINTER_INFO
                {
                    // IDs must fit the initialized contact capacity (0 and 1).
                    PointerType = 2, PointerId = (uint)index, PointerFlags = flags,
                    PixelLocation = new POINT { X = point.X, Y = point.Y }
                },
                TouchMask = 1, // TOUCH_MASK_CONTACTAREA; default pressure is 512.
                Contact = new RECT { Left = point.X - 2, Top = point.Y - 2, Right = point.X + 2, Bottom = point.Y + 2 }
            };
        }
        return contacts;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTER_INFO
    {
        public uint PointerType, PointerId, FrameId, PointerFlags;
        public IntPtr SourceDevice, WindowTarget;
        public POINT PixelLocation, HimetricLocation, PixelLocationRaw, HimetricLocationRaw;
        public uint Time, HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO PointerInfo;
        public uint TouchFlags, TouchMask;
        public RECT Contact, ContactRaw;
        public uint Orientation, Pressure;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeTouchInjection(uint maxCount, uint mode);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);
}
