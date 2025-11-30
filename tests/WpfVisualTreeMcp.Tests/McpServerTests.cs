using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class McpServerTests
{
    private readonly Mock<ILogger<McpServer>> _loggerMock;
    private readonly Mock<IProcessManager> _processManagerMock;
    private readonly Mock<IIpcBridge> _ipcBridgeMock;

    public McpServerTests()
    {
        _loggerMock = new Mock<ILogger<McpServer>>();
        _processManagerMock = new Mock<IProcessManager>();
        _ipcBridgeMock = new Mock<IIpcBridge>();
    }

    private McpServer CreateServer() => new McpServer(
        _loggerMock.Object,
        _processManagerMock.Object,
        _ipcBridgeMock.Object);

    [Fact]
    public async Task Initialize_ReturnsProtocolVersion()
    {
        // Arrange
        var request = CreateJsonRpcRequest("initialize", 1, new { });

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var result = response.RootElement.GetProperty("result");
        result.GetProperty("protocolVersion").GetString().Should().Be("2024-11-05");
        result.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("wpf-visual-tree");
    }

    [Fact]
    public async Task ToolsList_ReturnsAllTools()
    {
        // Arrange
        var request = CreateJsonRpcRequest("tools/list", 1, null);

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var tools = response.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterOrEqualTo(5);

        // Verify required tools are present
        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        toolNames.Should().Contain("wpf_list_processes");
        toolNames.Should().Contain("wpf_attach");
        toolNames.Should().Contain("wpf_get_visual_tree");
        toolNames.Should().Contain("wpf_get_element_properties");
        toolNames.Should().Contain("wpf_find_elements");
    }

    [Fact]
    public async Task ToolCall_ListProcesses_ReturnsProcessList()
    {
        // Arrange
        var expectedProcesses = new List<WpfProcessInfo>
        {
            new WpfProcessInfo
            {
                ProcessId = 1234,
                ProcessName = "TestApp",
                MainWindowTitle = "Test Window",
                IsAttached = false
            }
        };

        _processManagerMock
            .Setup(x => x.GetWpfProcessesAsync())
            .ReturnsAsync(expectedProcesses);

        var request = CreateJsonRpcRequest("tools/call", 1, new
        {
            name = "wpf_list_processes",
            arguments = new { }
        });

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var content = response.RootElement.GetProperty("result").GetProperty("content");
        content.GetArrayLength().Should().Be(1);

        var text = content[0].GetProperty("text").GetString();
        text.Should().Contain("1234");
        text.Should().Contain("TestApp");
    }

    [Fact]
    public async Task ToolCall_Attach_AttachesToProcess()
    {
        // Arrange
        var expectedSession = new InspectionSession
        {
            SessionId = "test-session",
            ProcessId = 1234,
            MainWindowHandle = "window_0x12345",
            AttachedAt = DateTime.UtcNow
        };

        _processManagerMock
            .Setup(x => x.AttachToProcessAsync(1234, null))
            .ReturnsAsync(expectedSession);

        var request = CreateJsonRpcRequest("tools/call", 1, new
        {
            name = "wpf_attach",
            arguments = new { process_id = 1234 }
        });

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var content = response.RootElement.GetProperty("result").GetProperty("content");
        var text = content[0].GetProperty("text").GetString();

        text.Should().Contain("success");
        text.Should().Contain("1234");
        text.Should().Contain("test-session");
    }

    [Fact]
    public async Task ToolCall_WithMissingRequiredParameter_ReturnsError()
    {
        // Arrange
        var request = CreateJsonRpcRequest("tools/call", 1, new
        {
            name = "wpf_get_element_properties",
            arguments = new { } // Missing required element_handle
        });

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var content = response.RootElement.GetProperty("result").GetProperty("content");
        content[0].GetProperty("text").GetString().Should().Contain("Error");

        var isError = response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean();
        isError.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        // Arrange
        var request = CreateJsonRpcRequest("unknown/method", 1, null);

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var error = response.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(-32601);
        error.GetProperty("message").GetString().Should().Contain("Method not found");
    }

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        // Arrange
        var request = CreateJsonRpcRequest("ping", 1, null);

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.RootElement.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ToolCall_GetVisualTree_ReturnsTree()
    {
        // Arrange
        var expectedTree = new VisualTreeResult
        {
            Root = new VisualTreeNode
            {
                Handle = "root",
                TypeName = "Window",
                Name = "MainWindow"
            },
            TotalElements = 1,
            MaxDepthReached = false
        };

        _ipcBridgeMock
            .Setup(x => x.GetVisualTreeAsync(null, 10))
            .ReturnsAsync(expectedTree);

        _processManagerMock
            .SetupGet(x => x.CurrentSession)
            .Returns(new InspectionSession { SessionId = "test", ProcessId = 1 });

        var request = CreateJsonRpcRequest("tools/call", 1, new
        {
            name = "wpf_get_visual_tree",
            arguments = new { }
        });

        // Act
        var response = await SendRequestAsync(request);

        // Assert
        response.Should().NotBeNull();
        var content = response.RootElement.GetProperty("result").GetProperty("content");
        var text = content[0].GetProperty("text").GetString();
        text.Should().Contain("root");
        text.Should().Contain("MainWindow");
    }

    private static string CreateJsonRpcRequest(string method, int id, object? @params)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = id,
            method = method,
            @params = @params
        };

        return JsonSerializer.Serialize(request);
    }

    private async Task<JsonDocument> SendRequestAsync(string request)
    {
        var server = CreateServer();

        // Create input with request
        var inputBytes = Encoding.UTF8.GetBytes(request + "\n");
        using var inputStream = new MemoryStream(inputBytes);
        using var outputStream = new MemoryStream();

        // Run server - it will exit when input stream ends (returns null from ReadLineAsync)
        await server.RunAsync(inputStream, outputStream);

        // Read response
        outputStream.Position = 0;
        using var reader = new StreamReader(outputStream);
        var responseText = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new InvalidOperationException("No response received from server");
        }

        // Get the first non-empty line (response)
        var firstLine = responseText.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (firstLine == null)
        {
            throw new InvalidOperationException("No valid response line found");
        }

        return JsonDocument.Parse(firstLine);
    }
}
