// Hands the pages of this process back to the operating system once it is idle in the tray, so Task Manager /
// system monitors show what Filee really needs instead of what the last conversion or window touched.

using System.Runtime.InteropServices;

namespace Filee.Platform.Windows;

public static class ProcessMemory
{
    [DllImport("libc", EntryPoint = "malloc_trim", SetLastError = true)]
    private static extern int MallocTrim(nuint pad);

    /// <summary>
    /// Removes as many pages as possible from the working set / heap (they come back on use).
    /// Calls SetProcessWorkingSetSize on Windows and malloc_trim on Linux; false when the OS refused.
    /// </summary>
    public static bool TrimWorkingSet()
    {
        if (OperatingSystem.IsWindows())
            return NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);

        if (OperatingSystem.IsLinux())
        {
            try
            {
                return MallocTrim(0) != 0;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }
}
