using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class RecordingIndicatorTests
{
    [Fact]
    public Task Draws_a_noninteractive_outline_that_follows_window_layout_and_is_excluded_from_inspection() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        var warnings = new List<string>();
        using var indicator = new RecordingIndicator(warnings.Add, Colors.Red, () => new[] { window });
        try
        {
            window.Show(); window.UpdateLayout();
            var content = (UIElement)window.Content;
            var foreground = GetForegroundWindow();
            var focus = Keyboard.FocusedElement;
            int sourceCount = PresentationSource.CurrentSources.OfType<HwndSource>().Count(s => s.Dispatcher.CheckAccess());
            var layer = AdornerLayer.GetAdornerLayer(content);
            var applicationAdorner = new ApplicationAdorner(content);
            layer.Add(applicationAdorner);

            indicator.Start(); window.UpdateLayout();
            var outline = FindOutline(window)!;
            outline.Should().NotBeNull();
            var surface = outline.AdornedElement;
            outline.IsHitTestVisible.Should().BeFalse();
            outline.Focusable.Should().BeFalse();
            GetForegroundWindow().Should().Be(foreground);
            Keyboard.FocusedElement.Should().BeSameAs(focus);
            PresentationSource.CurrentSources.OfType<HwndSource>().Count(s => s.Dispatcher.CheckAccess()).Should().Be(sourceCount);
            outline.RenderSize.Width.Should().BeApproximately(surface.RenderSize.Width, 0.01);
            outline.RenderSize.Height.Should().BeApproximately(surface.RenderSize.Height, 0.01);
            AssertBorder(outline, Colors.Red);
            TreeWalker.GetAllVisualChildren(content).Should().Contain(applicationAdorner).And.NotContain(outline);
            TreeWalker.GetAllVisualChildren(surface).Should().NotContain(outline);
            TreeWalker.GetAllVisualChildren(layer).Should().Contain(applicationAdorner).And.NotContain(outline);

            window.Left += 30; window.Top += 20; window.Width += 70; window.Height += 50;
            window.UpdateLayout();
            outline.RenderSize.Width.Should().BeApproximately(surface.RenderSize.Width, 0.01, "WPF updates layout without an indicator refresh");
            outline.RenderSize.Height.Should().BeApproximately(surface.RenderSize.Height, 0.01);
            AssertBorder(outline, Colors.Red);
            indicator.Refresh();
            FindOutline(window).Should().BeSameAs(outline, "refresh must not add duplicate adorners");

            window.Hide();
            outline.IsVisible.Should().BeFalse();
            window.Show(); window.UpdateLayout();
            outline.IsVisible.Should().BeTrue();
            indicator.Dispose(); indicator.Dispose();
            FindOutline(window).Should().BeNull();
            layer.GetAdorners(content).Should().Contain(applicationAdorner, "application adorners are left intact");
            window.IsVisible.Should().BeTrue();
            warnings.Should().BeEmpty();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Discovers_new_windows_and_releases_closed_windows() => DiagnosticReporterTests.OnSta(() =>
    {
        var windows = new List<Window>();
        using var indicator = new RecordingIndicator(_ => { }, Colors.Red, () => windows);
        var window = NewWindow();
        try
        {
            indicator.Start();
            windows.Add(window);
            window.Show(); window.UpdateLayout();
            var pump = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timeout.Tick += (_, _) => { timeout.Stop(); pump.Continue = false; };
            timeout.Start(); Dispatcher.PushFrame(pump);
            var outline = FindOutline(window)!;
            outline.Should().NotBeNull();
            window.Close(); windows.Clear(); indicator.Refresh();
            (outline.Layer.GetAdorners(outline.AdornedElement) ?? Array.Empty<Adorner>()).Should().NotContain(outline);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Skips_missing_layers_without_changing_the_window_and_attaches_when_a_layer_becomes_available() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        var content = window.Content;
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        var template = new ControlTemplate(typeof(Window)) { VisualTree = presenter };
        window.Template = template;
        var warnings = new List<string>();
        using var indicator = new RecordingIndicator(warnings.Add, Colors.Red, () => new[] { window });
        try
        {
            window.Show(); window.UpdateLayout();
            AdornerLayer.GetAdornerLayer((UIElement)content).Should().BeNull();
            indicator.Start(); indicator.Refresh();
            FindOutline(window).Should().BeNull();
            window.Template.Should().BeSameAs(template);
            window.Content.Should().BeSameAs(content);
            warnings.Should().BeEmpty();

            window.ClearValue(Control.TemplateProperty); window.UpdateLayout();
            indicator.Refresh();
            FindOutline(window).Should().NotBeNull();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Keeps_the_window_outline_when_page_content_changes() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        using var indicator = new RecordingIndicator(_ => { }, Colors.Red, () => new[] { window });
        try
        {
            window.Show(); window.UpdateLayout(); indicator.Start();
            var old = FindOutline(window)!;
            window.Content = new Grid { Width = 50, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
            window.UpdateLayout(); indicator.Refresh();
            var outline = FindOutline(window)!;
            outline.Should().BeSameAs(old);
            outline.AdornedElement.Should().NotBeSameAs(window.Content);
            outline.RenderSize.Width.Should().BeGreaterThan(250);
            indicator.Dispose();
            FindOutline(window).Should().BeNull();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Inspector_outline_turns_orange_for_recording_and_returns_to_red_on_stop_or_clear() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        using var recorder = new WorkflowRecorder(new TreeWalker());
        try
        {
            window.Show(); window.UpdateLayout();
            recorder.ShowInspectionOutline(); window.UpdateLayout();
            var outline = FindOutline(window)!;
            AssertBorder(outline, Colors.Red);
            foreach (var end in new[] { "stop", "clear" })
            {
                recorder.Control("start", null); window.UpdateLayout();
                FindOutline(window).Should().BeSameAs(outline);
                AssertBorder(outline, Colors.Orange);
                recorder.Control(end, null); window.UpdateLayout();
                AssertBorder(outline, Colors.Red);
                recorder.Control("clear", null);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Inspector_shutdown_removes_the_outline_and_restart_restores_it() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        using var recorder = new WorkflowRecorder(new TreeWalker());
        try
        {
            window.Show(); window.UpdateLayout();
            recorder.ShowInspectionOutline(); window.UpdateLayout();
            var outline = FindOutline(window)!;
            recorder.ShowInspectionOutline();
            FindOutline(window).Should().BeSameAs(outline);
            recorder.Control("start", null); window.UpdateLayout();
            AssertBorder(outline, Colors.Orange);
            Task.Run(() => { recorder.Dispose(); recorder.Dispose(); }).GetAwaiter().GetResult();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            FindOutline(window).Should().BeNull();
            recorder.Active.Should().BeFalse();
            recorder.ShowInspectionOutline(); window.UpdateLayout();
            AssertBorder(FindOutline(window)!, Colors.Red);
        }
        finally { window.Close(); }
    });

    private static Window NewWindow() => new() { Title = "Recording outline test", Width = 320, Height = 240,
        Left = 40, Top = 40, ShowActivated = false, ShowInTaskbar = false, Content = new TextBox { Name = "Editor" } };

    [Fact]
    public Task Outline_covers_the_custom_title_bar_and_ignores_page_size_margin_and_transform() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        var decorator = new FrameworkElementFactory(typeof(AdornerDecorator));
        var surface = new FrameworkElementFactory(typeof(DockPanel));
        var title = new FrameworkElementFactory(typeof(Border));
        title.SetValue(FrameworkElement.HeightProperty, 40.0);
        title.SetValue(DockPanel.DockProperty, Dock.Top);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(20, 10, 20, 20));
        surface.AppendChild(title); surface.AppendChild(presenter); decorator.AppendChild(surface);
        window.Template = new ControlTemplate(typeof(Window)) { VisualTree = decorator };
        var content = new TextBox { Width = 80, Height = 30, Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = new ScaleTransform(1.25, 1.25) };
        window.Content = content;
        using var indicator = new RecordingIndicator(_ => { }, Colors.Red, () => new[] { window });
        try
        {
            window.Show(); window.UpdateLayout(); indicator.Start(); window.UpdateLayout();
            var outline = FindOutline(window)!;
            outline.AdornedElement.Should().BeOfType<DockPanel>();
            foreach (var width in new[] { 320.0, 460.0 })
            {
                window.Width = width; window.UpdateLayout();
                outline.RenderSize.Width.Should().BeApproximately(window.RenderSize.Width, 0.01);
                outline.RenderSize.Height.Should().BeApproximately(window.RenderSize.Height, 0.01);
                outline.TransformToAncestor(window).Transform(new Point()).Should().Be(new Point());
                content.ActualWidth.Should().BeApproximately(80, 0.01);
                AssertBorder(outline, Colors.Red);
            }

            window.ClearValue(Control.TemplateProperty); window.UpdateLayout(); indicator.Refresh();
            FindOutline(window).Should().NotBeNull().And.NotBeSameAs(outline);
            (outline.Layer.GetAdorners(outline.AdornedElement) ?? Array.Empty<Adorner>()).Should().NotContain(outline);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Does_not_use_a_small_page_adorner_layer_when_the_window_template_has_none() => DiagnosticReporterTests.OnSta(() =>
    {
        var window = NewWindow();
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        window.Template = new ControlTemplate(typeof(Window)) { VisualTree = presenter };
        var pageLayer = new AdornerDecorator { Width = 100, Height = 80,
            HorizontalAlignment = HorizontalAlignment.Center, Child = new TextBox() };
        window.Content = pageLayer;
        using var indicator = new RecordingIndicator(_ => { }, Colors.Red, () => new[] { window });
        try
        {
            window.Show(); window.UpdateLayout(); indicator.Start();
            AdornerLayer.GetAdornerLayer(pageLayer.Child).Should().NotBeNull();
            FindOutline(window).Should().BeNull();
            window.Content.Should().BeSameAs(pageLayer);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Skips_nonactivating_shadow_windows_but_keeps_ordinary_owned_dialogs() => DiagnosticReporterTests.OnSta(() =>
    {
        var owner = NewWindow();
        var dialog = NewWindow();
        var shadow = NewWindow();
        using var indicator = new RecordingIndicator(_ => { }, Colors.Red, () => new[] { owner, dialog, shadow });
        try
        {
            owner.Show();
            dialog.Owner = owner; dialog.Show();
            shadow.Owner = dialog;
            shadow.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(shadow).Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000);
            };
            shadow.Show();
            owner.UpdateLayout(); dialog.UpdateLayout(); shadow.UpdateLayout();
            indicator.Start(); indicator.Refresh();
            FindOutline(owner).Should().NotBeNull();
            FindOutline(dialog).Should().NotBeNull("ShowActivated=false alone does not make a dialog a shadow window");
            FindOutline(shadow).Should().BeNull();
        }
        finally { shadow.Close(); dialog.Close(); owner.Close(); }
    });

    private static RecordingIndicator.RecordingAdorner? FindOutline(Window window)
        => VisualDescendants(window).OfType<RecordingIndicator.RecordingAdorner>().SingleOrDefault();

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject element)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static void AssertBorder(Adorner outline, Color color)
    {
        int width = (int)Math.Ceiling(outline.RenderSize.Width), height = (int)Math.Ceiling(outline.RenderSize.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(outline);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        int edge = ((height / 2) * width + 1) * 4;
        pixels[edge].Should().Be(color.B);
        pixels[edge + 1].Should().Be(color.G);
        pixels[edge + 2].Should().Be(color.R);
        pixels[edge + 3].Should().Be(255);
        pixels[((height / 2) * width + width / 2) * 4 + 3].Should().Be(0, "the content remains unobscured");
    }

    private sealed class ApplicationAdorner : Adorner
    {
        internal ApplicationAdorner(UIElement content) : base(content) { }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
}
