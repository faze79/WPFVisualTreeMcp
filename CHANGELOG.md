# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.17.0] - 2026-10-22

### Fixed

- Preserve WPF `NaN`/`Infinity`, numeric, boolean and null property values; use
  culture-independent JSON and retain binding/element metadata. Invalid payloads
  now return actionable errors instead of an apparently successful empty result.
- Accept visible HWND children of `HwndHost` for mouse/touch input while retaining
  overlay and process checks. Native boundaries are explicitly marked in tree/find results.
- Screenshot `auto` mode (new default) uses screen capture for native hosted content
  and reports the selected mode. Explicit render/full-content requests reject native
  content with recovery guidance instead of returning an incomplete black image.
- Expose input-validation and Inspector errors to MCP clients; document report limits.
- Read composite/custom headers and inline text in backstage/tab searches and snapshots. Include
  dependency-property `ownerType` to distinguish same-name attached properties.
- Delegate foreground permission before physical input, replay and screen capture;
  verify the target is foreground and not minimized before capturing desktop pixels.

### Added

- `wpf_move_mouse` / CLI `move-mouse` for hover and `wpf_scroll` / CLI `scroll` for
  vertical/horizontal wheel input. Paired local `x`/`y` coordinates on click imply
  physical input. All new inputs support stable-selector recording and replay.
- Regression coverage for real WPF defaults under en-US/it-IT, native HWND input
  boundaries, mixed JSON value parsing and fresh-process pointer workflow export.
- Full and compact Windows x64 MCPB bundles are built and validated by the release
  workflow alongside the portable archive, NuGet package and Codex companion plugin.

### Upgrade notes

- Restart the MCP client and previously inspected applications after updating so
  the server and injected Inspector both reload. Coordinate clicks and pointer
  replay detect older Inspectors and explain how to reload them.
- Screenshot capture now defaults to `auto`; choose `render` explicitly for
  off-screen WPF-only capture or `screen` for visible popups and menus.

## [0.16.0] - 2026-10-19

### Added

- Workflow recording, stable-selector replay, explicit property assertions and
  runnable xUnit export (`wpf_record`, `wpf_get_recording`, `wpf_assert`,
  `wpf_replay`, `wpf_export_test`) with matching CLI commands. Includes mouse
  drag and native touch parameters, explicit waits, bounded storage and deadlines,
  and fresh-process round-trip tests. Resolves the v1 scope of issue #13.
- `wpf_report` / CLI `report`: bounded aggregate diagnostics for current binding
  and validation errors, UI state counts and recent process-wide binding traces.
  Reports identify their scope and incomplete coverage explicitly.
- .NET Framework 4.7.2 targets for Inspector and shared contracts, adapted from
  @miloszkukla's PR #22. Injection packages use the 4.7.2-compatible payload for
  .NET Framework targets; separate 4.8 and .NET 8 builds remain available.
- Dependency-clean x86/x64 injection checks now cover applications targeting
  both .NET Framework 4.7.2 and 4.8. Gesture test runners also accept net472 targets.
- Full-content render screenshots (`full_content=true`, CLI `--full-content`),
  adapted from @miloszkukla's PR #22: direct capture of non-virtualized content and
  bounded paging/stitching of virtualized lists, with scroll restoration and
  cooperative execution deadlines. Full-content capture shares the interaction
  gate with mouse/touch and other mutations.

### Fixed

- Replay lets WPF process queued input, bindings and layout between steps;
  recorded async waits remain explicit. `wait-for --root` now respects its root.
- Named-pipe request decoding preserves UTF-8 characters split across reads and
  rejects requests larger than the configured input bound.
- CLI tool updates download the exact official NuGet package before scheduling
  installation, with four bounded attempts for propagation delays and temporary
  HTTP failures. Installation uses that staged package instead of resolving it
  again through potentially stale NuGet metadata. No installation changes occur
  when download preparation fails.
