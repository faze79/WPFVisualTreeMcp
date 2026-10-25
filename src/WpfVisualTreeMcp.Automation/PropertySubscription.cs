using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Automation;

/// <summary>A live dependency-property subscription on one control instance. Dispose to remove the Inspector handler.</summary>
public sealed class PropertySubscription : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly Channel<PropertyChangedNotification> _changes = Channel.CreateBounded<PropertyChangedNotification>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _receive;
    private int _disposed;
    internal Action? OnDisposed { get; set; }
    public string WatchId { get; }
    public string? InitialValue { get; }
    /// <summary>One ordered stream; use one consumer. Overflow and unexpected disconnect surface as errors.</summary>
    public IAsyncEnumerable<PropertyChangedNotification> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _changes.Reader.ReadAllAsync(cancellationToken);

    private PropertySubscription(NamedPipeClientStream pipe, StreamReader reader, WatchPropertyResponse response)
    {
        _pipe = pipe; _reader = reader; WatchId = response.WatchId; InitialValue = response.InitialValue;
        _receive = ReceiveAsync();
    }

    internal static async Task<PropertySubscription> ConnectAsync(int processId, string handle, string property, CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", $"wpf_inspector_{processId}", PipeDirection.InOut, PipeOptions.Asynchronous);
        var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            var request = new SubscribePropertyRequest { ElementHandle = handle, PropertyName = property };
            var bytes = Encoding.UTF8.GetBytes(IpcSerializer.SerializeRequest(request) + "\n");
            await pipe.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new WpfAutomationException("Inspector disconnected during property subscription.");
            var response = IpcSerializer.Deserialize<WatchPropertyResponse>(line);
            if (response == null || !response.Success || response.WatchId != request.WatchId)
                throw new WpfAutomationException(response?.Error ?? "Invalid property subscription response.");
            return new PropertySubscription(pipe, reader, response);
        }
        catch { reader.Dispose(); pipe.Dispose(); throw; }
    }

    private async Task ReceiveAsync()
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var line = await _reader.ReadLineAsync(_stop.Token).ConfigureAwait(false)
                    ?? throw new WpfAutomationException("Inspector disconnected from the property subscription.");
                var frame = IpcSerializer.Deserialize<PropertyStreamFrame>(line)
                    ?? throw new WpfAutomationException("Invalid property notification.");
                if (frame.Error != null) throw new WpfAutomationException(frame.Error);
                if (frame.Change == null || frame.Change.WatchId != WatchId) throw new WpfAutomationException("Invalid property watch ID.");
                if (!_changes.Writer.TryWrite(frame.Change))
                    throw new WpfAutomationException("Property notification buffer exceeded 1024 changes. Consume changes faster.");
            }
        }
        catch (Exception ex) { if (!_stop.IsCancellationRequested) failure = ex; }
        finally { _changes.Writer.TryComplete(failure); _pipe.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _pipe.Dispose();
        await _receive.ConfigureAwait(false);
        _reader.Dispose();
        OnDisposed?.Invoke();
    }
}
