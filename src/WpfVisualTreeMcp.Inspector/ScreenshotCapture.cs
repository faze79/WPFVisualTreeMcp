using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private const long MaxFullContentPixelCount = 64L * 1024 * 1024;

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
    /// Captures all content hosted by a ScrollViewer, including content outside the
    /// current viewport. Non-virtualized content is rendered directly. Virtualized
    /// content is paged through the viewport and stitched into one image.
    /// </summary>
    public (string base64, int width, int height) CaptureFullContent(
        UIElement element, int maxWidth = 1920, int maxHeight = 1080)
    {
        element.UpdateLayout();
        var scrollViewer = FindScrollViewer(element);
        if (scrollViewer == null)
        {
            throw new InvalidOperationException(
                "Full-content capture requires a ScrollViewer or an element whose template contains one.");
        }

        scrollViewer.ApplyTemplate();
        scrollViewer.UpdateLayout();

        if (scrollViewer.Content is UIElement content && !ContainsActiveVirtualizingPanel(scrollViewer))
        {
            var bounds = GetFullContentBounds(content);
            if (bounds.Width >= 1 && bounds.Height >= 1)
                return CaptureVisual(content, bounds, maxWidth, maxHeight);
        }

        return CaptureScrollableViewport(scrollViewer, maxWidth, maxHeight);
    }

    private static Rect GetFullContentBounds(UIElement element)
    {
        var bounds = new Rect(new Point(0, 0), element.RenderSize);
        var descendantBounds = VisualTreeHelper.GetDescendantBounds(element);
        if (!descendantBounds.IsEmpty)
            bounds.Union(descendantBounds);
        return bounds;
    }

    private static (string base64, int width, int height) CaptureVisual(
        UIElement element, Rect bounds, int maxWidth, int maxHeight)
    {
        GetDpi(element, out var dpiX, out var dpiY);

        int nativePixelWidth = (int)Math.Ceiling(bounds.Width * dpiX / 96.0);
        int nativePixelHeight = (int)Math.Ceiling(bounds.Height * dpiY / 96.0);
        var scale = CalculateScale(nativePixelWidth, nativePixelHeight, maxWidth, maxHeight);
        var bitmap = RenderVisual(element, bounds, dpiX, dpiY, scale);

        return EncodePng(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static BitmapSource RenderVisual(
        UIElement element, Rect bounds, double dpiX, double dpiY, double scale = 1.0)
    {
        int pixelWidth = Math.Max(1, (int)(Math.Ceiling(bounds.Width * dpiX / 96.0) * scale));
        int pixelHeight = Math.Max(1, (int)(Math.Ceiling(bounds.Height * dpiY / 96.0) * scale));
        EnsureFullContentBudget(checked((long)pixelWidth * pixelHeight));

        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            var vb = new VisualBrush(element)
            {
                Viewbox = bounds,
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
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
        rtb.Freeze();
        return rtb;
    }

    private static void GetDpi(Visual visual, out double dpiX, out double dpiY)
    {
        dpiX = 96.0;
        dpiY = 96.0;
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
        {
            dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
            dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
        }
    }

    private static double CalculateScale(int pixelWidth, int pixelHeight, int maxWidth, int maxHeight)
    {
        if (pixelWidth <= maxWidth && pixelHeight <= maxHeight)
            return 1.0;
        return Math.Min((double)maxWidth / pixelWidth, (double)maxHeight / pixelHeight);
    }

    private static ScrollViewer? FindScrollViewer(UIElement element)
    {
        if (element is ScrollViewer scrollViewer)
            return scrollViewer;

        if (element is Control control)
            control.ApplyTemplate();

        ScrollViewer? best = null;
        double bestExtent = -1;
        VisitVisualDescendants(element, current =>
        {
            if (current is not ScrollViewer candidate)
                return false;

            candidate.ApplyTemplate();
            candidate.UpdateLayout();
            var extent = candidate.ExtentWidth * candidate.ExtentHeight;
            if (extent > bestExtent)
            {
                best = candidate;
                bestExtent = extent;
            }
            return false;
        });
        return best;
    }

    private static bool ContainsActiveVirtualizingPanel(DependencyObject root)
    {
        return FindActiveVirtualizingPanel(root) != null;
    }

    private static VirtualizingPanel? FindActiveVirtualizingPanel(DependencyObject root)
    {
        VirtualizingPanel? found = null;
        VisitVisualDescendants(root, current =>
        {
            if (current is not VirtualizingPanel panel)
                return false;

            var owner = ItemsControl.GetItemsOwner(panel);
            if (owner != null && VirtualizingPanel.GetIsVirtualizing(owner))
            {
                found = panel;
                return true;
            }
            if (owner == null && found == null)
                found = panel;
            return false;
        });
        return found;
    }

    private static ScrollContentPresenter? FindScrollContentPresenter(ScrollViewer scrollViewer)
    {
        var presenter = scrollViewer.Template?.FindName("PART_ScrollContentPresenter", scrollViewer)
            as ScrollContentPresenter;
        if (presenter != null)
            return presenter;

        VisitVisualDescendants(scrollViewer, current =>
        {
            presenter = current as ScrollContentPresenter;
            return presenter != null;
        });
        return presenter;
    }

    private static bool VisitVisualDescendants(
        DependencyObject root, Func<DependencyObject, bool> visitor)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (visitor(child))
                return true;
            if (VisitVisualDescendants(child, visitor))
                return true;
        }
        return false;
    }

    private static (string base64, int width, int height) CaptureScrollableViewport(
        ScrollViewer scrollViewer, int maxWidth, int maxHeight)
    {
        var presenter = FindScrollContentPresenter(scrollViewer);
        if (presenter == null || presenter.ActualWidth < 1 || presenter.ActualHeight < 1)
            throw new InvalidOperationException("The ScrollViewer viewport is not rendered and cannot be stitched.");

        if (scrollViewer.CanContentScroll)
            return CaptureLogicalVerticalScroll(scrollViewer, presenter, maxWidth, maxHeight);
        return CapturePhysicalScroll(scrollViewer, presenter, maxWidth, maxHeight);
    }

    private static (string base64, int width, int height) CapturePhysicalScroll(
        ScrollViewer scrollViewer, ScrollContentPresenter presenter, int maxWidth, int maxHeight)
    {
        var originalHorizontalOffset = scrollViewer.HorizontalOffset;
        var originalVerticalOffset = scrollViewer.VerticalOffset;
        try
        {
            GetDpi(scrollViewer, out var dpiX, out var dpiY);
            var frames = new List<CaptureFrame>();
            long retainedPixels = 0;
            var horizontalOffsets = BuildOffsets(scrollViewer.ScrollableWidth, scrollViewer.ViewportWidth);
            var verticalOffsets = BuildOffsets(scrollViewer.ScrollableHeight, scrollViewer.ViewportHeight);
            if (horizontalOffsets.Count * verticalOffsets.Count > 400)
                throw new InvalidOperationException("The scrollable surface requires more than 400 capture tiles.");

            foreach (var verticalOffset in verticalOffsets)
            {
                foreach (var horizontalOffset in horizontalOffsets)
                {
                    scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                    scrollViewer.ScrollToVerticalOffset(verticalOffset);
                    scrollViewer.UpdateLayout();

                    var bitmap = RenderVisual(
                        presenter,
                        new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight),
                        dpiX,
                        dpiY);
                    retainedPixels = AddFrame(frames, new CaptureFrame(
                        bitmap,
                        (int)Math.Round(scrollViewer.HorizontalOffset * dpiX / 96.0),
                        (int)Math.Round(scrollViewer.VerticalOffset * dpiY / 96.0)), retainedPixels);
                }
            }

            var width = Math.Max(1, (int)Math.Ceiling(
                Math.Max(scrollViewer.ExtentWidth, presenter.ActualWidth) * dpiX / 96.0));
            var height = Math.Max(1, (int)Math.Ceiling(
                Math.Max(scrollViewer.ExtentHeight, presenter.ActualHeight) * dpiY / 96.0));
            return ComposeFrames(frames, width, height, maxWidth, maxHeight, retainedPixels);
        }
        finally
        {
            RestoreOffsets(scrollViewer, originalHorizontalOffset, originalVerticalOffset);
        }
    }

    private static (string base64, int width, int height) CaptureLogicalVerticalScroll(
        ScrollViewer scrollViewer, ScrollContentPresenter presenter, int maxWidth, int maxHeight)
    {
        if (scrollViewer.ScrollableWidth > 0)
        {
            throw new InvalidOperationException(
                "Full-content stitching of a logically scrolling, virtualized control " +
                "currently supports vertical scrolling only.");
        }

        var originalHorizontalOffset = scrollViewer.HorizontalOffset;
        var originalVerticalOffset = scrollViewer.VerticalOffset;
        try
        {
            GetDpi(scrollViewer, out var dpiX, out var dpiY);
            var frames = new List<CaptureFrame>();
            BitmapSource? previous = null;
            IReadOnlyDictionary<int, int>? previousItemPositions = null;
            double previousOffset = 0;
            double previousViewportHeight = 0;
            long retainedPixels = 0;
            var y = 0;
            var virtualizingPanel = FindActiveVirtualizingPanel(scrollViewer);
            var itemsOwner = virtualizingPanel == null
                ? null
                : ItemsControl.GetItemsOwner(virtualizingPanel);

            scrollViewer.ScrollToHome();
            scrollViewer.UpdateLayout();

            for (var page = 0; page < 200; page++)
            {
                var currentOffset = scrollViewer.VerticalOffset;
                var bitmap = RenderVisual(
                    presenter,
                    new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight),
                    dpiX,
                    dpiY);
                var currentItemPositions = GetRealizedItemPositions(
                    virtualizingPanel, itemsOwner, presenter, dpiY);
                if (previous != null)
                {
                    var expectedAdvance = CalculateRealizedPixelAdvance(
                        previousItemPositions!, currentItemPositions);
                    if (expectedAdvance == null)
                    {
                        var logicalAdvance = currentOffset - previousOffset;
                        expectedAdvance = (int)Math.Round(
                            previous.PixelHeight * logicalAdvance / Math.Max(1.0, previousViewportHeight));
                    }
                    var boundedAdvance = Math.Max(
                        1, Math.Min(previous.PixelHeight, expectedAdvance.Value));
                    var expectedOverlap = previous.PixelHeight - boundedAdvance;
                    y += previous.PixelHeight - FindVerticalOverlap(
                        previous, bitmap, expectedOverlap, 2);
                }
                retainedPixels = AddFrame(frames, new CaptureFrame(bitmap, 0, y), retainedPixels);
                previous = bitmap;
                previousItemPositions = currentItemPositions;
                previousOffset = currentOffset;
                previousViewportHeight = scrollViewer.ViewportHeight;

                if (currentOffset >= scrollViewer.ScrollableHeight)
                    break;

                var pageSize = Math.Max(1.0, Math.Floor(scrollViewer.ViewportHeight) - 1.0);
                scrollViewer.ScrollToVerticalOffset(
                    Math.Min(scrollViewer.ScrollableHeight, currentOffset + pageSize));
                scrollViewer.UpdateLayout();
                if (scrollViewer.VerticalOffset <= currentOffset)
                    break;

                if (page == 199)
                    throw new InvalidOperationException("The scrollable surface requires more than 200 capture pages.");
            }

            var width = 1;
            var height = 1;
            foreach (var frame in frames)
            {
                width = Math.Max(width, frame.X + frame.Bitmap.PixelWidth);
                height = Math.Max(height, frame.Y + frame.Bitmap.PixelHeight);
            }
            return ComposeFrames(frames, width, height, maxWidth, maxHeight, retainedPixels);
        }
        finally
        {
            RestoreOffsets(scrollViewer, originalHorizontalOffset, originalVerticalOffset);
        }
    }

    private static IReadOnlyDictionary<int, int> GetRealizedItemPositions(
        VirtualizingPanel? panel,
        ItemsControl? owner,
        Visual ancestor,
        double dpiY)
    {
        var positions = new Dictionary<int, int>();
        if (panel == null || owner == null)
            return positions;

        VisitVisualDescendants(panel, current =>
        {
            if (current is not Visual visual)
                return false;

            var index = owner.ItemContainerGenerator.IndexFromContainer(current);
            if (index < 0)
                return false;

            var point = visual.TransformToAncestor(ancestor).Transform(new Point());
            positions[index] = (int)Math.Round(point.Y * dpiY / 96.0);
            return false;
        });
        return positions;
    }

    internal static int? CalculateRealizedPixelAdvance(
        IReadOnlyDictionary<int, int> previousPositions,
        IReadOnlyDictionary<int, int> currentPositions)
    {
        var advances = new List<int>();
        foreach (var pair in previousPositions)
        {
            if (!currentPositions.TryGetValue(pair.Key, out var currentPosition))
                continue;

            var advance = pair.Value - currentPosition;
            if (advance > 0)
                advances.Add(advance);
        }

        if (advances.Count == 0)
            return null;

        advances.Sort();
        var middle = advances.Count / 2;
        if (advances.Count % 2 != 0)
            return advances[middle];
        return (int)Math.Round((advances[middle - 1] + advances[middle]) / 2.0);
    }

    private static List<double> BuildOffsets(double scrollable, double viewport)
    {
        var offsets = new List<double> { 0 };
        var step = Math.Max(1.0, viewport);
        while (offsets[offsets.Count - 1] < scrollable)
        {
            var next = Math.Min(scrollable, offsets[offsets.Count - 1] + step);
            if (next <= offsets[offsets.Count - 1])
                break;
            offsets.Add(next);
        }
        return offsets;
    }

    private static int FindVerticalOverlap(
        BitmapSource previous, BitmapSource current, int expectedOverlap, int tolerance)
    {
        var width = Math.Min(previous.PixelWidth, current.PixelWidth);
        var maximum = Math.Min(previous.PixelHeight, current.PixelHeight);
        var stride = width * 4;
        var previousPixels = new byte[stride * previous.PixelHeight];
        var currentPixels = new byte[stride * current.PixelHeight];
        previous.CopyPixels(new Int32Rect(0, 0, width, previous.PixelHeight), previousPixels, stride, 0);
        current.CopyPixels(new Int32Rect(0, 0, width, current.PixelHeight), currentPixels, stride, 0);

        expectedOverlap = Math.Max(0, Math.Min(maximum, expectedOverlap));
        tolerance = Math.Max(0, tolerance);
        for (var distance = 0; distance <= tolerance; distance++)
        {
            for (var direction = distance == 0 ? 0 : -1; direction <= 1; direction += 2)
            {
                var overlap = expectedOverlap + distance * direction;
                if (overlap < 0 || overlap > maximum)
                    continue;

                var previousStart = (previous.PixelHeight - overlap) * stride;
                var bytes = overlap * stride;
                var equal = true;
                for (var i = 0; i < bytes; i++)
                {
                    if (previousPixels[previousStart + i] == currentPixels[i])
                        continue;
                    equal = false;
                    break;
                }
                if (equal)
                    return overlap;

                if (distance == 0)
                    break;
            }
        }
        return expectedOverlap;
    }

    private static (string base64, int width, int height) ComposeFrames(
        List<CaptureFrame> frames, int sourceWidth, int sourceHeight, int maxWidth, int maxHeight,
        long retainedPixels)
    {
        var scale = CalculateScale(sourceWidth, sourceHeight, maxWidth, maxHeight);
        var pixelWidth = Math.Max(1, (int)(sourceWidth * scale));
        var pixelHeight = Math.Max(1, (int)(sourceHeight * scale));
        EnsureFullContentBudget(checked(retainedPixels + (long)pixelWidth * pixelHeight));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            foreach (var frame in frames)
            {
                context.DrawImage(frame.Bitmap, new Rect(
                    frame.X * scale,
                    frame.Y * scale,
                    frame.Bitmap.PixelWidth * scale,
                    frame.Bitmap.PixelHeight * scale));
            }
        }

        var bitmap = new RenderTargetBitmap(
            pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return EncodePng(bitmap, pixelWidth, pixelHeight);
    }

    private static long AddFrame(
        List<CaptureFrame> frames, CaptureFrame frame, long retainedPixels)
    {
        var nextPixelCount = checked(
            retainedPixels + (long)frame.Bitmap.PixelWidth * frame.Bitmap.PixelHeight);
        EnsureFullContentBudget(nextPixelCount);
        frames.Add(frame);
        return nextPixelCount;
    }

    private static void EnsureFullContentBudget(long pixelCount)
    {
        if (pixelCount > MaxFullContentPixelCount)
        {
            throw new InvalidOperationException(
                $"Full-content capture exceeds the {MaxFullContentPixelCount:N0}-pixel memory budget.");
        }
    }

    private static void RestoreOffsets(
        ScrollViewer scrollViewer, double horizontalOffset, double verticalOffset)
    {
        scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
        scrollViewer.ScrollToVerticalOffset(verticalOffset);
        scrollViewer.UpdateLayout();
    }

    private sealed class CaptureFrame
    {
        public CaptureFrame(BitmapSource bitmap, int x, int y)
        {
            Bitmap = bitmap;
            X = x;
            Y = y;
        }

        public BitmapSource Bitmap { get; }
        public int X { get; }
        public int Y { get; }
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
        public const uint SRCCOPY = 0x00CC0020;
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