- Update failures only recommend closing applications when a file-sharing/lock
  error was detected; unrelated failures retain their actual diagnosis.

### Validation

- CI installs and upgrades the Codex plugin in an isolated profile and verifies
  its complete skill and resolved MCP configuration. Release builds run the same
  check against the actual plugin ZIP before publishing.

## [0.15.0] - 2026-10-19

### Added

- Repository Codex plugin marketplace, portable MCP launch configuration and
  companion WPF inspection skill, contributed by @gaoshang212 in #23. Verified
  plugin installation, MCP startup, all 30 tools and x64/x86 Inspector injection.
- Companion skill guidance for mouse dragging, touch gestures and the distinction
  between CLI self-update, `dnx` server refresh and plugin/skill updates.
- Separate `WpfVisualTreeMcp-v0.15.0-codex-plugin.zip` release asset containing the
  local marketplace, MCP configuration and skill, including their dot-directories.
  The existing Windows server archive and NuGet package remain available.

### Installation and updates

- Install the Codex plugin from Git: `codex plugin marketplace add https://github.com/faze79/WPFVisualTreeMcp.git`,
  then `codex plugin add wpfvisualtreemcp@WPFVisualTreeMcp` and start a new session.
- For the plugin ZIP, extract it and run `codex plugin marketplace add .` from the
  extracted root, then the same `plugin add` command. Requires Windows, a Codex
  client with plugin support, .NET SDK 10.0.300+ within .NET 10, the .NET 8 runtime
  and internet access. The plugin resolves the latest stable server from NuGet.
- Existing Git marketplace users: run `codex plugin marketplace upgrade WPFVisualTreeMcp`,
  then reinstall with `codex plugin add wpfvisualtreemcp@WPFVisualTreeMcp` and start
  a new session to refresh the skill and configuration.
- Existing standalone CLI users: `wpfinspect update`, then `wpfinspect update --status`.
  This updates that CLI installation; plugin/skill files use the steps above.
- Includes all 30 MCP tools, mouse dragging, touch gestures and CLI self-update
  from previous releases. No new MCP commands or server behavior changes.

