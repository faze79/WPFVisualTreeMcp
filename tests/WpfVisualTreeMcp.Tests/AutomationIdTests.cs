using WpfVisualTreeMcp.Automation.Infrastructure;
using System.IO.Pipes;
using System.Text;
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

public class AutomationIdTests
{
    private static NamedPipeBridge Bridge()
    {
        var manager = new Mock<IProcessManager>();
        manager.SetupGet(x => x.CurrentSession).Returns(new InspectionSession
        { ProcessId = Environment.ProcessId, InspectorReady = true });
        return new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, manager.Object);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("handle", "save", null)]
    [InlineData("handle", null, "root")]
    [InlineData("", null, null)]
    [InlineData(null, "", null)]
    [InlineData(null, "  ", null)]
    public async Task Invalid_target_is_rejected_without_IPC(string? handle, string? id, string? root)
    {
        var act = () => Bridge().ResolveElementHandleAsync(handle, id, root);
        await act.Should().ThrowAsync<McpException>();
    }

    [Fact]
    public async Task Existing_handle_does_not_require_new_inspector_capabilities()
    {
        (await Bridge().ResolveElementHandleAsync("original", null)).Should().Be("original");
    }

    [Fact]
    public async Task Click_resolves_id_before_sending_the_original_click_options()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "save", "dialog")).ReturnsAsync("resolved");
        bridge.Setup(x => x.ClickElementAsync("resolved", false, "double", 10, 20))
            .ReturnsAsync(new ClickResult { Method = "Physical" });
        var tools = new WpfTools(new Mock<IProcessManager>().Object, bridge.Object);
        await tools.WpfClickElement(automation_id: "save", root_handle: "dialog", click_type: "double", x: 10, y: 20);
        bridge.VerifyAll();
    }

    [Fact]
    public async Task Failed_resolution_never_sends_a_click()
    {
        var bridge = new Mock<IIpcBridge>(MockBehavior.Strict);
        bridge.Setup(x => x.ResolveElementHandleAsync(null, "save", null)).ThrowsAsync(new McpException("Ambiguous AutomationId"));
        var tools = new WpfTools(new Mock<IProcessManager>().Object, bridge.Object);
        var act = () => tools.WpfClickElement(automation_id: "save");
        await act.Should().ThrowAsync<McpException>().WithMessage("*Ambiguous*");
        bridge.Verify(x => x.ClickElementAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>()), Times.Never);
    }

    [Theory]
    [InlineData("find")]
    [InlineData("deep")]
    [InlineData("wait")]
    [InlineData("resolve")]
    [InlineData("replay")]
    public async Task Old_inspector_cannot_silently_ignore_an_id(string operation)
    {
        using var pipe = Pipe();
        var response = Reply(pipe, data =>
        {
            data.type.Should().Be("GetCapabilities");
            return new GetCapabilitiesResponse { CoordinatePointerInput = true };
        });
        var bridge = Bridge();
        var act = async () =>
        {
            switch (operation)
            {
                case "find": await bridge.FindElementsAsync(null, null, null, null, null, automationId: "save"); break;
                case "deep": await bridge.FindElementsDeepAsync(null, null, null, automationId: "save"); break;
                case "wait": await bridge.WaitForElementAsync(null, null, null, null, "exists", 100, 50, "save"); break;
                case "resolve": await bridge.ResolveElementHandleAsync(null, "save"); break;
                case "replay": await bridge.ReplayAsync(new RecordedWorkflow { Steps = new()
                { new WorkflowStep { Action = "FindElements", Arguments = WorkflowCodec.Arguments(new FindElementsRequest { AutomationId = "save" }) } } }); break;
            }
        };
        await act.Should().ThrowAsync<McpException>().WithMessage("*older Inspector*restart*");
        await response;
    }

    [Fact]
    public async Task Resolution_roundtrips_exact_id_and_scope_over_IPC()
    {
        using var pipe = Pipe();
        var responses = Task.Run(async () =>
        {
            await Reply(pipe, _ => new GetCapabilitiesResponse { AutomationIdTargeting = true });
            pipe.Disconnect();
            await Reply(pipe, envelope =>
            {
                envelope.type.Should().Be("ResolveAutomationId");
                var request = IpcSerializer.DeserializeRequestData<ResolveAutomationIdRequest>(envelope.data)!;
                request.AutomationId.Should().Be("Ordine.保存");
                request.RootHandle.Should().Be("dialog");
                return new ResolveAutomationIdResponse { ElementHandle = "resolved" };
            });
        });
        (await Bridge().ResolveElementHandleAsync(null, "Ordine.保存", "dialog")).Should().Be("resolved");
        await responses;
    }

    [Theory]
    [InlineData("FindElements")]
    [InlineData("FindElementsDeep")]
    [InlineData("WaitForElement")]
    public void Filters_survive_workflow_export_and_replay(string action)
    {
        IpcRequest request = action switch
        {
            "FindElements" => new FindElementsRequest { AutomationId = "save" },
            "FindElementsDeep" => new FindElementsDeepRequest { AutomationId = "save" },
            _ => new WaitForElementRequest { AutomationId = "save", Condition = "enabled" }
        };
        var step = new WorkflowStep { Action = action, Arguments = WorkflowCodec.Arguments(request) };
        WorkflowCodec.Validate(new RecordedWorkflow { Steps = new() { step } });
        var replay = WorkflowCodec.ReadRequest(action, step.Arguments);
        IpcSerializer.SerializeRequest(replay).Should().Contain("\"automationId\":\"save\"");
    }

    private static NamedPipeServerStream Pipe() => new($"wpf_inspector_{Environment.ProcessId}",
        PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static async Task Reply(NamedPipeServerStream pipe,
        Func<(string type, System.Text.Json.JsonElement data), IpcResponse> response)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(timeout.Token);
        var request = IpcSerializer.DeserializeRequest(line!)!.Value;
        await writer.WriteLineAsync(IpcSerializer.SerializeResponse(response(request)));
    }
}
