using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using WpfVisualTreeMcp.Server.Services;

// Build the host with MCP server and dependency injection
var builder = Host.CreateApplicationBuilder(args);

// CRITICAL: stdout must be completely clean for MCP stdio protocol
// Enable logging to STDERR only for debugging
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace; // Everything goes to stderr
});
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Register WPF-specific services needed by tools
builder.Services.AddSingleton<IProcessManager, ProcessManager>();
builder.Services.AddSingleton<IIpcBridge, NamedPipeBridge>();

// Add MCP server with stdio transport and auto-discover tools from this assembly
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Build and run
await builder.Build().RunAsync();
