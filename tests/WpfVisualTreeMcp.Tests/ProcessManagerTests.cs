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
    public async Task GetWpfProcessesAsync_ReturnsNonNullList()
    {
        // Act
        var processes = await _processManager.GetWpfProcessesAsync();

        // Assert - just verify it doesn't throw and returns a list
        processes.Should().NotBeNull();
    }

    [Fact]
    public void CurrentSession_IsNullInitially()
    {
        // Assert
        _processManager.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task AttachToProcessAsync_WithNeitherIdNorName_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _processManager.AttachToProcessAsync(null, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AttachToProcessAsync_WithNonExistentProcessId_ThrowsInvalidOperationException()
    {
        // Use a very high process ID that's unlikely to exist
        var invalidProcessId = int.MaxValue - 1;

        // Act & Assert
        var act = () => _processManager.AttachToProcessAsync(invalidProcessId, null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task AttachToProcessAsync_WithNonExistentProcessName_ThrowsInvalidOperationException()
    {
        // Use a process name that definitely doesn't exist
        var invalidProcessName = "ThisProcessDefinitelyDoesNotExist_12345";

        // Act & Assert
        var act = () => _processManager.AttachToProcessAsync(null, invalidProcessName);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task DetachAsync_WithNonMatchingSession_DoesNotThrow()
    {
        // Act & Assert - should not throw
        await _processManager.DetachAsync("non-existent-session");
        _processManager.CurrentSession.Should().BeNull();
    }
}

public class WpfProcessInfoTests
{
    [Fact]
    public void WpfProcessInfo_HasCorrectProperties()
    {
        // Arrange & Act
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

    [Fact]
    public void WpfProcessInfo_DefaultValues()
    {
        // Arrange & Act
        var processInfo = new WpfProcessInfo();

        // Assert
        processInfo.ProcessId.Should().Be(0);
        processInfo.ProcessName.Should().BeEmpty();
        processInfo.MainWindowTitle.Should().BeNull();
        processInfo.IsAttached.Should().BeFalse();
        processInfo.DotNetVersion.Should().BeNull();
    }
}

public class InspectionSessionTests
{
    [Fact]
    public void InspectionSession_HasCorrectProperties()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
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

    [Fact]
    public void InspectionSession_DefaultValues()
    {
        // Arrange & Act
        var session = new InspectionSession();

        // Assert
        session.SessionId.Should().BeEmpty();
        session.ProcessId.Should().Be(0);
        session.MainWindowHandle.Should().BeEmpty();
        session.AttachedAt.Should().Be(default);
    }
}
