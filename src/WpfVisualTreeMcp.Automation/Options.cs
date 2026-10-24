namespace WpfVisualTreeMcp.Automation;

/// <summary>Filters combine with AND. AutomationId is exact and case-sensitive.</summary>
public sealed class ElementQuery
{
    public string? AutomationId { get; init; }
    public string? TypeName { get; init; }
    public string? Name { get; init; }
    public string? Text { get; init; }
    public Dictionary<string, string>? Properties { get; init; }
    public bool VisibleOnly { get; init; }
    public int MaxResults { get; init; } = 50;
}

/// <summary>Coordinates are local WPF device-independent pixels.</summary>
public readonly record struct LocalPoint(double X, double Y);
public enum MouseButton { Left, Right, Middle }
public enum ClickKind { Single, Double, Right }
public enum ElementCondition { Exists, Visible, Enabled, Hidden }

public sealed class DragSettings
{
    public LocalPoint? Start { get; init; }
    public LocalPoint? Destination { get; init; }
    public MouseButton Button { get; init; } = MouseButton.Left;
    public int DurationMs { get; init; } = 500;
}

public sealed class TouchSettings
{
    public LocalPoint? Position { get; init; }
    public double? Radius { get; init; }
    public int? DurationMs { get; init; }
}

public sealed class WaitOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    internal void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Timeout));
        if (PollInterval <= TimeSpan.Zero || PollInterval > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
    }
}
