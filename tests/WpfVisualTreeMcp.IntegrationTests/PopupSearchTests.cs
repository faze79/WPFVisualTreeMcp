using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;
using TreeWalker = WpfVisualTreeMcp.Inspector.TreeWalker;

namespace WpfVisualTreeMcp.IntegrationTests;

public class PopupSearchTests
{
    [Fact]
    public Task Finds_menu_items_by_type_text_name_and_id_without_expanding_scoped_search() => DiagnosticReporterTests.OnSta(() =>
    {
        var item = new MenuItem { Header = "Save document", Name = "SaveCommand" };
        AutomationProperties.SetAutomationId(item, "context.save");
        var menu = new ContextMenu { Items = { item } };
        WithOpenMenu(menu, window =>
        {
            var walker = new TreeWalker();
            var criteria = new FindCriteria { TypeName = "MenuItem", Text = "Save document",
                ElementName = "SaveCommand", AutomationId = "context.save", VisibleOnly = true };
            var roots = TreeWalker.GetAllSearchRoots();
            AssertHandles(walker.FindElements(roots, criteria), walker.GetOrCreateHandle(item));
            AssertHandles(walker.FindElementsDeep(roots, criteria), walker.GetOrCreateHandle(item));
            AssertHandles(walker.FindElements(window, criteria));
            AssertHandles(walker.FindElementsDeep(window, criteria));
            AssertHandles(walker.FindElements(menu, criteria), walker.GetOrCreateHandle(item));
            AutomationIdResolver.Resolve("context.save", roots).Should().BeSameAs(item);
            menu.IsOpen = false;
            FlushLayout(window);
            AssertHandles(walker.FindElements(TreeWalker.GetAllSearchRoots(), criteria));
            AssertHandles(walker.FindElementsDeep(TreeWalker.GetAllSearchRoots(), criteria));
        });
    });

    [Fact]
    public Task Deduplicates_popup_content_across_roots_before_applying_result_limit() => DiagnosticReporterTests.OnSta(() =>
    {
        var target = new Button { Name = "SearchPopupTarget" };
        var popup = new Popup { Child = target, StaysOpen = true };
        var panel = new StackPanel { Children = { popup } };
        var sibling = new Button { Name = "SearchWindowTarget" };
        panel.Children.Add(sibling);
        WithOpenPopup(popup, panel, window =>
        {
            var walker = new TreeWalker();
            var roots = TreeWalker.GetAllSearchRoots();
            roots.Contains(window).Should().BeTrue();
            roots.Contains(PresentationSource.FromVisual(target)!.RootVisual).Should().BeTrue();
            var overlapping = roots.Concat(new DependencyObject[] { popup, target, window });
            var criteria = new FindCriteria { TypeName = "Button", ElementName = "Search", VisibleOnly = true };
            var handles = new[] { walker.GetOrCreateHandle(target), walker.GetOrCreateHandle(sibling) };
            AssertHandles(walker.FindElements(overlapping, criteria, 2), handles);
            AssertHandles(walker.FindElementsDeep(overlapping, criteria), handles);
            AssertHandles(walker.FindElementsDeep(overlapping, criteria, 2), handles);
        });
    });

    [Fact]
    public async Task Skips_presentation_sources_owned_by_other_dispatchers()
    {
        using var release = new ManualResetEventSlim();
        var ready = new TaskCompletionSource<DependencyObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = DiagnosticReporterTests.OnSta(() =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Foreign search root")
                { Width = 100, Height = 100, WindowStyle = 0 }) { RootVisual = new Border() };
            TreeWalker.GetAllSearchRoots().Contains(source.RootVisual).Should().BeTrue();
            ready.SetResult(source.RootVisual);
            release.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        });
        try
        {
            var foreignRoot = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await DiagnosticReporterTests.OnSta(() =>
                TreeWalker.GetAllSearchRoots().Contains(foreignRoot).Should().BeFalse());
        }
        finally { release.Set(); await owner; }
    }

    [Fact]
    public Task Discovers_context_menu_and_submenu_presentation_roots() => DiagnosticReporterTests.OnSta(() =>
    {
        var nested = new MenuItem { Header = "Nested command" };
        var parent = new MenuItem { Header = "More", Items = { nested } };
        var menu = new ContextMenu { Items = { parent } };
        WithOpenMenu(menu, window =>
        {
            AssertDiscovered(parent);
            parent.IsSubmenuOpen = true;
            FlushLayout(menu);
            AssertDiscovered(nested);
            parent.IsSubmenuOpen = false;
            menu.IsOpen = false;
            FlushLayout(window);
            new TreeWalker().FindFirstMatch(new FindCriteria { TypeName = "MenuItem", Text = "Nested command", VisibleOnly = true })
                .Should().BeNull();
        });
    });

    private static void AssertDiscovered(MenuItem item)
    {
        var source = PresentationSource.FromVisual(item);
        source.Should().NotBeNull();
        TreeWalker.GetAllSearchRoots().Contains(source!.RootVisual).Should().BeTrue();
        var walker = new TreeWalker();
        var criteria = new FindCriteria { TypeName = "MenuItem", Text = (string)item.Header, VisibleOnly = true };
        AssertHandles(walker.FindElements(TreeWalker.GetAllSearchRoots(), criteria), walker.GetOrCreateHandle(item));
        AssertHandles(walker.FindElementsDeep(TreeWalker.GetAllSearchRoots(), criteria), walker.GetOrCreateHandle(item));
        var match = new TreeWalker().FindFirstMatch(new FindCriteria
        {
            TypeName = "MenuItem", Text = (string)item.Header, VisibleOnly = true
        });
        match.Should().NotBeNull();
    }

    private static void AssertHandles(string json, params string[] expected)
    {
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("count").GetInt32().Should().Be(expected.Length);
        document.RootElement.GetProperty("elements").EnumerateArray()
            .Select(element => element.GetProperty("handle").GetString()).Should().BeEquivalentTo(expected);
    }

    private static void WithOpenPopup(Popup popup, FrameworkElement content, Action<Window> assertion)
    {
        var window = new Window { Content = content, Width = 240, Height = 160, ShowInTaskbar = false };
        try
        {
            window.Show();
            popup.PlacementTarget = content;
            popup.IsOpen = true;
            FlushLayout(content);
            assertion(window);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    }

    private static void WithOpenMenu(ContextMenu menu, Action<Window> assertion)
    {
        var owner = new Button { Content = "Open menu", ContextMenu = menu };
        var window = new Window { Content = owner, Width = 240, Height = 160, ShowInTaskbar = false };
        try
        {
            window.Show();
            menu.PlacementTarget = owner;
            menu.IsOpen = true;
            ((Popup)menu.Parent).PopupAnimation = PopupAnimation.None;
            FlushLayout(menu);
            assertion(window);
        }
        finally
        {
            menu.IsOpen = false;
            window.Close();
        }
    }

    private static void FlushLayout(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
