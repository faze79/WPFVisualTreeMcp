# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WpfVisualTreeMcp** is an MCP (Model Context Protocol) server that enables AI agents to inspect running WPF applications in real-time. It exposes WPF visual tree inspection capabilities similar to Snoop WPF or Visual Studio's Live Visual Tree through the MCP protocol.

## Build Commands

```bash
# Build managed solution
dotnet build WpfVisualTreeMcp.sln

# Build managed solution for Release
dotnet build -c Release WpfVisualTreeMcp.sln

# Build native auto-injection bootstrapper (Visual Studio MSBuild)
msbuild src/WpfVisualTreeMcp.Bootstrapper/WpfVisualTreeMcp.Bootstrapper.vcxproj /m /p:Configuration=Release /p:Platform=x64
msbuild src/WpfVisualTreeMcp.Bootstrapper/WpfVisualTreeMcp.Bootstrapper.vcxproj /m /p:Configuration=Release /p:Platform=Win32

# Run tests
dotnet test WpfVisualTreeMcp.sln

# Run the full self-hosted/auto-injection matrix (Windows PowerShell)
./tests/run-integration-tests.ps1

# Run sample WPF app for testing
dotnet run --project samples/SampleWpfApp --framework net8.0-windows

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
    ├─ WpfTools (28 tools)
    ├─ ProcessManager (discovers WPF processes)
    └─ NamedPipeBridge (IPC)
        ↓ [Named Pipes: wpf_inspector_{pid}]
Target WPF Application (.NET Framework 4.7.2/4.8 or .NET 8)
    └─ Inspector DLL (compatible framework payload)
        ├─ TreeWalker (visual tree navigation + adorner/popup traversal)
        ├─ ScreenshotCapture (RenderTargetBitmap element capture)
        ├─ PropertyReader (dependency properties)
        ├─ BindingAnalyzer (binding inspection)
        ├─ ControlInteractor (UI Automation / physical click)
        └─ IpcServer (named pipe communication)
```

**Key Design:** Multi-process architecture for safety. The server runs separately and communicates via named pipes. The six interaction/property commands — `wpf_click_element`, `wpf_select_item`, `wpf_set_text`, `wpf_send_keys`, `wpf_set_property`, and `wpf_revert_property` — drive controls or edit property values and change application state. `wpf_set_property` is reversible via `wpf_revert_property` (per-session undo stack that restores the prior binding, local value, or default). `wpf_highlight_element` temporarily changes the UI, while `wpf_clear_binding_errors` clears Inspector-held diagnostics.

## Key Source Locations

| Component | Location | Purpose |
|-----------|----------|---------|
| MCP Server Entry | `src/WpfVisualTreeMcp.Server/Program.cs` | Server init with MCP SDK; routes to CLI mode if args present |
| Tool Definitions | `src/WpfVisualTreeMcp.Server/WpfTools.cs` | 28 tools with `[McpServerTool]` attributes |
| CLI Front-End | `src/WpfVisualTreeMcp.Server/Cli/CliRunner.cs` | Command-line front-end over the same services (28 commands) |
| Trigger / Style Diagnostics | `ResourceInspector.ExplainTriggers` | Evaluate Style + ControlTemplate triggers vs current state (active/inactive + setters); attribute a property value to its style setter / active trigger |
| Control Interactor | `src/WpfVisualTreeMcp.Inspector/ControlInteractor.cs` | Clicks, text input, and keyboard shortcuts (UI Automation + SendInput physical fallback) |
| Property Writer | `src/WpfVisualTreeMcp.Inspector/PropertyWriter.cs` | Live-edits dependency properties (TypeConverter coercion) with a per-session undo stack (restores prior binding/local/default) |
| Snapshot / Diff | `TreeWalker.CaptureSnapshot` + `InspectorService.ComputeDiff` | Capture a subtree's curated state keyed by element handle; diff two snapshots (handle stable → changed/added/removed) |
| Injector Helper | `src/WpfVisualTreeMcp.InjectorHelper/Program.cs` | 32-bit .NET 8 helper exe spawned by `ProcessInjector` for cross-arch injection (v0.6.0) |
| IPC Bridge | `src/WpfVisualTreeMcp.Server/Services/NamedPipeBridge.cs` | Named pipe communication to Inspector |
| Process Manager | `src/WpfVisualTreeMcp.Server/Services/ProcessManager.cs` | WPF process discovery and attachment |
| Inspector Entry | `src/WpfVisualTreeMcp.Inspector/InspectorService.cs` | Injected or self-hosted DLL main entry point |
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

