# WpfVisualTreeMcp.Automation

Write C# tests against running Windows WPF applications. The package includes the
Inspector, x64/x86 native bootstrapper and x86 injection helper. It talks directly
to the same inspection engine used by MCP and the CLI; neither server needs to run.

The test project targets .NET 8 or later and runs as x64 or x86 on Windows. The
target application may use .NET Framework 4.7.2/4.8 or .NET 8 WPF.
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
session between polls. Disposing detaches and never closes the target process.

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

Install from nuget.org in your test project:

```powershell
dotnet add YourTests.csproj package WpfVisualTreeMcp.Automation --version 0.18.0
```

The Inspector and native bootstrapper are included. Cross-architecture injection
into an x86 app also needs the x86 .NET 8 runtime for the bundled helper.
For an app already self-hosting the Inspector, use `AttachAsync(pid, autoInject: false)`.
An older loaded Inspector requires restarting the target before the new features can be used.

Verify the installed package and its exported tests with
`python scripts/test_automation_package.py --packages artifacts/packages`.
Add `--gestures` on an unlocked desktop for real mouse drag and touch checks.
For a source build, first run `scripts/Build-NativeBootstrapper.ps1`, then
`dotnet pack src/WpfVisualTreeMcp.Automation -c Release -o artifacts/packages`.
