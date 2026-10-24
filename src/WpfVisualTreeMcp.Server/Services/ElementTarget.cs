using WpfVisualTreeMcp.Automation.Infrastructure;
using ModelContextProtocol;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>Shared target validation for MCP and CLI, before any input is sent.</summary>
internal static class ElementTarget
{
    // The returned identifier is only for validating option geometry before IPC.
    // Callers must resolve it to a handle before sending an action to the Inspector.
    internal static string Validate(string? handle, string? automationId, string? rootHandle)
    {
        if ((handle == null) == (automationId == null))
            throw new McpException("Specify exactly one of element_handle or automation_id (CLI: --handle or --automation-id). For a drag destination use target_handle or target_automation_id.");
        if (handle != null)
        {
            if (string.IsNullOrWhiteSpace(handle)) throw new McpException("element_handle cannot be blank.");
            if (rootHandle != null) throw new McpException("root_handle is only supported with automation_id.");
            return handle;
        }
        if (string.IsNullOrWhiteSpace(automationId) || automationId!.Length > 2048)
            throw new McpException("automation_id must contain 1-2048 characters and cannot be blank.");
        return automationId;
    }

    internal static string? ValidateOptional(string? handle, string? automationId, string? rootHandle) =>
        handle == null && automationId == null && rootHandle == null ? null : Validate(handle, automationId, rootHandle);

    internal static async Task<string?> ResolveOptionalAsync(IIpcBridge bridge, string? handle, string? automationId, string? rootHandle)
    {
        if (ValidateOptional(handle, automationId, rootHandle) == null) return null;
        return await bridge.ResolveElementHandleAsync(handle, automationId, rootHandle);
    }
}
