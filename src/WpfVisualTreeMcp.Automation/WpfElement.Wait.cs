using WpfVisualTreeMcp.Automation.Infrastructure;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

public sealed partial class WpfElement
{
    /// <summary>Wait for the exact, case-sensitive Text property value. Missing elements are retried; ambiguous IDs fail.</summary>
    public Task WaitForTextAsync(string expectedText, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedText);
        return WaitForPropertyAsync("Text", expectedText, new WaitOptions { Timeout = timeout ?? TimeSpan.FromSeconds(10) }, cancellationToken);
    }

    /// <summary>Wait for an exact invariant string property value. A null expectedValue matches a null property.</summary>
    public Task WaitForPropertyAsync(string propertyName, string? expectedValue, WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        string last = "element not found";
        return PollAsync(async bridge =>
        {
            var handle = await TryResolveAsync(bridge).ConfigureAwait(false);
            if (handle == null) { last = "element not found"; return false; }
            var value = PropertyValue(await bridge.GetElementPropertiesAsync(handle).ConfigureAwait(false), propertyName);
            last = value == null ? "null" : $"'{value}'";
            return string.Equals(value, expectedValue, StringComparison.Ordinal);
        }, () => $"{propertyName} == {(expectedValue == null ? "null" : "'" + expectedValue + "'")}; last observed: {last}", options, cancellationToken);
    }

    /// <summary>Poll fresh properties until the predicate returns true. Predicates must be fast and side-effect free.</summary>
    public Task WaitUntilAsync(Func<ElementPropertiesResult, bool> predicate, WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return PollAsync(async bridge =>
        {
            var handle = await TryResolveAsync(bridge).ConfigureAwait(false);
            return handle != null && predicate(await bridge.GetElementPropertiesAsync(handle).ConfigureAwait(false));
        }, () => "the property predicate to become true", options, cancellationToken);
    }

    public Task WaitForAsync(ElementCondition condition = ElementCondition.Visible, WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(condition)) throw new ArgumentOutOfRangeException(nameof(condition));
        return PollAsync(async bridge =>
        {
            var handle = await TryResolveAsync(bridge).ConfigureAwait(false);
            if (handle == null) return condition == ElementCondition.Hidden;
            if (condition == ElementCondition.Exists) return true;
            var properties = await bridge.GetElementPropertiesAsync(handle).ConfigureAwait(false);
            bool visible = string.Equals(PropertyValue(properties, "System.Windows.UIElement.IsVisible"), "true", StringComparison.OrdinalIgnoreCase);
            return condition == ElementCondition.Hidden ? !visible : visible &&
                (condition != ElementCondition.Enabled || string.Equals(PropertyValue(properties, "System.Windows.UIElement.IsEnabled"), "true", StringComparison.OrdinalIgnoreCase));
        }, () => condition.ToString(), options, cancellationToken);
    }

    private async Task<string?> TryResolveAsync(IIpcBridge bridge)
    {
        if (_customResolver != null) return await _customResolver(bridge).ConfigureAwait(false);
        if (_handle != null) return _handle;
        var root = _root == null ? null : await _root.TryResolveAsync(bridge).ConfigureAwait(false);
        if (_root != null && root == null) return null; // Never broaden a missing scoped root to all windows.
        return await bridge.TryResolveAutomationIdAsync(_automationId!, root).ConfigureAwait(false);
    }

    private async Task PollAsync(Func<IIpcBridge, Task<bool>> predicate, Func<string> description, WaitOptions? options, CancellationToken token)
    {
        options ??= new WaitOptions();
        options.Validate();
        using var deadline = new CancellationTokenSource(options.Timeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try
        {
            while (true)
            {
                combined.Token.ThrowIfCancellationRequested();
                if (await Application.RunAsync(predicate, combined.Token).ConfigureAwait(false)) return;
                // Release the session gate between polls, so another operation may make the condition true.
                await Task.Delay(options.PollInterval, combined.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException($"Process {Application.ProcessId}: timed out after {options.Timeout} waiting for {this}: {description()}.");
        }
    }
}
