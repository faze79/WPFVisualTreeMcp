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

    [Fact]
    public void InjectIntoProcess_WithValidManagedProcess_RequiresBootstrapperDll()
    {
        // This test verifies that injection is implemented and either:
        // - Throws FileNotFoundException if bootstrapper DLL is missing
        // - Proceeds to actual injection if bootstrapper is found (may fail for other reasons)

        // Arrange
        var process = Process.GetCurrentProcess();
        var dllPath = _injector.GetInspectorDllPath();

        // Skip if Inspector DLL doesn't exist (build required)
        if (!System.IO.File.Exists(dllPath))
        {
            return; // Skip test if Inspector DLL not built
        }

        var bootstrapperPath = _injector.GetBootstrapperDllPath();
        var bootstrapperExists = System.IO.File.Exists(bootstrapperPath);

        // Act & Assert
        var act = () => _injector.InjectIntoProcess(process.Id, dllPath);

        if (!bootstrapperExists)
        {
            // Bootstrapper not built: should throw FileNotFoundException
            act.Should().Throw<FileNotFoundException>(
                "Should throw FileNotFoundException for missing bootstrapper DLL")
                .WithMessage("*Bootstrapper*");
        }
        else
        {
            // Bootstrapper exists: injection will proceed but may fail (we're injecting into ourselves)
            // Just verify it doesn't throw NotImplementedException
            act.Should().NotThrow<NotImplementedException>(
                "Injection should be implemented, not stubbed");
        }
    }

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
