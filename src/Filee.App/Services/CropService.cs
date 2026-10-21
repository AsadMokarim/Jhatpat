// Loads image previews and performs crops using ImageMagick and OutputPathResolver.

using System.IO;
using Avalonia.Media.Imaging;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public enum CropStatus
{
    Success,
    SkippedExists,
    Failed,
}

public sealed record CropResult(CropStatus Status, string? OutputPath, string? ErrorKey, string? ErrorDetail);

public static class CropService
{
    private static MagickReadSettings CreateReadSettings(string inputPath)
    {
        var ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var settings = new MagickReadSettings();
        if (ImageEncoder.ExplicitCoder.Contains(ext))
            settings.Format = ImageEncoder.ToMagickFormat(ext);
        return settings;
    }

    private static void EnsureNotAnimated(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Image file not found.", inputPath);

        var fi = new FileInfo(inputPath);
        if (fi.Length == 0)
            throw new InvalidDataException("The file is empty (0 bytes).");

        if (inputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && Apng.IsAnimated(inputPath))
            throw new NotSupportedException("Animated PNG files cannot be cropped interactively.");

        var readSettings = CreateReadSettings(inputPath);
        var infos = readSettings.Format != MagickFormat.Unknown
            ? MagickImageInfo.ReadCollection(inputPath, readSettings).ToList()
            : MagickImageInfo.ReadCollection(inputPath).ToList();

        if (infos.Count > 1)
            throw new NotSupportedException("Multi-frame / animated images cannot be cropped interactively.");
    }

    /// <summary>
    /// Loads an image as a standard Avalonia Bitmap along with its true pixel dimensions,
    /// properly oriented according to EXIF.
    /// </summary>
    public static async Task<(Bitmap Bitmap, int Width, int Height)> LoadPreviewAsync(string inputPath)
    {
        return await Task.Run(() =>
        {
            EnsureNotAnimated(inputPath);

            var readSettings = CreateReadSettings(inputPath);
            using var image = readSettings.Format != MagickFormat.Unknown
                ? new MagickImage(inputPath, readSettings)
                : new MagickImage(inputPath);

            image.AutoOrient();
            var origWidth = (int)image.Width;
            var origHeight = (int)image.Height;

            // Generate an in-memory PNG for the viewport, downscaling huge images for UI fluidity.
            const int maxDisplayDim = 2560;
            if (image.Width > maxDisplayDim || image.Height > maxDisplayDim)
            {
                image.Resize(new MagickGeometry((uint)maxDisplayDim, (uint)maxDisplayDim) { Greater = true });
            }

            using var mem = new MemoryStream();
            image.Write(mem, MagickFormat.Png);
            mem.Position = 0;
            var bitmap = new Bitmap(mem);

            return (bitmap, origWidth, origHeight);
        });
    }

