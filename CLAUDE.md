# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WpfVisualTreeMcp** is an MCP (Model Context Protocol) server that enables AI agents to inspect running WPF applications in real-time. It exposes WPF visual tree inspection capabilities similar to Snoop WPF or Visual Studio's Live Visual Tree through the MCP protocol.

## Build Commands

```bash
# Build solution
dotnet build WpfVisualTreeMcp.sln

# Build for Release
dotnet build -c Release WpfVisualTreeMcp.sln

# Run tests
dotnet test WpfVisualTreeMcp.sln

# Run sample WPF app for testing
dotnet run --project samples/SampleWpfApp

# Publish MCP Server executable (same exe also runs as the CLI)
dotnet publish src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj -c Release -o ./publish

# Run as CLI (any subcommand switches from MCP server mode to CLI mode)
dotnet run --project src/WpfVisualTreeMcp.Server -- list
./publish/WpfVisualTreeMcp.Server.exe help
```

## Architecture

```
AI Agent (Claude Code)
    ↓ [MCP Protocol - JSON-RPC over stdio]
MCP Server (.NET 8.0)
    ├─ WpfTools (38 tools)
    ├─ ProcessManager (discovers WPF processes)
    └─ NamedPipeBridge (IPC)
        ↓ [Named Pipes: wpf_inspector_{pid}]
Target WPF Application (.NET Framework)
    └─ Inspector DLL (.NET Framework 4.7.2/4.8 or .NET 8)
        ├─ TreeWalker (visual tree navigation + adorner/popup traversal)
        ├─ ScreenshotCapture (RenderTargetBitmap element capture)
        ├─ PropertyReader (dependency properties)
        ├─ BindingAnalyzer (binding inspection)
        ├─ ControlInteractor (UI Automation / physical click)
        └─ IpcServer (named pipe communication)
```

**Key Design:** The server runs separately and communicates via named pipes. `wpf_click_element`, `wpf_move_mouse`, `wpf_scroll`, `wpf_drag`, `wpf_touch_gesture`, `wpf_select_item`, `wpf_set_text`, `wpf_send_keys`, `wpf_set_property`, and `wpf_revert_property` change application state. `wpf_replay` executes recorded actions; full-content screenshots can temporarily scroll controls and trigger handlers. Recording/snapshots/watches maintain Inspector state. `wpf_set_property` is reversible via `wpf_revert_property` (per-session undo stack that restores the prior binding, local value, or default).

## Key Source Locations

