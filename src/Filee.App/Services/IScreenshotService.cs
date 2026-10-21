// Service abstraction for screen and region capture.

namespace Filee.App.Services;

public interface IScreenshotService
{
    /// <summary>
    /// Indicates whether screen capture is supported on the current operating system and environment.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Prompts the user to interactively capture a screen region or window.
    /// Returns the file path of the captured image, or null if cancelled or unsupported.
    /// </summary>
    Task<string?> CaptureInteractiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes cached screenshot files older than the specified age (default 24 hours).
    /// </summary>
    void CleanOldTemporaryFiles(TimeSpan? maxAge = null);
}
