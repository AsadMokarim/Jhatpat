// Image resizing and compression service: scaling, quality presets, transparency handling, and exports.

using System.IO;
using Avalonia.Media.Imaging;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Settings;
using ImageMagick;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.Services;

public sealed record ResizeOptions
{
    public int TargetWidth { get; init; }
    public int TargetHeight { get; init; }
    public bool MaintainAspectRatio { get; init; } = true;
    public double ScalePercent { get; init; } = 100.0;
    public string TargetFormat { get; init; } = "same"; // "same", "png", "jpg", "webp"
    public int Quality { get; init; } = 85; // 10-100
}

public sealed record ResizePreviewInfo(
    Bitmap PreviewBitmap,
    int TargetWidth,
    int TargetHeight,
    long EstimatedBytes,
    bool HasTransparency,
    bool WillLoseTransparency);

public enum ResizeStatus
{
    Success,
    SkippedExists,
    Failed,
}

public sealed record ResizeResult(ResizeStatus Status, string? OutputPath, string? ErrorKey, string? ErrorDetail);

public static class ResizeService
{
    public const int MinDimension = 16;
    public const int MaxDimension = 16384;

    public static readonly IReadOnlyList<double> ScalePresets = [0.25, 0.50, 0.75, 1.0];

    public static (int TargetWidth, int TargetHeight) CalculateDimensions(
        int sourceWidth,
        int sourceHeight,
        int? requestedWidth,
        int? requestedHeight,
        bool maintainAspectRatio,
        bool widthChangedLast = true)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new ArgumentException("Source dimensions must be positive.");

        double ratio = (double)sourceWidth / sourceHeight;

        if (!maintainAspectRatio)
        {
            int w = Math.Clamp(requestedWidth ?? sourceWidth, MinDimension, MaxDimension);
            int h = Math.Clamp(requestedHeight ?? sourceHeight, MinDimension, MaxDimension);
            return (w, h);
        }

