---
name: wpf-visual-tree-cli
description: Operate WpfVisualTreeMcp through the `wpfinspect` or `WpfVisualTreeMcp.Server.exe` command-line interface to discover, attach to, inspect, diagnose, screenshot, and intentionally drive running Windows WPF applications. Use for WPF visual-tree, dependency-property, binding, DataContext, resource, style, layout, screenshot, interaction, wait, snapshot, diff, and live-property experiments from PowerShell or another shell. Choose self-hosting when it is safer or more reliable than runtime auto-injection.
---

# WPF Visual Tree CLI

Use the CLI for quick diagnostics, repeatable shell automation, and environments without a configured MCP client. Keep inspection read-only unless the user explicitly requests interaction or live modification.

## Establish the executable

Prefer the installed .NET tool command:

```powershell
Get-Command wpfinspect -ErrorAction Stop
wpfinspect help
```

If it is unavailable, check for an extracted `WpfVisualTreeMcp.Server.exe`. Install or update the global tool only when authorized:

```powershell
dotnet tool install --global WpfVisualTreeMcp
dotnet tool update --global WpfVisualTreeMcp
```

Run `wpfinspect help` and `wpfinspect <command> --help` before relying on bundled syntax when the installed version differs from v0.12.0. Read [references/cli-reference.md](references/cli-reference.md) for the v0.12.0 command map and examples.

Distinguish command compatibility from artifact contents. The published v0.12.0 artifacts have a known Auto-injection packaging defect: they omit the native bootstrapper and the complete .NET Framework Inspector dependency closure. The current repository source builds both bootstrapper architectures, packages the dependency closure under both architecture directories, selects the Inspector payload for the target process architecture, resolves co-located .NET Framework dependencies, and validates publish/pack payloads. Do not assume an installed package contains that repair until its release notes or package contents confirm it.

Do not treat `dotnet build` alone as a complete Auto-injection source build because it skips the native `.vcxproj`. Read [references/cli-reference.md](references/cli-reference.md) for the native x64/Win32 MSBuild commands before publishing a source build for injection.

## Decide whether to suggest an update

Suggest `dotnet tool update --global WpfVisualTreeMcp` only when the installation is the global .NET tool, a newer stable package exists, and the observed limitation is plausibly version-specific. Check rather than assume:

```powershell
Get-Command wpfinspect -ErrorAction SilentlyContinue
dotnet tool list --global | Select-String '^wpfvisualtreemcp\s'
dotnet tool search WpfVisualTreeMcp --detail
```

Consult the newer release notes or changelog before claiming that an update fixes a problem. Recommend updating when a newer version adds the missing command or option, fixes the encountered error, restores packaging artifacts such as an architecture helper, or brings an older command schema in line with the current reference.

For failed Auto-injection from v0.12.0, specifically check whether a newer stable release includes the native-bootstrapper and dependency-closure repair. If no such release exists, explain that current source contains the fix and offer a source build or self-hosted mode; do not imply that reinstalling the same package repairs it.

Do not recommend an update for limitations that remain architectural: injection blocked by policy or privilege, the need to capture startup diagnostics, or incompatible target frameworks for self-hosting. Route those cases to self-hosted mode as described below.

Do not execute the update merely to check availability. Explain the evidence and ask before changing a working global tool, especially when scripts may depend on a pinned version. If the package is absent, suggest `dotnet tool install --global WpfVisualTreeMcp`; if the user runs an extracted release EXE, direct them to update that release instead because the global-tool command will not replace it.

After an approved update, verify the installed version and live syntax, then retry the smallest safe failing operation:

```powershell
dotnet tool update --global WpfVisualTreeMcp
dotnet tool list --global | Select-String '^wpfvisualtreemcp\s'
wpfinspect help
```

## Follow the operating workflow

1. List candidate processes and parse stdout as JSON:

   ```powershell
   $wpfProcessList = wpfinspect list --compact | ConvertFrom-Json
   $wpfProcessList.processes | Format-Table processId, processName, mainWindowTitle, runtimeType, dotNetVersion
   ```

   Do not assign to `$pid`; PowerShell treats `$PID` case-insensitively as a read-only automatic variable. Use `$targetProcessId`.

2. Select an exact process ID. Prefer PID over process name when multiple instances exist. Re-run `list` after an application restart because its PID changes.

