using System.Collections.Concurrent;
using System.Diagnostics;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

public sealed class LaunchOptions
{
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool AutoInject { get; init; } = true;
    /// <summary>On disposal, close the launched process; kill it if graceful shutdown takes longer than two seconds.</summary>
    public bool CloseOnDispose { get; init; } = true;
}

/// <summary>Exact, case-sensitive criteria combined with AND. Multiple matching windows fail immediately.</summary>
public sealed record WindowQuery
{
    public string? AutomationId { get; init; }
    public string? Name { get; init; }
    public string? Title { get; init; }
    public string? OwnerHandle { get; init; }
    public bool? IsMain { get; init; }
    public bool VisibleOnly { get; init; } = true;
    internal bool Matches(WpfWindowInfo window) =>
        (AutomationId == null || window.AutomationId == AutomationId) && (Name == null || window.Name == Name)
        && (Title == null || window.Title == Title) && (OwnerHandle == null || window.OwnerHandle == OwnerHandle)
        && (IsMain == null || window.IsMain == IsMain) && (!VisibleOnly || window.IsVisible);
}

public sealed partial class WpfApplication
{
    private Process? _ownedProcess;
    private bool _closeOnDispose;
    private readonly ConcurrentDictionary<string, PropertySubscription> _subscriptions = new();
    public bool OwnsProcess => _ownedProcess != null;

