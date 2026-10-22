using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class HeaderSearchTests
{
    [Fact]
    public Task Composite_header_identifies_a_tab_without_including_its_page() => DiagnosticReporterTests.OnSta(() =>
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Impostazioni" });
        var tab = new TabItem { Header = header, Content = "Unrelated page body" };
        TreeWalker.GetSearchableText(tab).Should().Be("Impostazioni");
    });

    [Fact]
    public Task Custom_backstage_header_is_read_without_a_realized_template() => DiagnosticReporterTests.OnSta(() =>
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Recenti" });
        var tab = new CustomBackstageTab { Header = header, Content = "Unrelated page body" };
        TreeWalker.GetSearchableText(tab).Should().Be("Recenti");
        tab.Header = "Aiuto";
        TreeWalker.GetSearchableText(tab).Should().Be("Aiuto");
    });

    [Fact]
    public Task A_failing_custom_header_does_not_break_search() => DiagnosticReporterTests.OnSta(() =>
    {
        TreeWalker.GetSearchableText(new FailingHeader { Content = "Fallback" }).Should().Be("Fallback");
    });

    [Fact]
    public Task Bound_inline_header_text_is_searchable() => DiagnosticReporterTests.OnSta(() =>
    {
        var run = new Run();
        run.SetBinding(Run.TextProperty, new Binding("Caption") { Source = new { Caption = "Settings" }, Mode = BindingMode.OneWay });
        var label = new TextBlock();
        label.Inlines.Add(new Span(run));
        var header = new StackPanel();
        header.Children.Add(label);
        TreeWalker.GetSearchableText(new CustomBackstageTab { Header = header }).Should().Be("Settings");
    });

    private sealed class CustomBackstageTab : ContentControl
    {
        public object? Header { get; set; }
    }

    private sealed class FailingHeader : ContentControl
    {
        public object Header => throw new InvalidOperationException("Header unavailable");
    }
}
