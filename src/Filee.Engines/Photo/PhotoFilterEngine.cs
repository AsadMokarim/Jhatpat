// High-performance SkiaSharp photo adjustment filter engine (Light, Color, Detail).

using SkiaSharp;

namespace Filee.Engines.Photo;

public sealed record PhotoAdjustments
{
    // Light
    public double Exposure { get; init; } = 0.0;     // -2.0 .. +2.0 EV
    public double Brightness { get; init; } = 0.0;   // -100 .. +100
    public double Contrast { get; init; } = 0.0;     // -100 .. +100
    public double Highlights { get; init; } = 0.0;   // -100 .. +100
    public double Shadows { get; init; } = 0.0;      // -100 .. +100
    public double Whites { get; init; } = 0.0;       // -100 .. +100
    public double Blacks { get; init; } = 0.0;       // -100 .. +100
    public double Gamma { get; init; } = 1.0;        // 0.2 .. 2.5

    // Color
    public double Temperature { get; init; } = 0.0;  // -100 .. +100 (Cool - Warm)
    public double Tint { get; init; } = 0.0;         // -100 .. +100 (Green - Magenta)
    public double Saturation { get; init; } = 0.0;   // -100 .. +100
    public double Vibrance { get; init; } = 0.0;     // -100 .. +100
    public double Hue { get; init; } = 0.0;          // -180 .. +180

    // Detail
    public double Dehaze { get; init; } = 0.0;       // 0 .. 100
    public double Clarity { get; init; } = 0.0;      // -100 .. +100
    public double Grain { get; init; } = 0.0;        // 0 .. 100
    public double NoiseReduction { get; init; } = 0.0; // 0 .. 100

    public bool IsDefault =>
        Math.Abs(Exposure) < 0.01 &&
        Math.Abs(Brightness) < 0.5 &&
        Math.Abs(Contrast) < 0.5 &&
        Math.Abs(Highlights) < 0.5 &&
        Math.Abs(Shadows) < 0.5 &&
        Math.Abs(Whites) < 0.5 &&
        Math.Abs(Blacks) < 0.5 &&
        Math.Abs(Gamma - 1.0) < 0.01 &&
        Math.Abs(Temperature) < 0.5 &&
        Math.Abs(Tint) < 0.5 &&
        Math.Abs(Saturation) < 0.5 &&
        Math.Abs(Vibrance) < 0.5 &&
        Math.Abs(Hue) < 0.5 &&
        Dehaze < 0.5 &&
        Math.Abs(Clarity) < 0.5 &&
        Grain < 0.5 &&
        NoiseReduction < 0.5;
}