Thanks to @gaoshang212 for [PR #23](https://github.com/faze79/WPFVisualTreeMcp/pull/23).

## [0.14.0] - 2026-10-19

### Added

- CLI `update` installs the latest stable version of the invoked global/custom-path
  .NET tool or portable server folder. No process ID or MCP connection is required.
- `update --check` reports installed/latest versions and installation type;
  `update --status` reports the detached worker's result and diagnostic log.
- Windows updater waits for the launching CLI to exit, uses official NuGet/GitHub
  sources, verifies portable release SHA-256 digests, preserves unrelated files,
  and restores backed-up portable files on replacement failure. Occupied files
  fail without terminating other applications. Source builds and shared package
  caches remain managed through their build/package workflows.
- Installation/update documentation, including the one-time upgrade needed by
  versions before v0.14.0 and restart requirements for loaded Inspectors.

## [0.13.0] - 2026-10-18

### Added

- `wpf_drag` and CLI `drag` send a gradual physical mouse drag between visual-tree
  elements or by a relative displacement, with local coordinate pairs, configurable
  duration and left/right/middle button support. Results report input delivery and
  screen coordinates; applications remain responsible for accepting a drop.
- Timed input runs outside the UI Dispatcher, with checked native input, foreground
  and endpoint validation, exclusive interaction execution, and release cleanup on
  failures/cancellation. Read-only inspection remains available during a drag.
- Sample drag window and an interactive desktop test script covering sliders,
  splitters, WPF/OLE drag-and-drop, mouse capture, concurrent inspection and CLI input.

- `wpf_touch_gesture` and CLI `touch`: native tap, long press, swipe, two-finger pinch
  and rotation. Paths use local WPF DIPs, are checked before injection, and retain
  stable contact identities. Contact cancellation runs on failure or shutdown.
- Touch sample with actual WPF event/manipulation telemetry and tests for expansion,
  contraction, signed rotation, concurrent inspection, mouse/touch exclusion and
  rejection of invalid paths. The sample opts into WPF's WM_POINTER stack at startup;
  the Inspector preserves the inspected application's existing input configuration.
- Documented compatibility, examples and verification steps for all 30 MCP tools/CLI commands.

### Validation

- 144 automated tests, including input failure/cancellation cleanup and native structure layout.
- Real mouse and touch tests on .NET Framework 4.8 x64/x86 and .NET 8 x64.
- Touch success reports native input delivery; gesture recognition requires application
  support. Legacy WPF input stacks may need WM_POINTER enabled when the application starts.

## [0.12.1] - 2026-10-17

### Fixed

- Build and package the x86/x64 native bootstrapper in CI and releases. Publish
  and NuGet pack now fail when required injection payloads are missing (#14).
- Collect managed injection files after builds complete, including all net48
  dependencies, and resolve those dependencies in injected applications without
  requiring changes to the host's binding redirects (#14).
- Anchor native output paths to the bootstrapper project directory for consistent
  standalone and solution builds (#15).
- Report `success: false` and `inspector_ready: false` when attach cannot reach
  the Inspector. Subsequent MCP errors retain the PID and original cause; CLI
  attach returns exit code 1. Existing inspectors are detected through their pipe (#16).
- Replace the stale injector README with the implementation, payload layout,
  runtime/architecture requirements, build instructions, and troubleshooting (#17).

### Tests

- Published-package smoke tests exercise real injection into isolated x64 and x86
  .NET Framework WPF processes without a self-hosted Inspector. They also cover
  missing bootstrapper diagnostics, CLI exit codes, and reconnecting.
- Verify native files and the helper in the NuGet archive before release.

Thanks to @gentledepp for the detailed reports and reproduction steps.

## [0.12.0] - 2026-07-24

### Added

- **`wpf_explain_triggers` / `explain-triggers`** (28th tool) — evaluate an element's triggers
  and attribute a value to its source. Lists the Style triggers (walking `BasedOn`) **and the
  ControlTemplate triggers** (where most visual-state triggers live — `IsMouseOver`,
  `IsEnabled`, `IsKeyboardFocused`, ...), each evaluated against the current state: the
  condition (`property=expected`), the property's **current value**, whether it matches,
  whether the trigger is **active**, and the **setters** it applies. Answers "why doesn't this
  trigger fire?" — its condition property currently holds a different value. With
  `property_name`, also **attributes** that property's effective value to the exact source: a
  style setter (which style, which setter) or an active trigger (which condition), or
  local/inherited/default. Handles `Trigger`, `MultiTrigger`, `DataTrigger` (best-effort
  binding evaluation) and `EventTrigger`. Read-only.

### Notes

- The tool you reach for when you'd otherwise crack open the visual tree by hand to see why a
  `Style`/`ControlTemplate` trigger isn't reacting, or where a value/binding really comes from.
  Verified live: disabling a TextBox surfaces its template `IsEnabled=False` trigger as
  `active: true` applying `Opacity=0.56`; a custom button's `Foreground` is attributed to the
  exact style setter that set it.

## [0.11.0] - 2026-07-24

### Added

- **`wpf_evaluate_binding` / `evaluate-binding`** (27th tool) — explain *why* a property has
  its current value. Reports the value source (`Local`, `Style`, `Binding`, `Inherited`,
  `Default`, ...), the effective value, and whether it's an expression/animated/coerced.
  When the value comes from a binding, it resolves the binding path **hop-by-hop** against
  the source (`DataContext` / `ElementName` / `RelativeSource` / `Source`), reporting each
  segment's value and type and pinpointing exactly where a broken binding fails — a null
  intermediate, or a property that doesn't exist on the DataContext type. Read-only.

### Notes

- The go-to answer for "why is X empty/wrong/disabled?". Complements `wpf_get_bindings`
  (which lists bindings) by actually *evaluating* one. Verified live against the sample app:
  the intentionally misspelled `Statsu` binding reports `status: PathError`,
  `brokenAt: "Statsu"`, `"property 'Statsu' not found on type 'MainViewModel'"`; a working
  `UserName` binding resolves hop-by-hop to its value; a non-bound property reports its
  value source with no binding.

## [0.10.0] - 2026-07-15

### Added

- **`wpf_snapshot` / `snapshot`** (25th tool) — capture an element subtree's curated state
  (visibility, IsEnabled, Opacity, ActualWidth/Height, Margin/Padding, alignment,
  Background/Foreground/BorderBrush, text) under a label, keyed by element handle.
- **`wpf_diff` / `diff`** (26th tool) — diff two snapshots and report exactly what changed:
  for each element, the properties that changed (`from` → `to`), plus elements added or
  removed. Handles are stable per element, so the diff aligns the same element across the
  two snapshots.

Together they complete the "change → measure → is it effective?" loop: `wpf_snapshot`
(before) → a change (e.g. `wpf_set_property`) → `wpf_snapshot` (after) → `wpf_diff`.
Both tools are read-only.

### Notes

- Verified live: editing the layout root's `Margin` (0,0,0,0 → 40,40,40,40) and
  `Background` shows the direct change **and** the cascade — 13 descendants report reduced
  `ActualWidth`/`ActualHeight` (−80px = 40 per side), i.e. the diff surfaces the knock-on
  effect of a change, not just the edited property.

## [0.9.0] - 2026-07-13

### Added

- **`wpf_set_property` / `set-property`** (23rd tool) — live-edit any dependency property
  on an element at runtime, to test whether a planned UI change is effective without a
  rebuild. The string value is converted to the property's type via its `TypeConverter`
  (covers `Thickness`, `Brush`, `Visibility`, `GridLength`, `Color`, enums, `double`, ...);
  `{null}` sets null. Returns the coerced value read back and what previously held the
  property (`Binding` / `Local` / `Unset`). Read-only properties are rejected with a clear
  error.
- **`wpf_revert_property` / `revert-property`** (24th tool) — undo live edits: the most
  recent one, a filtered one (`element_handle` / `property_name`), or `all=true` for the
  whole experiment. Restores the exact prior state — including a **binding** that was
  overwritten (not just the last value), a local value, or clears to style/inherited/default
  when there was none. Backed by a per-session undo stack.

### Notes

- Setting a data-bound property replaces the binding with a local value (usually what you
  want for a quick test); `wpf_revert_property` puts the binding back. Verified live against
  the sample app: `Margin` and `Background` edits are visible in a screenshot, and
  overwriting a bound `Text` reports `previousSource: "Binding"` with the binding restored on
  revert.
- Pairs with `wpf_capture_screenshot` to see the effect. The planned `wpf_diff` (roadmap)
  will add automatic before/after measurement.

## [0.8.0] - 2026-07-12

### Added

- **`wpf_wait_for` / `wait-for`** (22nd tool) — wait until an element matching
  type/name/text satisfies a condition (`visible`, `exists`, `enabled`, `hidden`),
  polling inside the target app instead of sleep-and-retry from the agent. Returns
  `matched`, `waited_ms` and the matched handle/type. Use after an action that triggers
  async work (a dialog opens, a spinner clears, a button enables) before the next step.
  `timeout_ms` default 10000 (max 25000), `poll_interval_ms` default 250.

### Changed

- **`IpcServer` now accepts concurrent pipe connections.** Previously the pipe served
  one client at a time, so a long-running request (like a `wpf_wait_for` poll) blocked
  every other command. Each client is now handled on its own task; requests still
  serialize on the UI Dispatcher, so tree access stays safe. This also means the state
  change a `wpf_wait_for` is waiting on can arrive over a second connection.

### Notes

- `wpf_wait_for` is dispatched before the blocking `Dispatcher.Invoke` path and polls on
  the background thread (short Invoke per check + `Task.Delay` between), so the UI thread
  stays free and the awaited condition can actually change. Verified live against the
  sample app: waiting for the Submit button to become enabled returns as soon as the
  username is typed over a separate connection.

## [0.7.1] - 2026-07-12

### Fixed

- **Binding errors were never captured.** `PresentationTraceSources.Refresh()` was
  never called, so WPF ignored the listener the Inspector attaches at runtime
  (`DataBindingSource` only honours runtime listener/switch changes after a
  Refresh, unless tracing was enabled via app.config or the registry).
  `wpf_get_binding_errors` therefore always returned an empty list, even with a
  broken binding on screen — the project's headline diagnostic feature was inert.
- **`wpf_get_binding_errors` dropped the errors it did have.** The server parsed the
  Inspector's `{"errors":[...],"count":N}` payload as a bare JSON array, silently
  yielding `errors: []` while reporting a non-zero `count`. The Inspector's own
  `count` was also computed with an array-item counter that overcounts objects.

### Added

- **NuGet packaging**: the server ships as a .NET tool (`dotnet tool install -g
  WpfVisualTreeMcp` → `wpfinspect` command) and as an **MCP server package**
  (`PackageType: McpServer`, runnable via `dnx WpfVisualTreeMcp`), with the native
  bootstrappers and the x86 injector helper bundled in the package.
- `.mcp/server.json` manifest for registry.modelcontextprotocol.io, plus a manual
  **Publish to MCP Registry** workflow (GitHub OIDC, no secrets).
- Release workflow now packs and pushes the NuGet package (needs `NUGET_API_KEY`)
  and attaches the `.nupkg` to the GitHub release.
- README: demo GIF, 60-second quickstart, and a comparison table against Snoop,
  FlaUI/WinAppDriver and generic computer-use agents.

### Changed

- CI fixes (both failures pre-dated this release): the Linux `code-quality` job now
  restores with `EnableWindowsTargeting`, and the release workflow publishes the
  multi-targeted Inspector with an explicit `--framework`.

## [0.7.0] - 2026-07-12

### Fixed

- **CRITICAL: IPC requests silently dropped every parameter.**
  `IpcSerializer.SerializeRequest` wrapped the payload in an anonymous object whose
  `data` member was statically typed as `IpcRequest` — System.Text.Json serializes
  by declared type, so every derived-class property (search filters, element
  handles, text to type, key combos, ...) never reached the Inspector. In practice:
  `find` always returned the first 50 elements unfiltered, and **all handle-based
  operations** (`props`, `layout`, `click`, `set-text`, ...) failed with
  "ElementHandle required". Only parameterless operations (window screenshot,
  default-depth tree) appeared to work. Fixed by serializing the payload by its
  runtime type (`data = (object)request`); guarded by round-trip regression tests
  in `IpcSerializerTests`.

### Added

- **Element query engine** in `wpf_find_elements` / `wpf_find_elements_deep`
  (CLI `find` / `find-deep`). New AND-combinable filters:
  - `text` — visible text content: button captions (even nested in templates),
    TextBlock/TextBox text, window title, `AutomationProperties.Name`, ToolTip;
    case-insensitive substring.
  - `property_filter` — property name → value substring (declared in the IPC
    contract since v0.1 but never implemented; now wired end-to-end).
  - `visible_only` — excludes collapsed/hidden elements, pruning invisible
    subtrees during traversal.
  - Results now include `text`, `automationId`, `isVisible`, `isEnabled` and
    `screenBounds` (physical pixels, same space as the OS mouse), so an agent can
    pick the right control without follow-up property dumps.
- **`wpf_select_item` / `select-item`** (21st tool) — select an item in any
  `Selector` control (ComboBox, ListBox, ListView, TabControl) by visible text or
  zero-based index. Drives `Items`/`SelectedIndex`, so it works with virtualized
  items that have no visual-tree container yet and raises proper selection events.
  Understands containers, `DisplayMemberPath`, overridden `ToString()`, realized
  `ItemTemplate` content, and common display properties; failed matches list the
  available items in the error.
- **`click_type` on `wpf_click_element` / `click`** — `double` and `right`
  (physical) clicks in addition to `single`; right-click opens context menus.
- **Screen-mode screenshots** — `wpf_capture_screenshot(mode='screen')` /
  `screenshot --mode screen` captures the actual on-screen pixels via GDI BitBlt
  (`CAPTUREBLT`), including open Popups, ComboBox dropdowns, context menus and
  tooltips that `RenderTargetBitmap` cannot see. Default `render` mode unchanged.
- **Scroll-into-view** before physical clicks and keyboard focus
  (`BringIntoView` + layout pass) so off-viewport elements get correct
  screen coordinates.
- **Read-back verification in `wpf_set_text` / `set-text`** — the response reports
  the element's value after the write (`value now: '...'`) and flags coercion or
  validation mismatches. Passwords report length only.

### Changed

- **Element handle cache uses weak references** (`ConditionalWeakTable` + reverse
  lookup) — the Inspector no longer keeps removed UI subtrees alive, and handle
  resolution is O(1) instead of a linear scan.
- **Stale-handle errors are consistent and actionable** across all 15 handle-based
  operations: they name the handle, explain why it expired, and say how to recover
  (re-run `wpf_find_elements`). "Not a UIElement" is now reported distinctly from
  "handle not found".
- `wpf_find_elements_deep` accepts `text` as its bounding criterion (previously
  required `type_name` or `element_name`).

### Testing

57 unit tests pass (9 new: IPC envelope round-trip regressions, query
pass-through, new-criteria validation).

## [0.6.0] - 2026-05-24

### Added

- **Cross-architecture auto-injection.** A 64-bit server can now inject the
  Inspector into 32-bit WPF targets (and vice-versa), removing the v0.5.0
  known limitation. Implementation: a new tiny architecture-matching helper
  exe — [`WpfInjectorHelper`](src/WpfVisualTreeMcp.InjectorHelper/) — that
  performs the `CreateRemoteThread` + `LoadLibraryW` step in matching
  bitness. The server detects the target's bitness; when it matches the
  server's own, in-process injection is used as before; when it differs,
  the helper is spawned with `--pid` and `--dll` and its exit code reports
  success.
- **`ProcessInjector.InjectBootstrapper(int pid, string dllPath)`** public
  method — the helper's entry point; performs only the low-level remote
  LoadLibrary step with no architecture detection.

### Changed

- `WpfVisualTreeMcp.Injector` is now **multi-targeted** (`net48;net8.0`) so
  the new x86 .NET 8 helper can reference it without a cross-TFM warning.
  The existing net48 build path is unchanged.
- The Server publish now bundles five additional files under
  `native/x86/`: `WpfInjectorHelper.exe` (32-bit apphost),
  `WpfInjectorHelper.dll`, `WpfInjectorHelper.runtimeconfig.json`,
  `WpfInjectorHelper.deps.json`, and `WpfVisualTreeMcp.Injector.dll`.
  The release zip grows by ~140 KB.

### Removed

- **v0.5.0 known limitation eliminated.** Auto-injection no longer requires
  injector and target to share bitness — both x86 and x64 WPF apps can be
  driven from the same 64-bit server.

### Notes

- The x86 helper is **framework-dependent** — running it requires the
  32-bit .NET 8 Desktop Runtime on the machine. On a machine that already
  runs 32-bit .NET 8 WPF apps (e.g. OCONWPF) the runtime is already
  present. Otherwise install it from
  https://dotnet.microsoft.com/download/dotnet/8.0.
- The reverse direction (an x86 server reaching x64 targets) would need a
  symmetric x64 helper; not shipped yet because the default deployment is
  x64. The error message from `ProcessInjector` makes that explicit when
  encountered.

### Testing

All 48 existing unit tests continue to pass.

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
