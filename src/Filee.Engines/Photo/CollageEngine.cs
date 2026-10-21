// Photo collage rendering engine: Grid, Row, Column, and Featured layouts with rounded corners and spacing.

using SkiaSharp;

namespace Filee.Engines.Photo;

public enum CollageLayout
{
    Grid,
    Row,
    Column,
    Featured
}

public enum CollageFitMode
{
    Fill,
    Fit
}

public sealed record CollageOptions
{
    public CollageLayout Layout { get; init; } = CollageLayout.Grid;
    public double AspectRatio { get; init; } = 1.0; // W / H
    public int CanvasWidth { get; init; } = 2048;
    public int Spacing { get; init; } = 12;
    public int Padding { get; init; } = 16;
    public double CornerRadius { get; init; } = 8;
    public SKColor BackgroundColor { get; init; } = SKColors.White;
    public CollageFitMode FitMode { get; init; } = CollageFitMode.Fill;
}

public static class CollageEngine
{
    /// <summary>
    /// Computes target cell rectangles for a given layout and canvas dimensions.
    /// </summary>
    public static List<SKRect> ComputeCellRects(int imageCount, int canvasW, int canvasH, CollageOptions options)
    {
        var rects = new List<SKRect>();
        if (imageCount <= 0 || canvasW <= 0 || canvasH <= 0)
            return rects;

        var pad = options.Padding;
        var space = options.Spacing;
        var innerW = Math.Max(10, canvasW - pad * 2);
        var innerH = Math.Max(10, canvasH - pad * 2);

        switch (options.Layout)
        {
            case CollageLayout.Row:
                {
                    var totalGaps = space * (imageCount - 1);
                    var cellW = Math.Max(1.0f, (innerW - totalGaps) / (float)imageCount);
                    for (var i = 0; i < imageCount; i++)
                    {
                        var x = pad + i * (cellW + space);
                        rects.Add(new SKRect(x, pad, x + cellW, pad + innerH));
                    }
                    break;
                }

            case CollageLayout.Column:
                {
                    var totalGaps = space * (imageCount - 1);
                    var cellH = Math.Max(1.0f, (innerH - totalGaps) / (float)imageCount);
                    for (var i = 0; i < imageCount; i++)
                    {
                        var y = pad + i * (cellH + space);
                        rects.Add(new SKRect(pad, y, pad + innerW, y + cellH));
                    }
                    break;
                }

            case CollageLayout.Featured when imageCount > 1:
                {
                    // Left 55% is hero image, Right 45% is remaining images
                    var heroW = (innerW - space) * 0.55f;
                    var heroRect = new SKRect(pad, pad, pad + heroW, pad + innerH);
                    rects.Add(heroRect);

                    var remaining = imageCount - 1;
                    var rightX = pad + heroW + space;
                    var rightW = innerW - heroW - space;

                    var cols = remaining switch
                    {
                        1 => 1,
                        2 or 3 or 4 => 1,
                        _ => 2
                    };
                    var rows = (int)Math.Ceiling(remaining / (double)cols);
                    var cellW = (rightW - space * (cols - 1)) / cols;
                    var cellH = (innerH - space * (rows - 1)) / rows;

                    for (var i = 0; i < remaining; i++)
                    {
                        var r = i / cols;
                        var c = i % cols;
                        var x = rightX + c * (cellW + space);
                        var y = pad + r * (cellH + space);
                        rects.Add(new SKRect(x, y, x + cellW, y + cellH));
                    }
                    break;
                }

            case CollageLayout.Grid:
            default:
                {
                    if (imageCount == 1)
                    {
                        rects.Add(new SKRect(pad, pad, pad + innerW, pad + innerH));
                        break;
                    }

                    // Determine best column / row count
                    var cols = imageCount switch
                    {
                        2 => canvasW >= canvasH ? 2 : 1,
                        3 => canvasW >= canvasH ? 3 : 1,
                        4 => 2,
                        5 or 6 => 3,
                        7 or 8 => 4,
                        9 => 3,
                        _ => (int)Math.Ceiling(Math.Sqrt(imageCount * ((double)canvasW / canvasH)))
                    };
                    cols = Math.Clamp(cols, 1, imageCount);
                    var rows = (int)Math.Ceiling(imageCount / (double)cols);

                    var cellW = (innerW - space * (cols - 1)) / (float)cols;
                    var cellH = (innerH - space * (rows - 1)) / (float)rows;

                    for (var i = 0; i < imageCount; i++)
                    {
                        var r = i / cols;
                        var c = i % cols;

                        // If last row has only 1 item and columns > 1, center it
                        var itemsInThisRow = (r == rows - 1) ? (imageCount - r * cols) : cols;
                        var rowOffsetX = (cols - itemsInThisRow) * (cellW + space) / 2f;

                        var x = pad + rowOffsetX + c * (cellW + space);
                        var y = pad + r * (cellH + space);
                        rects.Add(new SKRect(x, y, x + cellW, y + cellH));
                    }
                    break;
                }
        }

        return rects;
    }

    /// <summary>
    /// Renders the collage from a list of SKBitmaps into a new SKBitmap canvas.
    /// </summary>
    public static SKBitmap RenderCollage(IReadOnlyList<SKBitmap> bitmaps, CollageOptions options)
    {
        ArgumentNullException.ThrowIfNull(bitmaps);
        ArgumentNullException.ThrowIfNull(options);

        var canvasW = Math.Max(100, options.CanvasWidth);
        var aspect = options.AspectRatio > 0 ? options.AspectRatio : 1.0;
        var canvasH = Math.Max(100, (int)Math.Round(canvasW / aspect));

        var output = new SKBitmap(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(options.BackgroundColor);

        if (bitmaps.Count == 0)
            return output;

        var cellRects = ComputeCellRects(bitmaps.Count, canvasW, canvasH, options);
        var radius = (float)options.CornerRadius;
        using var paint = new SKPaint { IsAntialias = true };

        for (var i = 0; i < bitmaps.Count && i < cellRects.Count; i++)
        {
            var bmp = bitmaps[i];
            var rect = cellRects[i];

            canvas.Save();
            if (radius > 0)
            {
                using var roundRect = new SKRoundRect(rect, radius, radius);
                canvas.ClipRoundRect(roundRect, antialias: true);
            }
            else
            {
                canvas.ClipRect(rect, antialias: true);
            }

            // Draw image inside rect based on FitMode
            if (options.FitMode == CollageFitMode.Fill)
            {
                var scale = Math.Max(rect.Width / bmp.Width, rect.Height / bmp.Height);
                var drawW = bmp.Width * scale;
                var drawH = bmp.Height * scale;
                var drawX = rect.MidX - drawW / 2f;
                var drawY = rect.MidY - drawH / 2f;
                var destRect = new SKRect(drawX, drawY, drawX + drawW, drawY + drawH);
                canvas.DrawBitmap(bmp, destRect);
            }
            else
            {
                var scale = Math.Min(rect.Width / bmp.Width, rect.Height / bmp.Height);
                var drawW = bmp.Width * scale;
                var drawH = bmp.Height * scale;
                var drawX = rect.MidX - drawW / 2f;
                var drawY = rect.MidY - drawH / 2f;
                var destRect = new SKRect(drawX, drawY, drawX + drawW, drawY + drawH);
                canvas.DrawBitmap(bmp, destRect);
            }

            canvas.Restore();
        }

        return output;
    }
}
