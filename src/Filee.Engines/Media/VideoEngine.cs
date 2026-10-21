// High-level video processing engine wrapping FFmpeg: trimming, cropping, speed adjustments, concatenation, frame extraction, and splitting.

using System.Diagnostics;
using System.Globalization;
using Filee.Engines.Infrastructure;
using SkiaSharp;

namespace Filee.Engines.Media;

public sealed record VideoMetadata(
    double DurationSeconds,
    int Width,
    int Height,
    double FrameRate,
    string FormatName,
    bool HasAudio);

public static class VideoEngine
{
    private static (string Ffmpeg, string Ffprobe) EnsureTools()
    {
        return FfmpegConverter.Locate()
            ?? throw new InvalidOperationException("FFmpeg tools were not found on this system.");
    }

    /// <summary>
    /// Probes video file metadata including duration and dimensions.
    /// </summary>
    public static async Task<VideoMetadata> ProbeVideoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, ffprobe) = EnsureTools();
        var info = await MediaInfo.ProbeAsync(ffprobe, videoPath, cancellationToken);

        var duration = info.Duration?.TotalSeconds ?? 0.0;
        var width = info.Video?.Width ?? 1920;
        var height = info.Video?.Height ?? 1080;
        var fps = info.Video?.FrameRate ?? 30.0;
        var formatName = Path.GetExtension(videoPath).TrimStart('.').ToUpperInvariant();
        var hasAudio = info.Audio is not null;

        return new VideoMetadata(duration, width, height, fps, formatName, hasAudio);
    }

    /// <summary>
    /// Extracts a single frame at the specified timecode as an SKBitmap.
    /// </summary>
    public static async Task<SKBitmap?> ExtractFrameAsync(string videoPath, double timeSeconds, int targetHeight = 0, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        var tempPng = Path.Combine(Path.GetTempPath(), $"jhatpat_frame_{Guid.NewGuid():N}.png");

        try
        {
            var timeStr = timeSeconds.ToString("0.000", CultureInfo.InvariantCulture);
            var args = new List<string>
            {
                "-ss", timeStr,
                "-i", videoPath,
                "-vframes", "1",
                "-an"
            };

            if (targetHeight > 0)
            {
                args.AddRange(["-vf", $"scale=-2:{targetHeight}"]);
            }

            args.AddRange(["-y", tempPng]);

            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            await proc.WaitForExitAsync(cancellationToken);

            if (File.Exists(tempPng))
            {
                using var stream = File.OpenRead(tempPng);
                return SKBitmap.Decode(stream);
            }
        }
        catch
        {
            // Ignore preview failure
        }
        finally
        {
            try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
        }

        return null;
    }

    /// <summary>
    /// Extracts a filmstrip of evenly spaced frame thumbnails across the video duration.
    /// </summary>
    public static async Task<List<SKBitmap>> ExtractFilmstripAsync(string videoPath, int count, int thumbHeight = 90, CancellationToken cancellationToken = default)
    {
        var thumbnails = new List<SKBitmap>();
        var meta = await ProbeVideoAsync(videoPath, cancellationToken);
        if (meta.DurationSeconds <= 0 || count <= 0)
            return thumbnails;

        var step = meta.DurationSeconds / (count + 1);
        for (var i = 1; i <= count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var time = i * step;
            var frame = await ExtractFrameAsync(videoPath, time, thumbHeight, cancellationToken);
            if (frame is not null)
            {
                thumbnails.Add(frame);
            }
        }

        return thumbnails;
    }

    /// <summary>
    /// Trims a video between startSeconds and endSeconds.
    /// </summary>
    public static async Task TrimVideoAsync(
        string inputPath, double startSeconds, double endSeconds, string outputPath,
        bool accurateReencode = true, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
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

        if (accurateReencode)
        {
            args.AddRange(["-c:v", "libx264", "-preset", "fast", "-crf", "20", "-c:a", "aac", "-b:a", "192k"]);
        }
        else
        {
            args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]);
        }

        args.AddRange(["-y", outputPath]);

        var result = await FfmpegRunner.RunAsync(ffmpeg, args, pos =>
        {
            if (duration > 0)
                progress?.Report(Math.Clamp(pos / duration, 0.0, 1.0));
        }, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed to trim video: {FfmpegRunner.Summarize(result.StandardError)}");
    }

    /// <summary>
    /// Crops a video to the specified rectangle.
    /// </summary>
    public static async Task CropVideoAsync(
        string inputPath, int x, int y, int width, int height, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        var meta = await ProbeVideoAsync(inputPath, cancellationToken);

        // Ensure even dimensions for H.264
        var w = (width / 2) * 2;
        var h = (height / 2) * 2;
        var cropFilter = $"crop={w}:{h}:{x}:{y}";

        var args = new List<string>
        {
            "-i", inputPath,
            "-vf", cropFilter,
            "-c:v", "libx264",
            "-preset", "fast",
            "-crf", "20",
            "-c:a", "copy",
            "-y", outputPath
        };

        var result = await FfmpegRunner.RunAsync(ffmpeg, args, pos =>
        {
            if (meta.DurationSeconds > 0)
                progress?.Report(Math.Clamp(pos / meta.DurationSeconds, 0.0, 1.0));
        }, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed to crop video: {FfmpegRunner.Summarize(result.StandardError)}");
    }

    /// <summary>
    /// Changes the speed of a video (0.25x to 4x).
    /// </summary>
    public static async Task ChangeSpeedAsync(
        string inputPath, double speed, bool muteAudio, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        var meta = await ProbeVideoAsync(inputPath, cancellationToken);

        var clampedSpeed = Math.Clamp(speed, 0.25, 4.0);
        var ptsFactor = (1.0 / clampedSpeed).ToString("0.0000", CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-i", inputPath
        };

        if (muteAudio || !meta.HasAudio)
        {
            args.AddRange(["-vf", $"setpts={ptsFactor}*PTS", "-an"]);
        }
        else
        {
            // Audio atempo filter accepts between 0.5 and 2.0; chain if speed is outside that
            string atempoFilter;
            if (clampedSpeed < 0.5)
                atempoFilter = $"atempo=0.5,atempo={clampedSpeed / 0.5:0.0000}";
            else if (clampedSpeed > 2.0)
                atempoFilter = $"atempo=2.0,atempo={clampedSpeed / 2.0:0.0000}";
            else
                atempoFilter = $"atempo={clampedSpeed:0.0000}";

            var filterComplex = $"[0:v]setpts={ptsFactor}*PTS[v];[0:a]{atempoFilter}[a]";
            args.AddRange(["-filter_complex", filterComplex, "-map", "[v]", "-map", "[a]"]);
        }

        args.AddRange(["-c:v", "libx264", "-preset", "fast", "-crf", "20", "-c:a", "aac", "-y", outputPath]);

        var targetDuration = meta.DurationSeconds / clampedSpeed;
        var result = await FfmpegRunner.RunAsync(ffmpeg, args, pos =>
        {
            if (targetDuration > 0)
                progress?.Report(Math.Clamp(pos / targetDuration, 0.0, 1.0));
        }, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed to change video speed: {FfmpegRunner.Summarize(result.StandardError)}");
    }

    /// <summary>
    /// Joins multiple video files together in sequence.
    /// </summary>
    public static async Task JoinVideosAsync(
        IReadOnlyList<string> inputFiles, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        if (inputFiles.Count < 2)
            throw new ArgumentException("At least 2 video files are required to join.", nameof(inputFiles));

        var concatListFile = Path.Combine(Path.GetTempPath(), $"concat_{Guid.NewGuid():N}.txt");

        try
        {
            var lines = inputFiles.Select(f => $"file '{f.Replace("'", "'\\''")}'");
            await File.WriteAllLinesAsync(concatListFile, lines, cancellationToken);

            // Try fast stream copy concat first
            var args = new List<string>
            {
                "-f", "concat",
                "-safe", "0",
                "-i", concatListFile,
                "-c", "copy",
                "-y", outputPath
            };

            var result = await FfmpegRunner.RunAsync(ffmpeg, args, null, cancellationToken);
            if (result.ExitCode != 0)
            {
                // If stream copy fails due to codec mismatch, transcode concat
                var filterInputs = string.Concat(Enumerable.Range(0, inputFiles.Count).Select(i => $"[{i}:v:0][{i}:a:0]"));
                var filterComplex = $"{filterInputs}concat=n={inputFiles.Count}:v=1:a=1[outv][outa]";

                var transcodeArgs = new List<string>();
                foreach (var file in inputFiles)
                {
                    transcodeArgs.AddRange(["-i", file]);
                }
                transcodeArgs.AddRange([
                    "-filter_complex", filterComplex,
                    "-map", "[outv]",
                    "-map", "[outa]",
                    "-c:v", "libx264",
                    "-preset", "fast",
                    "-crf", "20",
                    "-c:a", "aac",
                    "-y", outputPath
                ]);

                var tResult = await FfmpegRunner.RunAsync(ffmpeg, transcodeArgs, null, cancellationToken);
                if (tResult.ExitCode != 0)
                    throw new InvalidOperationException($"FFmpeg failed to join videos: {FfmpegRunner.Summarize(tResult.StandardError)}");
            }
        }
        finally
        {
            try { if (File.Exists(concatListFile)) File.Delete(concatListFile); } catch { }
        }
    }

    /// <summary>
    /// Splits a video into multiple segments by duration (e.g. 30 seconds for stories/shorts).
    /// </summary>
    public static async Task SplitVideoAsync(
        string inputPath, double segmentSeconds, string outputFolder,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var (ffmpeg, _) = EnsureTools();
        var meta = await ProbeVideoAsync(inputPath, cancellationToken);

        Directory.CreateDirectory(outputFolder);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);
        var pattern = Path.Combine(outputFolder, $"{baseName}_part%03d{ext}");

        var segStr = segmentSeconds.ToString("0.000", CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-i", inputPath,
            "-c", "copy",
            "-map", "0",
            "-segment_time", segStr,
            "-f", "segment",
            "-reset_timestamps", "1",
            "-y", pattern
        };

        var result = await FfmpegRunner.RunAsync(ffmpeg, args, null, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed to split video: {FfmpegRunner.Summarize(result.StandardError)}");
    }
}
