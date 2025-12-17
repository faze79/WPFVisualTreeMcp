using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Serilog;
using Serilog.Events;
using WpfVisualTreeMcp.Server.Services;

// Configure Serilog for file logging
var logFilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WpfVisualTreeMcp",
    "logs",
    "mcp-server-.log"
);

// Ensure log directory exists
var logDir = Path.GetDirectoryName(logFilePath);
if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
{
    Directory.CreateDirectory(logDir);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        standardErrorFromLevel: LogEventLevel.Verbose, // CRITICAL: Write to stderr for MCP stdio protocol
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .WriteTo.File(
        logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

try
{
    Log.Information("WPF Visual Tree MCP Server starting up");
    Log.Information("Log file: {LogFile}", logFilePath.Replace("-.log", $"-{DateTime.Now:yyyyMMdd}.log"));

    // Build the host with MCP server and dependency injection
    var builder = Host.CreateApplicationBuilder(args);

    // CRITICAL: stdout must be completely clean for MCP stdio protocol
    // Use Serilog for logging (writes to stderr AND file)
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

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

    Log.Information("WPF Visual Tree MCP Server shutting down gracefully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "WPF Visual Tree MCP Server terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
