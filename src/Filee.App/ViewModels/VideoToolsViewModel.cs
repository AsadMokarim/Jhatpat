// ViewModel for comprehensive Video Tools: Crop, Speed, Join, Export Frames, and Split.

using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
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

public sealed partial class VideoClipItemViewModel : ObservableObject
{
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private bool _isFirst;
    [ObservableProperty] private bool _isLast;

    public VideoClipItemViewModel(string path, int index)
    {
        FilePath = path;
        DisplayIndex = index;
        FileName = Path.GetFileName(path);
    }
}

public sealed partial class VideoToolsViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<Task<IReadOnlyList<string>?>>? AddVideosRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Func<Task<string?>>? SelectFolderRequested;
    public event Action<string>? OpenOutputFolderRequested;

    [ObservableProperty] private string _activeTab = "Crop"; // Crop, Speed, Join, Frames, Split
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private string _durationText = "00:00";
    [ObservableProperty] private int _videoWidth;
    [ObservableProperty] private int _videoHeight;

    // Preview
    [ObservableProperty] private Bitmap? _previewFrame;

    // CROP TOOL
    [ObservableProperty] private string _cropAspect = "16:9";
    [ObservableProperty] private int _cropWidth;
    [ObservableProperty] private int _cropHeight;
    [ObservableProperty] private int _cropX;
    [ObservableProperty] private int _cropY;

    // SPEED TOOL
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private bool _muteAudio;
    [ObservableProperty] private string _newDurationText = "00:00";

    // JOIN TOOL
    public ObservableCollection<VideoClipItemViewModel> JoinClips { get; } = [];
    [ObservableProperty] private string _totalJoinDurationText = "00:00";

    // EXPORT FRAMES TOOL
    [ObservableProperty] private double _frameTimeSeconds;
    [ObservableProperty] private string _frameTimeText = "00:00.00";
    [ObservableProperty] private string _frameExportFormat = "png";
    [ObservableProperty] private double _sequenceInterval = 1.0; // seconds

    // SPLIT TOOL
    [ObservableProperty] private double _splitIntervalSeconds = 30.0;
    [ObservableProperty] private int _estimatedParts = 1;

    // Execution state
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public bool IsTabCrop => ActiveTab == "Crop";
    public bool IsTabSpeed => ActiveTab == "Speed";
    public bool IsTabJoin => ActiveTab == "Join";
    public bool IsTabFrames => ActiveTab == "Frames";
    public bool IsTabSplit => ActiveTab == "Split";

    public bool HasJoinClips => JoinClips.Count > 0;
    public bool CanJoin => !IsProcessing && JoinClips.Count >= 2;

    public VideoToolsViewModel(IReadOnlyList<string> initialFiles, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        if (initialFiles.Count > 0)
        {
            FilePath = initialFiles[0];
            FileName = Path.GetFileName(initialFiles[0]);
            LoadVideoAsync(initialFiles[0]);
        }

        foreach (var file in initialFiles)
        {
            if (File.Exists(file))
            {
                JoinClips.Add(new VideoClipItemViewModel(file, JoinClips.Count + 1));
            }
        }
        RefreshJoinClips();
    }

    private async void LoadVideoAsync(string path)
    {
        try
        {
            var meta = await Task.Run(() => VideoEngine.ProbeVideoAsync(path));
            DurationSeconds = meta.DurationSeconds;
            DurationText = VideoTrimViewModel.FormatTimecode(meta.DurationSeconds);
            VideoWidth = meta.Width;
            VideoHeight = meta.Height;

            // Initialize Crop to 16:9 or default aspect
            SetCropAspect("16:9");
            UpdateSpeedDuration();
            UpdateSplitEstimate();

            // Load initial frame preview
            var skBmp = await Task.Run(() => VideoEngine.ExtractFrameAsync(path, Math.Min(1.0, meta.DurationSeconds / 2.0), 360));
            if (skBmp is not null)
            {
                using (skBmp)
                using (var img = SKImage.FromBitmap(skBmp))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 85))
                using (var mem = new MemoryStream())
                {
                    data.SaveTo(mem);
                    mem.Position = 0;
                    PreviewFrame = new Bitmap(mem);
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load video info for tools: {Path}", path);
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        ActiveTab = tab;
        OnPropertyChanged(nameof(IsTabCrop));
        OnPropertyChanged(nameof(IsTabSpeed));
        OnPropertyChanged(nameof(IsTabJoin));
        OnPropertyChanged(nameof(IsTabFrames));
        OnPropertyChanged(nameof(IsTabSplit));
    }

    // ──────── CROP ACTIONS ────────
    [RelayCommand]
    private void SetCropAspect(string aspect)
    {
        CropAspect = aspect;
        if (VideoWidth <= 0 || VideoHeight <= 0) return;

        double targetRatio = aspect switch
        {
            "9:16" => 9.0 / 16.0,
            "1:1" => 1.0,
            "4:3" => 4.0 / 3.0,
            "Free" => (double)VideoWidth / VideoHeight,
            _ => 16.0 / 9.0
        };

        if ((double)VideoWidth / VideoHeight > targetRatio)
        {
            CropHeight = VideoHeight;
            CropWidth = (int)Math.Round(VideoHeight * targetRatio);
        }
        else
        {
            CropWidth = VideoWidth;
            CropHeight = (int)Math.Round(VideoWidth / targetRatio);
        }

        CropX = Math.Max(0, (VideoWidth - CropWidth) / 2);
        CropY = Math.Max(0, (VideoHeight - CropHeight) / 2);
    }

    [RelayCommand]
    private async Task ExecuteCropAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        IsProcessing = true;
        StatusMessage = "Cropping video...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_cropped{ext}";
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
            await Task.Run(() => VideoEngine.CropVideoAsync(FilePath, CropX, CropY, CropWidth, CropHeight, outPath, prog));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Cropped video saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to crop video");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ──────── SPEED ACTIONS ────────
    [RelayCommand]
    private void SetSpeed(double val)
    {
        Speed = val;
        UpdateSpeedDuration();
    }

    private void UpdateSpeedDuration()
    {
        if (DurationSeconds > 0 && Speed > 0)
        {
            NewDurationText = VideoTrimViewModel.FormatTimecode(DurationSeconds / Speed);
        }
    }

    [RelayCommand]
    private async Task ExecuteSpeedAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        IsProcessing = true;
        StatusMessage = $"Adjusting speed to {Speed}x...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_{Speed}x{ext}";
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
            await Task.Run(() => VideoEngine.ChangeSpeedAsync(FilePath, Speed, MuteAudio, outPath, prog));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Speed-adjusted video saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to change video speed");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ──────── JOIN ACTIONS ────────
    [RelayCommand]
    private async Task AddJoinClipsAsync()
    {
        if (AddVideosRequested is { } handler)
        {
            var selected = await handler();
            if (selected is { Count: > 0 })
            {
                foreach (var path in selected)
                {
                    if (File.Exists(path))
                    {
                        JoinClips.Add(new VideoClipItemViewModel(path, JoinClips.Count + 1));
                    }
                }
                RefreshJoinClips();
            }
        }
    }

    [RelayCommand]
    private void MoveClipUp(VideoClipItemViewModel clip)
    {
        var idx = JoinClips.IndexOf(clip);
        if (idx > 0)
        {
            JoinClips.Move(idx, idx - 1);
            RefreshJoinClips();
        }
    }

    [RelayCommand]
    private void MoveClipDown(VideoClipItemViewModel clip)
    {
        var idx = JoinClips.IndexOf(clip);
        if (idx >= 0 && idx < JoinClips.Count - 1)
        {
            JoinClips.Move(idx, idx + 1);
            RefreshJoinClips();
        }
    }

    [RelayCommand]
    private void RemoveClip(VideoClipItemViewModel clip)
    {
        JoinClips.Remove(clip);
        RefreshJoinClips();
    }

    private void RefreshJoinClips()
    {
        for (var i = 0; i < JoinClips.Count; i++)
        {
            JoinClips[i].DisplayIndex = i + 1;
            JoinClips[i].IsFirst = i == 0;
            JoinClips[i].IsLast = i == JoinClips.Count - 1;
        }
        OnPropertyChanged(nameof(HasJoinClips));
        OnPropertyChanged(nameof(CanJoin));
    }

    [RelayCommand]
    private async Task ExecuteJoinAsync()
    {
        if (JoinClips.Count < 2 || IsProcessing) return;

        IsProcessing = true;
        StatusMessage = $"Joining {JoinClips.Count} video clips...";

        try
        {
            var ext = Path.GetExtension(JoinClips[0].FilePath);
            var defaultName = $"joined_video{ext}";
            string outPath;

            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(defaultName);
                if (string.IsNullOrWhiteSpace(chosen)) { IsProcessing = false; return; }
                outPath = chosen;
            }
            else
            {
                var dir = Path.GetDirectoryName(JoinClips[0].FilePath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, defaultName);
            }

            var inputPaths = JoinClips.Select(c => c.FilePath).ToList();
            await Task.Run(() => VideoEngine.JoinVideosAsync(inputPaths, outPath));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Joined video saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to join videos");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ──────── EXPORT FRAMES ────────
    partial void OnFrameTimeSecondsChanged(double value)
    {
        FrameTimeText = VideoTrimViewModel.FormatTimecode(value);
    }

    [RelayCommand]
    private async Task ExecuteExportFrameAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        try
        {
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_frame_{Math.Round(FrameTimeSeconds):00}.{FrameExportFormat}";
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

            var skBmp = await Task.Run(() => VideoEngine.ExtractFrameAsync(FilePath, FrameTimeSeconds));
            if (skBmp is not null)
            {
                using (skBmp)
                using (var img = SKImage.FromBitmap(skBmp))
                {
                    var fmt = FrameExportFormat == "jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
                    using var data = img.Encode(fmt, 95);
                    using var file = File.OpenWrite(outPath);
                    data.SaveTo(file);
                }
                ResultFilePath = outPath;
                IsFinished = true;
                StatusMessage = $"Exported frame to {Path.GetFileName(outPath)}";
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to export frame");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    // ──────── SPLIT VIDEO ────────
    partial void OnSplitIntervalSecondsChanged(double value) => UpdateSplitEstimate();

    private void UpdateSplitEstimate()
    {
        if (DurationSeconds > 0 && SplitIntervalSeconds > 0)
        {
            EstimatedParts = (int)Math.Ceiling(DurationSeconds / SplitIntervalSeconds);
        }
    }

    [RelayCommand]
    private async Task ExecuteSplitAsync()
    {
        if (IsProcessing || string.IsNullOrEmpty(FilePath)) return;

        IsProcessing = true;
        StatusMessage = $"Splitting video into ~{EstimatedParts} parts...";

        try
        {
            string outDir;
            if (SelectFolderRequested is { } selectFolder)
            {
                var chosen = await selectFolder();
                if (string.IsNullOrWhiteSpace(chosen)) { IsProcessing = false; return; }
                outDir = chosen;
            }
            else
            {
                var baseDir = Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();
                outDir = Path.Combine(baseDir, $"{Path.GetFileNameWithoutExtension(FilePath)}_parts");
            }

            await Task.Run(() => VideoEngine.SplitVideoAsync(FilePath, SplitIntervalSeconds, outDir));

            ResultFilePath = outDir;
            IsFinished = true;
            StatusMessage = $"Video split into {EstimatedParts} parts in {Path.GetFileName(outDir)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to split video");
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
        if (!string.IsNullOrEmpty(ResultFilePath))
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
        PreviewFrame?.Dispose();
    }
}
