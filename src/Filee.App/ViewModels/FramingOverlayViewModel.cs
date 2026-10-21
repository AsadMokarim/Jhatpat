// ViewModel for interactive background framing overlay window.

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
using SkiaSharp;

namespace Filee.App.ViewModels;

public sealed partial class FramingOverlayViewModel : ObservableObject, IDisposable
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
    private SKBitmap? _cachedSourcePreview;
    private int _origWidth;
    private int _origHeight;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _lastCanvasWidth;
    private int _lastCanvasHeight;
    private bool _isSyncingColor;

    public event Action? LayoutUpdated;

    [ObservableProperty] private int _targetX;
    [ObservableProperty] private int _targetY;
    [ObservableProperty] private int _targetWidth;
    [ObservableProperty] private int _targetHeight;
    [ObservableProperty] private int _canvasWidth;
    [ObservableProperty] private int _canvasHeight;

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private string _currentFileName = "";
    [ObservableProperty] private string _counterText = "";
    [ObservableProperty] private bool _hasMultipleFiles;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _statusNotice = "";
    [ObservableProperty] private string _dimensionsText = "";

    // Framing Options properties
    [ObservableProperty] private int _padding = 48;
    [ObservableProperty] private double _cornerRadius = 16.0;
    [ObservableProperty] private bool _hasShadow = true;
    [ObservableProperty] private double _shadowOpacity = 0.45;
    [ObservableProperty] private double _shadowBlur = 14.0;
    [ObservableProperty] private int _shadowOffsetY = 8;

    [ObservableProperty] private FramingBackgroundType _backgroundType = FramingBackgroundType.SolidColor;
    [ObservableProperty] private string _selectedSolidColor = "#1E1E2E";
    [ObservableProperty] private string _hexInputText = "#1E1E2E";
    [ObservableProperty] private bool _isHexValid = true;
    [ObservableProperty] private byte _colorR = 0x1E;
    [ObservableProperty] private byte _colorG = 0x1E;
    [ObservableProperty] private byte _colorB = 0x2E;
    [ObservableProperty] private Avalonia.Media.IBrush _currentColorBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1E, 0x1E, 0x2E));

    [ObservableProperty] private string _gradientStartColor = "#4F46E5";
    [ObservableProperty] private string _gradientEndColor = "#EC4899";
    [ObservableProperty] private double _gradientAngle = 45.0;
    [ObservableProperty] private string? _backgroundImagePath;

    [ObservableProperty] private string _selectedAspect = "original";
    [ObservableProperty] private int _customWidth = 1200;
    [ObservableProperty] private int _customHeight = 1200;

    [ObservableProperty] private double _relativeOffsetX = 0.0;
    [ObservableProperty] private double _relativeOffsetY = 0.0;

    public bool HasOffset => Math.Abs(RelativeOffsetX) > 0.001 || Math.Abs(RelativeOffsetY) > 0.001;

    public ObservableCollection<string> Problems { get; } = [];

    public bool IsEditing => !IsFinished;
    public bool HasStatusNotice => !string.IsNullOrEmpty(StatusNotice);

    public bool IsSolidBg => BackgroundType == FramingBackgroundType.SolidColor;
    public bool IsGradientBg => BackgroundType == FramingBackgroundType.Gradient;
    public bool IsImageBg => BackgroundType == FramingBackgroundType.Image;
    public bool IsTransparentBg => BackgroundType == FramingBackgroundType.Transparent;

    public bool IsAspectOriginal => SelectedAspect == "original";
    public bool IsAspectSquare => SelectedAspect == "1:1";
    public bool IsAspect45 => SelectedAspect == "4:5";
    public bool IsAspect916 => SelectedAspect == "9:16";
    public bool IsAspect169 => SelectedAspect == "16:9";
    public bool IsAspect43 => SelectedAspect == "4:3";
    public bool IsAspect32 => SelectedAspect == "3:2";
    public bool IsAspect23 => SelectedAspect == "2:3";
    public bool IsAspectCustom => SelectedAspect == "custom";

    public event Action? CloseRequested;
    public event Func<Task<string?>>? BrowseImageRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;

    public FramingOverlayViewModel(
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

    public FramingOptions CurrentOptions => new()
    {
        BackgroundType = BackgroundType,
        BackgroundColor = SelectedSolidColor,
        GradientStartColor = GradientStartColor,
        GradientEndColor = GradientEndColor,
        GradientAngle = GradientAngle,
        BackgroundImagePath = BackgroundImagePath,
        Padding = Padding,
        CornerRadius = CornerRadius,
        HasShadow = HasShadow,
        ShadowOpacity = ShadowOpacity,
        ShadowBlur = ShadowBlur,
        ShadowOffsetY = ShadowOffsetY,
        AspectRatio = SelectedAspect,
        CustomWidth = CustomWidth,
        CustomHeight = CustomHeight,
        RelativeOffsetX = RelativeOffsetX,
        RelativeOffsetY = RelativeOffsetY,
    };

    partial void OnPaddingChanged(int value) => SchedulePreviewUpdate();
    partial void OnCornerRadiusChanged(double value) => SchedulePreviewUpdate();
    partial void OnHasShadowChanged(bool value) => SchedulePreviewUpdate();
    partial void OnBackgroundTypeChanged(FramingBackgroundType value)
    {
        OnPropertyChanged(nameof(IsSolidBg));
        OnPropertyChanged(nameof(IsGradientBg));
        OnPropertyChanged(nameof(IsImageBg));
        OnPropertyChanged(nameof(IsTransparentBg));
        SchedulePreviewUpdate();
    }
    partial void OnSelectedSolidColorChanged(string value)
    {
        SyncFromHex(value, updateInputText: true);
        SchedulePreviewUpdate();
    }

    private void SyncFromHex(string hex, bool updateInputText)
    {
        if (_isSyncingColor) return;
        _isSyncingColor = true;
        try
        {
            var sk = FramingService.ParseHexColor(hex, new SkiaSharp.SKColor(0x1E, 0x1E, 0x2E));
            ColorR = sk.Red;
            ColorG = sk.Green;
            ColorB = sk.Blue;
            CurrentColorBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(sk.Red, sk.Green, sk.Blue));
            if (updateInputText)
            {
                HexInputText = $"#{sk.Red:X2}{sk.Green:X2}{sk.Blue:X2}";
            }
            IsHexValid = true;
        }
        finally
        {
            _isSyncingColor = false;
        }
    }

    partial void OnHexInputTextChanged(string value)
    {
        if (_isSyncingColor) return;
        if (FramingService.IsValidHexColor(value))
        {
            IsHexValid = true;
            _isSyncingColor = true;
            try
            {
                var sk = FramingService.ParseHexColor(value, new SkiaSharp.SKColor(0x1E, 0x1E, 0x2E));
                ColorR = sk.Red;
                ColorG = sk.Green;
                ColorB = sk.Blue;
                CurrentColorBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(sk.Red, sk.Green, sk.Blue));
                var formatted = $"#{sk.Red:X2}{sk.Green:X2}{sk.Blue:X2}";
                SelectedSolidColor = formatted;
            }
            finally
            {
                _isSyncingColor = false;
            }
        }
        else
        {
            IsHexValid = false;
        }
    }

    partial void OnColorRChanged(byte value) => OnRgbSliderChanged();
    partial void OnColorGChanged(byte value) => OnRgbSliderChanged();
    partial void OnColorBChanged(byte value) => OnRgbSliderChanged();

    private void OnRgbSliderChanged()
    {
        if (_isSyncingColor) return;
        _isSyncingColor = true;
        try
        {
            var hex = $"#{ColorR:X2}{ColorG:X2}{ColorB:X2}";
            HexInputText = hex;
            IsHexValid = true;
            CurrentColorBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(ColorR, ColorG, ColorB));
            SelectedSolidColor = hex;
        }
        finally
        {
            _isSyncingColor = false;
        }
    }

    partial void OnGradientStartColorChanged(string value) => SchedulePreviewUpdate();
    partial void OnGradientEndColorChanged(string value) => SchedulePreviewUpdate();
    partial void OnBackgroundImagePathChanged(string? value) => SchedulePreviewUpdate();
    partial void OnSelectedAspectChanged(string value)
    {
        OnPropertyChanged(nameof(IsAspectOriginal));
        OnPropertyChanged(nameof(IsAspectSquare));
        OnPropertyChanged(nameof(IsAspect45));
        OnPropertyChanged(nameof(IsAspect916));
        OnPropertyChanged(nameof(IsAspect169));
        OnPropertyChanged(nameof(IsAspect43));
        OnPropertyChanged(nameof(IsAspect32));
        OnPropertyChanged(nameof(IsAspect23));
        OnPropertyChanged(nameof(IsAspectCustom));
        SchedulePreviewUpdate();
    }
    partial void OnCustomWidthChanged(int value)
    {
        if (value >= FramingService.MinCustomDimension && value <= FramingService.MaxCustomDimension && IsAspectCustom)
        {
            SchedulePreviewUpdate();
        }
    }

    partial void OnCustomHeightChanged(int value)
    {
        if (value >= FramingService.MinCustomDimension && value <= FramingService.MaxCustomDimension && IsAspectCustom)
        {
            SchedulePreviewUpdate();
        }
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
            _previewCts?.Cancel();
            _cachedSourcePreview?.Dispose();
            _cachedSourcePreview = null;

            await Task.Run(() =>
            {
                using var fullSource = FramingService.LoadSourceBitmap(path);
                _origWidth = fullSource.Width;
                _origHeight = fullSource.Height;

                const int maxPreviewDim = 1600;
                if (fullSource.Width > maxPreviewDim || fullSource.Height > maxPreviewDim)
                {
                    double scale = Math.Min((double)maxPreviewDim / fullSource.Width, (double)maxPreviewDim / fullSource.Height);
                    int scaledW = Math.Max(1, (int)Math.Round(fullSource.Width * scale));
                    int scaledH = Math.Max(1, (int)Math.Round(fullSource.Height * scale));
                    _cachedSourcePreview = fullSource.Resize(new SKImageInfo(scaledW, scaledH), new SKSamplingOptions(SKFilterMode.Linear));
                }
                else
                {
                    _cachedSourcePreview = fullSource.Copy();
                }
            });

            UpdatePreviewNow();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load preview for {File}", path);
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

    private void SchedulePreviewUpdate() => UpdatePreviewNow();

    public void UpdatePreviewNow()
    {
        if (_closed || IsFinished || CurrentIndex >= _files.Count || _cachedSourcePreview == null)
            return;

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        var source = _cachedSourcePreview;
        var options = CurrentOptions;
        var origW = _origWidth > 0 ? _origWidth : source.Width;
        var origH = _origHeight > 0 ? _origHeight : source.Height;

        _ = Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            var (bitmap, w, h, srcW, srcH, tx, ty, tw, th) =
                FramingService.RenderPreview(source, options, origW, origH);

            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested || _closed)
                {
                    bitmap.Dispose();
                    return;
                }

                _sourceWidth = srcW;
                _sourceHeight = srcH;
                _lastCanvasWidth = w;
                _lastCanvasHeight = h;
                CanvasWidth = w;
                CanvasHeight = h;
                TargetX = tx;
                TargetY = ty;
                TargetWidth = tw;
                TargetHeight = th;

                var old = PreviewImage;
                PreviewImage = bitmap;
                old?.Dispose();
                DimensionsText = $"{w} × {h} px";
                OnPropertyChanged(nameof(HasOffset));
                LayoutUpdated?.Invoke();
            });
        }, token);
    }

    public Task RefreshPreviewAsync()
    {
        UpdatePreviewNow();
        return Task.CompletedTask;
    }

    [RelayCommand]
    public void SetBackgroundType(FramingBackgroundType type) => BackgroundType = type;

    [RelayCommand]
    public void SetSolidColor(string hex)
    {
        BackgroundType = FramingBackgroundType.SolidColor;
        SelectedSolidColor = hex;
    }

    [RelayCommand]
    public void SetGradient(string colors)
    {
        BackgroundType = FramingBackgroundType.Gradient;
        var parts = colors.Split(',');
        if (parts.Length >= 2)
        {
            GradientStartColor = parts[0].Trim();
            GradientEndColor = parts[1].Trim();
        }
    }

    [RelayCommand]
    public async Task BrowseBackgroundImageAsync()
    {
        if (BrowseImageRequested is not null)
        {
            var selected = await BrowseImageRequested.Invoke();
            if (!string.IsNullOrEmpty(selected) && File.Exists(selected))
            {
                BackgroundImagePath = selected;
                BackgroundType = FramingBackgroundType.Image;
            }
        }
    }

    [RelayCommand]
    public void SetAspect(string aspect)
    {
        if (aspect == "custom" && SelectedAspect != "custom")
        {
            if (_lastCanvasWidth > 0 && _lastCanvasHeight > 0)
            {
                CustomWidth = Math.Clamp(_lastCanvasWidth, FramingService.MinCustomDimension, FramingService.MaxCustomDimension);
                CustomHeight = Math.Clamp(_lastCanvasHeight, FramingService.MinCustomDimension, FramingService.MaxCustomDimension);
            }
        }
        SelectedAspect = aspect;
    }

    public void SetRelativeOffset(double relX, double relY)
    {
        RelativeOffsetX = Math.Clamp(relX, -1.0, 1.0);
        RelativeOffsetY = Math.Clamp(relY, -1.0, 1.0);
        OnPropertyChanged(nameof(HasOffset));
        UpdatePreviewNow();
    }

    public void MoveImageOffset(double deltaX, double deltaY, double displayedCanvasWidth, double displayedCanvasHeight)
    {
        if (displayedCanvasWidth <= 0 || displayedCanvasHeight <= 0 || CurrentIndex >= _files.Count)
            return;

        int srcW = _sourceWidth > 0 ? _sourceWidth : 1000;
        int srcH = _sourceHeight > 0 ? _sourceHeight : 1000;

        var (canvasW, canvasH, _, _, targetW, targetH) = FramingService.CalculateLayout(srcW, srcH, CurrentOptions);
        int maxOffsetX = (canvasW - targetW) / 2;
        int maxOffsetY = (canvasH - targetH) / 2;

        double scale = Math.Min(displayedCanvasWidth / canvasW, displayedCanvasHeight / canvasH);
        double maxScreenOffsetX = maxOffsetX * scale;
        double maxScreenOffsetY = maxOffsetY * scale;

        double deltaRelX = maxScreenOffsetX > 1.0 ? (deltaX / maxScreenOffsetX) : 0.0;
        double deltaRelY = maxScreenOffsetY > 1.0 ? (deltaY / maxScreenOffsetY) : 0.0;

        RelativeOffsetX = Math.Clamp(RelativeOffsetX + deltaRelX, -1.0, 1.0);
        RelativeOffsetY = Math.Clamp(RelativeOffsetY + deltaRelY, -1.0, 1.0);
        OnPropertyChanged(nameof(HasOffset));
        UpdatePreviewNow();
    }

    [RelayCommand]
    public void ResetPosition()
    {
        RelativeOffsetX = 0.0;
        RelativeOffsetY = 0.0;
        OnPropertyChanged(nameof(HasOffset));
        UpdatePreviewNow();
    }

    [RelayCommand]
    public async Task FrameAndSaveAsync()
    {
        if (IsBusy || IsFinished || _closed || CurrentIndex >= _files.Count)
            return;

        IsBusy = true;
        var path = _files[CurrentIndex];
        _processedSources.Add(path);

        try
        {
            var result = await FramingService.FrameFileAsync(path, CurrentOptions, _preset, _store, _loc, _log);
            if (result.Status == FramingStatus.Success && result.OutputPath is not null)
                _outputs.Add(result.OutputPath);
            else if (result.Status == FramingStatus.SkippedExists)
                AddProblem(path, "toast.skipped");
            else
                AddFailure(path, result.ErrorKey ?? "error.conversion_failed", result.ErrorDetail);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to frame {File}", path);
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
    public async Task CopyToClipboardAsync()
    {
        if (IsBusy || IsFinished || _closed || CurrentIndex >= _files.Count)
            return;

        IsBusy = true;
        try
        {
            var path = _files[CurrentIndex];
            using var source = FramingService.LoadSourceBitmap(path);
            using var framed = FramingService.RenderFramedBitmap(source, CurrentOptions);
            using var image = SkiaSharp.SKImage.FromBitmap(framed);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            ms.Position = 0;
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms);

            var clipboard = AppHost.Get<IClipboardService>();
            var copied = await clipboard.CopyBitmapToClipboardAsync(bmp);
            if (copied)
            {
                AppHost.Get<WindowService>().ShowToastNotification(_loc["clipboard.copied_to_clipboard"]);
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to copy framed image to clipboard.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsAsync()
    {
        if (IsBusy || IsFinished || _closed || CurrentIndex >= _files.Count || SaveAsRequested is null)
            return;

        var path = _files[CurrentIndex];
        var defaultName = Path.GetFileNameWithoutExtension(path) + "_framed" + Path.GetExtension(path);
        var dest = await SaveAsRequested.Invoke(defaultName);
        if (string.IsNullOrEmpty(dest))
            return;

        IsBusy = true;
        _processedSources.Add(path);

        try
        {
            var result = await FramingService.FrameFileAsync(path, CurrentOptions, _preset, _store, _loc, _log, customOutputPath: dest);
            if (result.Status == FramingStatus.Success && result.OutputPath is not null)
                _outputs.Add(result.OutputPath);
            else
                AddFailure(path, result.ErrorKey ?? "error.conversion_failed", result.ErrorDetail);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to frame {File}", path);
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
        Padding = 48;
        CornerRadius = 16.0;
        HasShadow = true;
        ShadowOpacity = 0.45;
        ShadowBlur = 14.0;
        ShadowOffsetY = 8;
        BackgroundType = FramingBackgroundType.SolidColor;
        SelectedSolidColor = "#1E1E2E";
        SelectedAspect = "original";
        RelativeOffsetX = 0.0;
        RelativeOffsetY = 0.0;
        OnPropertyChanged(nameof(HasOffset));
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
        _previewCts?.Cancel();
        _cachedSourcePreview?.Dispose();
        _cachedSourcePreview = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