| Component | Location | Purpose |
|-----------|----------|---------|
| MCP Server Entry | `src/WpfVisualTreeMcp.Server/Program.cs` | Server init with MCP SDK; routes to CLI mode if args present |
| Tool Definitions | `src/WpfVisualTreeMcp.Server/WpfTools.cs` | 38 tools with `[McpServerTool]` attributes |
| CLI Front-End | `src/WpfVisualTreeMcp.Server/Cli/CliRunner.cs` | 38 inspection/workflow commands plus CLI-only `update` |
| Workflow Recording | `WorkflowRecorder.cs`, `StableSelectors.cs`, `InspectorService.Workflow.cs` | Inspector-owned recording and gated replay; exact selectors, explicit waits/assertions |
| Test Export | `src/WpfVisualTreeMcp.Server/WorkflowTestExporter.cs` | Data-only workflow JSON plus a standalone xUnit project using CLI replay |
| Aggregate Diagnostics | `src/WpfVisualTreeMcp.Inspector/DiagnosticReporter.cs` | Bounded realized-tree scan, current binding/validation errors and separate process-wide trace history |
| CLI Updater | `Cli/SelfUpdater.cs` + embedded `Cli/UpdateWorker.ps1` | Stable version lookup, verified staging, detached installation and status |
| Trigger / Style Diagnostics | `ResourceInspector.ExplainTriggers` | Evaluate Style + ControlTemplate triggers vs current state (active/inactive + setters); attribute a property value to its style setter / active trigger |
| Control Interactor | `src/WpfVisualTreeMcp.Inspector/ControlInteractor.cs` | Clicks, text input, and keyboard shortcuts (UI Automation + SendInput physical fallback) |
| Property Writer | `src/WpfVisualTreeMcp.Inspector/PropertyWriter.cs` | Live-edits dependency properties (TypeConverter coercion) with a per-session undo stack (restores prior binding/local/default) |
| Snapshot / Diff | `TreeWalker.CaptureSnapshot` + `InspectorService.ComputeDiff` | Capture a subtree's curated state keyed by element handle; diff two snapshots (handle stable → changed/added/removed) |
| Injector Helper | `src/WpfVisualTreeMcp.InjectorHelper/Program.cs` | 32-bit .NET 8 helper exe spawned by `ProcessInjector` for cross-arch injection (v0.6.0) |
| IPC Bridge | `src/WpfVisualTreeMcp.Server/Services/NamedPipeBridge.cs` | Named pipe communication to Inspector |
| Process Manager | `src/WpfVisualTreeMcp.Server/Services/ProcessManager.cs` | WPF process discovery and attachment |
| Inspector Entry | `src/WpfVisualTreeMcp.Inspector/InspectorService.cs` | Injected DLL main entry point |
| Screenshot Capture | `src/WpfVisualTreeMcp.Inspector/ScreenshotCapture.cs` | RenderTargetBitmap element/window capture |
| IPC Server | `src/WpfVisualTreeMcp.Inspector/IpcServer.cs` | Named pipe server in target process |
| IPC Messages | `src/WpfVisualTreeMcp.Shared/Ipc/IpcMessages.cs` | Request/response contracts |
| Native Bootstrapper | `src/WpfVisualTreeMcp.Bootstrapper/WpfInspectorBootstrapper.cpp` | CLR hosting for DLL injection |
| Process Injector | `src/WpfVisualTreeMcp.Injector/ProcessInjector.cs` | CreateRemoteThread + LoadLibrary injection |

## Threading Model

- WPF apps are single-threaded (STA). All visual tree operations must run on the UI Dispatcher thread.
- Inspector wraps Dispatcher.Invoke in `Task.Run()` to avoid blocking the IPC thread.
- 10-second timeout for UI operations.
- `IpcServer` accepts **concurrent** pipe connections (each client handled on its own task);
  requests still serialize on the UI Dispatcher, so a long `wpf_wait_for` no longer blocks
  other commands.
- `wpf_wait_for` polls on the background thread (short `Dispatcher.Invoke` per check +
  `Task.Delay` between) — it is handled in `HandleRequestAsync` *before* the blocking
  Dispatcher.Invoke path, so the UI thread stays free and the awaited condition can change.
  Its timeout is clamped to 25s to stay under the 30s IPC request timeout.

## Important Implementation Notes

### Physical mouse input

- Coordinate clicks (`x`/`y`), `move-mouse`/`wpf_move_mouse` and `scroll`/`wpf_scroll` support local WPF DIPs, including HwndHost/WindowsFormsHost. Wheel units are signed (120 per notch), vertical or horizontal. New inputs are recordable/replayable; hover waits run off the Dispatcher. Coordinate clicks and pointer replay probe Inspector capabilities first so an old DLL cannot silently ignore coordinates.
- Native hit testing checks HWND ownership/descendants without accepting overlays; tree/find results mark native boundaries. Native CAD geometry requires an application-specific accessible provider or API.

### Physical mouse drag

- `wpf_drag` / CLI `drag` accept `DragOptions`: source handle plus either a target
  handle or a displacement in source-local WPF DIPs; optional start/target points.
- `ControlInteractor.PrepareDrag` resolves visible, hit-testable endpoints on the
  Dispatcher. `MouseDragExecutor` then sends the timed sequence on a worker so WPF
  mouse capture and `DragDrop.DoDragDrop` can continue processing events.
- `NativeMouseInput` uses checked SendInput, virtual-desktop absolute coordinates,
  per-call thread DPI context, foreground/endpoint checks and release cleanup.
