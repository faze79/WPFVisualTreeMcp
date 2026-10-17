namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>Gesture geometry is expressed in local WPF DIPs, before element transforms.</summary>
public class TouchGestureOptions
{
    public string ElementHandle { get; set; } = string.Empty;
    public string Gesture { get; set; } = "tap";
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? DeltaX { get; set; }
    public double? DeltaY { get; set; }
    public double? Scale { get; set; }
    public double? AngleDegrees { get; set; }
    public double? Radius { get; set; }
    public int? DurationMs { get; set; }

    public int EffectiveDurationMs => DurationMs ?? (Gesture == "tap" ? 100 : Gesture == "long_press" ? 1000 : 700);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ElementHandle)) throw new ArgumentException("element_handle is required.");
        if (Gesture is not ("tap" or "long_press" or "swipe" or "pinch" or "rotate"))
            throw new ArgumentException("gesture must be tap, long_press, swipe, pinch, or rotate.");
        foreach (var number in new[] { X, Y, DeltaX, DeltaY, Scale, AngleDegrees, Radius })
            if (number.HasValue && (double.IsNaN(number.Value) || double.IsInfinity(number.Value)))
                throw new ArgumentException("Touch coordinates and parameters must be finite numbers.");
        if (X.HasValue != Y.HasValue || X < 0 || Y < 0)
            throw new ArgumentException("Provide both x and y as non-negative local coordinates, or omit both for the center.");
        if (Gesture == "swipe")
        {
            if (DeltaX.GetValueOrDefault() == 0 && DeltaY.GetValueOrDefault() == 0)
                throw new ArgumentException("swipe requires a non-zero delta_x or delta_y.");
        }
        else if (DeltaX.HasValue || DeltaY.HasValue)
            throw new ArgumentException("delta_x/delta_y are only valid for swipe.");
        if (Gesture == "pinch")
        {
            if (!Scale.HasValue || Scale < 0.1 || Scale > 10 || Scale == 1)
                throw new ArgumentException("pinch requires scale between 0.1 and 10, excluding 1. Values above 1 spread the fingers.");
        }
        else if (Scale.HasValue) throw new ArgumentException("scale is only valid for pinch.");
        if (Gesture == "rotate")
        {
            if (!AngleDegrees.HasValue || AngleDegrees == 0 || Math.Abs(AngleDegrees.Value) > 360)
                throw new ArgumentException("rotate requires a non-zero angle_degrees between -360 and 360 (positive is clockwise).");
        }
        else if (AngleDegrees.HasValue) throw new ArgumentException("angle_degrees is only valid for rotate.");
        if (Radius.HasValue && (Gesture is not ("pinch" or "rotate") || Radius < 4))
            throw new ArgumentException("radius is only valid for pinch/rotate and must be at least 4 DIPs.");
        int minimum = Gesture == "tap" ? 50 : Gesture == "long_press" ? 500 : 100;
        int maximum = Gesture == "tap" ? 500 : 10000;
        if (EffectiveDurationMs < minimum || EffectiveDurationMs > maximum)
            throw new ArgumentException($"duration_ms for {Gesture} must be between {minimum} and {maximum}.");
    }
}

public class TouchContactPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>Successful injection does not imply application gesture recognition.</summary>
public class TouchGestureResult
{
    public string Method { get; set; } = "Physical.Touch";
    public string Gesture { get; set; } = string.Empty;
    public string? ElementType { get; set; }
    public int ContactCount { get; set; }
    public int DurationMs { get; set; }
    public TouchContactPoint[] Start { get; set; } = Array.Empty<TouchContactPoint>();
    public TouchContactPoint[] End { get; set; } = Array.Empty<TouchContactPoint>();
    public string Detail { get; set; } = "Touch input sent. Verify the application state; gesture recognition depends on the application's touch support.";
}
