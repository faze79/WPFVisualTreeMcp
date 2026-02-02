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
    public bool IsInjected { get; init; }
    public string? DotNetVersion { get; init; }
}

/// <summary>
/// Result of an injection attempt.
/// </summary>
public record InjectionResult
{
    public bool Success { get; init; }
    public int ProcessId { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public bool AlreadyInjected { get; init; }
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

    /// <summary>
    /// Injects the Inspector DLL into a running WPF process.
    /// This allows inspecting applications that don't have the Inspector pre-loaded.
    /// </summary>
    /// <param name="processId">Process ID to inject into.</param>
    /// <returns>The injection result.</returns>
    Task<InjectionResult> InjectIntoProcessAsync(int processId);

    /// <summary>
    /// Checks if a process has the Inspector already loaded.
    /// </summary>
    /// <param name="processId">Process ID to check.</param>
    /// <returns>True if Inspector is loaded.</returns>
    Task<bool> IsInspectorLoadedAsync(int processId);
}
