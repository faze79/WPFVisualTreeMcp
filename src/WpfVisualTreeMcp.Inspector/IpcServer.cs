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
        try
        {
            DebugLog("HandleClientAsync: Client connected");

            // Fix for .NET Framework 4.8: StreamReader/StreamWriter cause deadlocks on NamedPipeServerStream
            // Use direct byte I/O instead
            var buffer = new byte[4096];
            var stringBuilder = new StringBuilder();

            DebugLog("HandleClientAsync: Entering read loop with direct byte I/O");

            while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    stringBuilder.Clear();
                    DebugLog("HandleClientAsync: Reading from pipe...");

                    // Read until we get a newline
                    while (true)
                    {
                        var bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        DebugLog($"HandleClientAsync: Read {bytesRead} bytes");

                        if (bytesRead == 0)
                        {
                            DebugLog("HandleClientAsync: Client disconnected (0 bytes read)");
                            return;
                        }

                        var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        stringBuilder.Append(text);

                        // Check if we have a complete line
                        var currentText = stringBuilder.ToString();
                        var newlineIndex = currentText.IndexOf('\n');
                        if (newlineIndex >= 0)
                        {
                            var line = currentText.Substring(0, newlineIndex).TrimEnd('\r');

                            // Remove UTF-8 BOM if present (0xEF 0xBB 0xBF = U+FEFF)
                            if (line.Length > 0 && line[0] == '\uFEFF')
                            {
                                line = line.Substring(1);
                                DebugLog("HandleClientAsync: Removed UTF-8 BOM from line");
                            }

                            DebugLog($"HandleClientAsync: Received line (length={line.Length})");

                            // Process the request
                            DebugLog("HandleClientAsync: Processing request...");
                            var response = await ProcessRequestAsync(line);
                            DebugLog($"HandleClientAsync: Response ready (length={response.Length})");

                            // Send response
                            var responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                            DebugLog($"HandleClientAsync: Writing {responseBytes.Length} bytes...");
                            await pipeServer.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                            await pipeServer.FlushAsync(cancellationToken);
                            DebugLog("HandleClientAsync: Response sent");

                            // Keep any remaining data for next line
                            if (newlineIndex + 1 < currentText.Length)
                            {
                                stringBuilder.Clear();
                                stringBuilder.Append(currentText.Substring(newlineIndex + 1));
                            }
                            else
                            {
                                stringBuilder.Clear();
                            }

                            break; // Exit inner loop to read next request
                        }
                    }
                }
                catch (IOException ex)
                {
                    DebugLog($"HandleClientAsync: IOException - {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    DebugLog($"HandleClientAsync: Exception - {ex.Message}\nStack: {ex.StackTrace}");
                    try
                    {
                        var errorResponse = new GetVisualTreeResponse
                        {
                            Success = false,
                            Error = ex.Message
                        };
                        var errorJson = IpcSerializer.SerializeResponse(errorResponse) + "\n";
                        var errorBytes = Encoding.UTF8.GetBytes(errorJson);
                        await pipeServer.WriteAsync(errorBytes, 0, errorBytes.Length, cancellationToken);
                        await pipeServer.FlushAsync(cancellationToken);
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            DebugLog("HandleClientAsync: Connection closed");
        }
        catch (Exception ex)
        {
            DebugLog($"HandleClientAsync: FATAL ERROR - {ex.Message}\nStack: {ex.StackTrace}");
        }
    }

    private static void DebugLog(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WpfInspector_Debug.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
            // Ignore logging errors
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