    /// <summary>
    /// Crops the image and returns an in-memory Avalonia Bitmap, suitable for live copying to clipboard.
    /// </summary>
    public static Bitmap CropToBitmap(string inputPath, int x, int y, int width, int height)
    {
        EnsureNotAnimated(inputPath);
        var readSettings = CreateReadSettings(inputPath);
        using var image = readSettings.Format != MagickFormat.Unknown
            ? new MagickImage(inputPath, readSettings)
            : new MagickImage(inputPath);

        image.AutoOrient();
        var clampedX = Math.Clamp(x, 0, Math.Max(0, (int)image.Width - 1));
        var clampedY = Math.Clamp(y, 0, Math.Max(0, (int)image.Height - 1));
        var clampedW = (uint)Math.Clamp(width, 1, Math.Max(1, (int)image.Width - clampedX));
        var clampedH = (uint)Math.Clamp(height, 1, Math.Max(1, (int)image.Height - clampedY));

        image.Crop(new MagickGeometry(clampedX, clampedY, clampedW, clampedH));
        image.ResetPage();

        using var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    /// <summary>
    /// Crops the image at <paramref name="inputPath"/> with the given pixel coordinates and saves the result using
    /// the preset's output rule and the application's default output settings.
    /// </summary>
    public static async Task<CropResult> CropFileAsync(
        string inputPath,
        int x, int y, int width, int height,
        Preset preset,
        UserDataStore store,
        ILocalizer loc,
        ILogger? log = null,
        string? customOutputPath = null)
    {
        return await Task.Run(() =>
        {
            string? tempOutputPath = null;
            try
            {
                if (!File.Exists(inputPath))
                {
                    log?.LogWarning("Input file for crop not found: {Path}", inputPath);
                    return new CropResult(CropStatus.Failed, null, "error.file_not_found", inputPath);
                }

                try
                {
                    var fi = new FileInfo(inputPath);
                    if (fi.Length == 0)
                    {
                        log?.LogWarning("Input file for crop is 0 bytes: {Path}", inputPath);
                        return new CropResult(CropStatus.Failed, null, "error.empty_file", inputPath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.LogWarning(ex, "Cannot access input file for crop: {Path}", inputPath);
                    return new CropResult(CropStatus.Failed, null, "error.permission_denied", ex.Message);
                }

                EnsureNotAnimated(inputPath);

                var ext = Path.GetExtension(inputPath).TrimStart('.');
                var appliedPreset = store.Settings.WithDefaultOutput(preset);
                var tokens = new OutputPathResolver.Tokens(inputPath, loc.DisplayName(appliedPreset), 1, DateTime.Now);
                var outputPath = customOutputPath ?? OutputPathResolver.Resolve(appliedPreset.Output, tokens, ext, File.Exists);
                if (outputPath is null)
                {
                    log?.LogInformation("Skipping crop of {Input} as conflict policy resolved to null (file exists)", inputPath);
                    return new CropResult(CropStatus.SkippedExists, null, "toast.skipped", Path.GetFileName(inputPath));
                }

                // Prevent accidental overwriting of source file
                if (FileSystemPaths.PathsEqual(outputPath, inputPath))
                {
                    log?.LogWarning("Attempted to overwrite source file during crop: {Path}", inputPath);
                    return new CropResult(CropStatus.Failed, null, "error.overwrite_source_prevented", inputPath);
                }

                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                tempOutputPath = Path.Combine(outDir ?? ".", $".tmp_{Guid.NewGuid():N}_{Path.GetFileName(outputPath)}");

                var readSettings = CreateReadSettings(inputPath);
                using var image = readSettings.Format != MagickFormat.Unknown
                    ? new MagickImage(inputPath, readSettings)
                    : new MagickImage(inputPath);

                image.AutoOrient();

                var clampedX = Math.Clamp(x, 0, Math.Max(0, (int)image.Width - 1));
                var clampedY = Math.Clamp(y, 0, Math.Max(0, (int)image.Height - 1));
                var clampedW = (uint)Math.Clamp(width, 1, Math.Max(1, (int)image.Width - clampedX));
                var clampedH = (uint)Math.Clamp(height, 1, Math.Max(1, (int)image.Height - clampedY));

                image.Crop(new MagickGeometry(clampedX, clampedY, clampedW, clampedH));
                image.ResetPage();

                // Drop EXIF thumbnail so viewers don't show the uncropped preview
                if (image.GetExifProfile() is { } exif && exif.ThumbnailLength > 0)
                {
                    exif.RemoveThumbnail();
                    image.SetProfile(exif);
                }

                image.Write(tempOutputPath);

                var writtenInfo = new FileInfo(tempOutputPath);
                if (!writtenInfo.Exists || writtenInfo.Length == 0)
                    throw new IOException("Crop produced an empty output file.");

                // Atomic replacement of target file
                File.Move(tempOutputPath, outputPath, overwrite: true);

                if (appliedPreset.Output.KeepDates || store.Settings.KeepFileDates)
                {
                    try
                    {
                        File.SetCreationTimeUtc(outputPath, File.GetCreationTimeUtc(inputPath));
                        File.SetLastWriteTimeUtc(outputPath, File.GetLastWriteTimeUtc(inputPath));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        log?.LogDebug(ex, "Could not preserve file timestamps for {Path}", outputPath);
                    }
                }

                return new CropResult(CropStatus.Success, outputPath, null, null);
            }
            catch (NotSupportedException ex)
            {
                log?.LogInformation(ex, "Not cropping {File}", inputPath);
                return new CropResult(CropStatus.Failed, null, "error.unsupported_source", null);
            }
            catch (UnauthorizedAccessException ex)
            {
                log?.LogError(ex, "Permission denied cropping {File}", inputPath);
                return new CropResult(CropStatus.Failed, null, "error.permission_denied", ex.Message);
            }
            catch (IOException ex) when (FileSystemPaths.IsDiskFull(ex))
            {
                log?.LogError(ex, "Disk full cropping {File}", inputPath);
                return new CropResult(CropStatus.Failed, null, "error.not_enough_space", ex.Message);
            }
            catch (Exception ex) when (FileSystemPaths.IsCorrupt(ex))
            {
                log?.LogError(ex, "Corrupt file cropping {File}", inputPath);
                return new CropResult(CropStatus.Failed, null, "error.corrupt_file", ex.Message);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Failed to crop {File}", inputPath);
                return new CropResult(CropStatus.Failed, null, "error.conversion_failed", ex.Message);
            }
            finally
            {
                if (tempOutputPath is not null && File.Exists(tempOutputPath))
                {
                    try { File.Delete(tempOutputPath); } catch { }
                }
            }
        });
    }
}
