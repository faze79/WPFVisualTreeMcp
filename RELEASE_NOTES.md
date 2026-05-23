# Release Notes

## v0.5.0 — Text input and keyboard shortcuts (2026-05-23)

This release extends v0.4.0's interaction surface with two more state-changing commands so an AI agent can fully drive a WPF app: **type text** into inputs and **send keyboard shortcuts**.

### `wpf_set_text` / `set-text` — fill TextBox / ComboBox / RichTextBox / PasswordBox

- **Default — UI Automation.** `IValueProvider.SetValue(text)`. Clean, no focus needed, raises proper events. Read-only fields are refused with a clear error.
- **Fallbacks when no value pattern is exposed:** `TextBox.Text` → `PasswordBox.Password` → reflected string `Text` property (covers many third-party controls without an automation peer).
- **`physical=true` / `--physical`.** Focuses the element, clears with `Ctrl+A` + `Delete`, then types each character via `SendInput` with `KEYEVENTF_UNICODE` — full Unicode BMP support, not just ASCII.

```text
wpfinspect set-text --pid 1234 --handle elem_0052 --text "hello world"
wpfinspect set-text --pid 1234 --handle elem_0052 --text "12345" --physical
```

### `wpf_send_keys` / `send-keys` — keyboard shortcuts

Send a key combination via OS keyboard input. The keys go to the focused element first and bubble up to window-level `InputBindings`, so window-scoped commands (like Save) work even when you target a child element.

- **Modifiers:** `Ctrl`, `Shift`, `Alt`, `Win`.
- **Keys:** `A`-`Z`, `0`-`9`, `F1`-`F12`, `Enter`, `Esc`, `Tab`, `Space`, `Backspace`, `Delete`, `Insert`, `Home`, `End`, `PageUp`, `PageDown`, `Up`, `Down`, `Left`, `Right`.
- **`element_handle` is optional** — when omitted, keys go to whatever currently has keyboard focus.

```text
wpfinspect send-keys --pid 1234 --keys "Ctrl+S"
wpfinspect send-keys --pid 1234 --keys "Alt+F4"
wpfinspect send-keys --pid 1234 --keys "Enter" --handle elem_0052
wpfinspect send-keys --pid 1234 --keys "F5"
```

### Implementation notes

- `ControlInteractor.ClickOutcome` renamed to `InteractionOutcome` (now shared by `Click`, `SetText`, and `SendKeys`). Internal type, no public API impact.
- Native interop adds `SendInput` with proper `INPUT`/`KEYBDINPUT`/`InputUnion` structs alongside the existing `mouse_event`. Unicode typing uses `KEYEVENTF_UNICODE` so the full BMP is sent through `SendInput`, while modifier/function keys use `keybd_event` for simplicity.
- `IValueProvider.SetValue` uses `UIAutomationProvider` + `UIAutomationTypes`, already referenced as of v0.4.0.

### Testing

All 48 existing unit tests pass. CLI smoke tests cover help surfacing, the error paths (missing args, unknown modifier/key in the parser), and the new dispatch.

### Tool count

`WpfTools` now exposes **20 tools**: 17 read-only inspection + 3 state-changing (`click`, `set-text`, `send-keys`).

### Asset

`WpfVisualTreeMcp-v0.5.0-win-x64.zip` — framework-dependent publish of the Server/CLI (.NET 8 Desktop Runtime required on the target machine), including the x64 and x86 native bootstrappers under `native/`.

---

## v0.4.0 — CLI mode and click interaction (2026-05-23)

This release adds two substantial capabilities without changing how the MCP
server itself behaves: a one-shot **CLI front-end** sharing the same code as
the MCP tools, and a **`wpf_click_element`** / `click` command that actually
drives controls.

### What's new

#### Dual-mode executable — CLI + MCP

`WpfVisualTreeMcp.Server.exe` with no arguments still runs as the MCP stdio
server. With any recognised subcommand (`list`, `attach`, `tree`, `find`,
`props`, `bindings`, `screenshot`, ...) it runs as a **one-shot CLI**
instead. Same 18 capabilities, no MCP connection required — useful when
the MCP server is not connected, for scripting, and for verifying the
pipeline manually.

