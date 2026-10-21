using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.History;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Filee.App.Tests;

public class HistoryAndPreferencesTests
{
    [Fact]
    public void UserDataStore_RemoveHistory_removes_single_entry_by_job_id()
    {
        var tempDir = Directory.CreateTempSubdirectory("history-test-").FullName;
        try
        {
            var store = new UserDataStore(tempDir);
            store.Load();

            var job1 = "job-111";
            var job2 = "job-222";
            var job3 = "job-333";

            store.AddHistory(new HistoryEntry
            {
                JobId = job1,
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["test1.png"],
                Outputs = ["out1.png"]
            });
            store.AddHistory(new HistoryEntry
            {
                JobId = job2,
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["test2.png"],
                Outputs = ["out2.png"]
            });
            store.AddHistory(new HistoryEntry
            {
                JobId = job3,
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["test3.png"],
                Outputs = ["out3.png"]
            });

            Assert.Equal(3, store.History.Count);

            var eventFired = false;
            store.HistoryChanged += (_, _) => eventFired = true;

            // Remove middle job
            var removed = store.RemoveHistory(job2);
            Assert.True(removed);
            Assert.True(eventFired);
            Assert.Equal(2, store.History.Count);
            Assert.DoesNotContain(store.History, h => h.JobId == job2);
            Assert.Contains(store.History, h => h.JobId == job1);
            Assert.Contains(store.History, h => h.JobId == job3);

            // Removing nonexistent returns false
            eventFired = false;
            var removedNonexistent = store.RemoveHistory("nonexistent-job");
            Assert.False(removedNonexistent);
            Assert.False(eventFired);
            Assert.Equal(2, store.History.Count);

            // Reload store from disk and verify persistence
            var reloadedStore = new UserDataStore(tempDir);
            reloadedStore.Load();
            Assert.Equal(2, reloadedStore.History.Count);
            Assert.DoesNotContain(reloadedStore.History, h => h.JobId == job2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void UserDataStore_ClearHistory_removes_all_entries()
    {
        var tempDir = Directory.CreateTempSubdirectory("history-clear-test-").FullName;
        try
        {
            var store = new UserDataStore(tempDir);
            store.Load();

            store.AddHistory(new HistoryEntry
            {
                JobId = "job-a",
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["a.png"],
                Outputs = ["a.webp"]
            });
            store.AddHistory(new HistoryEntry
            {
                JobId = "job-b",
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["b.png"],
                Outputs = ["b.webp"]
            });
            Assert.Equal(2, store.History.Count);

            var eventFired = false;
            store.HistoryChanged += (_, _) => eventFired = true;

            store.ClearHistory();
            Assert.True(eventFired);
            Assert.Empty(store.History);

            // Reload from disk
            var reloaded = new UserDataStore(tempDir);
            reloaded.Load();
            Assert.Empty(reloaded.History);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_DefaultCompressionQuality_clamping()
    {
        var settings = new AppSettings();
        Assert.Equal("same", settings.DefaultImageFormat);
        Assert.Equal(85, settings.DefaultCompressionQuality);
        Assert.Equal(85, settings.EffectiveDefaultCompressionQuality);
        Assert.True(settings.DefaultMaintainAspectRatio);

        settings.DefaultCompressionQuality = 5;
        Assert.Equal(10, settings.EffectiveDefaultCompressionQuality);

        settings.DefaultCompressionQuality = 150;
        Assert.Equal(100, settings.EffectiveDefaultCompressionQuality);

        settings.DefaultCompressionQuality = 50;
        Assert.Equal(50, settings.EffectiveDefaultCompressionQuality);
    }

    [AvaloniaFact]
    public void HistoryItemViewModel_CanOpenOutput_and_RemoveCommand()
    {
        TestServices.EnsureInitialized();
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        var tempDir = Directory.CreateTempSubdirectory("history-item-test-").FullName;
        try
        {
            var store = new UserDataStore(tempDir);
            store.Load();

            var existingFilePath = Path.Combine(tempDir, "output.png");
            File.WriteAllText(existingFilePath, "dummy");

            var entryWithRealFile = new HistoryEntry
            {
                JobId = "job-real",
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["in.png"],
                Outputs = [existingFilePath],
            };

            var entryWithMissingFile = new HistoryEntry
            {
                JobId = "job-missing",
                FinishedAt = DateTime.Now,
                PresetName = "PNG",
                Sources = ["in.png"],
                Outputs = ["/nonexistent/file.png"],
            };

            store.AddHistory(entryWithRealFile);
            store.AddHistory(entryWithMissingFile);

            var itemReal = new HistoryItemViewModel(entryWithRealFile, loc, platform, onRemove: e => store.RemoveHistory(e.JobId));
            Assert.True(itemReal.CanOpenOutput);
            Assert.Equal(existingFilePath, itemReal.PrimaryOutput);

            var itemMissing = new HistoryItemViewModel(entryWithMissingFile, loc, platform, onRemove: e => store.RemoveHistory(e.JobId));
            Assert.False(itemMissing.CanOpenOutput);

            // Execute RemoveCommand on real
            itemReal.RemoveCommand.Execute(null);
            Assert.DoesNotContain(store.History, h => h.JobId == "job-real");
            Assert.Contains(store.History, h => h.JobId == "job-missing");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void GeneralPageViewModel_updates_settings_properties()
    {
        TestServices.EnsureInitialized();
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        using var vm = new GeneralPageViewModel(store, loc, platform, NullLogger<GeneralPageViewModel>.Instance);

        var webpChoice = vm.ImageFormats.FirstOrDefault(f => f.Value == "webp");
        Assert.NotNull(webpChoice);
        vm.DefaultImageFormat = webpChoice;
        Assert.Equal("webp", store.Settings.DefaultImageFormat);

        vm.DefaultQuality = 70;
        Assert.Equal(70, store.Settings.DefaultCompressionQuality);

        vm.DefaultMaintainAspectRatio = false;
        Assert.False(store.Settings.DefaultMaintainAspectRatio);

        // Reset to normal defaults
        var sameChoice = vm.ImageFormats.FirstOrDefault(f => f.Value == "same");
        Assert.NotNull(sameChoice);
        vm.DefaultImageFormat = sameChoice;
        vm.DefaultQuality = 85;
        vm.DefaultMaintainAspectRatio = true;
    }

    [Fact]
    public void HistoryItemViewModel_CreateOpenProcessStartInfo_is_platform_aware()
    {
        var testPath = Path.Combine(Path.GetTempPath(), "test-output.png");
        var psi = HistoryItemViewModel.CreateOpenProcessStartInfo(testPath);
        Assert.NotNull(psi);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(psi.UseShellExecute);
            Assert.Equal(testPath, psi.FileName);
            Assert.Empty(psi.ArgumentList);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.False(psi.UseShellExecute);
            Assert.Equal("open", psi.FileName);
            Assert.Contains(testPath, psi.ArgumentList);
        }
        else
        {
            Assert.False(psi.UseShellExecute);
            Assert.Equal("xdg-open", psi.FileName);
            Assert.Contains(testPath, psi.ArgumentList);
        }
    }
}
