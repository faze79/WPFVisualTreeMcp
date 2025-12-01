using FluentAssertions;
using Moq;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

/// <summary>
/// Tests for WpfTools - the MCP tool implementations.
/// After migrating to the official MCP SDK, we test the tool class directly
/// rather than the protocol layer (which is handled by the SDK).
/// </summary>
public class WpfToolsTests
{
    private readonly Mock<IProcessManager> _processManagerMock;
    private readonly Mock<IIpcBridge> _ipcBridgeMock;
    private readonly WpfTools _tools;

    public WpfToolsTests()
    {
        _processManagerMock = new Mock<IProcessManager>();
        _ipcBridgeMock = new Mock<IIpcBridge>();
        _tools = new WpfTools(_processManagerMock.Object, _ipcBridgeMock.Object);
    }

    [Fact]
    public async Task WpfListProcesses_ReturnsProcessList()
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

        // Act
        var result = await _tools.WpfListProcesses();

        // Assert
        result.Should().NotBeNull();
        var resultType = result.GetType();
        var processesProperty = resultType.GetProperty("processes");
        processesProperty.Should().NotBeNull();
    }

    [Fact]
    public async Task WpfAttach_WithProcessId_AttachesToProcess()
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

        // Act
        var result = await _tools.WpfAttach(process_id: 1234);

        // Assert
        result.Should().NotBeNull();
        _processManagerMock.Verify(x => x.AttachToProcessAsync(1234, null), Times.Once);
    }

    [Fact]
    public async Task WpfAttach_WithProcessName_AttachesToProcess()
    {
        // Arrange
        var expectedSession = new InspectionSession
        {
            SessionId = "test-session",
            ProcessId = 5678,
            MainWindowHandle = "window_0x56789",
            AttachedAt = DateTime.UtcNow
        };

        _processManagerMock
            .Setup(x => x.AttachToProcessAsync(null, "TestApp"))
            .ReturnsAsync(expectedSession);

        // Act
        var result = await _tools.WpfAttach(process_name: "TestApp");

        // Assert
        result.Should().NotBeNull();
        _processManagerMock.Verify(x => x.AttachToProcessAsync(null, "TestApp"), Times.Once);
    }

    [Fact]
    public async Task WpfAttach_WithNoParameters_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _tools.WpfAttach());
    }

    [Fact]
    public async Task WpfGetVisualTree_ReturnsTree()
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

        // Act
        var result = await _tools.WpfGetVisualTree();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedTree);
    }

    [Fact]
    public async Task WpfGetVisualTree_WithCustomDepth_UsesSpecifiedDepth()
    {
        // Arrange
        var expectedTree = new VisualTreeResult
        {
            Root = new VisualTreeNode { Handle = "root", TypeName = "Window" },
            TotalElements = 1,
            MaxDepthReached = false
        };

        _ipcBridgeMock
            .Setup(x => x.GetVisualTreeAsync(null, 5))
            .ReturnsAsync(expectedTree);

        // Act
        var result = await _tools.WpfGetVisualTree(max_depth: 5);

        // Assert
        _ipcBridgeMock.Verify(x => x.GetVisualTreeAsync(null, 5), Times.Once);
    }

    [Fact]
    public async Task WpfGetElementProperties_WithValidHandle_ReturnsProperties()
    {
        // Arrange
        var expectedResult = new ElementPropertiesResult
        {
            Element = new ElementInfo { Handle = "elem_1", TypeName = "Button" },
            Properties = new List<PropertyInfo>
            {
                new PropertyInfo { Name = "Content", TypeName = "String", Value = "Click Me" }
            }
        };

        _ipcBridgeMock
            .Setup(x => x.GetElementPropertiesAsync("elem_1"))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfGetElementProperties("elem_1");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfGetElementProperties_WithEmptyHandle_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _tools.WpfGetElementProperties(""));
    }

    [Fact]
    public async Task WpfFindElements_ReturnsMatchingElements()
    {
        // Arrange
        var expectedResult = new FindElementsResult
        {
            Elements = new List<FoundElement>
            {
                new FoundElement { Handle = "btn_1", TypeName = "Button", Name = "SubmitButton" }
            },
            Count = 1
        };

        _ipcBridgeMock
            .Setup(x => x.FindElementsAsync("Button", null, null))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfFindElements(type_name: "Button");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfGetBindings_WithValidHandle_ReturnsBindings()
    {
        // Arrange
        var expectedResult = new BindingsResult
        {
            Element = new ElementInfo { Handle = "txt_1", TypeName = "TextBox" },
            Bindings = new List<BindingInfo>
            {
                new BindingInfo { Property = "Text", Path = "UserName", Mode = "TwoWay" }
            }
        };

        _ipcBridgeMock
            .Setup(x => x.GetBindingsAsync("txt_1"))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfGetBindings("txt_1");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfGetBindingErrors_ReturnsErrors()
    {
        // Arrange
        var expectedResult = new BindingErrorsResult
        {
            Errors = new List<BindingError>
            {
                new BindingError { Property = "Text", Message = "Path not found" }
            },
            Count = 1
        };

        _ipcBridgeMock
            .Setup(x => x.GetBindingErrorsAsync())
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfGetBindingErrors();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfGetResources_WithApplicationScope_ReturnsResources()
    {
        // Arrange
        var expectedResult = new ResourcesResult
        {
            Resources = new List<ResourceInfo>
            {
                new ResourceInfo { Key = "PrimaryColor", TypeName = "SolidColorBrush" }
            }
        };

        _ipcBridgeMock
            .Setup(x => x.GetResourcesAsync("application", null))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfGetResources(scope: "application");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfGetResources_WithElementScopeButNoHandle_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tools.WpfGetResources(scope: "element", element_handle: null));
    }

    [Fact]
    public async Task WpfGetStyles_WithValidHandle_ReturnsStyles()
    {
        // Arrange
        var expectedResult = new StylesResult
        {
            Element = new ElementInfo { Handle = "btn_1", TypeName = "Button" },
            Style = new StyleInfo { TargetType = "Button" }
        };

        _ipcBridgeMock
            .Setup(x => x.GetStylesAsync("btn_1"))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfGetStyles("btn_1");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfHighlightElement_WithValidHandle_HighlightsElement()
    {
        // Arrange
        _ipcBridgeMock
            .Setup(x => x.HighlightElementAsync("elem_1", 2000))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _tools.WpfHighlightElement("elem_1");

        // Assert
        result.Should().NotBeNull();
        _ipcBridgeMock.Verify(x => x.HighlightElementAsync("elem_1", 2000), Times.Once);
    }

    [Fact]
    public async Task WpfHighlightElement_WithCustomDuration_UsesSpecifiedDuration()
    {
        // Arrange
        _ipcBridgeMock
            .Setup(x => x.HighlightElementAsync("elem_1", 5000))
            .Returns(Task.CompletedTask);

        // Act
        await _tools.WpfHighlightElement("elem_1", duration_ms: 5000);

        // Assert
        _ipcBridgeMock.Verify(x => x.HighlightElementAsync("elem_1", 5000), Times.Once);
    }

    [Fact]
    public async Task WpfGetLayoutInfo_WithValidHandle_ReturnsLayoutInfo()
    {
        // Arrange
        var expectedResult = new LayoutInfoResult
        {
            Element = new ElementInfo { Handle = "grid_1", TypeName = "Grid" },
            Layout = new LayoutInfo { ActualWidth = 800, ActualHeight = 600 }
        };

        _ipcBridgeMock
            .Setup(x => x.GetLayoutInfoAsync("grid_1"))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfGetLayoutInfo("grid_1");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfExportTree_WithJsonFormat_ReturnsJsonExport()
    {
        // Arrange
        var expectedResult = new ExportResult
        {
            Format = "json",
            Content = "{\"root\": {}}",
            ElementCount = 1
        };

        _ipcBridgeMock
            .Setup(x => x.ExportTreeAsync(null, "json"))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfExportTree(format: "json");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfExportTree_WithXamlFormat_ReturnsXamlExport()
    {
        // Arrange
        var expectedResult = new ExportResult
        {
            Format = "xaml",
            Content = "<Window />",
            ElementCount = 1
        };

        _ipcBridgeMock
            .Setup(x => x.ExportTreeAsync(null, "xaml"))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _tools.WpfExportTree(format: "xaml");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task WpfExportTree_WithInvalidFormat_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tools.WpfExportTree(format: "invalid"));
    }

    [Fact]
    public async Task WpfWatchProperty_WithValidParameters_ReturnsWatchId()
    {
        // Arrange
        _ipcBridgeMock
            .Setup(x => x.WatchPropertyAsync("elem_1", "Width"))
            .ReturnsAsync("watch_123");

        // Act
        var result = await _tools.WpfWatchProperty("elem_1", "Width");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("watch_123");
    }

    [Fact]
    public async Task WpfWatchProperty_WithEmptyHandle_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tools.WpfWatchProperty("", "Width"));
    }

    [Fact]
    public async Task WpfWatchProperty_WithEmptyPropertyName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tools.WpfWatchProperty("elem_1", ""));
    }
}
