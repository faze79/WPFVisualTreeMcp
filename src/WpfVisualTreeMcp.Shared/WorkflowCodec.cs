using System.Text.Json;
using System.Text.Json.Nodes;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared;

/// <summary>Versioned, bounded data-only workflows. No scripts or process commands.</summary>
public static class WorkflowCodec
{
    public const int MaxSteps = 100;
    public const int MaxJsonLength = 512 * 1024;
    public static bool IsRecordable(string action) => action is "ClickElement" or "SetText" or "SelectItem" or "SendKeys"
        or "Drag" or "TouchGesture" or "SetProperty" or "RevertProperty" or "FindElements" or "FindElementsDeep"
        or "WaitForElement" or "AssertElement";

    public static void Validate(RecordedWorkflow workflow)
    {
        if (workflow == null || workflow.SchemaVersion != 1 || workflow.Steps == null || workflow.Steps.Count is < 1 or > MaxSteps)
            throw new ArgumentException("A schemaVersion 1 workflow with 1-100 steps is required.");
        if (workflow.Name == null || workflow.Name.Length > 200 || IpcSerializer.Serialize(workflow).Length > MaxJsonLength)
            throw new ArgumentException("Workflow exceeds its name or 512 KiB JSON limit.");
        foreach (var step in workflow.Steps)
        {
            if (step == null) throw new ArgumentException("Workflow steps must not be null.");
            ValidateSelector(step.Target); ValidateSelector(step.Destination); ValidateSelector(step.Root);
            var request = ReadRequest(step.Action, step.Arguments);
            var needsTarget = request is ClickElementRequest or SetTextRequest or SelectItemRequest or SendKeysRequest
                or DragRequest or TouchGestureRequest or SetPropertyRequest or AssertElementRequest;
            if (needsTarget && step.Target == null) throw new ArgumentException($"{step.Action} requires a target selector.");
            BindHandles(request, step.Target == null ? null : "selector", step.Destination == null ? null : "selector", step.Root == null ? null : "selector");
            ValidateRequest(request);
        }
    }

    public static void ValidateSelector(ElementSelector? selector, int depth = 0)
    {
        if (selector == null) return;
        if (depth > 3 || string.IsNullOrWhiteSpace(selector.TypeName) || selector.TypeName.Length > 1024)
            throw new ArgumentException("Selectors require an exact type name and at most four ancestor levels.");
        foreach (var value in new[] { selector.AutomationId, selector.Name, selector.Text })
            if (value != null && (value.Length == 0 || value.Length > 2048)) throw new ArgumentException("Selector identifiers must contain 1-2048 characters.");
        if (selector.AutomationId == null && selector.Name == null && selector.Text == null && selector.Ancestor == null)
            throw new ArgumentException("A selector needs an AutomationId, name, text or identified ancestor.");
        ValidateSelector(selector.Ancestor, depth + 1);
    }

