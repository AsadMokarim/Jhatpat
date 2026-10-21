using System.IO;

namespace Filee.Core.Platform;

public static class FileSystemPaths
{
    // Unix volumes can be case-sensitive, including APFS formatted with that option.
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Checks whether two file paths refer to the exact same file on disk, canonicalizing full paths.
    /// Case-insensitive comparison is used to prevent accidental source overwrites across all platforms.
    /// </summary>
    public static bool PathsEqual(string? pathA, string? pathB)
    {
        if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
            return false;
        try
        {
            var fullA = Path.GetFullPath(pathA);
            var fullB = Path.GetFullPath(pathB);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether an exception represents an out of disk space error.
    /// </summary>
    public static bool IsDiskFull(Exception ex)
    {
        const int ERROR_DISK_FULL = unchecked((int)0x80070070);
        const int ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);

        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current.HResult == ERROR_DISK_FULL || current.HResult == ERROR_HANDLE_DISK_FULL)
                return true;
            if (current is IOException && (
                current.Message.Contains("space", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("disk full", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Determines whether an exception indicates a corrupt or invalid data structure.
    /// </summary>
    public static bool IsCorrupt(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is InvalidDataException)
                return true;
            var name = current.GetType().Name;
            if (name.Contains("Corrupt", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Determines whether an exception is caused by unauthorized access or permissions.
    /// </summary>
    public static bool IsPermissionDenied(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Determines whether an exception is caused by a missing source file or directory.
    /// </summary>
    public static bool IsNotFound(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is FileNotFoundException or DirectoryNotFoundException)
                return true;
        }
        return false;
    }
}
