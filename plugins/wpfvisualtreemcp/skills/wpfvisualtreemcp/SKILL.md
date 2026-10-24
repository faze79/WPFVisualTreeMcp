---
name: wpfvisualtreemcp
description: Inspect and interact with running Windows WPF applications using the WPF Visual Tree MCP. Use for visual-tree inspection, binding/style diagnosis, diagnostic reports, screenshots, mouse dragging, touch gestures, recording/replaying workflows and exporting xUnit regression tests, or updating this plugin's server/skill. Not for WinUI, Avalonia, or static XAML review without a running WPF app.
---

# WPFVisualTreeMcp

Use the plugin's `wpf-visual-tree` MCP tools. Tool names may carry a client-added prefix; discover the available tools and use their actual schemas.

## Connect to the target

Call `wpf_list_processes` to identify the running application, then `wpf_attach` with its process ID. If multiple processes match and the intended target is unclear, ask which one to inspect.

`wpf_attach` defaults to `auto_inject=false`, which expects the application to have initialized the Inspector itself. For an ordinary WPF app, use `auto_inject=true` when attaching for the user's requested inspection. This loads the Inspector into the target process. Do not require source changes merely to inspect an existing app.

## Investigate the symptom

- Start a broad diagnosis with `wpf_report`; use `root_handle` to narrow it. Check `truncationReasons` and coverage before interpreting results. Disabled/invisible/zero-size counts are observations, while recent binding traces are process-wide history even for a subtree report. Follow current findings with the specific inspection tools below.
- Locate controls with `wpf_find_elements`, filtering by visible text, name or type. Use the returned handles; do not invent or reuse handles from a previous process instance. Use a bounded `wpf_get_visual_tree` when hierarchy is relevant.
- All input tools, assertions and property edits accept `automation_id` directly instead of `element_handle`. IDs match `AutomationProperties.AutomationId` exactly and case-sensitively; missing or duplicate IDs fail before input. Use `root_handle` to scope ID lookup. Drag also accepts `target_automation_id` and independent `target_root_handle`; a destination is exclusive with deltas. CLI equivalents: `--automation-id`, `--root`, `--target-automation-id`, `--target-root`. Find/deep/wait support the same ID filter. Other inspection tools use the handle returned by find. Restart and reattach with the updated Inspector if the capability is unavailable.
- For binding failures, combine `wpf_get_binding_errors`, `wpf_get_bindings`, and `wpf_get_data_context`. Use `wpf_evaluate_binding` to find the failing path segment or explain a property's value source. An empty error list alone does not prove a binding is correct.
- For unexpected styling or disabled controls, inspect properties and use `wpf_explain_triggers` to check the current style/template conditions. `ownerType` distinguishes same-name attached properties; use the control's actual dependency property.
- Backstage pages are included through adorner traversal; find their tabs by type and header text, including composite headers. `visible_only` means WPF visibility, not freedom from overlays. Verify the selected page after clicking and use physical input if a custom control's automation action does not execute its command.
- Native `HwndHost`/`WindowsFormsHost` nodes include `nativeContent`. Their empty WPF children list does not imply empty native content. Use local-coordinate input and screen/auto capture; CAD entity semantics require application accessibility or a dedicated API.
- For layout, combine `wpf_get_layout_info` with a screenshot. Use screen-mode screenshots for visible popups and dropdowns. Default screenshot mode is `auto`: native hosted content uses screen capture with an explicit mode/detail; otherwise render. Explicit render rejects native content instead of silently returning a black area.
- To inspect content beyond a scroll viewport, use `wpf_capture_screenshot(full_content=true, mode="render")` on that scroll control. Virtualized capture temporarily scrolls and restores offsets, can trigger application handlers, and must not overlap other interactions. Memory/deadline limits can reject oversized captures; do not report a failed capture as complete.

## Interact and verify

For requested UI interaction, prefer `wpf_click_element`, `wpf_select_item`, and `wpf_set_text`. Use physical input when the application needs real OS input; it requires the visible desktop and can move focus. Use `wpf_wait_for_element` to wait for a UI condition.

- For hover, use `wpf_move_mouse` with optional paired local `x`/`y` and `hover_ms` (0-5000). For vertical/horizontal wheel input use `wpf_scroll`, signed `delta` in wheel units (120 per notch, nonzero -12000..12000), and optional `horizontal=true`. The app may scroll or zoom; verify the result.
- `wpf_click_element` accepts paired local `x`/`y`, which imply physical input. This supports arbitrary points inside native hosts without substituting touch for mouse. UI Automation Toggle may only change state; use physical input when a custom button needs its click command.
- For mouse dragging, use `wpf_drag` with `element_handle` or `automation_id` and either `target_handle`, `target_automation_id` or nonzero `delta_x`/`delta_y`. Optional paired start/target coordinates are local to their elements; `button` is left/right/middle and `duration_ms` controls the sequence.
- For touch, use `wpf_touch_gesture` with `element_handle` or `automation_id` and `gesture`: `tap`, `long_press`, `swipe`, `pinch`, or `rotate`. Swipe performs a touch pan and needs nonzero deltas; pinch performs a touch zoom and needs `scale` (above 1 spreads, below 1 contracts); rotation uses `angle_degrees` (positive clockwise). Optional `x`/`y` select the center and `radius` sets the initial finger radius for pinch/rotate. Use the tool schema for bounds and duration limits.
- Drag/touch coordinates are local WPF DIPs, not absolute screen pixels. Targets must be visible and unobstructed on an interactive desktop; touch paths must fit inside the element. Mutating interactions are serialized, while inspection remains available during a gesture.
- Success confirms input submission, not a recognized gesture or accepted drop. Verify the resulting value, layout or UI state. WPF applications must handle touch/manipulations; legacy input stacks can ignore injected touch. Do not change the target application's input configuration to force support.

