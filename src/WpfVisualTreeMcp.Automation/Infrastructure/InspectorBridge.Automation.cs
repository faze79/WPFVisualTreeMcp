using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Automation.Infrastructure;

public partial class InspectorBridge
{
    private async Task EnsureAdvancedAutomationAsync(bool subscriptions = false)
    {
        var capabilities = await SendRequestAsync<GetCapabilitiesRequest, GetCapabilitiesResponse>(EnsureConnected().ProcessId, new());
        if (!capabilities.Success || !(subscriptions ? capabilities.PropertySubscriptions : capabilities.AdvancedAutomation))
            throw CreateException("This operation requires Inspector 0.19 or later. Restart the target application and attach with the current SDK.");
    }

    public async Task<ControlOperationResponse> ControlOperationAsync(ControlOperationRequest request)
    {
        request.Validate();
        await EnsureAdvancedAutomationAsync().ConfigureAwait(false);
        var response = await SendRequestAsync<ControlOperationRequest, ControlOperationResponse>(EnsureConnected().ProcessId, request);
        if (!response.Success) throw CreateException(response.Error ?? "WPF control operation failed.");
        return response;
    }

    public async Task<WindowResponse> WindowAsync(WindowRequest request)
    {
        await EnsureAdvancedAutomationAsync().ConfigureAwait(false);
        var response = await SendRequestAsync<WindowRequest, WindowResponse>(EnsureConnected().ProcessId, request);
        if (!response.Success) throw CreateException(response.Error ?? "WPF window operation failed.");
        return response;
    }

    public async Task<PropertySubscription> ObservePropertyAsync(string elementHandle, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        await EnsureAdvancedAutomationAsync(true).ConfigureAwait(false);
        return await PropertySubscription.ConnectAsync(EnsureConnected().ProcessId, elementHandle, propertyName, _cancellation.Value).ConfigureAwait(false);
    }

    public async Task UnwatchPropertyAsync(string watchId)
    {
        var response = await SendRequestAsync<UnwatchPropertyRequest, UnwatchPropertyResponse>(EnsureConnected().ProcessId, new() { WatchId = watchId });
        if (!response.Success) throw CreateException(response.Error ?? "Cannot remove property watch.");
    }
}
