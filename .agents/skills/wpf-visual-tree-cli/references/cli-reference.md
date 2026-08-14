# WpfVisualTreeMcp CLI reference

The command map in this reference matches the current WpfVisualTreeMcp repository. Treat the installed command's `help` output as authoritative; v0.12.0 does not contain every current option. Packaging and target-framework notes describe the current repository state; verify which later release first contains them.

## Installation and mode selection

```powershell
dotnet tool install --global WpfVisualTreeMcp
wpfinspect help
```

For an existing global-tool installation, inspect version drift without changing it:

```powershell
dotnet tool list --global | Select-String '^wpfvisualtreemcp\s'
dotnet tool search WpfVisualTreeMcp --detail
```

When a newer stable release specifically addresses the observed limitation and the user approves the change:

```powershell
dotnet tool update --global WpfVisualTreeMcp
wpfinspect help
```

This update command does not replace a manually extracted `WpfVisualTreeMcp.Server.exe`. Update that installation from GitHub Releases instead.

The published v0.12.0 package and ZIP omit required Auto-injection files. Reinstalling v0.12.0 does not fix that defect. Prefer a later release whose notes include the native-bootstrapper and .NET Framework dependency-closure repair, or use a verified current source build or self-hosted mode.

For an Auto-injection source build, build both native bootstrapper platforms with Visual Studio MSBuild before publishing the server; `dotnet build` alone skips the `.vcxproj`:

```powershell
msbuild src/WpfVisualTreeMcp.Bootstrapper/WpfVisualTreeMcp.Bootstrapper.vcxproj /m /p:Configuration=Release /p:Platform=x64
msbuild src/WpfVisualTreeMcp.Bootstrapper/WpfVisualTreeMcp.Bootstrapper.vcxproj /m /p:Configuration=Release /p:Platform=Win32
dotnet publish src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj --configuration Release --output ./publish
```

The release ZIP exposes the same CLI as `WpfVisualTreeMcp.Server.exe`. Starting the executable with no arguments runs the MCP stdio server; a recognized subcommand runs one CLI operation.

Every command except `list` accepts `--pid <id>` or `--process <name>`. Prefer `--pid`. Global options are `--compact`, `--verbose`, and `--help`/`-h`.

## Command map

### Discover and attach

```text
list
attach --pid|--process [--auto-inject]
```

`attach` without `--auto-inject` connects only when the Inspector is already hosted or was injected earlier. `--auto-inject` loads it once into the current target process.

### Find and inspect

```text
tree             --pid [--root H] [--depth N]
props            --pid --handle H
find             --pid [--type T] [--name N] [--text S] [--visible-only] [--root H] [--max N] [--filter JSON]
find-deep        --pid (--type T | --name N | --text S) [--visible-only] [--root H] [--filter JSON]
bindings         --pid --handle H
binding-errors   --pid
data-context     --pid --handle H
resources        --pid [--scope application|element] [--handle H]
styles           --pid --handle H
layout           --pid --handle H
evaluate-binding --pid --handle H --property P
explain-triggers --pid --handle H [--property P]
```

`find` limits results to 50 by default. Filters combine with AND. `find-deep` requires at least one of type, name, or text so the search is bounded semantically.

### Observe and compare

```text
watch-property --pid --handle H --property P
wait-for       --pid (--type T | --name N | --text S) [--condition visible|exists|enabled|hidden] [--timeout MS] [--poll MS]
snapshot       --pid [--handle H] [--label L] [--depth N]
diff           --pid --before L1 --after L2
```

`watch-property` registers the watch, but the one-shot CLI cannot stream change events; re-read properties. `wait-for` defaults to a 10-second timeout and 250 ms poll interval, with a 25-second maximum timeout.

### Capture and export

```text
highlight  --pid --handle H [--duration MS]
export     --pid [--handle H] [--format json|xaml] [--out FILE]
screenshot --pid [--handle H] [--out FILE] [--max-width N] [--max-height N] [--mode render|screen] [--full-content]
```

`render` is the screenshot default and works if the window is covered, but it omits popup windows. `screen` captures visible popups, dropdowns, context menus, and tooltips but requires an unobstructed visible window.

Add `--full-content` in render mode to capture all content in a `ScrollViewer`. Pass the most precise element handle because a control template or subtree can contain more than one ScrollViewer. Ordinary content is rendered directly; virtualized content is paged and stitched, with the original scroll offsets restored. Increase `--max-height` when the default limit would make a long image unreadably small. Full-content capture cannot be combined with `--mode screen`, logically scrolling virtualized controls with horizontal overflow are unsupported, and captures whose retained frames plus output exceed the built-in 67,108,864-pixel budget fail safely.

### Change application state

```text
clear-binding-errors --pid
click          --pid --handle H [--physical] [--click-type single|double|right]
select-item    --pid --handle H (--item-text S | --index N)
set-text       --pid --handle H --text VALUE [--physical]
send-keys      --pid --keys COMBO [--handle H]
set-property   --pid --handle H --property P --value V
revert-property --pid (--all | [--handle H] [--property P])
```

`set-property` replaces a binding with a local value when applied to a bound dependency property. `revert-property` restores the prior binding, local value, or default.

## Read-only diagnostic example

```powershell
$wpfProcessList = wpfinspect list --compact | ConvertFrom-Json
$targetProcessId = ($wpfProcessList.processes | Where-Object processName -eq 'MyApp' | Select-Object -First 1).processId

$attachResult = wpfinspect attach --pid $targetProcessId --compact | ConvertFrom-Json
if ($attachResult.inspectorStatus -like 'Not loaded*') {
    wpfinspect attach --pid $targetProcessId --auto-inject
}

$buttons = wpfinspect find --pid $targetProcessId --type Button --text Save --visible-only --compact | ConvertFrom-Json
wpfinspect binding-errors --pid $targetProcessId
```

Check for multiple matching processes before using `Select-Object -First 1`; do not silently choose one in an ambiguous live environment.

## Reversible experiment example

```powershell
wpfinspect snapshot --pid $targetProcessId --label before
wpfinspect set-property --pid $targetProcessId --handle elem_00000052 --property Width --value 300
wpfinspect snapshot --pid $targetProcessId --label after
wpfinspect diff --pid $targetProcessId --before before --after after
wpfinspect revert-property --pid $targetProcessId --handle elem_00000052 --property Width
```

## Self-hosted startup shape

Use only with an Inspector build compatible with the target application's framework. Current source provides `net472`, `net48`, and `net8.0-windows` Inspector targets:

```csharp
using System.Diagnostics;
using System.Windows;
using WpfVisualTreeMcp.Inspector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InspectorService.Initialize(Process.GetCurrentProcess().Id);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InspectorService.Instance?.Dispose();
        base.OnExit(e);
    }
}
```

After self-hosting, run `attach` without `--auto-inject`.

## Sources

- Repository: <https://github.com/faze79/WPFVisualTreeMcp>
- NuGet tool: <https://www.nuget.org/packages/WpfVisualTreeMcp>
- CLI implementation for v0.12.0: <https://github.com/faze79/WPFVisualTreeMcp/blob/v0.12.0/src/WpfVisualTreeMcp.Server/Cli/CliRunner.cs>
