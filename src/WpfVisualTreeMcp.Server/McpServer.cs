using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Server.Tools;

namespace WpfVisualTreeMcp.Server;

/// <summary>
/// Main MCP (Model Context Protocol) server implementation.
/// Handles JSON-RPC communication over stdio and dispatches to tool handlers.
/// </summary>
public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly IProcessManager _processManager;
    private readonly IIpcBridge _ipcBridge;
    private readonly Dictionary<string, Func<JsonElement?, Task<object>>> _tools;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "wpf-visual-tree";
    private const string ServerVersion = "0.1.0";

    public McpServer(
        ILogger<McpServer> logger,
        IProcessManager processManager,
        IIpcBridge ipcBridge)
    {
        _logger = logger;
        _processManager = processManager;
        _ipcBridge = ipcBridge;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        // Register tool handlers
        _tools = new Dictionary<string, Func<JsonElement?, Task<object>>>
        {
            ["wpf_list_processes"] = ListWpfProcessesAsync,
            ["wpf_attach"] = AttachToProcessAsync,
            ["wpf_get_visual_tree"] = GetVisualTreeAsync,
            ["wpf_get_element_properties"] = GetElementPropertiesAsync,
            ["wpf_find_elements"] = FindElementsAsync,
            ["wpf_get_bindings"] = GetBindingsAsync,
            ["wpf_get_binding_errors"] = GetBindingErrorsAsync,
            ["wpf_get_resources"] = GetResourcesAsync,
            ["wpf_get_styles"] = GetStylesAsync,
            ["wpf_watch_property"] = WatchPropertyAsync,
            ["wpf_highlight_element"] = HighlightElementAsync,
            ["wpf_get_layout_info"] = GetLayoutInfoAsync,
            ["wpf_export_tree"] = ExportTreeAsync
        };
    }

    /// <summary>
    /// Run the MCP server, reading from input and writing to output.
    /// </summary>
    public async Task RunAsync(Stream input, Stream output)
    {
        using var reader = new StreamReader(input, Encoding.UTF8);
        await using var writer = new StreamWriter(output, Encoding.UTF8) { AutoFlush = true };

        _logger.LogInformation("MCP Server running, waiting for messages...");

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                _logger.LogInformation("Input stream closed, shutting down");
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var response = await HandleMessageAsync(line);
                if (response != null)
                {
                    await writer.WriteLineAsync(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message: {Message}", line);
            }
        }
    }

    private async Task<string?> HandleMessageAsync(string message)
    {
        _logger.LogDebug("Received: {Message}", message);

        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        if (!root.TryGetProperty("jsonrpc", out _))
        {
            return CreateErrorResponse(null, -32600, "Invalid Request: missing jsonrpc");
        }

        var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
        var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;

        if (method == null)
        {
            return CreateErrorResponse(id, -32600, "Invalid Request: missing method");
        }

        var @params = root.TryGetProperty("params", out var paramsElement) ? paramsElement.Clone() : (JsonElement?)null;

        try
        {
            var result = method switch
            {
                "initialize" => HandleInitialize(@params),
                "initialized" => HandleInitialized(),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolCallAsync(@params),
                "ping" => HandlePing(),
                _ => throw new McpException(-32601, $"Method not found: {method}")
            };

            // Notifications (no id) don't get responses
            if (id == null)
                return null;

            return CreateSuccessResponse(id.Value, result);
        }
        catch (McpException ex)
        {
            return CreateErrorResponse(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling method {Method}", method);
            return CreateErrorResponse(id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private object HandleInitialize(JsonElement? @params)
    {
        _logger.LogInformation("Initializing MCP connection");

        return new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = ServerName,
                version = ServerVersion
            }
        };
    }

    private object? HandleInitialized()
    {
        _logger.LogInformation("MCP connection initialized");
        return null;
    }

    private object HandlePing()
    {
        return new { };
    }

    private object HandleToolsList()
    {
        return new
        {
            tools = GetToolDefinitions()
        };
    }

    private async Task<object> HandleToolCallAsync(JsonElement? @params)
    {
        if (@params == null)
            throw new McpException(-32602, "Invalid params: missing params");

        var paramsValue = @params.Value;

        if (!paramsValue.TryGetProperty("name", out var nameElement))
            throw new McpException(-32602, "Invalid params: missing name");

        var toolName = nameElement.GetString();
        if (string.IsNullOrEmpty(toolName))
            throw new McpException(-32602, "Invalid params: empty name");

        if (!_tools.TryGetValue(toolName, out var handler))
            throw new McpException(-32602, $"Unknown tool: {toolName}");

        var arguments = paramsValue.TryGetProperty("arguments", out var argsElement)
            ? argsElement
            : (JsonElement?)null;

        _logger.LogInformation("Calling tool: {ToolName}", toolName);

        try
        {
            var result = await handler(arguments);
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(result, _jsonOptions)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} failed", toolName);
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = $"Error: {ex.Message}"
                    }
                },
                isError = true
            };
        }
    }

    private string CreateSuccessResponse(JsonElement id, object? result)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            result = result
        };
        return JsonSerializer.Serialize(response, _jsonOptions);
    }

    private string CreateErrorResponse(JsonElement? id, int code, string message)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            error = new { code, message }
        };
        return JsonSerializer.Serialize(response, _jsonOptions);
    }

    private object[] GetToolDefinitions()
    {
        return new object[]
        {
            new
            {
                name = "wpf_list_processes",
                description = "List all running WPF applications available for inspection",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "wpf_attach",
                description = "Attach to a WPF application by process ID or name",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        process_id = new { type = "integer", description = "Process ID to attach to" },
                        process_name = new { type = "string", description = "Process name to attach to" }
                    }
                }
            },
            new
            {
                name = "wpf_get_visual_tree",
                description = "Get the visual tree hierarchy starting from a root element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        root_handle = new { type = "string", description = "Handle of root element (optional, defaults to main window)" },
                        max_depth = new { type = "integer", description = "Maximum tree depth", @default = 10 }
                    }
                }
            },
            new
            {
                name = "wpf_get_element_properties",
                description = "Get all dependency properties of a UI element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of the element to inspect" }
                    },
                    required = new[] { "element_handle" }
                }
            },
            new
            {
                name = "wpf_find_elements",
                description = "Search for elements by type, name, or property value",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        type_name = new { type = "string", description = "Element type name (e.g., 'Button', 'TextBox')" },
                        element_name = new { type = "string", description = "x:Name of the element" },
                        property_filter = new { type = "object", description = "Property name/value pairs to filter by" }
                    }
                }
            },
            new
            {
                name = "wpf_get_bindings",
                description = "Get all data bindings for an element with their status",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of the element to inspect" }
                    },
                    required = new[] { "element_handle" }
                }
            },
            new
            {
                name = "wpf_get_binding_errors",
                description = "List all binding errors in the application"
            },
            new
            {
                name = "wpf_get_resources",
                description = "Enumerate resource dictionaries and their contents",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        scope = new { type = "string", description = "Scope: 'application', 'window', or 'element'", @enum = new[] { "application", "window", "element" } },
                        element_handle = new { type = "string", description = "Required when scope is 'element'" }
                    }
                }
            },
            new
            {
                name = "wpf_get_styles",
                description = "Get applied styles and templates for an element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of the element to inspect" }
                    },
                    required = new[] { "element_handle" }
                }
            },
            new
            {
                name = "wpf_watch_property",
                description = "Monitor a property for changes",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of the element" },
                        property_name = new { type = "string", description = "Name of the property to watch" }
                    },
                    required = new[] { "element_handle", "property_name" }
                }
            },
            new
            {
                name = "wpf_highlight_element",
                description = "Visually highlight an element in the running application",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of the element to highlight" },
                        duration_ms = new { type = "integer", description = "Duration in milliseconds", @default = 2000 }
                    },
                    required = new[] { "element_handle" }
                }
            },
            new
            {
                name = "wpf_get_layout_info",
                description = "Get layout information (ActualWidth, ActualHeight, Margin, etc.)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of the element" }
                    },
                    required = new[] { "element_handle" }
                }
            },
            new
            {
                name = "wpf_export_tree",
                description = "Export visual tree to XAML or JSON",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        element_handle = new { type = "string", description = "Handle of root element (optional)" },
                        format = new { type = "string", description = "Export format", @enum = new[] { "json", "xaml" }, @default = "json" }
                    }
                }
            }
        };
    }

    #region Tool Implementations

    private async Task<object> ListWpfProcessesAsync(JsonElement? args)
    {
        var processes = await _processManager.GetWpfProcessesAsync();
        return new
        {
            processes = processes.Select(p => new
            {
                process_id = p.ProcessId,
                process_name = p.ProcessName,
                main_window_title = p.MainWindowTitle,
                is_attached = p.IsAttached,
                dotnet_version = p.DotNetVersion
            })
        };
    }

    private async Task<object> AttachToProcessAsync(JsonElement? args)
    {
        int? processId = null;
        string? processName = null;

        if (args.HasValue)
        {
            if (args.Value.TryGetProperty("process_id", out var pidElement))
                processId = pidElement.GetInt32();
            if (args.Value.TryGetProperty("process_name", out var nameElement))
                processName = nameElement.GetString();
        }

        if (processId == null && string.IsNullOrEmpty(processName))
        {
            throw new ArgumentException("Either process_id or process_name must be provided");
        }

        var session = await _processManager.AttachToProcessAsync(processId, processName);
        return new
        {
            success = true,
            process_id = session.ProcessId,
            session_id = session.SessionId,
            main_window_handle = session.MainWindowHandle
        };
    }

    private async Task<object> GetVisualTreeAsync(JsonElement? args)
    {
        string? rootHandle = null;
        int maxDepth = 10;

        if (args.HasValue)
        {
            if (args.Value.TryGetProperty("root_handle", out var handleElement))
                rootHandle = handleElement.GetString();
            if (args.Value.TryGetProperty("max_depth", out var depthElement))
                maxDepth = depthElement.GetInt32();
        }

        var tree = await _ipcBridge.GetVisualTreeAsync(rootHandle, maxDepth);
        return tree;
    }

    private async Task<object> GetElementPropertiesAsync(JsonElement? args)
    {
        if (!args.HasValue || !args.Value.TryGetProperty("element_handle", out var handleElement))
        {
            throw new ArgumentException("element_handle is required");
        }

        var handle = handleElement.GetString()!;
        var properties = await _ipcBridge.GetElementPropertiesAsync(handle);
        return properties;
    }

    private async Task<object> FindElementsAsync(JsonElement? args)
    {
        string? typeName = null;
        string? elementName = null;
        Dictionary<string, string>? propertyFilter = null;

        if (args.HasValue)
        {
            if (args.Value.TryGetProperty("type_name", out var typeElement))
                typeName = typeElement.GetString();
            if (args.Value.TryGetProperty("element_name", out var nameElement))
                elementName = nameElement.GetString();
            if (args.Value.TryGetProperty("property_filter", out var filterElement))
            {
                propertyFilter = new Dictionary<string, string>();
                foreach (var prop in filterElement.EnumerateObject())
                {
                    propertyFilter[prop.Name] = prop.Value.ToString();
                }
            }
        }

        var elements = await _ipcBridge.FindElementsAsync(typeName, elementName, propertyFilter);
        return elements;
    }

    private async Task<object> GetBindingsAsync(JsonElement? args)
    {
        if (!args.HasValue || !args.Value.TryGetProperty("element_handle", out var handleElement))
        {
            throw new ArgumentException("element_handle is required");
        }

        var handle = handleElement.GetString()!;
        var bindings = await _ipcBridge.GetBindingsAsync(handle);
        return bindings;
    }

    private async Task<object> GetBindingErrorsAsync(JsonElement? args)
    {
        var errors = await _ipcBridge.GetBindingErrorsAsync();
        return errors;
    }

    private async Task<object> GetResourcesAsync(JsonElement? args)
    {
        string scope = "application";
        string? elementHandle = null;

        if (args.HasValue)
        {
            if (args.Value.TryGetProperty("scope", out var scopeElement))
                scope = scopeElement.GetString() ?? "application";
            if (args.Value.TryGetProperty("element_handle", out var handleElement))
                elementHandle = handleElement.GetString();
        }

        var resources = await _ipcBridge.GetResourcesAsync(scope, elementHandle);
        return resources;
    }

    private async Task<object> GetStylesAsync(JsonElement? args)
    {
        if (!args.HasValue || !args.Value.TryGetProperty("element_handle", out var handleElement))
        {
            throw new ArgumentException("element_handle is required");
        }

        var handle = handleElement.GetString()!;
        var styles = await _ipcBridge.GetStylesAsync(handle);
        return styles;
    }

    private async Task<object> WatchPropertyAsync(JsonElement? args)
    {
        if (!args.HasValue)
            throw new ArgumentException("element_handle and property_name are required");

        if (!args.Value.TryGetProperty("element_handle", out var handleElement))
            throw new ArgumentException("element_handle is required");
        if (!args.Value.TryGetProperty("property_name", out var propElement))
            throw new ArgumentException("property_name is required");

        var handle = handleElement.GetString()!;
        var propertyName = propElement.GetString()!;

        var watchId = await _ipcBridge.WatchPropertyAsync(handle, propertyName);
        return new
        {
            watch_id = watchId,
            element_handle = handle,
            property_name = propertyName
        };
    }

    private async Task<object> HighlightElementAsync(JsonElement? args)
    {
        if (!args.HasValue || !args.Value.TryGetProperty("element_handle", out var handleElement))
        {
            throw new ArgumentException("element_handle is required");
        }

        var handle = handleElement.GetString()!;
        int durationMs = 2000;

        if (args.Value.TryGetProperty("duration_ms", out var durationElement))
            durationMs = durationElement.GetInt32();

        await _ipcBridge.HighlightElementAsync(handle, durationMs);
        return new
        {
            success = true,
            element_handle = handle,
            duration_ms = durationMs
        };
    }

    private async Task<object> GetLayoutInfoAsync(JsonElement? args)
    {
        if (!args.HasValue || !args.Value.TryGetProperty("element_handle", out var handleElement))
        {
            throw new ArgumentException("element_handle is required");
        }

        var handle = handleElement.GetString()!;
        var layoutInfo = await _ipcBridge.GetLayoutInfoAsync(handle);
        return layoutInfo;
    }

    private async Task<object> ExportTreeAsync(JsonElement? args)
    {
        string? elementHandle = null;
        string format = "json";

        if (args.HasValue)
        {
            if (args.Value.TryGetProperty("element_handle", out var handleElement))
                elementHandle = handleElement.GetString();
            if (args.Value.TryGetProperty("format", out var formatElement))
                format = formatElement.GetString() ?? "json";
        }

        var export = await _ipcBridge.ExportTreeAsync(elementHandle, format);
        return export;
    }

    #endregion
}

/// <summary>
/// Exception type for MCP protocol errors.
/// </summary>
public class McpException : Exception
{
    public int Code { get; }

    public McpException(int code, string message) : base(message)
    {
        Code = code;
    }
}
