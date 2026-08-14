using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
    internal const long MaxFullContentOutputPixelCount = 8L * 1024 * 1024;
    internal const long MaxFullContentEncodedByteCount = 32L * 1024 * 1024;
    private const int MaxRenderTilePixelCount = 1024 * 1024;
    private const int RenderTileWidth = 1024;
    private const int MaxPhysicalCaptureTileCount = 400;
    private const int ComparisonTileWidth = 1024;
    private const int ComparisonTileHeight = 64;

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
        return CaptureFullContent(
            element, maxWidth, maxHeight, CancellationToken.None);
    }

    internal (string base64, int width, int height) CaptureFullContent(
        UIElement element, int maxWidth, int maxHeight, CancellationToken cancellationToken)
    {
        ThrowIfFullContentCaptureTimedOut(cancellationToken);
        element.UpdateLayout();
        ThrowIfFullContentCaptureTimedOut(cancellationToken);
        var scrollViewer = FindScrollViewer(element);
        ThrowIfFullContentCaptureTimedOut(cancellationToken);
        if (scrollViewer == null)
        {
            throw new InvalidOperationException(
                "Full-content capture requires a ScrollViewer or an element whose template contains one.");
        }

        scrollViewer.ApplyTemplate();
        scrollViewer.UpdateLayout();
        ThrowIfFullContentCaptureTimedOut(cancellationToken);

        if (scrollViewer.Content is UIElement content && !ContainsActiveVirtualizingPanel(scrollViewer))
        {
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            var bounds = GetFullContentBounds(content);
            if (bounds.Width >= 1 && bounds.Height >= 1)
            {
                var result = CaptureVisual(
                    content, bounds, maxWidth, maxHeight, cancellationToken);
                ThrowIfFullContentCaptureTimedOut(cancellationToken);
                return result;
            }
        }

        return CaptureScrollableViewport(
            scrollViewer, maxWidth, maxHeight, cancellationToken);
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
        UIElement element, Rect bounds, int maxWidth, int maxHeight,
        CancellationToken cancellationToken)
    {
        GetDpi(element, out var dpiX, out var dpiY);

        int nativePixelWidth = (int)Math.Ceiling(bounds.Width * dpiX / 96.0);
        int nativePixelHeight = (int)Math.Ceiling(bounds.Height * dpiY / 96.0);
        var scale = CalculateFullContentScale(
            nativePixelWidth, nativePixelHeight, maxWidth, maxHeight);
        var bitmap = RenderVisual(
            element, bounds, dpiX, dpiY, scale, cancellationToken: cancellationToken);

        return EncodeFullContentPng(
            bitmap, bitmap.PixelWidth, bitmap.PixelHeight, cancellationToken);
    }

    private static BitmapSource RenderVisual(
        UIElement element, Rect bounds, double dpiX, double dpiY, double scale = 1.0,
        long retainedPixels = 0, CancellationToken cancellationToken = default)
    {
        int pixelWidth = Math.Max(1, (int)(Math.Ceiling(bounds.Width * dpiX / 96.0) * scale));
        int pixelHeight = Math.Max(1, (int)(Math.Ceiling(bounds.Height * dpiY / 96.0) * scale));
        CalculateNextRetainedPixelCount(retainedPixels, pixelWidth, pixelHeight);

        var bitmap = new WriteableBitmap(
            pixelWidth, pixelHeight, dpiX * scale, dpiY * scale, PixelFormats.Pbgra32, null);
        var tiles = BuildRenderTiles(pixelWidth, pixelHeight);
        var maxTileWidth = Math.Min(pixelWidth, RenderTileWidth);
        var maxTileHeight = Math.Max(
            1, Math.Min(pixelHeight, MaxRenderTilePixelCount / maxTileWidth));
        var pixels = new byte[checked(maxTileWidth * maxTileHeight * 4)];
        var pixelsPerDipX = pixelWidth / bounds.Width;
        var pixelsPerDipY = pixelHeight / bounds.Height;

        foreach (var tile in tiles)
        {
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            var tileBounds = new Rect(
                bounds.X + tile.X / pixelsPerDipX,
                bounds.Y + tile.Y / pixelsPerDipY,
                tile.Width / pixelsPerDipX,
                tile.Height / pixelsPerDipY);
            var tileBitmap = RenderVisualTile(
                element, tileBounds, tile.Width, tile.Height);
            var stride = checked(tile.Width * 4);
            tileBitmap.CopyPixels(pixels, stride, 0);
            bitmap.WritePixels(tile, pixels, stride, 0);
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
        }

        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource RenderVisualTile(
        UIElement element, Rect bounds, int pixelWidth, int pixelHeight)
    {
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
            96.0 * pixelWidth / bounds.Width,
            96.0 * pixelHeight / bounds.Height,
            PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    internal static IReadOnlyList<Int32Rect> BuildRenderTiles(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth < 1)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var tiles = new List<Int32Rect>();
        var tileWidth = Math.Min(pixelWidth, RenderTileWidth);
        var tileHeight = Math.Max(
            1, Math.Min(pixelHeight, MaxRenderTilePixelCount / tileWidth));
        for (var y = 0; y < pixelHeight; y += tileHeight)
        {
            var height = Math.Min(tileHeight, pixelHeight - y);
            for (var x = 0; x < pixelWidth; x += tileWidth)
            {
                var width = Math.Min(tileWidth, pixelWidth - x);
                tiles.Add(new Int32Rect(x, y, width, height));
            }
        }
        return tiles;
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

    internal static double CalculateFullContentScale(
        int pixelWidth, int pixelHeight, int maxWidth, int maxHeight)
    {
        return CalculateScale(
            pixelWidth, pixelHeight, maxWidth, maxHeight, MaxFullContentOutputPixelCount);
    }

    private static double CalculateScale(
        int pixelWidth, int pixelHeight, int maxWidth, int maxHeight,
        long maxPixelCount = long.MaxValue)
    {
        var scale = Math.Min(
            1.0,
            Math.Min((double)maxWidth / pixelWidth, (double)maxHeight / pixelHeight));
        var scaledPixelCount = pixelWidth * (double)pixelHeight * scale * scale;
        if (scaledPixelCount > maxPixelCount)
        {
            scale = Math.Min(
                scale, Math.Sqrt(maxPixelCount / (pixelWidth * (double)pixelHeight)));
        }
        return scale;
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
        ScrollViewer scrollViewer, int maxWidth, int maxHeight,
        CancellationToken cancellationToken)
    {
        ThrowIfFullContentCaptureTimedOut(cancellationToken);
        var presenter = FindScrollContentPresenter(scrollViewer);
        if (presenter == null || presenter.ActualWidth < 1 || presenter.ActualHeight < 1)
            throw new InvalidOperationException("The ScrollViewer viewport is not rendered and cannot be stitched.");

        if (scrollViewer.CanContentScroll)
            return CaptureLogicalVerticalScroll(
                scrollViewer, presenter, maxWidth, maxHeight, cancellationToken);
        return CapturePhysicalScroll(
            scrollViewer, presenter, maxWidth, maxHeight, cancellationToken);
    }

    private static (string base64, int width, int height) CapturePhysicalScroll(
        ScrollViewer scrollViewer, ScrollContentPresenter presenter, int maxWidth, int maxHeight,
        CancellationToken cancellationToken)
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
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            if (ExceedsCaptureTileLimit(horizontalOffsets.Count, verticalOffsets.Count))
                throw new InvalidOperationException("The scrollable surface requires more than 400 capture tiles.");

            foreach (var verticalOffset in verticalOffsets)
            {
                foreach (var horizontalOffset in horizontalOffsets)
                {
                    ThrowIfFullContentCaptureTimedOut(cancellationToken);
                    scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                    scrollViewer.ScrollToVerticalOffset(verticalOffset);
                    scrollViewer.UpdateLayout();
                    ThrowIfFullContentCaptureTimedOut(cancellationToken);

                    var bitmap = RenderVisual(
                        presenter,
                        new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight),
                        dpiX,
                        dpiY,
                        retainedPixels: retainedPixels,
                        cancellationToken: cancellationToken);
                    ThrowIfFullContentCaptureTimedOut(cancellationToken);
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
            return ComposeFrames(
                frames, width, height, maxWidth, maxHeight, retainedPixels, cancellationToken);
        }
        finally
        {
            RestoreOffsets(scrollViewer, originalHorizontalOffset, originalVerticalOffset);
        }
    }

    private static (string base64, int width, int height) CaptureLogicalVerticalScroll(
        ScrollViewer scrollViewer, ScrollContentPresenter presenter, int maxWidth, int maxHeight,
        CancellationToken cancellationToken)
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
            ThrowIfFullContentCaptureTimedOut(cancellationToken);

            for (var page = 0; page < 200; page++)
            {
                ThrowIfFullContentCaptureTimedOut(cancellationToken);
                var currentOffset = scrollViewer.VerticalOffset;
                var bitmap = RenderVisual(
                    presenter,
                    new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight),
                    dpiX,
                    dpiY,
                    retainedPixels: retainedPixels,
                    cancellationToken: cancellationToken);
                ThrowIfFullContentCaptureTimedOut(cancellationToken);
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
                ThrowIfFullContentCaptureTimedOut(cancellationToken);
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
            return ComposeFrames(
                frames, width, height, maxWidth, maxHeight, retainedPixels, cancellationToken);
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

    internal static List<double> BuildOffsets(double scrollable, double viewport)
    {
        var offsets = new List<double> { 0 };
        var step = Math.Max(1.0, viewport);
        while (offsets.Count <= MaxPhysicalCaptureTileCount &&
            offsets[offsets.Count - 1] < scrollable)
        {
            var next = Math.Min(scrollable, offsets[offsets.Count - 1] + step);
            if (next <= offsets[offsets.Count - 1])
                break;
            offsets.Add(next);
        }
        return offsets;
    }

    internal static bool ExceedsCaptureTileLimit(int horizontalCount, int verticalCount)
    {
        return (long)horizontalCount * verticalCount > MaxPhysicalCaptureTileCount;
    }

    internal static int FindVerticalOverlap(
        BitmapSource previous, BitmapSource current, int expectedOverlap, int tolerance)
    {
        var width = Math.Min(previous.PixelWidth, current.PixelWidth);
        var maximum = Math.Min(previous.PixelHeight, current.PixelHeight);
        var bufferWidth = Math.Min(width, ComparisonTileWidth);
        var bufferHeight = Math.Min(maximum, ComparisonTileHeight);
        var bufferSize = checked(bufferWidth * bufferHeight * 4);
        var previousPixels = new byte[bufferSize];
        var currentPixels = new byte[bufferSize];

        expectedOverlap = Math.Max(0, Math.Min(maximum, expectedOverlap));
        tolerance = Math.Max(0, tolerance);
        for (var distance = 0; distance <= tolerance; distance++)
        {
            for (var direction = distance == 0 ? 0 : -1; direction <= 1; direction += 2)
            {
                var overlap = expectedOverlap + distance * direction;
                if (overlap < 0 || overlap > maximum)
                    continue;

                if (VerticalRegionsEqual(
                    previous, current, width, overlap, previousPixels, currentPixels))
                    return overlap;

                if (distance == 0)
                    break;
            }
        }
        return expectedOverlap;
    }

    private static bool VerticalRegionsEqual(
        BitmapSource previous,
        BitmapSource current,
        int width,
        int overlap,
        byte[] previousPixels,
        byte[] currentPixels)
    {
        for (var y = 0; y < overlap; y += ComparisonTileHeight)
        {
            var height = Math.Min(ComparisonTileHeight, overlap - y);
            for (var x = 0; x < width; x += ComparisonTileWidth)
            {
                var currentWidth = Math.Min(ComparisonTileWidth, width - x);
                var stride = checked(currentWidth * 4);
                var byteCount = checked(stride * height);
                previous.CopyPixels(
                    new Int32Rect(x, previous.PixelHeight - overlap + y, currentWidth, height),
                    previousPixels,
                    stride,
                    0);
                current.CopyPixels(
                    new Int32Rect(x, y, currentWidth, height),
                    currentPixels,
                    stride,
                    0);
                for (var i = 0; i < byteCount; i++)
                {
                    if (previousPixels[i] != currentPixels[i])
                        return false;
                }
            }
        }
        return true;
    }

    private static (string base64, int width, int height) ComposeFrames(
        List<CaptureFrame> frames, int sourceWidth, int sourceHeight, int maxWidth, int maxHeight,
        long retainedPixels, CancellationToken cancellationToken)
    {
        ThrowIfFullContentCaptureTimedOut(cancellationToken);
        var scale = CalculateFullContentScale(
            sourceWidth, sourceHeight, maxWidth, maxHeight);
        var pixelWidth = Math.Max(1, (int)(sourceWidth * scale));
        var pixelHeight = Math.Max(1, (int)(sourceHeight * scale));
        EnsureFullContentBudget(checked(retainedPixels + (long)pixelWidth * pixelHeight));
        var bitmap = new WriteableBitmap(
            pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32, null);
        foreach (var frame in frames)
        {
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            CopyFrame(
                frame, bitmap, scale, cancellationToken);
        }

        bitmap.Freeze();
        var result = EncodeFullContentPng(
            bitmap, pixelWidth, pixelHeight, cancellationToken);
        ThrowIfFullContentCaptureTimedOut(cancellationToken);
        return result;
    }

    private static void CopyFrame(
        CaptureFrame frame, WriteableBitmap target, double scale,
        CancellationToken cancellationToken)
    {
        var destinationX = (int)Math.Round(frame.X * scale);
        var destinationY = (int)Math.Round(frame.Y * scale);
        var scaledWidth = Math.Max(1, (int)Math.Round(frame.Bitmap.PixelWidth * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(frame.Bitmap.PixelHeight * scale));
        BitmapSource source = frame.Bitmap;
        if (scaledWidth != source.PixelWidth || scaledHeight != source.PixelHeight)
        {
            source = new TransformedBitmap(source, new ScaleTransform(
                (double)scaledWidth / source.PixelWidth,
                (double)scaledHeight / source.PixelHeight));
            source.Freeze();
        }

        var copyWidth = Math.Min(source.PixelWidth, target.PixelWidth - destinationX);
        var copyHeight = Math.Min(source.PixelHeight, target.PixelHeight - destinationY);
        if (copyWidth < 1 || copyHeight < 1)
            return;

        var tileWidth = Math.Min(copyWidth, RenderTileWidth);
        var tileHeight = Math.Max(
            1, Math.Min(copyHeight, MaxRenderTilePixelCount / tileWidth));
        var pixels = new byte[checked(tileWidth * tileHeight * 4)];
        for (var y = 0; y < copyHeight; y += tileHeight)
        {
            var height = Math.Min(tileHeight, copyHeight - y);
            for (var x = 0; x < copyWidth; x += tileWidth)
            {
                ThrowIfFullContentCaptureTimedOut(cancellationToken);
                var width = Math.Min(tileWidth, copyWidth - x);
                var sourceRect = new Int32Rect(x, y, width, height);
                var stride = checked(width * 4);
                source.CopyPixels(sourceRect, pixels, stride, 0);
                target.WritePixels(
                    new Int32Rect(destinationX + x, destinationY + y, width, height),
                    pixels,
                    stride,
                    0);
                ThrowIfFullContentCaptureTimedOut(cancellationToken);
            }
        }
    }

    private static long AddFrame(
        List<CaptureFrame> frames, CaptureFrame frame, long retainedPixels)
    {
        var nextPixelCount = CalculateNextRetainedPixelCount(
            retainedPixels, frame.Bitmap.PixelWidth, frame.Bitmap.PixelHeight);
        frames.Add(frame);
        return nextPixelCount;
    }

    internal static long CalculateNextRetainedPixelCount(
        long retainedPixels, int pixelWidth, int pixelHeight)
    {
        var nextPixelCount = checked(retainedPixels + (long)pixelWidth * pixelHeight);
        EnsureFullContentBudget(nextPixelCount);
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

    private static void ThrowIfFullContentCaptureTimedOut(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new TimeoutException("Full-content capture exceeded its execution deadline.");
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
        return EncodePng(
            source, width, height, new MemoryStream(), CancellationToken.None);
    }

    internal static (string base64, int width, int height) EncodeFullContentPng(
        BitmapSource source, int width, int height, CancellationToken cancellationToken,
        long maxEncodedByteCount = MaxFullContentEncodedByteCount)
    {
        return EncodePng(
            source,
            width,
            height,
            new BoundedMemoryStream(maxEncodedByteCount, cancellationToken),
            cancellationToken);
    }

    private static (string base64, int width, int height) EncodePng(
        BitmapSource source, int width, int height, MemoryStream stream,
        CancellationToken cancellationToken)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using (stream)
        {
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            try
            {
                encoder.Save(stream);
            }
            catch (InvalidOperationException)
                when (stream is BoundedMemoryStream boundedStream &&
                    boundedStream.Failure is TimeoutException timeoutException)
            {
                throw new TimeoutException(timeoutException.Message, timeoutException);
            }
            catch (InvalidOperationException)
                when (stream is BoundedMemoryStream boundedStream &&
                    boundedStream.Failure is InvalidOperationException budgetException &&
                    budgetException.Message.StartsWith("Full-content encoded PNG", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(budgetException.Message, budgetException);
            }
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            var base64 = Convert.ToBase64String(
                stream.GetBuffer(), 0, checked((int)stream.Length));
            ThrowIfFullContentCaptureTimedOut(cancellationToken);
            return (base64, width, height);
        }
    }

    private sealed class BoundedMemoryStream : MemoryStream
    {
        private readonly long _maxLength;
        private readonly CancellationToken _cancellationToken;

        public Exception? Failure { get; private set; }

        public BoundedMemoryStream(long maxLength, CancellationToken cancellationToken)
        {
            if (maxLength < 1 || maxLength > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxLength));

            _maxLength = maxLength;
            _cancellationToken = cancellationToken;
        }

        public override void SetLength(long value)
        {
            EnsureLength(value);
            base.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureLength(checked(Position + count));
            base.Write(buffer, offset, count);
            EnsureNotCanceled();
        }

        public override void WriteByte(byte value)
        {
            EnsureLength(checked(Position + 1));
            base.WriteByte(value);
            EnsureNotCanceled();
        }

        private void EnsureLength(long requestedLength)
        {
            EnsureNotCanceled();
            if (requestedLength > _maxLength)
            {
                Failure = new InvalidOperationException(
                    $"Full-content encoded PNG exceeds the {_maxLength:N0}-byte memory budget.");
                throw Failure;
            }
        }

        private void EnsureNotCanceled()
        {
            if (!_cancellationToken.IsCancellationRequested)
                return;

            Failure = new TimeoutException(
                "Full-content capture exceeded its execution deadline.");
            throw Failure;
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
