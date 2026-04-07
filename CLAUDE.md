# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WpfVisualTreeMcp** is an MCP (Model Context Protocol) server that enables AI agents to inspect running WPF applications in real-time. It exposes WPF visual tree inspection capabilities similar to Snoop WPF or Visual Studio's Live Visual Tree through the MCP protocol.

## Build Commands

```bash
# Build solution
dotnet build WpfVisualTreeMcp.sln

# Build for Release
dotnet build -c Release WpfVisualTreeMcp.sln

# Run tests
dotnet test WpfVisualTreeMcp.sln

# Run sample WPF app for testing
dotnet run --project samples/SampleWpfApp

# Publish MCP Server executable
dotnet publish src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj -c Release -o ./publish
```

## Architecture

```
AI Agent (Claude Code)
    ↓ [MCP Protocol - JSON-RPC over stdio]
MCP Server (.NET 8.0)
    ├─ WpfTools (15 inspection tools)
    ├─ ProcessManager (discovers WPF processes)
    └─ NamedPipeBridge (IPC)
        ↓ [Named Pipes: wpf_inspector_{pid}]
Target WPF Application (.NET Framework)
    └─ Inspector DLL (.NET 4.8)
        ├─ TreeWalker (visual tree navigation + adorner/popup traversal)
        ├─ ScreenshotCapture (RenderTargetBitmap element capture)
        ├─ PropertyReader (dependency properties)
        ├─ BindingAnalyzer (binding inspection)
        └─ IpcServer (named pipe communication)
```

**Key Design:** Multi-process architecture for safety. The server runs separately and communicates via named pipes. All operations are read-only.

## Key Source Locations

| Component | Location | Purpose |
|-----------|----------|---------|
| MCP Server Entry | `src/WpfVisualTreeMcp.Server/Program.cs` | Server initialization with MCP SDK |
| Tool Definitions | `src/WpfVisualTreeMcp.Server/WpfTools.cs` | 17 tools with `[McpServerTool]` attributes |
| IPC Bridge | `src/WpfVisualTreeMcp.Server/Services/NamedPipeBridge.cs` | Named pipe communication to Inspector |
| Process Manager | `src/WpfVisualTreeMcp.Server/Services/ProcessManager.cs` | WPF process discovery and attachment |
| Inspector Entry | `src/WpfVisualTreeMcp.Inspector/InspectorService.cs` | Injected DLL main entry point |
| Screenshot Capture | `src/WpfVisualTreeMcp.Inspector/ScreenshotCapture.cs` | RenderTargetBitmap element/window capture |
| IPC Server | `src/WpfVisualTreeMcp.Inspector/IpcServer.cs` | Named pipe server in target process |
| IPC Messages | `src/WpfVisualTreeMcp.Shared/Ipc/IpcMessages.cs` | Request/response contracts |
| Native Bootstrapper | `src/WpfVisualTreeMcp.Bootstrapper/WpfInspectorBootstrapper.cpp` | CLR hosting for DLL injection |
| Process Injector | `src/WpfVisualTreeMcp.Injector/ProcessInjector.cs` | CreateRemoteThread + LoadLibrary injection |

## Threading Model

- WPF apps are single-threaded (STA). All visual tree operations must run on the UI Dispatcher thread.
- Inspector wraps Dispatcher.Invoke in `Task.Run()` to avoid blocking the IPC thread.
- 10-second timeout for UI operations.

## Important Implementation Notes

### Named Pipe Communication
- Pipe name format: `wpf_inspector_{pid}`
- Connection timeout: 5 seconds
- Request timeout: 30 seconds
- Uses direct byte I/O (not StreamReader/Writer) due to .NET Framework 4.8 deadlock issues

### Element Handles
- Valid only within same Inspector session (same process lifetime)
- Format: `elem_{counter:X8}` (hex counter, e.g., `elem_00000052`)
- Restarting the target WPF app invalidates all handles
- If a handle is not found, the tool returns an error (not a silent fallback)

### UTF-8 BOM Handling
The Inspector strips UTF-8 BOM (0xEF 0xBB 0xBF) before JSON parsing to prevent deserialization errors.

### Visual Tree Traversal
- Traverses standard visual children via `VisualTreeHelper`
- Also enumerates `AdornerLayer` adorners (e.g., Fluent.Ribbon Backstage)
- Traverses into `Popup` child elements (separate visual trees)
- `FindElements` searches across ALL open windows when no `root_handle` given
- Default `max_depth` is 25 (configurable 1-100)

### Screenshot Capture
- Uses `RenderTargetBitmap` + `VisualBrush` technique (handles transforms)
- DPI-aware via `PresentationSource.FromVisual`
- Downscales if exceeding `max_width`/`max_height` (default 1920x1080)
- Returns MCP `ImageContentBlock` (base64 PNG) — Claude sees the image directly

### Logging
- Server logs: `%LOCALAPPDATA%\WpfVisualTreeMcp\logs\mcp-server-YYYYMMDD.log`
- Inspector debug: `%TEMP%\WpfInspector_Debug.log`
- Bootstrapper debug: `%TEMP%\WpfInspectorBootstrapper.log`
- stdout must remain clean for JSON-RPC protocol

## WPF App Inspection Modes

### Auto-Injection Mode
Use `wpf_attach(process_id=<pid>, auto_inject=true)` to inject the Inspector into any running .NET Framework WPF app. Requires:
- Native bootstrapper DLL in `publish/native/x64/` (or x86)
- Target process must be .NET Framework (CLR hosting)
- Architecture detection is automatic (x64 vs x86)

### Self-Hosted Mode
For your own WPF application, add a reference to the Inspector and initialize on startup:

```csharp
// In App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    InspectorService.Initialize(Process.GetCurrentProcess().Id);
}
```

## MCP Server Configuration

The server uses the official Microsoft/Anthropic MCP SDK. Configure in `.mcp.json`:

```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "C:/path/to/WpfVisualTreeMcp.Server.exe",
      "args": []
    }
  }
}
```

## Test Framework

- **xUnit** - Test runner
- **Moq** - Mocking
- **FluentAssertions** - Assertions
- Tests located in `tests/WpfVisualTreeMcp.Tests/`
