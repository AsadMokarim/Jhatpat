// Background framing processing service: layouts, backgrounds, padding, corners, shadows, and exports.

using System.IO;
using Avalonia.Media.Imaging;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using ImageMagick;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.Services;

public enum FramingBackgroundType
{
    SolidColor,
    Gradient,
    Image,
    Transparent,
}

public sealed record FramingOptions
{
    public FramingBackgroundType BackgroundType { get; init; } = FramingBackgroundType.SolidColor;
    public string BackgroundColor { get; init; } = "#1E1E2E";
    public string GradientStartColor { get; init; } = "#4F46E5";
    public string GradientEndColor { get; init; } = "#EC4899";
    public double GradientAngle { get; init; } = 45.0;
    public string? BackgroundImagePath { get; init; }

    public int Padding { get; init; } = 48;
    public double CornerRadius { get; init; } = 16.0;
    public bool HasShadow { get; init; } = true;
    public double ShadowBlur { get; init; } = 14.0;
    public double ShadowOpacity { get; init; } = 0.45;
    public int ShadowOffsetY { get; init; } = 8;

    public string AspectRatio { get; init; } = "original";
    public int CustomWidth { get; init; }
    public int CustomHeight { get; init; }

    public double RelativeOffsetX { get; init; } = 0.0;
    public double RelativeOffsetY { get; init; } = 0.0;
}

public enum FramingStatus
{
    Success,
    SkippedExists,
    Failed,
}

public sealed record FramingResult(FramingStatus Status, string? OutputPath, string? ErrorKey, string? ErrorDetail);

public static class FramingService
{
    public const int MinCustomDimension = 32;
    public const int MaxCustomDimension = 10000;

    public static readonly IReadOnlyList<string> AspectRatioPresets =
        ["original", "1:1", "4:5", "9:16", "16:9", "4:3", "3:2", "2:3", "custom"];

    public static double RatioValueOf(string aspect) => aspect switch
    {
        "1:1" => 1.0,
        "4:5" => 4.0 / 5.0,
        "9:16" => 9.0 / 16.0,
        "16:9" => 16.0 / 9.0,
        "4:3" => 4.0 / 3.0,
        "3:2" => 3.0 / 2.0,
        "2:3" => 2.0 / 3.0,
        _ => 0.0,
    };

    public static SKColor ParseHexColor(string? hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        var clean = hex.Trim().TrimStart('#');
        if (clean.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return SKColors.Transparent;

        if (clean.Length == 6 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return new SKColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);
        }
        if (clean.Length == 8 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            return new SKColor((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF), (byte)((argb >> 24) & 0xFF));
        }
        return fallback;
    }

    public static bool IsValidHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var clean = hex.Trim().TrimStart('#');
        if (clean.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return true;

        if (clean.Length == 6 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out _))
            return true;

