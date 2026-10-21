using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Presets;
using Filee.Core.Settings;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class BatchResizeTests
{
    private static SKBitmap CreateBitmap(int width, int height, SKColor color, bool transparentBackground = false)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        if (transparentBackground)
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        else
        {
            canvas.Clear(color);
        }
        return bmp;
    }

    private static string SaveBitmap(SKBitmap bmp, string path, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        using var stream = File.OpenWrite(path);
        bmp.Encode(stream, format, quality);
        return path;
    }

    [Theory]
    [InlineData("photo.png", true)]
    [InlineData("PHOTO.JPG", true)]
    [InlineData("image.JPEG", true)]
    [InlineData("graphic.webp", true)]
    [InlineData("icon.bmp", true)]
    [InlineData("scan.tiff", true)]
    [InlineData("doc.pdf", false)]
    [InlineData("archive.zip", false)]
    [InlineData("data.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedImage_identifies_supported_and_unsupported_extensions(string? path, bool expected)
    {
        Assert.Equal(expected, BatchResizeService.IsSupportedImage(path!));
    }

    [Fact]
    public void InspectImage_reads_dimensions_and_filesize_accurately()
    {
        var folder = Directory.CreateTempSubdirectory("filee-batch-inspect-").FullName;
        try
        {
            var filePath = Path.Combine(folder, "sample.png");
            using (var bmp = CreateBitmap(640, 480, SKColors.CornflowerBlue))
            {
                SaveBitmap(bmp, filePath);
            }

            var (width, height, size) = BatchResizeService.InspectImage(filePath);

            Assert.Equal(640, width);
            Assert.Equal(480, height);
            Assert.True(size > 0, "File size must be positive");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void InspectImage_throws_FileNotFoundException_for_missing_file()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");
        Assert.Throws<FileNotFoundException>(() => BatchResizeService.InspectImage(missingPath));
    }

    [Theory]
    [InlineData(1000, 800, 50.0, 500, 400)]
    [InlineData(1000, 800, 25.0, 250, 200)]
    [InlineData(1000, 800, 75.0, 750, 600)]
    [InlineData(1000, 800, 100.0, 1000, 800)]
    [InlineData(1000, 800, 200.0, 2000, 1600)]
    public void CalculateTargetDimensions_ScalePercentage_scales_proportionally(
        int srcW, int srcH, double scalePercent, int expectedW, int expectedH)
    {
        var settings = new BatchResizeSettings
        {
            Mode = BatchResizeMode.ScalePercentage,
            ScalePercent = scalePercent,
        };

        var (w, h) = BatchResizeService.CalculateTargetDimensions(srcW, srcH, settings);

        Assert.Equal(expectedW, w);
        Assert.Equal(expectedH, h);
    }

    [Fact]
    public void CalculateTargetDimensions_FitBoundingBox_constrains_within_box_preserving_aspect_ratio()
    {
        var settings = new BatchResizeSettings
        {
            Mode = BatchResizeMode.FitBoundingBox,
            MaxWidth = 800,
            MaxHeight = 600,
        };

        // Landscape 1600 x 900 (16:9) -> constrained by width (800) -> 800 x 450
        var (w1, h1) = BatchResizeService.CalculateTargetDimensions(1600, 900, settings);
        Assert.Equal(800, w1);
        Assert.Equal(450, h1);

        // Portrait 900 x 1600 (9:16) -> constrained by height (600) -> 338 x 600
        var (w2, h2) = BatchResizeService.CalculateTargetDimensions(900, 1600, settings);
        Assert.True(w2 <= 800);
        Assert.Equal(600, h2);
        Assert.InRange(w2, 336, 340);

        // Smaller than box (400 x 300) -> scales up to fit bounding box
        var (w3, h3) = BatchResizeService.CalculateTargetDimensions(400, 300, settings);
        Assert.Equal(800, w3);
        Assert.Equal(600, h3);
    }

    [Fact]
    public void CalculateTargetDimensions_ExactDimensions_allows_independent_dimensions_when_aspect_ratio_unlocked()
    {
        var settings = new BatchResizeSettings
        {
            Mode = BatchResizeMode.ExactDimensions,
            MaxWidth = 1280,
            MaxHeight = 720,
            MaintainAspectRatio = false,
        };

        var (w, h) = BatchResizeService.CalculateTargetDimensions(1000, 1000, settings);

        Assert.Equal(1280, w);
        Assert.Equal(720, h);
    }

    [Fact]
    public void ResolveDestinationPath_never_overwrites_original_input_file()
    {
        var folder = Directory.CreateTempSubdirectory("filee-batch-dest-").FullName;
        try
        {
            var original = Path.Combine(folder, "image.png");
            File.WriteAllText(original, "dummy");

            var resolved = BatchResizeService.ResolveDestinationPath(
                original,
                targetFormat: "same",
                outputDirectory: null,
                collisionPolicy: FileCollisionPolicy.RenameWithSuffix);

            Assert.NotNull(resolved);
            Assert.NotEqual(Path.GetFullPath(original), Path.GetFullPath(resolved));
            Assert.Equal(Path.Combine(folder, "image_compressed.png"), resolved);

            // Now simulate that the user passes an input file already named image_compressed.png
            var compressedOriginal = Path.Combine(folder, "image_compressed.png");
            File.WriteAllText(compressedOriginal, "dummy2");

            var resolved2 = BatchResizeService.ResolveDestinationPath(
                compressedOriginal,
                targetFormat: "same",
                outputDirectory: null,
                collisionPolicy: FileCollisionPolicy.Overwrite);

            Assert.NotNull(resolved2);
            // Even under Overwrite policy, MUST NOT overwrite the original input file
            Assert.NotEqual(Path.GetFullPath(compressedOriginal), Path.GetFullPath(resolved2));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ResolveDestinationPath_collision_policy_rename_generates_numbered_suffix()
    {
        var folder = Directory.CreateTempSubdirectory("filee-batch-collision-").FullName;
        try
        {
            var original = Path.Combine(folder, "photo.png");
            File.WriteAllText(original, "original");

            // Existing output candidate
            var candidate1 = Path.Combine(folder, "photo_compressed.png");
            File.WriteAllText(candidate1, "existing 1");

            var resolved = BatchResizeService.ResolveDestinationPath(
                original,
                targetFormat: "same",
                outputDirectory: null,
                collisionPolicy: FileCollisionPolicy.RenameWithSuffix);

            Assert.Equal(Path.Combine(folder, "photo_compressed_1.png"), resolved);

            // If photo_compressed_1 also exists, it should increment to _2
            File.WriteAllText(resolved!, "existing 2");
            var resolved2 = BatchResizeService.ResolveDestinationPath(
                original,
                targetFormat: "same",
                outputDirectory: null,
                collisionPolicy: FileCollisionPolicy.RenameWithSuffix);

            Assert.Equal(Path.Combine(folder, "photo_compressed_2.png"), resolved2);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ResolveDestinationPath_collision_policy_skip_returns_null_when_target_exists()
    {
        var folder = Directory.CreateTempSubdirectory("filee-batch-skip-").FullName;
        try
        {
            var original = Path.Combine(folder, "photo.png");
            File.WriteAllText(original, "original");

            var candidate = Path.Combine(folder, "photo_compressed.png");
            File.WriteAllText(candidate, "existing");

            var resolved = BatchResizeService.ResolveDestinationPath(
                original,
                targetFormat: "same",
                outputDirectory: null,
                collisionPolicy: FileCollisionPolicy.Skip);

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ResolveDestinationPath_collision_policy_overwrite_returns_path_when_not_original()
    {
        var folder = Directory.CreateTempSubdirectory("filee-batch-overwrite-").FullName;
        try
        {
            var original = Path.Combine(folder, "photo.png");
            File.WriteAllText(original, "original");

            var candidate = Path.Combine(folder, "photo_compressed.png");
            File.WriteAllText(candidate, "existing");

            var resolved = BatchResizeService.ResolveDestinationPath(
                original,
                targetFormat: "same",
                outputDirectory: null,
                collisionPolicy: FileCollisionPolicy.Overwrite);

            Assert.Equal(candidate, resolved);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessBatchAsync_mixed_queue_processes_valid_files_and_reports_invalid_files_without_aborting()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-batch-mixed-").FullName;
        try
        {
            var validPng = Path.Combine(folder, "pic1.png");
            using (var bmp = CreateBitmap(400, 300, SKColors.Crimson))
                SaveBitmap(bmp, validPng);

            var corruptFile = Path.Combine(folder, "corrupt.png");
            File.WriteAllText(corruptFile, "NOT_AN_IMAGE_CONTENT");

            var missingFile = Path.Combine(folder, "nonexistent.png");

            var validJpg = Path.Combine(folder, "pic2.jpg");
            using (var bmp = CreateBitmap(800, 600, SKColors.SeaGreen))
                SaveBitmap(bmp, validJpg, SKEncodedImageFormat.Jpeg, 90);

            var items = new List<BatchResizeItem>
            {
                new() { SourcePath = validPng },
                new() { SourcePath = corruptFile },
                new() { SourcePath = missingFile },
                new() { SourcePath = validJpg },
            };

            var settings = new BatchResizeSettings
            {
                Mode = BatchResizeMode.ScalePercentage,
                ScalePercent = 50.0,
                TargetFormat = "same",
            };

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var summary = await BatchResizeService.ProcessBatchAsync(
                items,
                settings,
                preset,
                store,
                loc,
                CancellationToken.None);

            // Total 4 items: 2 success (validPng, validJpg), 2 failed (corrupt, missing)
            Assert.Equal(4, summary.TotalCount);
            Assert.Equal(2, summary.SuccessCount);
            Assert.Equal(2, summary.FailedCount);
            Assert.Equal(0, summary.SkippedCount);
            Assert.Equal(0, summary.CancelledCount);

            // Item statuses
            Assert.Equal(BatchItemStatus.Success, items[0].Status);
            Assert.NotNull(items[0].OutputPath);
            Assert.True(File.Exists(items[0].OutputPath));
            using (var outBmp = SKBitmap.Decode(items[0].OutputPath))
            {
                Assert.Equal(200, outBmp.Width);
                Assert.Equal(150, outBmp.Height);
            }

            Assert.Equal(BatchItemStatus.Failed, items[1].Status);
            Assert.NotNull(items[1].ErrorMessage);

            Assert.Equal(BatchItemStatus.Failed, items[2].Status);
            Assert.NotNull(items[2].ErrorMessage);

            Assert.Equal(BatchItemStatus.Success, items[3].Status);
            Assert.NotNull(items[3].OutputPath);
            Assert.True(File.Exists(items[3].OutputPath));
            using (var outBmp2 = SKBitmap.Decode(items[3].OutputPath))
            {
                Assert.Equal(400, outBmp2.Width);
                Assert.Equal(300, outBmp2.Height);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessBatchAsync_supports_cancellation_and_cleans_up_temp_files()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-batch-cancel-").FullName;
        try
        {
            var files = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var file = Path.Combine(folder, $"img_{i}.png");
                using (var bmp = CreateBitmap(300, 300, SKColors.Purple))
                    SaveBitmap(bmp, file);
                files.Add(file);
            }

            var items = files.Select(f => new BatchResizeItem { SourcePath = f }).ToList();
            var settings = new BatchResizeSettings
            {
                Mode = BatchResizeMode.ScalePercentage,
                ScalePercent = 50.0,
            };

            using var cts = new CancellationTokenSource();

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            // Cancel immediately during first progress callback
            var summary = await BatchResizeService.ProcessBatchAsync(
                items,
                settings,
                preset,
                store,
                loc,
                cts.Token,
                onProgress: (current, total) =>
                {
                    if (current >= 1)
                        cts.Cancel();
                });

            // At least one item should be cancelled
            Assert.True(summary.CancelledCount > 0, "Cancelled items count should be greater than 0");
            Assert.Contains(items, i => i.Status == BatchItemStatus.Cancelled);

            // Ensure NO leftover temporary files (.tmp.) in the directory
            var leftoverTmp = Directory.GetFiles(folder, "*.tmp.*");
            Assert.Empty(leftoverTmp);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessBatchAsync_collision_policy_skip_skips_existing_destination()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-batch-skip-test-").FullName;
        try
        {
            var src = Path.Combine(folder, "sample.png");
            using (var bmp = CreateBitmap(400, 400, SKColors.DarkOrange))
                SaveBitmap(bmp, src);

            // Pre-create output path
            var existingOutput = Path.Combine(folder, "sample_compressed.png");
            File.WriteAllText(existingOutput, "already converted");

            var items = new List<BatchResizeItem> { new() { SourcePath = src } };
            var settings = new BatchResizeSettings
            {
                CollisionPolicy = FileCollisionPolicy.Skip,
            };

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var summary = await BatchResizeService.ProcessBatchAsync(
                items,
                settings,
                preset,
                store,
                loc,
                CancellationToken.None);

            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(0, summary.SuccessCount);
            Assert.Equal(BatchItemStatus.Skipped, items[0].Status);
            // Existing output must be unmodified
            Assert.Equal("already converted", File.ReadAllText(existingOutput));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessBatchAsync_respects_custom_output_directory()
    {
        TestServices.EnsureInitialized("en");
        var sourceFolder = Directory.CreateTempSubdirectory("filee-batch-src-").FullName;
        var outputFolder = Directory.CreateTempSubdirectory("filee-batch-out-").FullName;
        try
        {
            var src = Path.Combine(sourceFolder, "sample.png");
            using (var bmp = CreateBitmap(200, 200, SKColors.Teal))
                SaveBitmap(bmp, src);

            var items = new List<BatchResizeItem> { new() { SourcePath = src } };
            var settings = new BatchResizeSettings
            {
                OutputDirectory = outputFolder,
            };

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var summary = await BatchResizeService.ProcessBatchAsync(
                items,
                settings,
                preset,
                store,
                loc,
                CancellationToken.None);

            Assert.Equal(1, summary.SuccessCount);
            Assert.NotNull(items[0].OutputPath);
            Assert.StartsWith(outputFolder, items[0].OutputPath);
            Assert.True(File.Exists(items[0].OutputPath));
        }
        finally
        {
            Directory.Delete(sourceFolder, recursive: true);
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessBatchAsync_transparency_preservation_and_format_conversion()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-batch-trans-").FullName;
        try
        {
            var src = Path.Combine(folder, "transparent.png");
            using (var bmp = CreateBitmap(200, 200, SKColors.Crimson, transparentBackground: true))
                SaveBitmap(bmp, src);

            // 1. Convert to WebP: should preserve transparency
            var itemWebp = new BatchResizeItem { SourcePath = src };
            var webpSettings = new BatchResizeSettings
            {
                TargetFormat = "webp",
                Quality = 85,
            };

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            await BatchResizeService.ProcessBatchAsync(
                [itemWebp],
                webpSettings,
                preset,
                store,
                loc,
                CancellationToken.None);

            Assert.Equal(BatchItemStatus.Success, itemWebp.Status);
            Assert.NotNull(itemWebp.OutputPath);
            Assert.EndsWith(".webp", itemWebp.OutputPath);
            using (var outBmp = SKBitmap.Decode(itemWebp.OutputPath))
            {
                // Top-left pixel should remain transparent (alpha = 0)
                var corner = outBmp.GetPixel(0, 0);
                Assert.Equal(0, corner.Alpha);
            }

            // 2. Convert to JPEG: flattens background with white
            var itemJpg = new BatchResizeItem { SourcePath = src };
            var jpgSettings = new BatchResizeSettings
            {
                TargetFormat = "jpg",
                Quality = 85,
            };

            await BatchResizeService.ProcessBatchAsync(
                [itemJpg],
                jpgSettings,
                preset,
                store,
                loc,
                CancellationToken.None);

            Assert.Equal(BatchItemStatus.Success, itemJpg.Status);
            Assert.NotNull(itemJpg.OutputPath);
            Assert.EndsWith(".jpg", itemJpg.OutputPath);
            using (var outBmp = SKBitmap.Decode(itemJpg.OutputPath))
            {
                var corner = outBmp.GetPixel(0, 0);
                Assert.Equal(255, corner.Alpha);
                // JPEG compression on white is near 255
                Assert.InRange(corner.Red, 240, 255);
                Assert.InRange(corner.Green, 240, 255);
                Assert.InRange(corner.Blue, 240, 255);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void BatchResizeViewModel_queue_management_and_settings()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-vm-test-").FullName;
        try
        {
            var file1 = Path.Combine(folder, "1.png");
            var file2 = Path.Combine(folder, "2.png");
            File.WriteAllText(file1, "dummy");
            File.WriteAllText(file2, "dummy");

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            using var vm = new BatchResizeViewModel([file1], preset, loc, store);

            Assert.Single(vm.Queue);
            Assert.True(vm.HasItems);
            Assert.False(vm.IsQueueEmpty);
            Assert.True(vm.CanStart);

            // Adding duplicate should not duplicate item
            vm.AddFiles([file1]);
            Assert.Single(vm.Queue);

            // Adding file2 should increase count
            vm.AddFiles([file2]);
            Assert.Equal(2, vm.Queue.Count);

            // Mode changes
            vm.SetModeCommand.Execute("Scale");
            Assert.True(vm.IsModeScale);
            Assert.False(vm.IsModeFit);
            Assert.False(vm.IsModeExact);

            vm.SetModeCommand.Execute("Fit");
            Assert.False(vm.IsModeScale);
            Assert.True(vm.IsModeFit);
            Assert.False(vm.IsModeExact);

            vm.SetModeCommand.Execute("Exact");
            Assert.False(vm.IsModeScale);
            Assert.False(vm.IsModeFit);
            Assert.True(vm.IsModeExact);

            // Format changes
            vm.SetFormatCommand.Execute("jpg");
            Assert.True(vm.IsFormatJpg);
            Assert.False(vm.IsFormatPng);

            // Quality changes
            vm.SetQualityCommand.Execute(92);
            Assert.True(vm.IsQualityHigh);

            // Collision policy changes
            vm.SetCollisionPolicyCommand.Execute("Skip");
            Assert.True(vm.IsCollisionSkip);

            // Remove item
            vm.RemoveItemCommand.Execute(vm.Queue[0]);
            Assert.Single(vm.Queue);

            // Clear queue
            vm.ClearQueueCommand.Execute(null);
            Assert.Empty(vm.Queue);
            Assert.False(vm.HasItems);
            Assert.True(vm.IsQueueEmpty);
            Assert.False(vm.CanStart);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void RadialController_multi_file_resize_routes_to_BatchResizeWindow()
    {
        TestServices.EnsureInitialized("en");
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var vm = AppHost.Get<RadialViewModel>();

        var folder = Directory.CreateTempSubdirectory("filee-radial-batch-").FullName;
        try
        {
            var f1 = Path.Combine(folder, "a.png");
            var f2 = Path.Combine(folder, "b.png");
            File.WriteAllText(f1, "1");
            File.WriteAllText(f2, "2");

            vm.Load([f1, f2], clickMode: true);

            var itemsList = vm.Items.ToList();
            var toolsIndex = itemsList.FindIndex(i => i.Id == "image-tools");
            Assert.True(toolsIndex >= 0, "image-tools must be present");

            var toolsItem = itemsList[toolsIndex];
            var childrenList = toolsItem.Children!.ToList();
            var resizeSubIndex = childrenList.FindIndex(c => c.Id == "tool-resize");
            Assert.True(resizeSubIndex >= 0, "tool-resize must be inside image-tools");

            // Convert via sub-segment
            radial.Convert(toolsIndex, resizeSubIndex, vm.Files);

            // Should open BatchResizeWindow instead of single-file ResizeOverlayWindow
            Assert.NotNull(windows.BatchResizeWindow);
            Assert.True(windows.BatchResizeWindow.IsVisible);

            // Cleanup
            windows.BatchResizeWindow.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessItemAsync_rejects_empty_file()
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-batch-empty-").FullName;
        try
        {
            var emptyFile = Path.Combine(folder, "empty.png");
            File.WriteAllBytes(emptyFile, []);

            var store = AppHost.Get<UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

            var item = new BatchResizeItem { SourcePath = emptyFile };
            var settings = new BatchResizeSettings { OutputDirectory = folder };

            await BatchResizeService.ProcessItemAsync(item, settings, preset, store, loc, CancellationToken.None);
            Assert.Equal(BatchItemStatus.Failed, item.Status);
            Assert.NotNull(item.ErrorMessage);
            Assert.Contains("0 bytes", item.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ProcessItemAsync_rejects_missing_file()
    {
        TestServices.EnsureInitialized("en");
        var missingFile = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
        var preset = BuiltInData.CreatePresets().First(p => p.Id == "tool-resize");

        var item = new BatchResizeItem { SourcePath = missingFile };
        var settings = new BatchResizeSettings();

        await BatchResizeService.ProcessItemAsync(item, settings, preset, store, loc, CancellationToken.None);
        Assert.Equal(BatchItemStatus.Failed, item.Status);
        Assert.NotNull(item.ErrorMessage);
        Assert.Equal(loc["error.file_not_found"], item.ErrorMessage);
    }

    [Fact]
    public void ResolveDestinationPath_never_overwrites_source_file()
    {
        var folder = Directory.CreateTempSubdirectory("filee-batch-dest-").FullName;
        try
        {
            var source = Path.Combine(folder, "image.png");
            File.WriteAllBytes(source, [1, 2, 3]);

            // With Overwrite collision policy and candidate clashing with source
            var dest = BatchResizeService.ResolveDestinationPath(
                source,
                "same",
                folder,
                FileCollisionPolicy.Overwrite);

            Assert.NotNull(dest);
            Assert.False(string.Equals(Path.GetFullPath(dest), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
