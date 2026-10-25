using FluentAssertions;
using WpfVisualTreeMcp.Automation;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Ipc;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class PropertyStreamTests
{
    [Fact]
    public async Task Unconsumed_notification_overflow_is_explicit_and_cleans_up()
    {
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pid = Random.Shared.Next(100000000, int.MaxValue);
        using var server = new IpcServer(pid, (type, data) =>
        {
            if (type == "WatchProperty") return Task.FromResult<IpcResponse>(new WatchPropertyResponse
            { WatchId = IpcSerializer.DeserializeRequestData<WatchPropertyRequest>(data)!.WatchId! });
            removed.TrySetResult(); return Task.FromResult<IpcResponse>(new UnwatchPropertyResponse());
        });
        server.Start();
        await using var watch = await PropertySubscription.ConnectAsync(pid, "handle", "Text", CancellationToken.None);
        for (var i = 0; i < 10000; i++)
            server.SendNotification(IpcSerializer.Serialize(new PropertyChangedNotification
            { WatchId = watch.WatchId, PropertyName = "Text", NewValue = i.ToString() }));
        // Either bounded queue may overflow first; both terminate and unregister explicitly.
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var consume = async () => { await foreach (var change in watch.ReadAllAsync()) { } };
        await consume.Should().ThrowAsync<WpfAutomationException>().WithMessage("*buffer*1024*");
    }

    [Fact]
    public async Task Retains_change_during_registration_and_unwatches_on_client_disconnect()
    {
        var removed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pid = Random.Shared.Next(100000000, int.MaxValue);
        IpcServer server = null!;
        using (server = new IpcServer(pid, (type, data) =>
        {
            if (type == "WatchProperty")
            {
                var request = IpcSerializer.DeserializeRequestData<WatchPropertyRequest>(data)!;
                // Deliberately notify before the registration response reaches the client.
                server.SendNotification(IpcSerializer.Serialize(new PropertyChangedNotification
                { WatchId = request.WatchId!, PropertyName = "Text", OldValue = "initial", NewValue = "next" }));
                return Task.FromResult<IpcResponse>(new WatchPropertyResponse { WatchId = request.WatchId!, InitialValue = "initial" });
            }
            removed.TrySetResult(IpcSerializer.DeserializeRequestData<UnwatchPropertyRequest>(data)!.WatchId);
            return Task.FromResult<IpcResponse>(new UnwatchPropertyResponse());
        }))
        {
            server.Start();
            await using var watch = await PropertySubscription.ConnectAsync(pid, "handle", "Text", CancellationToken.None);
            watch.InitialValue.Should().Be("initial");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var changes = watch.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
            (await changes.MoveNextAsync()).Should().BeTrue();
            changes.Current.NewValue.Should().Be("next");
            await watch.DisposeAsync();
            (await removed.Task.WaitAsync(timeout.Token)).Should().Be(watch.WatchId);
        }
    }

    [Fact]
    public async Task Unexpected_server_disconnect_is_an_error_and_still_removes_the_watch()
    {
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pid = Random.Shared.Next(100000000, int.MaxValue);
        using var server = new IpcServer(pid, (type, data) =>
        {
            if (type == "WatchProperty") return Task.FromResult<IpcResponse>(new WatchPropertyResponse
            { WatchId = IpcSerializer.DeserializeRequestData<WatchPropertyRequest>(data)!.WatchId! });
            removed.TrySetResult(); return Task.FromResult<IpcResponse>(new UnwatchPropertyResponse());
        });
        server.Start();
        await using var watch = await PropertySubscription.ConnectAsync(pid, "handle", "Text", CancellationToken.None);
        server.Stop();
        var consume = async () => { await foreach (var change in watch.ReadAllAsync()) { } };
        await consume.Should().ThrowAsync<Exception>();
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
