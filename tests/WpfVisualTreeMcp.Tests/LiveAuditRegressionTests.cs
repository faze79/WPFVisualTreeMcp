using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Moq;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class LiveAuditRegressionTests
{
    private static NamedPipeBridge Bridge() => new(NullLogger<NamedPipeBridge>.Instance, new Mock<IProcessManager>().Object);

    [Fact]
    public void Property_parser_preserves_numeric_boolean_null_and_binding_values()
    {
        var response = new GetElementPropertiesResponse { PropertiesJson = """
            {"element":{"typeName":"Button","name":"Save"},"properties":[
            {"name":"Width","value":123.5,"ownerType":"System.Windows.FrameworkElement"},{"name":"IsEnabled","value":true,"isBinding":true},
            {"name":"Tag","value":null},{"name":"Height","value":"NaN"},{"name":"MaxHeight","value":"Infinity"}]}
            """ };
        var result = Bridge().ParseElementPropertiesResponse(response, "live");
        result.Element.Name.Should().Be("Save");
        result.Properties.Select(p => p.Value).Should().Equal("123.5", "true", null, "NaN", "Infinity");
        result.Properties[1].IsBinding.Should().BeTrue();
        result.Properties[0].OwnerType.Should().Be("System.Windows.FrameworkElement");
    }

    [Theory]
    [InlineData("{\"properties\":[{\"value\":Infinity}]}")]
    [InlineData("{\"properties\":[{\"value\":NaN}]}")]
    [InlineData("")]
    [InlineData("{}")]
    public void Invalid_or_empty_property_payload_is_an_actionable_error(string payload)
    {
        var act = () => Bridge().ParseElementPropertiesResponse(new GetElementPropertiesResponse { PropertiesJson = payload }, "live");
        act.Should().Throw<McpException>().WithMessage("*Inspector*");
    }

    [Fact]
    public void Layout_parser_retains_element_identity()
    {
        var result = Bridge().ParseLayoutInfoResponse(new GetLayoutInfoResponse
        { LayoutJson = """{"element":{"typeName":"ComboBox","name":"Types"},"layout":{"actualWidth":12.5}}""" }, "live");
        result.Element.TypeName.Should().Be("ComboBox");
        result.Element.Name.Should().Be("Types");
        result.Layout.ActualWidth.Should().Be(12.5);
    }

    [Theory]
    [InlineData("move", 0, false)]
    [InlineData("scroll", -240, true)]
    public void Pointer_workflow_roundtrip_rebinds_handles_and_preserves_input(string action, int delta, bool horizontal)
    {
        var request = new MousePointerRequest { Options = new MousePointerOptions
        { ElementHandle = "old", Action = action, X = 12.5, Y = 34.5, Delta = delta, Horizontal = horizontal, HoverMs = 100 } };
        var step = new WorkflowStep { Action = "MousePointer", Target = new ElementSelector { TypeName = "HwndHost", Name = "Canvas" }, Arguments = WorkflowCodec.Arguments(request) };
        var workflow = new RecordedWorkflow { Steps = new() { step } };
        WorkflowCodec.Validate(workflow);
        var exported = WorkflowTestExporter.Export(new RecordingState { Workflow = workflow }, "json")["workflow.json"];
        exported.Should().NotContain("old").And.NotContain("elementHandle");
        var replay = (MousePointerRequest)WorkflowCodec.ReadRequest(step.Action, step.Arguments);
        WorkflowCodec.BindHandles(replay, "fresh", null, null);
        replay.Options.ElementHandle.Should().Be("fresh");
        replay.Options.X.Should().Be(12.5); replay.Options.Delta.Should().Be(delta); replay.Options.Horizontal.Should().Be(horizontal);
    }

    [Fact]
    public void Coordinate_click_is_retained_in_recording()
    {
        var request = new ClickElementRequest { ElementHandle = "old", X = 20, Y = 30, ClickType = "right" };
        var replay = (ClickElementRequest)WorkflowCodec.ReadRequest("ClickElement", WorkflowCodec.Arguments(request));
        replay.X.Should().Be(20); replay.Y.Should().Be(30); replay.ClickType.Should().Be("right");
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(double.NaN, 2)]
    [InlineData(double.PositiveInfinity, 2)]
    public void Invalid_coordinates_are_rejected(double x, double y)
    {
        var act = () => MousePointerOptions.ValidatePoint(x, y);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12001)]
    [InlineData(-12001)]
    public async Task Invalid_wheel_events_are_exposed_as_MCP_errors(int delta)
    {
        var tools = new WpfTools(new Mock<IProcessManager>().Object, new Mock<IIpcBridge>().Object);
        var act = () => tools.WpfScroll("canvas", delta);
        await act.Should().ThrowAsync<McpException>().WithMessage("*120*");
    }
}
