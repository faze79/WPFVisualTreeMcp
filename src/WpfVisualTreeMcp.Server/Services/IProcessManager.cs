namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Information about a WPF process.
/// </summary>
public record WpfProcessInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? MainWindowTitle { get; init; }
    public bool IsAttached { get; init; }
    public string? DotNetVersion { get; init; }
    public string? RuntimeType { get; init; }  // "Framework" or "CoreCLR"
}

/// <summary>
/// Information about an active inspection session.
/// </summary>
public class InspectionSession
{
    public string SessionId { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string MainWindowHandle { get; set; } = string.Empty;
    public DateTime AttachedAt { get; set; }
    public string InspectorStatus { get; set; } = "Unknown";
}

/// <summary>
/// Manages WPF process discovery and attachment.
/// </summary>
public interface IProcessManager
{
    /// <summary>
    /// Gets all running WPF processes that can be inspected.
    /// </summary>
    Task<IReadOnlyList<WpfProcessInfo>> GetWpfProcessesAsync();

    /// <summary>
    /// Attaches to a WPF process for inspection.
    /// </summary>
    /// <param name="processId">Process ID to attach to (optional if processName is provided).</param>
    /// <param name="processName">Process name to attach to (optional if processId is provided).</param>
    /// <param name="autoInject">If true, automatically inject the Inspector DLL if not already loaded.</param>
    /// <returns>The inspection session.</returns>
    Task<InspectionSession> AttachToProcessAsync(int? processId, string? processName, bool autoInject = false);

    /// <summary>
    /// Detaches from a WPF process.
    /// </summary>
    /// <param name="sessionId">Session ID to detach.</param>
    Task DetachAsync(string sessionId);

    /// <summary>
    /// Gets the current active session, if any.
    /// </summary>
    InspectionSession? CurrentSession { get; }
}
