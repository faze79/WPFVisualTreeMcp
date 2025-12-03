# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-12-02

### Added
- Added `claude mcp add` command line instructions for easier Claude Code configuration
- Updated documentation with multiple configuration options (CLI vs JSON)

### Changed

#### Migration to Official MCP SDK
- **BREAKING**: Migrated from custom MCP protocol implementation to official [Microsoft/Anthropic MCP SDK for .NET](https://github.com/modelcontextprotocol/csharp-sdk)
- **BREAKING**: Configuration now requires direct path to `.exe` file instead of `dotnet run`
- Simplified `Program.cs` from 55 lines to 28 lines using SDK's built-in features
- All 13 WPF inspection tools now use `[McpServerTool]` attributes for auto-discovery
- Improved protocol compatibility and stability with Claude Code

#### Benefits of Migration
- ✅ **Guaranteed compatibility** with Claude Code and other MCP clients
- ✅ **Faster connection** (~463ms vs previous timeouts)
- ✅ **Automatic protocol negotiation** - no more version mismatches
- ✅ **Better maintainability** - SDK handles all JSON-RPC details
- ✅ **Official support** from Microsoft/Anthropic

#### Technical Changes
- Added NuGet dependency: `ModelContextProtocol` (v0.4.1-preview.1)
- Removed custom `McpServer.cs` protocol implementation (722 lines → SDK managed)
- Created new `WpfTools.cs` with declarative tool definitions
- Simplified logging configuration - completely disabled for stdio protocol
- Removed UTF-8 BOM encoding issues that caused JSON parse errors

### Fixed
- Fixed connection timeout issues with Claude Code (was 30+ seconds, now <500ms)
- Fixed JSON parsing errors caused by log output on stdout
- Fixed protocol version negotiation (now accepts client's version)
- Fixed notification handling (no longer sends error responses for notifications)

### Migration Guide

**Old Configuration:**
```json
{
  "mcpServers": {
    "wpf-visual-tree": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/WpfVisualTreeMcp.Server"]
    }
  }
}
```

**New Configuration:**
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

**Steps:**
1. Build the project: `dotnet build -c Release`
2. Update your MCP configuration with absolute path to the `.exe`
3. Restart Claude Code
4. Verify tools appear with `mcp__wpf-visual-tree__` prefix

[1.0.0]: https://github.com/faze79/WpfVisualTreeMcp/releases/tag/v1.0.0
