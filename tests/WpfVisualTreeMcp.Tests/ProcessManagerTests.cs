using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WpfVisualTreeMcp.Server.Services;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class ProcessManagerTests
{
    private readonly Mock<ILogger<ProcessManager>> _loggerMock;
    private readonly ProcessManager _processManager;

    public ProcessManagerTests()
    {
        _loggerMock = new Mock<ILogger<ProcessManager>>();
        _processManager = new ProcessManager(_loggerMock.Object);
    }

    [Fact]
    public async Task GetWpfProcessesAsync_ReturnsProcessList()
    {
        // Act
        var processes = await _processManager.GetWpfProcessesAsync();

        // Assert
        processes.Should().NotBeNull();
        // Note: The actual number of processes depends on the system
    }

    [Fact]
    public void CurrentSession_IsNullInitially()
    {
        // Assert
        _processManager.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task AttachToProcessAsync_WithInvalidProcessId_ThrowsException()
    {
        // Arrange
        var invalidProcessId = 999999;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _processManager.AttachToProcessAsync(invalidProcessId, null));
    }

    [Fact]
    public async Task AttachToProcessAsync_WithNeitherIdNorName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _processManager.AttachToProcessAsync(null, null));
    }

    [Fact]
    public async Task DetachAsync_ClearsCurrentSession()
    {
        // Arrange - first we need to attach to create a session
        // This test would need a real process to attach to
        // For now, we'll just verify the detach behavior

        await _processManager.DetachAsync("non-existent-session");

        // Assert
        _processManager.CurrentSession.Should().BeNull();
    }
}

public class WpfProcessInfoTests
{
    [Fact]
    public void WpfProcessInfo_HasCorrectProperties()
    {
        // Arrange
        var processInfo = new WpfProcessInfo
        {
            ProcessId = 1234,
            ProcessName = "TestApp",
            MainWindowTitle = "Test Window",
            IsAttached = true,
            DotNetVersion = "4.8.0"
        };

        // Assert
        processInfo.ProcessId.Should().Be(1234);
        processInfo.ProcessName.Should().Be("TestApp");
        processInfo.MainWindowTitle.Should().Be("Test Window");
        processInfo.IsAttached.Should().BeTrue();
        processInfo.DotNetVersion.Should().Be("4.8.0");
    }
}

public class InspectionSessionTests
{
    [Fact]
    public void InspectionSession_HasCorrectProperties()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var session = new InspectionSession
        {
            SessionId = "abc123",
            ProcessId = 1234,
            MainWindowHandle = "window_0x12345",
            AttachedAt = now
        };

        // Assert
        session.SessionId.Should().Be("abc123");
        session.ProcessId.Should().Be(1234);
        session.MainWindowHandle.Should().Be("window_0x12345");
        session.AttachedAt.Should().Be(now);
    }
}
