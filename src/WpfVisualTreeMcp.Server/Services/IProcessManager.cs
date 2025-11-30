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
}

/// <summary>
/// Information about an active inspection session.
/// </summary>
public record InspectionSession
{
    public string SessionId { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string MainWindowHandle { get; init; } = string.Empty;
    public DateTime AttachedAt { get; init; }
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
    /// <returns>The inspection session.</returns>
    Task<InspectionSession> AttachToProcessAsync(int? processId, string? processName);

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