```text
WpfVisualTreeMcp.Server.exe list
WpfVisualTreeMcp.Server.exe attach --pid 1234 --auto-inject
WpfVisualTreeMcp.Server.exe find --pid 1234 --type Button
WpfVisualTreeMcp.Server.exe screenshot --pid 1234 --out app.png
WpfVisualTreeMcp.Server.exe help                # full reference
WpfVisualTreeMcp.Server.exe <command> --help    # one command
```

- Output is JSON on stdout (use `--compact` for single-line); logging is
  routed to stderr so stdout stays pipe-friendly.
- `screenshot` writes a PNG file and prints its path — an AI agent can
  re-read the file with its normal Read tool, no base64 round-trip.
- `export` writes to `--out` if given, otherwise prints content inline.
- Each invocation is stateless. Element handles live inside the Inspector
  in the *target* process, so they remain valid across separate CLI calls
  for as long as the target app keeps running.

#### `wpf_click_element` — drive controls

A new MCP tool (and `click` CLI command) lets an AI agent actually interact
with WPF controls — the first state-changing capability in an otherwise
read-only tool.

- **Default — UI Automation.** Invokes the control's action via the first
  matching pattern: `Invoke` (buttons, menu items, hyperlinks), `Toggle`
  (checkboxes, radio buttons, toggle buttons), `SelectionItem` (list items,
  tab items, combo box items), `ExpandCollapse` (expanders, tree view
  items). No cursor movement, no window focus required. Elements with no
  automation pattern fall back to best-effort routed mouse events.
- **`physical=true` / `--physical`.** A real OS mouse click at the
  element's on-screen centre — moves the cursor and brings the window
  forward. Use this when the default doesn't trigger the behaviour you
  want (custom-drawn elements without an automation peer, etc.).
- The response reports which `method` actually fired, so the caller knows
  whether the action was an `Invoke`, a `Toggle`, a `Physical` click, etc.
- Disabled or zero-size elements are refused with a clear error.

#### User-level Claude Code skill

A standalone `wpf-inspector` skill (installed under
`~/.claude/skills/wpf-inspector/`) bundles a self-contained Release build
of the CLI. It documents the workflow and all 18 commands so any Claude
Code session on the machine can drive and inspect WPF apps without setting
up an MCP server.

### Known limitations

**Auto-injection is same-architecture only.** A 64-bit CLI / MCP server
cannot inject into a 32-bit target — the remote `LoadLibraryW` thread
starts at the injector's 64-bit `kernel32` address, which is invalid in the
target's 32-bit address space, and the bootstrapper DLL never runs (silent
"Injection failed", no log entries). For 32-bit WPF apps the workaround is
**self-hosted mode** — reference the Inspector DLL and call
`InspectorService.Initialize(Process.GetCurrentProcess().Id)` from
`App.OnStartup`. The Inspector's `net8.0-windows` build is AnyCPU and loads
fine inside an x86 process. A clearer error on bitness mismatch (instead of
the current silent failure) is a planned follow-up.

### Files of note

- `src/WpfVisualTreeMcp.Server/Cli/CliRunner.cs` — new CLI front-end.
- `src/WpfVisualTreeMcp.Server/Program.cs` — routes to CLI vs MCP based
  on `args[0]`.
- `src/WpfVisualTreeMcp.Inspector/ControlInteractor.cs` — UI Automation
  patterns + synthetic mouse fallback + physical OS click.
- `src/WpfVisualTreeMcp.Shared/Models/InteractionModels.cs` — new
  `ClickResult` model.
- `src/WpfVisualTreeMcp.Inspector/WpfVisualTreeMcp.Inspector.csproj` —
  adds `UIAutomationProvider` / `UIAutomationTypes` references for net48.

---

## Recent Improvements (PR #10)

### Critical Bug Fixes

#### 1. IPC Communication Deadlock Fix (.NET Framework 4.8)
**Problem:** Inspector calls were hanging indefinitely (~30+ seconds timeout) when communicating with WPF applications.

**Root Cause:** `StreamReader`/`StreamWriter` on `NamedPipeServerStream` causes deadlocks in .NET Framework 4.8.

**Solution:** Complete rewrite of `IpcServer.cs` using direct byte I/O:
- Replaced `StreamReader`/`StreamWriter` with direct `pipeServer.ReadAsync()` and `WriteAsync()`
- Manual newline detection and string building
- Response time reduced from 30+ seconds to ~340ms

