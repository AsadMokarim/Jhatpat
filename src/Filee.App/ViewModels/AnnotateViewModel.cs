// ViewModel for image annotation: freehand pen, shapes, arrows, highlights, text, redaction, and numbering.

using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
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

public sealed partial class AnnotateViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private bool _disposed;

    private SKBitmap? _originalFull;
    private SKBitmap? _previewSource;
    private double _previewScale = 1.0;

    private readonly List<List<AnnotationItem>> _undoStack = [];
    private readonly List<List<AnnotationItem>> _redoStack = [];

    // Temporary active in-progress drawing shape
    private AnnotationItem? _currentDrawingItem;
    private Point _startPoint;
    private bool _isDrawing;

    public event Action? CloseRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private int _imageWidth;
    [ObservableProperty] private int _imageHeight;
    [ObservableProperty] private string _resolutionText = "";

    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private AnnotationTool _activeTool = AnnotationTool.Pen;
    [ObservableProperty] private string _selectedColorKey = "Red";
    [ObservableProperty] private float _strokeWidth = 4f;
    [ObservableProperty] private string _textInput = "Sample Text";
    [ObservableProperty] private int _nextBadgeNumber = 1;

    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public ObservableCollection<AnnotationItem> Annotations { get; } = [];

    public bool IsToolPen => ActiveTool == AnnotationTool.Pen;
    public bool IsToolArrow => ActiveTool == AnnotationTool.Arrow;
    public bool IsToolRect => ActiveTool == AnnotationTool.Rectangle;
    public bool IsToolEllipse => ActiveTool == AnnotationTool.Ellipse;
    public bool IsToolLine => ActiveTool == AnnotationTool.Line;
    public bool IsToolText => ActiveTool == AnnotationTool.Text;
    public bool IsToolHighlight => ActiveTool == AnnotationTool.Highlight;
    public bool IsToolRedact => ActiveTool == AnnotationTool.Redact;
    public bool IsToolBadge => ActiveTool == AnnotationTool.Badge;

    public bool IsColorRed => SelectedColorKey == "Red";
    public bool IsColorOrange => SelectedColorKey == "Orange";
    public bool IsColorYellow => SelectedColorKey == "Yellow";
    public bool IsColorGreen => SelectedColorKey == "Green";
    public bool IsColorBlue => SelectedColorKey == "Blue";
    public bool IsColorPurple => SelectedColorKey == "Purple";
    public bool IsColorWhite => SelectedColorKey == "White";
    public bool IsColorBlack => SelectedColorKey == "Black";

    public AnnotateViewModel(string imagePath, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
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
        if (!File.Exists(path)) return;

        try
        {
            _originalFull = ResizeService.LoadSourceBitmap(path);
            ImageWidth = _originalFull.Width;
            ImageHeight = _originalFull.Height;
            ResolutionText = $"{ImageWidth} × {ImageHeight}";

            const int maxDim = 1400;
            if (_originalFull.Width > maxDim || _originalFull.Height > maxDim)
            {
                var s = Math.Min((double)maxDim / _originalFull.Width, (double)maxDim / _originalFull.Height);
                var tw = Math.Max(1, (int)(_originalFull.Width * s));
                var th = Math.Max(1, (int)(_originalFull.Height * s));
                _previewSource = _originalFull.Resize(new SKImageInfo(tw, th), new SKSamplingOptions(SKFilterMode.Linear));
                _previewScale = (double)tw / _originalFull.Width;
            }
            else
            {
                _previewSource = _originalFull.Copy();
                _previewScale = 1.0;
            }

            RenderPreview();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load image for annotation: {Path}", path);
            StatusMessage = $"Failed to open image: {ex.Message}";
        }
    }

    private SKColor ResolveSelectedColor() => SelectedColorKey switch
    {
        "Orange" => SKColor.Parse("#F97316"),
        "Yellow" => SKColor.Parse("#EAB308"),
        "Green" => SKColor.Parse("#22C55E"),
        "Blue" => SKColor.Parse("#3B82F6"),
        "Purple" => SKColor.Parse("#A855F7"),
        "White" => SKColors.White,
        "Black" => SKColors.Black,
        _ => SKColor.Parse("#EF4444")
    };

    [RelayCommand]
    private void SetTool(string toolName)
    {
        ActiveTool = toolName switch
        {
            "Arrow" => AnnotationTool.Arrow,
            "Rect" => AnnotationTool.Rectangle,
            "Ellipse" => AnnotationTool.Ellipse,
            "Line" => AnnotationTool.Line,
            "Text" => AnnotationTool.Text,
            "Highlight" => AnnotationTool.Highlight,
            "Redact" => AnnotationTool.Redact,
            "Badge" => AnnotationTool.Badge,
            _ => AnnotationTool.Pen
        };
        UpdateToolProperties();
    }

    [RelayCommand]
    private void SetColor(string colorKey)
    {
        SelectedColorKey = colorKey;
        UpdateColorProperties();
    }

    [RelayCommand]
    private void SetStroke(double width)
    {
        StrokeWidth = (float)width;
    }

    private void UpdateToolProperties()
    {
        OnPropertyChanged(nameof(IsToolPen));
        OnPropertyChanged(nameof(IsToolArrow));
        OnPropertyChanged(nameof(IsToolRect));
        OnPropertyChanged(nameof(IsToolEllipse));
        OnPropertyChanged(nameof(IsToolLine));
        OnPropertyChanged(nameof(IsToolText));
        OnPropertyChanged(nameof(IsToolHighlight));
        OnPropertyChanged(nameof(IsToolRedact));
        OnPropertyChanged(nameof(IsToolBadge));
    }

    private void UpdateColorProperties()
    {
        OnPropertyChanged(nameof(IsColorRed));
        OnPropertyChanged(nameof(IsColorOrange));
        OnPropertyChanged(nameof(IsColorYellow));
        OnPropertyChanged(nameof(IsColorGreen));
        OnPropertyChanged(nameof(IsColorBlue));
        OnPropertyChanged(nameof(IsColorPurple));
        OnPropertyChanged(nameof(IsColorWhite));
        OnPropertyChanged(nameof(IsColorBlack));
    }

    public void OnPointerDown(Point relativePoint, double elementW, double elementH)
    {
        if (_previewSource is null || elementW <= 0 || elementH <= 0) return;

        var fullX = (float)(relativePoint.X / elementW * ImageWidth);
        var fullY = (float)(relativePoint.Y / elementH * ImageHeight);
        _startPoint = new Point(fullX, fullY);
        _isDrawing = true;

        SaveStateForUndo();

        var col = ResolveSelectedColor();

        switch (ActiveTool)
        {
            case AnnotationTool.Pen:
                _currentDrawingItem = new PenAnnotation
                {
                    Color = col,
                    StrokeWidth = StrokeWidth,
                    Points = [new SKPoint(fullX, fullY)]
                };
                break;

            case AnnotationTool.Highlight:
                _currentDrawingItem = new HighlightAnnotation
                {
                    Color = col,
                    StrokeWidth = StrokeWidth,
                    Points = [new SKPoint(fullX, fullY)]
                };
                break;

            case AnnotationTool.Arrow:
                _currentDrawingItem = new ArrowAnnotation
                {
                    Color = col,
                    StrokeWidth = StrokeWidth,
                    Start = new SKPoint(fullX, fullY),
                    End = new SKPoint(fullX, fullY)
                };
                break;

            case AnnotationTool.Rectangle:
                _currentDrawingItem = new RectAnnotation
                {
                    Color = col,
                    StrokeWidth = StrokeWidth,
                    Rect = new SKRect(fullX, fullY, fullX, fullY)
                };
                break;

            case AnnotationTool.Ellipse:
                _currentDrawingItem = new EllipseAnnotation
                {
                    Color = col,
                    StrokeWidth = StrokeWidth,
                    Rect = new SKRect(fullX, fullY, fullX, fullY)
                };
                break;

            case AnnotationTool.Line:
                _currentDrawingItem = new LineAnnotation
                {
                    Color = col,
                    StrokeWidth = StrokeWidth,
                    Start = new SKPoint(fullX, fullY),
                    End = new SKPoint(fullX, fullY)
                };
                break;

            case AnnotationTool.Redact:
                _currentDrawingItem = new RedactAnnotation
                {
                    Color = SKColors.Black,
                    StrokeWidth = 1,
                    Rect = new SKRect(fullX, fullY, fullX, fullY)
                };
                break;

            case AnnotationTool.Badge:
                _currentDrawingItem = new BadgeAnnotation
                {
                    Color = col,
                    Center = new SKPoint(fullX, fullY),
                    Number = NextBadgeNumber++,
                    Radius = Math.Max(16f, StrokeWidth * 4f)
                };
                Annotations.Add(_currentDrawingItem);
                _isDrawing = false;
                _currentDrawingItem = null;
                RenderPreview();
                break;

            case AnnotationTool.Text:
                _currentDrawingItem = new TextAnnotation
                {
                    Color = col,
                    Position = new SKPoint(fullX, fullY),
                    Text = TextInput,
                    FontSize = Math.Max(18f, StrokeWidth * 4.5f)
                };
                Annotations.Add(_currentDrawingItem);
                _isDrawing = false;
                _currentDrawingItem = null;
                RenderPreview();
                break;
        }

        if (_currentDrawingItem is not null)
        {
            Annotations.Add(_currentDrawingItem);
            RenderPreview();
        }
    }

    public void OnPointerMove(Point relativePoint, double elementW, double elementH)
    {
        if (!_isDrawing || _currentDrawingItem is null || _previewSource is null || elementW <= 0 || elementH <= 0) return;

        var fullX = (float)(relativePoint.X / elementW * ImageWidth);
        var fullY = (float)(relativePoint.Y / elementH * ImageHeight);

        switch (_currentDrawingItem)
        {
            case PenAnnotation pen:
                pen.Points.Add(new SKPoint(fullX, fullY));
                break;

            case HighlightAnnotation hl:
                hl.Points.Add(new SKPoint(fullX, fullY));
                break;

            case ArrowAnnotation arrow:
                var newArrow = arrow with { End = new SKPoint(fullX, fullY) };
                ReplaceCurrentDrawing(newArrow);
                break;

            case RectAnnotation rect:
                var newRect = rect with
                {
                    Rect = new SKRect(
                        (float)Math.Min(_startPoint.X, fullX),
                        (float)Math.Min(_startPoint.Y, fullY),
                        (float)Math.Max(_startPoint.X, fullX),
                        (float)Math.Max(_startPoint.Y, fullY))
                };
                ReplaceCurrentDrawing(newRect);
                break;

            case EllipseAnnotation ell:
                var newEll = ell with
                {
                    Rect = new SKRect(
                        (float)Math.Min(_startPoint.X, fullX),
                        (float)Math.Min(_startPoint.Y, fullY),
                        (float)Math.Max(_startPoint.X, fullX),
                        (float)Math.Max(_startPoint.Y, fullY))
                };
                ReplaceCurrentDrawing(newEll);
                break;

            case LineAnnotation line:
                var newLine = line with { End = new SKPoint(fullX, fullY) };
                ReplaceCurrentDrawing(newLine);
                break;

            case RedactAnnotation redact:
                var newRedact = redact with
                {
                    Rect = new SKRect(
                        (float)Math.Min(_startPoint.X, fullX),
                        (float)Math.Min(_startPoint.Y, fullY),
                        (float)Math.Max(_startPoint.X, fullX),
                        (float)Math.Max(_startPoint.Y, fullY))
                };
                ReplaceCurrentDrawing(newRedact);
                break;
        }

        RenderPreview();
    }

    public void OnPointerUp()
    {
        _isDrawing = false;
        _currentDrawingItem = null;
    }

    private void ReplaceCurrentDrawing(AnnotationItem item)
    {
        if (Annotations.Count > 0)
        {
            Annotations[Annotations.Count - 1] = item;
            _currentDrawingItem = item;
        }
    }

    private void SaveStateForUndo()
    {
        _undoStack.Add([.. Annotations]);
        _redoStack.Clear();
        CanUndo = _undoStack.Count > 0;
        CanRedo = false;
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;

        var previous = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add([.. Annotations]);

        Annotations.Clear();
        foreach (var item in previous)
        {
            Annotations.Add(item);
        }

        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        RenderPreview();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;

        var next = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add([.. Annotations]);

        Annotations.Clear();
        foreach (var item in next)
        {
            Annotations.Add(item);
        }

        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        RenderPreview();
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Annotations.Count == 0) return;
        SaveStateForUndo();
        Annotations.Clear();
        NextBadgeNumber = 1;
        RenderPreview();
    }

    private void RenderPreview()
    {
        if (_previewSource is null) return;

        var canvasW = _previewSource.Width;
        var canvasH = _previewSource.Height;

        using var output = new SKBitmap(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.DrawBitmap(_previewSource, 0, 0);

        var scale = (float)_previewScale;
        foreach (var item in Annotations)
        {
            item.Render(canvas, _previewSource, scale);
        }

        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 85);
        using var mem = new MemoryStream();
        data.SaveTo(mem);
        mem.Position = 0;

        var avBmp = new Bitmap(mem);
        var old = PreviewBitmap;
        PreviewBitmap = avBmp;
        old?.Dispose();
    }

    [RelayCommand]
    private async Task SaveAnnotatedCopyAsync()
    {
        if (_originalFull is null || IsSaving) return;

        IsSaving = true;
        StatusMessage = "Exporting annotated image...";

        try
        {
            var ext = Path.GetExtension(FilePath);
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_annotated{ext}";
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

            var full = _originalFull;
            var items = Annotations.ToList();

            await Task.Run(() =>
            {
                using var output = AnnotationEngine.FlattenAnnotations(full, items);
                using var image = SKImage.FromBitmap(output);

                var format = Path.GetExtension(outPath).ToLowerInvariant() switch
                {
                    ".png" => SKEncodedImageFormat.Png,
                    ".webp" => SKEncodedImageFormat.Webp,
                    _ => SKEncodedImageFormat.Jpeg
                };

                using var data = image.Encode(format, 95);
                using var stream = File.OpenWrite(outPath);
                data.SaveTo(stream);
            });

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Saved to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to export annotated image");
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
        if (_disposed) return;
        _disposed = true;
        _previewSource?.Dispose();
        _originalFull?.Dispose();
        PreviewBitmap?.Dispose();
    }
}
