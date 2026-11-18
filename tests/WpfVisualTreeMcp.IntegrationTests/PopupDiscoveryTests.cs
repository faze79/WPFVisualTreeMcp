using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;
using TreeWalker = WpfVisualTreeMcp.Inspector.TreeWalker;

namespace WpfVisualTreeMcp.IntegrationTests;

public class PopupDiscoveryTests
{
    [Fact]
    public Task Discovers_a_detached_context_menu_by_text_type_id_and_wait() => DiagnosticReporterTests.OnSta(() =>
    {
        var owner = new Button { Content = "Owner" };
        var window = CreateWindow(owner);
        var menu = new ContextMenu { PlacementTarget = owner, StaysOpen = true };
        menu.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        var command = new MenuItem { Header = "Inspect document" };
        AutomationProperties.SetAutomationId(command, "inspect-document");
        menu.Items.Add(command);
        try
        {
            window.Show();
            // Created dynamically: neither owner.ContextMenu nor a visual child references it.
            menu.IsOpen = true;
            Pump();
            command.IsVisible.Should().BeTrue();
            var walker = new TreeWalker();
            var roots = TreeWalker.GetAllSearchRoots();
            using var found = JsonDocument.Parse(walker.FindElements(roots, new FindCriteria
            {
                TypeName = "MenuItem", Text = "Inspect document", VisibleOnly = true
            }));
            found.RootElement.GetProperty("count").GetInt32().Should().Be(1);
            var handle = found.RootElement.GetProperty("elements")[0].GetProperty("handle").GetString()!;
            walker.ResolveHandle(handle).Should().BeSameAs(command);
            AutomationIdResolver.Resolve("inspect-document", roots).Should().BeSameAs(command);
            walker.FindFirstMatch(new FindCriteria { AutomationId = "inspect-document", VisibleOnly = true })
                .Should().NotBeNull().And.Be((handle, typeof(MenuItem).FullName!));
            var selectors = new StableSelectors();
            selectors.Resolve(selectors.Capture(command)).Should().BeSameAs(command);

            using var scoped = JsonDocument.Parse(walker.FindElements(owner, new FindCriteria { AutomationId = "inspect-document" }));
            scoped.RootElement.GetProperty("count").GetInt32().Should().Be(0, "an explicit visual subtree must not search unrelated popup roots");

            menu.IsOpen = false;
            PumpUntil(() => !command.IsVisible);
            walker.FindFirstMatch(new FindCriteria { AutomationId = "inspect-document", VisibleOnly = true }).Should().BeNull();
        }
        finally { menu.IsOpen = false; window.Close(); }
    });

    [Fact]
    public Task Finds_each_element_once_when_popup_is_reachable_from_multiple_roots() => DiagnosticReporterTests.OnSta(() =>
    {
        var popupChild = new StackPanel();
        var first = new Button { Content = "First" };
        var second = new Button { Content = "Second" };
        AutomationProperties.SetAutomationId(first, "first");
        AutomationProperties.SetAutomationId(second, "second");
        popupChild.Children.Add(first);
        popupChild.Children.Add(second);
        var popup = new Popup { Child = popupChild, StaysOpen = true };
        var owner = new Grid();
        owner.Children.Add(popup);
        var window = CreateWindow(owner);
        try
        {
            window.Show();
            popup.PlacementTarget = owner;
            popup.IsOpen = true;
            Pump();
            var roots = new DependencyObject[] { window, popupChild, second };
            var walker = new TreeWalker();
            using var found = JsonDocument.Parse(walker.FindElements(roots, new FindCriteria { TypeName = "Button", VisibleOnly = true }, 2));
            found.RootElement.GetProperty("count").GetInt32().Should().Be(2);
            var ids = found.RootElement.GetProperty("elements").EnumerateArray()
                .Select(element => element.GetProperty("automationId").GetString()).ToArray();
            ids.Should().Equal("first", "second");
            using var deep = JsonDocument.Parse(walker.FindElementsDeep(roots, new FindCriteria { TypeName = "Button", VisibleOnly = true }));
            deep.RootElement.GetProperty("count").GetInt32().Should().Be(2);
            deep.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
            using var limited = JsonDocument.Parse(walker.FindElementsDeep(roots, new FindCriteria { TypeName = "Button" }, 1));
            limited.RootElement.GetProperty("count").GetInt32().Should().Be(1);
            limited.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
            AutomationIdResolver.Resolve("first", TreeWalker.GetAllSearchRoots()).Should().BeSameAs(first);
        }
        finally { popup.IsOpen = false; window.Close(); }
    });

