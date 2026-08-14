using FluentAssertions;
using System.Threading;
using System.Windows;
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
    public void BuildOffsets_HugeSurface_StopsAtTileLimitSentinel()
    {
        var offsets = ScreenshotCapture.BuildOffsets(1_000_000, 1);

        offsets.Should().HaveCount(401);
        offsets[^1].Should().Be(400);
        ScreenshotCapture.ExceedsCaptureTileLimit(offsets.Count, 1).Should().BeTrue();
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
    public void CalculateFullContentScale_LargeRequestedOutput_BoundsEncodedArea()
    {
        var scale = ScreenshotCapture.CalculateFullContentScale(
            16384, 16384, 16384, 16384);

        var width = (int)(16384 * scale);
        var height = (int)(16384 * scale);
        width.Should().Be(2896);
        height.Should().Be(2896);
        ((long)width * height).Should().BeLessOrEqualTo(
            ScreenshotCapture.MaxFullContentOutputPixelCount);
        scale.Should().BeLessThan(1.0);
    }

    [Fact]
    public void BuildRenderTiles_LargeSurface_BoundsEachRenderAndCoversSurface()
    {
        var tiles = ScreenshotCapture.BuildRenderTiles(4096, 2048);

        tiles.Should().HaveCountGreaterThan(1);
        tiles.Should().OnlyContain(tile => (long)tile.Width * tile.Height <= 1024 * 1024);
        tiles.Sum(tile => (long)tile.Width * tile.Height).Should().Be(4096L * 2048);
    }

    [Fact]
    public void EncodeFullContentPng_EncodedOutputOverBudget_ThrowsBeforeBase64Allocation()
    {
        var bitmap = CreateBitmap(16, 10, 16);

        var act = () => ScreenshotCapture.EncodeFullContentPng(
            bitmap, bitmap.PixelWidth, bitmap.PixelHeight, CancellationToken.None, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*encoded PNG exceeds the 1-byte memory budget*");
    }

    [Fact]
    public void CaptureFullContent_CanceledBeforeStart_ThrowsExecutionDeadlineError()
    {
        var capture = new ScreenshotCapture();
        var cancellationToken = new CancellationToken(canceled: true);

        var act = () => capture.CaptureFullContent(
            new UIElement(), 100, 100, cancellationToken);

        act.Should().Throw<TimeoutException>()
            .WithMessage("Full-content capture exceeded its execution deadline.");
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
