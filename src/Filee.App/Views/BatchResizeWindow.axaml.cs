using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class BatchResizeWindow : Window
{
    public BatchResizeWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        if (DataContext is BatchResizeViewModel vm)
        {
            vm.CloseRequested += Close;

            vm.AddFilesRequested += async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Images to Add",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Supported Images")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.tiff", "*.tif", "*.gif", "*.avif"]
                        }
                    ]
                });
                return files.Select(f => f.Path.LocalPath).ToList();
            };

            vm.AddFolderRequested += async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder Containing Images",
                    AllowMultiple = false,
                });
                return folders.Count > 0 ? folders[0].Path.LocalPath : null;
            };

            vm.BrowseFolderRequested += async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Output Directory",
                    AllowMultiple = false,
                });
                return folders.Count > 0 ? folders[0].Path.LocalPath : null;
            };

            vm.OpenFolderRequested += folder =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true,
                    });
                }
                catch { /* Ignore explorer launch errors */ }
            };

            Closed += (_, _) => vm.Dispose();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (DataContext is BatchResizeViewModel vm && vm.IsProcessing)
                vm.CancelBatch();
            else
                Close();
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (pt.Y < 48 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