- A per-Inspector interaction gate rejects overlapping state-changing commands.
  Queries remain available during drags. Success reports input delivery only.
- `scripts/Test-Drag.ps1` runs real desktop tests on an unlocked Windows desktop;
  the regular unit suite uses a fake mouse to verify failure/cancellation cleanup.
- `wpf_touch_gesture` / CLI `touch` use the same interaction gate and Dispatcher/worker
  split. `TouchGesturePath` builds local geometry; `PrepareTouch` validates the full
  path and creates screen-coordinate frames; `NativeTouchInput` uses two contact slots
  (IDs 0 and 1), periodic UPDATE frames and matching final UP coordinates.
- `PhysicalInputGuard` is shared by mouse and touch. Do not change target-app input
  compatibility switches. The sample opts into WPF's WM_POINTER stack at startup;
  legacy input configurations can ignore native injected touch.
- `scripts/Test-Touch.ps1` verifies WPF touch and manipulation events on the same
  runtime/architecture matrix as the mouse tests. These require an interactive desktop.

### Named Pipe Communication
- Pipe name format: `wpf_inspector_{pid}`
- Connection timeout: 5 seconds
- Request timeout: 30 seconds
- Replay uses its explicit 1–120-second deadline plus 15 seconds of IPC headroom.
- Recording state belongs to the target Inspector, persists across one-shot CLI
  calls and uses the interaction gate for recordable calls. Replay holds the gate
  for the sequence while leaving other inspection queries available. It yields
  at Dispatcher Background priority between steps; explicit waits handle app async work.
- `scripts/test_workflow.py --server <exe> --sample <exe> [--gestures]` records
  through MCP/CLI, verifies export equality, compiles the generated xUnit project,
  and replays in a fresh app. The optional gesture path requires the interactive desktop.
- Uses direct byte I/O (not StreamReader/Writer) due to .NET Framework 4.8 deadlock issues

### Element Handles
- Valid only within same Inspector session (same process lifetime)
- Format: `elem_{counter:X8}` (hex counter, e.g., `elem_00000052`)
- Restarting the target WPF app invalidates all handles
- Handle cache uses weak references: elements removed from the UI can be garbage-collected (their handles then expire)
- If a handle is not found, the tool returns an error explaining how to recover (re-run `wpf_find_elements`)

### IPC Request Serialization (IMPORTANT)
`IpcSerializer.SerializeRequest` must serialize the payload by its **runtime** type
(`data = (object)request`). System.Text.Json serializes by declared type: typing the
wrapper member as `IpcRequest` silently drops every derived-class property (filters,
element handles, ...). Regression-tested in `IpcSerializerTests`.

### UTF-8 BOM Handling
The Inspector strips UTF-8 BOM (0xEF 0xBB 0xBF) before JSON parsing to prevent deserialization errors.

### Visual Tree Traversal
- Traverses standard visual children via `VisualTreeHelper`
- Also enumerates `AdornerLayer` adorners (e.g., Fluent.Ribbon Backstage)
- Traverses into `Popup` child elements (separate visual trees)
- `FindElements` searches across ALL open windows when no `root_handle` given
- Query filters (AND semantics): `type_name` (partial), `element_name` (x:Name substring),
  `text` (visible text content — button captions, TextBlock text, window title, tooltip,
  AutomationProperties.Name; aggregated from shallow descendants), `property_filter`
  (property name → value substring), `visible_only`
- Results include `text`, `automationId`, `isVisible`, `isEnabled` and `screenBounds`
  (device pixels, same space as the OS mouse)
- Default `max_depth` is 25 (configurable 1-100)

### Screenshot Capture
- Three modes: `auto` (default) selects screen for visible HwndHost content, render otherwise, and reports the selected mode. Explicit render rejects native content with recovery guidance. `render` uses `RenderTargetBitmap` + `VisualBrush` (handles
  transforms, works when the window is covered, but cannot see Popups/menus);
  `screen` uses GDI BitBlt of the on-screen pixels (includes open Popups, ComboBox
  dropdowns, context menus and tooltips — requires the window visible and unobstructed)
