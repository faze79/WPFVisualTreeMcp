using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class PropertyReaderTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    public Task Real_Wpf_defaults_and_fractional_layout_produce_valid_JSON(string culture) => DiagnosticReporterTests.OnSta(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var button = new Button { Name = "AuditButton", Width = 123.5, Margin = new Thickness(1.25), Content = "a\b\f\u0001\"\\\n" };
        button.SetBinding(UIElement.IsEnabledProperty, new Binding("Enabled") { Source = new { Enabled = true } });
        button.Measure(new Size(500, 500));
        button.Arrange(new Rect(0, 0, 200.5, 50.25));
        var reader = new PropertyReader();
        using var document = JsonDocument.Parse(reader.GetProperties(button));
        var values = document.RootElement.GetProperty("properties").EnumerateArray().GroupBy(p => p.GetProperty("name").GetString()!).ToDictionary(g => g.Key, g => g.First());
        values["Height"].GetProperty("value").GetString().Should().Be("NaN");
        values["MaxHeight"].GetProperty("value").GetString().Should().Be("Infinity");
        values["Width"].GetProperty("value").GetDouble().Should().Be(123.5);
        values["Width"].GetProperty("ownerType").GetString().Should().Be(typeof(FrameworkElement).FullName);
        values["IsEnabled"].GetProperty("value").GetBoolean().Should().BeTrue();
        values["IsEnabled"].GetProperty("isBinding").GetBoolean().Should().BeTrue();
        values["Content"].GetProperty("value").GetString().Should().Be((string)button.Content);
        using var layout = JsonDocument.Parse(reader.GetLayoutInfo(button));
        layout.RootElement.GetProperty("layout").GetProperty("margin").GetProperty("left").GetDouble().Should().Be(1.25);
    });
}
