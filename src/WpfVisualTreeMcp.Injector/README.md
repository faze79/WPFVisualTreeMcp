# WpfVisualTreeMcp.Injector

This project handles injection of the Inspector DLL into target WPF processes, enabling inspection of any running WPF application without requiring them to reference the Inspector library.

## How It Works

The injection process uses Windows API calls to:

1. **Open the target process** with appropriate permissions
2. **Allocate memory** in the target process for the DLL path
3. **Write the DLL path** to the allocated memory
4. **Create a remote thread** that calls `LoadLibraryW` to load the bootstrapper DLL
5. **Call the Initialize export** to start the Inspector

## Architecture Requirements

- **Architecture must match**: 64-bit injector can only inject into 64-bit processes
- Both x86 and x64 builds are supported
- The target process must be a .NET application (CLR must be loaded)

## Components

### ProcessInjector.cs

The main injection class with these key methods:

- `InjectIntoProcess(int processId)` - Inject the Inspector into a process
- `IsManagedProcess(Process process)` - Check if a process is .NET
- `IsInspectorLoaded(Process process)` - Check if Inspector is already loaded
- `IsArchitectureMatch(Process process)` - Verify architecture compatibility

### WpfVisualTreeMcp.Bootstrapper

A C++/CLI mixed-mode DLL that acts as a bridge between native code and managed code:

1. Exposes native exports (`Initialize`, `IsLoaded`) that can be called via `CreateRemoteThread`
2. Uses .NET reflection to load the Inspector assembly
3. Calls `InspectorService.Initialize()` on the WPF dispatcher thread

## Building the Bootstrapper

The bootstrapper requires Visual Studio with C++/CLI support:

1. Install Visual Studio 2022 with "Desktop development with C++" workload
2. Include the "C++/CLI support for v143 build tools" component
3. Build the `WpfVisualTreeMcp.Bootstrapper` project for both x86 and x64

```bash
msbuild WpfVisualTreeMcp.Bootstrapper.vcxproj /p:Configuration=Release /p:Platform=x64
msbuild WpfVisualTreeMcp.Bootstrapper.vcxproj /p:Configuration=Release /p:Platform=x86
```

## Usage

### Via MCP Tool

```
Use the wpf_inject tool to inject the Inspector into process ID 1234.
```

### Programmatic Usage

```csharp
using WpfVisualTreeMcp.Injector;

var injector = new ProcessInjector();
var result = injector.InjectIntoProcess(processId);

if (result.Success)
{
    Console.WriteLine($"Injected successfully: {result.Message}");
}
else
{
    Console.WriteLine($"Injection failed: {result.Error}");
}
```

## Alternative: Self-Hosted Mode

For development and testing, the recommended approach is to have your WPF application directly reference the Inspector DLL:

```csharp
// In your WPF application's App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // Initialize the inspector
    WpfVisualTreeMcp.Inspector.InspectorService.Initialize(Process.GetCurrentProcess().Id);
}
```

This avoids the complexity of injection and provides the most reliable experience.

## Troubleshooting

### "Access denied" or permission errors

- Run the MCP server as Administrator
- The target process may have elevated permissions

### "Architecture mismatch"

- Ensure the server matches the target process architecture
- Use the x64 build for 64-bit applications

### "LoadLibrary failed"

- The bootstrapper DLL may not be found
- Ensure both `WpfVisualTreeMcp.Bootstrapper.dll` and `WpfVisualTreeMcp.Inspector.dll` are in the same directory

### "Application.Current is null"

- The target application may not have fully initialized yet
- Try waiting a few seconds after the application starts

## Security Considerations

DLL injection requires elevated permissions and may be flagged by antivirus software. This is expected behavior for debugging tools. The injection is only intended for:

- Development and debugging
- Automated testing
- UI inspection and analysis

Never use this tool on applications you don't own or have permission to inspect.