        if (widthChangedLast)
        {
            int w = Math.Clamp(requestedWidth ?? sourceWidth, MinDimension, MaxDimension);
            int h = Math.Clamp((int)Math.Round(w / ratio), MinDimension, MaxDimension);
            return (w, h);
        }
        else
        {
            int h = Math.Clamp(requestedHeight ?? sourceHeight, MinDimension, MaxDimension);
            int w = Math.Clamp((int)Math.Round(h * ratio), MinDimension, MaxDimension);
            return (w, h);
        }
    }

    public static (int TargetWidth, int TargetHeight) ScaleDimensions(
        int sourceWidth,
        int sourceHeight,
        double scaleFactor)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new ArgumentException("Source dimensions must be positive.");

        int w = Math.Clamp((int)Math.Round(sourceWidth * scaleFactor), MinDimension, MaxDimension);
        int h = Math.Clamp((int)Math.Round(sourceHeight * scaleFactor), MinDimension, MaxDimension);
        return (w, h);
    }

    public static string ResolveFormat(string inputPath, string targetFormatOption)
    {
        if (string.IsNullOrWhiteSpace(targetFormatOption) || targetFormatOption.Equals("same", StringComparison.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            return string.IsNullOrEmpty(ext) ? "png" : (ext == "jpeg" ? "jpg" : ext);
        }
        return targetFormatOption.TrimStart('.').ToLowerInvariant();
    }

    public static bool IsLossyFormat(string format)
    {
        var f = format.ToLowerInvariant();
        return f is "jpg" or "jpeg" or "webp" or "avif";
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    public static SKBitmap LoadSourceBitmap(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Image file not found.", inputPath);

        var fi = new FileInfo(inputPath);
        if (fi.Length == 0)
            throw new InvalidDataException("The file is empty (0 bytes).");

        using var magick = new MagickImage(inputPath);
        magick.AutoOrient();

        using var mem = new MemoryStream();
        magick.Write(mem, MagickFormat.Png);
        mem.Position = 0;
        var bitmap = SKBitmap.Decode(mem);
        if (bitmap is null)
            throw new InvalidDataException($"Could not decode image at {inputPath}");
        return bitmap;
    }

    /// <summary>
    /// Resizes an SKBitmap to the target dimensions using high quality linear sampling.
    /// </summary>
    public static SKBitmap ResizeBitmap(SKBitmap source, int targetWidth, int targetHeight)
    {
        var clampedW = Math.Clamp(targetWidth, MinDimension, MaxDimension);
        var clampedH = Math.Clamp(targetHeight, MinDimension, MaxDimension);
        return source.Resize(new SKImageInfo(clampedW, clampedH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? new SKBitmap(clampedW, clampedH);
    }

    public static bool CheckHasTransparency(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
            return false;

        // Sample pixels for transparency
        int step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 50);
        for (int y = 0; y < bitmap.Height; y += step)
        {
            for (int x = 0; x < bitmap.Width; x += step)
            {
                if (bitmap.GetPixel(x, y).Alpha < 250)
                    return true;
            }
        }
        return false;
    }

    public static async Task<ResizePreviewInfo> GeneratePreviewAsync(string inputPath, ResizeOptions options)
    {
        return await Task.Run(() =>
        {
            using var source = LoadSourceBitmap(inputPath);
            bool hasTransparency = CheckHasTransparency(source);

            int targetW = options.TargetWidth > 0 ? options.TargetWidth : source.Width;
            int targetH = options.TargetHeight > 0 ? options.TargetHeight : source.Height;
            targetW = Math.Clamp(targetW, MinDimension, MaxDimension);
            targetH = Math.Clamp(targetH, MinDimension, MaxDimension);

            var resolvedFormat = ResolveFormat(inputPath, options.TargetFormat);
            bool willLoseTransparency = hasTransparency && (resolvedFormat is "jpg" or "jpeg" or "bmp");

            // For preview viewport, downscale target if very large
            const int maxPreviewDim = 1200;
            double previewScale = 1.0;
            if (targetW > maxPreviewDim || targetH > maxPreviewDim)
            {
                previewScale = Math.Min((double)maxPreviewDim / targetW, (double)maxPreviewDim / targetH);
            }

            int dispW = Math.Max(1, (int)Math.Round(targetW * previewScale));
            int dispH = Math.Max(1, (int)Math.Round(targetH * previewScale));

            using var resized = source.Resize(new SKImageInfo(dispW, dispH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            // Flatten onto white background if saving as JPEG with transparency
            SKBitmap renderBitmap = resized;
            bool disposeRenderBitmap = false;
            if (willLoseTransparency)
            {
                var flattened = new SKBitmap(dispW, dispH, SKColorType.Rgba8888, SKAlphaType.Opaque);
                using var canvas = new SKCanvas(flattened);
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(resized, 0, 0);
                renderBitmap = flattened;
                disposeRenderBitmap = true;
            }

            try
            {
                using var image = SKImage.FromBitmap(renderBitmap);
                SKEncodedImageFormat encodeFormat = resolvedFormat switch
                {
                    "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
                    "webp" => SKEncodedImageFormat.Webp,
                    _ => SKEncodedImageFormat.Png,
                };

                int quality = Math.Clamp(options.Quality, 10, 100);
                using var data = image.Encode(encodeFormat, quality);

                // Estimate full-resolution file size based on preview density and resolution ratio
                long previewBytes = data.Size;
                double pixelRatio = ((double)targetW * targetH) / Math.Max(1, dispW * dispH);
                // Sublinear scaling exponent for compression estimation
                double sizeRatio = Math.Pow(pixelRatio, 0.85);
                long estimatedBytes = (long)Math.Max(512, Math.Round(previewBytes * sizeRatio));

                using var mem = new MemoryStream();
                data.SaveTo(mem);
                mem.Position = 0;
                var avaloniaBitmap = new Bitmap(mem);

                return new ResizePreviewInfo(
                    avaloniaBitmap,
                    targetW,
                    targetH,
                    estimatedBytes,
                    hasTransparency,
                    willLoseTransparency);
            }
            finally
            {
                if (disposeRenderBitmap)
                    renderBitmap.Dispose();
            }
        });
    }

    public static async Task<ResizeResult> ResizeFileAsync(
        string inputPath,
        ResizeOptions options,
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
                    return new ResizeResult(ResizeStatus.Failed, null, "error.file_not_found", inputPath);

                try
                {
                    var fi = new FileInfo(inputPath);
                    if (fi.Length == 0)
                        return new ResizeResult(ResizeStatus.Failed, null, "error.empty_file", inputPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new ResizeResult(ResizeStatus.Failed, null, "error.permission_denied", ex.Message);
                }

                var resolvedExt = ResolveFormat(inputPath, options.TargetFormat);
                var appliedPreset = store.Settings.WithDefaultOutput(preset);
                var tokens = new OutputPathResolver.Tokens(inputPath, loc.DisplayName(appliedPreset), 1, DateTime.Now);
                var outputPath = customOutputPath ?? OutputPathResolver.Resolve(appliedPreset.Output, tokens, resolvedExt, File.Exists);

                if (outputPath is null)
                {
                    log?.LogInformation("Skipping resize of {Input} as conflict policy resolved to null (file exists)", inputPath);
                    return new ResizeResult(ResizeStatus.SkippedExists, null, "toast.skipped", Path.GetFileName(inputPath));
                }

                // Prevent accidental overwriting of source file
                if (FileSystemPaths.PathsEqual(outputPath, inputPath))
                {
                    log?.LogWarning("Attempted to overwrite source file during resize: {Path}", inputPath);
                    return new ResizeResult(ResizeStatus.Failed, null, "error.overwrite_source_prevented", inputPath);
                }

                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                tempOutputPath = Path.Combine(outDir ?? ".", $".tmp_{Guid.NewGuid():N}_{Path.GetFileName(outputPath)}");

                using var magick = new MagickImage(inputPath);
                magick.AutoOrient();

                int targetW = options.TargetWidth > 0 ? options.TargetWidth : (int)magick.Width;
                int targetH = options.TargetHeight > 0 ? options.TargetHeight : (int)magick.Height;
                targetW = Math.Clamp(targetW, MinDimension, MaxDimension);
                targetH = Math.Clamp(targetH, MinDimension, MaxDimension);

                // High quality Lanczos/Mitchell resize in ImageMagick
                magick.FilterType = FilterType.Lanczos;
                magick.Resize((uint)targetW, (uint)targetH);

                // Format & Transparency
                MagickFormat outFormat = resolvedExt switch
                {
                    "jpg" or "jpeg" => MagickFormat.Jpeg,
                    "webp" => MagickFormat.WebP,
                    "bmp" => MagickFormat.Bmp,
                    "tiff" or "tif" => MagickFormat.Tiff,
                    "gif" => MagickFormat.Gif,
                    _ => MagickFormat.Png,
                };
                magick.Format = outFormat;

                if ((resolvedExt is "jpg" or "jpeg" or "bmp") && magick.HasAlpha)
                {
                    magick.BackgroundColor = MagickColors.White;
                    magick.Alpha(AlphaOption.Remove);
                }

                // Compression quality
                if (IsLossyFormat(resolvedExt))
                {
                    magick.Quality = (uint)Math.Clamp(options.Quality, 10, 100);
                }

                magick.Write(tempOutputPath);

                var writtenInfo = new FileInfo(tempOutputPath);
                if (!writtenInfo.Exists || writtenInfo.Length == 0)
                    throw new IOException("Resize produced an empty output file.");

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

                return new ResizeResult(ResizeStatus.Success, outputPath, null, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                log?.LogError(ex, "Permission denied resizing image {File}", inputPath);
                return new ResizeResult(ResizeStatus.Failed, null, "error.permission_denied", ex.Message);
            }
            catch (IOException ex) when (FileSystemPaths.IsDiskFull(ex))
            {
                log?.LogError(ex, "Disk full resizing image {File}", inputPath);
                return new ResizeResult(ResizeStatus.Failed, null, "error.not_enough_space", ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                log?.LogError(ex, "Input file not found resizing {File}", inputPath);
                return new ResizeResult(ResizeStatus.Failed, null, "error.file_not_found", ex.Message);
            }
            catch (Exception ex) when (FileSystemPaths.IsCorrupt(ex))
            {
                log?.LogError(ex, "Corrupt file resizing image {File}", inputPath);
                return new ResizeResult(ResizeStatus.Failed, null, "error.corrupt_file", ex.Message);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Failed to resize image {File}", inputPath);
                return new ResizeResult(ResizeStatus.Failed, null, "error.conversion_failed", ex.Message);
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
