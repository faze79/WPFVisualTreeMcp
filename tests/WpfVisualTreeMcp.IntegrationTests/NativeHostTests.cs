using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class NativeHostTests
{
    [Fact]
    public Task Native_host_accepts_its_child_pixels_but_rejects_covering_windows() => DiagnosticReporterTests.OnSta(() =>
    {
        using var host = new TestHost { Name = "NativeSurface" };
        var window = new Window { Content = host, Width = 300, Height = 250, Left = 50, Top = 50, Topmost = true };
        Window? overlay = null;
        try
        {
            window.Show(); window.UpdateLayout();
            var plan = ControlInteractor.PreparePointer(host, 20, 30);
            PhysicalInputGuard.IsHostedPoint(host.Handle, plan.StartX, plan.StartY).Should().BeTrue();
            var touch = new ControlInteractor().PrepareTouch(host, new TouchGestureOptions { ElementHandle = "test", Gesture = "tap", X = 20, Y = 30 });
            touch.Should().NotBeNull();
            using var tree = JsonDocument.Parse(new TreeWalker().WalkVisualTree(host));
            tree.RootElement.GetProperty("root").GetProperty("nativeContent").GetProperty("windowHandle").GetString().Should().StartWith("0x");
            var capture = () => new ScreenshotCapture().CaptureElement(window);
            capture.Should().Throw<InvalidOperationException>().WithMessage("*mode='auto'*");

            overlay = new Window { Width = 300, Height = 250, Left = 50, Top = 50, Topmost = true, Content = new TextBlock { Text = "Cover" } };
            overlay.Show(); overlay.UpdateLayout();
            PhysicalInputGuard.IsHostedPoint(host.Handle, plan.StartX, plan.StartY).Should().BeFalse();
        }
        finally { overlay?.Close(); window.Close(); }
    });

    private sealed class TestHost : HwndHost
    {
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            var host = CreateWindowEx(0, "button", "Native", 0x50000000, 0, 0, 300, 250, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (host == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            // WindowFromPoint resolves this child, not the HwndHost HWND itself.
            CreateWindowEx(0, "button", "Child", 0x50000000, 0, 0, 300, 250, host, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return new HandleRef(this, host);
        }
        protected override void DestroyWindowCore(HandleRef hwnd) => DestroyWindow(hwnd.Handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    }
}
