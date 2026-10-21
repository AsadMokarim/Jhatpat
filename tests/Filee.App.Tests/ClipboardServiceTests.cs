using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Filee.App.Services;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class ClipboardServiceTests
{
    [Fact]
    public void CleanOldTemporaryFiles_removes_old_pasted_images_and_keeps_fresh_ones()
    {
        var tempDir = Directory.CreateTempSubdirectory("clipboard-test-clean-").FullName;
        try
        {
            var oldPasted = Path.Combine(tempDir, "pasted_image_20200101_000000_abc123.png");
            var freshPasted = Path.Combine(tempDir, "pasted_image_20990101_000000_xyz789.png");
            var otherOldFile = Path.Combine(tempDir, "document_20200101.txt");

            File.WriteAllText(oldPasted, "old image data");
            File.WriteAllText(freshPasted, "fresh image data");
            File.WriteAllText(otherOldFile, "do not touch");

            // Set file times
            var oldTime = DateTime.UtcNow.AddHours(-48);
            File.SetCreationTimeUtc(oldPasted, oldTime);
            File.SetLastWriteTimeUtc(oldPasted, oldTime);

            File.SetCreationTimeUtc(otherOldFile, oldTime);
            File.SetLastWriteTimeUtc(otherOldFile, oldTime);

            var service = new ClipboardService(cacheDirectory: tempDir, clipboardAccessor: () => null);

            service.CleanOldTemporaryFiles(TimeSpan.FromHours(24));

            Assert.False(File.Exists(oldPasted), "Old pasted image should have been removed.");
            Assert.True(File.Exists(freshPasted), "Fresh pasted image should be preserved.");
            Assert.True(File.Exists(otherOldFile), "Non-pasted files should never be deleted.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task When_clipboard_is_null_methods_fail_gracefully()
    {
        var tempDir = Directory.CreateTempSubdirectory("clipboard-test-null-").FullName;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var service = new ClipboardService(cacheDirectory: tempDir, clipboardAccessor: () => null);

            Assert.False(await service.HasImageAsync());
            Assert.Null(await service.PasteImageToTempFileAsync(ct));

            var nonExistentFile = Path.Combine(tempDir, "nonexistent.png");
            Assert.False(await service.CopyImageToClipboardAsync(nonExistentFile, ct));

            var existingFile = Path.Combine(tempDir, "dummy.png");
            File.WriteAllText(existingFile, "content");
            Assert.False(await service.CopyImageToClipboardAsync(existingFile, ct));

            await Assert.ThrowsAsync<ArgumentNullException>(() => service.CopyBitmapToClipboardAsync(null!, ct));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PasteImageToTempFileAsync_returns_null_when_cancelled()
    {
        var tempDir = Directory.CreateTempSubdirectory("clipboard-test-cancel-").FullName;
        try
        {
            var service = new ClipboardService(cacheDirectory: tempDir, clipboardAccessor: () => null);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await service.PasteImageToTempFileAsync(cts.Token);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task HeadlessClipboard_interaction_test()
    {
        TestServices.EnsureInitialized();
        var window = new Window();
        window.Show();

        var clipboard = window.Clipboard;
        Assert.NotNull(clipboard);

        var tempDir = Directory.CreateTempSubdirectory("clipboard-test-headless-").FullName;
        try
        {
            var service = new ClipboardService(cacheDirectory: tempDir, clipboardAccessor: () => clipboard);

            // Without anything copied, HasImageAsync is false
            var hasImage = await service.HasImageAsync();
            Assert.False(hasImage);

            var pasted = await service.PasteImageToTempFileAsync();
            Assert.Null(pasted);

            // Test pasting via text representing a file path
            var dummyPng = Path.Combine(tempDir, "test_dummy.png");
            using (var bmp = new SkiaSharp.SKBitmap(10, 10))
            using (var img = SkiaSharp.SKImage.FromBitmap(bmp))
            using (var stream = File.OpenWrite(dummyPng))
            {
                img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).SaveTo(stream);
            }

            await clipboard.SetTextAsync(dummyPng);
            Assert.True(await service.HasImageAsync());
            var pastedPath = await service.PasteImageToTempFileAsync();
            Assert.Equal(dummyPng, pastedPath);

            // Test pasting via file:// URI in text
            var fileUri = new Uri(dummyPng).AbsoluteUri;
            await clipboard.SetTextAsync(fileUri);
            Assert.True(await service.HasImageAsync());
            var pastedFromUri = await service.PasteImageToTempFileAsync();
            Assert.Equal(dummyPng, pastedFromUri);

            // Test pasting via quoted file path in text
            await clipboard.SetTextAsync($"\"{dummyPng}\"");
            Assert.True(await service.HasImageAsync());
            var pastedFromQuoted = await service.PasteImageToTempFileAsync();
            Assert.Equal(dummyPng, pastedFromQuoted);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
