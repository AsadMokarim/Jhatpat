using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class AnnotateWindow : Window
{
    public AnnotateWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        CanvasContainer.PointerPressed += OnCanvasPointerPressed;
        CanvasContainer.PointerMoved += OnCanvasPointerMoved;
        CanvasContainer.PointerReleased += OnCanvasPointerReleased;
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

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AnnotateViewModel vm && CanvasContainer.Bounds.Width > 0 && CanvasContainer.Bounds.Height > 0)
        {
            var pt = e.GetPosition(CanvasContainer);
            vm.OnPointerDown(pt, CanvasContainer.Bounds.Width, CanvasContainer.Bounds.Height);
            e.Pointer.Capture(CanvasContainer);
            e.Handled = true;
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is AnnotateViewModel vm && CanvasContainer.Bounds.Width > 0 && CanvasContainer.Bounds.Height > 0)
        {
            var pt = e.GetPosition(CanvasContainer);
            vm.OnPointerMove(pt, CanvasContainer.Bounds.Width, CanvasContainer.Bounds.Height);
            e.Handled = true;
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is AnnotateViewModel vm)
        {
            vm.OnPointerUp();
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AnnotateViewModel vm)
        {
            vm.CloseRequested += Close;

            vm.SaveAsRequested += async defaultName =>
            {
                var ext = System.IO.Path.GetExtension(defaultName).TrimStart('.');
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Annotated Image",
                    DefaultExtension = ext,
                    SuggestedFileName = defaultName,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Image (*." + ext + ")")
                        {
                            Patterns = ["*." + ext]
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
}
