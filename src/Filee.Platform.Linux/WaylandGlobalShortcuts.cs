using System.Diagnostics;
using System.Runtime.Versioning;

namespace Filee.Platform.Linux;

/// <summary>
/// Probes and interacts with XDG Desktop Portal Global Shortcuts (org.freedesktop.portal.GlobalShortcuts)
/// where supported by modern Wayland compositors (KDE Plasma 5.27+, GNOME 47+, Hyprland).
/// </summary>
[SupportedOSPlatform("linux")]
public static class WaylandGlobalShortcuts
{
    private static bool? _portalSupported;

    /// <summary>
    /// Checks whether org.freedesktop.portal.GlobalShortcuts is available on the session D-Bus.
    /// Safe and non-throwing.
    /// </summary>
    public static bool IsPortalAvailable()
    {
        if (_portalSupported is { } cached)
            return cached;

        if (!LinuxPlatformServices.IsWaylandSession)
        {
            _portalSupported = false;
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gdbus",
                ArgumentList =
                {
                    "introspect",
                    "--session",
                    "--dest", "org.freedesktop.portal.Desktop",
                    "--object-path", "/org/freedesktop/portal/desktop"
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _portalSupported = false;
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: true);
                _portalSupported = false;
                return false;
            }

            _portalSupported = process.ExitCode == 0 && output.Contains("org.freedesktop.portal.GlobalShortcuts", StringComparison.Ordinal);
            return _portalSupported.Value;
        }
        catch
        {
            _portalSupported = false;
            return false;
        }
    }

    /// <summary>
    /// Resets the cached portal availability check (e.g. for unit testing).
    /// </summary>
    internal static void ResetCache()
    {
        _portalSupported = null;
    }
}