public static class PhotoFilterEngine
{
    /// <summary>
    /// Applies adjustments to a source SKBitmap and returns a new SKBitmap.
    /// Uses ultra-fast pointer-based pixel processing.
    /// </summary>
    public static SKBitmap ApplyAdjustments(SKBitmap source, PhotoAdjustments adj)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adj);

        var width = source.Width;
        var height = source.Height;

        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        if (adj.IsDefault)
        {
            using var canvas = new SKCanvas(result);
            canvas.DrawBitmap(source, 0, 0);
            return result;
        }

        // Precompute factors
        var expFactor = Math.Pow(2.0, adj.Exposure);
        var brightnessOffset = adj.Brightness * 2.55;
        var contrastFactor = adj.Contrast >= 0
            ? 1.0 + (adj.Contrast / 100.0) * 1.5
            : 1.0 + (adj.Contrast / 100.0) * 0.75;
        var invGamma = 1.0 / Math.Clamp(adj.Gamma, 0.2, 2.5);

        var tempOffset = adj.Temperature * 0.8; // warm vs cool
        var tintOffset = adj.Tint * 0.6;        // green vs magenta

        var satFactor = adj.Saturation >= 0
            ? 1.0 + (adj.Saturation / 100.0) * 1.5
            : 1.0 + (adj.Saturation / 100.0);
        var vibranceFactor = adj.Vibrance / 100.0;

        var hlFactor = adj.Highlights / 100.0;
        var shFactor = adj.Shadows / 100.0;
        var whitesFactor = adj.Whites / 100.0;
        var blacksFactor = adj.Blacks / 100.0;
        var dehazeFactor = adj.Dehaze / 100.0;
        var clarityFactor = adj.Clarity / 100.0;
        var grainFactor = adj.Grain / 100.0;

        var hueShift = (float)adj.Hue;
        var hasHueShift = Math.Abs(hueShift) > 0.5f;

        // 1D Gamma & Brightness/Contrast/Exposure LUT for base tone curve
        var toneLut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var val = (double)i / 255.0;
            // Exposure
            val *= expFactor;
            // Gamma
            val = Math.Pow(Math.Clamp(val, 0.0, 10.0), invGamma);
            // Contrast (centered around 0.5)
            val = (val - 0.5) * contrastFactor + 0.5;
            // Brightness
            val += brightnessOffset / 255.0;
            toneLut[i] = (byte)Math.Clamp((int)Math.Round(val * 255.0), 0, 255);
        }

        // Extract source pixels in RGBA format
        using var srcConverted = source.ColorType == SKColorType.Rgba8888
            ? source
            : ConvertToRgba8888(source);

        var srcSpan = srcConverted.GetPixelSpan();
        var dstSpan = result.GetPixelSpan();
        var totalPixels = width * height;

        for (var p = 0; p < totalPixels; p++)
        {
            var offset = p * 4;
            var r = (double)toneLut[srcSpan[offset + 0]];
            var g = (double)toneLut[srcSpan[offset + 1]];
            var b = (double)toneLut[srcSpan[offset + 2]];
            var a = srcSpan[offset + 3];

            if (a == 0)
            {
                dstSpan[offset + 0] = 0;
                dstSpan[offset + 1] = 0;
                dstSpan[offset + 2] = 0;
                dstSpan[offset + 3] = 0;
                continue;
            }

            // Luminance (Rec.709)
            var luma = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;

            // Highlights / Shadows adjustments
            if (hlFactor != 0 && luma > 0.5)
            {
                var hlWeight = (luma - 0.5) * 2.0; // 0 at 0.5, 1 at 1.0
                var shift = hlFactor * hlWeight * 40.0;
                r += shift;
                g += shift;
                b += shift;
            }

            if (shFactor != 0 && luma < 0.5)
            {
                var shWeight = (0.5 - luma) * 2.0; // 1 at 0.0, 0 at 0.5
                var shift = shFactor * shWeight * 40.0;
                r += shift;
                g += shift;
                b += shift;
            }

            // Whites / Blacks
            if (whitesFactor != 0 && luma > 0.7)
            {
                var wWeight = (luma - 0.7) / 0.3;
                var shift = whitesFactor * wWeight * 35.0;
                r += shift;
                g += shift;
                b += shift;
            }

            if (blacksFactor != 0 && luma < 0.3)
            {
                var bWeight = (0.3 - luma) / 0.3;
                var shift = blacksFactor * bWeight * 35.0;
                r += shift;
                g += shift;
                b += shift;
            }

            // Dehaze & Clarity
            if (dehazeFactor > 0)
            {
                // Darken shadows slightly and boost saturation
                var dShift = (luma - 0.5) * dehazeFactor * 30.0;
                r += dShift;
                g += dShift;
                b += dShift;
            }

            if (clarityFactor != 0)
            {
                // Midtone contrast
                var midWeight = 1.0 - Math.Abs(luma - 0.5) * 2.0;
                var cShift = (luma - 0.5) * midWeight * clarityFactor * 40.0;
                r += cShift;
                g += cShift;
                b += cShift;
            }

            // Temperature (warm/cool) & Tint (green/magenta)
            if (tempOffset != 0)
            {
                r += tempOffset;
                b -= tempOffset;
            }

            if (tintOffset != 0)
            {
                r += tintOffset * 0.5;
                b += tintOffset * 0.5;
                g -= tintOffset;
            }

            // Clamp before color operations
            r = Math.Clamp(r, 0.0, 255.0);
            g = Math.Clamp(g, 0.0, 255.0);
            b = Math.Clamp(b, 0.0, 255.0);

            // Saturation & Vibrance
            var currentLuma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            var maxChroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            var currentSat = maxChroma / Math.Max(1.0, currentLuma);

            var effectiveSat = satFactor;
            if (vibranceFactor != 0)
            {
                // Affect less-saturated pixels more
                var vibWeight = Math.Clamp(1.0 - currentSat, 0.0, 1.0);
                effectiveSat += vibranceFactor * vibWeight;
            }

            if (Math.Abs(effectiveSat - 1.0) > 0.01)
            {
                r = currentLuma + (r - currentLuma) * effectiveSat;
                g = currentLuma + (g - currentLuma) * effectiveSat;
                b = currentLuma + (b - currentLuma) * effectiveSat;
            }

            // Hue shift
            if (hasHueShift)
            {
                new SKColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255)).ToHsv(out var h, out var s, out var v);
                h = (h + hueShift) % 360f;
                if (h < 0) h += 360f;
                var hsvCol = SKColor.FromHsv(h, s, v);
                r = hsvCol.Red;
                g = hsvCol.Green;
                b = hsvCol.Blue;
            }

            // Film Grain
            if (grainFactor > 0)
            {
                // Fast deterministic hash noise per pixel index
                var hash = (p * 1664525 + 1013904223) & 0x7FFFFFFF;
                var noise = ((hash % 1000) / 500.0 - 1.0) * (grainFactor * 35.0);
                r += noise;
                g += noise;
                b += noise;
            }

            dstSpan[offset + 0] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
            dstSpan[offset + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
            dstSpan[offset + 2] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
            dstSpan[offset + 3] = a;
        }

        if (srcConverted != source)
            srcConverted.Dispose();

        // Noise reduction pass if requested
        if (adj.NoiseReduction > 1.0)
        {
            var blurSigma = (float)(adj.NoiseReduction / 100.0 * 2.5);
            using var blurPaint = new SKPaint
            {
                ImageFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma)
            };
            var blurred = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var bCanvas = new SKCanvas(blurred))
            {
                bCanvas.DrawBitmap(result, 0, 0, blurPaint);
            }
            result.Dispose();
            return blurred;
        }

        return result;
    }

    private static SKBitmap ConvertToRgba8888(SKBitmap src)
    {
        var copy = new SKBitmap(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(copy);
        canvas.DrawBitmap(src, 0, 0);
        return copy;
    }
}
