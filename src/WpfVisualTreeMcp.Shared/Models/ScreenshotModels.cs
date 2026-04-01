namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Result of a screenshot capture.
/// </summary>
public class ScreenshotResult
{
    public string ImageBase64 { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ElementType { get; set; }
}
