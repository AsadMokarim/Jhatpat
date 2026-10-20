using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.Core.Presets;
using Filee.Core.Settings;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class FramingServiceTests
{
    private static SKBitmap CreateBitmap(int width, int height, SKColor color)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    [Fact]
    public void CalculateLayout_original_aspect_ratio_adds_padding_evenly()
    {
        var options = new FramingOptions
        {
            Padding = 50,
            AspectRatio = "original",
        };

        var (canvasW, canvasH, targetX, targetY, targetW, targetH) =
            FramingService.CalculateLayout(200, 100, options);

        Assert.Equal(300, canvasW);
        Assert.Equal(200, canvasH);
        Assert.Equal(50, targetX);
        Assert.Equal(50, targetY);
        Assert.Equal(200, targetW);
        Assert.Equal(100, targetH);
    }

    [Theory]
    [InlineData("1:1", 200, 100, 50, 300, 300)]
    [InlineData("16:9", 160, 90, 0, 160, 90)]
    [InlineData("4:5", 100, 200, 20, 192, 240)]
    public void CalculateLayout_preset_aspect_ratios_meet_ratio_geometry(
        string ratio, int srcW, int srcH, int pad, int expectedW, int expectedH)
    {
        var options = new FramingOptions
        {
            Padding = pad,
            AspectRatio = ratio,
        };

        var (canvasW, canvasH, _, _, targetW, targetH) =
            FramingService.CalculateLayout(srcW, srcH, options);

        Assert.Equal(expectedW, canvasW);
        Assert.Equal(expectedH, canvasH);
        Assert.Equal(srcW, targetW);
        Assert.Equal(srcH, targetH);

        double actualRatio = (double)canvasW / canvasH;
        double targetRatio = FramingService.RatioValueOf(ratio);
        Assert.True(Math.Abs(actualRatio - targetRatio) < 0.02, $"Ratio {actualRatio} should match {targetRatio}");
    }

    [Fact]
    public void CalculateLayout_custom_dimensions_scales_and_centers_correctly()
    {
        var options = new FramingOptions
        {
            Padding = 20,
            AspectRatio = "custom",
            CustomWidth = 500,
            CustomHeight = 500,
        };

        var (canvasW, canvasH, targetX, targetY, targetW, targetH) =
            FramingService.CalculateLayout(400, 200, options);

        Assert.Equal(500, canvasW);
        Assert.Equal(500, canvasH);
        Assert.Equal(400, targetW);
        Assert.Equal(200, targetH);
        Assert.Equal(50, targetX);
        Assert.Equal(150, targetY);
    }

    [Fact]
    public void CalculateLayout_throws_on_invalid_dimensions()
    {
        var options = new FramingOptions();
        Assert.Throws<ArgumentException>(() => FramingService.CalculateLayout(0, 100, options));
        Assert.Throws<ArgumentException>(() => FramingService.CalculateLayout(100, -1, options));
    }

    [Fact]
    public void RenderFramedBitmap_solid_background_and_padding_pixel_colors()
    {
        using var source = CreateBitmap(100, 100, SKColors.Red);
        var options = new FramingOptions
        {
            Padding = 30,
            BackgroundColor = "#0000FF", // Blue
            BackgroundType = FramingBackgroundType.SolidColor,
            HasShadow = false,
            CornerRadius = 0,
        };

        using var framed = FramingService.RenderFramedBitmap(source, options);

        Assert.Equal(160, framed.Width);
        Assert.Equal(160, framed.Height);

        // Border pixel (10, 10) must be background blue
        var bgPixel = framed.GetPixel(10, 10);
        Assert.Equal(0, bgPixel.Red);
        Assert.Equal(255, bgPixel.Blue);

        // Center pixel (80, 80) must be foreground red
        var centerPixel = framed.GetPixel(80, 80);
        Assert.Equal(255, centerPixel.Red);
        Assert.Equal(0, centerPixel.Blue);
    }

    [Fact]
    public void RenderFramedBitmap_gradient_background()
    {
        using var source = CreateBitmap(60, 60, SKColors.White);
        var options = new FramingOptions
        {
            Padding = 20,
            BackgroundType = FramingBackgroundType.Gradient,
            GradientStartColor = "#FF0000",
            GradientEndColor = "#00FF00",
            HasShadow = false,
        };

        using var framed = FramingService.RenderFramedBitmap(source, options);
        Assert.Equal(100, framed.Width);
        Assert.Equal(100, framed.Height);

        var p0 = framed.GetPixel(2, 2);
        var pEnd = framed.GetPixel(98, 98);
        Assert.NotEqual(p0, pEnd);
    }

    [Fact]
    public void RenderFramedBitmap_transparent_background_preserves_alpha()
    {
        using var source = CreateBitmap(100, 100, SKColors.Yellow);
        var options = new FramingOptions
        {
            Padding = 25,
            BackgroundType = FramingBackgroundType.Transparent,
            HasShadow = false,
        };

        using var framed = FramingService.RenderFramedBitmap(source, options);

        // Outside pixel must be fully transparent
        var outsidePixel = framed.GetPixel(5, 5);
        Assert.Equal(0, outsidePixel.Alpha);

        // Inside pixel must be opaque yellow
        var insidePixel = framed.GetPixel(75, 75);
        Assert.Equal(255, insidePixel.Alpha);
        Assert.Equal(SKColors.Yellow.Red, insidePixel.Red);
        Assert.Equal(SKColors.Yellow.Green, insidePixel.Green);
    }

    [Fact]
    public void RenderFramedBitmap_corner_radius_rounds_inner_edges()
    {
        using var source = CreateBitmap(100, 100, SKColors.Green);
        var options = new FramingOptions
        {
            Padding = 20,
            CornerRadius = 15,
            BackgroundType = FramingBackgroundType.Transparent,
            HasShadow = false,
        };

        using var framed = FramingService.RenderFramedBitmap(source, options);

        // Corner of the inner rect is at (20, 20). Because radius is 15, (20, 20) should be outside the round rect:
        var cornerOfInner = framed.GetPixel(20, 20);
        Assert.Equal(0, cornerOfInner.Alpha);

        // Center of the image at (70, 70) must be green
        var centerOfInner = framed.GetPixel(70, 70);
        Assert.Equal(255, centerOfInner.Alpha);
        Assert.Equal(SKColors.Green.Green, centerOfInner.Green);
    }

    [Fact]
    public void RenderFramedBitmap_image_background()
    {
        var tempBg = Path.Combine(Path.GetTempPath(), $"filee-test-bg-{Guid.NewGuid():N}.png");
        try
        {
            using (var bg = CreateBitmap(100, 100, SKColors.Magenta))
            using (var s = File.OpenWrite(tempBg))
            {
                bg.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            using var source = CreateBitmap(50, 50, SKColors.Cyan);
            var options = new FramingOptions
            {
                Padding = 25,
                BackgroundType = FramingBackgroundType.Image,
                BackgroundImagePath = tempBg,
                HasShadow = false,
            };

            using var framed = FramingService.RenderFramedBitmap(source, options);
            Assert.Equal(100, framed.Width);
            Assert.Equal(100, framed.Height);

            // Margin should be magenta from background image
            var edge = framed.GetPixel(5, 5);
            Assert.Equal(SKColors.Magenta.Red, edge.Red);
            Assert.Equal(SKColors.Magenta.Blue, edge.Blue);
        }
        finally
        {
            if (File.Exists(tempBg))
                File.Delete(tempBg);
        }
    }

    [AvaloniaFact]
    public async Task FrameFileAsync_exports_without_overwriting_original_by_default()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-frame-test-").FullName;
        try
        {
            var tempSource = Path.Combine(folder, "sample.png");
            using (var bmp = CreateBitmap(80, 80, SKColors.Blue))
            using (var s = File.OpenWrite(tempSource))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-frame");

            var options = new FramingOptions { Padding = 20 };
            var result = await FramingService.FrameFileAsync(tempSource, options, preset, store, loc);

            Assert.Equal(FramingStatus.Success, result.Status);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.NotEqual(tempSource, result.OutputPath);
            Assert.Contains("_framed", result.OutputPath);
            Assert.True(File.Exists(tempSource), "Original must not be overwritten or deleted");

            // Verify framed output has padding
            using var outBmp = SKBitmap.Decode(result.OutputPath);
            Assert.Equal(120, outBmp.Width);
            Assert.Equal(120, outBmp.Height);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
