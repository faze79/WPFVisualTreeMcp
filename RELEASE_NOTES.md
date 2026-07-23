# Release Notes

## v0.12.0 — Trigger & style diagnostics (2026-07-24)

If you've debugged WPF, you've done this by hand: crack open the visual tree, hunt through a `Style` or `ControlTemplate`, and try to work out why a trigger isn't reacting — or which setter, in which style or trigger, actually produced a value. This release does it for you.

### `wpf_explain_triggers`

Give it an element and it lists the triggers from its **Style** (following `BasedOn`) **and its `ControlTemplate`** — the latter being where most visual-state triggers actually live (`IsMouseOver`, `IsEnabled`, `IsKeyboardFocused`, ...). Crucially, it doesn't just list them, it **evaluates** each one against the live state:

- the condition (`property = expected`),
- the property's **current** value,
- whether it **matches**,
- whether the trigger is **active**,
- and the **setters** it applies.

So "why doesn't my trigger fire?" gets a direct answer: its condition property currently holds a different value than the trigger expects — and you can see both, side by side.

Pass `property_name` and it also **attributes** that property's effective value to its exact source: a specific style setter (which style, which setter), an active trigger (which condition), or local / inherited / default.

```
explain-triggers --pid 1234 --handle elem_00                    # evaluate all triggers
explain-triggers --pid 1234 --handle elem_00 --property Foreground   # + attribute this value
```

Verified live against the sample app: disable a TextBox and its ControlTemplate's `IsEnabled=False` trigger shows up as `active: true`, applying `Opacity=0.56` — i.e. "it looks greyed out because this template trigger is firing". And a custom button's `Foreground` is attributed to the precise style setter that set it.

Handles `Trigger`, `MultiTrigger`, `DataTrigger` (best-effort binding evaluation) and `EventTrigger`. Read-only. Together with `wpf_evaluate_binding` (v0.11.0), the "why is it *this* value?" story now covers bindings, styles and triggers.

---

## v0.11.0 — Why is this the value? (2026-07-24)

The single most common question an AI agent gets about a WPF app is *"why is this empty / wrong / disabled?"*. This release answers it.

### `wpf_evaluate_binding`

Given an element and a property, it explains **why** the property has its current value:

- the **value source** — `Local`, `Style`, `Binding`, `Inherited`, `Default`, a trigger, ... — plus whether it's an expression, animated, or coerced;
- the **effective value** and its type;
- and, when the value comes from a binding, a **hop-by-hop resolution of the binding path** against its source.

That last part is the point. Instead of just telling you "there's a binding to `User.Address.City`", it walks the path against the live source and shows each step's value and type — so a broken binding tells you *exactly* where it broke: a null intermediate (`Address` is null, so `City` can't resolve), or a property that doesn't exist on the DataContext type (the classic typo).

```
evaluate-binding --pid 1234 --handle elem_00 --property Text
```

Verified live against the sample app's deliberately misspelled binding:

```json
"binding": {
  "path": "Statsu", "status": "PathError", "sourceType": "SampleWpfApp.MainViewModel",
  "resolution": {
    "hops": [{ "segment": "Statsu", "found": false,
               "reason": "property 'Statsu' not found on type 'MainViewModel'" }],
    "brokenAt": "Statsu"
  }
}
```

A working binding resolves hop-by-hop to its value with `brokenAt: null`; a non-bound property reports its value source and `binding: null`. It resolves `DataContext`, `ElementName`, `RelativeSource` (Self / TemplatedParent / FindAncestor) and explicit `Source`, and handles indexers in the path. Read-only.

This complements `wpf_get_bindings` (which *lists* bindings) by actually *evaluating* one, and rounds out the binding-diagnostics story that is this project's core differentiator.

---

## v0.10.0 — Snapshot & diff: measure a change's effect (2026-07-15)

v0.9.0 let an agent *make* a live UI change. This release lets it *measure* the effect — closing the "change → is it effective?" loop.

### `wpf_snapshot` + `wpf_diff`

Capture the state of an element subtree, make a change, capture it again, and diff:

```
snapshot --pid 1234 --handle elem_00 --label before
set-property --pid 1234 --handle elem_00 --property Margin --value '40,40,40,40'
snapshot --pid 1234 --handle elem_00 --label after
diff --pid 1234 --before before --after after
```

The diff reports, for every element that changed, the exact properties (`from` → `to`), plus anything added or removed. Snapshots are keyed by **element handle**, which is stable per element, so the same control lines up across the two captures.

Captured per element: visibility, `IsEnabled`, `Opacity`, `ActualWidth`/`Height`, `Margin`/`Padding`, alignment, `Background`/`Foreground`/`BorderBrush`, and text.

