namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>Local DIP coordinates for physical pointer input.</summary>
public class MousePointerOptions
{
    public string ElementHandle { get; set; } = string.Empty;
    public string Action { get; set; } = "move";
    public double? X { get; set; }
    public double? Y { get; set; }
    public int Delta { get; set; }
    public bool Horizontal { get; set; }
    public int HoverMs { get; set; }

    public static void ValidatePoint(double? x, double? y)
    {
        if (x.HasValue != y.HasValue)
            throw new ArgumentException("Provide both x and y in local WPF DIPs, or omit both for the center.");
        foreach (var value in new[] { x, y })
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0))
                throw new ArgumentException("Local input coordinates must be finite and non-negative.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ElementHandle)) throw new ArgumentException("element_handle is required.");
        ValidatePoint(X, Y);
        if (Action is not ("move" or "scroll")) throw new ArgumentException("Pointer action must be move or scroll.");
        if (HoverMs is < 0 or > 5000) throw new ArgumentException("hover_ms must be between 0 and 5000.");
        if (Action == "scroll" && (Delta == 0 || Delta is < -12000 or > 12000))
            throw new ArgumentException("delta must be nonzero and between -12000 and 12000 wheel units (120 = one notch).");
        if (Action == "move" && (Delta != 0 || Horizontal))
            throw new ArgumentException("Wheel delta and horizontal are only supported for scroll.");
    }
}

public class MousePointerResult
{
    public string Method { get; set; } = string.Empty;
    public string? ElementType { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Delta { get; set; }
    public bool Horizontal { get; set; }
    public string CoordinateSpace { get; set; } = "screen_pixels";
    public string Detail { get; set; } = "Mouse input sent. Verify the application state; input delivery does not imply acceptance.";
}
