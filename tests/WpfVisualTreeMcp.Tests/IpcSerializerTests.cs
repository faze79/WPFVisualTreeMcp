using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Shared.Ipc;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

/// <summary>
/// Regression tests for the IPC request envelope. SerializeRequest historically
/// serialized the payload by its declared base type (IpcRequest), silently dropping
/// every derived-class property — filters never reached the Inspector and all
/// handle-based operations failed with "ElementHandle required".
/// </summary>
public class IpcSerializerTests
{
    [Fact]
    public void SerializeRequest_IncludesDerivedClassProperties()
    {
        var request = new FindElementsRequest
        {
            TypeName = "Button",
            ElementName = "SubmitButton",
            Text = "Save",
            VisibleOnly = true,
            MaxResults = 25
        };

        var json = IpcSerializer.SerializeRequest(request);

        json.Should().Contain("\"typeName\":\"Button\"");
        json.Should().Contain("\"elementName\":\"SubmitButton\"");
        json.Should().Contain("\"text\":\"Save\"");
        json.Should().Contain("\"visibleOnly\":true");
        json.Should().Contain("\"maxResults\":25");
    }

    [Fact]
    public void SerializeRequest_IncludesElementHandle()
    {
        var request = new ClickElementRequest
        {
            ElementHandle = "elem_00000042",
            Physical = true
        };

        var json = IpcSerializer.SerializeRequest(request);

        json.Should().Contain("\"elementHandle\":\"elem_00000042\"");
        json.Should().Contain("\"physical\":true");
    }

    [Fact]
    public void SerializeRequest_RoundTripsThroughInspectorDeserialization()
    {
        var request = new FindElementsRequest
        {
            TypeName = "Button",
            Text = "Save",
            PropertyFilter = new Dictionary<string, string> { ["IsEnabled"] = "True" },
            VisibleOnly = true,
            MaxResults = 10
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);

        envelope.Should().NotBeNull();
        envelope!.Value.type.Should().Be("FindElements");

        var roundTripped = IpcSerializer.DeserializeRequestData<FindElementsRequest>(envelope.Value.data);
        roundTripped.Should().NotBeNull();
        roundTripped!.TypeName.Should().Be("Button");
        roundTripped.Text.Should().Be("Save");
        roundTripped.VisibleOnly.Should().BeTrue();
        roundTripped.MaxResults.Should().Be(10);
        roundTripped.PropertyFilter.Should().ContainKey("IsEnabled").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void SerializeRequest_RoundTripsSelectItemRequest()
    {
        var request = new SelectItemRequest
        {
            ElementHandle = "elem_00000007",
            ItemText = "Italia",
            Index = null
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);

        envelope!.Value.type.Should().Be("SelectItem");
        var roundTripped = IpcSerializer.DeserializeRequestData<SelectItemRequest>(envelope.Value.data);
        roundTripped!.ElementHandle.Should().Be("elem_00000007");
        roundTripped.ItemText.Should().Be("Italia");
        roundTripped.Index.Should().BeNull();
    }

    [Fact]
    public void SerializeRequest_RoundTripsClickTypeAndScreenshotMode()
    {
        var click = new ClickElementRequest { ElementHandle = "elem_1", ClickType = "right" };
        var clickJson = IpcSerializer.SerializeRequest(click);
        var clickBack = IpcSerializer.DeserializeRequestData<ClickElementRequest>(
            IpcSerializer.DeserializeRequest(clickJson)!.Value.data);
        clickBack!.ClickType.Should().Be("right");

        var shot = new CaptureScreenshotRequest { Mode = "screen" };
        var shotJson = IpcSerializer.SerializeRequest(shot);
        var shotBack = IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(
            IpcSerializer.DeserializeRequest(shotJson)!.Value.data);
        shotBack!.Mode.Should().Be("screen");
    }

    [Fact]
    public void SerializeRequest_RoundTripsSnapshotAndDiffRequests()
    {
        var snap = new SnapshotRequest { ElementHandle = "elem_3", Label = "before", MaxDepth = 10 };
        var snapBack = IpcSerializer.DeserializeRequestData<SnapshotRequest>(
            IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(snap))!.Value.data);
        snapBack!.ElementHandle.Should().Be("elem_3");
        snapBack.Label.Should().Be("before");
        snapBack.MaxDepth.Should().Be(10);

        var diff = new DiffRequest { Before = "before", After = "after" };
        var diffEnvelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(diff));
        diffEnvelope!.Value.type.Should().Be("Diff");
        var diffBack = IpcSerializer.DeserializeRequestData<DiffRequest>(diffEnvelope.Value.data);
        diffBack!.Before.Should().Be("before");
        diffBack.After.Should().Be("after");
    }

    [Fact]
    public void SerializeRequest_RoundTripsSetAndRevertPropertyRequests()
    {
        var set = new SetPropertyRequest { ElementHandle = "elem_9", PropertyName = "Visibility", Value = "Collapsed" };
        var setBack = IpcSerializer.DeserializeRequestData<SetPropertyRequest>(
            IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(set))!.Value.data);
        setBack!.ElementHandle.Should().Be("elem_9");
        setBack.PropertyName.Should().Be("Visibility");
        setBack.Value.Should().Be("Collapsed");

        var revert = new RevertPropertyRequest { All = true };
        var revertEnvelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(revert));
        revertEnvelope!.Value.type.Should().Be("RevertProperty");
        var revertBack = IpcSerializer.DeserializeRequestData<RevertPropertyRequest>(revertEnvelope.Value.data);
        revertBack!.All.Should().BeTrue();
    }

    [Fact]
    public void SerializeRequest_RoundTripsWaitForElementRequest()
    {
        var request = new WaitForElementRequest
        {
            TypeName = "ProgressBar",
            Text = "Loading",
            Condition = "hidden",
            TimeoutMs = 8000,
            PollIntervalMs = 200
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);

        envelope!.Value.type.Should().Be("WaitForElement");
        var back = IpcSerializer.DeserializeRequestData<WaitForElementRequest>(envelope.Value.data);
        back!.TypeName.Should().Be("ProgressBar");
        back.Text.Should().Be("Loading");
        back.Condition.Should().Be("hidden");
        back.TimeoutMs.Should().Be(8000);
        back.PollIntervalMs.Should().Be(200);
    }

    [Fact]
    public void SerializeRequest_RoundTripsSetTextRequest()
    {
        var request = new SetTextRequest
        {
            ElementHandle = "elem_00000010",
            Text = "hello@example.com"
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);
        var roundTripped = IpcSerializer.DeserializeRequestData<SetTextRequest>(envelope!.Value.data);

        roundTripped!.ElementHandle.Should().Be("elem_00000010");
        roundTripped.Text.Should().Be("hello@example.com");
        roundTripped.Physical.Should().BeFalse();
    }
}
