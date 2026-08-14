# WpfVisualTreeMcp.Injector

This project injects the WPF Inspector into an already-running WPF process. It
is used by both the MCP server and the one-shot CLI when auto-injection is
requested.

## How Auto-Injection Works

1. `ProcessInjector` verifies that the target is managed and detects its
   architecture.
2. It selects the matching x64 or x86 `WpfInspectorBootstrapper.dll`.
3. For a same-bitness target, it uses `OpenProcess`, `VirtualAllocEx`,
   `WriteProcessMemory`, and `CreateRemoteThread` to call `LoadLibraryW` in the
   target. A 64-bit server launches the bundled 32-bit `WpfInjectorHelper.exe`
   for an x86 target.
4. The native bootstrapper detects the loaded CLR. It uses
   `ExecuteInDefaultAppDomain` for .NET Framework and `hostfxr` for CoreCLR.
5. The bootstrapper loads `WpfVisualTreeMcp.Inspector.dll`, which starts the
   named-pipe endpoint used by the server.

Auto-injection does not require source changes to the target application.
The server treats a successful named-pipe connection as the readiness signal;
a loaded Inspector module without a responsive pipe is reported diagnostically
rather than treated as a usable attachment.

## Requirements and Limitations

- **Windows and architecture:** Native bootstrappers are provided for x64 and
  x86. Native ARM64 targets are not supported. Cross-bitness injection from the
  normal 64-bit server into an x86 target requires `WpfInjectorHelper.exe` and
  the x86 .NET 8 runtime.
- **Process access:** The injector needs process-query, remote-thread, and
  virtual-memory access. Integrity-level differences, protected or sandboxed
  processes, endpoint security, and process-hardening policies can deny these
  operations.
- **Runtime compatibility:** .NET Framework targets load the `net48` Inspector
  in the default AppDomain and require the .NET Framework 4.8 runtime. CoreCLR
  targets load the `net8.0-windows` Inspector through `hostfxr` and require a
  compatible Windows Desktop runtime. Custom CLR hosts and incompatible loaded
  runtimes may reject the Inspector.
- **Complete payload:** The architecture directory must contain the matching
  native bootstrapper, Inspector assembly, and managed dependency closure.
  CoreCLR injection also requires the Inspector runtime configuration.
  `LoadLibraryW` or managed initialization fails when the payload is incomplete.
- **Application state:** Injection starts the pipe server immediately, but WPF
  operations require `Application.Current` and a responsive UI dispatcher.
  Requests time out while the UI thread is blocked.
- **Timing and lifetime:** Auto-injection cannot observe binding errors or UI
  initialization that occurred before attachment. A restarted process must be
  injected again. Detaching the external client does not unload the Inspector;
  it remains in the target until process exit.
- **Target stability:** Injection loads native and managed code, starts threads,
  registers listeners, and resolves private dependencies inside the target.
  Applications with conflicting assemblies or unusual hosting arrangements may
  be destabilized.

## Self-Hosted Alternative

When the application source can be changed, self-hosting avoids remote-process
injection and gives the application control over startup and disposal:

```csharp
// In your WPF application's App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // Initialize the inspector
    WpfVisualTreeMcp.Inspector.InspectorService.Initialize(System.Diagnostics.Process.GetCurrentProcess().Id);
}
```

This avoids the complexity of injection and provides the most reliable experience.

Self-hosting is appropriate when policy blocks injection, diagnostics must start
with the application, or injection conflicts with the target's runtime. It
requires a target-framework-compatible Inspector reference.

## Diagnostics

Run the smallest attachment attempt with verbose logging:

```powershell
wpfinspect attach --pid <process-id> --auto-inject --verbose
```

The native bootstrapper writes initialization details to
`%TEMP%\WpfInspectorBootstrapper.log`. When running as an MCP server, logs are
stored under `%LOCALAPPDATA%\WpfVisualTreeMcp\logs`.

## Files

- `ProcessInjector.cs` - Target discovery, architecture selection, and remote
  `LoadLibraryW` injection
- `../WpfVisualTreeMcp.Bootstrapper/WpfInspectorBootstrapper.cpp` - Native CLR
  bootstrapper
- `../WpfVisualTreeMcp.InjectorHelper/Program.cs` - x86 cross-bitness helper
