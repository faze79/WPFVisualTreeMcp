using System;
using System.IO;
using System.Windows;
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
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using (var ms = new MemoryStream())
        {
            encoder.Save(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            return (base64, pixelWidth, pixelHeight);
        }
    }
}
