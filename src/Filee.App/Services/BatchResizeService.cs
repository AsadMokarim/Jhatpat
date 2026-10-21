// Batch image processing service: multi-file queuing, shared settings, collision safety, and cancellation.

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Filee.Core.Conversion;
using Filee.Core.History;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Settings;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public enum BatchItemStatus
{
    Queued,
    Processing,
    Success,
    Skipped,
    Failed,
    Cancelled,
}

public enum BatchResizeMode
{
    ScalePercentage,
    FitBoundingBox,
    ExactDimensions,
}

public enum FileCollisionPolicy
{
    RenameWithSuffix,
    Skip,
    Overwrite,
}

public sealed partial class BatchResizeItem : ObservableObject
{
    public required string SourcePath { get; init; }
    public string FileName => Path.GetFileName(SourcePath);

    [ObservableProperty] private int _sourceWidth;
    [ObservableProperty] private int _sourceHeight;
    [ObservableProperty] private long _sourceFileSizeBytes;
    [ObservableProperty] private string _sourceDimensionsText = "—";
    [ObservableProperty] private string _sourceFileSizeText = "—";

    [ObservableProperty] private int _targetWidth;
    [ObservableProperty] private int _targetHeight;
    [ObservableProperty] private string _targetDimensionsText = "—";

    [ObservableProperty] private BatchItemStatus _status = BatchItemStatus.Queued;
    [ObservableProperty] private string? _outputPath;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private double _progress; // 0..1

    public bool IsSuccess => Status == BatchItemStatus.Success;
    public bool IsFailed => Status == BatchItemStatus.Failed;
    public bool IsSkipped => Status == BatchItemStatus.Skipped;
    public bool IsProcessing => Status == BatchItemStatus.Processing;
    public bool IsCancelled => Status == BatchItemStatus.Cancelled;

    partial void OnStatusChanged(BatchItemStatus value)
    {
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsCancelled));
    }
}

public sealed record BatchResizeSettings
{
    public BatchResizeMode Mode { get; init; } = BatchResizeMode.ScalePercentage;
    public double ScalePercent { get; init; } = 100.0;
    public int MaxWidth { get; init; } = 1920;
    public int MaxHeight { get; init; } = 1080;
    public bool MaintainAspectRatio { get; init; } = true;
    public string TargetFormat { get; init; } = "same"; // "same", "png", "jpg", "webp"
    public int Quality { get; init; } = 85;
    public string? OutputDirectory { get; init; }
    public FileCollisionPolicy CollisionPolicy { get; init; } = FileCollisionPolicy.RenameWithSuffix;
}

public sealed record BatchSummary(
    int TotalCount,
    int SuccessCount,
    int FailedCount,
    int SkippedCount,
    int CancelledCount,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<HistoryFailure> Failures);

