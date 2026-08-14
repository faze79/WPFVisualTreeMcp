using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class InspectorServiceDependencyResolverTests
{
    private static readonly string InspectorDirectory = Path.GetDirectoryName(
        typeof(InspectorService).Assembly.Location)!;

    [Fact]
    public void ShouldResolvePrivateDependency_ApplicationRequesterInPayloadDirectory_ReturnsFalse()
    {
        var result = InspectorService.ShouldResolvePrivateDependency(
            "System.Text.Json",
            "SampleWpfApp",
            InspectorDirectory,
            InspectorDirectory);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldResolvePrivateDependency_InspectorRequesterInPayloadDirectory_ReturnsTrue()
    {
        var result = InspectorService.ShouldResolvePrivateDependency(
            "System.Text.Json",
            "WpfVisualTreeMcp.Inspector",
            InspectorDirectory,
            InspectorDirectory);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldResolvePrivateDependency_PrivateDependencyRequesterInPayloadDirectory_ReturnsTrue()
    {
        var result = InspectorService.ShouldResolvePrivateDependency(
            "System.Memory",
            "System.Text.Json",
            InspectorDirectory,
            InspectorDirectory);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldResolvePrivateDependency_InspectorRequesterOutsidePayloadDirectory_ReturnsFalse()
    {
        var result = InspectorService.ShouldResolvePrivateDependency(
            "System.Text.Json",
            "WpfVisualTreeMcp.Inspector",
            Path.Combine(InspectorDirectory, "other"),
            InspectorDirectory);

        result.Should().BeFalse();
    }
}
