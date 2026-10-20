using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class FramingOverlayWindow : Window
{
    private bool _isDraggingImage;
    private Point _lastPointerPoint;

    public FramingOverlayWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        PreviewBorder.PointerPressed += OnPreviewPointerPressed;
        PreviewBorder.PointerMoved += OnPreviewPointerMoved;
        PreviewBorder.PointerReleased += OnPreviewPointerReleased;
        PreviewBorder.DoubleTapped += OnPreviewDoubleTapped;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FramingOverlayViewModel vm && vm.IsEditing)
        {
            var cp = e.GetCurrentPoint(PreviewBorder);
            if (cp.Properties.IsLeftButtonPressed)
            {
                _isDraggingImage = true;
                _lastPointerPoint = e.GetPosition(PreviewBorder);
                e.Pointer.Capture(PreviewBorder);
                e.Handled = true;
            }
        }
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDraggingImage && DataContext is FramingOverlayViewModel vm && vm.IsEditing)
        {
            var currentPoint = e.GetPosition(PreviewBorder);
            var deltaX = currentPoint.X - _lastPointerPoint.X;
            var deltaY = currentPoint.Y - _lastPointerPoint.Y;
            _lastPointerPoint = currentPoint;

            vm.MoveImageOffset(deltaX, deltaY, PreviewBorder.Bounds.Width, PreviewBorder.Bounds.Height);
            e.Handled = true;
        }
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingImage)
        {
            _isDraggingImage = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnPreviewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FramingOverlayViewModel vm && vm.IsEditing)
        {
            vm.ResetPosition();
            e.Handled = true;
        }
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
        if (DataContext is FramingOverlayViewModel vm)
        {
            vm.CloseRequested += Close;
            vm.BrowseImageRequested += async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Background Image",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Images")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.avif"]
                        }
                    ]
                });
                return files.Count > 0 ? files[0].Path.LocalPath : null;
            };
            vm.SaveAsRequested += async defaultName =>
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Framed Image",
                    DefaultExtension = System.IO.Path.GetExtension(defaultName).TrimStart('.'),
                    SuggestedFileName = defaultName,
                });
                return file?.Path.LocalPath;
            };
            Closed += (_, _) => vm.Dispose();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.R && e.KeyModifiers == KeyModifiers.None)
        {
            if (DataContext is FramingOverlayViewModel vm)
            {
                vm.Reset();
                e.Handled = true;
            }
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
