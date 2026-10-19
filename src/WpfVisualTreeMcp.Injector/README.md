# WPF Inspector injection

`ProcessInjector` implements native DLL injection into running Windows WPF applications.
Use `wpf_attach(process_id=1234, auto_inject=true)` or
`wpfinspect attach --pid 1234 --auto-inject`. The MCP parameter defaults to `false`:
without it, attach only connects to an Inspector that is already running.

## Injection sequence

1. The server probes `wpf_inspector_<pid>` to detect an existing Inspector.
2. `ProcessInjector` checks the target process and detects its architecture.
3. For a target matching the server architecture, `CreateRemoteThread` calls
   `LoadLibraryW` with the matching `WpfInspectorBootstrapper.dll`.
4. For a 64-bit server attaching to a 32-bit target, the server starts the
   32-bit `WpfInjectorHelper.exe`, which performs that operation in the correct
   architecture. The shipped helper requires the **x86 .NET 8 runtime**. A
   32-bit server injecting into a 64-bit target is not supported by the shipped
   package; use the 64-bit server instead.
5. The native bootstrapper starts a worker thread outside the loader lock and
   selects the target runtime:
   - **.NET Framework 4.7.2+:** CLR hosting APIs call
     `InspectorService.Initialize(string)` in the `net472` Inspector.
   - **CoreCLR (.NET 8+):** hostfxr loads the `net8.0-windows` Inspector from
     `coreclr/` and invokes `InspectorService.InitializeUnmanaged`.
6. The server waits up to ten seconds for the Inspector pipe. Loading a DLL
   alone does not count as a successful attach.

The server and target must run under an account/integrity level that permits
opening and injecting into the target process. Use the same elevation level.

## Published payload

```text
server/
  WpfVisualTreeMcp.Server.exe
  native/
    x64/
      WpfInspectorBootstrapper.dll
      WpfVisualTreeMcp.Inspector.dll      # net472
      WpfVisualTreeMcp.Shared.dll        # net472
      System.Text.Json.dll              # plus its net472 dependencies
      coreclr/
        WpfVisualTreeMcp.Inspector.dll   # net8.0-windows
        WpfVisualTreeMcp.Shared.dll      # net8.0
        WpfVisualTreeMcp.Inspector.coreclr.runtimeconfig.json
    x86/
      ...                               # same payload, x86 bootstrapper
      WpfInjectorHelper.exe
      WpfInjectorHelper.dll
      WpfInjectorHelper.deps.json
      WpfInjectorHelper.runtimeconfig.json
      WpfVisualTreeMcp.Injector.dll
```

Keep these directories together when copying a release or installing the
NuGet tool. The bootstrapper resolves the managed Inspector relative to its
own location. The injector probes a legacy flat layout, `native/<arch>/`, and
then a development build location. Published packages use `native/<arch>/`.
The net472 Inspector resolves its packaged dependency versions without requiring
binding redirects in the target application's configuration.

## Build and publish

Install Visual Studio Build Tools with **Desktop development with C++**, the
v143 toolset, Windows SDK, .NET Framework 4.7.2 and 4.8 targeting packs, and .NET 8 SDK.
Run from a Visual Studio Developer PowerShell at the repository root:

```powershell
./scripts/Build-NativeBootstrapper.ps1 -Configuration Release
dotnet build WpfVisualTreeMcp.sln -c Release
dotnet publish src/WpfVisualTreeMcp.Server -c Release --no-build -o publish/server
dotnet pack src/WpfVisualTreeMcp.Server -c Release --no-build -o artifacts
```

The script uses full MSBuild to compile the C++ project for Win32 and x64.
`dotnet build`/`dotnet publish` do not compile that project. Native outputs are
always under `src/WpfVisualTreeMcp.Bootstrapper/build/<platform>/<configuration>`,
independently of `SolutionDir`. The Server builds both managed Inspector runtimes
and collects their dependencies after project references finish, including on a
clean build. Publish and pack fail explicitly if a required payload is missing.

For a Debug package, use `-Configuration Debug` / `-c Debug` consistently.
Managed builds without the native toolchain remain usable for self-hosting.

CI builds both native architectures before the managed solution. Both build and
release workflows run `scripts/Test-Injection.ps1` against the published server,
using x64 and x86 sample processes built **without an Inspector reference**.
The test checks missing-DLL errors over MCP and CLI, then performs real injection
and requires a nonempty visual tree.

## Attach results and troubleshooting

`success` and `inspector_ready` are true only after the pipe is available.
On failure they are false; `inspector_status` describes the missing file,
injection error, or initialization timeout. The failed session is retained for
diagnostics, so subsequent inspection tools return the process ID and original
attach failure. Fix the cause and call `wpf_attach` again. CLI attach similarly
returns `success: false`, `inspectorReady: false`, and exit code 1.

- **Bootstrapper missing:** rebuild both native architectures before publish,
  or install a complete release. Running `dotnet publish` alone is insufficient.
- **Helper missing / cannot start:** restore the x86 helper files and install
  the x86 .NET 8 runtime; the x64 runtime alone cannot run the helper.
- **Pipe timeout:** loading the native DLL succeeded but managed initialization
  did not complete. Check `%TEMP%/WpfInspectorBootstrapper.log` and
  `%TEMP%/WpfInspector_Debug.log`, including missing managed dependencies and
  runtime compatibility.

## Optional self-hosting

Applications you control may reference the matching Inspector build and call
`InspectorService.Initialize(Process.GetCurrentProcess().Id)` after WPF startup.
Then attach with `auto_inject=false`. The sample uses this mode by default;
`-p:EnableInspectorSelfHosting=false` removes the reference for injection tests.

## Source files

- `ProcessInjector.cs`: process/architecture checks, bootstrapper lookup,
  remote loading, and cross-architecture helper invocation.
- `../WpfVisualTreeMcp.InjectorHelper/Program.cs`: x86 injection helper.
- `../WpfVisualTreeMcp.Bootstrapper/WpfInspectorBootstrapper.cpp`: runtime hosting.
- `../WpfVisualTreeMcp.Server/InjectionPayload.targets`: payload collection and
  publish/pack validation.