### Named Pipe Communication
- Pipe name format: `wpf_inspector_{pid}`
- Connection timeout: 5 seconds
- Request timeout: 30 seconds
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
- Two modes: `render` (default) uses `RenderTargetBitmap` + `VisualBrush` (handles
  transforms, works when the window is covered, but cannot see Popups/menus);
  `screen` uses GDI BitBlt of the on-screen pixels (includes open Popups, ComboBox
  dropdowns, context menus and tooltips — requires the window visible and unobstructed)
- DPI-aware via `PresentationSource.FromVisual`
- Downscales if exceeding `max_width`/`max_height` (default 1920x1080)
- Captures the element's current arranged bounds by default; `full_content=true`
  (render mode only) locates the target's largest ScrollViewer, renders ordinary
  content directly, or pages and stitches virtualized content before restoring offsets
- Full-content capture supports physical scrolling in both dimensions and logical
  virtualized scrolling vertically; logical virtualized horizontal overflow is rejected
- Returns MCP `ImageContentBlock` (base64 PNG) — Claude sees the image directly

### Logging
- Server logs: `%LOCALAPPDATA%\WpfVisualTreeMcp\logs\mcp-server-YYYYMMDD.log`
- Inspector debug: `%TEMP%\WpfInspector_Debug.log`
- Bootstrapper debug: `%TEMP%\WpfInspectorBootstrapper.log`
- stdout must remain clean for JSON-RPC protocol

## WPF App Inspection Modes

### Auto-Injection Mode
Use `wpf_attach(process_id=<pid>, auto_inject=true)` to inject the Inspector into a running .NET Framework or .NET 8 WPF app. Requires:
- Native bootstrapper DLL in `publish/native/x64/` (or x86)
- A supported CLR and matching Inspector payload (`net48` for .NET Framework or `net8.0-windows` for CoreCLR)
- Architecture detection and payload selection use the target process (x64 vs x86)

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

The Inspector multi-targets `net472`, `net48`, and `net8.0-windows`, so a project
reference selects the build matching the WPF application's target framework.
Self-hosting avoids runtime injection but requires modifying the application.

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
`CliRunner.IsCliCommand`). This gives the same 28 capabilities without an MCP
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
- **Targeting:** every command except `list` takes `--pid <id>` or `--process <name>`.
- **screenshot** writes a PNG file and prints its path (Claude reads it with Read);
  **export** writes to `--out` if given, otherwise prints content inline.
- **click / select-item / set-text / send-keys / set-property / revert-property**
  are the six commands that change application state. `highlight` changes the UI
  temporarily, and `clear-binding-errors` clears Inspector-held diagnostics.
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
  - The first four live in `ControlInteractor`; property edits use `PropertyWriter`.

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

1. Bump `<Version>` in `src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj`
2. Bump **both** `version` fields in `src/WpfVisualTreeMcp.Server/.mcp/server.json`
   (registry manifest — must match the NuGet package version exactly)
3. Update `CHANGELOG.md` and `RELEASE_NOTES.md`
4. Commit, tag `vX.Y.Z`, push tag → `release.yml` builds the zip, creates the GitHub
   release, packs the NuGet package and pushes it to nuget.org using trusted
   publishing (requires the nuget.org policy and `NUGET_USER` repo secret;
   skipped with a notice if the secret is absent)
5. Once the package is live on nuget.org, run the manual **"Publish to MCP Registry"**
   workflow (GitHub OIDC, no secrets) to update registry.modelcontextprotocol.io

## Test Framework

- **xUnit** - Test runner
- **Moq** - Mocking
- **FluentAssertions** - Assertions
- Server and Injector tests: `tests/WpfVisualTreeMcp.Tests/` (`net8.0`)
- Shared IPC/model tests: `tests/WpfVisualTreeMcp.Shared.Tests/`
  (`net472`, `net48`, and `net8.0`)
- Live framework/architecture/mode matrix: `tests/WpfVisualTreeMcp.IntegrationTests/`
  (run through `tests/run-integration-tests.ps1`)
