using System.Diagnostics;
using WpfVisualTreeMcp.Automation;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

if (args.Length == 0) throw new ArgumentException("Pass the TestApp executable path, optionally followed by --inject.");
var executable = Path.GetFullPath(args[0]);
var inject = args.Contains("--inject");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
async Task Throws<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); throw new Exception("Expected " + typeof(T).Name + ": " + message); }
    catch (T) { Console.WriteLine("PASS " + message); }
}
bool Exited(int id) { try { using var p = Process.GetProcessById(id); return p.HasExited; } catch (ArgumentException) { return true; } }
var launch = new LaunchOptions
{
    FileName = executable, Arguments = inject ? new[] { "space argument", "--inject" } : new[] { "space argument" },
    WorkingDirectory = Path.GetDirectoryName(executable), AutoInject = inject,
    Environment = new Dictionary<string, string?> { ["WPF_TEST_VALUE"] = "test value" }
};
var app = await WpfApplication.LaunchAsync(launch);
var pid = app.ProcessId;
try
{
    Check(app.OwnsProcess, "launch owns only its started process");
    Check((await app.ByAutomationId("Arguments").GetPropertyAsync("Text"))!.StartsWith("space argument"), "arguments retain spaces");
    Check(await app.ByAutomationId("WorkingDirectory").GetPropertyAsync("Text") == launch.WorkingDirectory, "working directory");
    Check(await app.ByAutomationId("EnvironmentValue").GetPropertyAsync("Text") == "test value", "environment override");
    await app.ByAutomationId("Delayed").ClickAsync();
    await app.ByAutomationId("Status").WaitForTextAsync("loaded");
    Check(true, "automatic readiness and exact text wait");

    var check = app.ByAutomationId("Check"); await check.CheckAsync(); await check.CheckAsync();
    Check(await app.ByAutomationId("CheckCount").GetPropertyAsync("Text") == "1", "idempotent check emits one checked event");
    await check.UncheckAsync();
    var selected = await app.ByAutomationId("Multi").SelectItemsAsync(new[] { ItemLocator.ByText("A"), ItemLocator.ByText("C") });
    Check(selected.SelectedIndices.SequenceEqual(new[] { 0, 2 }), "exact multiple selection");
    Check((await app.ByAutomationId("Multi").SelectExactlyAsync("AB")).SelectedIndices.SequenceEqual(new[] { 1 }), "exact selection is not a substring");

    var grid = app.ByAutomationId("Orders").AsDataGrid(); var cell = grid.RowByKey("Id", "990").Cell("Quantity");
    Check((await grid.GetInfoAsync()).RowCount == 1000, "grid metadata includes virtualized rows");
    await app.StartRecordingAsync("Virtualized controls");
    await cell.SetTextAsync("edited");
    await app.ByAutomationId("Tree").AsTreeView().Node("Node 290", "Child 290").SelectAsync();
    await check.CheckAsync();
    var recording = await app.StopRecordingAsync();
    Check(recording.Workflow!.Steps.Count == 3, "records stable grid/tree/state operations without readiness polls");
    Check(await cell.GetTextAsync() == "edited", "virtualized row edited through its binding with columns reordered");
    await grid.Row(0).Cell("Quantity").GetTextAsync();
    await cell.AsElement().WaitForAsync(ElementCondition.Visible);
    Check(true, "cell locator re-resolves after virtualization recycling");
    var replay = await app.ReplayAsync(recording.Workflow); Check(replay.Success, "new control operations replay: " + replay.Error);
    await app.ClearRecordingAsync();
    await Throws<WpfAutomationException>(() => grid.Row(0).Cell("Id").SetTextAsync("1"), "read-only column rejected");
    await Throws<WpfAutomationException>(() => cell.SetTextAsync("invalid"), "binding validation failure surfaced");
    await cell.SetTextAsync("valid");

    var status = app.ByAutomationId("Status");
    await status.SetTextAsync("initial");
    await using (var watch = await status.ObservePropertyAsync("Text"))
    await using (var second = await status.ObservePropertyAsync("Text"))
    {
        Check(watch.InitialValue == "initial", "property subscription returns initial value");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task<List<PropertyChangedNotification>> ReadTwo(PropertySubscription subscription)
        {
            var values = new List<PropertyChangedNotification>();
            await foreach (var change in subscription.ReadAllAsync(timeout.Token)) { values.Add(change); if (values.Count == 2) break; }
            return values;
        }
        var changes = ReadTwo(watch); var other = ReadTwo(second);
        await app.ByAutomationId("Changes").ClickAsync();
        var values = await changes;
        Check(values.Count == 2 && values[0].OldValue == "initial" && values[0].NewValue == "one" && values[1].OldValue == "one" && values[1].NewValue == "two", "push notifications preserve order and previous values");
        Check((await other).Count == 2, "independent simultaneous subscriptions");
        await app.UnwatchPropertyAsync(watch.WatchId);
        await Throws<ArgumentException>(() => Task.FromResult(app.GetPropertyWatch(watch.WatchId)), "unwatch removes subscription from session");
    }
    var legacyId = await status.WatchPropertyAsync("Text");
    Check(app.GetPropertyWatch(legacyId).InitialValue == "two", "ID-based watch also exposes a real notification stream");
    await app.UnwatchPropertyAsync(legacyId);

    var main = await app.WaitForWindowAsync(new() { IsMain = true });
    await app.ByAutomationId("OpenDialog").ClickAsync();
    var dialog = await app.WaitForWindowAsync(new() { AutomationId = "Dialog", OwnerHandle = main.Handle });
    Check(await dialog.ByAutomationId("Status").GetPropertyAsync("Text") == "dialog", "modal window scopes duplicate AutomationIds");
    await dialog.ByAutomationId("DialogOk").ClickAsync(); await dialog.WaitForClosedAsync();
    await status.WaitForTextAsync("dialog closed");
    Check(true, "modal interaction completes without blocking the session");
    await app.ByAutomationId("OpenSecondary").ClickAsync();
    var secondary = await app.WaitForWindowAsync(new() { Title = "Secondary" });
    await secondary.MaximizeAsync(); await secondary.RestoreAsync(); await secondary.ActivateAsync();
    await Throws<TimeoutException>(() => secondary.CloseAsync(new() { Timeout = TimeSpan.FromMilliseconds(500) }), "cancelled Closing is reported as a timeout");
    await secondary.ByAutomationId("AllowClose").CheckAsync(); await secondary.CloseAsync();
    var attached = await WpfApplication.AttachAsync(pid, autoInject: false); await attached.DisposeAsync();
    Check(!Exited(pid), "disposing an attached session does not terminate the application");
    await using var closingWatch = await status.ObservePropertyAsync("Text");
    await app.CloseAsync(); Check(Exited(pid), "graceful application close waits for exit");
}
finally { await app.DisposeAsync(); }
Check(Exited(pid), "owned session cleanup");
var owned = await WpfApplication.LaunchAsync(launch);
var ownedPid = owned.ProcessId;
await owned.DisposeAsync();
Check(Exited(ownedPid), "disposal closes a still-running owned app");
var detached = await WpfApplication.LaunchAsync(launch);
var detachedPid = detached.ProcessId;
try
{
    await detached.DetachAsync();
    Check(!Exited(detachedPid), "explicit detach leaves a launched app running");
}
finally
{
    await detached.DisposeAsync();
    if (!Exited(detachedPid)) { using var process = Process.GetProcessById(detachedPid); process.Kill(); await process.WaitForExitAsync(); }
}
await Throws<WpfAutomationException>(() => WpfApplication.LaunchAsync(new() { FileName = executable, Arguments = new[] { "--exit" }, AutoInject = false }), "early startup exit");
var marker = Path.Combine(Path.GetTempPath(), "wpf-startup-" + Guid.NewGuid().ToString("N") + ".pid");
try
{
    await Throws<TimeoutException>(() => WpfApplication.LaunchAsync(new()
    {
        FileName = executable, Arguments = new[] { "--no-window", "--pid-file", marker },
        StartupTimeout = TimeSpan.FromSeconds(1), AutoInject = false
    }), "startup deadline");
    Check(Exited(int.Parse(await File.ReadAllTextAsync(marker))), "startup timeout terminates its owned process");
    using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    await Throws<OperationCanceledException>(() => WpfApplication.LaunchAsync(new()
    { FileName = executable, Arguments = new[] { "--no-window", "--pid-file", marker }, AutoInject = false }, cancelled.Token), "startup cancellation");
    Check(Exited(int.Parse(await File.ReadAllTextAsync(marker))), "startup cancellation terminates its owned process");
}
finally { File.Delete(marker); }
Console.WriteLine("Advanced WPF automation smoke passed: " + executable);