### Why it's more than "look at a screenshot"

A screenshot shows *that* something looks different; the diff shows *exactly what*. Verified live against the sample app: editing the layout root's `Margin` and `Background` reports the direct change **and the cascade** — 13 descendant elements show their `ActualWidth`/`ActualHeight` shrinking by 80px (40 per side) as the new margin reflows the subtree. That knock-on effect is precisely what you want to see when deciding whether a tweak did what you intended.

Both tools are read-only. This completes the live-tweak loop started in v0.9.0.

---

## v0.9.0 — Live property editing (2026-07-13)

"Will this change actually work?" — the question you'd normally answer by editing XAML, rebuilding, and relaunching. This release lets an agent answer it in seconds, on the running app.

### `wpf_set_property` — live-edit a dependency property

Change any dependency property at runtime. The string value is converted to the property's type via its `TypeConverter`, so the common cases just work:

```
set-property --pid 1234 --handle elem_00 --property Margin     --value '20,0,20,0'
set-property --pid 1234 --handle elem_00 --property Visibility  --value Collapsed
set-property --pid 1234 --handle elem_00 --property Background  --value Red        # or '#FF0000'
set-property --pid 1234 --handle elem_00 --property Width       --value 300
```

It returns the coerced value read back and what previously held the property (`Binding` / `Local` / `Unset`). Pair it with `wpf_capture_screenshot` to *see* the effect. This is the Snoop / VS "Live Property Explorer" experience, exposed to an AI agent — and, as with the rest of this project, it needs **no probe installed in the target app**.

### `wpf_revert_property` — undo, exactly

Every edit is reversible. Revert the most recent one, a filtered one, or `--all`:

```
revert-property --pid 1234 --all
```

The revert restores the *exact* prior state — and that includes a **binding** you overwrote. Overwrite a data-bound `Text` with a test value, look at the result, then revert and the binding is back (not just the previous string). Backed by a per-session undo stack.

### Verified live

Against the sample app: `Margin` and `Background` edits on the layout root shift and recolor the UI visibly in a screenshot; overwriting the bound Status `Text` reports `previousSource: "Binding"`, and `revert --all` restores the margin, the background, and the binding in one call.

### What's next

`wpf_diff` — a before/after snapshot so the agent can *measure* the effect of a change automatically, not just look at it. See [docs/ROADMAP.md](docs/ROADMAP.md).

---

## v0.8.0 — Wait for UI conditions, and concurrent IPC (2026-07-12)

Agent loops that drive a WPF app keep hitting the same wall: you click something, and the next step races the app's async work — the dialog hasn't opened yet, the spinner hasn't cleared, the button isn't enabled. Until now the only answer was sleep-and-retry from the agent side. This release adds a proper wait.

### `wpf_wait_for` / `wait-for` — the 22nd tool

Wait until an element matching `type_name` / `element_name` / `text` satisfies a condition, polling **inside the target app**:

- `visible` (default) — element exists and is on screen
- `exists` — in the tree even if not visible
- `enabled` — visible and `IsEnabled=true`
- `hidden` — no matching visible element (a spinner cleared, a dialog closed)

Returns `matched`, `waited_ms`, and the matched handle/type. Drop it between a trigger and the next action:

```
wait-for --pid 1234 --type Button --text Save --condition enabled --timeout 8000
```

### Concurrent IPC connections

Making the wait useful surfaced a limitation: the named-pipe server handled **one client at a time**, so a long `wpf_wait_for` poll would have blocked every other command. The server now accepts connections concurrently (each on its own task); requests still serialize on the WPF UI Dispatcher, so tree access stays safe. A nice consequence: the state change a wait is waiting for can arrive over a *second* connection — which is exactly how it was verified live (waiting for Submit to enable, then typing the username over a separate connection; the wait returned the instant the button lit up).

### Why polling doesn't freeze the UI

`wpf_wait_for` is handled before the blocking `Dispatcher.Invoke` path: it runs a short Invoke per check and `Task.Delay`s between checks on a background thread, so the UI thread stays free and the awaited condition can actually change. The timeout is clamped to 25s to stay under the 30s IPC request timeout.

---

## v0.7.1 — Binding-error capture actually works, plus NuGet/MCP-registry distribution (2026-07-12)

### Fixed: binding errors were never captured

`wpf_get_binding_errors` always came back empty — even with a broken binding visibly failing on screen. Two independent bugs stacked on top of each other:

1. The Inspector attaches a `TraceListener` to `PresentationTraceSources.DataBindingSource` at runtime, but **never called `PresentationTraceSources.Refresh()`**. Without it, WPF ignores listener and switch-level changes made after startup (unless tracing was already enabled via app.config or the registry), so the listener sat there and never received a single event.
2. Even when errors *were* captured, the server parsed the Inspector's `{"errors":[...],"count":N}` payload as a bare JSON array, yielding `errors: []` alongside a non-zero `count`.

The project's headline diagnostic capability — "let the AI read the app's binding errors" — was inert until now. Verified live: a deliberately misspelled binding path in the sample app is now reported with element, property, path and the full WPF message.

### Added: install from NuGet

- **`dotnet tool install -g WpfVisualTreeMcp`** → the `wpfinspect` command.
- The package is also an **MCP server package** (`PackageType: McpServer`), runnable via **`dnx WpfVisualTreeMcp`** and discoverable in MCP client catalogs. The native bootstrappers and the x86 injector helper are bundled inside the package, so auto-injection works out of the box.
- `.mcp/server.json` manifest + a manual **Publish to MCP Registry** workflow (GitHub OIDC) for registry.modelcontextprotocol.io.
- The release workflow now packs the NuGet package, pushes it to nuget.org (with the `NUGET_API_KEY` secret) and attaches the `.nupkg` to the release.

### Also

- README rewritten around the actual value: demo GIF, 60-second quickstart, and a comparison against Snoop, FlaUI/WinAppDriver and generic computer-use agents.
- CI fixes (both failures pre-dated this release): Linux `code-quality` job restores with `EnableWindowsTargeting`; the release workflow publishes the multi-targeted Inspector with an explicit `--framework`.

---

## v0.7.0 — Element queries, item selection, screen capture + critical IPC fix (2026-07-12)

This release makes the tool surface actually usable by an AI agent end-to-end: a **critical serialization bug** meant most tool parameters never reached the Inspector, and on top of the fix comes a **query engine** for finding controls, **item selection** for dropdowns/lists, **right/double click**, and **screen-mode screenshots** that can see popups and menus.

### Fixed — the big one

`IpcSerializer.SerializeRequest` serialized the request payload by its **declared** base type (`IpcRequest`), so System.Text.Json dropped every derived-class property. Everything a request carried — search filters, element handles, text, key combos — was silently discarded:

- `find` ignored all filters and always returned the first 50 elements of the tree (~20 KB of noise per call);
- **every handle-based operation failed**: `props`, `layout`, `bindings`, `click`, `set-text`, `send-keys`, ... all returned "ElementHandle required";
- only parameterless calls (window screenshot, default tree) appeared to work — which is why the pipeline looked alive in demos.

One-line fix (`data = (object)request`) + round-trip regression tests so it can't come back.

### What's new

#### Element query engine — find the control in one shot

`wpf_find_elements` / `find` now takes AND-combinable filters: **`text`** (visible text content — button captions even when nested in templates, TextBlock text, window titles, `AutomationProperties.Name`, tooltips), **`property_filter`** (property → value substring; in the contract since v0.1, implemented now), **`visible_only`** (prunes collapsed/hidden subtrees). Results include `text`, `automationId`, `isVisible`, `isEnabled` and `screenBounds` (physical pixels), so the agent picks the right control without extra property dumps — e.g. `find --type Button --text Save --visible-only`.

#### `wpf_select_item` / `select-item` — 21st tool

