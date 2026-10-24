using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using WpfVisualTreeMcp.Automation.Infrastructure;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>Exposes shared engine errors as MCP tool errors.</summary>
public sealed class NamedPipeBridge : InspectorBridge
{
    public NamedPipeBridge(ILogger<NamedPipeBridge> logger, IProcessManager processManager) : base(logger, processManager) { }
    protected override Exception CreateException(string message) => new McpException(message);
}
