using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Implementation of IPC bridge using named pipes.
/// Communicates with the Inspector DLL injected into the target WPF process.
/// </summary>
public class NamedPipeBridge : IIpcBridge
{
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
    private readonly ILogger<NamedPipeBridge> _logger;
    private readonly IProcessManager _processManager;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);

    public NamedPipeBridge(ILogger<NamedPipeBridge> logger, IProcessManager processManager)
    {
        _logger = logger;
        _processManager = processManager;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<RecordingState> RecordAsync(string action, string? name = null)
    {
        var response = await SendRequestAsync<RecordRequest, RecordResponse>(EnsureConnected().ProcessId,
            new RecordRequest { Action = action, Name = name });
        if (!response.Success || response.State == null) throw new McpException(response.Error ?? "Recording unavailable.");
        return response.State;
    }

    public async Task<ReplayResult> ReplayAsync(RecordedWorkflow workflow, int timeoutMs = 120000)
    {
        try { Shared.WorkflowCodec.Validate(workflow); }
        catch (ArgumentException ex) { throw new McpException(ex.Message); }
        if (timeoutMs is < 1000 or > 120000) throw new McpException("Replay timeout must be between 1000 and 120000 ms.");
        if (workflow.Steps.Any(step => step.Action == "MousePointer" ||
            step.Action == "ClickElement" && (step.Arguments.TryGetProperty("x", out _) || step.Arguments.TryGetProperty("y", out _))))
            await EnsurePointerSupportAsync(EnsureConnected().ProcessId);
        var response = await SendRequestAsync<ReplayRequest, ReplayResponse>(EnsureConnected().ProcessId,
            new ReplayRequest { Workflow = workflow, TimeoutMs = timeoutMs });
        if (!response.Success || response.Result == null) throw new McpException(response.Error ?? "Replay unavailable.");
        return response.Result;
    }

    public async Task<string?> AssertElementAsync(string elementHandle, string propertyName, string expectedValue)
    {
        var response = await SendRequestAsync<AssertElementRequest, AssertElementResponse>(EnsureConnected().ProcessId,
            new AssertElementRequest { ElementHandle = elementHandle, PropertyName = propertyName, ExpectedValue = expectedValue });
        if (!response.Success) throw new McpException(response.Error ?? "Assertion failed.");
        return response.ActualValue;
    }

    public async Task<DiagnosticReport> ReportAsync(string? rootHandle, int maxElements, int maxDepth, int maxFindings)
    {
        var response = await SendRequestAsync<ReportRequest, ReportResponse>(EnsureConnected().ProcessId,
            new ReportRequest { RootHandle = rootHandle, MaxElements = maxElements, MaxDepth = maxDepth, MaxFindings = maxFindings });
        if (!response.Success || response.Report == null)
            throw new McpException(response.Error ?? "Diagnostic report unavailable.");
        return response.Report;
    }

    public async Task<VisualTreeResult> GetVisualTreeAsync(string? rootHandle, int maxDepth)
    {
        var session = EnsureConnected();

        var request = new GetVisualTreeRequest
        {
            RootHandle = rootHandle,
            MaxDepth = maxDepth
        };

        var response = await SendRequestAsync<GetVisualTreeRequest, GetVisualTreeResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get visual tree");
        }

        return ParseVisualTreeResponse(response);
    }

    public async Task<ElementPropertiesResult> GetElementPropertiesAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetElementPropertiesRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetElementPropertiesRequest, GetElementPropertiesResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get element properties");
        }

        return ParseElementPropertiesResponse(response, elementHandle);
    }

    public async Task<FindElementsResult> FindElementsAsync(string? rootHandle, string? typeName, string? elementName,
        string? text, Dictionary<string, string>? propertyFilter, bool visibleOnly = false, int maxResults = 50)
    {
        var session = EnsureConnected();

        var request = new FindElementsRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            Text = text,
            PropertyFilter = propertyFilter,
            VisibleOnly = visibleOnly,
            MaxResults = maxResults
        };

        var response = await SendRequestAsync<FindElementsRequest, FindElementsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to find elements");
        }

        return ParseFindElementsResponse(response);
    }

    public async Task<FindElementsResult> FindElementsDeepAsync(string? rootHandle, string? typeName, string? elementName,
        string? text = null, Dictionary<string, string>? propertyFilter = null, bool visibleOnly = false)
    {
        var session = EnsureConnected();

        var request = new FindElementsDeepRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            Text = text,
            PropertyFilter = propertyFilter,
            VisibleOnly = visibleOnly
        };

        var response = await SendRequestAsync<FindElementsDeepRequest, FindElementsDeepResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to find elements (deep search)");
        }

        return ParseFindElementsDeepResponse(response);
    }

    public async Task<BindingsResult> GetBindingsAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetBindingsRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetBindingsRequest, GetBindingsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get bindings");
        }

        return ParseBindingsResponse(response, elementHandle);
    }

    public async Task<BindingErrorsResult> GetBindingErrorsAsync()
    {
        var session = EnsureConnected();

        var request = new GetBindingErrorsRequest();

        var response = await SendRequestAsync<GetBindingErrorsRequest, GetBindingErrorsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get binding errors");
        }

        return ParseBindingErrorsResponse(response);
    }

    public async Task<ResourcesResult> GetResourcesAsync(string scope, string? elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetResourcesRequest
        {
            Scope = scope,
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetResourcesRequest, GetResourcesResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get resources");
        }

        return ParseResourcesResponse(response);
    }

    public async Task<StylesResult> GetStylesAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetStylesRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetStylesRequest, GetStylesResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get styles");
        }

        return ParseStylesResponse(response, elementHandle);
    }

    public async Task<string> WatchPropertyAsync(string elementHandle, string propertyName)
    {
        var session = EnsureConnected();

        var request = new WatchPropertyRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<WatchPropertyRequest, WatchPropertyResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to watch property");
        }

        return response.WatchId;
    }

    public async Task HighlightElementAsync(string elementHandle, int durationMs)
    {
        var session = EnsureConnected();

        var request = new HighlightElementRequest
        {
            ElementHandle = elementHandle,
            DurationMs = durationMs
        };

        var response = await SendRequestAsync<HighlightElementRequest, HighlightElementResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to highlight element");
        }
    }

    public async Task<LayoutInfoResult> GetLayoutInfoAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetLayoutInfoRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetLayoutInfoRequest, GetLayoutInfoResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get layout info");
        }

        return ParseLayoutInfoResponse(response, elementHandle);
    }

    public async Task<ExplainTriggersResult> ExplainTriggersAsync(string elementHandle, string? propertyName)
    {
        var session = EnsureConnected();

        var request = new ExplainTriggersRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<ExplainTriggersRequest, ExplainTriggersResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to explain triggers");
        }

        return new ExplainTriggersResult { Json = response.Json };
    }

    public async Task<EvaluateBindingResult> EvaluateBindingAsync(string elementHandle, string propertyName)
    {
        var session = EnsureConnected();

        var request = new EvaluateBindingRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<EvaluateBindingRequest, EvaluateBindingResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to evaluate binding");
        }

        return new EvaluateBindingResult { Json = response.EvaluationJson };
    }

    public async Task<SnapshotResult> SnapshotAsync(string? elementHandle, string? label, int maxDepth)
    {
        var session = EnsureConnected();

        var request = new SnapshotRequest
        {
            ElementHandle = elementHandle,
            Label = label,
            MaxDepth = maxDepth
        };

        var response = await SendRequestAsync<SnapshotRequest, SnapshotResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to capture snapshot");
        }

        return new SnapshotResult
        {
            Label = response.Label ?? "",
            ElementCount = response.ElementCount
        };
    }

    public async Task<DiffResult> DiffAsync(string before, string after)
    {
        var session = EnsureConnected();

        var request = new DiffRequest { Before = before, After = after };

        var response = await SendRequestAsync<DiffRequest, DiffResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to diff snapshots");
        }

        return new DiffResult
        {
            ChangedCount = response.ChangedCount,
            AddedCount = response.AddedCount,
            RemovedCount = response.RemovedCount,
            Json = response.DiffJson
        };
    }

    public async Task<SetPropertyResult> SetPropertyAsync(string elementHandle, string propertyName, string value)
    {
        var session = EnsureConnected();

        var request = new SetPropertyRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName,
            Value = value
        };

        var response = await SendRequestAsync<SetPropertyRequest, SetPropertyResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to set property");
        }

        return new SetPropertyResult
        {
            ElementType = response.ElementType,
            AppliedValue = response.AppliedValue,
            ValueType = response.ValueType,
            PreviousSource = response.PreviousSource
        };
    }

    public async Task<RevertPropertyResult> RevertPropertyAsync(bool all, string? elementHandle, string? propertyName)
    {
        var session = EnsureConnected();

        var request = new RevertPropertyRequest
        {
            All = all,
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<RevertPropertyRequest, RevertPropertyResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to revert property");
        }

        return new RevertPropertyResult
        {
            RevertedCount = response.RevertedCount,
            RevertedHandle = response.RevertedHandle,
            RevertedProperty = response.RevertedProperty,
            PendingCount = response.PendingCount
        };
    }

    public async Task<WaitForResult> WaitForElementAsync(string? rootHandle, string? typeName, string? elementName,
        string? text, string condition, int timeoutMs, int pollIntervalMs)
    {
        var session = EnsureConnected();

        var request = new WaitForElementRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            Text = text,
            Condition = condition,
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs
        };

        var response = await SendRequestAsync<WaitForElementRequest, WaitForElementResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to wait for element");
        }

        return new WaitForResult
        {
            Matched = response.Matched,
            MatchedHandle = response.MatchedHandle,
            ElementType = response.ElementType,
            WaitedMs = response.WaitedMs
        };
    }

    public async Task<ExportResult> ExportTreeAsync(string? elementHandle, string format)
    {
        var session = EnsureConnected();

        var request = new ExportTreeRequest
        {
            ElementHandle = elementHandle,
            Format = format
        };

        var response = await SendRequestAsync<ExportTreeRequest, ExportTreeResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to export tree");
        }

        return new ExportResult
        {
            Format = format,
            Content = response.Content ?? "",
            ElementCount = response.ElementCount
        };
    }

    public async Task<ScreenshotResult> CaptureScreenshotAsync(string? elementHandle, int maxWidth, int maxHeight, string mode = "auto", bool fullContent = false)
    {
        var session = EnsureConnected();

        var request = new CaptureScreenshotRequest
        {
            ElementHandle = elementHandle,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            Mode = mode,
            FullContent = fullContent
        };

        var response = await SendRequestAsync<CaptureScreenshotRequest, CaptureScreenshotResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to capture screenshot");
        }

        return new ScreenshotResult
        {
            ImageBase64 = response.ImageBase64 ?? string.Empty,
            MimeType = "image/png",
            Width = response.Width,
            Height = response.Height,
            ElementType = response.ElementType, CaptureMode = response.CaptureMode, Detail = response.Detail
        };
    }

    public async Task<DataContextResult> GetDataContextAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetDataContextRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetDataContextRequest, GetDataContextResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to get DataContext");
        }

        return new DataContextResult
        {
            Element = new ElementInfo { Handle = elementHandle },
            DataContextJson = response.DataContextJson
        };
    }

    public async Task ClearBindingErrorsAsync()
    {
        var session = EnsureConnected();

        var request = new ClearBindingErrorsRequest();

        var response = await SendRequestAsync<ClearBindingErrorsRequest, ClearBindingErrorsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to clear binding errors");
        }
    }

    public async Task<ClickResult> ClickElementAsync(string elementHandle, bool physical, string? clickType = null, double? x = null, double? y = null)
    {
        var session = EnsureConnected();
        if (x.HasValue || y.HasValue) await EnsurePointerSupportAsync(session.ProcessId);

        var request = new ClickElementRequest
        {
            ElementHandle = elementHandle,
            Physical = physical,
            ClickType = clickType, X = x, Y = y
        };

        var response = await SendRequestAsync<ClickElementRequest, ClickElementResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to click element");
        }

        return new ClickResult
        {
            Method = response.Method ?? "Unknown",
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<TouchGestureResult> TouchGestureAsync(TouchGestureOptions options)
    {
        options.Validate();
        var session = EnsureConnected();
        var response = await SendRequestAsync<TouchGestureRequest, TouchGestureResponse>(
            session.ProcessId, new TouchGestureRequest { Options = options });
        if (!response.Success) throw new McpException(response.Error ?? "Failed to inject touch gesture");
        return response.Result ?? throw new McpException("Inspector returned no touch result.");
    }

    public async Task<MousePointerResult> MousePointerAsync(MousePointerOptions options)
    {
        options.Validate();
        var session = EnsureConnected();
        var response = await SendRequestAsync<MousePointerRequest, MousePointerResponse>(
            session.ProcessId, new MousePointerRequest { Options = options });
        if (!response.Success) throw new McpException(response.Error ?? "Failed to send pointer input.");
        return response.Result ?? throw new McpException("Inspector returned no pointer result. Update and reload the Inspector.");
    }

    private async Task EnsurePointerSupportAsync(int processId)
    {
        // Older Inspectors ignore unknown JSON fields. Without this read-only
        // handshake a coordinate click could silently click the element center.
        var response = await SendRequestAsync<GetCapabilitiesRequest, GetCapabilitiesResponse>(processId, new GetCapabilitiesRequest());
        if (!response.Success || !response.CoordinatePointerInput)
            throw new McpException("The attached app still has an older Inspector. Save your work, restart the app, and reattach using the updated server before sending coordinate clicks or replaying pointer workflows.");
    }

    public async Task<DragResult> DragAsync(DragOptions options)
    {
        options.Validate();
        var session = EnsureConnected();
        var response = await SendRequestAsync<DragRequest, DragResponse>(
            session.ProcessId, new DragRequest { Options = options });
        if (!response.Success)
            throw new McpException(response.Error ?? "Failed to drag element");
        return response.Result ?? throw new McpException("Inspector returned no drag result.");
    }

    public async Task<SelectItemResult> SelectItemAsync(string elementHandle, string? itemText, int? index)
    {
        var session = EnsureConnected();

        var request = new SelectItemRequest
        {
            ElementHandle = elementHandle,
            ItemText = itemText,
            Index = index
        };

        var response = await SendRequestAsync<SelectItemRequest, SelectItemResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to select item");
        }

        return new SelectItemResult
        {
            Method = response.Method ?? "Unknown",
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<SetTextResult> SetTextAsync(string elementHandle, string text, bool physical)
    {
        var session = EnsureConnected();

        var request = new SetTextRequest
        {
            ElementHandle = elementHandle,
            Text = text ?? string.Empty,
            Physical = physical
        };

        var response = await SendRequestAsync<SetTextRequest, SetTextResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to set element text");
        }

        return new SetTextResult
        {
            Method = response.Method ?? "Unknown",
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<SendKeysResult> SendKeysAsync(string? elementHandle, string keys)
    {
        var session = EnsureConnected();

        var request = new SendKeysRequest
        {
            ElementHandle = elementHandle,
            Keys = keys
        };

        var response = await SendRequestAsync<SendKeysRequest, SendKeysResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new McpException(response.Error ?? "Failed to send keys");
        }

        return new SendKeysResult
        {
            Method = response.Method ?? "Unknown",
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(int processId, TRequest request)
        where TRequest : IpcRequest
        where TResponse : IpcResponse, new()
    {
        var pipeName = $"wpf_inspector_{processId}";

        // Check if the target process still exists
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            if (process.HasExited)
            {
                var errorMsg = $"Process {processId} has exited. Use wpf_list_processes() to see available WPF applications, then wpf_attach(process_id=<new_pid>) to connect.";
                _logger.LogWarning(errorMsg);
                return new TResponse { Success = false, Error = errorMsg };
            }
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            var errorMsg = $"Process {processId} no longer exists. The application may have been closed or restarted. Use wpf_list_processes() to see available WPF applications, then wpf_attach(process_id=<new_pid>) to connect to the current instance.";
            _logger.LogWarning(errorMsg);
            return new TResponse { Success = false, Error = errorMsg };
        }

        _logger.LogDebug("Sending {RequestType} request to pipe {PipeName}",
            request.RequestType, pipeName);

        // The CLI/MCP host may hold Windows foreground permission while the
        // inspected app has been idle. Delegate that permission before asking
        // its Dispatcher to activate. Input guards still verify the actual owner.
        if (OperatingSystem.IsWindows() && request is ReplayRequest or DragRequest or TouchGestureRequest or MousePointerRequest or
            SendKeysRequest or ClickElementRequest { Physical: true } or ClickElementRequest { X: not null } or
            ClickElementRequest { ClickType: "double" or "right" } or SetTextRequest { Physical: true } or
            CaptureScreenshotRequest { Mode: "screen" or "auto" })
            AllowSetForegroundWindow((uint)processId);

        try
        {
            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(_connectionTimeout);
            await pipeClient.ConnectAsync(cts.Token);

            using var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var requestJson = IpcSerializer.SerializeRequest(request);
            await writer.WriteLineAsync(requestJson);

            using var readCts = new CancellationTokenSource(request is ReplayRequest replay
                ? TimeSpan.FromMilliseconds(replay.TimeoutMs + 15000) : _requestTimeout);
            var responseJson = await reader.ReadLineAsync(readCts.Token);

            if (string.IsNullOrEmpty(responseJson))
            {
                return new TResponse { Success = false, Error = "Empty response from inspector" };
            }

            var response = IpcSerializer.DeserializeResponse<TResponse>(responseJson);
            return response ?? new TResponse { Success = false, Error = "Failed to parse response" };
        }
        catch (TimeoutException)
        {
            var errorMsg = $"Connection to process {processId} timed out. The Inspector may not be loaded. Try restarting the application or use wpf_list_processes() and wpf_attach() to reconnect.";
            _logger.LogWarning(errorMsg);
            return new TResponse { Success = false, Error = errorMsg };
        }
        catch (IOException ex)
        {
            var errorMsg = $"Cannot connect to process {processId}: {ex.Message}. The named pipe may not exist. Use wpf_list_processes() to see available WPF applications, then wpf_attach(process_id=<new_pid>) to connect.";
            _logger.LogWarning(ex, errorMsg);
            return new TResponse { Success = false, Error = errorMsg };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending request to inspector");
            return new TResponse { Success = false, Error = ex.Message };
        }
    }

    private InspectionSession EnsureConnected()
    {
        var session = _processManager.CurrentSession;
        if (session == null)
        {
            throw new McpException("Not attached to any WPF process. Use wpf_attach first.");
        }
        if (!session.InspectorReady)
        {
            throw new McpException(
                $"Inspector unavailable in process {session.ProcessId}: {session.InspectorStatus}. " +
                "Resolve the attach failure and retry wpf_attach before calling inspection tools.");
        }
        return session;
    }

    #region Response Parsers

    private VisualTreeResult ParseVisualTreeResponse(GetVisualTreeResponse response)
    {
        if (string.IsNullOrEmpty(response.TreeJson))
        {
            return new VisualTreeResult
            {
                Root = new VisualTreeNode { Handle = "empty", TypeName = "Unknown" },
                TotalElements = 0,
                MaxDepthReached = false
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(response.TreeJson);
            var root = doc.RootElement;

            var rootNode = ParseVisualTreeNode(root.GetProperty("root"));

            return new VisualTreeResult
            {
                Root = rootNode,
                TotalElements = response.TotalElements,
                MaxDepthReached = response.MaxDepthReached
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse visual tree JSON");
            return new VisualTreeResult
            {
                Root = new VisualTreeNode { Handle = "parse_error", TypeName = "Error", Name = ex.Message },
                TotalElements = 0,
                MaxDepthReached = false
            };
        }
    }

    private VisualTreeNode ParseVisualTreeNode(JsonElement element)
    {
        var node = new VisualTreeNode
        {
            Handle = element.GetProperty("handle").GetString() ?? "",
            TypeName = element.GetProperty("typeName").GetString() ?? "",
            Name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
            Children = new List<VisualTreeNode>()
        };
        if (element.TryGetProperty("nativeContent", out var native))
            node.NativeContent = IpcSerializer.Deserialize<NativeContentInfo>(native.GetRawText());

        if (element.TryGetProperty("children", out var childrenProp))
        {
            foreach (var childElement in childrenProp.EnumerateArray())
            {
                node.Children.Add(ParseVisualTreeNode(childElement));
            }
        }

        return node;
    }

    internal ElementPropertiesResult ParseElementPropertiesResponse(GetElementPropertiesResponse response, string handle)
    {
        var result = new ElementPropertiesResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Properties = new List<PropertyInfo>()
        };

        if (string.IsNullOrEmpty(response.PropertiesJson))
        {
            throw new McpException("Inspector returned no property data. Reattach with an updated Inspector and retry.");
        }

        try
        {
            using var doc = JsonDocument.Parse(response.PropertiesJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("element", out _) || !root.TryGetProperty("properties", out var propertyList) || propertyList.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected element metadata and a properties array.");

            if (root.TryGetProperty("element", out var elem))
            {
                result.Element.TypeName = elem.TryGetProperty("typeName", out var tp)
                    ? tp.GetString() ?? "Unknown" : "Unknown";
                result.Element.Name = elem.TryGetProperty("name", out var en) ? en.GetString() : null;
            }

            if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in props.EnumerateArray())
                {
                    result.Properties.Add(new PropertyInfo
                    {
                        Name = prop.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        TypeName = prop.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
                        OwnerType = prop.TryGetProperty("ownerType", out var owner) ? owner.GetString() : null,
                        Value = prop.TryGetProperty("value", out var v) ? v.ValueKind switch
                        {
                            JsonValueKind.Null => null,
                            JsonValueKind.String => v.GetString(),
                            _ => v.GetRawText()
                        } : null,
                        Source = prop.TryGetProperty("source", out var s) ? s.GetString() ?? "Unknown" : "Unknown",
                        IsBinding = prop.TryGetProperty("isBinding", out var binding) && binding.GetBoolean()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse properties JSON");
            throw new McpException("Inspector returned invalid property data. Update the Inspector and restart the target application before reattaching. " + ex.Message);
        }

        return result;
    }

    private FindElementsResult ParseFindElementsResponse(FindElementsResponse response)
    {
        var result = new FindElementsResult
        {
            Elements = new List<FoundElement>(),
            Count = response.Count
        };

        if (string.IsNullOrEmpty(response.ElementsJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.ElementsJson);
            var root = doc.RootElement;

            // New format: {"elements":[...], "count":N}
            if (root.TryGetProperty("elements", out var elementsArray))
            {
                foreach (var elem in elementsArray.EnumerateArray())
                {
                    result.Elements.Add(ParseFoundElement(elem));
                }
            }
            else
            {
                // Old format: plain array (backward compatibility)
                foreach (var elem in root.EnumerateArray())
                {
                    result.Elements.Add(ParseFoundElement(elem));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse elements JSON");
        }

        return result;
    }

    private static FoundElement ParseFoundElement(JsonElement elem)
    {
        var found = new FoundElement
        {
            Handle = elem.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "",
            TypeName = elem.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
            Name = elem.TryGetProperty("name", out var n) ? n.GetString() : null,
            Text = elem.TryGetProperty("text", out var txt) ? txt.GetString() : null,
            AutomationId = elem.TryGetProperty("automationId", out var aid) ? aid.GetString() : null,
            Path = elem.TryGetProperty("path", out var p) ? p.GetString() ?? "" : ""
        };

        if (elem.TryGetProperty("nativeContent", out var native))
            found.NativeContent = IpcSerializer.Deserialize<NativeContentInfo>(native.GetRawText());

        if (elem.TryGetProperty("isVisible", out var vis) &&
            (vis.ValueKind == JsonValueKind.True || vis.ValueKind == JsonValueKind.False))
        {
            found.IsVisible = vis.GetBoolean();
        }

        if (elem.TryGetProperty("isEnabled", out var en) &&
            (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
        {
            found.IsEnabled = en.GetBoolean();
        }

        if (elem.TryGetProperty("screenBounds", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            found.ScreenBounds = new ScreenBounds
            {
                X = b.TryGetProperty("x", out var x) ? x.GetInt32() : 0,
                Y = b.TryGetProperty("y", out var y) ? y.GetInt32() : 0,
                Width = b.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                Height = b.TryGetProperty("height", out var hh) ? hh.GetInt32() : 0
            };
        }

        return found;
    }

    private FindElementsResult ParseFindElementsDeepResponse(FindElementsDeepResponse response)
    {
        var result = new FindElementsResult
        {
            Elements = new List<FoundElement>(),
            Count = response.Count
        };

        if (string.IsNullOrEmpty(response.ElementsJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.ElementsJson);
            var root = doc.RootElement;

            // Format: {"elements":[...], "count":N}
            if (root.TryGetProperty("elements", out var elementsArray))
            {
                foreach (var elem in elementsArray.EnumerateArray())
                {
                    result.Elements.Add(ParseFoundElement(elem));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse elements deep search JSON");
        }

        return result;
    }

    private BindingsResult ParseBindingsResponse(GetBindingsResponse response, string handle)
    {
        var result = new BindingsResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Bindings = new List<BindingInfo>()
        };

        if (string.IsNullOrEmpty(response.BindingsJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.BindingsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("bindings", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
            {
                foreach (var binding in bindings.EnumerateArray())
                {
                    result.Bindings.Add(new BindingInfo
                    {
                        Property = binding.TryGetProperty("property", out var prop) ? prop.GetString() ?? "" : "",
                        Path = binding.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "",
                        Source = binding.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                        Mode = binding.TryGetProperty("mode", out var mode) ? mode.GetString() ?? "" : "",
                        UpdateTrigger = binding.TryGetProperty("updateTrigger", out var trigger) ? trigger.GetString() ?? "" : "",
                        Status = binding.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
                        CurrentValue = binding.TryGetProperty("currentValue", out var val) ? val.GetString() : null
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse bindings JSON");
        }

        return result;
    }

    private BindingErrorsResult ParseBindingErrorsResponse(GetBindingErrorsResponse response)
    {
        var result = new BindingErrorsResult
        {
            Errors = new List<BindingError>(),
            Count = response.Count
        };

        if (string.IsNullOrEmpty(response.ErrorsJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.ErrorsJson);
            // Inspector format: {"errors":[...],"count":N}; plain array kept for compatibility.
            var errorsArray = doc.RootElement.ValueKind == JsonValueKind.Object &&
                              doc.RootElement.TryGetProperty("errors", out var e)
                ? e
                : doc.RootElement;
            foreach (var error in errorsArray.EnumerateArray())
            {
                result.Errors.Add(new BindingError
                {
                    ElementType = error.TryGetProperty("elementType", out var et) ? et.GetString() ?? "" : "",
                    ElementName = error.TryGetProperty("elementName", out var en) ? en.GetString() : null,
                    Property = error.TryGetProperty("property", out var p) ? p.GetString() ?? "" : "",
                    BindingPath = error.TryGetProperty("bindingPath", out var bp) ? bp.GetString() ?? "" : "",
                    Message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                    Timestamp = error.TryGetProperty("timestamp", out var ts)
                        ? DateTime.Parse(ts.GetString() ?? DateTime.UtcNow.ToString())
                        : DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse binding errors JSON");
        }

        return result;
    }

    private ResourcesResult ParseResourcesResponse(GetResourcesResponse response)
    {
        var result = new ResourcesResult
        {
            Resources = new List<ResourceInfo>()
        };

        if (string.IsNullOrEmpty(response.ResourcesJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.ResourcesJson);
            foreach (var resource in doc.RootElement.EnumerateArray())
            {
                result.Resources.Add(new ResourceInfo
                {
                    Key = resource.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                    TypeName = resource.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
                    Value = resource.TryGetProperty("value", out var v) ? v.GetString() : null,
                    Source = resource.TryGetProperty("source", out var s) ? s.GetString() ?? "" : ""
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse resources JSON");
        }

        return result;
    }

    private StylesResult ParseStylesResponse(GetStylesResponse response, string handle)
    {
        var result = new StylesResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Style = new StyleInfo
            {
                Key = "",
                TargetType = "",
                Setters = new List<SetterInfo>()
            }
        };

        if (string.IsNullOrEmpty(response.StylesJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.StylesJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("style", out var style))
            {
                result.Style.Key = style.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                result.Style.TargetType = style.TryGetProperty("targetType", out var t) ? t.GetString() ?? "" : "";

                if (style.TryGetProperty("setters", out var setters) && setters.ValueKind == JsonValueKind.Array)
                {
                    foreach (var setter in setters.EnumerateArray())
                    {
                        result.Style.Setters.Add(new SetterInfo
                        {
                            Property = setter.TryGetProperty("property", out var p) ? p.GetString() ?? "" : "",
                            Value = setter.TryGetProperty("value", out var v) ? v.GetString() : null
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse styles JSON");
        }

        return result;
    }

    internal LayoutInfoResult ParseLayoutInfoResponse(GetLayoutInfoResponse response, string handle)
    {
        var result = new LayoutInfoResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Layout = new LayoutInfo()
        };

        if (string.IsNullOrEmpty(response.LayoutJson))
        {
            throw new McpException("Inspector returned no layout data.");
        }

        try
        {
            using var doc = JsonDocument.Parse(response.LayoutJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("element", out _) || !root.TryGetProperty("layout", out var layoutData) || layoutData.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected element metadata and a layout object.");

            if (root.TryGetProperty("layout", out var layout))
            {
                if (root.TryGetProperty("element", out var element))
                {
                    result.Element.TypeName = element.GetProperty("typeName").GetString() ?? "Unknown";
                    result.Element.Name = element.TryGetProperty("name", out var name) ? name.GetString() : null;
                }
                result.Layout.ActualWidth = layout.TryGetProperty("actualWidth", out var aw) ? aw.GetDouble() : 0;
                result.Layout.ActualHeight = layout.TryGetProperty("actualHeight", out var ah) ? ah.GetDouble() : 0;
                result.Layout.HorizontalAlignment = layout.TryGetProperty("horizontalAlignment", out var ha) ? ha.GetString() ?? "" : "";
                result.Layout.VerticalAlignment = layout.TryGetProperty("verticalAlignment", out var va) ? va.GetString() ?? "" : "";
                result.Layout.Visibility = layout.TryGetProperty("visibility", out var vis) ? vis.GetString() ?? "" : "";

                if (layout.TryGetProperty("desiredSize", out var ds))
                {
                    result.Layout.DesiredSize = new SizeInfo
                    {
                        Width = ds.TryGetProperty("width", out var w) ? w.GetDouble() : 0,
                        Height = ds.TryGetProperty("height", out var h) ? h.GetDouble() : 0
                    };
                }

                if (layout.TryGetProperty("renderSize", out var rs))
                {
                    result.Layout.RenderSize = new SizeInfo
                    {
                        Width = rs.TryGetProperty("width", out var w) ? w.GetDouble() : 0,
                        Height = rs.TryGetProperty("height", out var h) ? h.GetDouble() : 0
                    };
                }

                if (layout.TryGetProperty("margin", out var margin))
                {
                    result.Layout.Margin = new ThicknessInfo
                    {
                        Left = margin.TryGetProperty("left", out var l) ? l.GetDouble() : 0,
                        Top = margin.TryGetProperty("top", out var t) ? t.GetDouble() : 0,
                        Right = margin.TryGetProperty("right", out var r) ? r.GetDouble() : 0,
                        Bottom = margin.TryGetProperty("bottom", out var b) ? b.GetDouble() : 0
                    };
                }

                if (layout.TryGetProperty("padding", out var padding))
                {
                    result.Layout.Padding = new ThicknessInfo
                    {
                        Left = padding.TryGetProperty("left", out var l) ? l.GetDouble() : 0,
                        Top = padding.TryGetProperty("top", out var t) ? t.GetDouble() : 0,
                        Right = padding.TryGetProperty("right", out var r) ? r.GetDouble() : 0,
                        Bottom = padding.TryGetProperty("bottom", out var b) ? b.GetDouble() : 0
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse layout info JSON");
            throw new McpException("Inspector returned invalid layout data: " + ex.Message);
        }

        return result;
    }

    #endregion
}
