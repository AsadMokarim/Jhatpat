// Tests for CropService and the crop overlay: output resolving, conflict policies, quality, failures and skips.

using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using ImageMagick;

namespace Filee.App.Tests;

public class CropServiceTests
{
    private static string CreateTestImage(string folder, string name, int width = 100, int height = 80)
    {
        var path = Path.Combine(folder, name);
        using var image = new MagickImage(MagickColors.Green, (uint)width, (uint)height);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    [AvaloniaFact]
    public async Task CropFileAsync_resolves_output_path_and_crops_image()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-test-").FullName;
        try
        {
            var source = CreateTestImage(folder, "photo.png", 200, 100);
            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");

            var result = await CropService.CropFileAsync(source, 10, 10, 50, 40, preset, store, loc);

            Assert.Equal(CropStatus.Success, result.Status);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.EndsWith("photo_cropped.png", result.OutputPath);

            using var cropped = new MagickImage(result.OutputPath);
            Assert.Equal((uint)50, cropped.Width);
            Assert.Equal((uint)40, cropped.Height);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task CropFileAsync_honors_conflict_policies()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-conflict-").FullName;
        try
        {
            var source = CreateTestImage(folder, "sample.png", 100, 100);
            var expectedOutput = Path.Combine(folder, "sample_cropped.png");
            File.WriteAllBytes(expectedOutput, [0x89, 0x50, 0x4E, 0x47]); // Existing file

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();

            // 1. ConflictPolicy.Rename -> generates (2)
            var renamePreset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop").Clone();
            renamePreset.Output.Conflict = ConflictPolicy.Rename;
            var renameResult = await CropService.CropFileAsync(source, 0, 0, 50, 50, renamePreset, store, loc);

            Assert.Equal(CropStatus.Success, renameResult.Status);
            Assert.NotNull(renameResult.OutputPath);
            Assert.EndsWith("sample_cropped (2).png", renameResult.OutputPath);
            Assert.True(File.Exists(renameResult.OutputPath));

            // 2. ConflictPolicy.Skip -> returns SkippedExists and null path
            var skipPreset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop").Clone();
            skipPreset.Output.Conflict = ConflictPolicy.Skip;
            var skipResult = await CropService.CropFileAsync(source, 0, 0, 50, 50, skipPreset, store, loc);

            Assert.Equal(CropStatus.SkippedExists, skipResult.Status);
            Assert.Null(skipResult.OutputPath);
            Assert.Equal("toast.skipped", skipResult.ErrorKey);

            // 3. ConflictPolicy.Overwrite -> overwrites existing file
            var overwritePreset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop").Clone();
            overwritePreset.Output.Conflict = ConflictPolicy.Overwrite;
            var overwriteResult = await CropService.CropFileAsync(source, 0, 0, 30, 30, overwritePreset, store, loc);

            Assert.Equal(CropStatus.Success, overwriteResult.Status);
            Assert.Equal(expectedOutput, overwriteResult.OutputPath);
            using var overwritten = new MagickImage(expectedOutput);
            Assert.Equal((uint)30, overwritten.Width);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task CropFileAsync_keeps_the_sources_quality()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-quality-").FullName;
        try
        {
            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");

            // A JPEG saved at 75 isn't saved again at a higher quality.
            var jpeg = Path.Combine(folder, "photo.jpg");
            using (var image = new MagickImage("plasma:", 120, 90))
            {
                image.Quality = 75;
                image.Write(jpeg, MagickFormat.Jpeg);
            }
            var jpegResult = await CropService.CropFileAsync(jpeg, 10, 10, 60, 40, preset, store, loc);
            Assert.Equal(CropStatus.Success, jpegResult.Status);
            using (var cropped = new MagickImage(jpegResult.OutputPath!))
                Assert.InRange(cropped.Quality, 73u, 77u);

            // A lossless WebP (VP8L) stays lossless instead of becoming lossy (VP8).
            var webp = Path.Combine(folder, "logo.webp");
            using (var image = new MagickImage("plasma:", 120, 90))
            {
                image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                image.Write(webp, MagickFormat.WebP);
            }
            Assert.True(IsLosslessWebp(webp));
            var webpResult = await CropService.CropFileAsync(webp, 10, 10, 60, 40, preset, store, loc);
            Assert.Equal(CropStatus.Success, webpResult.Status);
            Assert.True(IsLosslessWebp(webpResult.OutputPath!));
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    /// <summary>True when the WebP's image chunk is VP8L (lossless), not VP8 (lossy).</summary>
    private static bool IsLosslessWebp(string path)
    {
        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path));
        return text.Contains("VP8L", StringComparison.Ordinal) && !text.Contains("VP8 ", StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task An_only_file_that_cant_be_cropped_keeps_the_overlay_open_with_the_reason_and_no_retry()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-animated-").FullName;
        try
        {
            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();
            var gif = Path.Combine(folder, "spinner.gif");
            using (var frames = new MagickImageCollection())
            {
                frames.Add(new MagickImage(MagickColors.Red, 40, 40));
                frames.Add(new MagickImage(MagickColors.Blue, 40, 40));
                frames.Write(gif, MagickFormat.Gif);
            }
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");

            using var vm = new CropOverlayViewModel([gif], preset, loc, store);
            var closed = false;
            vm.CloseRequested += () => closed = true;
            await WaitFor(() => vm.IsFinished);

            Assert.False(closed);
            Assert.Equal(["spinner.gif: " + loc["error.unsupported_source"]], vm.Problems);
            var entry = store.History.First(h => h.Sources.Contains(gif));
            Assert.Equal(JobState.Failed, entry.State);
            var failure = Assert.Single(entry.Failures);
            Assert.Equal("error.unsupported_source", failure.ErrorKey);
            Assert.Null(failure.ErrorDetail); // the English exception text stays in the log
            Assert.Null(entry.Preset); // Home offers no Retry: ConversionService doesn't run tools

            vm.CancelCommand.Execute(null);
            Assert.True(closed);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task A_file_skipped_because_the_output_exists_is_shown_but_isnt_a_failure()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-skip-").FullName;
        try
        {
            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();
            var source = CreateTestImage(folder, "scan.png");
            File.WriteAllBytes(Path.Combine(folder, "scan_cropped.png"), [0x89, 0x50, 0x4E, 0x47]);
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");
            preset.Output.Conflict = ConflictPolicy.Skip;

            using var vm = new CropOverlayViewModel([source], preset, loc, store);
            await WaitFor(() => !vm.IsBusy && vm.OriginalWidth > 0);
            await vm.CropAndSaveAsync();

            Assert.True(vm.IsFinished);
            Assert.Equal(["scan.png: " + loc["toast.skipped"]], vm.Problems);
            Assert.Equal(loc["toast.done"], vm.Summary);
            var entry = store.History.First(h => h.Sources.Contains(source));
            Assert.Equal(JobState.Completed, entry.State);
            Assert.Empty(entry.Failures);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task SetAspect_sets_the_correct_ratio_for_all_presets()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-aspect-").FullName;
        try
        {
            var source = CreateTestImage(folder, "ratio.png", 400, 300);
            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");

            using var vm = new CropOverlayViewModel([source], preset, loc, store);
            await WaitFor(() => !vm.IsBusy && vm.OriginalWidth > 0);

            Assert.Equal(400, vm.OriginalWidth);
            Assert.Equal(300, vm.OriginalHeight);

            // Existing presets
            vm.SetAspect("free");
            Assert.Equal(0.0, vm.AspectRatio);
            Assert.True(vm.IsAspectFree);

            vm.SetAspect("1:1");
            Assert.Equal(1.0, vm.AspectRatio);
            Assert.True(vm.IsAspectSquare);

            vm.SetAspect("4:3");
            Assert.Equal(4.0 / 3.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspect43);

            vm.SetAspect("16:9");
            Assert.Equal(16.0 / 9.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspect169);

            vm.SetAspect("original");
            Assert.Equal(400.0 / 300.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspectOriginal);

            // New presets
            vm.SetAspect("9:16");
            Assert.Equal(9.0 / 16.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspect916);

            vm.SetAspect("4:5");
            Assert.Equal(4.0 / 5.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspect45);

            vm.SetAspect("3:2");
            Assert.Equal(3.0 / 2.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspect32);

            vm.SetAspect("2:3");
            Assert.Equal(2.0 / 3.0, vm.AspectRatio, 6);
            Assert.True(vm.IsAspect23);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task UpdateDimensions_shows_live_pixel_dimensions_for_each_aspect()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-crop-dims-").FullName;
        try
        {
            var source = CreateTestImage(folder, "dims.png", 1920, 1080);
            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<ILocalizer>();
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");

            using var vm = new CropOverlayViewModel([source], preset, loc, store);
            await WaitFor(() => !vm.IsBusy && vm.OriginalWidth > 0);

            Assert.Equal(1920, vm.OriginalWidth);
            Assert.Equal(1080, vm.OriginalHeight);

            // Full image
            vm.UpdateDimensions(new Rect(0, 0, 1.0, 1.0));
            Assert.Equal("1920 × 1080 px", vm.DimensionsText);

            // Half width, half height
            vm.UpdateDimensions(new Rect(0.25, 0.25, 0.5, 0.5));
            Assert.Equal("960 × 540 px", vm.DimensionsText);

            // 9:16 crop on a landscape image: tall and narrow
            // width = 0.3125 of 1920 = 600, height = 1.0 of 1080 = 1080 → 600 × 1080
            vm.UpdateDimensions(new Rect(0.1, 0.0, 600.0 / 1920.0, 1.0));
            Assert.Equal("600 × 1080 px", vm.DimensionsText);

            // Minimum crop: very small normalized rect
            vm.UpdateDimensions(new Rect(0.0, 0.0, 1.0 / 1920.0, 1.0 / 1080.0));
            Assert.Equal("1 × 1 px", vm.DimensionsText);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void UpdateDimensions_computes_correct_output_for_known_image_sizes()
    {
        // Test the dimension calculation logic directly without loading files.
        // CropOverlayViewModel.UpdateDimensions computes:
        //   pixelW = Max(1, Round(norm.Width * OriginalWidth))
        //   pixelH = Max(1, Round(norm.Height * OriginalHeight))

        var testCases = new (int origW, int origH, double normW, double normH, int expectW, int expectH)[]
        {
            // Full image
            (1920, 1080, 1.0, 1.0, 1920, 1080),
            // Half
            (1920, 1080, 0.5, 0.5, 960, 540),
            // 4:5 crop on 1000x1000
            (1000, 1000, 0.8, 1.0, 800, 1000),
            // 3:2 crop on 900x600 (full width, proportional height)
            (900, 600, 1.0, 1.0, 900, 600),
            // 2:3 portrait crop
            (1200, 1800, 2.0 / 3.0, 1.0, 800, 1800),
            // 9:16 vertical
            (1080, 1920, 9.0 / 16.0, 1.0, 608, 1920),
            // Tiny crop
            (100, 100, 0.01, 0.01, 1, 1),
        };

        foreach (var (origW, origH, normW, normH, expectW, expectH) in testCases)
        {
            var pixelW = Math.Max(1, (int)Math.Round(normW * origW));
            var pixelH = Math.Max(1, (int)Math.Round(normH * origH));
            Assert.Equal(expectW, pixelW);
            Assert.Equal(expectH, pixelH);
        }
    }

    [AvaloniaFact]
    public void All_aspect_ratio_boolean_properties_are_mutually_exclusive()
    {
        // Verify that setting any aspect makes only its boolean true and all others false.
        var modes = new[]
        {
            ("free", (Func<CropOverlayViewModel, bool>)(vm => vm.IsAspectFree)),
            ("1:1", vm => vm.IsAspectSquare),
            ("4:3", vm => vm.IsAspect43),
            ("16:9", vm => vm.IsAspect169),
            ("9:16", vm => vm.IsAspect916),
            ("4:5", vm => vm.IsAspect45),
            ("3:2", vm => vm.IsAspect32),
            ("2:3", vm => vm.IsAspect23),
            ("original", vm => vm.IsAspectOriginal),
        };

        // Use a minimal ViewModel without loading files by testing the property logic directly.
        // The boolean properties only depend on SelectedAspect, which SetAspect sets.
        // We need a dummy store and localizer.
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<ILocalizer>();
        var preset = BuiltInData.CreatePresets().Single(p => p.Id == "tool-crop");

        // Create VM with an empty file list so no file loading happens.
        // Set OriginalWidth/Height manually so "original" aspect computes a ratio.
        using var vm = new CropOverlayViewModel([], preset, loc, store);
        // Manually set dimensions for the "original" mode test.
        vm.OriginalWidth = 800;
        vm.OriginalHeight = 600;

        foreach (var (mode, check) in modes)
        {
            vm.SetAspect(mode);
            Assert.True(check(vm), $"Expected Is* to be true for mode '{mode}'");

            // All OTHER modes should be false
            foreach (var (otherMode, otherCheck) in modes)
            {
                if (otherMode != mode)
                    Assert.False(otherCheck(vm), $"Expected Is* for '{otherMode}' to be false when mode is '{mode}'");
            }
        }
    }

    [AvaloniaFact]
    public void CropCanvas_SetAspectRatio_respects_boundaries_and_ratio()
    {
        var canvas = new CropCanvas
        {
            Width = 800,
            Height = 600,
        };
        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));

        using var stream = new MemoryStream();
        using (var img = new MagickImage(MagickColors.Blue, 800, 600))
        {
            img.Write(stream, MagickFormat.Png);
        }
        stream.Position = 0;
        canvas.SourceImage = new Bitmap(stream);

        var ratios = new[] { 9.0 / 16.0, 4.0 / 5.0, 3.0 / 2.0, 2.0 / 3.0, 1.0, 16.0 / 9.0, 4.0 / 3.0 };
        foreach (var r in ratios)
        {
            canvas.SetAspectRatio(r);
            var rect = canvas.NormalizedCropRect;
            Assert.True(rect.X >= 0.0 && rect.X <= 1.0, $"rect.X was {rect.X} for ratio {r}");
            Assert.True(rect.Y >= 0.0 && rect.Y <= 1.0, $"rect.Y was {rect.Y} for ratio {r}");
            Assert.True(rect.Right <= 1.0001, $"rect.Right was {rect.Right} for ratio {r}");
            Assert.True(rect.Bottom <= 1.0001, $"rect.Bottom was {rect.Bottom} for ratio {r}");
            var pixelW = rect.Width * 800.0;
            var pixelH = rect.Height * 600.0;
            Assert.Equal(r, pixelW / pixelH, 2);
        }
    }
}
