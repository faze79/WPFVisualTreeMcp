using Microsoft.Extensions.Logging.Abstractions;
using WpfVisualTreeMcp.Automation.Infrastructure;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

/// <summary>A session attached to one WPF process. Disposing detaches; it never terminates the application.</summary>
public sealed class WpfApplication : IAsyncDisposable
{
    private readonly IIpcBridge _bridge;
    private readonly IProcessManager _processes;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    internal InspectionSession Session { get; }
    public int ProcessId => Session.ProcessId;

    internal WpfApplication(IIpcBridge bridge, IProcessManager processes, InspectionSession session)
        => (_bridge, _processes, Session) = (bridge, processes, session);

    public static Task<IReadOnlyList<WpfProcessInfo>> ListProcessesAsync()
    {
        RequireWindows();
        return new ProcessManager(NullLogger<ProcessManager>.Instance).GetWpfProcessesAsync();
    }

    /// <summary>Attach to a PID. By default loads the bundled Inspector if necessary.</summary>
    public static async Task<WpfApplication> AttachAsync(int processId, bool autoInject = true, CancellationToken cancellationToken = default)
    {
        RequireWindows();
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        cancellationToken.ThrowIfCancellationRequested();
        var manager = new ProcessManager(NullLogger<ProcessManager>.Instance);
        try
        {
            var session = await manager.AttachToProcessAsync(processId, null, autoInject).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.InspectorReady) throw new WpfAutomationException($"Cannot attach to process {processId}: {session.InspectorStatus}");
            return new WpfApplication(new InspectorBridge(NullLogger.Instance, manager), manager, session);
        }
        catch
        {
            if (manager.CurrentSession is { } session) await manager.DetachAsync(session.SessionId).ConfigureAwait(false);
            throw;
        }
    }

    public WpfElement ByAutomationId(string automationId) => new(this, automationId, null, null);
    public WpfElement ByHandle(string handle) => new(this, null, handle, null);

    internal async Task<T> RunAsync<T>(Func<IIpcBridge, Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            using var scope = (_bridge as InspectorBridge)?.WithCancellation(linked.Token);
            return await operation(_bridge).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal Task RunAsync(Func<IIpcBridge, Task> operation, CancellationToken token) =>
        RunAsync(async bridge => { await operation(bridge).ConfigureAwait(false); return true; }, token);

    public Task<FindElementsResult> FindElementsAsync(ElementQuery query, WpfElement? root = null, CancellationToken cancellationToken = default) =>
        FindAsync(query, root, false, cancellationToken);
    public Task<FindElementsResult> FindElementsDeepAsync(ElementQuery query, WpfElement? root = null, CancellationToken cancellationToken = default) =>
        FindAsync(query, root, true, cancellationToken);

    /// <summary>Inspector-side wait, recorded when successful. Timeout is 0-25000 ms; text uses substring matching.</summary>
    public Task<WaitForResult> WaitForElementAsync(ElementQuery query, ElementCondition condition = ElementCondition.Visible,
        int timeoutMs = 10000, int pollIntervalMs = 250, WpfElement? root = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CheckOwner(root);
        if (!Enum.IsDefined(condition)) throw new ArgumentOutOfRangeException(nameof(condition));
        if (timeoutMs is < 0 or > 25000 || pollIntervalMs is < 50 or > 1000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        if (query.Properties?.Count > 0) throw new ArgumentException("Use WpfElement.WaitForPropertyAsync for exact property conditions.");
        return RunAsync(async bridge => await bridge.WaitForElementAsync(
            root == null ? null : await root.ResolveAsync(bridge).ConfigureAwait(false), query.TypeName, query.Name, query.Text,
            condition.ToString().ToLowerInvariant(), timeoutMs, pollIntervalMs, query.AutomationId).ConfigureAwait(false), cancellationToken);
    }

    private Task<FindElementsResult> FindAsync(ElementQuery query, WpfElement? root, bool deep, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        CheckOwner(root);
        return RunAsync(async bridge =>
        {
            var handle = root == null ? null : await root.ResolveAsync(bridge).ConfigureAwait(false);
            return deep
                ? await bridge.FindElementsDeepAsync(handle, query.TypeName, query.Name, query.Text, query.Properties, query.VisibleOnly, query.AutomationId).ConfigureAwait(false)
                : await bridge.FindElementsAsync(handle, query.TypeName, query.Name, query.Text, query.Properties, query.VisibleOnly, query.MaxResults, query.AutomationId).ConfigureAwait(false);
        }, token);
    }

    internal void CheckOwner(WpfElement? element)
    {
        if (element != null && !ReferenceEquals(element.Application, this))
            throw new ArgumentException("The element belongs to a different application session.");
    }

    public Task<VisualTreeResult> GetVisualTreeAsync(int maxDepth = 25, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.GetVisualTreeAsync(null, maxDepth), cancellationToken);
    public Task<DiagnosticReport> ReportAsync(int maxElements = 500, int maxDepth = 25, int maxFindings = 100, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.ReportAsync(null, maxElements, maxDepth, maxFindings), cancellationToken);
    public Task<BindingErrorsResult> GetBindingErrorsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.GetBindingErrorsAsync(), cancellationToken);
    public Task ClearBindingErrorsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.ClearBindingErrorsAsync(), cancellationToken);
    public Task<ResourcesResult> GetResourcesAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.GetResourcesAsync("application", null), cancellationToken);
    public Task<ResourcesResult> GetWindowResourcesAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.GetResourcesAsync("window", null), cancellationToken);
    public Task<SnapshotResult> SnapshotAsync(string? label = null, int maxDepth = 25, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.SnapshotAsync(null, label, maxDepth), cancellationToken);
    public Task<DiffResult> DiffAsync(string before, string after, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.DiffAsync(before, after), cancellationToken);
    public Task<ScreenshotResult> CaptureScreenshotAsync(string mode = "auto", int maxWidth = 1920, int maxHeight = 1080, bool fullContent = false, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.CaptureScreenshotAsync(null, maxWidth, maxHeight, mode, fullContent), cancellationToken);
    public Task<ExportResult> ExportTreeAsync(string format = "json", CancellationToken cancellationToken = default) =>
        RunAsync(b => b.ExportTreeAsync(null, format), cancellationToken);
    public Task<SendKeysResult> SendKeysAsync(string keys, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.SendKeysAsync(null, keys), cancellationToken);
    public Task<RevertPropertyResult> RevertPropertiesAsync(bool all = false, string? propertyName = null, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.RevertPropertyAsync(all, null, propertyName), cancellationToken);
    public Task<RecordingState> StartRecordingAsync(string? name = null, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.RecordAsync("start", name), cancellationToken);
    public Task<RecordingState> StopRecordingAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.RecordAsync("stop"), cancellationToken);
    public Task<RecordingState> GetRecordingAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.RecordAsync("status"), cancellationToken);
    public Task<RecordingState> ClearRecordingAsync(CancellationToken cancellationToken = default) =>
        RunAsync(b => b.RecordAsync("clear"), cancellationToken);
    public Task<ReplayResult> ReplayAsync(RecordedWorkflow workflow, int timeoutMs = 120000, CancellationToken cancellationToken = default) =>
        RunAsync(b => b.ReplayAsync(workflow, timeoutMs), cancellationToken);
    public async Task<IReadOnlyDictionary<string, string>> ExportTestAsync(string format = "xunit", CancellationToken cancellationToken = default) =>
        AutomationTestExporter.Export(await GetRecordingAsync(cancellationToken).ConfigureAwait(false), format);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await _processes.DetachAsync(Session.SessionId).ConfigureAwait(false); }
        finally { _gate.Release(); }
        // Keep synchronization objects valid for concurrent callers unwinding after cancellation.
    }

    public ValueTask DetachAsync() => DisposeAsync();

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WPF automation requires Windows.");
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture is not
            (System.Runtime.InteropServices.Architecture.X64 or System.Runtime.InteropServices.Architecture.X86))
            throw new PlatformNotSupportedException("Use an x64 or x86 .NET test process for WPF automation.");
    }
}