- DPI-aware via `PresentationSource.FromVisual`
- Downscales if exceeding `max_width`/`max_height` (default 1920x1080)
- Returns MCP `ImageContentBlock` (base64 PNG) — Claude sees the image directly
- `full_content=true` / `--full-content` is render-only and may temporarily scroll
  virtualized controls, restoring offsets in `finally`. It uses the interaction
  gate, bounded tiles/memory and a cooperative 25-second deadline. The live matrix
  (`tests/run-integration-tests.ps1 -ServerDirectory <publish directory>`) verifies
  net472/net48/net8 x86/x64, injection/self-hosting and scroll restoration.

### Logging
- Server logs: `%LOCALAPPDATA%\WpfVisualTreeMcp\logs\mcp-server-YYYYMMDD.log`
- Inspector debug: `%TEMP%\WpfInspector_Debug.log`
- Bootstrapper debug: `%TEMP%\WpfInspectorBootstrapper.log`
- stdout must remain clean for JSON-RPC protocol

## WPF App Inspection Modes

### Auto-Injection Mode
Use `wpf_attach(process_id=<pid>, auto_inject=true)` to inject the Inspector into any running .NET Framework WPF app. Requires:
- Native bootstrapper DLL in `publish/native/x64/` (or x86)
- Target process must be .NET Framework (CLR hosting)
- Architecture detection is automatic (x64 vs x86)

### Self-Hosted Mode
For your own WPF application, add a reference to the Inspector and initialize on startup:

```csharp
// In App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    InspectorService.Initialize(Process.GetCurrentProcess().Id);
}
```

## MCP Server Configuration

The server uses the official Microsoft/Anthropic MCP SDK. Configure in `.mcp.json`:

```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "C:/path/to/WpfVisualTreeMcp.Server.exe",
      "args": []
    }
  }
}
```

## CLI Mode

The server executable doubles as a command-line tool. `WpfVisualTreeMcp.Server.exe`
with **no arguments** runs the MCP stdio server; with **any recognised subcommand**
it runs a single one-shot CLI command instead (`Program.cs` checks `args[0]` via
`CliRunner.IsCliCommand`). This gives the same 38 capabilities without an MCP
connection — useful when the MCP server is not connected, for scripting, or for
verifying the pipeline manually.

**Why it exists:** AI agents (and humans) can invoke inspection through a plain
shell call. No MCP handshake required; `--help` is self-documenting.

### CLI behaviour
- **Output:** JSON to stdout (indented by default, `--compact` for single-line).
  All logging goes to stderr so stdout stays parseable.
- **Errors:** `{"error": "..."}` to stdout + a plain line to stderr, exit code `1`.
- **Stateless:** each command re-creates the lightweight session internally, so
  element handles (which live in the Inspector) stay valid across separate calls
  as long as the target app keeps running.
- **Targeting:** every inspection command except `list` takes `--pid <id>` or `--process <name>`; `update` takes neither.
- **Updating:** `update [--check | --status]` is CLI-only (not an MCP tool). It updates
  the invoked global/custom-path tool or portable folder, never another copy.
  Source builds and package caches are detected and left to their own workflows.
  Tool updates stage the exact official NuGet package with bounded retries before
  scheduling; the worker installs from that local feed to avoid metadata propagation races.
  The embedded PowerShell worker waits for the CLI to exit before replacing locked
  binaries. `scheduled` is not completion; `--status` reports `updated` or `failed`.
  Official stable NuGet/GitHub sources only; ZIP digest verification and bounded,
  traversal-safe extraction happen before replacement. Portable writes retain
  backups and roll back on failure. Do not kill MCP/target processes during updates.
  `scripts/Test-Update.ps1` exercises actual public downloads in isolated portable
  and tool-path installations, using a test-only seed older than the stable release.
- **screenshot** writes a PNG file and prints its path (Claude reads it with Read);
  **export** writes to `--out` if given, otherwise prints content inline.
