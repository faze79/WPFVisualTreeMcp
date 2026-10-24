using WpfVisualTreeMcp.Automation.Infrastructure;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

/// <summary>A locator, resolved again for each operation. AutomationIds survive control recreation.</summary>
public sealed partial class WpfElement
{
    internal WpfApplication Application { get; }
    private readonly string? _automationId;
    private readonly string? _handle;
    private readonly WpfElement? _root;

    internal WpfElement(WpfApplication application, string? automationId, string? handle, WpfElement? root)
    {
        if ((automationId == null) == (handle == null)) throw new ArgumentException("Provide exactly one AutomationId or handle.");
        if (string.IsNullOrWhiteSpace(automationId ?? handle)) throw new ArgumentException("The locator cannot be blank.");
        if (automationId?.Length > 2048) throw new ArgumentException("AutomationId exceeds 2048 characters.");
        Application = application; _automationId = automationId; _handle = handle; _root = root;
    }

    public WpfElement ByAutomationId(string automationId) => new(Application, automationId, null, this);
    public override string ToString() => (_root == null ? "" : _root + " / ") +
        (_automationId == null ? $"Handle='{_handle}'" : $"AutomationId='{_automationId}'");

    internal async Task<string> ResolveAsync(IIpcBridge bridge)
    {
        var root = _root == null ? null : await _root.ResolveAsync(bridge).ConfigureAwait(false);
        return await bridge.ResolveElementHandleAsync(_handle, _automationId, root).ConfigureAwait(false);
    }

    private Task<T> OnElementAsync<T>(Func<IIpcBridge, string, Task<T>> operation, CancellationToken token) =>
        Application.RunAsync(async bridge => await operation(bridge, await ResolveAsync(bridge).ConfigureAwait(false)).ConfigureAwait(false), token);

    private Task OnElementAsync(Func<IIpcBridge, string, Task> operation, CancellationToken token) =>
        Application.RunAsync(async bridge => await operation(bridge, await ResolveAsync(bridge).ConfigureAwait(false)).ConfigureAwait(false), token);

    public Task<string> GetHandleAsync(CancellationToken cancellationToken = default) =>
        Application.RunAsync(ResolveAsync, cancellationToken);

    public Task<ClickResult> ClickAsync(bool physical = false, ClickKind kind = ClickKind.Single, LocalPoint? position = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        MousePointerOptions.ValidatePoint(position?.X, position?.Y);
        return OnElementAsync((b, h) => b.ClickElementAsync(h, physical, kind.ToString().ToLowerInvariant(), position?.X, position?.Y), cancellationToken);
    }

    public Task<MousePointerResult> MoveMouseAsync(LocalPoint? position = null, int hoverMs = 500, CancellationToken cancellationToken = default) =>
        PointerAsync(new MousePointerOptions { ElementHandle = "locator", Action = "move", X = position?.X, Y = position?.Y, HoverMs = hoverMs }, cancellationToken);
    public Task<MousePointerResult> ScrollAsync(int delta, bool horizontal = false, LocalPoint? position = null, CancellationToken cancellationToken = default) =>
        PointerAsync(new MousePointerOptions { ElementHandle = "locator", Action = "scroll", Delta = delta, Horizontal = horizontal, X = position?.X, Y = position?.Y }, cancellationToken);
    private Task<MousePointerResult> PointerAsync(MousePointerOptions options, CancellationToken token)
    {
        options.Validate();
        return OnElementAsync((b, h) => { options.ElementHandle = h; return b.MousePointerAsync(options); }, token);
    }

