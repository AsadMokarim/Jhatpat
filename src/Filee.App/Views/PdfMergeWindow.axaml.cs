using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Filee.App.Services;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class PdfMergeWindow : Window
{
    public PdfMergeWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void PositionOnScreen(PixelPoint? near = null)
    {
        var screen = (near.HasValue ? Screens.ScreenFromPoint(near.Value) : null)
            ?? Screens.ScreenFromPoint(Screens.Primary?.Bounds.Center ?? default)
            ?? Screens.Primary;

        if (screen is not null)
        {
            var scaling = screen.Scaling;
            var area = screen.WorkingArea;
            var winW = (int)(Width * scaling);
            var winH = (int)(Height * scaling);
            var x = area.X + Math.Max(0, (area.Width - winW) / 2);
            var y = area.Y + Math.Max(0, (area.Height - winH) / 2);
            Position = new PixelPoint(x, y);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PdfMergeViewModel vm)
        {
            vm.CloseRequested += Close;

            vm.AddFilesRequested += async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Documents and Images to Merge",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("PDF & Image Files")
                        {
                            Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.tiff", "*.tif", "*.gif", "*.avif", "*.heic", "*.ico", "*.svg"],
                            MimeTypes = ["application/pdf", "image/*"]
                        },
                        new FilePickerFileType("PDF Documents (*.pdf)")
                        {
                            Patterns = ["*.pdf"],
                            MimeTypes = ["application/pdf"]
                        },
                        new FilePickerFileType("Image Files")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.tiff", "*.tif", "*.gif", "*.avif", "*.heic", "*.ico", "*.svg"],
                            MimeTypes = ["image/*"]
                        }
                    ]
                });
                return files?.Select(f => f.Path.LocalPath).ToList();
            };

            vm.SaveAsRequested += async defaultName =>
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Merged PDF",
                    DefaultExtension = "pdf",
                    SuggestedFileName = defaultName,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("PDF Document (*.pdf)")
                        {
                            Patterns = ["*.pdf"],
                            MimeTypes = ["application/pdf"]
                        }
                    ]
                });
                return file?.Path.LocalPath;
            };

            vm.OpenOutputFolderRequested += path =>
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                    }
                }
                catch
                {
                    // Ignore launch failure
                }
            };

            Closed += (_, _) => vm.Dispose();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (pt.Y < 50 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = DraggedFiles.Local(e.DataTransfer);
        if (files.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = DraggedFiles.Local(e.DataTransfer);
        if (files.Count > 0 && DataContext is PdfMergeViewModel vm)
        {
            vm.AddFilesInternal(files);
            e.Handled = true;
        }
    }
}
