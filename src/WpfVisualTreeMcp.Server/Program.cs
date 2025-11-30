using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Services;

// Build the host with dependency injection
var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr so stdout is clean for MCP protocol
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register services
builder.Services.AddSingleton<IProcessManager, ProcessManager>();
builder.Services.AddSingleton<IIpcBridge, NamedPipeBridge>();
builder.Services.AddSingleton<McpServer>();

var host = builder.Build();

// Get the MCP server and run it
var mcpServer = host.Services.GetRequiredService<McpServer>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("WpfVisualTreeMcp Server starting...");

try
{
    await mcpServer.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error in MCP server");
    Environment.Exit(1);
}