**Files Changed:**
- `src/WpfVisualTreeMcp.Inspector/IpcServer.cs`

#### 2. UTF-8 BOM Parsing Error Fix
**Problem:** JSON deserialization errors with message: `'0xEF' is an invalid start of a value`

**Root Cause:** UTF-8 Byte Order Mark (BOM: 0xEF 0xBB 0xBF) appearing in JSON strings during byte-to-string conversion.

**Solution:** Added BOM stripping before JSON deserialization:
```csharp
// Remove UTF-8 BOM if present (0xEF 0xBB 0xBF = U+FEFF)
if (line.Length > 0 && line[0] == '\uFEFF')
{
    line = line.Substring(1);
}
```

**Files Changed:**
- `src/WpfVisualTreeMcp.Inspector/IpcServer.cs`

#### 3. Dispatcher Thread Deadlock Prevention
**Problem:** UI thread could block during inspector request processing.

**Solution:**
- Wrapped Dispatcher.Invoke in Task.Run to avoid blocking named pipe thread
- Added 10-second timeout for UI operations
- Comprehensive debug logging for diagnostics

**Files Changed:**
- `src/WpfVisualTreeMcp.Inspector/InspectorService.cs`

#### 4. Helpful Error Messages for Stale PID Connections
**Problem:** When AI agents attempted to use MCP tools with an obsolete PID (from a restarted application), they received generic errors like "An error occurred invoking 'wpf_find_elements'" without explanation or guidance.

**Root Cause:** The MCP server didn't check if the target process still existed before attempting connection, resulting in uninformative error messages.

**Solution:** Enhanced error detection and messaging in `NamedPipeBridge`:
- Validates target process exists before connection attempt
- Provides specific error messages for different scenarios
- Includes actionable guidance in every error message

**Error Message Examples:**
```
Process 25076 no longer exists. The application may have been closed
or restarted. Use wpf_list_processes() to see available WPF applications,
then wpf_attach(process_id=<new_pid>) to connect to the current instance.
```

```
Connection to process 38668 timed out. The Inspector may not be loaded.
Try restarting the application or use wpf_list_processes() and
wpf_attach() to reconnect.
```

**Benefits:**
- AI agents receive actionable guidance instead of generic errors
- Clear explanation of what went wrong
- Specific instructions on how to fix the issue
- Reduces debugging time and user confusion

**Files Changed:**
- `src/WpfVisualTreeMcp.Server/Services/NamedPipeBridge.cs`

### New Features

#### 1. `max_results` Parameter for `wpf_find_elements`
**Problem:** Finding common UI elements (like `TabItem`) in complex applications returned hundreds of results, filling Claude Code context with 25k+ tokens and causing response truncation.

**Solution:** Added optional `max_results` parameter (default: 50) to limit search results:

```csharp
// Default: returns up to 50 results
wpf_find_elements(type_name: "TabItem")

// Custom limit
wpf_find_elements(type_name: "Button", max_results: 10)

// Broader search
wpf_find_elements(type_name: "TextBox", max_results: 100)
```

**Benefits:**
- ✅ Prevents context overflow
- ✅ Faster performance (early termination when limit reached)
- ✅ Flexible and backwards compatible
- ✅ Default value (50) handles most use cases

**Files Changed:**
- `src/WpfVisualTreeMcp.Inspector/TreeWalker.cs`
- `src/WpfVisualTreeMcp.Shared/Ipc/IpcMessages.cs`
- `src/WpfVisualTreeMcp.Inspector/InspectorService.cs`
- `src/WpfVisualTreeMcp.Server/Services/IIpcBridge.cs`
- `src/WpfVisualTreeMcp.Server/Services/NamedPipeBridge.cs`
- `src/WpfVisualTreeMcp.Server/WpfTools.cs`

#### 2. Automatic Binding Details in `wpf_get_element_properties`
**Problem:** When AI agents called `wpf_get_element_properties`, they could see `isBinding=true` but had no details about the binding (path, source, mode, status). They needed to make a separate call to `wpf_get_bindings` to get this information, requiring extra round trips.

**Solution:** Enhanced `wpf_get_element_properties` to automatically include complete binding details when `isBinding=true`.

