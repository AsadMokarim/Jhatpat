using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Xunit;

namespace Filee.App.Tests;

public class HomePageViewModelTests
{
    [AvaloniaFact]
    public void HomePageViewModel_initializes_with_expected_defaults()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        using var vm = new HomePageViewModel(store, loc, platform);

        Assert.NotNull(vm.DropHint);
        Assert.False(vm.IsCapturing);
        Assert.False(vm.IsPasting);
        Assert.False(vm.IsBrowsing);
        Assert.NotNull(vm.History);
        Assert.NotNull(vm.BrowseFilesCommand);
        Assert.NotNull(vm.PasteImageCommand);
        Assert.NotNull(vm.CaptureScreenshotCommand);
    }

    [AvaloniaFact]
    public async Task PasteImageAsync_handles_empty_clipboard_gracefully()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        using var vm = new HomePageViewModel(store, loc, platform);

        await vm.PasteImageAsync();
        Assert.False(vm.IsPasting);
    }

    [AvaloniaFact]
    public async Task CaptureScreenshotAsync_handles_completion_or_cancellation_gracefully()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        using var vm = new HomePageViewModel(store, loc, platform);

        await vm.CaptureScreenshotAsync();
        Assert.False(vm.IsCapturing);
    }

    [AvaloniaFact]
    public async Task BrowseFilesAsync_handles_no_window_or_cancel_gracefully()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var loc = AppHost.Get<ILocalizer>();
        var platform = AppHost.Get<IPlatformServices>();

        using var vm = new HomePageViewModel(store, loc, platform);

        await vm.BrowseFilesAsync();
        Assert.False(vm.IsBrowsing);
    }
}
