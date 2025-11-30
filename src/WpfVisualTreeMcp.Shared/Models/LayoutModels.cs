namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Size information.
/// </summary>
public class SizeInfo
{
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Thickness information (margins, padding).
/// </summary>
public class ThicknessInfo
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }
}

/// <summary>
/// Layout information for an element.
/// </summary>
public class LayoutInfo
{
    public double ActualWidth { get; set; }
    public double ActualHeight { get; set; }
    public SizeInfo DesiredSize { get; set; } = new();
    public SizeInfo RenderSize { get; set; } = new();
    public ThicknessInfo Margin { get; set; } = new();
    public ThicknessInfo? Padding { get; set; }
    public string HorizontalAlignment { get; set; } = "Stretch";
    public string VerticalAlignment { get; set; } = "Stretch";
    public string Visibility { get; set; } = "Visible";
}

/// <summary>
/// Result of a layout info query.
/// </summary>
public class LayoutInfoResult
{
    public ElementInfo Element { get; set; } = new();
    public LayoutInfo Layout { get; set; } = new();
}

/// <summary>
/// Result of a tree export.
/// </summary>
public class ExportResult
{
    public string Format { get; set; } = "json";
    public string Content { get; set; } = string.Empty;
    public int ElementCount { get; set; }
}
