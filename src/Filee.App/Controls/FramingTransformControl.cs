// Interactive Canva/Photoshop-style transform overlay for background framing.
// Provides live bounding box, 8 transform handles, magnetic center snapping guides, and position readout badge.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.ViewModels;

namespace Filee.App.Controls;

public sealed class FramingTransformControl : Control
{
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<FramingTransformControl, IBrush?>(nameof(AccentBrush));

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private bool _isDragging;
    private Point _dragStartPointer;
    private double _dragStartRelX;
    private double _dragStartRelY;
    private bool _isPointerOverImage;
    private Rect _lastImageRect;
    private Rect _lastCanvasRect;

    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    public FramingTransformControl()
    {
        ClipToBounds = true;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is FramingOverlayViewModel vm)
        {
            vm.LayoutUpdated += () => InvalidateVisual();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(vm.PreviewImage)
                    or nameof(vm.RelativeOffsetX)
                    or nameof(vm.RelativeOffsetY)
                    or nameof(vm.TargetX)
                    or nameof(vm.TargetY)
                    or nameof(vm.CanvasWidth)
                    or nameof(vm.CanvasHeight))
                {
                    InvalidateVisual();
                }
            };
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (DataContext is not FramingOverlayViewModel vm || !vm.IsEditing || vm.CanvasWidth <= 0 || vm.CanvasHeight <= 0)
            return;

        double availW = Bounds.Width;
        double availH = Bounds.Height;
        if (availW <= 0 || availH <= 0)
            return;

        double scale = Math.Min(availW / vm.CanvasWidth, availH / vm.CanvasHeight);
        double displayedCanvasW = vm.CanvasWidth * scale;
        double displayedCanvasH = vm.CanvasHeight * scale;
        double displayedCanvasX = (availW - displayedCanvasW) / 2.0;
        double displayedCanvasY = (availH - displayedCanvasH) / 2.0;

        var canvasRect = new Rect(displayedCanvasX, displayedCanvasY, displayedCanvasW, displayedCanvasH);
        var imageRect = new Rect(
            displayedCanvasX + vm.TargetX * scale,
            displayedCanvasY + vm.TargetY * scale,
            vm.TargetWidth * scale,
            vm.TargetHeight * scale);

        _lastCanvasRect = canvasRect;
        _lastImageRect = imageRect;

        if (imageRect.Width <= 0 || imageRect.Height <= 0)
            return;

        var accentBrush = AccentBrush ?? new SolidColorBrush(Color.Parse("#6366F1"));

        // 1. Center Alignment Guides (Crosshairs) - pink/magenta magnetic alignment
        var isHCentered = Math.Abs(vm.RelativeOffsetX) < 0.005;
        var isVCentered = Math.Abs(vm.RelativeOffsetY) < 0.005;

        if (isHCentered || _isDragging)
        {
            if (isHCentered)
            {
                var hGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(230, 236, 72, 153)), 1.5, new DashStyle([4, 3], 0));
                context.DrawLine(hGuidePen, new Point(canvasRect.Center.X, canvasRect.Top), new Point(canvasRect.Center.X, canvasRect.Bottom));
            }
        }

        if (isVCentered || _isDragging)
        {
            if (isVCentered)
            {
                var vGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(230, 236, 72, 153)), 1.5, new DashStyle([4, 3], 0));
                context.DrawLine(vGuidePen, new Point(canvasRect.Left, canvasRect.Center.Y), new Point(canvasRect.Right, canvasRect.Center.Y));
            }
        }

        // 2. Canva/Photoshop Style Inner Image Bounding Box
        double maxR = Math.Min(imageRect.Width, imageRect.Height) / 2.0;
        double r = Math.Clamp(vm.CornerRadius * scale, 0.0, maxR);

        var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 3.0);
        var boxPen = new Pen(accentBrush, 1.5);

        context.DrawRectangle(null, shadowPen, new RoundedRect(imageRect, r, r));
        context.DrawRectangle(null, boxPen, new RoundedRect(imageRect, r, r));

        // 3. 8 Transform Handles
        // 4 Corner circular handles
        var corners = new[]
        {
            new Point(imageRect.Left, imageRect.Top),
            new Point(imageRect.Right, imageRect.Top),
            new Point(imageRect.Left, imageRect.Bottom),
            new Point(imageRect.Right, imageRect.Bottom),
        };

        var handleFill = new SolidColorBrush(Colors.White);
        var handleShadow = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

        foreach (var c in corners)
        {
            context.DrawEllipse(handleShadow, null, new Point(c.X, c.Y + 1), 5.5, 5.5);
            context.DrawEllipse(handleFill, boxPen, c, 4.5, 4.5);
        }

        // 4 Edge midpoint pill handles (Canva signature style)
        var edgePills = new[]
        {
            new Rect(imageRect.Center.X - 7, imageRect.Top - 2, 14, 4),
            new Rect(imageRect.Center.X - 7, imageRect.Bottom - 2, 14, 4),
            new Rect(imageRect.Left - 2, imageRect.Center.Y - 7, 4, 14),
            new Rect(imageRect.Right - 2, imageRect.Center.Y - 7, 4, 14),
        };

        var pillBorderPen = new Pen(accentBrush, 1.0);
        foreach (var ep in edgePills)
        {
            context.DrawRectangle(handleFill, pillBorderPen, new RoundedRect(ep, 2, 2));
        }

        // 4. Live Coordinate / Position Floating Badge
        if (_isDragging || _isPointerOverImage)
        {
            int maxOffsetX = (vm.CanvasWidth - vm.TargetWidth) / 2;
            int maxOffsetY = (vm.CanvasHeight - vm.TargetHeight) / 2;
            int pxOffsetX = (int)Math.Round(vm.RelativeOffsetX * maxOffsetX);
            int pxOffsetY = (int)Math.Round(vm.RelativeOffsetY * maxOffsetY);

            string badgeText;
            IBrush textBrush;
            if (isHCentered && isVCentered)
            {
                badgeText = "✦ Centered";
                textBrush = new SolidColorBrush(Color.Parse("#34D399")); // Emerald
            }
            else
            {
                var signX = pxOffsetX >= 0 ? "+" : "";
                var signY = pxOffsetY >= 0 ? "+" : "";
                badgeText = $"X: {signX}{pxOffsetX} px   Y: {signY}{pxOffsetY} px";
                textBrush = new SolidColorBrush(Colors.White);
            }

            var ft = new FormattedText(
                badgeText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                11.0,
                textBrush);

            double badgePaddingH = 10.0;
            double badgePaddingV = 4.0;
            double badgeW = ft.Width + badgePaddingH * 2;
            double badgeH = ft.Height + badgePaddingV * 2;
            double badgeX = imageRect.Center.X - badgeW / 2.0;
            double badgeY = Math.Max(displayedCanvasY + 6, imageRect.Top - badgeH - 8);

            // Clamp badge within viewport
            badgeX = Math.Clamp(badgeX, 8, availW - badgeW - 8);

            var badgeRect = new Rect(badgeX, badgeY, badgeW, badgeH);
            var badgeBg = new SolidColorBrush(Color.FromArgb(230, 20, 20, 32));
            var badgeBorder = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.0);

            context.DrawRectangle(badgeBg, badgeBorder, new RoundedRect(badgeRect, 10, 10));
            context.DrawText(ft, new Point(badgeX + badgePaddingH, badgeY + badgePaddingV));
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdateHoverState(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerOverImage = false;
        Cursor = ArrowCursor;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isDragging && DataContext is FramingOverlayViewModel vm && vm.IsEditing)
        {
            double deltaX = pos.X - _dragStartPointer.X;
            double deltaY = pos.Y - _dragStartPointer.Y;

            double maxScreenOffsetX = (_lastCanvasRect.Width - _lastImageRect.Width) / 2.0;
            double maxScreenOffsetY = (_lastCanvasRect.Height - _lastImageRect.Height) / 2.0;

            double deltaRelX = maxScreenOffsetX > 1.0 ? deltaX / maxScreenOffsetX : 0.0;
            double deltaRelY = maxScreenOffsetY > 1.0 ? deltaY / maxScreenOffsetY : 0.0;

            double targetRelX = Math.Clamp(_dragStartRelX + deltaRelX, -1.0, 1.0);
            double targetRelY = Math.Clamp(_dragStartRelY + deltaRelY, -1.0, 1.0);

            // Magnetic snap to center within 3.5% threshold
            if (Math.Abs(targetRelX) < 0.035)
                targetRelX = 0.0;
            if (Math.Abs(targetRelY) < 0.035)
                targetRelY = 0.0;

            vm.SetRelativeOffset(targetRelX, targetRelY);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        UpdateHoverState(pos);
    }

    private void UpdateHoverState(Point pos)
    {
        var hit = _lastImageRect.Inflate(8).Contains(pos);
        if (hit != _isPointerOverImage)
        {
            _isPointerOverImage = hit;
            Cursor = hit ? SizeAllCursor : ArrowCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DataContext is FramingOverlayViewModel vm && vm.IsEditing)
        {
            var pos = e.GetPosition(this);
            var cp = e.GetCurrentPoint(this);

            if (cp.Properties.IsLeftButtonPressed && _lastImageRect.Inflate(8).Contains(pos))
            {
                _isDragging = true;
                _dragStartPointer = pos;
                _dragStartRelX = vm.RelativeOffsetX;
                _dragStartRelY = vm.RelativeOffsetY;
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);

        if (DataContext is FramingOverlayViewModel vm && vm.IsEditing)
        {
            var pos = e.GetPosition(this);
            if (_lastImageRect.Inflate(8).Contains(pos))
            {
                vm.ResetPosition();
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }
}