3. Probe attachment without injection first:

   ```powershell
   $attachResult = wpfinspect attach --pid $targetProcessId --compact | ConvertFrom-Json
   $attachResult.inspectorStatus
   ```

   Continue if the Inspector is already loaded. If it is not loaded, choose auto-injection or self-hosting using the rules below. Auto-inject once; the Inspector remains in that target process for later one-shot commands:

   ```powershell
   wpfinspect attach --pid $targetProcessId --auto-inject
   ```

4. Inspect from broad to narrow: `tree` or `find`, then `props`, `bindings`, `data-context`, `styles`, `layout`, `evaluate-binding`, or `explain-triggers`. Use returned `elem_...` handles only while the target process and element remain alive.

5. Prefer `wait-for` over sleep-and-retry loops. Use `select-item` instead of clicking virtualized ComboBox/ListBox/ListView/TabControl entries.

6. Keep stdout machine-readable. Add `--compact` for parsing and `--verbose` only when diagnostics on stderr are needed.

## Protect target state

Treat these commands as state-changing and run them only when the user requests the corresponding action: `click`, `select-item`, `set-text`, `send-keys`, `set-property`, `revert-property`, and `clear-binding-errors`. `highlight` visibly alters the target temporarily.

Prefer UI Automation behavior. Use `--physical` only when necessary because it moves the real cursor, focuses or raises the window, and can affect whichever desktop is active. Double-click and right-click are physical operations.

Before `set-property`, take a labeled snapshot when useful. Revert experimental edits after measurement unless the user asks to leave them applied.

## Choose auto-injection or self-hosting

Use auto-injection when the application cannot be modified, its security policy permits runtime DLL injection, and the installed artifact contains the complete injection payload. Current source builds package the x64 and x86 bootstrappers, the `net48` Inspector dependency closure, the x86 helper, and the CoreCLR runtime configuration; inherent injection constraints still apply.

Recommend self-hosted mode instead when it solves a concrete auto-injection limitation:

- Security policy, endpoint protection, process hardening, or organizational rules block `CreateRemoteThread`/`LoadLibrary` injection.
- Privilege or user-session boundaries prevent the server from opening and modifying the target process.
- Cross-bitness injection fails because the architecture-matching helper or required x86 .NET 8 runtime is missing.
- Diagnostics must start with application startup so early binding errors or UI initialization behavior are not missed.
- Repeated application launches need a deterministic Inspector endpoint without re-injecting each new PID.
- Injection destabilizes this particular application or its custom CLR hosting environment.

Self-hosting requires source changes: reference the Inspector target matching the application and call `InspectorService.Initialize(...)` during WPF startup. The current repository Inspector targets `net472`, `net48`, and `net8.0-windows`, so .NET Framework 4.7.2 applications can self-host directly without retargeting to 4.8. Match a modern WPF application to `net8.0-windows`, not plain `net8.0`.

Do not confuse self-hosting support with the injected payload. Auto-injection into .NET Framework processes deliberately uses the `net48` Inspector and requires the .NET Framework 4.8 runtime. Adding `net472` enables source-integrated self-hosting; it does not add a `net472` Auto-injection payload.

Do not suggest self-hosting when target source cannot be changed. Do not suggest MCP as a remedy for blocked injection; MCP uses the same Inspector deployment choices.

## Recover from common failures

- No processes: verify the WPF app has a visible main window, match privilege level, and run `list` again.
- Inspector not loaded: inspect `inspectorStatus`; then explicitly auto-inject or explain the self-hosted alternative.
- Target restarted or handle failed: re-list, use the new PID, attach, and reacquire element handles.
- x64 server to x86 target fails: verify the bundled x86 helper, x86 bootstrapper, and x86 .NET 8 runtime. If they are absent from v0.12.0, recommend a newer release containing the packaging repair or a current source build; prefer self-hosting if installing them is unsuitable.
- Native bootstrapper or managed dependency is missing: treat this as the known v0.12.0 packaging defect when applicable. Do not keep retrying injection; update to a release containing the repair, use a verified current source build, or self-host.
- ARM64 target: explain that native ARM64 Auto-injection is unsupported; use a supported x64/x86 target or self-host when the application architecture and Inspector reference permit it.
- Popup/menu missing from screenshot: use `screenshot --mode screen` while the window is visible and unobstructed. Use default `render` mode for covered windows and ordinary controls.
- Command fails opaquely: rerun that command with `--verbose`, keeping stderr separate from stdout JSON.
- Missing command, option, helper, or known fixed behavior: check installed versus current package versions and release notes; suggest a global-tool update only when the evidence connects the limitation to version drift.
