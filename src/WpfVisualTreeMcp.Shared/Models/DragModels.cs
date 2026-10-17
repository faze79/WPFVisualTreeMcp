namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>A physical mouse drag. Coordinates are local WPF device-independent pixels.</summary>
public class DragOptions
{
    public string ElementHandle { get; set; } = string.Empty;
    public string? TargetHandle { get; set; }
    public double? DeltaX { get; set; }
    public double? DeltaY { get; set; }
    public double? StartX { get; set; }
    public double? StartY { get; set; }
    public double? TargetX { get; set; }
    public double? TargetY { get; set; }
    public string Button { get; set; } = "left";
    public int DurationMs { get; set; } = 500;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ElementHandle))
            throw new ArgumentException("element_handle is required.");
        if (TargetHandle != null && string.IsNullOrWhiteSpace(TargetHandle))
            throw new ArgumentException("target_handle must not be empty.");
        bool hasTarget = TargetHandle != null;
        bool hasDelta = DeltaX.HasValue || DeltaY.HasValue;
        if (hasTarget == hasDelta)
            throw new ArgumentException("Provide either target_handle or delta_x/delta_y, exclusively.");
        if (!hasTarget && DeltaX.GetValueOrDefault() == 0 && DeltaY.GetValueOrDefault() == 0)
            throw new ArgumentException("The drag displacement must be non-zero.");
        if (StartX.HasValue != StartY.HasValue || TargetX.HasValue != TargetY.HasValue)
            throw new ArgumentException("Provide both coordinates of start_x/start_y or target_x/target_y.");
        if (!hasTarget && TargetX.HasValue)
            throw new ArgumentException("target_x/target_y require target_handle.");
        foreach (var coordinate in new[] { DeltaX, DeltaY, StartX, StartY, TargetX, TargetY })
            if (coordinate.HasValue && (double.IsNaN(coordinate.Value) || double.IsInfinity(coordinate.Value)))
                throw new ArgumentException("Drag coordinates must be finite numbers.");
        if (StartX < 0 || StartY < 0 || TargetX < 0 || TargetY < 0)
            throw new ArgumentException("Local start and target coordinates must be non-negative.");
        if (Button != "left" && Button != "right" && Button != "middle")
            throw new ArgumentException("button must be 'left', 'right', or 'middle'.");
        if (DurationMs < 100 || DurationMs > 10000)
            throw new ArgumentException("duration_ms must be between 100 and 10000.");
    }
}

/// <summary>Reports input delivery, not whether the application accepted a drop.</summary>
public class DragResult
{
    public string Method { get; set; } = "Physical.Drag";
    public string? ElementType { get; set; }
    public string Button { get; set; } = "left";
    public int DurationMs { get; set; }
    public int StartX { get; set; }
    public int StartY { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    public string Detail { get; set; } = "Mouse input sent. Verify the resulting application state; drop acceptance is not inferred.";
}
