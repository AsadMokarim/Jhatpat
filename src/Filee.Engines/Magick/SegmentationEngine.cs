// AI-powered foreground segmentation using ONNX Runtime (Apache-2.0 / MIT models).
// High quality portrait and salient object background removal with edge-aware guided matting.

using System.Collections.Concurrent;
using Filee.Engines.Infrastructure;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Filee.Engines.Magick;

/// <summary>
/// Neural network foreground segmentation engine for portraits and complex scenes.
/// </summary>
public static class SegmentationEngine
{
    private static readonly ConcurrentDictionary<string, InferenceSession> Sessions = new();
    private static readonly object LockObj = new();

    private static readonly float[] ImageNetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] ImageNetStd = [0.229f, 0.224f, 0.225f];

    /// <summary>
    /// Preferred model filenames in order of precision.
    /// </summary>
    private static readonly string[] ModelCandidates =
    [
        "u2net_human_seg.onnx",
        "u2net.onnx",
        "u2netp.onnx"
    ];

    /// <summary>
    /// Checks if a compatible segmentation model is available locally.
    /// </summary>
    public static bool IsModelAvailable => FindModelPath() is not null;

    /// <summary>
    /// Finds the path to the best available ONNX model file.
    /// </summary>
    public static string? FindModelPath()
    {
        // 1. Explicit override via environment variables
        var envPath = Environment.GetEnvironmentVariable("JHATPAT_MODEL_PATH")
                      ?? Environment.GetEnvironmentVariable("FLUXA_MODEL_PATH")
                      ?? Environment.GetEnvironmentVariable("FILEE_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        // Candidate search folders
        var folders = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "models"),
            Path.Combine(AppContext.BaseDirectory, "engines", "models"),
            Path.Combine(EngineEnvironment.DownloadRoot, "models"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jhatpat", "models"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fluxa", "models"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Filee", "models"),
            "/tmp/models"
        };

        // Also search git repository checkout if running from source/build
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Filee.slnx")))
            {
                folders.Add(Path.Combine(dir.FullName, "models"));
                folders.Add(Path.Combine(dir.FullName, "engines", "models"));
                break;
            }
        }

        foreach (var candidate in ModelCandidates)
        {
            foreach (var folder in folders)
            {
                if (Directory.Exists(folder))
                {
                    var filePath = Path.Combine(folder, candidate);
                    if (File.Exists(filePath))
                        return filePath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets or creates a cached InferenceSession for the given model path.
    /// </summary>
    private static InferenceSession GetSession(string modelPath)
    {
        return Sessions.GetOrAdd(modelPath, path =>
        {
            var options = new SessionOptions
            {
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 8),
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };
            return new InferenceSession(path, options);
        });
    }

    /// <summary>
    /// Applies neural foreground segmentation and guided edge refinement to <paramref name="image"/>.
    /// </summary>
    /// <param name="image">Image to process in-place.</param>
    /// <param name="tolerance">User tolerance 0-100 (affects boundary sensitivity).</param>
    /// <returns>True if segmentation succeeded, false if model is unavailable or inference failed.</returns>
    public static bool TryApply(IMagickImage<byte> image, int tolerance = 25)
    {
        var modelPath = FindModelPath();
        if (modelPath is null)
            return false;

        var width = (int)image.Width;
        var height = (int)image.Height;
        if (width < 2 || height < 2)
            return false;

        if (image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB))
            image.ColorSpace = ColorSpace.sRGB;

        byte[]? pixels;
        using (var collection = image.GetPixels())
            pixels = collection.ToByteArray(PixelMapping.RGBA);
        if (pixels is null)
            return false;

        try
        {
            var session = GetSession(modelPath);
            var inputMeta = session.InputMetadata.First();
            var inputDims = inputMeta.Value.Dimensions;
            int netW = inputDims.Length >= 4 && inputDims[3] > 0 ? inputDims[3] : 320;
            int netH = inputDims.Length >= 4 && inputDims[2] > 0 ? inputDims[2] : 320;

            // 1. Prepare normalized input tensor [1, 3, netH, netW]
            var tensor = new DenseTensor<float>(new[] { 1, 3, netH, netW });
            float xRatio = (float)width / netW;
            float yRatio = (float)height / netH;

            for (int dy = 0; dy < netH; dy++)
            {
                int sy = Math.Clamp((int)Math.Round((dy + 0.5f) * yRatio - 0.5f), 0, height - 1);
                int rowOffset = sy * width * 4;

                for (int dx = 0; dx < netW; dx++)
                {
                    int sx = Math.Clamp((int)Math.Round((dx + 0.5f) * xRatio - 0.5f), 0, width - 1);
                    int o = rowOffset + sx * 4;

                    float r = pixels[o] / 255f;
                    float g = pixels[o + 1] / 255f;
                    float b = pixels[o + 2] / 255f;

                    tensor[0, 0, dy, dx] = (r - ImageNetMean[0]) / ImageNetStd[0];
                    tensor[0, 1, dy, dx] = (g - ImageNetMean[1]) / ImageNetStd[1];
                    tensor[0, 2, dy, dx] = (b - ImageNetMean[2]) / ImageNetStd[2];
                }
            }

            // 2. Run inference
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputMeta.Key, tensor) };
            using var results = session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            // 3. Extract and min-max scale coarse prediction [netH, netW]
            float[] coarseSub = new float[netW * netH];
            float minVal = float.MaxValue;
            float maxVal = float.MinValue;

            for (int y = 0; y < netH; y++)
            {
                for (int x = 0; x < netW; x++)
                {
                    float v = outputTensor[0, 0, y, x];
                    if (v < minVal) minVal = v;
                    if (v > maxVal) maxVal = v;
                    coarseSub[y * netW + x] = v;
                }
            }

            float range = Math.Max(maxVal - minVal, 1e-8f);
            for (int i = 0; i < coarseSub.Length; i++)
                coarseSub[i] = (coarseSub[i] - minVal) / range;

            // 4. Bilinear upsample coarse mask to full image dimensions
            float[] coarseMask = GuidedFilter.ResizeBilinear(coarseSub, netW, netH, width, height);

            // 5. Build guidance luminance from source image
            int totalPixels = width * height;
            float[] guidance = new float[totalPixels];
            for (int i = 0; i < totalPixels; i++)
            {
                int o = i * 4;
                // Luminance Y = 0.299 R + 0.587 G + 0.114 B
                guidance[i] = (0.299f * pixels[o] + 0.587f * pixels[o + 1] + 0.114f * pixels[o + 2]) / 255f;
            }

            // 6. Edge-aware guided refinement
            float[] refined = GuidedFilter.Filter(guidance, coarseMask, width, height, radius: 16, eps: 1e-4f, scale: 4);

            // 7. Core and edge thresholding based on tolerance
            // Tolerance 0..100: default 25 shifts boundary slightly; 50 is neutral 0.5.
            float tolShift = (tolerance - 25) / 100f * 0.15f;
            float lowCutoff = Math.Clamp(0.04f - tolShift, 0.01f, 0.20f);
            float highCutoff = Math.Clamp(0.96f - tolShift, 0.80f, 0.99f);

            // 8. Apply refined alpha to pixels, preserving 100% of the original RGB values
            for (int i = 0; i < totalPixels; i++)
            {
                int o = i * 4;
                float p = coarseMask[i];
                float alphaVal;

                if (p <= lowCutoff)
                {
                    alphaVal = 0f;
                }
                else if (p >= highCutoff)
                {
                    alphaVal = 1f;
                }
                else
                {
                    // Boundary transition: guided alpha with smooth contrast
                    float q = refined[i];
                    alphaVal = Math.Clamp((q - lowCutoff) / (highCutoff - lowCutoff), 0f, 1f);
                }

                // Existing alpha channel preservation (if image already had transparency)
                byte existingAlpha = pixels[o + 3];
                pixels[o + 3] = (byte)Math.Round(alphaVal * existingAlpha);
            }

            // 9. Re-import pixels with full alpha channel
            image.Alpha(AlphaOption.Set);
            image.ImportPixels(pixels, new PixelImportSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
