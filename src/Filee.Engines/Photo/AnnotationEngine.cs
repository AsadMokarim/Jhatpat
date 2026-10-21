// Vector annotation and redaction engine for images.

using SkiaSharp;

namespace Filee.Engines.Photo;

public enum AnnotationTool
{
    Select,
    Pen,
    Arrow,
    Rectangle,
    Ellipse,
    Line,
    Text,
    Highlight,
    Redact,
    Badge
}

public abstract record AnnotationItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public abstract AnnotationTool Tool { get; }
    public SKColor Color { get; init; } = SKColors.Red;
    public float StrokeWidth { get; init; } = 4f;

    public abstract void Render(SKCanvas canvas, SKBitmap source, float scale);
}

public sealed record PenAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Pen;
    public List<SKPoint> Points { get; init; } = [];

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        if (Points.Count < 2) return;
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth * scale,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };

        using var path = new SKPath();
        path.MoveTo(Points[0].X * scale, Points[0].Y * scale);
        for (var i = 1; i < Points.Count; i++)
        {
            path.LineTo(Points[i].X * scale, Points[i].Y * scale);
        }
        canvas.DrawPath(path, paint);
    }
}

public sealed record HighlightAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Highlight;
    public List<SKPoint> Points { get; init; } = [];

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        if (Points.Count < 2) return;
        var hlColor = new SKColor(Color.Red, Color.Green, Color.Blue, 110);
        using var paint = new SKPaint
        {
            Color = hlColor,
            StrokeWidth = (StrokeWidth * 3f) * scale,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            BlendMode = SKBlendMode.SrcOver
        };

        using var path = new SKPath();
        path.MoveTo(Points[0].X * scale, Points[0].Y * scale);
        for (var i = 1; i < Points.Count; i++)
        {
            path.LineTo(Points[i].X * scale, Points[i].Y * scale);
        }
        canvas.DrawPath(path, paint);
    }
}

public sealed record ArrowAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Arrow;
    public SKPoint Start { get; init; }
    public SKPoint End { get; init; }

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        var s = new SKPoint(Start.X * scale, Start.Y * scale);
        var e = new SKPoint(End.X * scale, End.Y * scale);

        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth * scale,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        canvas.DrawLine(s, e, paint);

        // Arrow head
        var angle = Math.Atan2(e.Y - s.Y, e.X - s.X);
        var headLen = Math.Max(14f, StrokeWidth * 3.5f) * scale;
        var p1 = new SKPoint((float)(e.X - headLen * Math.Cos(angle - Math.PI / 6)), (float)(e.Y - headLen * Math.Sin(angle - Math.PI / 6)));
        var p2 = new SKPoint((float)(e.X - headLen * Math.Cos(angle + Math.PI / 6)), (float)(e.Y - headLen * Math.Sin(angle + Math.PI / 6)));

        using var headPaint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var headPath = new SKPath();
        headPath.MoveTo(e);
        headPath.LineTo(p1);
        headPath.LineTo(p2);
        headPath.Close();
        canvas.DrawPath(headPath, headPaint);
    }
}

public sealed record RectAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Rectangle;
    public SKRect Rect { get; init; }

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        var scaledRect = new SKRect(Rect.Left * scale, Rect.Top * scale, Rect.Right * scale, Rect.Bottom * scale);
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth * scale,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawRoundRect(scaledRect, 4 * scale, 4 * scale, paint);
    }
}

public sealed record EllipseAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Ellipse;
    public SKRect Rect { get; init; }

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        var scaledRect = new SKRect(Rect.Left * scale, Rect.Top * scale, Rect.Right * scale, Rect.Bottom * scale);
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth * scale,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawOval(scaledRect, paint);
    }
}

public sealed record LineAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Line;
    public SKPoint Start { get; init; }
    public SKPoint End { get; init; }

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        var s = new SKPoint(Start.X * scale, Start.Y * scale);
        var e = new SKPoint(End.X * scale, End.Y * scale);

        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = StrokeWidth * scale,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        canvas.DrawLine(s, e, paint);
    }
}

public sealed record TextAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Text;
    public SKPoint Position { get; init; }
    public string Text { get; init; } = "";
    public float FontSize { get; init; } = 22f;

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        if (string.IsNullOrEmpty(Text)) return;
        using var paint = new SKPaint
        {
            Color = Color,
            TextSize = FontSize * scale,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold)
        };

        // Draw soft dark background plate for readability
        var textBounds = new SKRect();
        paint.MeasureText(Text, ref textBounds);
        var pad = 6f * scale;
        var bgRect = new SKRect(
            Position.X * scale + textBounds.Left - pad,
            Position.Y * scale + textBounds.Top - pad,
            Position.X * scale + textBounds.Right + pad,
            Position.Y * scale + textBounds.Bottom + pad);

        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 160),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(bgRect, 4 * scale, 4 * scale, bgPaint);
        canvas.DrawText(Text, Position.X * scale, Position.Y * scale, paint);
    }
}

public sealed record RedactAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Redact;
    public SKRect Rect { get; init; }

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        var r = new SKRect(
            Math.Min(Rect.Left, Rect.Right) * scale,
            Math.Min(Rect.Top, Rect.Bottom) * scale,
            Math.Max(Rect.Left, Rect.Right) * scale,
            Math.Max(Rect.Top, Rect.Bottom) * scale);

        if (r.Width <= 1 || r.Height <= 1) return;

        // Pixelate the region
        const int pixelBlock = 12;
        var roundW = Math.Max(1, (int)r.Width / pixelBlock);
        var roundH = Math.Max(1, (int)r.Height / pixelBlock);

        // Black redaction box with high opacity or solid fill
        using var fillPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(r, fillPaint);
    }
}

public sealed record BadgeAnnotation : AnnotationItem
{
    public override AnnotationTool Tool => AnnotationTool.Badge;
    public SKPoint Center { get; init; }
    public int Number { get; init; } = 1;
    public float Radius { get; init; } = 16f;

    public override void Render(SKCanvas canvas, SKBitmap source, float scale)
    {
        var c = new SKPoint(Center.X * scale, Center.Y * scale);
        var r = Radius * scale;

        // Badge circle fill
        using var circlePaint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(c, r, circlePaint);

        // White border
        using var strokePaint = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = 2f * scale,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawCircle(c, r, strokePaint);

        // Badge number text
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = r * 1.1f,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold)
        };
        var text = Number.ToString();
        var bounds = new SKRect();
        textPaint.MeasureText(text, ref bounds);
        var textY = c.Y - bounds.MidY;
        canvas.DrawText(text, c.X, textY, textPaint);
    }
}

public static class AnnotationEngine
{
    /// <summary>
    /// Renders annotations on top of the source bitmap and returns a new flattened SKBitmap.
    /// </summary>
    public static SKBitmap FlattenAnnotations(SKBitmap source, IReadOnlyList<AnnotationItem> annotations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(annotations);

        var output = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.DrawBitmap(source, 0, 0);

        foreach (var item in annotations)
        {
            item.Render(canvas, source, 1.0f);
        }

        return output;
    }
}
