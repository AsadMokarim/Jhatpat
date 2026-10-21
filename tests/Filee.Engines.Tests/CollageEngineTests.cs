// Tests for CollageEngine: layout calculations and collage rendering.

using Filee.Engines.Photo;
using SkiaSharp;

namespace Filee.Engines.Tests;

public class CollageEngineTests
{
    private static SKBitmap CreateSolidBitmap(int width, int height, SKColor color)
    {
        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    [Theory]
    [InlineData(CollageLayout.Grid, 4)]
    [InlineData(CollageLayout.Row, 3)]
    [InlineData(CollageLayout.Column, 3)]
    [InlineData(CollageLayout.Featured, 3)]
    public void ComputeCellRects_returns_matching_number_of_non_empty_rects(CollageLayout layout, int count)
    {
        var options = new CollageOptions
        {
            Layout = layout,
            CanvasWidth = 1000,
            Spacing = 10,
            Padding = 20
        };

        var rects = CollageEngine.ComputeCellRects(count, 1000, 1000, options);

        Assert.Equal(count, rects.Count);
        foreach (var r in rects)
        {
            Assert.True(r.Width > 0);
            Assert.True(r.Height > 0);
            Assert.True(r.Left >= options.Padding);
            Assert.True(r.Top >= options.Padding);
        }
    }

    [Fact]
    public void RenderCollage_produces_valid_composite_bitmap()
    {
        using var img1 = CreateSolidBitmap(200, 200, SKColors.Crimson);
        using var img2 = CreateSolidBitmap(200, 200, SKColors.DodgerBlue);

        var options = new CollageOptions
        {
            Layout = CollageLayout.Grid,
            CanvasWidth = 800,
            AspectRatio = 1.0,
            Padding = 10,
            Spacing = 10,
            CornerRadius = 16,
            BackgroundColor = SKColors.Black
        };

        using var collage = CollageEngine.RenderCollage([img1, img2], options);

        Assert.NotNull(collage);
        Assert.Equal(800, collage.Width);
        Assert.Equal(800, collage.Height);
    }
}
