using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Inspector;

public partial class IpcServer
{
    private async Task StreamPropertyAsync(NamedPipeServerStream pipe, JsonElement data, CancellationToken shutdown)
    {
        var request = IpcSerializer.DeserializeRequestData<SubscribePropertyRequest>(data)!;
        if (!Guid.TryParseExact(request.WatchId, "N", out _)) throw new ArgumentException("Invalid WatchId.");
        var queue = new ConcurrentQueue<string>();
        using var ready = new SemaphoreSlim(0);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        // Framework pipe cancellation is not reliable without closing the handle.
        using var closeOnShutdown = shutdown.Register(() => pipe.Dispose());
        var queued = 0;
        var overflow = 0;
        var registered = false;
        var subscriptionLock = new object();
        var active = true;
        void OnNotification(string json)
        {
            var change = IpcSerializer.Deserialize<PropertyChangedNotification>(json);
            if (change?.WatchId != request.WatchId) return;
            lock (subscriptionLock)
            {
                if (!active || overflow != 0) return;
                if (Interlocked.Increment(ref queued) > 1024) Interlocked.Exchange(ref overflow, 1);
                else queue.Enqueue(IpcSerializer.Serialize(new PropertyStreamFrame { Change = change }));
                ready.Release();
            }
        }
        NotificationReady += OnNotification;
        try
        {
            // Listener installed before registration: changes immediately following the initial value are retained.
            var watch = new WatchPropertyRequest { WatchId = request.WatchId, ElementHandle = request.ElementHandle, PropertyName = request.PropertyName };
            using var watchData = JsonDocument.Parse(IpcSerializer.Serialize(watch));
            var response = await _requestHandler("WatchProperty", watchData.RootElement).ConfigureAwait(false);
            registered = response.Success;
            await WriteFrameAsync(pipe, IpcSerializer.SerializeResponse(response), lifetime.Token).ConfigureAwait(false);
            if (!registered) return;
            var disconnected = pipe.ReadAsync(new byte[1], 0, 1, lifetime.Token);
            while (!shutdown.IsCancellationRequested)
            {
                var available = ready.WaitAsync(lifetime.Token);
                if (await Task.WhenAny(available, disconnected).ConfigureAwait(false) == disconnected) return;
                await available.ConfigureAwait(false);
                if (Volatile.Read(ref overflow) != 0)
                {
                    await WriteFrameAsync(pipe, IpcSerializer.Serialize(new PropertyStreamFrame
                    { Error = "Property notification buffer exceeded 1024 changes; subscription terminated. Consume changes faster." }), lifetime.Token).ConfigureAwait(false);
                    return;
                }
                if (queue.TryDequeue(out var frame))
                {
                    Interlocked.Decrement(ref queued);
                    await WriteFrameAsync(pipe, frame, lifetime.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            NotificationReady -= OnNotification;
            lock (subscriptionLock) active = false;
            lifetime.Cancel();
            pipe.Dispose();
            if (registered)
            {
                try
                {
                    using var unwatch = JsonDocument.Parse(IpcSerializer.Serialize(new UnwatchPropertyRequest { WatchId = request.WatchId }));
                    await _requestHandler("UnwatchProperty", unwatch.RootElement).ConfigureAwait(false);
                }
                catch (Exception ex) { DebugLog("Subscription cleanup: " + ex.Message); }
            }
        }
    }

    private static async Task WriteFrameAsync(Stream pipe, string json, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var closeOnTimeout = deadline.Token.Register(() => pipe.Dispose());
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await pipe.WriteAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false);
        await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
    }
}
