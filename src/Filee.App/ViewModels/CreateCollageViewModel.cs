// ViewModel for Create Collage: layout customization, live preview, and high-res export.

using System.Collections.ObjectModel;
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

public sealed partial class CollagePhotoItemViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isFirst;
    [ObservableProperty] private bool _isLast;

    public SKBitmap? FullBitmap { get; set; }

    public CollagePhotoItemViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        try
        {
            FullBitmap = ResizeService.LoadSourceBitmap(filePath);
            var scale = Math.Min(120.0 / FullBitmap.Width, 120.0 / FullBitmap.Height);
            var tw = Math.Max(1, (int)(FullBitmap.Width * scale));
            var th = Math.Max(1, (int)(FullBitmap.Height * scale));
            using var resized = ResizeService.ResizeBitmap(FullBitmap, tw, th);
            using var img = SKImage.FromBitmap(resized);
            using var data = img.Encode(SKEncodedImageFormat.Png, 85);
            using var mem = new MemoryStream();
            data.SaveTo(mem);
            mem.Position = 0;
            Thumbnail = new Bitmap(mem);
        }
        catch
        {
            FullBitmap = null;
            Thumbnail = null;
        }
    }

    public void Dispose()
    {
        Thumbnail?.Dispose();
        FullBitmap?.Dispose();
        Thumbnail = null;
        FullBitmap = null;
    }
}

