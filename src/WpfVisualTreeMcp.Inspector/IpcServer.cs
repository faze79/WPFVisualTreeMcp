using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Named pipe server for receiving inspection requests from the MCP server.
/// </summary>
public class IpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<string, string?, Task<string>> _requestHandler;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    /// <summary>
    /// Creates a new IPC server for the specified process.
    /// </summary>
    /// <param name="processId">The process ID to use in the pipe name.</param>
    /// <param name="requestHandler">Handler for incoming requests.</param>
    public IpcServer(int processId, Func<string, string?, Task<string>> requestHandler)
    {
        _pipeName = $"wpf_inspector_{processId}";
        _requestHandler = requestHandler;
    }

    /// <summary>
    /// Starts the IPC server.
    /// </summary>
    public void Start()
    {
        if (_serverTask != null) return;

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
    }

    /// <summary>
    /// Stops the IPC server.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Task was cancelled
        }

        _serverTask = null;
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                await HandleClientAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IPC server error: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipeServer, Encoding.UTF8);
        using var writer = new StreamWriter(pipeServer, Encoding.UTF8) { AutoFlush = true };

        while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                var response = await ProcessRequestAsync(line);
                await writer.WriteLineAsync(response);
            }
            catch (IOException)
            {
                break; // Client disconnected
            }
            catch (Exception ex)
            {
                var errorResponse = $"{{\"error\": \"{EscapeJson(ex.Message)}\"}}";
                try
                {
                    await writer.WriteLineAsync(errorResponse);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private async Task<string> ProcessRequestAsync(string request)
    {
        // Parse simple request format: TYPE|PARAMS
        var parts = request.Split(new[] { '|' }, 2);
        var requestType = parts[0];
        var parameters = parts.Length > 1 ? parts[1] : null;

        return await _requestHandler(requestType, parameters);
    }

    private static string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _cts?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
