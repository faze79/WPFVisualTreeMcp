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

1. Open your Claude Code settings or create a project-level MCP configuration.

2. Add the WpfVisualTreeMcp server:

   **Using `dotnet run`:**
   ```json
   {
     "mcpServers": {
       "wpf-visual-tree": {
         "command": "dotnet",
         "args": [
           "run",
           "--project",
           "C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server"
         ]
       }
     }
   }
   ```

   **Using the built executable:**
   ```json
   {
     "mcpServers": {
       "wpf-visual-tree": {
         "command": "C:/path/to/WpfVisualTreeMcp/src/WpfVisualTreeMcp.Server/bin/Debug/net8.0/WpfVisualTreeMcp.Server.exe",
         "args": []
       }
     }
   }
   ```

3. Restart Claude Code or reload the window.

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

## Quick Start Tutorial

### Step 1: Start a WPF Application

Either start your own WPF application or use the included sample:

```bash
cd WpfVisualTreeMcp
dotnet run --project samples/SampleWpfApp
```

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
- Try running with administrator privileges

### "Failed to attach to process"

- The application may be running as a different user
- Try running the MCP server with administrator privileges
- Check if antivirus is blocking the injection

### "Element not found"

- The element may have been removed from the visual tree
- Try refreshing by getting the visual tree again
- The element handle may have expired

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
