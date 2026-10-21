// ViewModel for the batch image processing window: queue management, shared settings, and asynchronous execution.

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class BatchResizeViewModel : ObservableObject, IDisposable
{
    private readonly Preset _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private CancellationTokenSource? _batchCts;
    private bool _disposed;

    public ObservableCollection<BatchResizeItem> Queue { get; } = [];

    // Settings
    [ObservableProperty] private BatchResizeMode _mode = BatchResizeMode.ScalePercentage;
    [ObservableProperty] private double _scalePercent = 100.0;
    [ObservableProperty] private int _maxWidth = 1920;
    [ObservableProperty] private int _maxHeight = 1080;
    [ObservableProperty] private bool _maintainAspectRatio = true;
    [ObservableProperty] private string _targetFormat = "same";
    [ObservableProperty] private int _quality = 85;
    [ObservableProperty] private string? _outputDirectory;
    [ObservableProperty] private FileCollisionPolicy _collisionPolicy = FileCollisionPolicy.RenameWithSuffix;

    public int Compression
    {
        get => Math.Clamp(100 - Quality, 5, 95);
        set
        {
            var q = Math.Clamp(100 - value, 10, 100);
            if (Quality != q)
            {
                Quality = q;
            }
        }
    }

    // Execution State
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private double _overallProgress; // 0..100
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _statusNotice = "";
    [ObservableProperty] private string? _lastOutputFolder;

    public bool CanEdit => !IsProcessing;
    public bool CanStart => !IsProcessing && Queue.Count > 0;
    public bool HasItems => Queue.Count > 0;
    public bool IsQueueEmpty => Queue.Count == 0;
    public int QueueCount => Queue.Count;

    public bool IsModeScale => Mode == BatchResizeMode.ScalePercentage;
    public bool IsModeFit => Mode == BatchResizeMode.FitBoundingBox;
    public bool IsModeExact => Mode == BatchResizeMode.ExactDimensions;

    public bool IsScale25 => Math.Abs(ScalePercent - 25.0) < 0.5;
    public bool IsScale50 => Math.Abs(ScalePercent - 50.0) < 0.5;
    public bool IsScale75 => Math.Abs(ScalePercent - 75.0) < 0.5;
    public bool IsScale100 => Math.Abs(ScalePercent - 100.0) < 0.5;

    public bool IsFormatSame => TargetFormat == "same";
    public bool IsFormatPng => TargetFormat == "png";
    public bool IsFormatJpg => TargetFormat == "jpg";
    public bool IsFormatWebp => TargetFormat == "webp";

    public bool IsQualityLow => Quality == 60 || Compression <= 25;
    public bool IsQualityBalanced => Quality == 80 || (Compression > 25 && Compression <= 60);
    public bool IsQualityHigh => Quality == 92 || Compression > 60;

    public bool IsCollisionRename => CollisionPolicy == FileCollisionPolicy.RenameWithSuffix;
    public bool IsCollisionSkip => CollisionPolicy == FileCollisionPolicy.Skip;
    public bool IsCollisionOverwrite => CollisionPolicy == FileCollisionPolicy.Overwrite;

    public string OutputDirectoryText => !string.IsNullOrWhiteSpace(OutputDirectory)
        ? OutputDirectory
        : _loc["batch.same_folder"];

    public bool HasCustomOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public event Action? CloseRequested;
    public event Func<Task<IReadOnlyList<string>?>>? AddFilesRequested;
    public event Func<Task<string?>>? AddFolderRequested;
    public event Func<Task<string?>>? BrowseFolderRequested;
    public event Action<string>? OpenFolderRequested;

    public BatchResizeViewModel(
        IReadOnlyList<string> initialFiles,
        Preset preset,
        ILocalizer loc,
        UserDataStore store,
        ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        TargetFormat = store.Settings.DefaultImageFormat;
        Quality = store.Settings.EffectiveDefaultCompressionQuality;
        MaintainAspectRatio = store.Settings.DefaultMaintainAspectRatio;
        if (!string.IsNullOrWhiteSpace(store.Settings.LastOutputDirectory) && Directory.Exists(store.Settings.LastOutputDirectory))
            OutputDirectory = store.Settings.LastOutputDirectory;

        AddFiles(initialFiles);
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsQueueEmpty));
            OnPropertyChanged(nameof(QueueCount));
        };
    }

    public BatchResizeSettings CurrentSettings => new()
    {
        Mode = Mode,
        ScalePercent = ScalePercent,
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        MaintainAspectRatio = MaintainAspectRatio,
        TargetFormat = TargetFormat,
        Quality = Quality,
        OutputDirectory = OutputDirectory,
        CollisionPolicy = CollisionPolicy,
    };

    public void AddFiles(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Queue.Select(i => Path.GetFullPath(i.SourcePath)), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (File.Exists(path) && BatchResizeService.IsSupportedImage(path) && !existing.Contains(Path.GetFullPath(path)))
            {
                var item = new BatchResizeItem { SourcePath = path };
                Queue.Add(item);
                existing.Add(Path.GetFullPath(path));
                _ = PopulateItemMetadataAsync(item);
            }
        }
    }

    private async Task PopulateItemMetadataAsync(BatchResizeItem item)
    {
        try
        {
            var (w, h, sz) = await Task.Run(() => BatchResizeService.InspectImage(item.SourcePath));
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.SourceWidth = w;
                item.SourceHeight = h;
                item.SourceFileSizeBytes = sz;
                item.SourceDimensionsText = $"{w} × {h} px";
                item.SourceFileSizeText = ResizeService.FormatFileSize(sz);

                var (targetW, targetH) = BatchResizeService.CalculateTargetDimensions(w, h, CurrentSettings);
                item.TargetWidth = targetW;
                item.TargetHeight = targetH;
                item.TargetDimensionsText = $"{targetW} × {targetH} px";
            });
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Could not inspect image metadata for {Path}", item.SourcePath);
        }
    }

    partial void OnModeChanged(BatchResizeMode value)
    {
        OnPropertyChanged(nameof(IsModeScale));
        OnPropertyChanged(nameof(IsModeFit));
        OnPropertyChanged(nameof(IsModeExact));
        UpdateAllItemTargets();
    }

    partial void OnScalePercentChanged(double value)
    {
        OnPropertyChanged(nameof(IsScale25));
        OnPropertyChanged(nameof(IsScale50));
        OnPropertyChanged(nameof(IsScale75));
        OnPropertyChanged(nameof(IsScale100));
        UpdateAllItemTargets();
    }

    partial void OnMaxWidthChanged(int value) => UpdateAllItemTargets();
    partial void OnMaxHeightChanged(int value) => UpdateAllItemTargets();
    partial void OnMaintainAspectRatioChanged(bool value) => UpdateAllItemTargets();

    partial void OnTargetFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsFormatSame));
        OnPropertyChanged(nameof(IsFormatPng));
        OnPropertyChanged(nameof(IsFormatJpg));
        OnPropertyChanged(nameof(IsFormatWebp));
    }

    partial void OnQualityChanged(int value)
    {
        OnPropertyChanged(nameof(Compression));
        OnPropertyChanged(nameof(IsQualityLow));
        OnPropertyChanged(nameof(IsQualityBalanced));
        OnPropertyChanged(nameof(IsQualityHigh));
    }

    partial void OnCollisionPolicyChanged(FileCollisionPolicy value)
    {
        OnPropertyChanged(nameof(IsCollisionRename));
        OnPropertyChanged(nameof(IsCollisionSkip));
        OnPropertyChanged(nameof(IsCollisionOverwrite));
    }

    partial void OnOutputDirectoryChanged(string? value)
    {
        OnPropertyChanged(nameof(OutputDirectoryText));
        OnPropertyChanged(nameof(HasCustomOutputDirectory));
    }

    partial void OnIsProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanStart));
    }

    private void UpdateAllItemTargets()
    {
        var settings = CurrentSettings;
        foreach (var item in Queue)
        {
            if (item.SourceWidth > 0 && item.SourceHeight > 0)
            {
                var (targetW, targetH) = BatchResizeService.CalculateTargetDimensions(item.SourceWidth, item.SourceHeight, settings);
                item.TargetWidth = targetW;
                item.TargetHeight = targetH;
                item.TargetDimensionsText = $"{targetW} × {targetH} px";
            }
        }
    }

    [RelayCommand]
    public void SetMode(string modeName)
    {
        Mode = modeName switch
        {
            "Fit" => BatchResizeMode.FitBoundingBox,
            "Exact" => BatchResizeMode.ExactDimensions,
            _ => BatchResizeMode.ScalePercentage,
        };
    }

    [RelayCommand]
    public void SetScale(double factor) => ScalePercent = factor * 100.0;

    [RelayCommand]
    public void SetFormat(string format) => TargetFormat = format;

    [RelayCommand]
    public void SetCompression(int compression) => Compression = compression;

    [RelayCommand]
    public void SetQuality(int q) => Quality = q;

    [RelayCommand]
    public void SetCollisionPolicy(string policy)
    {
        CollisionPolicy = policy switch
        {
            "Skip" => FileCollisionPolicy.Skip,
            "Overwrite" => FileCollisionPolicy.Overwrite,
            _ => FileCollisionPolicy.RenameWithSuffix,
        };
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        if (IsProcessing || AddFilesRequested is null)
            return;

        var files = await AddFilesRequested.Invoke();
        if (files is { Count: > 0 })
            AddFiles(files);
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        if (IsProcessing || AddFolderRequested is null)
            return;

        var folder = await AddFolderRequested.Invoke();
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(BatchResizeService.IsSupportedImage)
                .ToList();
            AddFiles(files);
        }
    }

    [RelayCommand]
    public void RemoveItem(BatchResizeItem item)
    {
        if (IsProcessing)
            return;
        Queue.Remove(item);
    }

    [RelayCommand]
    public void ClearQueue()
    {
        if (IsProcessing)
            return;
        Queue.Clear();
        IsFinished = false;
        ProgressText = "";
        SummaryText = "";
        StatusNotice = "";
    }

    [RelayCommand]
    public async Task BrowseOutputDirectoryAsync()
    {
        if (IsProcessing || BrowseFolderRequested is null)
            return;

        var selected = await BrowseFolderRequested.Invoke();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            OutputDirectory = selected;
            _store.Settings.LastOutputDirectory = selected;
            _store.SaveSettings();
        }
    }

    [RelayCommand]
    public void ResetOutputDirectory() => OutputDirectory = null;

    [RelayCommand]
    public async Task StartBatchAsync()
    {
        if (IsProcessing || Queue.Count == 0)
            return;

        IsProcessing = true;
        IsFinished = false;
        ProcessedCount = 0;
        SuccessCount = 0;
        FailedCount = 0;
        SkippedCount = 0;
        OverallProgress = 0;
        StatusNotice = "";
        SummaryText = "";

        _batchCts?.Cancel();
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;

        var settings = CurrentSettings;
        var total = Queue.Count;

        try
        {
            var summary = await Task.Run(async () =>
            {
                return await BatchResizeService.ProcessBatchAsync(
                    Queue,
                    settings,
                    _preset,
                    _store,
                    _loc,
                    token,
                    onProgress: (current, max) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            ProcessedCount = current;
                            OverallProgress = max > 0 ? (double)current / max * 100.0 : 0;
                            ProgressText = _loc.Format("crop.page_counter", current, max);
                        });
                    },
                    log: _log);
            }, token);

            SuccessCount = summary.SuccessCount;
            FailedCount = summary.FailedCount;
            SkippedCount = summary.SkippedCount;
            OverallProgress = 100.0;

            if (summary.Outputs.Count > 0)
                LastOutputFolder = Path.GetDirectoryName(summary.Outputs[0]);

            SummaryText = _loc.Format(
                "batch.summary_format",
                summary.SuccessCount,
                summary.FailedCount,
                summary.SkippedCount);

            StatusNotice = summary.FailedCount > 0
                ? _loc.Format("toast.done_errors", summary.FailedCount, total)
                : _loc["toast.done"];
        }
        catch (OperationCanceledException)
        {
            StatusNotice = _loc["toast.canceled"];
            SummaryText = "Batch cancelled by user.";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Unexpected error during batch processing");
            StatusNotice = ex.Message;
        }
        finally
        {
            IsProcessing = false;
            IsFinished = true;
        }
    }

    [RelayCommand]
    public void CancelBatch()
    {
        if (IsProcessing)
        {
            _batchCts?.Cancel();
        }
        else
        {
            CloseRequested?.Invoke();
        }
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        var folder = OutputDirectory ?? LastOutputFolder;
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            OpenFolderRequested?.Invoke(folder);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _batchCts?.Cancel();
        _batchCts?.Dispose();
    }
}
