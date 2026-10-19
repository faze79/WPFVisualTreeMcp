---
name: wpfvisualtreemcp
description: Inspect and interact with running Windows WPF applications using the WPF Visual Tree MCP. Use for visual-tree inspection, binding/style diagnosis, screenshots, mouse dragging, touch gestures and other requested UI interaction, or updating this plugin's server/skill. Not for WinUI, Avalonia, or static XAML review without a running WPF app.
---

# WPFVisualTreeMcp

Use the plugin's `wpf-visual-tree` MCP tools. Tool names may carry a client-added prefix; discover the available tools and use their actual schemas.

## Connect to the target

Call `wpf_list_processes` to identify the running application, then `wpf_attach` with its process ID. If multiple processes match and the intended target is unclear, ask which one to inspect.

`wpf_attach` defaults to `auto_inject=false`, which expects the application to have initialized the Inspector itself. For an ordinary WPF app, use `auto_inject=true` when attaching for the user's requested inspection. This loads the Inspector into the target process. Do not require source changes merely to inspect an existing app.

## Investigate the symptom

- Locate controls with `wpf_find_elements`, filtering by visible text, name or type. Use the returned handles; do not invent or reuse handles from a previous process instance. Use a bounded `wpf_get_visual_tree` when hierarchy is relevant.
- For binding failures, combine `wpf_get_binding_errors`, `wpf_get_bindings`, and `wpf_get_data_context`. Use `wpf_evaluate_binding` to find the failing path segment or explain a property's value source. An empty error list alone does not prove a binding is correct.
- For unexpected styling or disabled controls, inspect properties and use `wpf_explain_triggers` to check the current style/template conditions.
- For layout, combine `wpf_get_layout_info` with a screenshot. Use screen-mode screenshots for visible popups and dropdowns.

## Interact and verify

For requested UI interaction, prefer `wpf_click_element`, `wpf_select_item`, and `wpf_set_text`. Use physical input when the application needs real OS input; it requires the visible desktop and can move focus. Use `wpf_wait_for_element` to wait for a UI condition.

- For mouse dragging, use `wpf_drag` with `element_handle` and either `target_handle` or nonzero `delta_x`/`delta_y`. Optional paired start/target coordinates are local to their elements; `button` is left/right/middle and `duration_ms` controls the sequence.
- For touch, use `wpf_touch_gesture` with `element_handle` and `gesture`: `tap`, `long_press`, `swipe`, `pinch`, or `rotate`. Swipe needs nonzero deltas; pinch needs `scale` (above 1 spreads, below 1 contracts); rotation uses `angle_degrees` (positive clockwise). Optional `x`/`y` select the center and `radius` sets the initial finger radius for pinch/rotate. Use the tool schema for bounds and duration limits.
- Drag/touch coordinates are local WPF DIPs, not absolute screen pixels. Targets must be visible and unobstructed on an interactive desktop; touch paths must fit inside the element. Mutating interactions are serialized, while inspection remains available during a gesture.
- Success confirms input submission, not a recognized gesture or accepted drop. Verify the resulting value, layout or UI state. WPF applications must handle touch/manipulations; legacy input stacks can ignore injected touch. Do not change the target application's input configuration to force support.

For a requested temporary property experiment, take a `wpf_snapshot`, apply `wpf_set_property`, and compare with `wpf_diff`. Restore experimental values with `wpf_revert_property` unless the user wants them retained. Runtime edits do not update source XAML.

Report the observed control, relevant property or binding, and evidence for the diagnosis. Distinguish a verified cause from an inference and state whether an interaction or experiment changed application state.

## Connection failures

This plugin runs locally on Windows and starts the latest stable NuGet package through `dotnet dnx`. It requires .NET 10 SDK version 10.0.300 or newer within .NET 10, and the .NET 8 runtime. Each MCP start checks nuget.org for the latest stable package, bypassing HTTP metadata caches; downloaded packages are reused. Restart the MCP server or Codex to pick up a new release. Attaching to x86 .NET apps also requires the x86 .NET 8 runtime. If the MCP tools are unavailable, report the startup failure and check those prerequisites before diagnosing the target app.

## Updates and CLI fallback

When an update is requested, identify which installation the user is using:

- **This plugin's server:** restart its MCP server or Codex to resolve the newest stable NuGet package. Do not run self-update against the shared `dnx` cache.
- **Plugin configuration and this skill:** refresh the configured marketplace (`codex plugin marketplace upgrade WPFVisualTreeMcp` for a Git-backed source), reinstall with `codex plugin add wpfvisualtreemcp@WPFVisualTreeMcp`, and start a new session. For a local marketplace, update its source checkout first. Updating the server does not update the skill.
- **Separately installed CLI (v0.14.0+):** `wpfinspect update --check` compares versions; `wpfinspect update` schedules an update; `wpfinspect update --status` reports completion or failure. For a portable copy, invoke that exact `WpfVisualTreeMcp.Server.exe` path. Older global tools first need `dotnet tool update -g WpfVisualTreeMcp`. Source builds use their Git/build workflow.

CLI self-update changes only the invoked installation and is not an MCP tool. On Windows, the worker installs after the CLI exits: `scheduled` does not mean finished. Confirm `updated`; failures return exit code 1 and a log path. Close processes using the same files before retrying, without stopping the user's applications automatically. Restart the MCP client and previously inspected apps after an update to reload their server/Inspector.

For an available CLI fallback, use `help` or `<command> --help`. Mouse and touch equivalents are `drag` and `touch`; CLI rotation uses `--angle`, while the MCP parameter is `angle_degrees`. Never append a CLI subcommand to the plugin's stdio launch configuration.
