// ViewModel for Audio Tools: Volume Normalization (EBU R128) and Audio Trimming.

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Media;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class AudioToolsViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    [ObservableProperty] private string _activeTab = "Normalize"; // Normalize, Trim
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private double _totalDurationSeconds;
    [ObservableProperty] private string _totalDurationText = "00:00";
    [ObservableProperty] private string _formatInfo = "";

    // NORMALIZE SETTINGS
    [ObservableProperty] private double _targetLufs = -16.0;
    [ObservableProperty] private double _truePeak = -1.5;
    [ObservableProperty] private string _presetStandard = "-16"; // -16, -14, -23, Custom

    // TRIM SETTINGS
    [ObservableProperty] private double _startTimeSeconds;
    [ObservableProperty] private double _endTimeSeconds;
    [ObservableProperty] private string _startTimeText = "00:00.00";
    [ObservableProperty] private string _endTimeText = "00:00.00";
    [ObservableProperty] private string _trimDurationText = "00:00.00";

    // Execution state
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public bool IsTabNormalize => ActiveTab == "Normalize";
    public bool IsTabTrim => ActiveTab == "Trim";

    public bool IsStandardPodcast => PresetStandard == "-16";
    public bool IsStandardStreaming => PresetStandard == "-14";
    public bool IsStandardBroadcast => PresetStandard == "-23";

    public AudioToolsViewModel(string audioPath, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        FilePath = audioPath;
        FileName = Path.GetFileName(audioPath);

        LoadAudioAsync(audioPath);
    }

    private async void LoadAudioAsync(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            var meta = await Task.Run(() => AudioEngine.ProbeAudioAsync(path));
            TotalDurationSeconds = meta.DurationSeconds;
            TotalDurationText = VideoTrimViewModel.FormatTimecode(meta.DurationSeconds);
            FormatInfo = $"{meta.FormatName} • {meta.SampleRate / 1000.0:0.0} kHz • {(meta.Channels == 2 ? "Stereo" : "Mono")}";

            StartTimeSeconds = 0.0;
            EndTimeSeconds = TotalDurationSeconds;
            UpdateTrimReadouts();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to probe audio info for {Path}", path);
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        ActiveTab = tab;
        OnPropertyChanged(nameof(IsTabNormalize));
        OnPropertyChanged(nameof(IsTabTrim));
    }

    // ──────── NORMALIZE ────────
    [RelayCommand]
    private void SetStandardLufs(string lufs)
    {
        PresetStandard = lufs;
        TargetLufs = lufs switch
        {
            "-14" => -14.0,
            "-23" => -23.0,
            _ => -16.0
        };
        OnPropertyChanged(nameof(IsStandardPodcast));
        OnPropertyChanged(nameof(IsStandardStreaming));
        OnPropertyChanged(nameof(IsStandardBroadcast));
    }

    [RelayCommand]
    private async Task NormalizeAudioAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        IsProcessing = true;
        StatusMessage = $"Normalizing audio to {TargetLufs:0.0} LUFS...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_normalized{ext}";
            string outPath;

            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(defaultName);
                if (string.IsNullOrWhiteSpace(chosen)) { IsProcessing = false; return; }
                outPath = chosen;
            }
            else
            {
                var dir = Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, defaultName);
            }

            var prog = new Progress<double>(p => ProgressPercent = p * 100.0);
            await Task.Run(() => AudioEngine.NormalizeAudioAsync(FilePath, TargetLufs, TruePeak, outPath, prog));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Normalized audio saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to normalize audio");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ──────── TRIM ────────
    partial void OnStartTimeSecondsChanged(double value)
    {
        if (value > EndTimeSeconds) StartTimeSeconds = EndTimeSeconds;
        UpdateTrimReadouts();
    }

    partial void OnEndTimeSecondsChanged(double value)
    {
        if (value < StartTimeSeconds) EndTimeSeconds = StartTimeSeconds;
        UpdateTrimReadouts();
    }

    private void UpdateTrimReadouts()
    {
        StartTimeText = VideoTrimViewModel.FormatTimecode(StartTimeSeconds);
        EndTimeText = VideoTrimViewModel.FormatTimecode(EndTimeSeconds);
        TrimDurationText = VideoTrimViewModel.FormatTimecode(Math.Max(0, EndTimeSeconds - StartTimeSeconds));
    }

    [RelayCommand]
    private void SeekToStart()
    {
        StartTimeSeconds = 0;
    }

    [RelayCommand]
    private void SeekToEnd()
    {
        EndTimeSeconds = TotalDurationSeconds;
    }

    [RelayCommand]
    private async Task TrimAudioAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        IsProcessing = true;
        StatusMessage = "Trimming audio file...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_trimmed{ext}";
            string outPath;

            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(defaultName);
                if (string.IsNullOrWhiteSpace(chosen)) { IsProcessing = false; return; }
                outPath = chosen;
            }
            else
            {
                var dir = Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, defaultName);
            }

            var prog = new Progress<double>(p => ProgressPercent = p * 100.0);
            await Task.Run(() => AudioEngine.TrimAudioAsync(FilePath, StartTimeSeconds, EndTimeSeconds, outPath, true, prog));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Trimmed audio saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to trim audio");
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
