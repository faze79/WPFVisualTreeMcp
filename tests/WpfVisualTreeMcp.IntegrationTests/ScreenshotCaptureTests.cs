using FluentAssertions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class ScreenshotCaptureTests
{
    private const long MaxFullContentPixelCount = 64L * 1024 * 1024;

    [Fact]
    public void ExceedsCaptureTileLimit_LargeCounts_DoesNotOverflow()
    {
        ScreenshotCapture.ExceedsCaptureTileLimit(int.MaxValue, 2).Should().BeTrue();
    }

    [Fact]
    public void CalculateNextRetainedPixelCount_ExceedingAggregateBudget_Throws()
    {
        var act = () => ScreenshotCapture.CalculateNextRetainedPixelCount(
            MaxFullContentPixelCount, 1, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*pixel memory budget*");
    }

    [Fact]
    public void FindVerticalOverlap_MatchingRegionAcrossTiles_ReturnsActualOverlap()
    {
        var previous = CreateBitmap(1030, 10, 80);
        var current = CreateBitmap(1030, 20, 80);

        var overlap = ScreenshotCapture.FindVerticalOverlap(previous, current, 69, 1);

        overlap.Should().Be(70);
    }

    private static BitmapSource CreateBitmap(int width, int firstRowValue, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var value = (byte)(firstRowValue + y);
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        return BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
    }
}