    public Task<DragResult> DragToAsync(WpfElement destination, DragSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Application.CheckOwner(destination);
        return DragAsync(destination, null, null, settings, cancellationToken);
    }
    public Task<DragResult> DragByAsync(double deltaX, double deltaY = 0, DragSettings? settings = null, CancellationToken cancellationToken = default) =>
        DragAsync(null, deltaX, deltaY, settings, cancellationToken);
    private Task<DragResult> DragAsync(WpfElement? destination, double? dx, double? dy, DragSettings? settings, CancellationToken token)
    {
        settings ??= new DragSettings();
        var options = new DragOptions
        {
            ElementHandle = "source", TargetHandle = destination == null ? null : "destination", DeltaX = dx, DeltaY = dy,
            StartX = settings.Start?.X, StartY = settings.Start?.Y, TargetX = settings.Destination?.X, TargetY = settings.Destination?.Y,
            Button = settings.Button.ToString().ToLowerInvariant(), DurationMs = settings.DurationMs
        };
        options.Validate();
        return Application.RunAsync(async bridge =>
        {
            options.ElementHandle = await ResolveAsync(bridge).ConfigureAwait(false);
            options.TargetHandle = destination == null ? null : await destination.ResolveAsync(bridge).ConfigureAwait(false);
            return await bridge.DragAsync(options).ConfigureAwait(false);
        }, token);
    }

    public Task<TouchGestureResult> TapAsync(TouchSettings? settings = null, CancellationToken cancellationToken = default) =>
        TouchAsync("tap", settings, cancellationToken: cancellationToken);
    public Task<TouchGestureResult> LongPressAsync(TouchSettings? settings = null, CancellationToken cancellationToken = default) =>
        TouchAsync("long_press", settings, cancellationToken: cancellationToken);
    public Task<TouchGestureResult> PanAsync(double deltaX, double deltaY = 0, TouchSettings? settings = null, CancellationToken cancellationToken = default) =>
        TouchAsync("swipe", settings, deltaX, deltaY, cancellationToken: cancellationToken);
    public Task<TouchGestureResult> ZoomAsync(double scale, TouchSettings? settings = null, CancellationToken cancellationToken = default) =>
        TouchAsync("pinch", settings, scale: scale, cancellationToken: cancellationToken);
    public Task<TouchGestureResult> RotateAsync(double angleDegrees, TouchSettings? settings = null, CancellationToken cancellationToken = default) =>
        TouchAsync("rotate", settings, angleDegrees: angleDegrees, cancellationToken: cancellationToken);

    public Task<TouchGestureResult> TouchAsync(string gesture, TouchSettings? settings = null, double? deltaX = null, double? deltaY = null,
        double? scale = null, double? angleDegrees = null, CancellationToken cancellationToken = default)
    {
        settings ??= new TouchSettings();
        var options = new TouchGestureOptions
        {
            ElementHandle = "locator", Gesture = gesture, X = settings.Position?.X, Y = settings.Position?.Y,
            DeltaX = deltaX, DeltaY = deltaY, Scale = scale, AngleDegrees = angleDegrees, Radius = settings.Radius, DurationMs = settings.DurationMs
        };
        options.Validate();
        return OnElementAsync((b, h) => { options.ElementHandle = h; return b.TouchGestureAsync(options); }, cancellationToken);
    }

    public Task<SelectItemResult> SelectItemAsync(string itemText, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
        return OnElementAsync((b, h) => b.SelectItemAsync(h, itemText, null), cancellationToken);
    }
    public Task<SelectItemResult> SelectItemAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return OnElementAsync((b, h) => b.SelectItemAsync(h, null, index), cancellationToken);
    }
    public Task<SetTextResult> SetTextAsync(string text, bool physical = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return OnElementAsync((b, h) => b.SetTextAsync(h, text, physical), cancellationToken);
    }
    public Task<SendKeysResult> SendKeysAsync(string keys, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keys);
        return OnElementAsync((b, h) => b.SendKeysAsync(h, keys), cancellationToken);
    }
    public Task<string?> AssertPropertyAsync(string propertyName, string? expectedValue, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.AssertElementAsync(h, propertyName, expectedValue ?? "{null}"), cancellationToken);
    public Task<string?> AssertTextAsync(string expectedText, CancellationToken cancellationToken = default) =>
        AssertPropertyAsync("Text", expectedText, cancellationToken);
    public Task<SetPropertyResult> SetPropertyAsync(string propertyName, string? value, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.SetPropertyAsync(h, propertyName, value ?? "{null}"), cancellationToken);
    public Task<RevertPropertyResult> RevertPropertyAsync(string? propertyName = null, CancellationToken cancellationToken = default) =>
        OnElementAsync((b, h) => b.RevertPropertyAsync(false, h, propertyName), cancellationToken);
}
