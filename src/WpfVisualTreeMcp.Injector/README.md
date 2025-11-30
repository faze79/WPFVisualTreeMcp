# WpfVisualTreeMcp.Injector

This project handles injection of the Inspector DLL into target WPF processes.

## Current Status

The injector is currently a **stub implementation**. Full DLL injection into external .NET processes requires advanced techniques that are beyond the scope of the initial implementation.

## Injection Approaches

### Option 1: Self-Hosted Mode (Recommended for Development)

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

### Option 2: Native Injection (Future)

For production scenarios where you need to attach to arbitrary WPF applications, the following approaches can be considered:

1. **CreateRemoteThread with LoadLibrary**
   - Classic DLL injection technique
   - Requires a native C++ helper for bootstrapping managed code

2. **CLR Debugging APIs (ICorDebug)**
   - Uses the .NET debugging infrastructure
   - Can create threads and load assemblies in the target process

3. **EasyHook or Similar Libraries**
   - Third-party libraries that simplify managed injection
   - Handle the complexity of cross-process managed code loading

4. **AppDomain Injection via Profiling API**
   - Uses the CLR profiling infrastructure
   - Most invasive but most capable option

## Why Injection is Complex

Injecting managed code (.NET) into another managed process is significantly more complex than native DLL injection because:

1. The CLR must be properly initialized in the target process
2. The injected assembly must be loaded into the correct AppDomain
3. The injected code must run on the correct thread (usually the UI thread for WPF)
4. .NET Core/5+ has different hosting requirements than .NET Framework

## Recommended Development Workflow

1. **During Development**
   - Use self-hosted mode in your test applications
   - Reference the Inspector DLL directly

2. **For Testing with External Apps**
   - Build a test harness that loads Inspector on startup
   - Use this for integration testing

3. **Future Production**
   - Implement proper injection using one of the approaches above
   - Or consider a different architecture (e.g., a Visual Studio extension)

## Files

- `ProcessInjector.cs` - Stub implementation with P/Invoke declarations for future use