    [Fact]
    public Task Discovers_standalone_presentation_sources_without_an_application() => DiagnosticReporterTests.OnSta(() =>
    {
        var target = new Button { Content = "Hosted control" };
        AutomationProperties.SetAutomationId(target, "hosted-control");
        using var source = new HwndSource(new HwndSourceParameters("PopupDiscoveryTests") { Width = 100, Height = 100 });
        source.RootVisual = target;
        var roots = TreeWalker.GetAllSearchRoots();
        roots.Should().Contain(target);
        roots.Count(root => ReferenceEquals(root, target)).Should().Be(1);
        AutomationIdResolver.Resolve("hosted-control", roots).Should().BeSameAs(target);
        source.Dispose();
        TreeWalker.GetAllSearchRoots().Should().NotContain(target);
    });

    [Fact]
    public async Task Excludes_sources_owned_by_another_dispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var source = new HwndSource(new HwndSourceParameters("ForeignPopupDiscoveryTests"));
                var target = new Button();
                AutomationProperties.SetAutomationId(target, "foreign-control");
                source.RootVisual = target;
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
                stopped.SetResult();
            }
            catch (Exception error) { ready.TrySetException(error); stopped.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var dispatcher = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await DiagnosticReporterTests.OnSta(() =>
            {
                using var local = new HwndSource(new HwndSourceParameters("LocalPopupDiscoveryTests"));
                var target = new Button();
                local.RootVisual = target;
                var roots = TreeWalker.GetAllSearchRoots();
                roots.Should().Contain(target);
                roots.Should().OnlyContain(root => root.Dispatcher.CheckAccess());
                AutomationIdResolver.ResolveOrDefault("foreign-control", roots).Should().BeNull();
            });
        }
        finally
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Discovers_dropdown_and_tooltip_content(bool dropdown) => DiagnosticReporterTests.OnSta(() =>
    {
        var target = new TextBlock { Text = "Detached content" };
        AutomationProperties.SetAutomationId(target, "detached-content");
        var combo = new ComboBox();
        if (dropdown) combo.Items.Add(new ComboBoxItem { Content = target });
        var tooltip = new ToolTip { Content = dropdown ? null : target, StaysOpen = true };
        var window = CreateWindow(combo);
        try
        {
            window.Show();
            if (dropdown) combo.IsDropDownOpen = true;
            else { tooltip.PlacementTarget = combo; tooltip.IsOpen = true; }
            Pump();
            var walker = new TreeWalker();
            using var found = JsonDocument.Parse(walker.FindElementsDeep(TreeWalker.GetAllSearchRoots(),
                new FindCriteria { AutomationId = "detached-content", VisibleOnly = true }));
            found.RootElement.GetProperty("count").GetInt32().Should().Be(1);
            var handle = found.RootElement.GetProperty("elements")[0].GetProperty("handle").GetString()!;
            walker.ResolveHandle(handle).Should().BeSameAs(target);
        }
        finally { combo.IsDropDownOpen = false; tooltip.IsOpen = false; window.Close(); }
    });

    [Fact]
    public Task Rejects_duplicate_ids_across_a_window_and_a_detached_popup() => DiagnosticReporterTests.OnSta(() =>
    {
        var original = new Button();
        var duplicate = new Button();
        AutomationProperties.SetAutomationId(original, "shared-id");
        AutomationProperties.SetAutomationId(duplicate, "shared-id");
        var window = CreateWindow(original);
        var popup = new Popup { Child = duplicate, StaysOpen = true, PlacementTarget = original };
        try
        {
            window.Show();
            popup.IsOpen = true;
            Pump();
            var resolve = () => AutomationIdResolver.Resolve("shared-id", TreeWalker.GetAllSearchRoots());
            resolve.Should().Throw<InvalidOperationException>().WithMessage("*Ambiguous*root_handle*");
            AutomationIdResolver.Resolve("shared-id", new[] { original }).Should().BeSameAs(original);
        }
        finally { popup.IsOpen = false; window.Close(); }
    });

    private static Window CreateWindow(UIElement content) => new()
    {
        Content = content, Width = 300, Height = 200,
        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow
    };

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || clock.Elapsed > TimeSpan.FromSeconds(2)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        condition().Should().BeTrue("popup teardown must finish before checking hidden state");
    }
}
