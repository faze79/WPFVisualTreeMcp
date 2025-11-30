using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Named pipe server for receiving inspection requests from the MCP server.
/// </summary>
public class IpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<string, JsonElement, Task<IpcResponse>> _requestHandler;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    public event Action<string>? NotificationReady;

    public IpcServer(int processId, Func<string, JsonElement, Task<IpcResponse>> requestHandler)
    {
        _pipeName = $"wpf_inspector_{processId}";
        _requestHandler = requestHandler;
    }

    public string PipeName => _pipeName;

    public void Start()
    {
        if (_serverTask != null) return;

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
    }

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
        using var reader = new StreamReader(pipeServer, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipeServer, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

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
                break;
            }
            catch (Exception ex)
            {
                var errorResponse = new GetVisualTreeResponse
                {
                    Success = false,
                    Error = ex.Message
                };
                try
                {
                    await writer.WriteLineAsync(IpcSerializer.SerializeResponse(errorResponse));
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private async Task<string> ProcessRequestAsync(string requestJson)
    {
        var parsed = IpcSerializer.DeserializeRequest(requestJson);
        if (parsed == null)
        {
            return IpcSerializer.SerializeResponse(new GetVisualTreeResponse
            {
                Success = false,
                Error = "Invalid request format"
            });
        }

        var (type, data) = parsed.Value;
        var response = await _requestHandler(type, data);
        return IpcSerializer.SerializeResponse(response);
    }

    public void SendNotification(string notificationJson)
    {
        NotificationReady?.Invoke(notificationJson);
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
