using FluentAssertions;
using System.Diagnostics;
using WpfVisualTreeMcp.Injector;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class ProcessInjectorTests
{
    private readonly ProcessInjector _injector;

    public ProcessInjectorTests()
    {
        _injector = new ProcessInjector();
    }

    [Fact]
    public void GetInspectorDllPath_ReturnsValidPath()
    {
        // Act
        var path = _injector.GetInspectorDllPath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith("WpfVisualTreeMcp.Inspector.dll");
    }

    [Fact]
    public void IsManagedProcess_WithCurrentProcess_ReturnsTrue()
    {
        // Arrange - current process is a .NET process
        var process = Process.GetCurrentProcess();

        // Act
        var result = _injector.IsManagedProcess(process);

        // Assert - current test process should be managed
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInspectorLoaded_WithCurrentProcess_ReturnsFalse()
    {
        // Arrange - current process doesn't have Inspector loaded
        var process = Process.GetCurrentProcess();

        // Act
        var result = _injector.IsInspectorLoaded(process);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void InjectIntoProcess_WithNonExistentDll_ThrowsFileNotFoundException()
    {
        // Arrange
        var processId = Process.GetCurrentProcess().Id;
        var fakeDllPath = @"C:\NonExistent\Fake.dll";

        // Act & Assert
        var act = () => _injector.InjectIntoProcess(processId, fakeDllPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void InjectIntoProcess_WithInvalidProcessId_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidProcessId = int.MaxValue - 1;
        var dllPath = _injector.GetInspectorDllPath();

        // Act & Assert
        var act = () => _injector.InjectIntoProcess(invalidProcessId, dllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // Real injection is exercised against isolated WPF processes by
    // scripts/Test-Injection.ps1, never against the xUnit host itself.

    [Fact]
    public void GetBootstrapperDllPath_ReturnsValidPath()
    {
        // Act
        var path = _injector.GetBootstrapperDllPath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith("WpfInspectorBootstrapper.dll");
    }
}
