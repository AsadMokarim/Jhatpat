// Cross-platform screenshot capture service.

using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public sealed class ScreenshotService : IScreenshotService
{
    private readonly Func<CancellationToken, Task<string?>>? _captureHandler;
    private readonly string _cacheDirectory;
    private readonly ILogger<ScreenshotService>? _log;

    public ScreenshotService(
        string? cacheDirectory = null,
        Func<CancellationToken, Task<string?>>? captureHandler = null,
        ILogger<ScreenshotService>? log = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(UserDataStore.DefaultDirectory, "screenshot_cache");
        _captureHandler = captureHandler;
        _log = log;

        // Clean stale temporary screenshot files on startup
        try
        {
            CleanOldTemporaryFiles();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Failed to clean initial temporary screenshot files.");
        }
    }

    public bool IsSupported
    {
        get
        {
            if (_captureHandler is not null)
                return true;

            if (OperatingSystem.IsMacOS())
                return File.Exists("/usr/sbin/screencapture") || ExistsOnPath("screencapture");

            if (OperatingSystem.IsWindows())
                return true;

            if (OperatingSystem.IsLinux())
            {
                if (ExistsOnPath("python3") && ExistsOnPath("gdbus"))
                    return true;

                return ExistsOnPath("import") ||
                       ExistsOnPath("gnome-screenshot") ||
                       ExistsOnPath("spectacle") ||
                       (ExistsOnPath("grim") && ExistsOnPath("slurp")) ||
                       ExistsOnPath("maim") ||
                       ExistsOnPath("scrot") ||
                       ExistsOnPath("flameshot");
            }

            return false;
        }
    }

    public async Task<string?> CaptureInteractiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_captureHandler is not null)
            return await _captureHandler(cancellationToken);

        if (!IsSupported)
        {
            _log?.LogWarning("Screenshot capture is not supported on this platform.");
            return null;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var outputPath = Path.Combine(_cacheDirectory, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}.png");

        try
        {
            string? captured = null;
            if (OperatingSystem.IsLinux())
            {
                captured = await CaptureLinuxAsync(outputPath, cancellationToken);
            }
            else if (OperatingSystem.IsMacOS())
            {
                captured = await CaptureMacOSAsync(outputPath, cancellationToken);
            }
            else if (OperatingSystem.IsWindows())
            {
                captured = await CaptureWindowsAsync(outputPath, cancellationToken);
            }

            if (captured is not null && File.Exists(captured))
            {
                _log?.LogInformation("Screenshot captured successfully to {Path}", captured);
                return captured;
            }

            // Cleanup partial/empty output on cancellation or failure
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            _log?.LogInformation("Screenshot capture cancelled.");
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to capture screenshot.");
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
            return null;
        }
    }

    private async Task<string?> CaptureLinuxAsync(string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Try XDG Desktop Portal Screenshot (standard across GNOME, KDE, Wayland, and X11)
        if (ExistsOnPath("python3"))
        {
            try
            {
                var portalCapture = await CaptureWaylandGnomeAsync(outputPath, cancellationToken);
                if (portalCapture is not null && File.Exists(portalCapture) && new FileInfo(portalCapture).Length > 0)
                    return portalCapture;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Portal interactive screenshot was not completed or supported.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
                        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";

        if (isWayland)
        {
            if (ExistsOnPath("spectacle"))
            {
                var captured = await CaptureWaylandSpectacleAsync(outputPath, cancellationToken);
                if (captured is not null) return captured;
            }

            if (ExistsOnPath("grim") && ExistsOnPath("slurp"))
            {
                var captured = await CaptureWaylandGrimAsync(outputPath, cancellationToken);
                if (captured is not null) return captured;
            }
        }

        // 2. X11 & CLI screenshot tools
        if (ExistsOnPath("import"))
        {
            var captured = await RunCaptureProcessAsync("import", [outputPath], outputPath, cancellationToken);
            if (captured is not null) return captured;
        }

        if (ExistsOnPath("gnome-screenshot"))
        {
            var captured = await RunCaptureProcessAsync("gnome-screenshot", ["-a", "-f", outputPath], outputPath, cancellationToken);
            if (captured is not null) return captured;
        }

        if (ExistsOnPath("maim"))
        {
            var captured = await RunCaptureProcessAsync("maim", ["-s", outputPath], outputPath, cancellationToken);
            if (captured is not null) return captured;
        }

        if (ExistsOnPath("scrot"))
        {
            var captured = await RunCaptureProcessAsync("scrot", ["-s", outputPath], outputPath, cancellationToken);
            if (captured is not null) return captured;
        }

        if (ExistsOnPath("flameshot"))
        {
            var captured = await RunCaptureProcessAsync("flameshot", ["gui", "-r"], outputPath, cancellationToken, redirectStdoutToOutput: true);
            if (captured is not null) return captured;
        }

        return null;
    }

    private static async Task<string?> CaptureMacOSAsync(string outputPath, CancellationToken cancellationToken)
    {
        var bin = ResolveBinary("screencapture") ?? "/usr/sbin/screencapture";
        var psi = new ProcessStartInfo
        {
            FileName = bin,
            ArgumentList = { "-i", outputPath },
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            ? outputPath
            : null;
    }

    private static async Task<string?> CaptureWindowsAsync(string outputPath, CancellationToken cancellationToken)
    {
        var escapedPath = outputPath.Replace("'", "''");
        var script = $@"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
$bitmap.Save('{escapedPath}', [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            ? outputPath
            : null;
    }

    private static async Task<string?> RunCaptureProcessAsync(
        string binary,
        string[] args,
        string outputPath,
        CancellationToken cancellationToken,
        bool redirectStdoutToOutput = false)
    {
        var resolved = ResolveBinary(binary) ?? binary;
        var psi = new ProcessStartInfo
        {
            FileName = resolved,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (redirectStdoutToOutput)
            psi.RedirectStandardOutput = true;

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        if (redirectStdoutToOutput)
        {
            await using var fs = File.Create(outputPath);
            await process.StandardOutput.BaseStream.CopyToAsync(fs, cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            ? outputPath
            : null;
    }

    private static async Task<string?> CaptureWaylandGnomeAsync(string outputPath, CancellationToken cancellationToken)
    {
        // Try modern XDG Desktop Portal via python3 dbus
        var pyScript = @"
import sys, os, urllib.parse, shutil
try:
    import dbus, dbus.mainloop.glib
    from gi.repository import GLib

    dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
    bus = dbus.SessionBus()
    loop = GLib.MainLoop()
    output_path = sys.argv[1]
    result_path = [None]

    def on_response(response, results):
        if response == 0 and 'uri' in results:
            uri = str(results['uri'])
            parsed = urllib.parse.urlparse(uri)
            local = urllib.parse.unquote(parsed.path)
            if os.path.exists(local):
                result_path[0] = local
        loop.quit()

    portal = bus.get_object('org.freedesktop.portal.Desktop', '/org/freedesktop/portal/desktop')
    iface = dbus.Interface(portal, 'org.freedesktop.portal.Screenshot')
    token = f'jhatpat_{os.getpid()}'
    sender = bus.get_unique_name().replace('.', '_').lstrip(':')
    expected_handle = f'/org/freedesktop/portal/desktop/request/{sender}/{token}'

    bus.add_signal_receiver(on_response,
        signal_name='Response',
        dbus_interface='org.freedesktop.portal.Request',
        path=expected_handle)

    options = {'interactive': dbus.Boolean(True), 'handle_token': dbus.String(token)}
    handle = iface.Screenshot('', options)

    if str(handle) != expected_handle:
        bus.add_signal_receiver(on_response,
            signal_name='Response',
            dbus_interface='org.freedesktop.portal.Request',
            path=str(handle))

    loop.run()
    if result_path[0]:
        shutil.copy2(result_path[0], output_path)
        sys.exit(0)
    else:
        sys.exit(2)
except Exception as e:
    sys.exit(1)
";
        var psi = new ProcessStartInfo
        {
            FileName = ResolveBinary("python3") ?? "/usr/bin/python3",
            ArgumentList = { "-c", pyScript, outputPath },
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            return outputPath;

        return null;
    }

    private static async Task<string?> CaptureWaylandSpectacleAsync(string outputPath, CancellationToken cancellationToken)
    {
        return await RunCaptureProcessAsync("spectacle", ["-r", "-b", "-n", "-o", outputPath], outputPath, cancellationToken);
    }

    private static async Task<string?> CaptureWaylandGrimAsync(string outputPath, CancellationToken cancellationToken)
    {
        var slurpBin = ResolveBinary("slurp") ?? "/usr/bin/slurp";
        var grimBin = ResolveBinary("grim") ?? "/usr/bin/grim";

        var slurpPsi = new ProcessStartInfo
        {
            FileName = slurpBin,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var slurpProcess = Process.Start(slurpPsi);
        if (slurpProcess is null)
            return null;

        var geometry = (await slurpProcess.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        await slurpProcess.WaitForExitAsync(cancellationToken);

        if (slurpProcess.ExitCode != 0 || string.IsNullOrEmpty(geometry))
            return null;

        return await RunCaptureProcessAsync(grimBin, ["-g", geometry, outputPath], outputPath, cancellationToken);
    }

    private static bool ExistsOnPath(string binary) => ResolveBinary(binary) is not null;

    private static string? ResolveBinary(string binary)
    {
        if (Path.IsPathRooted(binary) && File.Exists(binary))
            return binary;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir, binary);
                if (File.Exists(candidate))
                    return candidate;
                if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe"))
                    return candidate + ".exe";
            }
        }

        string[] standardDirs = ["/usr/bin", "/usr/local/bin", "/bin", "/usr/sbin", "/sbin"];
        foreach (var dir in standardDirs)
        {
            var candidate = Path.Combine(dir, binary);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Cleans abandoned temporary screenshot cache files older than maxAge (default 24 hours).
    /// </summary>
    public void CleanOldTemporaryFiles(TimeSpan? maxAge = null)
    {
        if (!Directory.Exists(_cacheDirectory))
            return;

        var threshold = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(24));

        try
        {
            var dirInfo = new DirectoryInfo(_cacheDirectory);
            foreach (var file in dirInfo.EnumerateFiles("screenshot_*.*"))
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
            _log?.LogDebug(ex, "Failed to clean old screenshot files in {Dir}", _cacheDirectory);
        }
    }
}
