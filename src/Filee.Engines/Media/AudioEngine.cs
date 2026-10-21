// Audio processing engine wrapping FFmpeg: volume normalization (EBU R128 and peak) and audio trimming.

using System.Globalization;
using Filee.Engines.Infrastructure;

namespace Filee.Engines.Media;

public sealed record AudioMetadata(
    double DurationSeconds,
    string FormatName,
    int SampleRate,
    int Channels,
    string? Codec);

public static class AudioEngine
{
    private static (string Ffmpeg, string Ffprobe) EnsureTools()
    {
        return FfmpegConverter.Locate()
            ?? throw new InvalidOperationException("FFmpeg tools were not found on this system.");
    }

    /// <summary>
    /// Probes audio metadata: duration, sample rate, channels, codec.
    /// </summary>
    public static async Task<AudioMetadata> ProbeAudioAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, ffprobe) = EnsureTools();
        var info = await MediaInfo.ProbeAsync(ffprobe, audioPath, cancellationToken);

        var duration = info.Duration?.TotalSeconds ?? 0.0;
        var formatName = Path.GetExtension(audioPath).TrimStart('.').ToUpperInvariant();
        var sampleRate = info.Audio?.SampleRate ?? 44100;
        var channels = info.Audio?.Channels ?? 2;
        var codec = info.Audio?.Codec;

        return new AudioMetadata(duration, formatName, sampleRate, channels, codec);
    }

    /// <summary>
    /// Normalizes audio loudness to the target LUFS (EBU R128 standard).
    /// </summary>
    public static async Task NormalizeAudioAsync(
        string inputPath, double targetLufs, double truePeak, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        var meta = await ProbeAudioAsync(inputPath, cancellationToken);

        var lufsStr = targetLufs.ToString("0.0", CultureInfo.InvariantCulture);
        var tpStr = truePeak.ToString("0.0", CultureInfo.InvariantCulture);
        var filter = $"loudnorm=I={lufsStr}:TP={tpStr}:LRA=11";

        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        var codecArgs = ext switch
        {
            ".mp3" => new[] { "-c:a", "libmp3lame", "-q:a", "2" },
            ".flac" => new[] { "-c:a", "flac" },
            ".wav" => new[] { "-c:a", "pcm_s16le" },
            ".ogg" => new[] { "-c:a", "libvorbis", "-q:a", "6" },
            ".m4a" or ".aac" => new[] { "-c:a", "aac", "-b:a", "256k" },
            _ => new[] { "-c:a", "aac", "-b:a", "256k" }
        };

        var args = new List<string>
        {
            "-i", inputPath,
            "-af", filter
        };
        args.AddRange(codecArgs);
        args.AddRange(["-y", outputPath]);

        var result = await FfmpegRunner.RunAsync(ffmpeg, args, pos =>
        {
            if (meta.DurationSeconds > 0)
                progress?.Report(Math.Clamp(pos / meta.DurationSeconds, 0.0, 1.0));
        }, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed to normalize audio: {FfmpegRunner.Summarize(result.StandardError)}");
    }

    /// <summary>
    /// Trims an audio file between startSeconds and endSeconds.
    /// </summary>
    public static async Task TrimAudioAsync(
        string inputPath, double startSeconds, double endSeconds, string outputPath,
        bool reencode = true, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        var startStr = Math.Max(0, startSeconds).ToString("0.000", CultureInfo.InvariantCulture);
        var duration = Math.Max(0.1, endSeconds - startSeconds);
        var durationStr = duration.ToString("0.000", CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-ss", startStr,
            "-i", inputPath,
            "-t", durationStr
        };

        if (reencode)
        {
            var ext = Path.GetExtension(outputPath).ToLowerInvariant();
            var codecArgs = ext switch
            {
                ".mp3" => new[] { "-c:a", "libmp3lame", "-q:a", "2" },
                ".flac" => new[] { "-c:a", "flac" },
                ".wav" => new[] { "-c:a", "pcm_s16le" },
                ".ogg" => new[] { "-c:a", "libvorbis", "-q:a", "6" },
                _ => new[] { "-c:a", "aac", "-b:a", "256k" }
            };
            args.AddRange(codecArgs);
        }
        else
        {
            args.AddRange(["-c", "copy"]);
        }

        args.AddRange(["-y", outputPath]);

        var result = await FfmpegRunner.RunAsync(ffmpeg, args, pos =>
        {
            if (duration > 0)
                progress?.Report(Math.Clamp(pos / duration, 0.0, 1.0));
        }, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed to trim audio: {FfmpegRunner.Summarize(result.StandardError)}");
    }
}