public sealed partial class CreateCollageViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private CancellationTokenSource? _previewCts;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<Task<IReadOnlyList<string>?>>? AddPhotosRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    public ObservableCollection<CollagePhotoItemViewModel> Photos { get; } = [];

    // Configuration
    [ObservableProperty] private CollageLayout _layout = CollageLayout.Grid;
    [ObservableProperty] private string _aspectRatioPreset = "1:1";
    [ObservableProperty] private double _aspectRatio = 1.0;
    [ObservableProperty] private int _spacing = 12;
    [ObservableProperty] private int _outerPadding = 16;
    [ObservableProperty] private double _cornerRadius = 8.0;
    [ObservableProperty] private string _bgColorKey = "White";
    [ObservableProperty] private CollageFitMode _fitMode = CollageFitMode.Fill;

    // Live Preview
    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private bool _isRenderingPreview;
    [ObservableProperty] private string _dimensionsReadout = "2400 × 2400";

    // Execution state
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public bool HasPhotos => Photos.Count > 0;
    public int PhotoCount => Photos.Count;
    public bool CanCreate => !IsSaving && Photos.Count >= 1;

    public bool IsLayoutGrid => Layout == CollageLayout.Grid;
    public bool IsLayoutRow => Layout == CollageLayout.Row;
    public bool IsLayoutCol => Layout == CollageLayout.Column;
    public bool IsLayoutFeatured => Layout == CollageLayout.Featured;

    public bool IsAspectSquare => AspectRatioPreset == "1:1";
    public bool IsAspect43 => AspectRatioPreset == "4:3";
    public bool IsAspect169 => AspectRatioPreset == "16:9";
    public bool IsAspect916 => AspectRatioPreset == "9:16";
    public bool IsAspect32 => AspectRatioPreset == "3:2";
    public bool IsAspect23 => AspectRatioPreset == "2:3";

    public bool IsFitFill => FitMode == CollageFitMode.Fill;
    public bool IsFitContain => FitMode == CollageFitMode.Fit;

    public bool IsBgWhite => BgColorKey == "White";
    public bool IsBgBlack => BgColorKey == "Black";
    public bool IsBgTransparent => BgColorKey == "Transparent";
    public bool IsBgAccent => BgColorKey == "Accent";
    public bool IsBgDark => BgColorKey == "Dark";

    public CreateCollageViewModel(IReadOnlyList<string> initialFiles, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        AddPhotosInternal(initialFiles);
    }

    public void AddPhotosInternal(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path) && !Photos.Any(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                Photos.Add(new CollagePhotoItemViewModel(path));
            }
        }
        RefreshOrderFlags();
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(CanCreate));
        RequestPreviewUpdate();
    }

    [RelayCommand]
    private async Task AddPhotosAsync()
    {
        if (AddPhotosRequested is { } handler)
        {
            var selected = await handler();
            if (selected is { Count: > 0 })
            {
                AddPhotosInternal(selected);
            }
        }
    }

    [RelayCommand]
    private void MoveLeft(CollagePhotoItemViewModel photo)
    {
        var idx = Photos.IndexOf(photo);
        if (idx > 0)
        {
            Photos.Move(idx, idx - 1);
            RefreshOrderFlags();
            RequestPreviewUpdate();
        }
    }

    [RelayCommand]
    private void MoveRight(CollagePhotoItemViewModel photo)
    {
        var idx = Photos.IndexOf(photo);
        if (idx >= 0 && idx < Photos.Count - 1)
        {
            Photos.Move(idx, idx + 1);
            RefreshOrderFlags();
            RequestPreviewUpdate();
        }
    }

    [RelayCommand]
    private void RemovePhoto(CollagePhotoItemViewModel photo)
    {
        Photos.Remove(photo);
        photo.Dispose();
        RefreshOrderFlags();
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(CanCreate));
        RequestPreviewUpdate();
    }

    [RelayCommand]
    private void SetLayout(string layoutName)
    {
        Layout = layoutName switch
        {
            "Row" => CollageLayout.Row,
            "Col" => CollageLayout.Column,
            "Featured" => CollageLayout.Featured,
            _ => CollageLayout.Grid
        };
        OnPropertyChanged(nameof(IsLayoutGrid));
        OnPropertyChanged(nameof(IsLayoutRow));
        OnPropertyChanged(nameof(IsLayoutCol));
        OnPropertyChanged(nameof(IsLayoutFeatured));
        RequestPreviewUpdate();
    }

    [RelayCommand]
    private void SetAspectRatio(string preset)
    {
        AspectRatioPreset = preset;
        AspectRatio = preset switch
        {
            "4:3" => 4.0 / 3.0,
            "16:9" => 16.0 / 9.0,
            "9:16" => 9.0 / 16.0,
            "3:2" => 3.0 / 2.0,
            "2:3" => 2.0 / 3.0,
            _ => 1.0
        };
        var w = 2400;
        var h = (int)Math.Round(w / AspectRatio);
        DimensionsReadout = $"{w} × {h}";

        OnPropertyChanged(nameof(IsAspectSquare));
        OnPropertyChanged(nameof(IsAspect43));
        OnPropertyChanged(nameof(IsAspect169));
        OnPropertyChanged(nameof(IsAspect916));
        OnPropertyChanged(nameof(IsAspect32));
        OnPropertyChanged(nameof(IsAspect23));
        RequestPreviewUpdate();
    }

    [RelayCommand]
    private void SetBgColor(string key)
    {
        BgColorKey = key;
        OnPropertyChanged(nameof(IsBgWhite));
        OnPropertyChanged(nameof(IsBgBlack));
        OnPropertyChanged(nameof(IsBgTransparent));
        OnPropertyChanged(nameof(IsBgAccent));
        OnPropertyChanged(nameof(IsBgDark));
        RequestPreviewUpdate();
    }

    [RelayCommand]
    private void SetFitMode(string mode)
    {
        FitMode = mode == "Fit" ? CollageFitMode.Fit : CollageFitMode.Fill;
        OnPropertyChanged(nameof(IsFitFill));
        OnPropertyChanged(nameof(IsFitContain));
        RequestPreviewUpdate();
    }

    partial void OnSpacingChanged(int value) => RequestPreviewUpdate();
    partial void OnOuterPaddingChanged(int value) => RequestPreviewUpdate();
    partial void OnCornerRadiusChanged(double value) => RequestPreviewUpdate();

    private SKColor ResolveBgColor() => BgColorKey switch
    {
        "Black" => SKColors.Black,
        "Transparent" => SKColors.Transparent,
        "Accent" => SKColor.Parse("#EA580C"),
        "Dark" => SKColor.Parse("#1A1A22"),
        _ => SKColors.White
    };

    private void RequestPreviewUpdate()
    {
        if (Photos.Count == 0)
        {
            PreviewBitmap = null;
            return;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        var validBitmaps = Photos
            .Select(p => p.FullBitmap)
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        if (validBitmaps.Count == 0)
            return;

        const int previewW = 1200;
        var scale = (double)previewW / 2400.0;

        var options = new CollageOptions
        {
            Layout = Layout,
            AspectRatio = AspectRatio,
            CanvasWidth = previewW,
            Spacing = Math.Max(0, (int)Math.Round(Spacing * scale)),
            Padding = Math.Max(0, (int)Math.Round(OuterPadding * scale)),
            CornerRadius = Math.Max(0, CornerRadius * scale),
            BackgroundColor = ResolveBgColor(),
            FitMode = FitMode,
        };

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            using var collage = CollageEngine.RenderCollage(validBitmaps, options);
            if (token.IsCancellationRequested)
                return;

            using var image = SKImage.FromBitmap(collage);
            using var data = image.Encode(SKEncodedImageFormat.Png, 85);
            using var mem = new MemoryStream();
            data.SaveTo(mem);
            mem.Position = 0;
            var avBmp = new Bitmap(mem);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    var old = PreviewBitmap;
                    PreviewBitmap = avBmp;
                    old?.Dispose();
                }
                else
                {
                    avBmp.Dispose();
                }
            });
        }, token);
    }

    [RelayCommand]
    private async Task CreateCollageAsync()
    {
        var validBitmaps = Photos
            .Select(p => p.FullBitmap)
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        if (validBitmaps.Count == 0 || IsSaving)
            return;

        IsSaving = true;
        StatusMessage = "Rendering high-resolution collage...";

        try
        {
            const string defaultName = "collage.png";
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
                var dir = Path.GetDirectoryName(Photos[0].FilePath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, defaultName);
            }

            var fullOptions = new CollageOptions
            {
                Layout = Layout,
                AspectRatio = AspectRatio,
                CanvasWidth = 2400,
                Spacing = Spacing,
                Padding = OuterPadding,
                CornerRadius = CornerRadius,
                BackgroundColor = ResolveBgColor(),
                FitMode = FitMode,
            };

            await Task.Run(() =>
            {
                using var collage = CollageEngine.RenderCollage(validBitmaps, fullOptions);
                using var image = SKImage.FromBitmap(collage);

                var format = Path.GetExtension(outPath).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                    ".webp" => SKEncodedImageFormat.Webp,
                    _ => SKEncodedImageFormat.Png
                };

                using var data = image.Encode(format, 95);
                using var stream = File.OpenWrite(outPath);
                data.SaveTo(stream);
            });

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Collage exported to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to create collage");
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

    private void RefreshOrderFlags()
    {
        for (var i = 0; i < Photos.Count; i++)
        {
            Photos[i].IsFirst = i == 0;
            Photos[i].IsLast = i == Photos.Count - 1;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _previewCts?.Cancel();
        _previewCts?.Dispose();

        foreach (var photo in Photos)
        {
            photo.Dispose();
        }
        Photos.Clear();
        PreviewBitmap?.Dispose();
    }
}
