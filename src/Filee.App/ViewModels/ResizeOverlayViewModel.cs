// ViewModel for interactive image resizing and compression overlay window.

using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Conversion;
using Filee.Core.History;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class ResizeOverlayViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<string> _files;
    private readonly Preset _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private readonly List<string> _processedSources = [];
    private readonly List<string> _outputs = [];
    private readonly List<HistoryFailure> _failures = [];
    private bool _closed;
    private bool _historyRecorded;
    private CancellationTokenSource? _previewCts;
    private bool _isUpdatingDimensions;

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private string _currentFileName = "";
    [ObservableProperty] private string _counterText = "";
    [ObservableProperty] private bool _hasMultipleFiles;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _statusNotice = "";

    // Dimensions
    [ObservableProperty] private int _originalWidth;
    [ObservableProperty] private int _originalHeight;
    [ObservableProperty] private long _originalFileSizeBytes;
    [ObservableProperty] private string _originalFileSizeText = "";
    [ObservableProperty] private string _originalDimensionsText = "";

    [ObservableProperty] private int _targetWidth = 1000;
    [ObservableProperty] private int _targetHeight = 1000;
    [ObservableProperty] private string _dimensionsText = "";
    [ObservableProperty] private bool _maintainAspectRatio = true;

    // Quality & Format
    [ObservableProperty] private string _targetFormat = "same"; // "same", "png", "jpg", "webp"
    [ObservableProperty] private int _quality = 85;
    [ObservableProperty] private string _estimatedSizeText = "";
    [ObservableProperty] private bool _hasTransparency;

    public bool WillLoseTransparency => HasTransparency && (ResolvedFormat is "jpg" or "jpeg" or "bmp");

    public ObservableCollection<string> Problems { get; } = [];

    public bool IsEditing => !IsFinished;
    public bool HasStatusNotice => !string.IsNullOrEmpty(StatusNotice);

    public bool IsFormatSame => TargetFormat == "same";
    public bool IsFormatPng => TargetFormat == "png";
    public bool IsFormatJpg => TargetFormat == "jpg";
    public bool IsFormatWebp => TargetFormat == "webp";

    public bool IsLossySupported => ResizeService.IsLossyFormat(ResolvedFormat);

    public string ResolvedFormat => CurrentIndex < _files.Count
        ? ResizeService.ResolveFormat(_files[CurrentIndex], TargetFormat)
        : TargetFormat;

    public bool IsQualityLow => Quality == 60;
    public bool IsQualityBalanced => Quality == 80;
    public bool IsQualityHigh => Quality == 92;

    public bool IsScale25 => OriginalWidth > 0 && Math.Abs((double)TargetWidth / OriginalWidth - 0.25) < 0.02;
    public bool IsScale50 => OriginalWidth > 0 && Math.Abs((double)TargetWidth / OriginalWidth - 0.50) < 0.02;
    public bool IsScale75 => OriginalWidth > 0 && Math.Abs((double)TargetWidth / OriginalWidth - 0.75) < 0.02;
    public bool IsScale100 => OriginalWidth > 0 && Math.Abs((double)TargetWidth / OriginalWidth - 1.00) < 0.02;

    public event Action? CloseRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;

    public ResizeOverlayViewModel(
        IReadOnlyList<string> files,
        Preset preset,
        ILocalizer loc,
        UserDataStore store,
        ILogger? log = null)
    {
        _files = files;
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;
        _hasMultipleFiles = files.Count > 1;

        _ = LoadFileAsync(0);
    }

    public ResizeOptions CurrentOptions => new()
    {
        TargetWidth = TargetWidth,
        TargetHeight = TargetHeight,
        MaintainAspectRatio = MaintainAspectRatio,
        TargetFormat = TargetFormat,
        Quality = Quality,
    };

    partial void OnTargetWidthChanged(int value)
    {
        if (_isUpdatingDimensions)
            return;

        if (MaintainAspectRatio && OriginalWidth > 0 && OriginalHeight > 0 && value > 0)
        {
            _isUpdatingDimensions = true;
            try
            {
                double ratio = (double)OriginalWidth / OriginalHeight;
                TargetHeight = Math.Clamp((int)Math.Round(value / ratio), ResizeService.MinDimension, ResizeService.MaxDimension);
            }
            finally
            {
                _isUpdatingDimensions = false;
            }
        }
        NotifyScaleProperties();
        SchedulePreviewUpdate();
    }

    partial void OnTargetHeightChanged(int value)
    {
        if (_isUpdatingDimensions)
            return;

        if (MaintainAspectRatio && OriginalWidth > 0 && OriginalHeight > 0 && value > 0)
        {
            _isUpdatingDimensions = true;
            try
            {
                double ratio = (double)OriginalWidth / OriginalHeight;
                TargetWidth = Math.Clamp((int)Math.Round(value * ratio), ResizeService.MinDimension, ResizeService.MaxDimension);
            }
            finally
            {
                _isUpdatingDimensions = false;
            }
        }
        NotifyScaleProperties();
        SchedulePreviewUpdate();
    }

    partial void OnMaintainAspectRatioChanged(bool value)
    {
        if (value && OriginalWidth > 0 && OriginalHeight > 0)
        {
            // Snap height to match aspect ratio
            _isUpdatingDimensions = true;
            try
            {
                double ratio = (double)OriginalWidth / OriginalHeight;
                TargetHeight = Math.Clamp((int)Math.Round(TargetWidth / ratio), ResizeService.MinDimension, ResizeService.MaxDimension);
            }
            finally
            {
                _isUpdatingDimensions = false;
            }
            NotifyScaleProperties();
            SchedulePreviewUpdate();
        }
    }

    partial void OnTargetFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsFormatSame));
        OnPropertyChanged(nameof(IsFormatPng));
        OnPropertyChanged(nameof(IsFormatJpg));
        OnPropertyChanged(nameof(IsFormatWebp));
        OnPropertyChanged(nameof(ResolvedFormat));
        OnPropertyChanged(nameof(IsLossySupported));
        OnPropertyChanged(nameof(WillLoseTransparency));
        SchedulePreviewUpdate();
    }

    partial void OnHasTransparencyChanged(bool value)
    {
        OnPropertyChanged(nameof(WillLoseTransparency));
    }

    partial void OnQualityChanged(int value)
    {
        OnPropertyChanged(nameof(IsQualityLow));
        OnPropertyChanged(nameof(IsQualityBalanced));
        OnPropertyChanged(nameof(IsQualityHigh));
        SchedulePreviewUpdate();
    }

    private void NotifyScaleProperties()
    {
        DimensionsText = $"{TargetWidth} × {TargetHeight} px";
        OnPropertyChanged(nameof(IsScale25));
        OnPropertyChanged(nameof(IsScale50));
        OnPropertyChanged(nameof(IsScale75));
        OnPropertyChanged(nameof(IsScale100));
    }

    public async Task LoadFileAsync(int index)
    {
        if (index < 0 || index >= _files.Count)
        {
            FinishAndClose();
            return;
        }

        IsBusy = true;
        CurrentIndex = index;
        var path = _files[index];
        CurrentFileName = Path.GetFileName(path);
        CounterText = _loc.Format("crop.page_counter", index + 1, _files.Count);

        try
        {
            var fileInfo = new FileInfo(path);
            OriginalFileSizeBytes = fileInfo.Length;
            OriginalFileSizeText = ResizeService.FormatFileSize(OriginalFileSizeBytes);

            using var source = ResizeService.LoadSourceBitmap(path);
            OriginalWidth = source.Width;
            OriginalHeight = source.Height;
            OriginalDimensionsText = $"{OriginalWidth} × {OriginalHeight} px";

            _isUpdatingDimensions = true;
            TargetWidth = OriginalWidth;
            TargetHeight = OriginalHeight;
            _isUpdatingDimensions = false;
            NotifyScaleProperties();

            await RefreshPreviewAsync();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load resize preview for {File}", path);
            _processedSources.Add(path);
            var unsupported = ex is NotSupportedException;
            AddFailure(path, unsupported ? "error.unsupported_source" : "error.conversion_failed", unsupported ? null : ex.Message);

            if (index < _files.Count - 1)
                await LoadFileAsync(index + 1);
            else
                FinishAndClose();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SchedulePreviewUpdate()
    {
        if (_closed || IsFinished || CurrentIndex >= _files.Count)
            return;

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(40, token);
                if (token.IsCancellationRequested)
                    return;

                var info = await ResizeService.GeneratePreviewAsync(_files[CurrentIndex], CurrentOptions);
                if (token.IsCancellationRequested)
                {
                    info.PreviewBitmap.Dispose();
                    return;
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || _closed)
                    {
                        info.PreviewBitmap.Dispose();
                        return;
                    }
                    var old = PreviewImage;
                    PreviewImage = info.PreviewBitmap;
                    old?.Dispose();
                    DimensionsText = $"{info.TargetWidth} × {info.TargetHeight} px";
                    EstimatedSizeText = $"~{ResizeService.FormatFileSize(info.EstimatedBytes)}";
                    HasTransparency = info.HasTransparency;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Preview update cancelled or failed");
            }
        }, token);
    }

    public async Task RefreshPreviewAsync()
    {
        if (CurrentIndex >= _files.Count)
            return;

        var path = _files[CurrentIndex];
        var info = await ResizeService.GeneratePreviewAsync(path, CurrentOptions);
        var old = PreviewImage;
        PreviewImage = info.PreviewBitmap;
        old?.Dispose();
        DimensionsText = $"{info.TargetWidth} × {info.TargetHeight} px";
        EstimatedSizeText = $"~{ResizeService.FormatFileSize(info.EstimatedBytes)}";
        HasTransparency = info.HasTransparency;
    }

    [RelayCommand]
    public void SetScale(double factor)
    {
        if (OriginalWidth <= 0 || OriginalHeight <= 0)
            return;

        var (w, h) = ResizeService.ScaleDimensions(OriginalWidth, OriginalHeight, factor);
        _isUpdatingDimensions = true;
        TargetWidth = w;
        TargetHeight = h;
        _isUpdatingDimensions = false;
        NotifyScaleProperties();
        SchedulePreviewUpdate();
    }

    [RelayCommand]
    public void ToggleAspectRatio() => MaintainAspectRatio = !MaintainAspectRatio;

    [RelayCommand]
    public void SetFormat(string format) => TargetFormat = format;

    [RelayCommand]
    public void SetQuality(int quality) => Quality = quality;

    [RelayCommand]
    public async Task ResizeAndSaveAsync()
    {
        if (IsBusy || IsFinished || _closed || CurrentIndex >= _files.Count)
            return;

        IsBusy = true;
        var path = _files[CurrentIndex];
        _processedSources.Add(path);

        try
        {
            var result = await ResizeService.ResizeFileAsync(path, CurrentOptions, _preset, _store, _loc, _log);
            if (result.Status == ResizeStatus.Success && result.OutputPath is not null)
                _outputs.Add(result.OutputPath);
            else if (result.Status == ResizeStatus.SkippedExists)
                AddProblem(path, "toast.skipped");
            else
                AddFailure(path, result.ErrorKey ?? "error.conversion_failed", result.ErrorDetail);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to resize {File}", path);
            AddFailure(path, "error.conversion_failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (_closed)
        {
            RecordHistoryOnce();
            return;
        }

        if (CurrentIndex < _files.Count - 1)
            await LoadFileAsync(CurrentIndex + 1);
        else
            FinishAndClose();
    }

    [RelayCommand]
    public async Task SaveAsAsync()
    {
        if (IsBusy || IsFinished || _closed || CurrentIndex >= _files.Count || SaveAsRequested is null)
            return;

        var path = _files[CurrentIndex];
        var ext = ResolvedFormat;
        var defaultName = Path.GetFileNameWithoutExtension(path) + "_resized." + ext;
        var dest = await SaveAsRequested.Invoke(defaultName);
        if (string.IsNullOrEmpty(dest))
            return;

        IsBusy = true;
        _processedSources.Add(path);

        try
        {
            var result = await ResizeService.ResizeFileAsync(path, CurrentOptions, _preset, _store, _loc, _log, customOutputPath: dest);
            if (result.Status == ResizeStatus.Success && result.OutputPath is not null)
                _outputs.Add(result.OutputPath);
            else
                AddFailure(path, result.ErrorKey ?? "error.conversion_failed", result.ErrorDetail);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to resize {File}", path);
            AddFailure(path, "error.conversion_failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (CurrentIndex < _files.Count - 1)
            await LoadFileAsync(CurrentIndex + 1);
        else
            FinishAndClose();
    }

    [RelayCommand]
    public async Task SkipAsync()
    {
        if (IsBusy || IsFinished || _closed)
            return;

        if (CurrentIndex < _files.Count - 1)
            await LoadFileAsync(CurrentIndex + 1);
        else
            FinishAndClose();
    }

    [RelayCommand]
    public void Reset()
    {
        TargetFormat = "same";
        Quality = 85;
        MaintainAspectRatio = true;
        if (OriginalWidth > 0 && OriginalHeight > 0)
        {
            _isUpdatingDimensions = true;
            TargetWidth = OriginalWidth;
            TargetHeight = OriginalHeight;
            _isUpdatingDimensions = false;
            NotifyScaleProperties();
            SchedulePreviewUpdate();
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        Shutdown();
        CloseRequested?.Invoke();
    }

    private void AddFailure(string path, string errorKey, string? detail)
    {
        _failures.Add(new HistoryFailure { Source = path, ErrorKey = errorKey, ErrorDetail = detail });
        AddProblem(path, errorKey);
    }

    private void AddProblem(string path, string key)
    {
        var line = $"{Path.GetFileName(path)}: {_loc[key]}";
        Problems.Add(line);
        StatusNotice = line;
    }

    private void FinishAndClose()
    {
        RecordHistoryOnce();
        if (Problems.Count == 0)
        {
            CloseRequested?.Invoke();
            return;
        }
        Summary = _failures.Count > 0
            ? _loc.Format("toast.done_errors", _failures.Count, _processedSources.Count)
            : _loc["toast.done"];
        StatusNotice = "";
        HasMultipleFiles = false;
        var old = PreviewImage;
        PreviewImage = null;
        old?.Dispose();
        IsFinished = true;
        OnPropertyChanged(nameof(IsEditing));
    }

    private void Shutdown()
    {
        _closed = true;
        _previewCts?.Cancel();
        if (!IsBusy)
            RecordHistoryOnce();
    }

    private void RecordHistoryOnce()
    {
        if (_historyRecorded || !_store.Settings.KeepHistory || _processedSources.Count == 0)
            return;

        _historyRecorded = true;
        var state = _failures.Count == 0
            ? JobState.Completed
            : (_outputs.Count > 0 ? JobState.CompletedWithErrors : JobState.Failed);

        _store.AddHistory(new HistoryEntry
        {
            JobId = Guid.NewGuid().ToString("N")[..12],
            FinishedAt = DateTime.Now,
            PresetId = _preset.Id,
            PresetName = _loc.DisplayName(_preset),
            State = state,
            Sources = [.. _processedSources],
            Outputs = [.. _outputs],
            Failures = [.. _failures],
            Preset = null,
        });
    }

    public void Dispose()
    {
        Shutdown();
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
