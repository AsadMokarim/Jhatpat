// Tests for AudioEngine: probing, volume normalization (loudnorm), and trimming.

using System.Diagnostics;
using System.Globalization;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;

namespace Filee.Engines.Tests;

[Collection(nameof(FfmpegCollection))]
public class AudioEngineTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static readonly (string Ffmpeg, string Ffprobe)? Tools = FfmpegConverter.Locate();

    private static void SkipUnlessInstalled() =>
        Assert.SkipUnless(Tools is not null, "FFmpeg is not installed");

    private static async Task<string> GenerateSyntheticToneAsync(string folder, string name, double seconds = 2.0)
    {
        var path = Path.Combine(folder, name);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Tools!.Value.Ffmpeg,
            Arguments = $"-hide_banner -nostdin -v error -y -f lavfi -i sine=frequency=440:sample_rate=44100:duration={seconds.ToString(CultureInfo.InvariantCulture)} -ac 2 \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    [Fact]
    public async Task ProbeAudioAsync_detects_audio_properties()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var audioPath = await GenerateSyntheticToneAsync(folder, "tone.wav", 2.0);

        var meta = await AudioEngine.ProbeAudioAsync(audioPath, TestContext.Current.CancellationToken);

        Assert.InRange(meta.DurationSeconds, 1.8, 2.2);
        Assert.Equal(2, meta.Channels);
        Assert.Equal(44100, meta.SampleRate);
    }

    [Fact]
    public async Task TrimAudioAsync_cuts_audio_range()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var audioPath = await GenerateSyntheticToneAsync(folder, "long_tone.wav", 3.0);
        var outputPath = Path.Combine(folder, "trimmed_tone.wav");

        await AudioEngine.TrimAudioAsync(audioPath, 0.5, 1.5, outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        var meta = await AudioEngine.ProbeAudioAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.InRange(meta.DurationSeconds, 0.8, 1.2);
    }

    [Fact]
    public async Task NormalizeAudioAsync_processes_loudness()
    {
        SkipUnlessInstalled();
        var folder = fx.NewFolder();
        var audioPath = await GenerateSyntheticToneAsync(folder, "unnormalized.wav", 2.0);
        var outputPath = Path.Combine(folder, "normalized.wav");

        await AudioEngine.NormalizeAudioAsync(audioPath, targetLufs: -16.0, truePeak: -1.5, outputPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        var meta = await AudioEngine.ProbeAudioAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.InRange(meta.DurationSeconds, 1.8, 2.2);
    }
}