Select an item in a ComboBox / ListBox / ListView / TabControl by **visible text or index**. Drives `Items` + `SelectedIndex` rather than clicking containers, so it works with **virtualized items** (which don't exist in the visual tree until their popup opens) and raises proper selection events. Resolves item text through containers, `DisplayMemberPath`, overridden `ToString()`, realized `ItemTemplate` content and common display properties; a failed match lists the available items in the error so the agent can self-correct.

#### Right / double click

`wpf_click_element` / `click` accepts `click_type` = `single` (default) | `double` | `right`. Double and right clicks are OS-level physical clicks; right-click opens context menus. Physical clicks (and keyboard focus) now **auto-scroll the element into view** first.

#### Screen-mode screenshots — see the popups

`wpf_capture_screenshot(mode='screen')` / `screenshot --mode screen` captures the actual on-screen pixels via GDI BitBlt with `CAPTUREBLT`. Unlike the default `render` mode (RenderTargetBitmap, re-renders off-screen), it **includes open Popups, ComboBox dropdowns, context menus and tooltips** — completing the "right-click → screenshot → read the menu" loop. `render` remains the default and still works when the window is covered.

#### Set-text read-back

`wpf_set_text` / `set-text` responses now report the value read back after the write (`value now: '...'`) and flag when the control coerced or validated the input. Passwords report length only.

### Robustness

- Element handle cache switched to **weak references** — the Inspector no longer pins removed UI subtrees in memory, and handle resolution is O(1).
- **Consistent, actionable stale-handle errors** across all handle-based operations: what expired, why, and how to recover.

### Tool count

**21** (17 read-only + 4 state-changing: `wpf_click_element`, `wpf_select_item`, `wpf_set_text`, `wpf_send_keys`).

### Testing

57 unit tests pass (9 new). Full pipeline verified live against the sample app: text query → select-item → right click → screen-mode screenshot → set-text with read-back.

### Asset

`WpfVisualTreeMcp-v0.7.0-win-x64.zip` — framework-dependent publish of the Server/CLI (.NET 8 Desktop Runtime required), including the native bootstrappers and the x86 InjectorHelper under `native/`.

---

## v0.6.0 — Cross-architecture auto-injection (2026-05-24)

This release removes the **v0.5.0 known limitation**: a 64-bit server can now auto-inject into 32-bit WPF targets (and the converse, with the symmetric helper). The mechanism is a new tiny architecture-matching helper exe that performs the `CreateRemoteThread` + `LoadLibraryW` step in matching bitness, spawned on demand by the server.

### What's new

#### `WpfInjectorHelper.exe` — architecture-matching injection helper

A 64-bit process cannot do the standard remote-LoadLibrary injection into a 32-bit target — `GetProcAddress(kernel32, "LoadLibraryW")` resolves to the **64-bit** kernel32 base, which is invalid in the target's 32-bit address space, so the remote thread runs at a bogus address and returns 0 (no bootstrapper log, silent "Injection failed"). This was documented as a known limitation in v0.5.0.

The fix:

- New project [`WpfVisualTreeMcp.InjectorHelper`](src/WpfVisualTreeMcp.InjectorHelper/) — a small **32-bit .NET 8 console app** that takes `--pid <id> --dll <path>` and calls `ProcessInjector.InjectBootstrapper`. Framework-dependent (~150 KB unpacked), uses the 32-bit .NET 8 runtime on the target machine.
- `ProcessInjector.InjectIntoProcess` now compares `Environment.Is64BitProcess` against the target's bitness. **Same bitness:** in-process injection, as before. **Different bitness:** spawns the matching helper exe, waits for it (15 s timeout), reports success via the helper's exit code.
- `WpfVisualTreeMcp.Injector` is now multi-targeted (`net48;net8.0`) so the helper can reference it cleanly.
- The Server publish layout grows by five files under `native/x86/`: the helper exe + its `.dll` + two `.json` config files + a copy of `WpfVisualTreeMcp.Injector.dll`. Release zip is ~140 KB larger as a result.

### What this enables

- **Inject into 32-bit WPF apps from the default 64-bit server.** No more "Injection failed" with no explanation against `<PlatformTarget>x86</PlatformTarget>` apps like OCONWPF, MFC port targets, or apps that need 32-bit OLE/Jet/ODBC drivers.
- All 20 MCP tools / CLI commands work against both architectures from a single deployment.

### Caveats

- The x86 helper requires the **32-bit .NET 8 Desktop Runtime** on the machine. If you already run any 32-bit .NET 8 WPF app, it's already installed. Otherwise grab it from <https://dotnet.microsoft.com/download/dotnet/8.0>.
- The **reverse direction** (an x86 server reaching x64 targets) needs a symmetric x64 helper. Not shipped yet because the default Server publish is `AnyCPU`/x64. When encountered, `ProcessInjector` raises a clear error pointing at the missing helper.
- The helper inherits one current `ProcessInjector` limitation: the LoadLibrary success check via `GetExitCodeThread` returns a 32-bit value, which truncates the HMODULE on x64. In practice this only mis-reports when the module's low 32 bits are exactly zero (vanishingly unlikely). A more rigorous check could verify by re-enumerating the target's loaded modules.

### Tool count

Unchanged at 20. This release adds capability rather than commands.

### Testing

All 48 existing unit tests continue to pass. Solution builds clean across `net48`, `net8.0`, and `net8.0-windows` target frameworks. The publish output now includes 5 new files under `native/x86/`; the existing files in `native/x64/`, `native/x86/`, and `native/x64/coreclr/`, `native/x86/coreclr/` are unchanged.

### Asset

`WpfVisualTreeMcp-v0.6.0-win-x64.zip` — framework-dependent publish of the Server/CLI (.NET 8 Desktop Runtime required), now including the x86 InjectorHelper alongside the bootstrappers under `native/`.

---

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
