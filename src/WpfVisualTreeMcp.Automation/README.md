# WpfVisualTreeMcp.Automation

Write C# tests against running Windows WPF applications. The package includes the
Inspector, x64/x86 native bootstrapper and x86 injection helper. It talks directly
to the same inspection engine used by MCP and the CLI; neither server needs to run.

The test project targets .NET 8, 9 or 10 and runs as x64 or x86 on Windows. The
target WPF application may use .NET Framework 4.7.2, 4.8, 4.8.1 or .NET 8, 9, 10.
The test project and application do not need to use the same framework. The SDK
ships a `net8.0` library usable by .NET 9/10 consumers; a .NET Framework test
project cannot reference it directly.
Use an x64 test process to automate both x64 and x86 apps; an x86 test process
can inject into x86 apps. Custom helper runtime locations use `DOTNET_ROOT_X86`
(the test runner's generic `DOTNET_ROOT` is ignored for the cross-bitness helper).

```csharp
using WpfVisualTreeMcp.Automation;

await using var app = await WpfApplication.AttachAsync(processId);
var status = app.ByAutomationId("StatusTextBox");
await status.WaitForTextAsync("loaded", timeout: TimeSpan.FromSeconds(30));
await app.ByAutomationId("SaveButton").ClickAsync();
await status.AssertTextAsync("saved");
```

`WaitForTextAsync` compares the exact `Text` property, case-sensitively. It does
not accept `not loaded` or `Loaded`. Missing elements are retried. Duplicate IDs
fail immediately. A timeout throws `TimeoutException` with the locator, expected
value and last observed value. Use a `CancellationToken` to cancel a wait.

```csharp
var dialog = app.ByAutomationId("EditorDialog");
var field = dialog.ByAutomationId("UserName");
await field.SetTextAsync("Alice");
await field.WaitForAsync(ElementCondition.Enabled);
await field.WaitForPropertyAsync("Text", "Alice", new WaitOptions { Timeout = TimeSpan.FromSeconds(5) });
await field.WaitUntilAsync(p => p.Properties.Any(x => x.Name == "Text" && x.Value?.Length > 3));

await app.ByAutomationId("Source").DragToAsync(app.ByAutomationId("Destination"));
var canvas = app.ByAutomationId("Canvas");
await canvas.TapAsync();
await canvas.LongPressAsync();
await canvas.PanAsync(80, 0);
await canvas.ZoomAsync(1.5);
await canvas.RotateAsync(30);
await canvas.ScrollAsync(-120);
await canvas.MoveMouseAsync(new LocalPoint(50, 30));
```

Locators resolve on each action and wait poll, so replacing a control does not
invalidate an AutomationId locator. Scope repeated IDs to an ancestor. `ByHandle`
is available for advanced inspection, but its handles belong to one app instance.
If dependency properties share a name, qualify it with the owner type in
`GetPropertyAsync`/`WaitForPropertyAsync`, for example `System.Windows.UIElement.IsEnabled`.

Mouse/touch operations use the real desktop. The app must support the requested
gesture to react to it. Cancellation stops awaiting IPC; input already delivered
is not rolled back. Calls on a session are serialized, with waits releasing the
session between polls. Disposing an attached session leaves the process running.

## Automatic action waits

Input methods wait for the locator to exist, be visible and be enabled before
dispatching. They resolve AutomationIds again on every poll. Missing items and
unrealized grid/tree containers are also retried during preparation. Duplicate
matches, unsupported controls and transport errors fail immediately. An edit,
click or gesture already dispatched is never automatically repeated.

```csharp
app.DefaultActionOptions = new ActionOptions { Timeout = TimeSpan.FromSeconds(20) };
await app.ByAutomationId("Save").WithActionOptions(new ActionOptions
{
    Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(100)
}).ClickAsync();
```

The default action deadline is 10 seconds, including preparation and the response.
Set `AutoWait = false` for immediate execution. This readiness check does not
promise animation completion or unobstructed screen coordinates; physical input
still checks foreground/hit-testing. Inspecting or editing dependency properties
does not require the element to be enabled. Use explicit application-specific waits
for asynchronous work after an action.

## Standard WPF controls and virtualization

```csharp
using WpfVisualTreeMcp.Shared.Models;

var orders = app.ByAutomationId("Orders").AsDataGrid();
var quantity = orders.RowByKey("Id", "42").Cell("Quantity");
await quantity.SetTextAsync("7");
var actual = await quantity.GetTextAsync();
await quantity.AsElement().ClickAsync(); // resolves and realizes the cell anew
var columns = (await orders.GetInfoAsync()).Columns;

await app.ByAutomationId("Folders").AsTreeView().Node("Projects", "WPF").SelectAsync();
await app.ByAutomationId("Folders").AsTreeView()
    .NodeBy(ItemLocator.ByKey("Id", "projects"), ItemLocator.ByKey("Id", "wpf")).ExpandAsync();

await app.ByAutomationId("Enabled").CheckAsync();
await app.ByAutomationId("Enabled").UncheckAsync();
await app.ByAutomationId("Details").ExpandAsync();
await app.ByAutomationId("Details").CollapseAsync();
await app.ByAutomationId("Choice").SelectExactlyAsync("Production");
await app.ByAutomationId("Choices").SelectItemsAsync(
    new[] { ItemLocator.ByText("A"), ItemLocator.ByText("C") });
```

Grid rows support index, exact text and property key (including dotted paths).
Indices follow the current sorted/filtered view. Columns support exact header,
binding path (`CellByBinding`) and index in `DataGrid.Columns`, independent of
display reordering. Prefer stable, unique keys for templated/virtualized data;
text fallback from a complex visual template requires a realized container.
Missing keys wait; duplicate keys or headers fail. Grid operations scroll rows
and columns into view. Tree paths expand ancestors and realize virtualized nodes.
Items filtered out of the data view cannot be selected by key.

Text cell editing uses the actual WPF editor/binding, then commits cell and row.
Read-only columns, cancelled edits and validation errors fail explicitly; a failed
validation may leave WPF in edit mode so the test can correct the value. Supported
editors are `DataGridTextColumn` and templates containing one visible `TextBox`.
Other cell controls remain accessible through `AsElement()` and their normal
actions. The wrappers target standard WPF controls; vendor-specific grids need
their own adapters. Text-column reads preserve whitespace and empty strings.

Check/uncheck, expand/collapse and tree selection preserve the requested state
without toggling it again on repeated calls. Three-state checkboxes accept
`SetCheckedAsync(null)`. `SelectItemsAsync` replaces the selection by default;
`SelectionChange.Add`/`Remove` require a control configured for multiple selection.
An empty replacement clears selection. All requested items are resolved before
selection changes. Existing `SelectItemAsync(string)` retains its legacy substring
matching; use `SelectExactlyAsync` or keyed locators for exact tests.

## Process and WPF window lifecycle

```csharp
await using var launched = await WpfApplication.LaunchAsync(new LaunchOptions
{
    FileName = @"C:\Apps\MyApp.exe",
    Arguments = new[] { "--environment", "Test environment" },
    WorkingDirectory = @"C:\Apps",
    Environment = new Dictionary<string, string?> { ["APP_MODE"] = "test" },
    StartupTimeout = TimeSpan.FromSeconds(30)
});
await launched.ByAutomationId("OpenEditor").ClickAsync();
var editor = await launched.WaitForWindowAsync(new WindowQuery { AutomationId = "Editor" });
await editor.ByAutomationId("Name").SetTextAsync("Alice");
await editor.ByAutomationId("OK").ClickAsync();
await editor.WaitForClosedAsync();
```

`LaunchAsync` waits for a main window and Inspector attachment. Failure/cancellation
cleans up the process it launched. Disposal closes that owned process, allowing
two seconds for graceful shutdown before terminating it. `CloseOnDispose = false`
or explicit `DetachAsync()` leaves it running. `AttachAsync` never gains ownership.
`CloseAsync()` explicitly requests graceful main-window closure and waits for exit;
it does not force-kill an attached process.

`GetWindowsAsync` lists WPF windows with title, AutomationId, owner, main/active
state and visibility. `WaitForWindowAsync` requires an unambiguous exact match.
Window instances offer activate, minimize, maximize, restore and close. A modal
WPF dialog can be found and operated while its owner is disabled. For a close
confirmation, use `RequestCloseAsync()`, handle the dialog, then
`WaitForClosedAsync()`. A cancelled `Closing` event produces a timeout. Native
OS dialogs and windows in another process are outside these WPF window APIs.

## Live property subscriptions

```csharp
var statusBox = app.ByAutomationId("StatusTextBox");
await using var watch = await statusBox.ObservePropertyAsync("Text");
if (watch.InitialValue != "loaded")
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await foreach (var change in watch.ReadAllAsync(deadline.Token))
    {
        Console.WriteLine($"{change.OldValue} -> {change.NewValue}");
        if (change.NewValue == "loaded") break;
    }
}
```

Notifications are pushed over a dedicated pipe, preserving initial-value ordering
and the actual previous value on each change. The subscription watches one control
instance; recreate it if the application replaces the control. Each subscription
has one consuming reader and bounded 1024-change buffers. Slow consumers and
unexpected disconnects receive an error, not silent event loss. Disposing the
subscription or session closes the pipe and removes the Inspector handler;
client crashes/disconnections also trigger cleanup. Cancelling enumeration stops
that read, so retain `await using` to dispose the subscription.

The existing `WatchPropertyAsync` returns an ID backed by the same stream; obtain
it with `app.GetPropertyWatch(id)` and remove it with `app.UnwatchPropertyAsync(id)`.
For a simple condition, `WaitForTextAsync` remains the shorter API and follows
control replacement through its locator.

The library also exposes tree/property/binding/style/resource inspection, layout,
screenshots, highlights, diagnostics, snapshots/diffs, live property editing and
revert, property watches, assertions, recording, replay and xUnit/JSON export.
Results use the shared typed model classes.

```csharp
await app.StartRecordingAsync("Save workflow");
await field.SetTextAsync("Alice");
await field.AssertTextAsync("Alice");
await app.StopRecordingAsync();
var files = await app.ExportTestAsync(); // filename -> file contents; write to a new directory
```

Exported xUnit tests reference this package, start a fresh app using `WPF_TEST_APP`,
attach, replay and terminate only that test-owned process. C# property waits and
custom predicates run in the test process and are not serialized into recordings;
include explicit assertions for state that exported workflows must verify.
Grid edits, desired-state selection and tree operations are recorded with the
parent control selector plus row/column/path criteria. Application lifecycle and
property subscriptions remain C# test setup/teardown, not recorded workflow steps.

Install from nuget.org in your test project:

```powershell
dotnet add YourTests.csproj package WpfVisualTreeMcp.Automation --version 0.20.0
```

The Inspector and native bootstrapper are included. Cross-architecture injection
into an x86 app also needs an x86 .NET 8, 9 or 10 runtime for the bundled helper.
For an app already self-hosting the Inspector, use `AttachAsync(pid, autoInject: false)`.
An older loaded Inspector requires restarting the target before the new features can be used.

Verify the installed package and its exported tests with
`python scripts/test_automation_package.py --packages artifacts/packages`.
This runs inspection/input/export/replay against all six supported target frameworks
on x86 and x64. Advanced grid/tree, validation, notification, modal-window and
lifecycle scenarios run from .NET 8, 9 and 10 consumers against every target.
Build the suite with the .NET 10 SDK and install .NET 8/9/10 Desktop runtimes for
both architectures plus .NET Framework 4.8.1. Framework 4.x target builds run on
the installed 4.8.1 runtime; the suite does not install older runtimes side by side.
Add `--gestures` on an unlocked desktop for real mouse drag and touch checks.
For a source build, first run `scripts/Build-NativeBootstrapper.ps1`, then
`dotnet pack src/WpfVisualTreeMcp.Automation -c Release -o artifacts/packages`.