        if (clean.Length == 8 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out _))
            return true;

        return false;
    }

    /// <summary>
    /// Pure layout calculation: computes canvas dimensions, placement, and target size of inner image.
    /// </summary>
    public static (int CanvasWidth, int CanvasHeight, int TargetX, int TargetY, int TargetWidth, int TargetHeight) CalculateLayout(
        int sourceWidth, int sourceHeight, FramingOptions options)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new ArgumentException("Source image dimensions must be positive.");

        var pad = Math.Max(0, options.Padding);
        int targetW = sourceWidth;
        int targetH = sourceHeight;
        int canvasW;
        int canvasH;

        if (options.AspectRatio == "custom" && options.CustomWidth > 0 && options.CustomHeight > 0)
        {
            canvasW = Math.Clamp(options.CustomWidth, MinCustomDimension, MaxCustomDimension);
            canvasH = Math.Clamp(options.CustomHeight, MinCustomDimension, MaxCustomDimension);

            int availW = Math.Max(1, canvasW - 2 * pad);
            int availH = Math.Max(1, canvasH - 2 * pad);
            double scale = Math.Min(1.0, Math.Min((double)availW / sourceWidth, (double)availH / sourceHeight));
            targetW = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            targetH = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        }
        else if (RatioValueOf(options.AspectRatio) is var ratio && ratio > 0.0)
        {
            int minW = targetW + 2 * pad;
            int minH = targetH + 2 * pad;
            double currentRatio = (double)minW / minH;

            if (currentRatio < ratio)
            {
                canvasH = minH;
                canvasW = Math.Max(1, (int)Math.Round(canvasH * ratio));
            }
            else
            {
                canvasW = minW;
                canvasH = Math.Max(1, (int)Math.Round(canvasW / ratio));
            }
        }
        else
        {
            // "original" or unrecognized: pad evenly around source dimensions
            canvasW = targetW + 2 * pad;
            canvasH = targetH + 2 * pad;
        }

        int baseX = (canvasW - targetW) / 2;
        int baseY = (canvasH - targetH) / 2;

        int maxOffsetX = (canvasW - targetW) / 2;
        int maxOffsetY = (canvasH - targetH) / 2;

        int targetX = maxOffsetX > 0
            ? baseX + (int)Math.Round(Math.Clamp(options.RelativeOffsetX, -1.0, 1.0) * maxOffsetX)
            : baseX;

        int targetY = maxOffsetY > 0
            ? baseY + (int)Math.Round(Math.Clamp(options.RelativeOffsetY, -1.0, 1.0) * maxOffsetY)
            : baseY;

        targetX = Math.Clamp(targetX, 0, Math.Max(0, canvasW - targetW));
        targetY = Math.Clamp(targetY, 0, Math.Max(0, canvasH - targetH));

        return (canvasW, canvasH, targetX, targetY, targetW, targetH);
    }

    /// <summary>
    /// Pure image processing: renders source bitmap with framing options into a new SKBitmap.
    /// </summary>
    public static SKBitmap RenderFramedBitmap(SKBitmap source, FramingOptions options)
    {
        var (canvasW, canvasH, targetX, targetY, targetW, targetH) =
            CalculateLayout(source.Width, source.Height, options);

        var output = new SKBitmap(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);

        // 1. Draw Background
        DrawBackground(canvas, canvasW, canvasH, options);

        // 2. Prepare inner image rectangle and rounded corners
        var destRect = new SKRect(targetX, targetY, targetX + targetW, targetY + targetH);
        var maxR = Math.Min(targetW, targetH) / 2.0;
        var r = (float)Math.Clamp(options.CornerRadius, 0.0, maxR);
        var roundRect = new SKRoundRect(destRect, r, r);

        // 3. Draw Drop Shadow
        if (options.HasShadow && options.ShadowOpacity > 0.0)
        {
            var alpha = (byte)Math.Clamp(Math.Round(options.ShadowOpacity * 255), 0, 255);
            var shadowColor = new SKColor(0, 0, 0, alpha);
            var sigma = (float)Math.Max(1.0, options.ShadowBlur);
            var dy = (float)options.ShadowOffsetY;

            using var shadowFilter = SKImageFilter.CreateDropShadow(0, dy, sigma, sigma, shadowColor);
            using var shadowPaint = new SKPaint
            {
                ImageFilter = shadowFilter,
                IsAntialias = true,
            };
            canvas.DrawRoundRect(roundRect, shadowPaint);
        }

        // 4. Draw Inner Image (clipped to rounded corners, preserving transparency)
        canvas.Save();
        canvas.ClipRoundRect(roundRect, antialias: true);
        using var imagePaint = new SKPaint
        {
            IsAntialias = true,
        };
        canvas.DrawBitmap(source, destRect, imagePaint);
        canvas.Restore();

        return output;
    }

    private static void DrawBackground(SKCanvas canvas, int width, int height, FramingOptions options)
    {
        switch (options.BackgroundType)
        {
            case FramingBackgroundType.Transparent:
                canvas.Clear(SKColors.Transparent);
                break;

            case FramingBackgroundType.SolidColor:
                var solidColor = ParseHexColor(options.BackgroundColor, new SKColor(0x1E, 0x1E, 0x2E));
                canvas.Clear(solidColor);
                break;

            case FramingBackgroundType.Gradient:
                var startColor = ParseHexColor(options.GradientStartColor, new SKColor(0x4F, 0x46, 0xE5));
                var endColor = ParseHexColor(options.GradientEndColor, new SKColor(0xEC, 0x48, 0x99));
                var rad = options.GradientAngle * Math.PI / 180.0;
                var cx = width / 2.0;
                var cy = height / 2.0;
                var length = Math.Sqrt(width * width + height * height) / 2.0;
                var x0 = (float)(cx - Math.Cos(rad) * length);
                var y0 = (float)(cy - Math.Sin(rad) * length);
                var x1 = (float)(cx + Math.Cos(rad) * length);
                var y1 = (float)(cy + Math.Sin(rad) * length);

                using (var shader = SKShader.CreateLinearGradient(
                    new SKPoint(x0, y0), new SKPoint(x1, y1),
                    [startColor, endColor],
                    SKShaderTileMode.Clamp))
                using (var gradPaint = new SKPaint { Shader = shader, IsAntialias = true })
                {
                    canvas.DrawRect(0, 0, width, height, gradPaint);
                }
                break;

            case FramingBackgroundType.Image:
                if (!string.IsNullOrEmpty(options.BackgroundImagePath) && File.Exists(options.BackgroundImagePath))
                {
                    try
                    {
                        using var bgBitmap = SKBitmap.Decode(options.BackgroundImagePath);
                        if (bgBitmap is not null)
                        {
                            DrawCoverImage(canvas, bgBitmap, width, height);
                            break;
                        }
                    }
                    catch
                    {
                        // Fall back to default dark slate
                    }
                }
                canvas.Clear(new SKColor(0x1E, 0x1E, 0x2E));
                break;
        }
    }

    private static void DrawCoverImage(SKCanvas canvas, SKBitmap bg, int canvasW, int canvasH)
    {
        double scale = Math.Max((double)canvasW / bg.Width, (double)canvasH / bg.Height);
        float destW = (float)(bg.Width * scale);
        float destH = (float)(bg.Height * scale);
        float destX = (canvasW - destW) / 2f;
        float destY = (canvasH - destH) / 2f;

        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(bg, new SKRect(destX, destY, destX + destW, destY + destH), paint);
    }

    /// <summary>
    /// Loads a source image file as an SKBitmap, auto-orienting according to EXIF.
    /// </summary>
    public static SKBitmap LoadSourceBitmap(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Image file not found.", inputPath);

        var fi = new FileInfo(inputPath);
        if (fi.Length == 0)
            throw new InvalidDataException("The file is empty (0 bytes).");

        // Read through Magick to ensure proper EXIF orientation and format support
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
    /// Generates a fluid in-memory preview bitmap for UI display.
    /// </summary>
    public static async Task<(Bitmap Bitmap, int Width, int Height, int SourceWidth, int SourceHeight)> GeneratePreviewAsync(string inputPath, FramingOptions options)
    {
        return await Task.Run(() =>
        {
            using var source = LoadSourceBitmap(inputPath);

            // Scale down huge sources for live preview rendering
            const int maxPreviewDim = 1920;
            SKBitmap renderSource = source;
            bool disposeRenderSource = false;

            if (source.Width > maxPreviewDim || source.Height > maxPreviewDim)
            {
                double scale = Math.Min((double)maxPreviewDim / source.Width, (double)maxPreviewDim / source.Height);
                int scaledW = Math.Max(1, (int)Math.Round(source.Width * scale));
                int scaledH = Math.Max(1, (int)Math.Round(source.Height * scale));
                renderSource = source.Resize(new SKImageInfo(scaledW, scaledH), new SKSamplingOptions(SKFilterMode.Linear));
                disposeRenderSource = true;
            }

            try
            {
                // Adjust padding & radius proportionally for preview if downscaled
                FramingOptions previewOptions = options;
                if (disposeRenderSource)
                {
                    double factor = (double)renderSource.Width / source.Width;
                    previewOptions = options with
                    {
                        Padding = Math.Max(0, (int)Math.Round(options.Padding * factor)),
                        CornerRadius = Math.Max(0.0, options.CornerRadius * factor),
                        ShadowBlur = Math.Max(1.0, options.ShadowBlur * factor),
                        ShadowOffsetY = (int)Math.Round(options.ShadowOffsetY * factor),
                        CustomWidth = options.CustomWidth > 0 ? (int)Math.Round(options.CustomWidth * factor) : 0,
                        CustomHeight = options.CustomHeight > 0 ? (int)Math.Round(options.CustomHeight * factor) : 0,
                    };
                }

                using var framed = RenderFramedBitmap(renderSource, previewOptions);
                using var image = SKImage.FromBitmap(framed);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                using var mem = new MemoryStream();
                data.SaveTo(mem);
                mem.Position = 0;
                var avaloniaBitmap = new Bitmap(mem);

                // Full-resolution dimensions for display readout
                var (fullW, fullH, _, _, _, _) = CalculateLayout(source.Width, source.Height, options);
                return (avaloniaBitmap, fullW, fullH, source.Width, source.Height);
            }
            finally
            {
                if (disposeRenderSource)
                    renderSource.Dispose();
            }
        });
    }

    /// <summary>
    /// Processes and exports the framed image to a user-selected path or resolved output path.
    /// </summary>
    public static async Task<FramingResult> FrameFileAsync(
        string inputPath,
        FramingOptions options,
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
                    return new FramingResult(FramingStatus.Failed, null, "error.file_not_found", inputPath);

                try
                {
                    var fi = new FileInfo(inputPath);
                    if (fi.Length == 0)
                        return new FramingResult(FramingStatus.Failed, null, "error.empty_file", inputPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new FramingResult(FramingStatus.Failed, null, "error.permission_denied", ex.Message);
                }

                var ext = Path.GetExtension(inputPath).TrimStart('.');
                if (options.BackgroundType == FramingBackgroundType.Transparent &&
                    (ext.Equals("jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals("jpeg", StringComparison.OrdinalIgnoreCase)))
                {
                    ext = "png"; // preserve transparency
                }

                var appliedPreset = store.Settings.WithDefaultOutput(preset);
                var tokens = new OutputPathResolver.Tokens(inputPath, loc.DisplayName(appliedPreset), 1, DateTime.Now);
                var outputPath = customOutputPath ?? OutputPathResolver.Resolve(appliedPreset.Output, tokens, ext, File.Exists);

                if (outputPath is null)
                {
                    log?.LogInformation("Skipping framing of {Input} as conflict policy resolved to null (file exists)", inputPath);
                    return new FramingResult(FramingStatus.SkippedExists, null, "toast.skipped", Path.GetFileName(inputPath));
                }

                // Prevent accidental overwriting of source file
                if (FileSystemPaths.PathsEqual(outputPath, inputPath))
                {
                    log?.LogWarning("Attempted to overwrite source file during framing: {Path}", inputPath);
                    return new FramingResult(FramingStatus.Failed, null, "error.overwrite_source_prevented", inputPath);
                }

                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                tempOutputPath = Path.Combine(outDir ?? ".", $".tmp_{Guid.NewGuid():N}_{Path.GetFileName(outputPath)}");

                using var source = LoadSourceBitmap(inputPath);
                using var framed = RenderFramedBitmap(source, options);

                // Encode and write using SKBitmap and ImageMagick
                using var image = SKImage.FromBitmap(framed);
                using var data = image.Encode(
                    ext.Equals("jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                        ? SKEncodedImageFormat.Jpeg
                        : ext.Equals("webp", StringComparison.OrdinalIgnoreCase)
                            ? SKEncodedImageFormat.Webp
                            : SKEncodedImageFormat.Png,
                    95);

                if (data is null)
                    throw new InvalidOperationException("Failed to encode framed image.");

                using (var fileStream = File.Open(tempOutputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    data.SaveTo(fileStream);
                }

                var writtenInfo = new FileInfo(tempOutputPath);
                if (!writtenInfo.Exists || writtenInfo.Length == 0)
                    throw new IOException("Framing produced an empty output file.");

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

                return new FramingResult(FramingStatus.Success, outputPath, null, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                log?.LogError(ex, "Permission denied framing image {File}", inputPath);
                return new FramingResult(FramingStatus.Failed, null, "error.permission_denied", ex.Message);
            }
            catch (IOException ex) when (FileSystemPaths.IsDiskFull(ex))
            {
                log?.LogError(ex, "Disk full framing image {File}", inputPath);
                return new FramingResult(FramingStatus.Failed, null, "error.not_enough_space", ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                log?.LogError(ex, "Input file not found framing {File}", inputPath);
                return new FramingResult(FramingStatus.Failed, null, "error.file_not_found", ex.Message);
            }
            catch (Exception ex) when (FileSystemPaths.IsCorrupt(ex))
            {
                log?.LogError(ex, "Corrupt file framing image {File}", inputPath);
                return new FramingResult(FramingStatus.Failed, null, "error.corrupt_file", ex.Message);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Failed to frame image {File}", inputPath);
                return new FramingResult(FramingStatus.Failed, null, "error.conversion_failed", ex.Message);
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
