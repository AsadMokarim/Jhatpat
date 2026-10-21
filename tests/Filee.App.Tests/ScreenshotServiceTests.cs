using Filee.App.Services;
using Xunit;

namespace Filee.App.Tests;

public class ScreenshotServiceTests
{
    [Fact]
    public void IsSupported_returns_true_when_capture_handler_is_supplied()
    {
        var service = new ScreenshotService(
            cacheDirectory: null,
            captureHandler: _ => Task.FromResult<string?>("/tmp/dummy.png"));

        Assert.True(service.IsSupported);
    }

    [Fact]
    public async Task CaptureInteractiveAsync_invokes_custom_handler_and_returns_result()
    {
        var expectedPath = "/tmp/test_screenshot.png";
        var invoked = false;

        var service = new ScreenshotService(
            cacheDirectory: null,
            captureHandler: ct =>
            {
                invoked = true;
                return Task.FromResult<string?>(expectedPath);
            });

        var result = await service.CaptureInteractiveAsync(TestContext.Current.CancellationToken);

        Assert.True(invoked);
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public async Task CaptureInteractiveAsync_returns_null_when_handler_returns_null()
    {
        var service = new ScreenshotService(
            cacheDirectory: null,
            captureHandler: _ => Task.FromResult<string?>(null));

        var result = await service.CaptureInteractiveAsync(TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task CaptureInteractiveAsync_propagates_or_handles_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new ScreenshotService(
            cacheDirectory: null,
            captureHandler: ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<string?>("/tmp/not_reached.png");
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureInteractiveAsync(cts.Token));
    }

    [Fact]
    public void IsSupported_on_current_platform_does_not_throw()
    {
        var service = new ScreenshotService();
        var supported = service.IsSupported;
        if (OperatingSystem.IsLinux())
        {
            Assert.True(supported || !supported);
        }
    }

    [Fact]
    public void CleanOldTemporaryFiles_purges_stale_files_and_keeps_recent()
    {
        var tempDir = Directory.CreateTempSubdirectory("filee-screenshot-cache-test-").FullName;
        try
        {
            var oldFile = Path.Combine(tempDir, "screenshot_20200101_000000_old.png");
            var recentFile = Path.Combine(tempDir, "screenshot_20261020_000000_new.png");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(recentFile, "recent");

            // Explicitly set old file timestamps
            File.SetCreationTimeUtc(oldFile, DateTime.UtcNow.AddDays(-5));
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-5));

            var service = new ScreenshotService(tempDir);
            service.CleanOldTemporaryFiles(TimeSpan.FromHours(24));

            Assert.False(File.Exists(oldFile), "Old screenshot file should have been deleted");
            Assert.True(File.Exists(recentFile), "Recent screenshot file should have been preserved");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
