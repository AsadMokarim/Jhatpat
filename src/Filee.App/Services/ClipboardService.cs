// Cross-platform clipboard service for image input and output using Avalonia IClipboard.

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public sealed class ClipboardService : IClipboardService
{
    private readonly Func<IClipboard?>? _clipboardAccessor;
    private readonly string _cacheDirectory;
    private readonly ILogger<ClipboardService>? _log;

    public ClipboardService(
        string? cacheDirectory = null,
        Func<IClipboard?>? clipboardAccessor = null,
        ILogger<ClipboardService>? log = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(UserDataStore.DefaultDirectory, "clipboard_cache");
        _clipboardAccessor = clipboardAccessor;
        _log = log;

        // Clean stale files on startup
        try
        {
            CleanOldTemporaryFiles();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Failed to clean initial temporary clipboard files.");
        }
    }

    private IClipboard? GetClipboard()
    {
        if (_clipboardAccessor is not null)
            return _clipboardAccessor();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.Windows.FirstOrDefault(w => w.IsVisible && w.IsActive)
                ?? desktop.MainWindow
                ?? desktop.Windows.FirstOrDefault(w => w.IsVisible);

            if (window is not null)
                return TopLevel.GetTopLevel(window)?.Clipboard;
        }

        return null;
    }

    public async Task<bool> HasImageAsync()
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard is null)
                return false;

            var formats = await clipboard.GetDataFormatsAsync();
            if (formats is { Count: > 0 })
            {
                // Check if any format indicates an image
                var hasBitmap = formats.Any(f =>
                    f.Identifier.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                    f.Identifier.Contains("bitmap", StringComparison.OrdinalIgnoreCase) ||
                    f.Identifier.Contains("png", StringComparison.OrdinalIgnoreCase) ||
                    f.Identifier.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
                    f.Identifier.Contains("bmp", StringComparison.OrdinalIgnoreCase));

                if (hasBitmap)
                    return true;
            }

            // Check files
            var files = await clipboard.TryGetFilesAsync();
            if (files is { Length: > 0 } && files.Any(f => BatchResizeService.IsSupportedImage(Uri.UnescapeDataString(f.Path.LocalPath))))
                return true;

            // Check URI list
            var uriListData = await clipboard.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("text/uri-list"))
                ?? await clipboard.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files"));
            if (!string.IsNullOrWhiteSpace(uriListData))
            {
                var lines = uriListData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
                    {
                        var local = Uri.UnescapeDataString(uri.LocalPath);
                        if (File.Exists(local) && BatchResizeService.IsSupportedImage(local))
                            return true;
                    }
                }
            }

            // Check text representing file path or URI
            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim().Trim('"', '\'', '<', '>');
                    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
                    {
                        var local = Uri.UnescapeDataString(uri.LocalPath);
                        if (File.Exists(local) && BatchResizeService.IsSupportedImage(local))
                            return true;
                    }

                    if (File.Exists(trimmed) && BatchResizeService.IsSupportedImage(trimmed))
                        return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Error checking clipboard contents.");
            return false;
        }
    }

    public async Task<string?> PasteImageToTempFileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard is null)
            {
                _log?.LogWarning("Clipboard is not available.");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 1. Try reading raw bitmap data via Avalonia's built-in TryGetBitmapAsync
            Bitmap? bitmap = null;
            try
            {
                bitmap = await clipboard.TryGetBitmapAsync();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "TryGetBitmapAsync threw an exception.");
            }

            if (bitmap is not null)
            {
                using (bitmap)
                {
                    Directory.CreateDirectory(_cacheDirectory);
                    var fileName = $"pasted_image_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}.png";
                    var tempPath = Path.Combine(_cacheDirectory, fileName);
                    using (var s = File.Create(tempPath))
                    {
                        bitmap.Save(s, new PngBitmapEncoderOptions());
                    }
                    _log?.LogInformation("Pasted image saved from clipboard to {Path}", tempPath);
                    return tempPath;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Try reading raw image byte formats (image/png, image/jpeg, image/bmp, etc.)
            try
            {
                var formats = await clipboard.GetDataFormatsAsync();
                if (formats is { Count: > 0 })
                {
                    var imageFormat = formats.FirstOrDefault(f =>
                        f.Identifier.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/bmp", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/x-bmp", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/x-MS-bmp", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/tiff", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Contains("png", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Contains("bitmap", StringComparison.OrdinalIgnoreCase) ||
                        f.Identifier.Contains("image", StringComparison.OrdinalIgnoreCase));

                    if (imageFormat is not null)
                    {
                        var bytes = await clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(imageFormat.Identifier))
                            ?? await clipboard.TryGetValueAsync(DataFormat.CreateBytesApplicationFormat(imageFormat.Identifier));

                        if (bytes is { Length: > 0 } && IsValidImageData(bytes, out var ext))
                        {
                            Directory.CreateDirectory(_cacheDirectory);
                            var fileName = $"pasted_image_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}.{ext}";
                            var tempPath = Path.Combine(_cacheDirectory, fileName);
                            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
                            _log?.LogInformation("Pasted raw image format {Format} saved to {Path}", imageFormat.Identifier, tempPath);
                            return tempPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Failed to read raw image data from clipboard formats.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Try reading GNOME / KDE / X11 file copy formats (text/uri-list, x-special/gnome-copied-files)
            try
            {
                var uriListData = await clipboard.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("text/uri-list"))
                    ?? await clipboard.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files"));

                if (!string.IsNullOrWhiteSpace(uriListData))
                {
                    var lines = uriListData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("copy", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("cut", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
                        {
                            var localPath = Uri.UnescapeDataString(uri.LocalPath);
                            if (File.Exists(localPath) && BatchResizeService.IsSupportedImage(localPath))
                            {
                                _log?.LogInformation("Found image file in clipboard URI list: {Path}", localPath);
                                return localPath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Failed to read uri-list from clipboard.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 4. Try reading file list from clipboard
            try
            {
                var files = await clipboard.TryGetFilesAsync();
                if (files is { Length: > 0 })
                {
                    foreach (var file in files)
                    {
                        var localPath = Uri.UnescapeDataString(file.Path.LocalPath);
                        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath) && BatchResizeService.IsSupportedImage(localPath))
                        {
                            _log?.LogInformation("Found image file in clipboard: {Path}", localPath);
                            return localPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "TryGetFilesAsync threw an exception.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 5. Try reading text representing a file path or URI
            try
            {
                var text = await clipboard.TryGetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim().Trim('"', '\'', '<', '>');
                        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
                        {
                            var localPath = Uri.UnescapeDataString(uri.LocalPath);
                            if (File.Exists(localPath) && BatchResizeService.IsSupportedImage(localPath))
                            {
                                _log?.LogInformation("Found image path in clipboard URI text: {Path}", localPath);
                                return localPath;
                            }
                        }

                        if (File.Exists(trimmed) && BatchResizeService.IsSupportedImage(trimmed))
                        {
                            _log?.LogInformation("Found image path in clipboard text: {Path}", trimmed);
                            return trimmed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "TryGetTextAsync threw an exception.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 6. Linux CLI fallback if tools are available
            if (OperatingSystem.IsLinux())
            {
                var cliPasted = await TryPasteViaLinuxCliAsync(cancellationToken);
                if (cliPasted is not null && File.Exists(cliPasted))
                    return cliPasted;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            _log?.LogInformation("Paste image cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to paste image from clipboard.");
            return null;
        }
    }

    private static bool IsValidImageData(byte[] bytes, out string extension)
    {
        extension = "png";
        if (bytes == null || bytes.Length < 4)
            return false;

        // PNG: 89 50 4E 47
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            extension = "png";
            return true;
        }

        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            extension = "jpg";
            return true;
        }

        // BMP: 42 4D ("BM")
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            extension = "bmp";
            return true;
        }

        // GIF: 47 49 46 38 ("GIF8")
        if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            extension = "gif";
            return true;
        }

        // WebP: RIFF....WEBP
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            extension = "webp";
            return true;
        }

        // TIFF: II*. or MM.*
        if (bytes.Length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
        {
            extension = "tiff";
            return true;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            using var codec = SkiaSharp.SKCodec.Create(stream);
            if (codec is not null)
            {
                extension = codec.EncodedFormat switch
                {
                    SkiaSharp.SKEncodedImageFormat.Jpeg => "jpg",
                    SkiaSharp.SKEncodedImageFormat.Bmp => "bmp",
                    SkiaSharp.SKEncodedImageFormat.Gif => "gif",
                    SkiaSharp.SKEncodedImageFormat.Webp => "webp",
                    _ => "png"
                };
                return true;
            }
        }
        catch { }

        return false;
    }

    private async Task<string?> TryPasteViaLinuxCliAsync(CancellationToken cancellationToken)
    {
        try
        {
            var isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
                            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";

            if (isWayland && File.Exists("/usr/bin/wl-paste"))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/wl-paste",
                    ArgumentList = { "--type", "image/png" },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is not null)
                {
                    using var ms = new MemoryStream();
                    await proc.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
                    await proc.WaitForExitAsync(cancellationToken);
                    if (proc.ExitCode == 0 && ms.Length > 0 && IsValidImageData(ms.ToArray(), out var ext))
                    {
                        Directory.CreateDirectory(_cacheDirectory);
                        var fileName = $"pasted_image_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}.{ext}";
                        var tempPath = Path.Combine(_cacheDirectory, fileName);
                        await File.WriteAllBytesAsync(tempPath, ms.ToArray(), cancellationToken);
                        _log?.LogInformation("Pasted image via wl-paste saved to {Path}", tempPath);
                        return tempPath;
                    }
                }
            }
            else if (!isWayland && File.Exists("/usr/bin/xclip"))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/xclip",
                    ArgumentList = { "-selection", "clipboard", "-t", "image/png", "-o" },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is not null)
                {
                    using var ms = new MemoryStream();
                    await proc.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
                    await proc.WaitForExitAsync(cancellationToken);
                    if (proc.ExitCode == 0 && ms.Length > 0 && IsValidImageData(ms.ToArray(), out var ext))
                    {
                        Directory.CreateDirectory(_cacheDirectory);
                        var fileName = $"pasted_image_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}.{ext}";
                        var tempPath = Path.Combine(_cacheDirectory, fileName);
                        await File.WriteAllBytesAsync(tempPath, ms.ToArray(), cancellationToken);
                        _log?.LogInformation("Pasted image via xclip saved to {Path}", tempPath);
                        return tempPath;
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Linux CLI clipboard paste failed.");
        }

        return null;
    }

    public async Task<bool> CopyImageToClipboardAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var clipboard = GetClipboard();
            if (clipboard is null)
            {
                _log?.LogWarning("Clipboard is not available.");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(filePath);
            var bitmap = new Bitmap(stream);
            await clipboard.SetBitmapAsync(bitmap);
            _log?.LogInformation("Copied image {Path} to clipboard.", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to copy image {Path} to clipboard.", filePath);
            return false;
        }
    }

    public async Task<bool> CopyBitmapToClipboardAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        try
        {
            var clipboard = GetClipboard();
            if (clipboard is null)
            {
                _log?.LogWarning("Clipboard is not available.");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await clipboard.SetBitmapAsync(bitmap);
            _log?.LogInformation("Copied in-memory bitmap to clipboard.");
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to copy bitmap to clipboard.");
            return false;
        }
    }

    public void CleanOldTemporaryFiles(TimeSpan? maxAge = null)
    {
        if (!Directory.Exists(_cacheDirectory))
            return;

        var threshold = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(24));

        try
        {
            var dirInfo = new DirectoryInfo(_cacheDirectory);
            foreach (var file in dirInfo.EnumerateFiles("pasted_image_*.*"))
            {
                try
                {
                    if (file.CreationTimeUtc < threshold || file.LastWriteTimeUtc < threshold)
                    {
                        file.Delete();
                    }
                }
                catch
                {
                    // Ignore locked or inaccessible files
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Failed to clean old clipboard files in {Dir}", _cacheDirectory);
        }
    }
}
