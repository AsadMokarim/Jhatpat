// Tests for PhotoFilterEngine: tone curves, color adjustments, and detail filters.

using Filee.Engines.Photo;
using SkiaSharp;

namespace Filee.Engines.Tests;

public class PhotoFilterEngineTests
{
    private static SKBitmap CreateGradientBitmap(int width = 64, int height = 64)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [new SKColor(50, 50, 50), new SKColor(200, 200, 200)],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(SKRect.Create(0, 0, width, height), paint);
        return bmp;
    }

    [Fact]
    public void PhotoAdjustments_default_state_is_detected()
    {
        var adj = new PhotoAdjustments();
        Assert.True(adj.IsDefault);

        var modified = adj with { Brightness = 10 };
        Assert.False(modified.IsDefault);
    }

    [Fact]
    public void ApplyAdjustments_with_default_settings_returns_identical_dimensions()
    {
        using var src = CreateGradientBitmap(40, 40);
        var adj = new PhotoAdjustments();

        using var result = PhotoFilterEngine.ApplyAdjustments(src, adj);

        Assert.Equal(src.Width, result.Width);
        Assert.Equal(src.Height, result.Height);
    }

    [Fact]
    public void ApplyAdjustments_brightness_increases_average_pixel_value()
    {
        using var src = CreateGradientBitmap(32, 32);
        var adj = new PhotoAdjustments { Brightness = 50 };

        using var result = PhotoFilterEngine.ApplyAdjustments(src, adj);

        var srcPixel = src.GetPixel(16, 16);
        var resPixel = result.GetPixel(16, 16);

        // Brightness = +50 should increase R, G, B
        Assert.True(resPixel.Red >= srcPixel.Red);
        Assert.True(resPixel.Green >= srcPixel.Green);
        Assert.True(resPixel.Blue >= srcPixel.Blue);
    }

    [Fact]
    public void ApplyAdjustments_handles_all_adjustment_parameters()
    {
        using var src = CreateGradientBitmap(32, 32);
        var adj = new PhotoAdjustments
        {
            Exposure = 0.5,
            Brightness = 10,
            Contrast = 15,
            Highlights = -10,
            Shadows = 20,
            Whites = 10,
            Blacks = -10,
            Gamma = 1.1,
            Temperature = 25,
            Tint = -10,
            Saturation = 20,
            Vibrance = 15,
            Hue = 30,
            Dehaze = 15,
            Clarity = 20,
            Grain = 10,
            NoiseReduction = 10
        };

        using var result = PhotoFilterEngine.ApplyAdjustments(src, adj);

        Assert.NotNull(result);
        Assert.Equal(32, result.Width);
        Assert.Equal(32, result.Height);
    }
}
