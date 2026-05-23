# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2026-05-23

### Added

- **`wpf_set_text` MCP tool and `set-text` CLI command.** Replace the
  text/value of an element (TextBox, ComboBox, RichTextBox, PasswordBox, ...).
  - Default: UI Automation `IValueProvider.SetValue(text)` — clean, no focus
    needed, raises proper events, refuses read-only fields with a clear error.
  - Fallbacks when no value pattern is exposed: `TextBox.Text`,
    `PasswordBox.Password`, then a reflected string `Text` property (covers
    many third-party controls without an automation peer).
  - `physical=true` / `--physical` — focuses the element, clears existing
    text with `Ctrl+A` + `Delete`, then types each character via `SendInput`
    with `KEYEVENTF_UNICODE` (full Unicode BMP, not just ASCII).

- **`wpf_send_keys` MCP tool and `send-keys` CLI command.** Send a keyboard
  shortcut / key combination to an element (or to whatever currently has
  focus, when no handle is given).
  - Modifiers: `Ctrl`, `Shift`, `Alt`, `Win`.
  - Keys: `A`-`Z`, `0`-`9`, `F1`-`F12`, `Enter`, `Esc`, `Tab`, `Space`,
    `Backspace`, `Delete`, `Insert`, `Home`, `End`, `PageUp`, `PageDown`,
    `Up`, `Down`, `Left`, `Right`.
  - Examples: `Ctrl+S`, `Ctrl+Shift+F`, `Alt+F4`, `F5`, `Enter`, `Win+R`.

- `KeyComboParser` and `SendInput` + `KEYBDINPUT` interop in
  [`ControlInteractor`](src/WpfVisualTreeMcp.Inspector/ControlInteractor.cs);
  `SetTextResult` and `SendKeysResult` shared models.

### Changed

- `WpfTools` now exposes **20 tools** (up from 18) — 17 read-only inspection
  + 3 state-changing (`click`, `set-text`, `send-keys`).
- `ControlInteractor.ClickOutcome` renamed to `InteractionOutcome` (it now
  serves click, set-text, and send-keys). Internal type — no public API
  impact.
- `CLAUDE.md`, the architecture diagram, and the `Key Source Locations`
  table updated to reflect three state-changing commands.

### Testing

All 48 existing unit tests pass against this change.

## [0.4.0] - 2026-05-23

### Added

- **CLI mode.** `WpfVisualTreeMcp.Server.exe` now doubles as a one-shot
  command-line tool whenever it's invoked with a recognised subcommand
  (`list`, `attach`, `tree`, `find`, `props`, `bindings`, `screenshot`, ...).
  With no arguments it still runs as the MCP stdio server, exactly as before.
  - The CLI is dispatched from `Program.cs` via the new
    [`CliRunner`](src/WpfVisualTreeMcp.Server/Cli/CliRunner.cs).
  - Output is JSON on stdout (`--compact` for single-line), with logging
    forced to stderr so the output stays pipe-friendly.
  - `screenshot` writes a PNG file and prints its path (so an AI agent can
    re-read the image with its normal file-read tool, no base64 round-trip).
  - `export` writes to `--out` if given, otherwise prints content inline.
  - Each invocation is stateless — element handles live inside the Inspector
    in the target process, so they remain valid across separate CLI calls.

- **`wpf_click_element` MCP tool and `click` CLI command.** Interact with WPF
  controls — the first state-changing capability in an otherwise read-only
  tool.
  - **Default (UI Automation):** invokes the control's action via the first
    matching automation pattern — `Invoke` for buttons/menu items/hyperlinks,
    `Toggle` for checkboxes/radio buttons, `Select` for list/tab/combo items,
    expand/collapse for expanders. No cursor movement, no window focus.
    Elements with no pattern fall back to best-effort routed mouse events.
  - **`physical=true` / `--physical`:** real OS mouse click at the element's
    on-screen centre. Works on any visible element, but moves the cursor and
    brings the window forward.
  - Implemented in
    [`ControlInteractor`](src/WpfVisualTreeMcp.Inspector/ControlInteractor.cs)
    in the Inspector.

- **User-level Claude Code skill** for the wpf-inspector tooling (lives
  outside the repo under the user's `.claude/skills/wpf-inspector/`).
  Documents all 18 commands and bundles a self-contained Release build so it
  works in any project.

### Changed

- `WpfTools` now exposes **18 tools** (up from 17).
- `CLAUDE.md`, the architecture diagram, and the `Key Source Locations`
  table updated for dual-mode operation and the new `ControlInteractor`.
- `WpfVisualTreeMcp.Inspector` (net48 build) now references
  `UIAutomationProvider` and `UIAutomationTypes`; net8.0-windows pulls them
  in automatically via `UseWPF=true`.

### Known limitations

- **Auto-injection is same-architecture only.** A 64-bit CLI/MCP server
  cannot inject into a 32-bit target — the remote `LoadLibraryW` thread
  starts at the injector's 64-bit `kernel32` address, which is invalid in
  the target's 32-bit address space, and the bootstrapper never runs (silent
  "Injection failed", no log entries). For 32-bit WPF apps use **self-hosted
  mode** (reference the Inspector and call
  `InspectorService.Initialize(Process.GetCurrentProcess().Id)` in
  `OnStartup`), or ship an x86 build of the server. A clearer error message
  on bitness mismatch is a planned follow-up.

## [1.0.0] - 2025-12-02

### Added
- Added `claude mcp add` command line instructions for easier Claude Code configuration
- Updated documentation with multiple configuration options (CLI vs JSON)

### Changed

#### Migration to Official MCP SDK
- **BREAKING**: Migrated from custom MCP protocol implementation to official [Microsoft/Anthropic MCP SDK for .NET](https://github.com/modelcontextprotocol/csharp-sdk)
- **BREAKING**: Configuration now requires direct path to `.exe` file instead of `dotnet run`
- Simplified `Program.cs` from 55 lines to 28 lines using SDK's built-in features
- All 13 WPF inspection tools now use `[McpServerTool]` attributes for auto-discovery
- Improved protocol compatibility and stability with Claude Code

#### Benefits of Migration
- ✅ **Guaranteed compatibility** with Claude Code and other MCP clients
- ✅ **Faster connection** (~463ms vs previous timeouts)
- ✅ **Automatic protocol negotiation** - no more version mismatches
- ✅ **Better maintainability** - SDK handles all JSON-RPC details
- ✅ **Official support** from Microsoft/Anthropic

#### Technical Changes
- Added NuGet dependency: `ModelContextProtocol` (v0.4.1-preview.1)
- Removed custom `McpServer.cs` protocol implementation (722 lines → SDK managed)
- Created new `WpfTools.cs` with declarative tool definitions
- Simplified logging configuration - completely disabled for stdio protocol
- Removed UTF-8 BOM encoding issues that caused JSON parse errors

### Fixed
- Fixed connection timeout issues with Claude Code (was 30+ seconds, now <500ms)
- Fixed JSON parsing errors caused by log output on stdout
- Fixed protocol version negotiation (now accepts client's version)
- Fixed notification handling (no longer sends error responses for notifications)

### Migration Guide

**Old Configuration:**
```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/WpfVisualTreeMcp.Server"]
    }
  }
}
```

**New Configuration:**
```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe",
      "args": []
    }
  }
}
```

**Steps:**
1. Build the project: `dotnet build -c Release`
2. Update your MCP configuration with absolute path to the `.exe`
3. Restart Claude Code
4. Verify tools appear with `mcp__wpf-visual-tree__` prefix

[1.0.0]: https://github.com/faze79/WpfVisualTreeMcp/releases/tag/v1.0.0
