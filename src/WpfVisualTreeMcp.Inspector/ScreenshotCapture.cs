using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Captures screenshots of WPF elements using RenderTargetBitmap.
/// Must be called on the WPF Dispatcher thread.
/// </summary>
public class ScreenshotCapture
{
    /// <summary>
    /// Captures a UIElement as a PNG image and returns it as base64.
    /// </summary>
    /// <param name="element">The element to capture.</param>
    /// <param name="maxWidth">Maximum width in pixels (downscales if exceeded).</param>
    /// <param name="maxHeight">Maximum height in pixels (downscales if exceeded).</param>
    /// <returns>Tuple of (base64 PNG data, width, height).</returns>
    public (string base64, int width, int height) CaptureElement(
        UIElement element, int maxWidth = 1920, int maxHeight = 1080)
    {
        // Get the actual rendered bounds
        var bounds = VisualTreeHelper.GetDescendantBounds(element);
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            // Fallback: try ActualWidth/ActualHeight for FrameworkElement
            if (element is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
            {
                bounds = new Rect(0, 0, fe.ActualWidth, fe.ActualHeight);
            }
            else
            {
                throw new InvalidOperationException(
                    "Element has zero size and cannot be captured. " +
                    "It may be collapsed or not yet rendered.");
            }
        }

        // Get DPI from the visual's presentation source
        double dpiX = 96.0, dpiY = 96.0;
        var source = PresentationSource.FromVisual(element);
        if (source?.CompositionTarget != null)
        {
            dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
            dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
        }

        // Calculate pixel dimensions at native DPI
        int pixelWidth = (int)Math.Ceiling(bounds.Width * dpiX / 96.0);
        int pixelHeight = (int)Math.Ceiling(bounds.Height * dpiY / 96.0);

        // Calculate scale factor if image exceeds max dimensions
        double scale = 1.0;
        if (pixelWidth > maxWidth || pixelHeight > maxHeight)
        {
            scale = Math.Min(
                (double)maxWidth / pixelWidth,
                (double)maxHeight / pixelHeight);
            pixelWidth = (int)(pixelWidth * scale);
            pixelHeight = (int)(pixelHeight * scale);
        }

        // Ensure minimum size
        if (pixelWidth < 1) pixelWidth = 1;
        if (pixelHeight < 1) pixelHeight = 1;

        // Render using DrawingVisual + VisualBrush technique
        // This correctly handles elements with transforms or non-zero offsets
        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            var vb = new VisualBrush(element)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            ctx.DrawRectangle(vb, null,
                new Rect(0, 0, bounds.Width, bounds.Height));
        }

        var rtb = new RenderTargetBitmap(
            pixelWidth, pixelHeight,
            dpiX * scale, dpiY * scale,
            PixelFormats.Pbgra32);
        rtb.Render(dv);

        // Encode as PNG
        return EncodePng(rtb, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Captures what is actually shown on screen for the given element (or its whole
    /// window), via GDI BitBlt. Unlike <see cref="CaptureElement"/> (which re-renders
    /// the visual off-screen), this includes open Popups, ComboBox dropdowns, context
    /// menus and tooltips — they live in separate HWNDs that RenderTargetBitmap never
    /// sees. Requires the window to be visible on screen (not minimized or covered).
    /// </summary>
    public (string base64, int width, int height) CaptureScreen(
        UIElement element, int maxWidth = 1920, int maxHeight = 1080)
    {
        if (PresentationSource.FromVisual(element) == null || !element.IsVisible)
            throw new InvalidOperationException(
                "Element is not rendered on screen; screen capture needs a visible element. " +
                "Use the default render mode for off-screen captures.");

        // Bring the host window forward so the capture isn't of an occluding window.
        var window = Window.GetWindow(element);
        window?.Activate();

        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(
            new Point(element.RenderSize.Width, element.RenderSize.Height));

        var x = (int)Math.Floor(topLeft.X);
        var y = (int)Math.Floor(topLeft.Y);
        var width = (int)Math.Ceiling(bottomRight.X - topLeft.X);
        var height = (int)Math.Ceiling(bottomRight.Y - topLeft.Y);

        if (width < 1 || height < 1)
            throw new InvalidOperationException("Element has zero on-screen size and cannot be captured.");

        var source = CaptureScreenRegion(x, y, width, height);

        // Downscale if the capture exceeds the caller's limits.
        if (source.PixelWidth > maxWidth || source.PixelHeight > maxHeight)
        {
            var scale = Math.Min(
                (double)maxWidth / source.PixelWidth,
                (double)maxHeight / source.PixelHeight);
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        return EncodePng(source, source.PixelWidth, source.PixelHeight);
    }

    private static BitmapSource CaptureScreenRegion(int x, int y, int width, int height)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("GetDC failed; cannot access the screen.");

        var memDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create GDI capture surface.");

            var oldBitmap = NativeMethods.SelectObject(memDc, bitmap);
            // CAPTUREBLT includes layered windows (WPF popups/menus are layered).
            if (!NativeMethods.BitBlt(memDc, 0, 0, width, height, screenDc, x, y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed; the region may be off-screen.");
            NativeMethods.SelectObject(memDc, oldBitmap);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static (string base64, int width, int height) EncodePng(
        BitmapSource source, int width, int height)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using (var ms = new MemoryStream())
        {
            encoder.Save(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            return (base64, width, height);
        }
    }

    private static class NativeMethods
    {
        public const uint SRCCOPY    = 0x00CC0020;
        public const uint CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(
            IntPtr hDestDc, int xDest, int yDest, int width, int height,
            IntPtr hSrcDc, int xSrc, int ySrc, uint rop);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hDc);
    }
}