    /// <summary>Start an executable, wait for its main window, then attach. Failed startup cleans up only the process started here.</summary>
    public static async Task<WpfApplication> LaunchAsync(LaunchOptions options, CancellationToken cancellationToken = default)
    {
        RequireWindows(); ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);
        if (options.StartupTimeout <= TimeSpan.Zero || options.StartupTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options.StartupTimeout));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(options.FileName) { UseShellExecute = false };
        // VSTest may set this to its own x64 runtime. The launched app can be
        // x86: preserve architecture-specific roots and explicit caller overrides.
        start.Environment.Remove("DOTNET_ROOT");
        if (options.WorkingDirectory != null) start.WorkingDirectory = options.WorkingDirectory;
        foreach (var argument in options.Arguments) start.ArgumentList.Add(argument);
        foreach (var entry in options.Environment) start.Environment[entry.Key] = entry.Value;
        var process = Process.Start(start) ?? throw new WpfAutomationException("Process.Start did not return a process.");
        using var deadline = new CancellationTokenSource(options.StartupTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                process.Refresh();
                if (process.HasExited) throw new WpfAutomationException($"Application exited during startup with code {process.ExitCode}.");
                if (process.MainWindowHandle != IntPtr.Zero) break;
                await Task.Delay(100, linked.Token).ConfigureAwait(false);
            }
            var app = await AttachAsync(process.Id, options.AutoInject, linked.Token).ConfigureAwait(false);
            app._ownedProcess = process; app._closeOnDispose = options.CloseOnDispose;
            return app;
        }
        catch (Exception error)
        {
            try { await StopOwnedProcessAsync(process).ConfigureAwait(false); }
            finally { process.Dispose(); }
            if (error is OperationCanceledException && deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"Application did not become ready within {options.StartupTimeout}.", error);
            throw;
        }
    }

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (process.HasExited) return;
        process.CloseMainWindow();
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    internal bool HasProcessExited()
    {
        try { using var process = Process.GetProcessById(ProcessId); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    public async Task<IReadOnlyList<WpfWindowInfo>> GetWindowsAsync(CancellationToken cancellationToken = default) =>
        (await RunAsync(b => b.WindowAsync(new()), cancellationToken).ConfigureAwait(false)).Windows;

    public async Task<WpfWindow> WaitForWindowAsync(WindowQuery query, WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); options ??= new(); options.Validate();
        using var deadline = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _lifetime.Token);
        try
        {
            while (true)
            {
                var matches = (await GetWindowsAsync(linked.Token).ConfigureAwait(false)).Where(query.Matches).ToArray();
                if (matches.Length > 1) throw new WpfAutomationException("Ambiguous window query: multiple windows match. Add title, AutomationId or owner criteria.");
                if (matches.Length == 1) return new WpfWindow(this, matches[0]);
                await Task.Delay(options.PollInterval, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        { throw new TimeoutException($"Process {ProcessId}: no matching WPF window appeared within {options.Timeout} ({query})."); }
    }

    /// <summary>Request graceful close of the main WPF window and wait for process exit. Never force-kills an attached process.</summary>
    public async Task CloseAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (HasProcessExited()) return;
        var duration = timeout ?? TimeSpan.FromSeconds(10);
        new WaitOptions { Timeout = duration }.Validate();
        using var deadline = new CancellationTokenSource(duration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _lifetime.Token);
        try
        {
            var window = await WaitForWindowAsync(new() { IsMain = true, VisibleOnly = false }, cancellationToken: linked.Token).ConfigureAwait(false);
            await window.RequestCloseAsync(linked.Token).ConfigureAwait(false);
            while (!HasProcessExited()) await Task.Delay(100, linked.Token).ConfigureAwait(false);
        }
        catch (WpfAutomationException) when (HasProcessExited()) { }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        { throw new TimeoutException("The application did not exit after requesting close; a Closing handler or another window may be keeping it open."); }
    }

    internal PropertySubscription TrackSubscription(PropertySubscription subscription)
    {
        _subscriptions[subscription.WatchId] = subscription;
        subscription.OnDisposed = () => _subscriptions.TryRemove(subscription.WatchId, out _);
        return subscription;
    }
    public PropertySubscription GetPropertyWatch(string watchId) => _subscriptions.TryGetValue(watchId, out var subscription)
        ? subscription : throw new ArgumentException("Unknown or disposed property watch.", nameof(watchId));
    public async Task UnwatchPropertyAsync(string watchId, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryGetValue(watchId, out var subscription)) await subscription.DisposeAsync().ConfigureAwait(false);
        await RunAsync(b => b.UnwatchPropertyAsync(watchId), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>One WPF window instance, with element queries scoped to that window (including modal dialogs).</summary>
public sealed class WpfWindow
{
    private readonly WpfApplication _app;
    public WpfWindowInfo Info { get; }
    public string Handle => Info.Handle;
    internal WpfWindow(WpfApplication app, WpfWindowInfo info) { _app = app; Info = info; }
    public WpfElement AsElement() => _app.ByHandle(Handle);
    public WpfElement ByAutomationId(string automationId) => AsElement().ByAutomationId(automationId);
    private Task ActAsync(string action, CancellationToken token) => _app.RunAsync(b => b.WindowAsync(new() { ElementHandle = Handle, Action = action }), token);
    public Task ActivateAsync(CancellationToken cancellationToken = default) => ActAsync("activate", cancellationToken);
    public Task MinimizeAsync(CancellationToken cancellationToken = default) => ActAsync("minimize", cancellationToken);
    public Task MaximizeAsync(CancellationToken cancellationToken = default) => ActAsync("maximize", cancellationToken);
    public Task RestoreAsync(CancellationToken cancellationToken = default) => ActAsync("restore", cancellationToken);
    /// <summary>Send the close request without waiting, allowing the caller to handle a confirmation dialog.</summary>
    public async Task RequestCloseAsync(CancellationToken cancellationToken = default)
    {
        try { await ActAsync("close", cancellationToken).ConfigureAwait(false); }
        catch (WpfAutomationException) when (_app.HasProcessExited()) { }
    }
    public async Task CloseAsync(WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        await RequestCloseAsync(cancellationToken).ConfigureAwait(false);
        await WaitForClosedAsync(options, cancellationToken).ConfigureAwait(false);
    }
    public async Task WaitForClosedAsync(WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); options.Validate();
        using var deadline = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            while (!_app.HasProcessExited())
            {
                if (!(await _app.GetWindowsAsync(linked.Token).ConfigureAwait(false)).Any(w => w.Handle == Handle)) return;
                await Task.Delay(options.PollInterval, linked.Token).ConfigureAwait(false);
            }
        }
        catch (WpfAutomationException) when (_app.HasProcessExited()) { }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"Window '{Info.Title}' did not close; a Closing handler may have cancelled the request."); }
    }
}
