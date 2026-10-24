using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;
using TreeWalker = WpfVisualTreeMcp.Inspector.TreeWalker;

namespace WpfVisualTreeMcp.IntegrationTests;

public class AutomationIdTests
{
    private static Button Button(string id, Visibility visibility = Visibility.Visible)
    {
        var button = new Button { Name = "DifferentName", Content = "Different text", Visibility = visibility };
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    [Fact]
    public Task Resolves_only_the_exact_case_sensitive_id_including_the_root() => DiagnosticReporterTests.OnSta(() =>
    {
        var root = new StackPanel();
        var target = Button("Save.Order");
        root.Children.Add(Button("save.order"));
        root.Children.Add(Button("Save.Order.Copy"));
        root.Children.Add(target);
        AutomationIdResolver.Resolve("Save.Order", new[] { root }).Should().BeSameAs(target);
        AutomationIdResolver.Resolve("Save.Order", new[] { target, target }).Should().BeSameAs(target);
        var missing = () => AutomationIdResolver.Resolve("Save", new[] { root });
        missing.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    });

    [Fact]
    public Task Rejects_duplicates_even_if_hidden_and_supports_a_scoped_search() => DiagnosticReporterTests.OnSta(() =>
    {
        var left = new StackPanel();
        var right = new StackPanel();
        var target = Button("save");
        left.Children.Add(target);
        right.Children.Add(Button("save", Visibility.Collapsed));
        var ambiguous = () => AutomationIdResolver.Resolve("save", new[] { left, right });
        ambiguous.Should().Throw<InvalidOperationException>().WithMessage("*Ambiguous*root_handle*");
        AutomationIdResolver.Resolve("save", new[] { left }).Should().BeSameAs(target);
    });

    [Fact]
    public Task Searches_popup_children_and_deduplicates_overlapping_roots() => DiagnosticReporterTests.OnSta(() =>
    {
        var target = Button("popup.save");
        var popup = new Popup { Child = target };
        AutomationIdResolver.Resolve("popup.save", new DependencyObject[] { popup, target }).Should().BeSameAs(target);
    });

    [Fact]
    public Task Does_not_act_on_an_incomplete_search() => DiagnosticReporterTests.OnSta(() =>
    {
        var roots = Enumerable.Range(0, 10001).Select(_ => (DependencyObject)new Button()).ToArray();
        AutomationProperties.SetAutomationId(roots[0], "save");
        var overBudget = () => AutomationIdResolver.Resolve("save", roots);
        overBudget.Should().Throw<InvalidOperationException>().WithMessage("*uniqueness is unknown*");
    });

    [Fact]
    public Task Query_filters_combine_id_with_other_criteria_and_wait_can_find_it() => DiagnosticReporterTests.OnSta(() =>
    {
        var root = new StackPanel();
        var target = Button("save");
        root.Children.Add(target);
        root.Children.Add(Button("save.copy"));
        root.Children.Add(Button("SAVE"));
        var walker = new TreeWalker();
        var criteria = new FindCriteria { AutomationId = "save", TypeName = "Button", Text = "Different" };
        using var matches = JsonDocument.Parse(walker.FindElements(root, criteria));
        matches.RootElement.GetProperty("elements").GetArrayLength().Should().Be(1);
        walker.FindFirstMatch(criteria, root)!.Value.Handle.Should().Be(walker.GetOrCreateHandle(target));
        criteria.TypeName = "TextBox";
        using var mismatch = JsonDocument.Parse(walker.FindElementsDeep(root, criteria));
        mismatch.RootElement.GetProperty("elements").GetArrayLength().Should().Be(0);
    });
}
