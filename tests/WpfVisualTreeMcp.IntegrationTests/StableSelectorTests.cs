using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class StableSelectorTests
{
    [Fact]
    public Task Resolves_a_new_instance_by_automation_id_and_rejects_ambiguous_matches() => DiagnosticReporterTests.OnSta(() =>
    {
        var root = new StackPanel();
        var original = new Button { Content = "Save", Name = "OriginalName" };
        AutomationProperties.SetAutomationId(original, "save");
        root.Children.Add(original);
        var selectors = new StableSelectors(() => new[] { root });
        var selector = selectors.Capture(original);
        selector.AutomationId.Should().Be("save");
        root.Children.Clear();
        var replacement = new Button { Content = "Save changed", Name = "DifferentName" };
        AutomationProperties.SetAutomationId(replacement, "save");
        root.Children.Add(replacement);
        selectors.Resolve(selector).Should().BeSameAs(replacement);
        var duplicate = new Button();
        AutomationProperties.SetAutomationId(duplicate, "save");
        root.Children.Add(duplicate);
        var ambiguous = () => selectors.Resolve(selector);
        ambiguous.Should().Throw<InvalidOperationException>().WithMessage("*Ambiguous*");
        root.Children.Clear();
        var missing = () => selectors.Resolve(selector);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    });

    [Fact]
    public Task Records_an_unnamed_template_part_under_a_named_ancestor() => DiagnosticReporterTests.OnSta(() =>
    {
        var root = new StackPanel();
        var left = new StackPanel { Name = "LeftSlider" };
        var right = new StackPanel { Name = "RightSlider" };
        var leftThumb = new Thumb();
        var rightThumb = new Thumb();
        left.Children.Add(leftThumb); right.Children.Add(rightThumb);
        root.Children.Add(left); root.Children.Add(right);
        var selectors = new StableSelectors(() => new[] { root });
        var selector = selectors.Capture(leftThumb);
        selector.Ancestor.Should().NotBeNull();
        selector.Ancestor!.Name.Should().Be("LeftSlider");
        selector.TypeName.Should().Be(typeof(Thumb).FullName);
        left.Children.Clear();
        var fresh = new Thumb();
        left.Children.Add(fresh);
        selectors.Resolve(selector).Should().BeSameAs(fresh);
        selectors.Resolve(new ElementSelector { TypeName = typeof(Button).FullName!, Name = "Missing" }, true).Should().BeNull();
    });
}