**New JSON Structure:**
```json
{
  "name": "Text",
  "typeName": "System.String",
  "value": "Hello World",
  "source": "Local",
  "isBinding": true,
  "bindingDetails": {
    "path": "UserName",
    "sourceType": "DataContext",
    "mode": "TwoWay",
    "updateSourceTrigger": "PropertyChanged",
    "converter": "StringToUpperConverter",
    "status": "Active",
    "hasError": false
  }
}
```

**Binding Details Include:**
- `path` - Binding path expression
- `sourceType` - DataContext, ElementName, RelativeSource, or explicit type
- `elementName` - For ElementName bindings
- `relativeSourceMode` - For RelativeSource bindings (Self, FindAncestor, etc.)
- `ancestorType`/`ancestorLevel` - For FindAncestor mode
- `mode` - OneWay, TwoWay, OneWayToSource, OneTime
- `updateSourceTrigger` - PropertyChanged, LostFocus, Explicit
- `converter` - Converter type name if present
- `status` - Active, Error, PathError, Inactive, etc.
- `hasError` - Boolean flag for validation errors
- `errorMessage` - Validation error message if present

**Benefits:**
- ✅ Single call returns complete property AND binding information
- ✅ Reduces round trips for AI agents
- ✅ Easier to understand property values in context of their bindings
- ✅ `wpf_get_bindings` still available for binding-only queries
- ✅ No breaking changes - just additional data

**Files Changed:**
- `src/WpfVisualTreeMcp.Inspector/PropertyReader.cs`

### Development Tools

#### 1. `sync-to-values.ps1` Utility Script
Automated script for synchronizing Inspector DLLs to target applications:

```powershell
# Sync DLLs and restart application
.\sync-to-values.ps1

# Sync without restarting
.\sync-to-values.ps1 -NoRestart

# Custom application path
.\sync-to-values.ps1 -ValuesExePath "C:\Path\To\App.exe"
```

**Features:**
- Automatically stops target application
- Copies updated Inspector and Shared DLLs
- Optionally restarts application
- Shows DLL modification timestamps
- Provides next-step instructions

#### 2. Enhanced Debug Logging
Added comprehensive debug logging to `WpfInspector_Debug.log` in temp directory:
- Request/response tracking
- Thread IDs for Dispatcher debugging
- Timing information
- Error stack traces
- UTF-8 BOM detection

**Log Location:** `%TEMP%\WpfInspector_Debug.log`

### Performance Improvements

| Operation | Before | After | Improvement |
|-----------|--------|-------|-------------|
| IPC Request | 30+ sec (timeout) | ~340 ms | **99% faster** |
| Find Elements | 25k+ tokens | Configurable | **Context-friendly** |
| Error Recovery | App crash | Graceful degradation | **More reliable** |

### Migration Guide

**For Existing Users:**

1. **Rebuild the project:**
   ```bash
   dotnet build -c Release
   ```

2. **Update your MCP configuration** with absolute path to `.exe`

3. **Restart Claude Code** to reload the MCP server

4. **Test the improvements:**
   ```
   wpf_attach(process_id: <PID>)
   wpf_find_elements(type_name: "Button", max_results: 10)
   ```

### Known Limitations

1. **Handle Caching:** Element handles are valid only within the same MCP server session. Restarting Claude Code invalidates all handles.

2. **Visual Tree Depth:** Deep template hierarchies may require multiple calls with increased `max_depth` parameter.

3. **Process Restart Detection:** If you restart your WPF application, you must call `wpf_attach` again with the new PID.

### Troubleshooting

#### "Element not found" Errors
**Cause:** Using handles from a previous MCP server session or different process instance.
**Solution:** Restart Claude Code and call `wpf_attach` again.

#### "An error occurred invoking..." Generic Errors
**Cause:** MCP server is connected to an old/dead process instance.
**Solution:** Restart Claude Code and verify the correct PID with `wpf_list_processes`.

#### Truncated Find Results
**Cause:** Using old server without `max_results` parameter.
**Solution:** Restart Claude Code to load updated server, use `max_results` parameter.

### Testing

Tested with production WPF application (ValueS) with:
- ✅ 200+ TabItem elements successfully filtered
- ✅ All inspection operations <500ms response time
- ✅ No JSON parsing errors
- ✅ Stable over multiple attach/detach cycles

### Contributors

- Fix implementation and testing by Claude (Anthropic)
- Issue reporting and validation by @faze79
