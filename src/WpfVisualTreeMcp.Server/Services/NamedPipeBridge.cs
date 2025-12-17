using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Implementation of IPC bridge using named pipes.
/// Communicates with the Inspector DLL injected into the target WPF process.
/// </summary>
public class NamedPipeBridge : IIpcBridge
{
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
            throw new InvalidOperationException(response.Error ?? "Failed to get visual tree");
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
            throw new InvalidOperationException(response.Error ?? "Failed to get element properties");
        }

        return ParseElementPropertiesResponse(response, elementHandle);
    }

    public async Task<FindElementsResult> FindElementsAsync(string? rootHandle, string? typeName, string? elementName, Dictionary<string, string>? propertyFilter, int maxResults = 50)
    {
        var session = EnsureConnected();

        var request = new FindElementsRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            PropertyFilter = propertyFilter,
            MaxResults = maxResults
        };

        var response = await SendRequestAsync<FindElementsRequest, FindElementsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "Failed to find elements");
        }

        return ParseFindElementsResponse(response);
    }

    public async Task<FindElementsResult> FindElementsDeepAsync(string? rootHandle, string? typeName, string? elementName)
    {
        var session = EnsureConnected();

        var request = new FindElementsDeepRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName
        };

        var response = await SendRequestAsync<FindElementsDeepRequest, FindElementsDeepResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "Failed to find elements (deep search)");
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
            throw new InvalidOperationException(response.Error ?? "Failed to get bindings");
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
            throw new InvalidOperationException(response.Error ?? "Failed to get binding errors");
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
            throw new InvalidOperationException(response.Error ?? "Failed to get resources");
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
            throw new InvalidOperationException(response.Error ?? "Failed to get styles");
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
            throw new InvalidOperationException(response.Error ?? "Failed to watch property");
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
            throw new InvalidOperationException(response.Error ?? "Failed to highlight element");
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
            throw new InvalidOperationException(response.Error ?? "Failed to get layout info");
        }

        return ParseLayoutInfoResponse(response, elementHandle);
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
            throw new InvalidOperationException(response.Error ?? "Failed to export tree");
        }

        return new ExportResult
        {
            Format = format,
            Content = response.Content ?? "",
            ElementCount = response.ElementCount
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

            using var readCts = new CancellationTokenSource(_requestTimeout);
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
            throw new InvalidOperationException("Not attached to any WPF process. Use wpf_attach first.");
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

        if (element.TryGetProperty("children", out var childrenProp))
        {
            foreach (var childElement in childrenProp.EnumerateArray())
            {
                node.Children.Add(ParseVisualTreeNode(childElement));
            }
        }

        return node;
    }

    private ElementPropertiesResult ParseElementPropertiesResponse(GetElementPropertiesResponse response, string handle)
    {
        var result = new ElementPropertiesResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Properties = new List<PropertyInfo>()
        };

        if (string.IsNullOrEmpty(response.PropertiesJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.PropertiesJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("element", out var elem))
            {
                result.Element.TypeName = elem.TryGetProperty("typeName", out var tp)
                    ? tp.GetString() ?? "Unknown" : "Unknown";
            }

            if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in props.EnumerateArray())
                {
                    result.Properties.Add(new PropertyInfo
                    {
                        Name = prop.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        TypeName = prop.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
                        Value = prop.TryGetProperty("value", out var v) ? v.GetString() : null,
                        Source = prop.TryGetProperty("source", out var s) ? s.GetString() ?? "Unknown" : "Unknown"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse properties JSON");
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
                    result.Elements.Add(new FoundElement
                    {
                        Handle = elem.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "",
                        TypeName = elem.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
                        Name = elem.TryGetProperty("name", out var n) ? n.GetString() : null,
                        Path = elem.TryGetProperty("path", out var p) ? p.GetString() ?? "" : ""
                    });
                }
            }
            else
            {
                // Old format: plain array (backward compatibility)
                foreach (var elem in root.EnumerateArray())
                {
                    result.Elements.Add(new FoundElement
                    {
                        Handle = elem.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "",
                        TypeName = elem.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
                        Name = elem.TryGetProperty("name", out var n) ? n.GetString() : null,
                        Path = elem.TryGetProperty("path", out var p) ? p.GetString() ?? "" : ""
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse elements JSON");
        }

        return result;
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
                    result.Elements.Add(new FoundElement
                    {
                        Handle = elem.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "",
                        TypeName = elem.TryGetProperty("typeName", out var t) ? t.GetString() ?? "" : "",
                        Name = elem.TryGetProperty("name", out var n) ? n.GetString() : null,
                        Path = elem.TryGetProperty("path", out var p) ? p.GetString() ?? "" : ""
                    });
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
            foreach (var error in doc.RootElement.EnumerateArray())
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

    private LayoutInfoResult ParseLayoutInfoResponse(GetLayoutInfoResponse response, string handle)
    {
        var result = new LayoutInfoResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Layout = new LayoutInfo()
        };

        if (string.IsNullOrEmpty(response.LayoutJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.LayoutJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("layout", out var layout))
            {
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
        }

        return result;
    }

    #endregion
}
