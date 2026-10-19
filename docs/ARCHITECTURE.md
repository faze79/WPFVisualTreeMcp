# Architecture

This document describes the technical architecture of WpfVisualTreeMcp.

## Overview

WpfVisualTreeMcp is designed as a bridge between AI coding agents and running WPF applications. It follows a multi-process architecture to safely inspect WPF applications without affecting their stability.

## Components

### 1. MCP Server (`WpfVisualTreeMcp.Server`)

The MCP Server is the main entry point that communicates with AI agents via the Model Context Protocol.

**Technology:** .NET 8.0

**Responsibilities:**
- Handle MCP protocol communication (stdio transport)
- Register and expose tools to AI agents
- Manage connections to target WPF applications
- Coordinate inspection requests and responses

**Key Classes:**
- `McpServer` - Main protocol handler
- `ProcessManager` - Discovers and manages WPF processes
- `NamedPipeBridge` - IPC communication with injected DLLs

### 2. Inspector DLL (`WpfVisualTreeMcp.Inspector`)

The Inspector is a .NET Framework library that gets loaded into target WPF applications to perform inspection operations.

**Technology:** .NET Framework 4.7.2/4.8 and .NET 8.0-windows. Injection packages
use the net472 payload for .NET Framework applications and the separate CoreCLR
payload for modern .NET applications.

**Responsibilities:**
- Access `VisualTreeHelper` and `LogicalTreeHelper`
- Read dependency properties and bindings
- Enumerate resources and styles
- Monitor property changes
- Respond to inspection requests via named pipes

**Key Classes:**
- `InspectorService` - Main entry point when loaded
- `TreeWalker` - Navigates visual and logical trees
- `PropertyReader` - Reads element properties
- `BindingAnalyzer` - Analyzes data bindings

### 3. Injector (`WpfVisualTreeMcp.Injector`)

The Injector is responsible for loading the Inspector DLL into target WPF processes.

**Technology:** C++/CLI or managed code

**Responsibilities:**
- Enumerate running WPF processes
- Inject the Inspector DLL into target processes
- Handle injection failures gracefully

## Communication Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                        AI Agent                                   │
│                  (Claude Code, Cursor, etc.)                      │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           │ MCP Protocol (JSON-RPC over stdio)
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     MCP Server Process                            │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                    Tool Handlers                            │  │
│  │  • wpf_list_processes    • wpf_get_visual_tree             │  │
│  │  • wpf_attach            • wpf_get_element_properties      │  │
│  │  • wpf_find_elements     • wpf_get_bindings                │  │
│  └────────────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                  Process Manager                            │  │
│  │  • Process discovery     • Injection coordination          │  │
│  └────────────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                Named Pipe Bridge                            │  │
│  │  • Serialization         • Request/Response handling       │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           │ Named Pipes (IPC)
                           │ Pipe name: wpf_inspector_{pid}
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                  Target WPF Application                           │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                   Inspector DLL                             │  │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │  │
│  │  │  TreeWalker  │  │PropertyReader│  │BindingAnalyzer│     │  │
│  │  └──────────────┘  └──────────────┘  └──────────────┘      │  │
│  │  ┌──────────────────────────────────────────────────┐      │  │
│  │  │              IPC Server (Named Pipe)              │      │  │
│  │  └──────────────────────────────────────────────────┘      │  │
│  └────────────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                 Application UI Thread                       │  │
│  │                   (Visual Tree)                             │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

## Data Models

### Element Handle

Each UI element is identified by a unique handle for the duration of an inspection session:

```csharp
public record ElementHandle
{
    public string Id { get; init; }           // Unique identifier
    public string TypeName { get; init; }     // e.g., "System.Windows.Controls.Button"
    public string? Name { get; init; }        // x:Name if set
    public int HashCode { get; init; }        // GetHashCode() for identity
}
```

### Visual Tree Node

Represents a node in the visual tree:

```csharp
public record VisualTreeNode
{
    public ElementHandle Handle { get; init; }
    public string TypeName { get; init; }
    public string? Name { get; init; }
    public List<VisualTreeNode> Children { get; init; }
    public int Depth { get; init; }
}
```

### Property Value

Represents a dependency property value:

```csharp
public record PropertyValue
{
    public string Name { get; init; }
    public string TypeName { get; init; }
    public string? Value { get; init; }
    public PropertyValueSource Source { get; init; }  // Local, Style, Default, etc.
    public bool IsBinding { get; init; }
}
```

## Threading Considerations

Physical interaction has two phases. `ControlInteractor.PrepareDrag` / `PrepareTouch`
resolve handles, scroll before input, hit-test the path and transform local WPF DIPs
to screen coordinates on the UI Dispatcher. `MouseDragExecutor` / `TouchGestureExecutor`
then drive timed native input from worker tasks, leaving WPF free to process mouse
capture, OLE drag-and-drop and touch/manipulation events. A per-Inspector gate rejects
overlapping mutations while inspection remains available. Input has a bounded duration,
and failures/cancellation release the mouse or cancel active touch contacts.

`NativeMouseInput` uses SendInput; `NativeTouchInput` uses InitializeTouchInjection /
InjectTouchInput with stable contact IDs and complete multi-contact frames. Both share
desktop/foreground validation and per-call DPI context through `PhysicalInputGuard`.
Results describe input delivery, not application acceptance. The Inspector preserves
the host's touch configuration; see [touch compatibility](TOOLS_REFERENCE.md#touch-compatibility-and-verification).

WPF applications are single-threaded (STA). All inspection operations must be marshaled to the UI thread:

```csharp
// In Inspector DLL
public async Task<VisualTreeNode> GetVisualTreeAsync(ElementHandle root)
{
    return await Application.Current.Dispatcher.InvokeAsync(() =>
    {
        var element = ResolveHandle(root);
        return WalkVisualTree(element);
    });
}
```

## Security Considerations

### Process Isolation
- The MCP Server runs in a separate process from target applications
- Inspection tools read the visual tree; interaction/property tools explicitly change application state
- No modification of application state is possible through inspection

### Named Pipe Security
- Pipes are created with appropriate ACLs
- Only the MCP Server process can connect
- Pipe names include process IDs to prevent collisions

### Injection Safety
- Only .NET Framework WPF applications can be inspected
- Injection uses safe managed code techniques
- Target application stability is preserved

## Error Handling

### Connection Failures
- Target process may have exited
- Named pipe may be unavailable
- Injection may have failed

### Inspection Errors
- Element may have been garbage collected
- Property access may throw exceptions
- Tree structure may have changed

All errors are propagated back to the AI agent with meaningful error messages.

## Performance Considerations

### Tree Size
- Large visual trees can have thousands of elements
- Pagination and depth limits are enforced
- Lazy loading is used where possible

### Property Enumeration
- Not all properties are read by default
- Common properties are prioritized
- Expensive property reads are opt-in

### Caching
- Element handles are cached for session duration
- Property values are not cached (always fresh)
- Tree structure snapshots can be compared

## Future Extensibility

### Plugin Architecture
The Inspector DLL is designed to support plugins for:
- Custom property formatters
- Additional tree types (automation tree)
- Framework-specific inspectors (Prism, MVVM Light)

### Network Support
Future versions may support:
- Remote debugging over TCP
- Cloud-based inspection
- Multi-machine scenarios
