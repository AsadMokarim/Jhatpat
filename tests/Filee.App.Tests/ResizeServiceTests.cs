using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Presets;
using Filee.Core.Settings;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class ResizeServiceTests
{
    private static SKBitmap CreateBitmap(int width, int height, SKColor color, bool transparentBackground = false)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        if (transparentBackground)
        {
            canvas.Clear(SKColors.Transparent);
            // Draw a circle in the center with the color
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        else
        {
            canvas.Clear(color);
        }
        return bmp;
    }

    [Fact]
    public void CalculateDimensions_preserves_aspect_ratio_when_width_changes()
    {
        // Source 1600 x 900 (16:9)
        var (w, h) = ResizeService.CalculateDimensions(
            1600, 900,
            requestedWidth: 800, requestedHeight: null,
            maintainAspectRatio: true, widthChangedLast: true);

        Assert.Equal(800, w);
        Assert.Equal(450, h);
    }

    [Fact]
    public void CalculateDimensions_preserves_aspect_ratio_when_height_changes()
    {
        // Source 1200 x 800 (3:2)
        var (w, h) = ResizeService.CalculateDimensions(
            1200, 800,
            requestedWidth: null, requestedHeight: 400,
            maintainAspectRatio: true, widthChangedLast: false);

        Assert.Equal(600, w);
        Assert.Equal(400, h);
    }

    [Fact]
    public void CalculateDimensions_allows_independent_dimensions_when_aspect_ratio_unlocked()
    {
        var (w, h) = ResizeService.CalculateDimensions(
            1000, 1000,
            requestedWidth: 1280, requestedHeight: 720,
            maintainAspectRatio: false);

        Assert.Equal(1280, w);
        Assert.Equal(720, h);
    }

    [Fact]
    public void CalculateDimensions_clamps_within_min_and_max_bounds()
    {
        var (wMin, hMin) = ResizeService.CalculateDimensions(
            100, 100,
            requestedWidth: 2, requestedHeight: 2,
            maintainAspectRatio: false);

        Assert.Equal(ResizeService.MinDimension, wMin);
        Assert.Equal(ResizeService.MinDimension, hMin);

        var (wMax, hMax) = ResizeService.CalculateDimensions(
            100, 100,
            requestedWidth: 50000, requestedHeight: 50000,
            maintainAspectRatio: false);

        Assert.Equal(ResizeService.MaxDimension, wMax);
        Assert.Equal(ResizeService.MaxDimension, hMax);
    }

    [Theory]
    [InlineData(1000, 800, 0.25, 250, 200)]
    [InlineData(1000, 800, 0.50, 500, 400)]
    [InlineData(1000, 800, 0.75, 750, 600)]
    [InlineData(1000, 800, 1.00, 1000, 800)]
    public void ScaleDimensions_scales_percentages_accurately(
        int srcW, int srcH, double factor, int expectedW, int expectedH)
    {
        var (w, h) = ResizeService.ScaleDimensions(srcW, srcH, factor);
        Assert.Equal(expectedW, w);
        Assert.Equal(expectedH, h);
    }

    [Theory]
    [InlineData(@"photo.png", "same", "png")]
    [InlineData(@"photo.jpg", "same", "jpg")]
    [InlineData(@"photo.jpeg", "same", "jpg")]
    [InlineData(@"photo.png", "jpg", "jpg")]
    [InlineData(@"photo.png", "webp", "webp")]
    [InlineData(@"photo.webp", "png", "png")]
    public void ResolveFormat_resolves_expected_extensions(string input, string option, string expected)
    {
        Assert.Equal(expected, ResizeService.ResolveFormat(input, option));
    }

    [Theory]
    [InlineData("jpg", true)]
    [InlineData("jpeg", true)]
    [InlineData("webp", true)]
    [InlineData("png", false)]
    [InlineData("bmp", false)]
    [InlineData("tiff", false)]
    public void IsLossyFormat_identifies_lossy_codecs(string format, bool expected)
    {
        Assert.Equal(expected, ResizeService.IsLossyFormat(format));
    }

    [Fact]
    public void FormatFileSize_formats_readable_sizes()
    {
        Assert.Equal("500 B", ResizeService.FormatFileSize(500));
        Assert.Equal("1.5 KB", ResizeService.FormatFileSize(1536));
        Assert.Equal("2.50 MB", ResizeService.FormatFileSize((long)(2.5 * 1024 * 1024)));
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_exports_resized_image_without_overwriting_original()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-resize-test-").FullName;
        try
        {
            var tempSource = Path.Combine(folder, "sample.png");
            using (var bmp = CreateBitmap(400, 200, SKColors.Crimson))
            using (var s = File.OpenWrite(tempSource))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var options = new ResizeOptions
            {
                TargetWidth = 200,
                TargetHeight = 100,
                TargetFormat = "same",
            };

            var result = await ResizeService.ResizeFileAsync(tempSource, options, preset, store, loc);

            Assert.Equal(ResizeStatus.Success, result.Status);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.NotEqual(tempSource, result.OutputPath);
            Assert.True(result.OutputPath.Contains("_compressed") || result.OutputPath.Contains("_resized"));
            Assert.True(File.Exists(tempSource), "Original must be preserved");

            using var outBmp = SKBitmap.Decode(result.OutputPath);
            Assert.Equal(200, outBmp.Width);
            Assert.Equal(100, outBmp.Height);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_lossy_compression_reduces_file_size()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-compress-test-").FullName;
        try
        {
            var tempSource = Path.Combine(folder, "photo.jpg");
            using (var bmp = CreateBitmap(800, 600, SKColors.DodgerBlue))
            using (var s = File.OpenWrite(tempSource))
            {
                bmp.Encode(s, SKEncodedImageFormat.Jpeg, 95);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var highOptions = new ResizeOptions
            {
                TargetWidth = 800,
                TargetHeight = 600,
                TargetFormat = "jpg",
                Quality = 95,
            };
            var highResult = await ResizeService.ResizeFileAsync(tempSource, highOptions, preset, store, loc, customOutputPath: Path.Combine(folder, "high.jpg"));

            var lowOptions = new ResizeOptions
            {
                TargetWidth = 800,
                TargetHeight = 600,
                TargetFormat = "jpg",
                Quality = 30,
            };
            var lowResult = await ResizeService.ResizeFileAsync(tempSource, lowOptions, preset, store, loc, customOutputPath: Path.Combine(folder, "low.jpg"));

            Assert.Equal(ResizeStatus.Success, highResult.Status);
            Assert.Equal(ResizeStatus.Success, lowResult.Status);

            var highSize = new FileInfo(highResult.OutputPath!).Length;
            var lowSize = new FileInfo(lowResult.OutputPath!).Length;

            Assert.True(lowSize < highSize, $"Low quality size ({lowSize}) should be smaller than high quality size ({highSize})");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_flattens_transparency_to_white_when_exporting_jpeg()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-transparency-test-").FullName;
        try
        {
            var tempSource = Path.Combine(folder, "transparent.png");
            using (var bmp = CreateBitmap(100, 100, SKColors.Blue, transparentBackground: true))
            using (var s = File.OpenWrite(tempSource))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var options = new ResizeOptions
            {
                TargetWidth = 100,
                TargetHeight = 100,
                TargetFormat = "jpg",
                Quality = 90,
            };

            var result = await ResizeService.ResizeFileAsync(tempSource, options, preset, store, loc);
            Assert.Equal(ResizeStatus.Success, result.Status);
            Assert.NotNull(result.OutputPath);

            using var outBmp = SKBitmap.Decode(result.OutputPath);
            Assert.Equal(100, outBmp.Width);
            Assert.Equal(100, outBmp.Height);

            // Corner pixel (2, 2) that was transparent in PNG should be flattened to near white in JPEG
            var corner = outBmp.GetPixel(2, 2);
            Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240, $"Corner pixel should be white background, got {corner}");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ResizeOverlayViewModel_syncs_dimensions_presets_and_format()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-vm-resize-").FullName;
        try
        {
            var tempSource = Path.Combine(folder, "icon.png");
            using (var bmp = CreateBitmap(800, 400, SKColors.Green, transparentBackground: true))
            using (var s = File.OpenWrite(tempSource))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            using var vm = new ResizeOverlayViewModel([tempSource], preset, loc, store);
            await vm.LoadFileAsync(0);

            Assert.Equal(800, vm.OriginalWidth);
            Assert.Equal(400, vm.OriginalHeight);
            Assert.Equal(800, vm.TargetWidth);
            Assert.Equal(400, vm.TargetHeight);
            Assert.True(vm.MaintainAspectRatio);
            Assert.True(vm.HasTransparency);

            // 1. Changing width updates height with aspect ratio locked
            vm.TargetWidth = 400;
            Assert.Equal(200, vm.TargetHeight);
            Assert.True(vm.IsScale50);

            // 2. Preset scale 25%
            vm.SetScaleCommand.Execute(0.25);
            Assert.Equal(200, vm.TargetWidth);
            Assert.Equal(100, vm.TargetHeight);
            Assert.True(vm.IsScale25);

            // 3. Unlock aspect ratio
            vm.ToggleAspectRatioCommand.Execute(null);
            Assert.False(vm.MaintainAspectRatio);
            vm.TargetWidth = 300;
            Assert.Equal(100, vm.TargetHeight); // Height does not change when unlocked

            // 4. Format selection
            vm.SetFormatCommand.Execute("jpg");
            Assert.Equal("jpg", vm.TargetFormat);
            Assert.True(vm.IsLossySupported);
            Assert.True(vm.WillLoseTransparency); // PNG source with transparency converting to JPEG

            // 5. Quality preset selection
            vm.SetQualityCommand.Execute(60);
            Assert.True(vm.IsQualityLow);
            Assert.Equal(60, vm.Quality);

            vm.SetQualityCommand.Execute(80);
            Assert.True(vm.IsQualityBalanced);
            Assert.Equal(80, vm.Quality);

            // 6. Reset
            vm.ResetCommand.Execute(null);
            Assert.Equal(800, vm.TargetWidth);
            Assert.Equal(400, vm.TargetHeight);
            Assert.True(vm.MaintainAspectRatio);
            Assert.Equal("same", vm.TargetFormat);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Activating_resize_from_image_tools_routes_to_resize_overlay()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var vm = AppHost.Get<RadialViewModel>();

        var folder = Directory.CreateTempSubdirectory("filee-radial-resize-").FullName;
        try
        {
            var tempSource = Path.Combine(folder, "test.png");
            using (var bmp = CreateBitmap(100, 100, SKColors.Orange))
            using (var s = File.OpenWrite(tempSource))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            vm.Load([tempSource], clickMode: true);

            var toolsIndex = vm.Items.ToList().FindIndex(i => i.Id == "image-tools");
            Assert.True(toolsIndex >= 0, "Image tools group should be present in radial menu");
            Assert.True(vm.Items[toolsIndex].IsPromoted, "Image tools group should be promoted (larger slice)");
            Assert.Equal("Image Options", vm.Items[toolsIndex].Label);

            var resizeIndex = vm.Items[toolsIndex].Children!.ToList().FindIndex(c => c.Id == "tool-resize");
            Assert.True(resizeIndex >= 0, "tool-resize should be present in image tools submenu");
            Assert.Equal("Compression", vm.Items[toolsIndex].Children![resizeIndex].Label);

            radial.Convert(toolsIndex, resizeIndex, [tempSource]);

            Assert.NotNull(windows.ResizeOverlay);
            var resizeVm = Assert.IsType<ResizeOverlayViewModel>(windows.ResizeOverlay.DataContext);
            Assert.True(resizeVm.IsEditing);

            windows.ResizeOverlay.Close();
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_rejects_empty_file()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-resize-empty-").FullName;
        try
        {
            var emptyFile = Path.Combine(folder, "empty.png");
            File.WriteAllBytes(emptyFile, []);

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var result = await ResizeService.ResizeFileAsync(emptyFile, new ResizeOptions(), preset, store, loc);
            Assert.Equal(ResizeStatus.Failed, result.Status);
            Assert.Equal("error.empty_file", result.ErrorKey);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_rejects_missing_file()
    {
        TestServices.EnsureInitialized("en");
        var missingFile = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
        var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

        var result = await ResizeService.ResizeFileAsync(missingFile, new ResizeOptions(), preset, store, loc);
        Assert.Equal(ResizeStatus.Failed, result.Status);
        Assert.Equal("error.file_not_found", result.ErrorKey);
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_prevents_source_file_overwrite()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-resize-overwrite-").FullName;
        try
        {
            var source = Path.Combine(folder, "source.png");
            using (var bmp = CreateBitmap(100, 100, SKColors.Green))
            using (var s = File.OpenWrite(source))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }
            var initialBytes = File.ReadAllBytes(source);

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var result = await ResizeService.ResizeFileAsync(
                source,
                new ResizeOptions { TargetWidth = 50, TargetHeight = 50 },
                preset,
                store,
                loc,
                customOutputPath: source);

            Assert.Equal(ResizeStatus.Failed, result.Status);
            Assert.Equal("error.overwrite_source_prevented", result.ErrorKey);

            // Source file must remain untouched
            Assert.True(File.Exists(source));
            var currentBytes = File.ReadAllBytes(source);
            Assert.Equal(initialBytes, currentBytes);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ResizeFileAsync_uses_atomic_staging_and_cleans_temporary_files()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-resize-atomic-").FullName;
        try
        {
            var source = Path.Combine(folder, "source.png");
            using (var bmp = CreateBitmap(100, 100, SKColors.Green))
            using (var s = File.OpenWrite(source))
            {
                bmp.Encode(s, SKEncodedImageFormat.Png, 100);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var result = await ResizeService.ResizeFileAsync(
                source,
                new ResizeOptions { TargetWidth = 50, TargetHeight = 50 },
                preset,
                store,
                loc);

            Assert.Equal(ResizeStatus.Success, result.Status);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));

            // Ensure no leftover .tmp_* files exist in the folder
            var strayTemps = Directory.GetFiles(folder, ".tmp_*");
            Assert.Empty(strayTemps);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ResizeOverlayViewModel_universal_compression_on_png_and_reversed_bar_fix()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-resize-universal-").FullName;
        try
        {
            var sourcePath = Path.Combine(folder, "test.png");
            using (var bmp = CreateBitmap(400, 300, SKColors.DodgerBlue))
            using (var stream = File.OpenWrite(sourcePath))
            {
                bmp.Encode(stream, SKEncodedImageFormat.Png, 100);
            }

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            using var vm = new ResizeOverlayViewModel([sourcePath], preset, loc, store);

            // Universal compression active on PNG (not hidden)
            Assert.True(vm.IsLossySupported);

            // Setting compression to 20% (Low compression)
            vm.SetCompressionCommand.Execute(20);
            Assert.Equal(20, vm.Compression);
            Assert.True(vm.IsQualityLow);
            Assert.False(vm.IsQualityHigh);
            var lowQuality = vm.Quality;

            // Setting compression to 80% (High compression)
            vm.SetCompressionCommand.Execute(80);
            Assert.Equal(80, vm.Compression);
            Assert.True(vm.IsQualityHigh);
            Assert.False(vm.IsQualityLow);
            var highQuality = vm.Quality;

            // Higher compression means smaller quality number (stronger compression)
            Assert.True(highQuality < lowQuality);

            // Setting compression to 50% (Balanced)
            vm.SetCompressionCommand.Execute(50);
            Assert.Equal(50, vm.Compression);
            Assert.True(vm.IsQualityBalanced);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
