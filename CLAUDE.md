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
    ├─ WpfTools (14 inspection tools)
    ├─ ProcessManager (discovers WPF processes)
    └─ NamedPipeBridge (IPC)
        ↓ [Named Pipes: wpf_inspector_{pid}]
Target WPF Application (.NET Framework)
    └─ Inspector DLL (.NET 4.8)
        ├─ TreeWalker (visual tree navigation)
        ├─ PropertyReader (dependency properties)
        ├─ BindingAnalyzer (binding inspection)
        └─ IpcServer (named pipe communication)
```

**Key Design:** Multi-process architecture for safety. The server runs separately and communicates via named pipes. All operations are read-only.

## Key Source Locations

| Component | Location | Purpose |
|-----------|----------|---------|
| MCP Server Entry | `src/WpfVisualTreeMcp.Server/Program.cs` | Server initialization with MCP SDK |
| Tool Definitions | `src/WpfVisualTreeMcp.Server/WpfTools.cs` | 14 tools with `[McpServerTool]` attributes |
| IPC Bridge | `src/WpfVisualTreeMcp.Server/Services/NamedPipeBridge.cs` | Named pipe communication to Inspector |
| Process Manager | `src/WpfVisualTreeMcp.Server/Services/ProcessManager.cs` | WPF process discovery and attachment |
| Inspector Entry | `src/WpfVisualTreeMcp.Inspector/InspectorService.cs` | Injected DLL main entry point |
| IPC Server | `src/WpfVisualTreeMcp.Inspector/IpcServer.cs` | Named pipe server in target process |
| IPC Messages | `src/WpfVisualTreeMcp.Shared/Ipc/IpcMessages.cs` | Request/response contracts |

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
- Valid only within same MCP server session
- Format: `elem_{hashCode}` or similar
- Restarting Claude Code invalidates all handles

### UTF-8 BOM Handling
The Inspector strips UTF-8 BOM (0xEF 0xBB 0xBF) before JSON parsing to prevent deserialization errors.

### Logging
- Server logs: `%LOCALAPPDATA%\WpfVisualTreeMcp\logs\mcp-server-YYYYMMDD.log`
- Inspector debug: `%TEMP%\WpfInspector_Debug.log`
- stdout must remain clean for JSON-RPC protocol

## WPF App Requirements

For a WPF application to be inspectable, it must initialize the Inspector:

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
