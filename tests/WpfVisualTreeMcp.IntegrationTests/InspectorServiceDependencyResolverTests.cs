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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ShouldResolvePrivateDependency_UnknownRequester_DependsOnInspectorScope(
        bool allowUnknownRequester, bool expected)
    {
        var result = InspectorService.ShouldResolvePrivateDependency(
            "System.Runtime.CompilerServices.Unsafe",
            null,
            null,
            InspectorDirectory,
            allowUnknownRequester);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task StartServerTask_InsideDependencyResolutionScope_DoesNotInheritScope()
    {
        var inheritedScope = true;
        using (InspectorService.EnterPrivateDependencyResolutionScope())
        {
            await IpcServer.StartServerTask(() =>
            {
                inheritedScope = InspectorService.IsPrivateDependencyResolutionScopeActive;
                return Task.CompletedTask;
            });
            InspectorService.IsPrivateDependencyResolutionScopeActive.Should().BeTrue();
        }

        inheritedScope.Should().BeFalse();
    }
}