public static class BatchResizeService
{
    public static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".tif", ".gif", ".avif"];

    public static bool IsSupportedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Quickly inspects an image file to obtain width, height, and file size without loading all pixel data into memory.
    /// </summary>
    public static (int Width, int Height, long FileSize) InspectImage(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Image file not found.", path);

        var fileInfo = new FileInfo(path);
        long size = fileInfo.Length;

        try
        {
            var info = new MagickImageInfo(path);
            return ((int)info.Width, (int)info.Height, size);
        }
        catch
        {
            // Fallback to Skia load if MagickImageInfo cannot identify
            using var bitmap = ResizeService.LoadSourceBitmap(path);
            return (bitmap.Width, bitmap.Height, size);
        }
    }

    /// <summary>
    /// Calculates individual target dimensions for an image based on the batch settings.
    /// </summary>
    public static (int TargetWidth, int TargetHeight) CalculateTargetDimensions(
        int sourceWidth,
        int sourceHeight,
        BatchResizeSettings settings)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return (100, 100);

        switch (settings.Mode)
        {
            case BatchResizeMode.ScalePercentage:
                var factor = Math.Clamp(settings.ScalePercent / 100.0, 0.01, 10.0);
                return ResizeService.ScaleDimensions(sourceWidth, sourceHeight, factor);

            case BatchResizeMode.FitBoundingBox:
                double scaleX = (double)settings.MaxWidth / sourceWidth;
                double scaleY = (double)settings.MaxHeight / sourceHeight;
                double fitScale = Math.Min(scaleX, scaleY);
                int fitW = Math.Clamp((int)Math.Round(sourceWidth * fitScale), ResizeService.MinDimension, ResizeService.MaxDimension);
                int fitH = Math.Clamp((int)Math.Round(sourceHeight * fitScale), ResizeService.MinDimension, ResizeService.MaxDimension);
                return (fitW, fitH);

            case BatchResizeMode.ExactDimensions:
            default:
                return ResizeService.CalculateDimensions(
                    sourceWidth,
                    sourceHeight,
                    settings.MaxWidth,
                    settings.MaxHeight,
                    settings.MaintainAspectRatio,
                    widthChangedLast: true);
        }
    }

    /// <summary>
    /// Determines the safe destination path for an exported file, protecting original files and resolving collisions.
    /// Returns null if collision policy is Skip and the target file already exists.
    /// </summary>
    public static string? ResolveDestinationPath(
        string sourcePath,
        string targetFormat,
        string? outputDirectory,
        FileCollisionPolicy collisionPolicy)
    {
        var resolvedExt = ResizeService.ResolveFormat(sourcePath, targetFormat);
        var dir = !string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory)
            ? outputDirectory
            : (Path.GetDirectoryName(sourcePath) ?? ".");

        var originalBaseName = Path.GetFileNameWithoutExtension(sourcePath);
        var candidateBaseName = $"{originalBaseName}_compressed";
        var candidatePath = Path.Combine(dir, $"{candidateBaseName}.{resolvedExt}");

        // Absolute safety rule: NEVER overwrite the original source file
        if (string.Equals(Path.GetFullPath(candidatePath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            candidateBaseName = $"{originalBaseName}_compressed_1";
            candidatePath = Path.Combine(dir, $"{candidateBaseName}.{resolvedExt}");
        }

        if (!File.Exists(candidatePath))
            return candidatePath;

        return collisionPolicy switch
        {
            FileCollisionPolicy.Skip => null,
            FileCollisionPolicy.Overwrite =>
                // Even with overwrite, verify we are not overwriting the original input file
                string.Equals(Path.GetFullPath(candidatePath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)
                    ? FindAvailableNumberedPath(dir, candidateBaseName, resolvedExt, sourcePath)
                    : candidatePath,
            FileCollisionPolicy.RenameWithSuffix or _ =>
                FindAvailableNumberedPath(dir, candidateBaseName, resolvedExt, sourcePath),
        };
    }

    private static string FindAvailableNumberedPath(string directory, string baseName, string ext, string sourcePath)
    {
        for (var i = 1; i <= 9999; i++)
        {
            var candidate = Path.Combine(directory, $"{baseName}_{i}.{ext}");
            if (!File.Exists(candidate) && !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return Path.Combine(directory, $"{baseName}_{Guid.NewGuid():N}.{ext}");
    }

    /// <summary>
    /// Processes a single batch item asynchronously with cancellation and atomic file write safety.
    /// </summary>
    public static async Task ProcessItemAsync(
        BatchResizeItem item,
        BatchResizeSettings settings,
        Preset preset,
        UserDataStore store,
        ILocalizer loc,
        CancellationToken cancellationToken,
        ILogger? log = null)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            item.Status = BatchItemStatus.Cancelled;
            item.ErrorMessage = "Cancelled";
            return;
        }

        item.Status = BatchItemStatus.Processing;
        item.ErrorMessage = null;
        item.Progress = 0.1;

        string? tempFilePath = null;
        try
        {
            if (!File.Exists(item.SourcePath))
            {
                item.Status = BatchItemStatus.Failed;
                item.ErrorMessage = loc["error.file_not_found"];
                return;
            }

            var srcFi = new FileInfo(item.SourcePath);
            if (srcFi.Length == 0)
            {
                item.Status = BatchItemStatus.Failed;
                item.ErrorMessage = loc["error.empty_file"];
                return;
            }

            // Inspect source if dimensions unknown
            if (item.SourceWidth <= 0 || item.SourceHeight <= 0)
            {
                var (w, h, sz) = InspectImage(item.SourcePath);
                item.SourceWidth = w;
                item.SourceHeight = h;
                item.SourceFileSizeBytes = sz;
                item.SourceDimensionsText = $"{w} × {h} px";
                item.SourceFileSizeText = ResizeService.FormatFileSize(sz);
            }

            var (targetW, targetH) = CalculateTargetDimensions(item.SourceWidth, item.SourceHeight, settings);
            item.TargetWidth = targetW;
            item.TargetHeight = targetH;
            item.TargetDimensionsText = $"{targetW} × {targetH} px";

            var destinationPath = ResolveDestinationPath(
                item.SourcePath,
                settings.TargetFormat,
                settings.OutputDirectory,
                settings.CollisionPolicy);

            if (destinationPath is null)
            {
                item.Status = BatchItemStatus.Skipped;
                item.ErrorMessage = loc["toast.skipped"];
                item.Progress = 1.0;
                return;
            }

            var targetDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            // Atomic temp file in the same directory to avoid corrupted outputs if interrupted
            tempFilePath = destinationPath + $".tmp.{Guid.NewGuid():N}";

            var resizeOptions = new ResizeOptions
            {
                TargetWidth = targetW,
                TargetHeight = targetH,
                MaintainAspectRatio = settings.MaintainAspectRatio,
                TargetFormat = settings.TargetFormat,
                Quality = settings.Quality,
            };

            cancellationToken.ThrowIfCancellationRequested();
            item.Progress = 0.4;

            // Execute resize & compression to temp file
            var result = await ResizeService.ResizeFileAsync(
                item.SourcePath,
                resizeOptions,
                preset,
                store,
                loc,
                log,
                customOutputPath: tempFilePath);

            cancellationToken.ThrowIfCancellationRequested();

            if (result.Status == ResizeStatus.Success && File.Exists(tempFilePath))
            {
                // Verify output file is not 0 bytes
                var fi = new FileInfo(tempFilePath);
                if (fi.Length == 0)
                    throw new IOException("Generated output file is empty.");

                // Move temp file to final destination
                File.Move(tempFilePath, destinationPath, overwrite: true);
                tempFilePath = null; // Successfully transferred

                item.OutputPath = destinationPath;
                item.Status = BatchItemStatus.Success;
                item.Progress = 1.0;
            }
            else
            {
                item.Status = BatchItemStatus.Failed;
                item.ErrorMessage = result.ErrorDetail ?? loc[result.ErrorKey ?? "error.conversion_failed"];
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = BatchItemStatus.Cancelled;
            item.ErrorMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "Failed to process batch item {File}", item.SourcePath);
            item.Status = BatchItemStatus.Failed;
            item.ErrorMessage = ex.Message;
        }
        finally
        {
            // Safety: Always clean up any leftover partial temp file
            if (tempFilePath is not null && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); }
                catch { /* Ignore temp cleanup errors */ }
            }
        }
    }

    /// <summary>
    /// Processes a full batch queue sequentially, updating progress and recording history upon completion.
    /// </summary>
    public static async Task<BatchSummary> ProcessBatchAsync(
        IReadOnlyList<BatchResizeItem> items,
        BatchResizeSettings settings,
        Preset preset,
        UserDataStore store,
        ILocalizer loc,
        CancellationToken cancellationToken,
        Action<int, int>? onProgress = null,
        ILogger? log = null)
    {
        var processedSources = new List<string>();
        var outputs = new List<string>();
        var failures = new List<HistoryFailure>();
        var skippedCount = 0;
        var cancelledCount = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (cancellationToken.IsCancellationRequested)
            {
                // Mark remaining unstarted items as Cancelled
                for (var r = i; r < items.Count; r++)
                {
                    if (items[r].Status == BatchItemStatus.Queued)
                    {
                        items[r].Status = BatchItemStatus.Cancelled;
                        items[r].ErrorMessage = "Cancelled";
                        cancelledCount++;
                    }
                }
                break;
            }

            onProgress?.Invoke(i + 1, items.Count);

            await ProcessItemAsync(item, settings, preset, store, loc, cancellationToken, log);

            processedSources.Add(item.SourcePath);

            switch (item.Status)
            {
                case BatchItemStatus.Success:
                    if (item.OutputPath is not null)
                        outputs.Add(item.OutputPath);
                    break;
                case BatchItemStatus.Skipped:
                    skippedCount++;
                    break;
                case BatchItemStatus.Cancelled:
                    cancelledCount++;
                    break;
                case BatchItemStatus.Failed:
                default:
                    failures.Add(new HistoryFailure
                    {
                        Source = item.SourcePath,
                        ErrorKey = "error.conversion_failed",
                        ErrorDetail = item.ErrorMessage,
                    });
                    break;
            }
        }

        // Record history entry
        if (store.Settings.KeepHistory && processedSources.Count > 0)
        {
            var state = failures.Count == 0 && cancelledCount == 0
                ? JobState.Completed
                : (outputs.Count > 0 ? JobState.CompletedWithErrors : (cancelledCount > 0 ? JobState.Cancelled : JobState.Failed));

            store.AddHistory(new HistoryEntry
            {
                JobId = Guid.NewGuid().ToString("N")[..12],
                FinishedAt = DateTime.Now,
                PresetId = preset.Id,
                PresetName = loc.DisplayName(preset),
                State = state,
                Sources = [.. processedSources],
                Outputs = [.. outputs],
                Failures = [.. failures],
                Preset = null,
            });
        }

        return new BatchSummary(
            TotalCount: items.Count,
            SuccessCount: outputs.Count,
            FailedCount: failures.Count,
            SkippedCount: skippedCount,
            CancelledCount: cancelledCount,
            Outputs: outputs,
            Failures: failures);
    }
}