    public static JsonElement Arguments(IpcRequest request)
    {
        var node = JsonNode.Parse(IpcSerializer.Serialize<object>(request))!.AsObject();
        foreach (var key in new[] { "requestId", "requestType", "elementHandle", "targetHandle", "rootHandle" }) node.Remove(key);
        if (node["options"] is JsonObject options)
        {
            options.Remove("elementHandle"); options.Remove("targetHandle"); options.Remove("effectiveDurationMs");
        }
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    public static IpcRequest ReadRequest(string action, JsonElement arguments)
    {
        RejectHandles(arguments);
        return ParseLiveRequest(action, arguments);
    }

    public static IpcRequest ParseLiveRequest(string action, JsonElement arguments)
    {
        if (!IsRecordable(action) || arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Unsupported workflow action or invalid arguments: " + action);
        return action switch
        {
            "ClickElement" => IpcSerializer.DeserializeRequestData<ClickElementRequest>(arguments)!,
            "SetText" => IpcSerializer.DeserializeRequestData<SetTextRequest>(arguments)!,
            "SelectItem" => IpcSerializer.DeserializeRequestData<SelectItemRequest>(arguments)!,
            "SendKeys" => IpcSerializer.DeserializeRequestData<SendKeysRequest>(arguments)!,
            "Drag" => IpcSerializer.DeserializeRequestData<DragRequest>(arguments)!,
            "TouchGesture" => IpcSerializer.DeserializeRequestData<TouchGestureRequest>(arguments)!,
            "SetProperty" => IpcSerializer.DeserializeRequestData<SetPropertyRequest>(arguments)!,
            "RevertProperty" => IpcSerializer.DeserializeRequestData<RevertPropertyRequest>(arguments)!,
            "FindElements" => IpcSerializer.DeserializeRequestData<FindElementsRequest>(arguments)!,
            "FindElementsDeep" => IpcSerializer.DeserializeRequestData<FindElementsDeepRequest>(arguments)!,
            "WaitForElement" => IpcSerializer.DeserializeRequestData<WaitForElementRequest>(arguments)!,
            "AssertElement" => IpcSerializer.DeserializeRequestData<AssertElementRequest>(arguments)!,
            _ => throw new ArgumentException("Unsupported workflow action.")
        };
    }

    private static void RejectHandles(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "elementHandle" or "targetHandle" or "rootHandle" or "requestId" or "requestType")
                    throw new ArgumentException("Workflow arguments must not contain session handles or request metadata.");
                RejectHandles(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectHandles(item);
    }

    public static (string? target, string? destination, string? root) Handles(IpcRequest request) => request switch
    {
        ClickElementRequest r => (r.ElementHandle, null, null),
        SetTextRequest r => (r.ElementHandle, null, null),
        SelectItemRequest r => (r.ElementHandle, null, null),
        SendKeysRequest r => (r.ElementHandle, null, null),
        DragRequest r => (r.Options.ElementHandle, r.Options.TargetHandle, null),
        TouchGestureRequest r => (r.Options.ElementHandle, null, null),
        SetPropertyRequest r => (r.ElementHandle, null, null),
        RevertPropertyRequest r => (r.ElementHandle, null, null),
        FindElementsRequest r => (null, null, r.RootHandle),
        FindElementsDeepRequest r => (null, null, r.RootHandle),
        WaitForElementRequest r => (null, null, r.RootHandle),
        AssertElementRequest r => (r.ElementHandle, null, null),
        _ => (null, null, null)
    };

    public static void BindHandles(IpcRequest request, string? target, string? destination, string? root)
    {
        switch (request)
        {
            case ClickElementRequest r: r.ElementHandle = target!; break;
            case SetTextRequest r: r.ElementHandle = target!; break;
            case SelectItemRequest r: r.ElementHandle = target!; break;
            case SendKeysRequest r: r.ElementHandle = target; break;
            case DragRequest r:
                if (r.Options == null) throw new ArgumentException("Drag options required.");
                r.Options.ElementHandle = target!; r.Options.TargetHandle = destination; break;
            case TouchGestureRequest r:
                if (r.Options == null) throw new ArgumentException("Touch options required.");
                r.Options.ElementHandle = target!; break;
            case SetPropertyRequest r: r.ElementHandle = target!; break;
            case RevertPropertyRequest r: r.ElementHandle = target; break;
            case FindElementsRequest r: r.RootHandle = root; break;
            case FindElementsDeepRequest r: r.RootHandle = root; break;
            case WaitForElementRequest r: r.RootHandle = root; break;
            case AssertElementRequest r: r.ElementHandle = target!; break;
        }
    }

    public static void ValidateRequest(IpcRequest request)
    {
        switch (request)
        {
            case DragRequest r: (r.Options ?? throw new ArgumentException("Drag options required.")).Validate(); break;
            case TouchGestureRequest r: (r.Options ?? throw new ArgumentException("Touch options required.")).Validate(); break;
            case ClickElementRequest r when r.ClickType != null && r.ClickType is not ("single" or "double" or "right"):
                throw new ArgumentException("Invalid click type.");
            case SelectItemRequest r when (r.ItemText == null) == (r.Index == null) || r.Index < 0:
                throw new ArgumentException("SelectItem needs either itemText or a nonnegative index.");
            case SendKeysRequest r when string.IsNullOrWhiteSpace(r.Keys): throw new ArgumentException("Keys required.");
            case SetTextRequest r when r.Text == null: throw new ArgumentException("Text required.");
            case SetPropertyRequest r when string.IsNullOrWhiteSpace(r.PropertyName) || r.Value == null:
                throw new ArgumentException("Property and value required.");
            case AssertElementRequest r when string.IsNullOrWhiteSpace(r.PropertyName) || r.ExpectedValue == null:
                throw new ArgumentException("Assertion property and expectedValue required.");
            case WaitForElementRequest r when r.Condition is not ("visible" or "exists" or "enabled" or "hidden")
                || r.TimeoutMs is < 0 or > 25000 || r.PollIntervalMs is < 50 or > 1000
                || (string.IsNullOrWhiteSpace(r.TypeName) && string.IsNullOrWhiteSpace(r.ElementName) && string.IsNullOrWhiteSpace(r.Text)):
                throw new ArgumentException("Invalid wait condition, criteria or timeout/poll bounds.");
        }
    }
}
