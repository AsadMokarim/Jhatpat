// Tests for AnnotationEngine: vector shapes, highlighter, redaction, badges, and image flattening.

using Filee.Engines.Photo;
using SkiaSharp;

namespace Filee.Engines.Tests;

public class AnnotationEngineTests
{
    private static SKBitmap CreateTestBitmap(int width = 200, int height = 200)
    {
        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.LightGray);
        return bmp;
    }

    [Fact]
    public void FlattenAnnotations_renders_all_shapes_without_errors()
    {
        using var baseBmp = CreateTestBitmap(400, 400);

        var annotations = new List<AnnotationItem>
        {
            new PenAnnotation
            {
                Points = [new SKPoint(10, 10), new SKPoint(50, 60), new SKPoint(90, 20)],
                Color = SKColors.Red,
                StrokeWidth = 3f
            },
            new ArrowAnnotation
            {
                Start = new SKPoint(100, 100),
                End = new SKPoint(200, 200),
                Color = SKColors.Blue,
                StrokeWidth = 4f
            },
            new RectAnnotation
            {
                Rect = new SKRect(20, 200, 100, 280),
                Color = SKColors.Green,
                StrokeWidth = 2f
            },
            new EllipseAnnotation
            {
                Rect = new SKRect(150, 250, 250, 350),
                Color = SKColors.DarkOrange,
                StrokeWidth = 3f
            },
            new LineAnnotation
            {
                Start = new SKPoint(10, 380),
                End = new SKPoint(390, 380),
                Color = SKColors.Purple,
                StrokeWidth = 2f
            },
            new TextAnnotation
            {
                Position = new SKPoint(150, 50),
                Text = "Annotated Text",
                Color = SKColors.Black,
                FontSize = 18f
            },
            new HighlightAnnotation
            {
                Points = [new SKPoint(50, 120), new SKPoint(180, 120)],
                Color = SKColors.Yellow
            },
            new RedactAnnotation
            {
                Rect = new SKRect(250, 50, 350, 120)
            },
            new BadgeAnnotation
            {
                Center = new SKPoint(300, 200),
                Number = 1,
                Color = SKColors.OrangeRed,
                Radius = 14f
            }
        };

        using var flattened = AnnotationEngine.FlattenAnnotations(baseBmp, annotations);

        Assert.NotNull(flattened);
        Assert.Equal(baseBmp.Width, flattened.Width);
        Assert.Equal(baseBmp.Height, flattened.Height);

        // The redacted area (e.g. center pixel of redact rect (300, 85)) should be dark/obscured
        var redactPixel = flattened.GetPixel(300, 85);
        Assert.True(redactPixel.Red < 40 && redactPixel.Green < 40 && redactPixel.Blue < 40);
    }
}
