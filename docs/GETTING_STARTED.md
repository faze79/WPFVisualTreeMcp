# Getting Started

This guide will help you set up and start using WpfVisualTreeMcp to inspect WPF applications with AI coding agents.

## Prerequisites

Before you begin, ensure you have:

- **Windows 10/11** - WPF applications only run on Windows
- **.NET 8.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **A WPF application** - Either your own or the included sample app
- **An MCP-compatible AI agent** - Claude Code, Cursor, or similar

## Installation

### Option 1: Build from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/faze79/WpfVisualTreeMcp.git
   cd WpfVisualTreeMcp
   ```

2. Build the solution:
   ```bash
   dotnet build
   ```

3. The MCP server will be available at:
   ```
   src/WpfVisualTreeMcp.Server/bin/Debug/net8.0/WpfVisualTreeMcp.Server.exe
   ```

### Option 2: .NET Tool Installation (Coming Soon)

```bash
dotnet tool install -g WpfVisualTreeMcp
```

## Configuration

### Claude Code

There are multiple ways to configure the MCP server in Claude Code:

#### Option 1: Command Line (Recommended)

Use the `claude mcp add` command for quick setup:

```bash
# Build the server first
cd WpfVisualTreeMcp
dotnet build -c Release

# Add to current project only
claude mcp add wpf-visual-tree -- C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe

# Or add globally (available in all projects)
claude mcp add --scope user wpf-visual-tree -- C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe
```

Verify the server was added:
```bash
claude mcp list
```

Remove the server if needed:
```bash
claude mcp remove wpf-visual-tree
```

#### Option 2: JSON Configuration

Create or edit `.mcp.json` in your project root:

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

For global configuration, add to `~/.claude/settings.json`.

Restart Claude Code or reload the window after making changes.

### Cursor

1. Create or edit `.cursor/mcp.json` in your project:

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

2. Restart Cursor.

### VS Code with Continue

1. Edit your Continue configuration (`~/.continue/config.json`):

   ```json
   {
     "mcpServers": [
       {
         "name": "wpf-visual-tree",
         "command": "C:/path/to/WpfVisualTreeMcp.Server.exe"
       }
     ]
   }
   ```

## Setting Up Your WPF Application (Self-Hosted Mode)

For the MCP server to inspect your WPF application, you need to add the Inspector DLL to your project. This is called "self-hosted mode" and is the recommended approach.

### Step 1: Add Project Reference

Add a reference to `WpfVisualTreeMcp.Inspector` in your WPF project:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/WpfVisualTreeMcp.Inspector/WpfVisualTreeMcp.Inspector.csproj" />
</ItemGroup>
```

### Step 2: Initialize the Inspector

In your `App.xaml.cs`, initialize the inspector on startup:

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
        // This creates a named pipe server for the MCP server to connect to
        InspectorService.Initialize(Process.GetCurrentProcess().Id);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up the inspector service
        InspectorService.Instance?.Dispose();
        base.OnExit(e);
    }
}
```

### Step 3: Build and Run

Build your application. The inspector will start automatically and listen for MCP server connections.

## Quick Start Tutorial

### Step 1: Start a WPF Application

Either start your own WPF application (with the Inspector set up as above) or use the included sample:

```bash
cd WpfVisualTreeMcp
dotnet run --project samples/SampleWpfApp
```

The sample app already has the Inspector configured.

### Step 2: List Available Processes

Ask your AI agent to list WPF processes:

```
Show me all running WPF applications I can inspect.
```

Expected response:
```
Found 1 WPF application:
- SampleWpfApp (PID: 1234) - "Sample WPF Application"
```

### Step 3: Attach to the Application

```
Attach to SampleWpfApp so I can inspect it.
```

### Step 4: Explore the Visual Tree

```
Show me the visual tree of the main window.
```

This will display the hierarchy of UI elements.

### Step 5: Inspect an Element

```
Show me the properties of the SubmitButton.
```

### Step 6: Find Binding Errors

```
Are there any binding errors in this application?
```

## Common Tasks

### Debugging Layout Issues

```
Get the layout information for the ContentGrid element.
What are the actual sizes and margins?
```

### Finding Elements

```
Find all TextBox elements in the application.
Find elements where IsEnabled is false.
```

### Analyzing Bindings

```
Show me all bindings on the UserNameTextBox.
What is the binding source for the SelectedItem property?
```

### Checking Resources

```
List all application-level resources.
What styles are defined in this application?
```

## Troubleshooting

### "No WPF applications found"

- Ensure the target application is running
- Check that it's a .NET Framework WPF application
- The application must have a main window visible

### "Failed to attach to process" or "Inspector not loaded"

- Ensure the target application has the Inspector DLL set up (self-hosted mode)
- The application must call `InspectorService.Initialize()` on startup
- Check that the Inspector project reference is correctly added

### "Connection timeout" or "Communication error"

- The Inspector's named pipe server may not be running
- Verify the application started successfully and called `InspectorService.Initialize()`
- Check the pipe name: `wpf_inspector_{processId}`

### "Element not found"

- The element may have been removed from the visual tree
- Try refreshing by getting the visual tree again
- The element handle may have expired (handles are session-based)

### "Binding path error"

- The binding path contains a typo
- The source property doesn't exist
- The DataContext is null

## Best Practices

1. **Start with the visual tree** - Get an overview before diving into specific elements
2. **Use element search** - Don't manually navigate large trees
3. **Check binding errors first** - Many UI issues are caused by broken bindings
4. **Export for offline analysis** - Large trees can be exported to JSON

## Next Steps

- Read the [Tools Reference](TOOLS_REFERENCE.md) for complete API documentation
- Explore the [Architecture](ARCHITECTURE.md) to understand how it works
- Try the sample application with intentional binding errors
- Integrate into your development workflow
