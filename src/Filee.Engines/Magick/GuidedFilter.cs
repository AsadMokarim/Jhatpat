// Fast Guided Filter for edge-preserving alpha matte refinement.
// Based on "Fast Guided Filter" (Kaiming He & Jian Sun, 2015).

namespace Filee.Engines.Magick;

/// <summary>
/// Fast Guided Filter for edge-preserving smoothing and alpha matte refinement.
/// Transferred luminance edges guide the boundary alpha without halo artifacts.
/// </summary>
public static class GuidedFilter
{
    /// <summary>
    /// Refines probability mask <paramref name="p"/> using luminance of <paramref name="guidance"/>.
    /// </summary>
    /// <param name="guidance">Grayscale guidance image in range [0, 1], size width * height.</param>
    /// <param name="p">Input probability mask in range [0, 1], size width * height.</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="radius">Filter window radius (default: 16).</param>
    /// <param name="eps">Regularization epsilon (default: 1e-4).</param>
    /// <param name="scale">Subsampling scale factor for speed and edge preservation (default: 4).</param>
    /// <returns>Refined alpha mask in range [0, 1].</returns>
    public static float[] Filter(
        float[] guidance,
        float[] p,
        int width,
        int height,
        int radius = 16,
        float eps = 1e-4f,
        int scale = 4)
    {
        if (width <= 0 || height <= 0 || guidance.Length < width * height || p.Length < width * height)
            return p;

        int subW = Math.Max(1, width / scale);
        int subH = Math.Max(1, height / scale);
        int subRadius = Math.Max(1, radius / scale);

        float[] subI = ResizeBilinear(guidance, width, height, subW, subH);
        float[] subP = ResizeBilinear(p, width, height, subW, subH);

        int subLength = subW * subH;
        float[] meanI = BoxBlur(subI, subW, subH, subRadius);
        float[] meanP = BoxBlur(subP, subW, subH, subRadius);

        float[] subIp = new float[subLength];
        float[] subIi = new float[subLength];
        for (int i = 0; i < subLength; i++)
        {
            subIp[i] = subI[i] * subP[i];
            subIi[i] = subI[i] * subI[i];
        }

        float[] meanIp = BoxBlur(subIp, subW, subH, subRadius);
        float[] meanIi = BoxBlur(subIi, subW, subH, subRadius);

        float[] a = new float[subLength];
        float[] b = new float[subLength];
        for (int i = 0; i < subLength; i++)
        {
            float covIp = meanIp[i] - meanI[i] * meanP[i];
            float varI = meanIi[i] - meanI[i] * meanI[i];
            float aVal = covIp / (varI + eps);
            float bVal = meanP[i] - aVal * meanI[i];
            a[i] = aVal;
            b[i] = bVal;
        }

        float[] meanA = BoxBlur(a, subW, subH, subRadius);
        float[] meanB = BoxBlur(b, subW, subH, subRadius);

        float[] meanAUp = ResizeBilinear(meanA, subW, subH, width, height);
        float[] meanBUp = ResizeBilinear(meanB, subW, subH, width, height);

        int fullLength = width * height;
        float[] q = new float[fullLength];
        for (int i = 0; i < fullLength; i++)
        {
            float val = meanAUp[i] * guidance[i] + meanBUp[i];
            q[i] = Math.Clamp(val, 0f, 1f);
        }

        return q;
    }

    /// <summary>
    /// Separable 2D box blur with O(1) running sum per pixel.
    /// </summary>
    public static float[] BoxBlur(float[] src, int width, int height, int radius)
    {
        int length = width * height;
        float[] horiz = new float[length];
        float[] result = new float[length];
        int windowSize = radius * 2 + 1;

        // Horizontal pass
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            float sum = 0;
            for (int x = -radius; x <= radius; x++)
            {
                int clamped = Math.Clamp(x, 0, width - 1);
                sum += src[rowOffset + clamped];
            }
            horiz[rowOffset] = sum / windowSize;

            for (int x = 1; x < width; x++)
            {
                int addX = Math.Clamp(x + radius, 0, width - 1);
                int subX = Math.Clamp(x - radius - 1, 0, width - 1);
                sum += src[rowOffset + addX] - src[rowOffset + subX];
                horiz[rowOffset + x] = sum / windowSize;
            }
        }

        // Vertical pass
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int y = -radius; y <= radius; y++)
            {
                int clamped = Math.Clamp(y, 0, height - 1);
                sum += horiz[clamped * width + x];
            }
            result[x] = sum / windowSize;

            for (int y = 1; y < height; y++)
            {
                int addY = Math.Clamp(y + radius, 0, height - 1);
                int subY = Math.Clamp(y - radius - 1, 0, height - 1);
                sum += horiz[addY * width + x] - horiz[subY * width + x];
                result[y * width + x] = sum / windowSize;
            }
        }

        return result;
    }

    /// <summary>
    /// Bilinear resize for single-channel float array.
    /// </summary>
    public static float[] ResizeBilinear(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        if (srcW == dstW && srcH == dstH)
            return (float[])src.Clone();

        float[] dst = new float[dstW * dstH];
        float xRatio = (float)srcW / dstW;
        float yRatio = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            float sy = (dy + 0.5f) * yRatio - 0.5f;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, srcH - 1);
            int y1 = Math.Min(y0 + 1, srcH - 1);
            float yLerp = sy - y0;

            int dstRow = dy * dstW;
            int srcRow0 = y0 * srcW;
            int srcRow1 = y1 * srcW;

            for (int dx = 0; dx < dstW; dx++)
            {
                float sx = (dx + 0.5f) * xRatio - 0.5f;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, srcW - 1);
                int x1 = Math.Min(x0 + 1, srcW - 1);
                float xLerp = sx - x0;

                float top = src[srcRow0 + x0] * (1 - xLerp) + src[srcRow0 + x1] * xLerp;
                float bottom = src[srcRow1 + x0] * (1 - xLerp) + src[srcRow1 + x1] * xLerp;
                dst[dstRow + dx] = top * (1 - yLerp) + bottom * yLerp;
            }
        }

        return dst;
    }
}
