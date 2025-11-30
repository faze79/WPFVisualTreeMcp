using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Highlights UI elements with a visual overlay.
/// </summary>
public class ElementHighlighter
{
    private Window? _overlayWindow;
    private DispatcherTimer? _hideTimer;

    public void Highlight(UIElement element, int durationMs = 2000)
    {
        // Get element bounds in screen coordinates
        var window = Window.GetWindow(element);
        if (window == null) return;

        var bounds = GetElementBounds(element, window);
        if (bounds == Rect.Empty) return;

        // Remove existing overlay
        HideOverlay();

        // Create highlight overlay
        _overlayWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsHitTestVisible = false
        };

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)),
            BorderThickness = new Thickness(3),
            Background = new SolidColorBrush(Color.FromArgb(50, 255, 0, 0))
        };

        _overlayWindow.Content = border;
        _overlayWindow.Show();

        // Set up timer to hide
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        _hideTimer.Tick += (s, e) =>
        {
            HideOverlay();
        };
        _hideTimer.Start();
    }

    private void HideOverlay()
    {
        _hideTimer?.Stop();
        _hideTimer = null;

        _overlayWindow?.Close();
        _overlayWindow = null;
    }

    private static Rect GetElementBounds(UIElement element, Window window)
    {
        try
        {
            var transform = element.TransformToAncestor(window);
            var topLeft = transform.Transform(new Point(0, 0));
            var size = element.RenderSize;

            // Convert to screen coordinates
            var windowLocation = new Point(window.Left, window.Top);
            var screenTopLeft = new Point(
                windowLocation.X + topLeft.X,
                windowLocation.Y + topLeft.Y);

            return new Rect(screenTopLeft, size);
        }
        catch
        {
            return Rect.Empty;
        }
    }
}
