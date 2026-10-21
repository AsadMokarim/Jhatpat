// ViewModel for Photo Editor: real-time live preview adjustments and high-res export.

using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Photo;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.ViewModels;

public sealed partial class EditPhotoViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;

    private SKBitmap? _originalFull;
    private SKBitmap? _previewSource;
    private CancellationTokenSource? _renderCts;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private int _imageWidth;
    [ObservableProperty] private int _imageHeight;
    [ObservableProperty] private string _resolutionText = "";

    // Preview Bitmaps
    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private Bitmap? _originalPreviewBitmap;
    [ObservableProperty] private bool _isComparing;
    [ObservableProperty] private string _activeTab = "Light"; // "Light", "Color", "Detail"

    // Light Adjustments
    [ObservableProperty] private double _exposure;
    [ObservableProperty] private double _brightness;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private double _highlights;
    [ObservableProperty] private double _shadows;
    [ObservableProperty] private double _whites;
    [ObservableProperty] private double _blacks;
    [ObservableProperty] private double _gamma = 1.0;

    // Color Adjustments
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private double _tint;
    [ObservableProperty] private double _saturation;
    [ObservableProperty] private double _vibrance;
    [ObservableProperty] private double _hue;

    // Detail Adjustments
    [ObservableProperty] private double _dehaze;
    [ObservableProperty] private double _clarity;
    [ObservableProperty] private double _grain;
    [ObservableProperty] private double _noiseReduction;

    // Execution state
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public bool IsTabLight => ActiveTab == "Light";
    public bool IsTabColor => ActiveTab == "Color";
    public bool IsTabDetail => ActiveTab == "Detail";

    public EditPhotoViewModel(string imagePath, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        FilePath = imagePath;
        FileName = Path.GetFileName(imagePath);

        LoadImage(imagePath);
    }

    private void LoadImage(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            _originalFull = ResizeService.LoadSourceBitmap(path);
            ImageWidth = _originalFull.Width;
            ImageHeight = _originalFull.Height;
            ResolutionText = $"{ImageWidth} × {ImageHeight}";

            // Downscale for fast interactive UI preview
            const int maxPreviewDim = 1280;
            if (_originalFull.Width > maxPreviewDim || _originalFull.Height > maxPreviewDim)
            {
                var scale = Math.Min((double)maxPreviewDim / _originalFull.Width, (double)maxPreviewDim / _originalFull.Height);
                var targetW = Math.Max(1, (int)Math.Round(_originalFull.Width * scale));
                var targetH = Math.Max(1, (int)Math.Round(_originalFull.Height * scale));
                _previewSource = _originalFull.Resize(new SKImageInfo(targetW, targetH), new SKSamplingOptions(SKFilterMode.Linear));
            }
            else
            {
                _previewSource = _originalFull.Copy();
            }

            OriginalPreviewBitmap = SkiaToAvalonia(_previewSource);
            UpdatePreview();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load image for photo editing: {Path}", path);
            StatusMessage = $"Failed to open image: {ex.Message}";
        }
    }

    private PhotoAdjustments BuildAdjustments() => new()
    {
        Exposure = Exposure,
        Brightness = Brightness,
        Contrast = Contrast,
        Highlights = Highlights,
        Shadows = Shadows,
        Whites = Whites,
        Blacks = Blacks,
        Gamma = Gamma,
        Temperature = Temperature,
        Tint = Tint,
        Saturation = Saturation,
        Vibrance = Vibrance,
        Hue = Hue,
        Dehaze = Dehaze,
        Clarity = Clarity,
        Grain = Grain,
        NoiseReduction = NoiseReduction,
    };

    partial void OnExposureChanged(double value) => RequestUpdatePreview();
    partial void OnBrightnessChanged(double value) => RequestUpdatePreview();
    partial void OnContrastChanged(double value) => RequestUpdatePreview();
    partial void OnHighlightsChanged(double value) => RequestUpdatePreview();
    partial void OnShadowsChanged(double value) => RequestUpdatePreview();
    partial void OnWhitesChanged(double value) => RequestUpdatePreview();
    partial void OnBlacksChanged(double value) => RequestUpdatePreview();
    partial void OnGammaChanged(double value) => RequestUpdatePreview();

    partial void OnTemperatureChanged(double value) => RequestUpdatePreview();
    partial void OnTintChanged(double value) => RequestUpdatePreview();
    partial void OnSaturationChanged(double value) => RequestUpdatePreview();
    partial void OnVibranceChanged(double value) => RequestUpdatePreview();
    partial void OnHueChanged(double value) => RequestUpdatePreview();

    partial void OnDehazeChanged(double value) => RequestUpdatePreview();
    partial void OnClarityChanged(double value) => RequestUpdatePreview();
    partial void OnGrainChanged(double value) => RequestUpdatePreview();
    partial void OnNoiseReductionChanged(double value) => RequestUpdatePreview();

    private void RequestUpdatePreview()
    {
        if (_previewSource is null)
            return;

        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        var token = _renderCts.Token;

        var adj = BuildAdjustments();
        var src = _previewSource;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            using var adjusted = PhotoFilterEngine.ApplyAdjustments(src, adj);
            if (token.IsCancellationRequested)
                return;

            var avaloniaBitmap = SkiaToAvalonia(adjusted);
            if (token.IsCancellationRequested)
            {
                avaloniaBitmap?.Dispose();
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    var old = PreviewBitmap;
                    PreviewBitmap = avaloniaBitmap;
                    old?.Dispose();
                }
                else
                {
                    avaloniaBitmap?.Dispose();
                }
            });
        }, token);
    }

    private void UpdatePreview()
    {
        if (_previewSource is null)
            return;
        using var adjusted = PhotoFilterEngine.ApplyAdjustments(_previewSource, BuildAdjustments());
        PreviewBitmap = SkiaToAvalonia(adjusted);
    }

    private static Bitmap? SkiaToAvalonia(SKBitmap? skia)
    {
        if (skia is null)
            return null;
        try
        {
            using var image = SKImage.FromBitmap(skia);
            using var data = image.Encode(SKEncodedImageFormat.Png, 85);
            using var mem = new MemoryStream();
            data.SaveTo(mem);
            mem.Position = 0;
            return new Bitmap(mem);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        ActiveTab = tab;
        OnPropertyChanged(nameof(IsTabLight));
        OnPropertyChanged(nameof(IsTabColor));
        OnPropertyChanged(nameof(IsTabDetail));
    }

    [RelayCommand]
    private void ToggleCompare()
    {
        IsComparing = !IsComparing;
    }

    [RelayCommand]
    private void ResetLight()
    {
        Exposure = 0.0;
        Brightness = 0.0;
        Contrast = 0.0;
        Highlights = 0.0;
        Shadows = 0.0;
        Whites = 0.0;
        Blacks = 0.0;
        Gamma = 1.0;
    }

    [RelayCommand]
    private void ResetColor()
    {
        Temperature = 0.0;
        Tint = 0.0;
        Saturation = 0.0;
        Vibrance = 0.0;
        Hue = 0.0;
    }

    [RelayCommand]
    private void ResetDetail()
    {
        Dehaze = 0.0;
        Clarity = 0.0;
        Grain = 0.0;
        NoiseReduction = 0.0;
    }

    [RelayCommand]
    private void ResetAll()
    {
        ResetLight();
        ResetColor();
        ResetDetail();
    }

    [RelayCommand]
    private async Task SaveCopyAsync()
    {
        if (_originalFull is null || IsSaving)
            return;

        IsSaving = true;
        StatusMessage = "Applying adjustments and exporting...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_edited{ext}";
            string outPath;

            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(defaultName);
                if (string.IsNullOrWhiteSpace(chosen))
                {
                    IsSaving = false;
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

            var adj = BuildAdjustments();
            var full = _originalFull;

            await Task.Run(() =>
            {
                using var output = PhotoFilterEngine.ApplyAdjustments(full, adj);
                using var image = SKImage.FromBitmap(output);

                var format = Path.GetExtension(outPath).ToLowerInvariant() switch
                {
                    ".png" => SKEncodedImageFormat.Png,
                    ".webp" => SKEncodedImageFormat.Webp,
                    _ => SKEncodedImageFormat.Jpeg
                };

                using var data = image.Encode(format, 95);
                using var fileStream = File.OpenWrite(outPath);
                data.SaveTo(fileStream);
            });

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to save edited photo");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
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
        if (_disposed)
            return;
        _disposed = true;
        _renderCts?.Cancel();
        _renderCts?.Dispose();

        _previewSource?.Dispose();
        _originalFull?.Dispose();
        PreviewBitmap?.Dispose();
        OriginalPreviewBitmap?.Dispose();
    }
}
