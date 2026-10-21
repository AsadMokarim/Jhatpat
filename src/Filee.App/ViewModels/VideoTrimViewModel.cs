// ViewModel for Video Trimming: filmstrip timeline, start/end handles, playback, and export.

using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.ViewModels;

public sealed partial class VideoTrimViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private readonly DispatcherTimer _playTimer;
    private CancellationTokenSource? _previewCts;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    public ObservableCollection<Bitmap> FilmstripThumbnails { get; } = [];

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private double _totalDurationSeconds;
    [ObservableProperty] private string _totalDurationText = "00:00.00";

    // Playhead and Trim Range
    [ObservableProperty] private double _startTimeSeconds;
    [ObservableProperty] private double _endTimeSeconds;
    [ObservableProperty] private double _currentTimeSeconds;

    [ObservableProperty] private string _startTimeText = "00:00.00";
    [ObservableProperty] private string _endTimeText = "00:00.00";
    [ObservableProperty] private string _currentTimeText = "00:00.00";
    [ObservableProperty] private string _trimDurationText = "00:00.00";

    // Video Frame Preview
    [ObservableProperty] private Bitmap? _currentFrameBitmap;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isLoadingFilmstrip;

    // Execution state
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public VideoTrimViewModel(string videoPath, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        FilePath = videoPath;
        FileName = Path.GetFileName(videoPath);

        _playTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playTimer.Tick += OnPlayTimerTick;

        LoadVideoAsync(videoPath);
    }

    private async void LoadVideoAsync(string path)
    {
        if (!File.Exists(path)) return;

        IsLoadingFilmstrip = true;
        StatusMessage = "Extracting video timeline...";

        try
        {
            var meta = await Task.Run(() => VideoEngine.ProbeVideoAsync(path));
            TotalDurationSeconds = meta.DurationSeconds;
            TotalDurationText = FormatTimecode(TotalDurationSeconds);

            StartTimeSeconds = 0.0;
            EndTimeSeconds = TotalDurationSeconds;
            CurrentTimeSeconds = 0.0;

            UpdateTimecodeStrings();

            // Extract initial frame
            await RequestFramePreviewAsync(0.0);

            // Extract filmstrip thumbnails in background
            var thumbs = await Task.Run(() => VideoEngine.ExtractFilmstripAsync(path, 10, 80));
            FilmstripThumbnails.Clear();
            foreach (var skBmp in thumbs)
            {
                using (skBmp)
                using (var image = SKImage.FromBitmap(skBmp))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 80))
                using (var mem = new MemoryStream())
                {
                    data.SaveTo(mem);
                    mem.Position = 0;
                    FilmstripThumbnails.Add(new Bitmap(mem));
                }
            }

            StatusMessage = $"Video loaded: {TotalDurationText}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load video: {Path}", path);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingFilmstrip = false;
        }
    }

    private void UpdateTimecodeStrings()
    {
        StartTimeText = FormatTimecode(StartTimeSeconds);
        EndTimeText = FormatTimecode(EndTimeSeconds);
        CurrentTimeText = FormatTimecode(CurrentTimeSeconds);
        TrimDurationText = FormatTimecode(Math.Max(0, EndTimeSeconds - StartTimeSeconds));
    }

    partial void OnStartTimeSecondsChanged(double value)
    {
        if (value > EndTimeSeconds)
            StartTimeSeconds = EndTimeSeconds;
        UpdateTimecodeStrings();
        if (CurrentTimeSeconds < StartTimeSeconds)
        {
            CurrentTimeSeconds = StartTimeSeconds;
            _ = RequestFramePreviewAsync(CurrentTimeSeconds);
        }
    }

    partial void OnEndTimeSecondsChanged(double value)
    {
        if (value < StartTimeSeconds)
            EndTimeSeconds = StartTimeSeconds;
        UpdateTimecodeStrings();
        if (CurrentTimeSeconds > EndTimeSeconds)
        {
            CurrentTimeSeconds = EndTimeSeconds;
            _ = RequestFramePreviewAsync(CurrentTimeSeconds);
        }
    }

    partial void OnCurrentTimeSecondsChanged(double value)
    {
        UpdateTimecodeStrings();
        _ = RequestFramePreviewAsync(value);
    }

    private async Task RequestFramePreviewAsync(double time)
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        try
        {
            var skBmp = await Task.Run(() => VideoEngine.ExtractFrameAsync(FilePath, time, 480, token), token);
            if (skBmp is null || token.IsCancellationRequested)
            {
                skBmp?.Dispose();
                return;
            }

            using (skBmp)
            using (var img = SKImage.FromBitmap(skBmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 85))
            using (var mem = new MemoryStream())
            {
                data.SaveTo(mem);
                mem.Position = 0;
                var avBmp = new Bitmap(mem);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        var old = CurrentFrameBitmap;
                        CurrentFrameBitmap = avBmp;
                        old?.Dispose();
                    }
                    else
                    {
                        avBmp.Dispose();
                    }
                });
            }
        }
        catch
        {
            // Ignore preview cancellation
        }
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            _playTimer.Stop();
            IsPlaying = false;
        }
        else
        {
            if (CurrentTimeSeconds >= EndTimeSeconds)
                CurrentTimeSeconds = StartTimeSeconds;
            _playTimer.Start();
            IsPlaying = true;
        }
    }

    private void OnPlayTimerTick(object? sender, EventArgs e)
    {
        var nextTime = CurrentTimeSeconds + 0.05;
        if (nextTime >= EndTimeSeconds)
        {
            CurrentTimeSeconds = StartTimeSeconds;
        }
        else
        {
            CurrentTimeSeconds = nextTime;
        }
    }

    [RelayCommand]
    private void SeekToStart()
    {
        CurrentTimeSeconds = StartTimeSeconds;
    }

    [RelayCommand]
    private void SeekToEnd()
    {
        CurrentTimeSeconds = EndTimeSeconds;
    }

    [RelayCommand]
    private void StepBackward()
    {
        CurrentTimeSeconds = Math.Max(StartTimeSeconds, CurrentTimeSeconds - 0.1);
    }

    [RelayCommand]
    private void StepForward()
    {
        CurrentTimeSeconds = Math.Min(EndTimeSeconds, CurrentTimeSeconds + 0.1);
    }

    [RelayCommand]
    private async Task ExportCurrentFrameAsync()
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        try
        {
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_frame_{Math.Round(CurrentTimeSeconds):00}.png";
            string outPath;

            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(defaultName);
                if (string.IsNullOrWhiteSpace(chosen)) return;
                outPath = chosen;
            }
            else
            {
                var dir = Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, defaultName);
            }

            var skBmp = await Task.Run(() => VideoEngine.ExtractFrameAsync(FilePath, CurrentTimeSeconds));
            if (skBmp is not null)
            {
                using (skBmp)
                using (var img = SKImage.FromBitmap(skBmp))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 95))
                using (var file = File.OpenWrite(outPath))
                {
                    data.SaveTo(file);
                }
                StatusMessage = $"Saved frame to {Path.GetFileName(outPath)}";
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to export frame");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyTrimAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        IsProcessing = true;
        IsFinished = false;
        StatusMessage = "Trimming video...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_trimmed{ext}";
            string outPath;

            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(defaultName);
                if (string.IsNullOrWhiteSpace(chosen))
                {
                    IsProcessing = false;
                    StatusMessage = "";
                    return;
                }
                outPath = chosen;
            }
            else
            {
                var dir = Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, defaultName);
            }

            var progress = new Progress<double>(p => ExportProgress = p * 100.0);
            await Task.Run(() => VideoEngine.TrimVideoAsync(FilePath, StartTimeSeconds, EndTimeSeconds, outPath, true, progress));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Saved trimmed video to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to trim video");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(ResultFilePath) && File.Exists(ResultFilePath))
        {
            OpenOutputFolderRequested?.Invoke(ResultFilePath);
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    public static string FormatTimecode(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playTimer.Stop();
        _previewCts?.Cancel();
        _previewCts?.Dispose();

        CurrentFrameBitmap?.Dispose();
        foreach (var bmp in FilmstripThumbnails)
        {
            bmp.Dispose();
        }
        FilmstripThumbnails.Clear();
    }
}
