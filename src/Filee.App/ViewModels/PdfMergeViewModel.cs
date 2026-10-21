// ViewModel for the Merge PDFs tool: managing document order, adding/removing PDFs, and executing merge.

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Pdf;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class PdfMergeItemViewModel : ObservableObject
{
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _fileSizeText = "";
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private string _detailsText = "";
    [ObservableProperty] private bool _isFirst;
    [ObservableProperty] private bool _isLast;

    public PdfMergeItemViewModel(string filePath, int displayIndex)
    {
        FilePath = filePath;
        DisplayIndex = displayIndex;
        FileName = Path.GetFileName(filePath);

        try
        {
            var fileInfo = new FileInfo(filePath);
            var size = fileInfo.Exists ? fileInfo.Length : 0;
            FileSizeText = EnginePackageState.FormatBytes(size);
            PageCount = PdfToolEngine.GetPageCount(filePath);
            DetailsText = PageCount > 0
                ? $"{PageCount} {(PageCount == 1 ? "page" : "pages")} • {FileSizeText}"
                : FileSizeText;
        }
        catch
        {
            FileSizeText = "";
            DetailsText = "";
        }
    }
}

public sealed partial class PdfMergeViewModel : ObservableObject, IDisposable
{
    private readonly Preset? _preset;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly ILogger? _log;
    private bool _disposed;

    public event Action? CloseRequested;
    public event Func<Task<IReadOnlyList<string>?>>? AddFilesRequested;
    public event Func<string, Task<string?>>? SaveAsRequested;
    public event Action<string>? OpenOutputFolderRequested;

    public ObservableCollection<PdfMergeItemViewModel> Items { get; } = [];

    [ObservableProperty] private string _outputFileName = "merged_document.pdf";
    [ObservableProperty] private string? _outputDirectory;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _resultFilePath;

    public bool HasItems => Items.Count > 0;
    public bool CanMerge => !IsProcessing && Items.Count >= 2;
    public int ItemCount => Items.Count;

    public PdfMergeViewModel(IReadOnlyList<string> initialFiles, Preset? preset, ILocalizer loc, UserDataStore store, ILogger? log = null)
    {
        _preset = preset;
        _loc = loc;
        _store = store;
        _log = log;

        if (initialFiles.Count > 0)
        {
            var firstDir = Path.GetDirectoryName(initialFiles[0]);
            OutputDirectory = string.IsNullOrEmpty(firstDir) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : firstDir;
            var baseName = Path.GetFileNameWithoutExtension(initialFiles[0]);
            OutputFileName = $"{baseName}_merged.pdf";
        }

        AddFilesInternal(initialFiles);
    }

    public void AddFilesInternal(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (!Items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    Items.Add(new PdfMergeItemViewModel(path, Items.Count + 1));
                }
            }
        }
        RefreshOrderFlags();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(ItemCount));
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        if (AddFilesRequested is { } handler)
        {
            var selected = await handler();
            if (selected is { Count: > 0 })
            {
                AddFilesInternal(selected);
            }
        }
    }

    [RelayCommand]
    private void MoveUp(PdfMergeItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index > 0)
        {
            Items.Move(index, index - 1);
            RefreshOrderFlags();
        }
    }

    [RelayCommand]
    private void MoveDown(PdfMergeItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index >= 0 && index < Items.Count - 1)
        {
            Items.Move(index, index + 1);
            RefreshOrderFlags();
        }
    }

    [RelayCommand]
    private void RemoveItem(PdfMergeItemViewModel item)
    {
        Items.Remove(item);
        RefreshOrderFlags();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(ItemCount));
    }

    [RelayCommand]
    private void ClearAll()
    {
        Items.Clear();
        RefreshOrderFlags();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(ItemCount));
    }

    [RelayCommand]
    private async Task MergeAsync()
    {
        if (Items.Count < 2 || IsProcessing)
            return;

        IsProcessing = true;
        IsFinished = false;
        StatusMessage = "Merging PDFs...";

        try
        {
            string outPath;
            if (SaveAsRequested is { } saveAs)
            {
                var chosen = await saveAs(OutputFileName);
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
                var targetDir = !string.IsNullOrEmpty(OutputDirectory) ? OutputDirectory : Path.GetTempPath();
                outPath = Path.Combine(targetDir, OutputFileName);
            }

            var inputPaths = Items.Select(i => i.FilePath).ToList();
            await Task.Run(() => PdfToolEngine.MergePdfFiles(inputPaths, outPath));

            ResultFilePath = outPath;
            IsFinished = true;
            StatusMessage = $"Merged {Items.Count} documents successfully!";
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to merge PDFs");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            OnPropertyChanged(nameof(CanMerge));
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
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].DisplayIndex = i + 1;
            Items[i].IsFirst = i == 0;
            Items[i].IsLast = i == Items.Count - 1;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Items.Clear();
    }
}
