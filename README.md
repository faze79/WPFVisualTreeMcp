# WpfVisualTreeMcp

[![Build](https://github.com/faze79/WpfVisualTreeMcp/actions/workflows/build.yml/badge.svg)](https://github.com/faze79/WpfVisualTreeMcp/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![MCP Compatible](https://img.shields.io/badge/MCP-Compatible-green)](https://modelcontextprotocol.io/)

> MCP server for inspecting WPF application Visual Trees - enables AI agents to debug and analyze WPF UI hierarchies in real-time

## Overview

**WpfVisualTreeMcp** is a [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that allows AI coding agents (Claude Code, Cursor, GitHub Copilot) to inspect and interact with running WPF applications. Think of it as giving your AI assistant the same capabilities as tools like [Snoop WPF](https://github.com/snoopwpf/snoopwpf) or Visual Studio's Live Visual Tree.

### Why This Matters

Debugging WPF UI issues traditionally requires manual inspection with specialized tools. This project bridges that gap by exposing WPF inspection capabilities through MCP, enabling AI agents to:

- **Understand UI structure** during code reviews
- **Diagnose binding errors** automatically
- **Suggest fixes** for layout issues
- **Assist with UI refactoring** tasks
- **Analyze visual tree hierarchies** in real-time

## Features

### Core Inspection
- **Process Discovery** - List all running WPF applications available for inspection
- **Visual Tree Navigation** - Traverse the complete visual tree hierarchy
- **Logical Tree Access** - Navigate the logical tree structure
- **Property Inspection** - Read all dependency properties of any UI element

### Binding & Resources
- **Binding Analysis** - Inspect data bindings with their current status
- **Binding Error Detection** - Automatically find and report binding errors
- **Resource Enumeration** - Browse resource dictionaries at any scope
- **Style Inspection** - View applied styles and templates

### Search & Monitoring
- **Element Search** - Find elements by type, name, or property values
- **Property Watching** - Monitor property changes in real-time
- **Tree Diff** - Compare visual tree snapshots to detect changes

### Interaction & Export
- **Element Highlighting** - Visually highlight elements in the running app
- **Layout Information** - Get detailed layout metrics
- **Tree Export** - Export visual tree to XAML or JSON format

## Quick Start

### Prerequisites

- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A WPF application to inspect

### Installation

#### Option 1: Build from Source

```bash
git clone https://github.com/faze79/WpfVisualTreeMcp.git
cd WpfVisualTreeMcp
dotnet build
```

#### Option 2: .NET Tool (Coming Soon)

```bash
dotnet tool install -g WpfVisualTreeMcp
```

### Configuration

The server uses the **official Microsoft/Anthropic MCP SDK for .NET**, providing guaranteed compatibility with Claude Code and other MCP clients.

#### Claude Code

**Option 1: Project-level Configuration (Recommended)**

Create or edit `.claude.json` in your project root:

```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe",
      "args": []
    }
  }
}
```

**Option 2: Global Configuration**

Add to `~/.claude.json`:

```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe",
      "args": []
    }
  }
}
```

**Important Notes:**
- Use absolute paths to the built `.exe` file
- Use forward slashes (`/`) in paths on Windows
- Build in Release mode for production: `dotnet build -c Release`
- Restart Claude Code after configuration changes

#### Cursor

Add to your Cursor settings (`.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe",
      "args": []
    }
  }
}
```

### Self-Hosted Mode (Recommended)

For your WPF application to be inspectable, add a reference to the Inspector DLL and initialize it on startup:

1. Add a project reference to `WpfVisualTreeMcp.Inspector`

2. In your `App.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using WpfVisualTreeMcp.Inspector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize the WPF Visual Tree Inspector
        InspectorService.Initialize(Process.GetCurrentProcess().Id);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InspectorService.Instance?.Dispose();
        base.OnExit(e);
    }
}
```

This enables the MCP server to connect to your application via named pipes for real-time inspection.

## Usage Examples

### List Running WPF Applications

```
Use the wpf_list_processes tool to show all running WPF applications.
```

### Attach and Inspect Visual Tree

```
Attach to the MyApp.exe WPF application and show me the visual tree of the main window.
```

### Find Binding Errors

```
Check the attached WPF application for any binding errors and explain what's causing them.
```

### Search for Elements

```
Find all Button elements in the visual tree that have IsEnabled set to false.
```

### Export Visual Tree

```
Export the visual tree of the current window to JSON format so I can analyze the structure.
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    AI Agent (Claude Code)                    │
└─────────────────────────┬───────────────────────────────────┘
                          │ MCP Protocol (stdio)
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                 WpfVisualTreeMcp Server                      │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────┐  │
│  │   MCP Handler   │  │  Tree Navigator │  │   Injector  │  │
│  │  (Tools/Res.)   │  │   & Inspector   │  │   Manager   │  │
│  └────────┬────────┘  └────────┬────────┘  └──────┬──────┘  │
└───────────┼────────────────────┼───────────────────┼────────┘
            │                    │                   │
            └────────────────────┼───────────────────┘
                                 │ Named Pipes / IPC
                                 ▼
┌─────────────────────────────────────────────────────────────┐
│                   Target WPF Application                     │
│  ┌─────────────────────────────────────────────────────────┐│
│  │              Injected Inspector DLL                      ││
│  │  • VisualTreeHelper access                              ││
│  │  • LogicalTreeHelper access                             ││
│  │  • Property/Binding inspection                          ││
│  │  • Resource dictionary enumeration                      ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

For detailed architecture documentation, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Available Tools

| Tool | Description |
|------|-------------|
| `wpf_list_processes` | List all running WPF applications |
| `wpf_attach` | Attach to a WPF application by process ID or name |
| `wpf_get_visual_tree` | Get the visual tree hierarchy |
| `wpf_get_element_properties` | Get all dependency properties of an element |
| `wpf_find_elements` | Search for elements by criteria |
| `wpf_get_bindings` | Get data bindings for an element |
| `wpf_get_binding_errors` | List all binding errors |
| `wpf_get_resources` | Enumerate resource dictionaries |
| `wpf_get_styles` | Get applied styles and templates |
| `wpf_watch_property` | Monitor a property for changes |
| `wpf_highlight_element` | Visually highlight an element |
| `wpf_get_layout_info` | Get layout information |
| `wpf_export_tree` | Export visual tree to XAML or JSON |

For complete tool documentation, see [docs/TOOLS_REFERENCE.md](docs/TOOLS_REFERENCE.md).

## Roadmap

### Phase 1: Core Inspection ✅
- [x] Project structure and architecture
- [x] Process discovery and enumeration
- [x] Basic process attachment
- [x] Visual tree navigation
- [x] Property inspection
- [x] Element search

### Phase 2: Advanced Features ✅
- [x] IPC communication via named pipes
- [x] Binding analysis and error detection
- [x] Resource dictionary enumeration
- [x] Style and template inspection
- [x] Property change monitoring (with notifications)

### Phase 3: Interaction & Diagnostics (In Progress)
- [x] Element highlighting overlay
- [x] XAML/JSON export
- [ ] Visual tree diff/comparison
- [ ] Performance diagnostics

### Future Considerations
- [ ] DLL injection for external processes (currently self-hosted mode)
- [ ] Visual tree modification capabilities

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/faze79/WpfVisualTreeMcp.git
   cd WpfVisualTreeMcp
   ```

2. Open in Visual Studio 2022 or VS Code:
   ```bash
   code .
   # or
   start WpfVisualTreeMcp.sln
   ```

3. Build and run tests:
   ```bash
   dotnet build
   dotnet test
   ```

4. Run the sample WPF app for testing:
   ```bash
   dotnet run --project samples/SampleWpfApp
   ```

### Project Structure

```
WpfVisualTreeMcp/
├── src/
│   ├── WpfVisualTreeMcp.Server/      # MCP Server (.NET 8) - Uses official MCP SDK
│   │   ├── Program.cs                # Server initialization with MCP SDK
│   │   ├── WpfTools.cs               # 13 WPF inspection tools
│   │   └── Services/                 # Process & IPC management
│   ├── WpfVisualTreeMcp.Inspector/   # Injected DLL (.NET Framework 4.8)
│   ├── WpfVisualTreeMcp.Injector/    # Native injector
│   └── WpfVisualTreeMcp.Shared/      # Shared models
├── samples/
│   └── SampleWpfApp/                 # Test application
├── tests/
│   └── WpfVisualTreeMcp.Tests/       # Unit tests
└── docs/                             # Documentation
```

### Technical Details

- **MCP SDK**: Built with the [official C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk) from Microsoft/Anthropic
- **Protocol**: JSON-RPC 2.0 over stdio transport
- **Target Framework**: .NET 8.0 (Server) / .NET Framework 4.8 (Inspector)
- **IPC**: Named Pipes for server-to-application communication
- **Tools**: 13 inspection tools auto-discovered via `[McpServerTool]` attributes

## Acknowledgments

- Inspired by [Snoop WPF](https://github.com/snoopwpf/snoopwpf) - the original WPF spy utility
- Built on the [Model Context Protocol](https://modelcontextprotocol.io/) by Anthropic
- Thanks to the .NET and WPF community

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
