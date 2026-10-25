using WpfVisualTreeMcp.Automation.Infrastructure;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

/// <summary>Readiness polling before delivery. An input already delivered is never automatically repeated.</summary>
public sealed record ActionOptions
{
    public bool AutoWait { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    internal void Validate() => new WaitOptions { Timeout = Timeout, PollInterval = PollInterval }.Validate();
}

public sealed partial class WpfElement
{
    private ActionOptions? _actionOptions;
    private Func<IIpcBridge, Task<string?>>? _customResolver;

    internal static WpfElement FromResolver(WpfElement root, Func<IIpcBridge, Task<string?>> resolver) =>
        new(root.Application, null, "dynamic control", null) { _customResolver = resolver, _actionOptions = root._actionOptions };

    public WpfElement WithActionOptions(ActionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options); options.Validate();
        return new(Application, _automationId, _handle, _root) { _actionOptions = options, _customResolver = _customResolver };
    }

    private async Task<T> OnActionAsync<T>(Func<IIpcBridge, string, Task<T>> operation, CancellationToken token, WpfElement? destination = null)
    {
        var options = _actionOptions ?? Application.DefaultActionOptions;
        options.Validate();
        if (!options.AutoWait) return await OnElementAsync(operation, token).ConfigureAwait(false);
        T result = default!;
        var last = "element not found";
        await PollAsync(async bridge =>
        {
            var handle = await ReadyHandleAsync(bridge).ConfigureAwait(false);
            if (handle == null || destination != null && await destination.ReadyHandleAsync(bridge).ConfigureAwait(false) == null)
            { last = "element or drag destination is missing, hidden or disabled"; return false; }
            last = "action response (input may already have been delivered)";
            result = await operation(bridge, handle).ConfigureAwait(false);
            if (result is ControlOperationResponse { NotReady: true } pending)
            { last = pending.Reason ?? "control items are not ready"; return false; }
            return true;
        }, () => last, new WaitOptions { Timeout = options.Timeout, PollInterval = options.PollInterval }, token).ConfigureAwait(false);
        return result;
    }

    private async Task<string?> ReadyHandleAsync(IIpcBridge bridge)
    {
        var handle = await TryResolveAsync(bridge).ConfigureAwait(false);
        if (handle == null) return null;
        var properties = await bridge.GetElementPropertiesAsync(handle).ConfigureAwait(false);
        return string.Equals(PropertyValue(properties, "System.Windows.UIElement.IsVisible"), "true", StringComparison.OrdinalIgnoreCase)
            && string.Equals(PropertyValue(properties, "System.Windows.UIElement.IsEnabled"), "true", StringComparison.OrdinalIgnoreCase) ? handle : null;
    }

    internal async Task<ControlResult> ControlAsync(ControlOperationRequest request, CancellationToken token)
    {
        request.Validate();
        var response = await OnActionAsync((b, h) => { request.ElementHandle = h; return b.ControlOperationAsync(request); }, token).ConfigureAwait(false);
        if (response.NotReady) throw new WpfAutomationException(response.Reason ?? "Control is not ready.");
        return response.Result;
    }

    internal async Task<string?> ResolveControlAsync(IIpcBridge bridge, ControlOperationRequest request)
    {
        var handle = await TryResolveAsync(bridge).ConfigureAwait(false);
        if (handle == null) return null;
        request.ElementHandle = handle;
        var response = await bridge.ControlOperationAsync(request).ConfigureAwait(false);
        return response.NotReady ? null : response.Result.ElementHandle;
    }

    public Task<ControlResult> CheckAsync(CancellationToken cancellationToken = default) => SetCheckedAsync(true, cancellationToken);
    public Task<ControlResult> UncheckAsync(CancellationToken cancellationToken = default) => SetCheckedAsync(false, cancellationToken);
    public Task<ControlResult> SetCheckedAsync(bool? value, CancellationToken cancellationToken = default) =>
        ControlAsync(new() { Operation = "check", Checked = value }, cancellationToken);
    public Task<ControlResult> ExpandAsync(CancellationToken cancellationToken = default) => ControlAsync(new() { Operation = "expand" }, cancellationToken);
    public Task<ControlResult> CollapseAsync(CancellationToken cancellationToken = default) => ControlAsync(new() { Operation = "collapse" }, cancellationToken);
    public Task<ControlResult> SelectExactlyAsync(string text, CancellationToken cancellationToken = default) =>
        SelectItemsAsync(new[] { ItemLocator.ByText(text) }, cancellationToken: cancellationToken);
    /// <summary>Select exact items. Replace with an empty list clears selection. Add/remove require multi-selection.</summary>
    public Task<ControlResult> SelectItemsAsync(IEnumerable<ItemLocator> items, SelectionChange change = SelectionChange.Replace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!Enum.IsDefined(change)) throw new ArgumentOutOfRangeException(nameof(change));
        return ControlAsync(new() { Operation = "select", Items = items.ToList(), SelectionMode = change.ToString().ToLowerInvariant() }, cancellationToken);
    }
    public WpfDataGrid AsDataGrid() => new(this);
    public WpfTreeView AsTreeView() => new(this);
}

public enum SelectionChange { Replace, Add, Remove }
