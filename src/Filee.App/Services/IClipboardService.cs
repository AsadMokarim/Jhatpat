// Service abstraction for clipboard image input and output.

using Avalonia.Media.Imaging;

namespace Filee.App.Services;

public interface IClipboardService
{
    /// <summary>
    /// Checks whether the clipboard currently contains an image (raw bitmap or image file).
    /// </summary>
    Task<bool> HasImageAsync();

    /// <summary>
    /// Reads an image from the clipboard (raw bitmap, image file reference, or file path text)
    /// and saves it to a managed cache file. Returns the path to the saved file, or null if no image was found.
    /// </summary>
    Task<string?> PasteImageToTempFileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies an image file to the system clipboard as a native bitmap.
    /// </summary>
    Task<bool> CopyImageToClipboardAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies an in-memory Avalonia bitmap to the system clipboard.
    /// </summary>
    Task<bool> CopyBitmapToClipboardAsync(Bitmap bitmap, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up temporary clipboard cache files older than the specified age (default 24 hours).
    /// </summary>
    void CleanOldTemporaryFiles(TimeSpan? maxAge = null);
}