- **click / move-mouse / scroll / drag / touch / select-item / set-text / send-keys** are the state-changing input commands.
  - `drag` — physical mouse sequence between elements or by displacement; runs outside the UI Dispatcher.
  - `touch` — native tap, long press, swipe, pinch and rotation; requires application touch support.
  - `click` — UI Automation invoke by default; `--physical` for OS mouse click
    (auto-scrolls into view); `--click-type double|right` for double/right clicks
    (always physical; right opens context menus — capture with `screenshot --mode screen`).
  - `select-item` — select in ComboBox/ListBox/ListView/TabControl by `--item-text`
    (visible text, substring) or `--index`; works with virtualized items; on failure
    the error lists the available items.
  - `set-text` — `IValueProvider.SetValue` by default, with TextBox/PasswordBox
    direct-property and reflected fallbacks; `--physical` types via keyboard.
    The response reports the value read back after the write.
  - `send-keys` — OS-level keyboard input; modifiers `Ctrl/Shift/Alt/Win` plus
    letters, digits, F1-F12, and named keys.
  - `ControlInteractor` prepares interactions; mouse/touch executors run their timed sequences on workers.

### Typical CLI workflow
```bash
WpfVisualTreeMcp.Server.exe list                              # find the PID
WpfVisualTreeMcp.Server.exe attach --pid 1234 --auto-inject   # inject Inspector (once)
WpfVisualTreeMcp.Server.exe find --pid 1234 --type Button
WpfVisualTreeMcp.Server.exe props --pid 1234 --handle elem_00000052
WpfVisualTreeMcp.Server.exe screenshot --pid 1234 --out app.png
```
Run `WpfVisualTreeMcp.Server.exe help` for the full command list, or
`<command> --help` for one command.

## Release Checklist

When adding MCP/CLI commands, update the repository companion skill at
`plugins/wpfvisualtreemcp/skills/wpfvisualtreemcp/SKILL.md` as well as CLI help and
the tool reference. The plugin's manifest version tracks its configuration and
skill independently of the NuGet server. Its `dnx` launch resolves the stable
server at startup; plugin/skill changes require marketplace refresh/reinstallation.
An optional user-level Claude skill also exists outside the repository under
`~/.claude/skills/wpf-inspector/`. Publishing the server does not update that
skill's instructions or bundled `bin` directory; check them when maintaining a
user's local installation. CLI `update` replaces binaries, not skill instructions.

1. Bump `<Version>` in `src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj`
2. Bump **both** `version` fields in `src/WpfVisualTreeMcp.Server/.mcp/server.json`
   (registry manifest — must match the NuGet package version exactly)
3. Update `CHANGELOG.md` and `RELEASE_NOTES.md`
4. Commit, tag `vX.Y.Z`, push tag → `release.yml` builds the server and Codex plugin
   ZIPs, creates the GitHub release, packs the NuGet package and pushes it to
   nuget.org (trusted publishing / OIDC, using the `NUGET_USER` repo secret and
   the nuget.org publishing policy; skipped with a notice if absent). The plugin
   ZIP uses `git archive` to preserve its required dot-directories.
   `scripts/test-plugin.py` installs/upgrades it using an isolated Codex profile;
   it never opens a model session or changes the user's own plugin configuration.
5. Once the package is live on nuget.org, run the manual **"Publish to MCP Registry"**
   workflow (GitHub OIDC, no secrets) to update registry.modelcontextprotocol.io
6. For local MCPB distribution, use `scripts/Build-McpBundle.ps1` on a clean,
   built release checkout. It bundles the server runtime and a single-file x86
   helper, and generates metadata from the actual MCP tool list. Follow
   `docs/MCPB.md`, test the extracted archive, then attach the `.mcpb` to the
   corresponding GitHub release. Use `-Compact` for clients with upload limits;
   test the compact archive itself, including the gesture workflow. This
   packaging step is currently manual.

## Test Framework

- **xUnit** - Test runner
- **Moq** - Mocking
- **FluentAssertions** - Assertions
- Tests located in `tests/WpfVisualTreeMcp.Tests/`
