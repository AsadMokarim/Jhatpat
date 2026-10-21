// ViewModel for Organising PDF Pages: reordering, rotation, page removal, and saving.

using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Pdf;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Filee.App.ViewModels;

public sealed partial class PdfPageItemViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private int _originalPageIndex;
    [ObservableProperty] private int _displayPageNumber;
    [ObservableProperty] private int _rotationDegrees;
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isFirst;
    [ObservableProperty] private bool _isLast;

    public PdfPageItemViewModel(int originalPageIndex, int displayPageNumber)
    {
        OriginalPageIndex = originalPageIndex;
        DisplayPageNumber = displayPageNumber;
    }

    public void RotateClockwise()
    {
        RotationDegrees = (RotationDegrees + 90) % 360;
    }

    public void RotateCounterClockwise()
    {
        RotationDegrees = (RotationDegrees + 270) % 360;
    }

    public void SetThumbnailFromSkia(SKBitmap skiaBitmap)
    {
        try
        {
            using var image = SKImage.FromBitmap(skiaBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 85);
            using var mem = new MemoryStream();
            data.SaveTo(mem);
            mem.Position = 0;
            Thumbnail = new Bitmap(mem);
        }
        catch
        {
            Thumbnail = null;
        }
    }

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}

public sealed partial class PdfOrganizeViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private CancellationTokenSource? _loadingCts;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    public ObservableCollection<PdfPageItemViewModel> Pages { get; } = [];

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private int _initialPageCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public bool HasPages => Pages.Count > 0;
    public int CurrentPageCount => Pages.Count;
    public bool CanSave => !IsSaving && !IsLoading && Pages.Count > 0;

    public PdfOrganizeViewModel(string pdfPath, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        FilePath = pdfPath;
        FileName = Path.GetFileName(pdfPath);

        LoadPagesAsync(pdfPath);
    }

    private async void LoadPagesAsync(string path)
    {
        if (!File.Exists(path))
            return;

        IsLoading = true;
        StatusMessage = "Loading pages...";
        _loadingCts = new CancellationTokenSource();

        try
        {
            var count = await Task.Run(() => PdfToolEngine.GetPageCount(path));
            InitialPageCount = count;

            Pages.Clear();
            for (var i = 0; i < count; i++)
            {
                Pages.Add(new PdfPageItemViewModel(i, i + 1));
            }
            RefreshOrderFlags();
            OnPropertyChanged(nameof(HasPages));
            OnPropertyChanged(nameof(CurrentPageCount));

            // Load thumbnails in background
            await Task.Run(() =>
            {
                for (var i = 0; i < count; i++)
                {
                    if (_loadingCts.IsCancellationRequested)
                        break;

                    var skThumb = PdfToolEngine.RenderSinglePageThumbnail(path, i, 280);
                    if (skThumb is not null)
                    {
                        var pageItem = Pages.FirstOrDefault(p => p.OriginalPageIndex == i);
                        if (pageItem is not null)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                pageItem.SetThumbnailFromSkia(skThumb);
                                skThumb.Dispose();
                            });
                        }
                        else
                        {
                            skThumb.Dispose();
                        }
                    }
                }
            });

            StatusMessage = $"{count} pages loaded";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to load PDF pages for {Path}", path);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    private void MoveLeft(PdfPageItemViewModel page)
    {
        var index = Pages.IndexOf(page);
        if (index > 0)
        {
            Pages.Move(index, index - 1);
            RefreshOrderFlags();
        }
    }

    [RelayCommand]
    private void MoveRight(PdfPageItemViewModel page)
    {
        var index = Pages.IndexOf(page);
        if (index >= 0 && index < Pages.Count - 1)
        {
            Pages.Move(index, index + 1);
            RefreshOrderFlags();
        }
    }

    [RelayCommand]
    private void RotateClockwise(PdfPageItemViewModel page)
    {
        page.RotateClockwise();
    }

    [RelayCommand]
    private void RotateCounterClockwise(PdfPageItemViewModel page)
    {
        page.RotateCounterClockwise();
    }

    [RelayCommand]
    private void DeletePage(PdfPageItemViewModel page)
    {
        Pages.Remove(page);
        page.Dispose();
        RefreshOrderFlags();
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CurrentPageCount));
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private void RotateAllClockwise()
    {
        foreach (var page in Pages)
        {
            page.RotateClockwise();
        }
    }

    [RelayCommand]
    private void ResetOrder()
    {
        if (string.IsNullOrEmpty(FilePath))
            return;
        LoadPagesAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Pages.Count == 0 || IsSaving)
            return;

        IsSaving = true;
        StatusMessage = "Saving reorganized PDF...";

        try
        {
            var defaultName = $"{Path.GetFileNameWithoutExtension(FilePath)}_organized.pdf";
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

            var organizedPages = Pages
                .Select(p => new OrganizedPageInfo(p.OriginalPageIndex, p.RotationDegrees))
                .ToList();

            await Task.Run(() => PdfToolEngine.SaveOrganizedPdf(FilePath, organizedPages, outPath));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Saved {Pages.Count} pages to {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to save organized PDF");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            OnPropertyChanged(nameof(CanSave));
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
        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].DisplayPageNumber = i + 1;
            Pages[i].IsFirst = i == 0;
            Pages[i].IsLast = i == Pages.Count - 1;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();

        foreach (var page in Pages)
        {
            page.Dispose();
        }
        Pages.Clear();
    }
}
