---
name: wpfvisualtreemcp
description: Inspect and interact with running Windows WPF applications using the WPF Visual Tree MCP. Use for visual-tree inspection, binding errors, disabled controls, layout and style diagnosis, screenshots, or requested UI interaction. Not for WinUI, Avalonia, or static XAML review without a running WPF app.
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

For a requested temporary property experiment, take a `wpf_snapshot`, apply `wpf_set_property`, and compare with `wpf_diff`. Restore experimental values with `wpf_revert_property` unless the user wants them retained. Runtime edits do not update source XAML.

Report the observed control, relevant property or binding, and evidence for the diagnosis. Distinguish a verified cause from an inference and state whether an interaction or experiment changed application state.

## Connection failures

This plugin runs locally on Windows and starts the latest stable NuGet package through `dotnet dnx`. It requires .NET 10 SDK version 10.0.300 or newer within .NET 10, and the .NET 8 runtime. Each MCP start checks nuget.org for the latest stable package, bypassing HTTP metadata caches; downloaded packages are reused. Restart the MCP server or Codex to pick up a new release. Attaching to x86 .NET apps also requires the x86 .NET 8 runtime. If the MCP tools are unavailable, report the startup failure and check those prerequisites before diagnosing the target app.