For a requested temporary property experiment, take a `wpf_snapshot`, apply `wpf_set_property`, and compare with `wpf_diff`. Restore experimental values with `wpf_revert_property` unless the user wants them retained. Runtime edits do not update source XAML.

Report the observed control, relevant property or binding, and evidence for the diagnosis. Distinguish a verified cause from an inference and state whether an interaction or experiment changed application state.

## Record a repeatable test

For a requested regression test, start `wpf_record(action="start")`, then perform the workflow sequentially. Existing input tools, including drag/touch, finds, successful waits and assertions are captured. Use explicit `wpf_wait_for_element` calls for asynchronous changes and `wpf_assert` for exact dependency-property values (`expected_value` uses invariant strings; `{null}` means null).

Stop recording, inspect `wpf_get_recording`, then use `wpf_export_test(format="xunit")`. Save its file-name/content map in a new directory. The generated test runs with `WPF_TEST_APP` and `WPFINSPECT_PATH` set to absolute executable paths, then `dotnet test`; it starts a fresh app. Verify that test before reporting the workflow reproducible. CLI equivalents are `record start|stop|status|clear`, `get-recording`, `assert`, `replay --file`, and `export-test --format xunit --out NEW_DIRECTORY`.

Recordings live in the inspected process and vanish when it exits. Export before clearing; input values are plain text. Selectors use exact identities and reject missing/ambiguous controls. Do not replace a failed selector with a stale handle or arbitrary first match. Version 1 is linear, limited to 100 steps/512 KiB; replay has a maximum 120-second deadline and stops at the first failure without undoing earlier actions. Physical input still requires the interactive desktop and app gesture support. Inspect explicit assertions and application state when replaying; input delivery alone does not prove the intended result.

## Connection failures

This plugin runs locally on Windows and starts the latest stable NuGet package through `dotnet dnx`. It requires .NET 10 SDK version 10.0.300 or newer within .NET 10, and the .NET 8 runtime. Each MCP start checks nuget.org for the latest stable package, bypassing HTTP metadata caches; downloaded packages are reused. Restart the MCP server or Codex to pick up a new release. Attaching to x86 .NET apps also requires the x86 .NET 8 runtime. If the MCP tools are unavailable, report the startup failure and check those prerequisites before diagnosing the target app.

## Updates and CLI fallback

When an update is requested, identify which installation the user is using:

- **This plugin's server:** restart its MCP server or Codex to resolve the newest stable NuGet package. Do not run self-update against the shared `dnx` cache.
- **Plugin configuration and this skill:** refresh the configured marketplace (`codex plugin marketplace upgrade WPFVisualTreeMcp` for a Git-backed source), reinstall with `codex plugin add wpfvisualtreemcp@WPFVisualTreeMcp`, and start a new session. For a local marketplace, update its source checkout first. Updating the server does not update the skill.
- **Separately installed CLI (v0.14.0+):** `wpfinspect update --check` compares versions; `wpfinspect update` schedules an update; `wpfinspect update --status` reports completion or failure. For a portable copy, invoke that exact `WpfVisualTreeMcp.Server.exe` path. Older global tools first need `dotnet tool update -g WpfVisualTreeMcp`. Source builds use their Git/build workflow.

CLI self-update changes only the invoked installation and is not an MCP tool. On Windows, the worker installs after the CLI exits: `scheduled` does not mean finished. Confirm `updated`; failures return exit code 1 and a log path. Close processes using the same files before retrying, without stopping the user's applications automatically. Restart the MCP client and previously inspected apps after an update to reload their server/Inspector.

For an available CLI fallback, use `help` or `<command> --help`. Mouse and touch equivalents are `click --x/--y`, `move-mouse`, `scroll`, `drag` and `touch`; CLI rotation uses `--angle`, while the MCP parameter is `angle_degrees`. Never append a CLI subcommand to the plugin's stdio launch configuration.

## Version compatibility

Direct AutomationId targeting and scoped drag destinations require v0.18.0 or newer.
For hand-written C# tests, use the `WpfVisualTreeMcp.Automation` NuGet package:
`WpfApplication.AttachAsync(pid)` and `app.ByAutomationId(id)` expose the same engine.
`WaitForTextAsync("loaded")` compares the exact Text property and throws on timeout;
client-side property waits and predicates are not serialized into recordings.

The hover/wheel commands, click coordinates and screenshot auto mode require v0.17.0 or newer (38 tools). Release 0.16.0 has 36 tools. Discover the live schema before using them. CLI `update` installs the published stable release. Restart and reattach the target after replacing an injected Inspector; an already loaded DLL is not hot-reloaded.
