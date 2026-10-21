// Tests for VideoEngine: probing, trimming, cropping, speed adjustments, and frame extraction.

using System.Diagnostics;
using System.Globalization;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;
using SkiaSharp;

namespace Filee.Engines.Tests;

[Collection(nameof(FfmpegCollection))]
public class VideoEngineTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static readonly (string Ffmpeg, string Ffprobe)? Tools = FfmpegConverter.Locate();

    private static void SkipUnlessInstalled() =>
        Assert.SkipUnless(Tools is not null, "FFmpeg is not installed");

    private static async Task<string> GenerateSyntheticVideoAsync(string folder, string name, int width = 320, int height = 240, double seconds = 2.0)
    {
        var path = Path.Combine(folder, name);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Tools!.Value.Ffmpeg,
            Arguments = $"-hide_banner -nostdin -v error -y -f lavfi -i testsrc=size={width}x{height}:rate=25:duration={seconds.ToString(CultureInfo.InvariantCulture)} -f lavfi -i sine=frequency=440:sample_rate=44100:duration={seconds.ToString(CultureInfo.InvariantCulture)} -pix_fmt yuv420p \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    [Fact]
    public async Task ProbeVideoAsync_detects_duration_and_dimensions()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var videoPath = await GenerateSyntheticVideoAsync(folder, "sample.mp4", 320, 240, 2.0);

        var meta = await VideoEngine.ProbeVideoAsync(videoPath, TestContext.Current.CancellationToken);

        Assert.Equal(320, meta.Width);
        Assert.Equal(240, meta.Height);
        Assert.InRange(meta.DurationSeconds, 1.8, 2.2);
        Assert.True(meta.HasAudio);
    }

    [Fact]
    public async Task ExtractFrameAsync_returns_valid_skbitmap()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var videoPath = await GenerateSyntheticVideoAsync(folder, "sample_frame.mp4", 320, 240, 2.0);

        using var frame = await VideoEngine.ExtractFrameAsync(videoPath, 0.5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(frame);
        Assert.Equal(320, frame.Width);
        Assert.Equal(240, frame.Height);
    }

    [Fact]
    public async Task TrimVideoAsync_produces_clipped_output()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var videoPath = await GenerateSyntheticVideoAsync(folder, "sample_trim.mp4", 320, 240, 3.0);
        var outputPath = Path.Combine(folder, "trimmed.mp4");

        await VideoEngine.TrimVideoAsync(videoPath, 0.5, 1.5, outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        var meta = await VideoEngine.ProbeVideoAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.InRange(meta.DurationSeconds, 0.8, 1.2);
    }

    [Fact]
    public async Task CropVideoAsync_resizes_video_stream()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var videoPath = await GenerateSyntheticVideoAsync(folder, "sample_crop.mp4", 320, 240, 2.0);
        var outputPath = Path.Combine(folder, "cropped.mp4");

        await VideoEngine.CropVideoAsync(videoPath, 10, 10, 160, 120, outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        var meta = await VideoEngine.ProbeVideoAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.Equal(160, meta.Width);
        Assert.Equal(120, meta.Height);
    }

    [Fact]
    public async Task ChangeSpeedAsync_modifies_video_duration()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var videoPath = await GenerateSyntheticVideoAsync(folder, "sample_speed.mp4", 320, 240, 2.0);
        var outputPath = Path.Combine(folder, "fast.mp4");

        await VideoEngine.ChangeSpeedAsync(videoPath, 2.0, muteAudio: true, outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        var meta = await VideoEngine.ProbeVideoAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.InRange(meta.DurationSeconds, 0.8, 1.2);
    }

    [Fact]
    public async Task JoinVideosAsync_concatenates_multiple_files()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var part1 = await GenerateSyntheticVideoAsync(folder, "part1.mp4", 320, 240, 1.0);
        var part2 = await GenerateSyntheticVideoAsync(folder, "part2.mp4", 320, 240, 1.0);
        var outputPath = Path.Combine(folder, "joined.mp4");

        await VideoEngine.JoinVideosAsync([part1, part2], outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        var meta = await VideoEngine.ProbeVideoAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.InRange(meta.DurationSeconds, 1.8, 2.3);
    }
}
