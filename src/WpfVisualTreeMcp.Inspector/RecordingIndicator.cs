using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Marks the window template's outer WPF surface during inspection.</summary>
internal sealed class RecordingIndicator : IDisposable
{
    private readonly Action<string> _warn;
    private readonly Func<IEnumerable<Window>> _windows;
    private readonly Dictionary<Window, RecordingAdorner> _adorners = new();
    private readonly DispatcherTimer _timer;
    private Pen _outline;
    private int _disposed;

    internal Dispatcher Dispatcher => _timer.Dispatcher;

    internal RecordingIndicator(Action<string> warn, Color color, Func<IEnumerable<Window>>? windows = null)
    {
        _warn = warn;
        _windows = windows ?? CurrentWindows;
        _outline = new Pen(new SolidColorBrush(color), 3);
        _outline.Freeze();
        // Only discovers windows/template changes. WPF handles layout and visibility.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTick;
    }

    internal void Start()
    {
        if (_disposed != 0) return;
        Refresh();
        _timer.Start();
    }

    internal void SetColor(Color color)
    {
        if (((SolidColorBrush)_outline.Brush).Color == color) return;
        _outline = new Pen(new SolidColorBrush(color), 3);
        _outline.Freeze();
        foreach (var adorner in _adorners.Values) adorner.SetOutline(_outline);
    }

    private void OnTick(object? sender, EventArgs args) => Refresh();

    internal void Refresh()
    {
        if (_disposed != 0) return;
        try
        {
            var current = new HashSet<Window>(_windows().Where(IsRecordingWindow));
            foreach (var window in _adorners.Keys.Where(window => !current.Contains(window)).ToArray())
            {
                _adorners[window].Dispose();
                _adorners.Remove(window);
            }
            foreach (var window in current)
            {
                var decorator = FindWindowDecorator(window, window);
                var surface = decorator?.Child;
                var layer = decorator?.AdornerLayer;
                if (_adorners.TryGetValue(window, out var existing))
                {
                    if (existing.AdornedElement == surface && existing.Layer == layer) continue;
                    existing.Dispose();
                    _adorners.Remove(window);
                }
                // Like Snoop, do not alter the application's template to create a layer.
                if (surface == null || layer == null) continue;
                var adorner = new RecordingAdorner(surface, layer, _outline);
                layer.Add(adorner);
                _adorners.Add(window, adorner);
            }
        }
        catch (Exception ex) { _warn("Recording outline unavailable: " + ex.Message); }
    }

    private static AdornerDecorator? FindWindowDecorator(FrameworkElement element, Window window)
    {
        if (element is AdornerDecorator decorator) return decorator;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            // Stay inside the window template. A page's nested adorner layer
            // may be small, centered or transformed independently of the window.
            if (VisualTreeHelper.GetChild(element, i) is not FrameworkElement child || child.TemplatedParent != window) continue;
            var found = FindWindowDecorator(child, window);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<Window> CurrentWindows()
    {
        var app = Application.Current;
        if (app != null && app.Dispatcher.CheckAccess()) return app.Windows.Cast<Window>().ToArray();
        return PresentationSource.CurrentSources.OfType<HwndSource>()
            .Where(source => source.Dispatcher.CheckAccess()).Select(source => source.RootVisual).OfType<Window>().ToArray();
    }

    private static bool IsRecordingWindow(Window window)
    {
        if (!window.Dispatcher.CheckAccess() || !window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        // Shadow/glow windows can be WPF windows too. Their nonactivating style
        // distinguishes them from ordinary dialogs, even ones shown without focus.
        return handle != IntPtr.Zero && (GetWindowLong(handle, -20) & 0x08000000) == 0; // GWL_EXSTYLE / WS_EX_NOACTIVATE
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        void Detach()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            foreach (var adorner in _adorners.Values) adorner.Dispose();
            _adorners.Clear();
        }
        // Inspector shutdown may run on a pipe worker rather than the UI thread.
        if (_timer.Dispatcher.CheckAccess()) Detach();
        else if (!_timer.Dispatcher.HasShutdownStarted) _timer.Dispatcher.BeginInvoke(new Action(Detach));
    }

    internal sealed class RecordingAdorner : Adorner, IDisposable
    {
        private Pen _outline;
        internal AdornerLayer Layer { get; }

        internal RecordingAdorner(UIElement content, AdornerLayer layer, Pen outline) : base(content)
        {
            Layer = layer;
            _outline = outline;
            IsHitTestVisible = false;
            Focusable = false;
            UseLayoutRounding = true;
        }

        internal void SetOutline(Pen outline)
        {
            _outline = outline;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = AdornedElement.RenderSize;
            if (size.Width <= _outline.Thickness || size.Height <= _outline.Thickness) return;
            // Draw inside the content bounds so the layer does not clip the stroke.
            double inset = _outline.Thickness / 2;
            drawingContext.DrawRectangle(null, _outline,
                new Rect(inset, inset, size.Width - _outline.Thickness, size.Height - _outline.Thickness));
        }

        public void Dispose() => Layer.Remove(this);
    }
}
