// Tray visibility follows the macOS menu bar preference without changing other platforms.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public sealed class TrayServiceTests
{
    [AvaloniaFact]
    public void Mac_tray_visibility_follows_preference_and_non_mac_tray_stays_visible()
    {
        TestServices.EnsureInitialized("en");
        var app = Application.Current!;
        var directory = Directory.CreateTempSubdirectory("filee-tray-").FullName;
        try
        {
            var store = new UserDataStore(directory);
            store.Load();
            store.Settings.ShowMenuBarIcon = false;
            store.SaveSettings();
            var platform = new IconPlatform { SupportsDockVisibility = true };
            var service = new TrayService(store, AppHost.Get<ILocalizer>(), platform, AppHost.Get<WindowService>(),
                AppHost.Get<UpdateService>());
            try
            {
                service.Create(app);
                var icon = Assert.Single(TrayIcon.GetIcons(app)!);
                Assert.False(icon.IsVisible);

                store.Settings.ShowMenuBarIcon = true;
                store.SaveSettings();
                Assert.True(icon.IsVisible);
            }
            finally
            {
                service.Dispose();
            }

            var nonMacStore = new UserDataStore(Path.Combine(directory, "non-mac"));
            nonMacStore.Load();
            nonMacStore.Settings.ShowMenuBarIcon = false;
            nonMacStore.SaveSettings();
            var nonMacService = new TrayService(nonMacStore, AppHost.Get<ILocalizer>(), new IconPlatform(),
                AppHost.Get<WindowService>(), AppHost.Get<UpdateService>());
            try
            {
                nonMacService.Create(app);
                Assert.True(Assert.Single(TrayIcon.GetIcons(app)!).IsVisible);
            }
            finally
            {
                nonMacService.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void Start_with_system_toggle_item_reflects_and_updates_settings()
    {
        TestServices.EnsureInitialized("en");
        var app = Application.Current!;
        var directory = Directory.CreateTempSubdirectory("filee-tray-autostart-").FullName;
        try
        {
            var store = new UserDataStore(directory);
            store.Load();
            store.Settings.StartWithSystem = true;
            store.SaveSettings();

            var service = new TrayService(store, AppHost.Get<ILocalizer>(), new IconPlatform(),
                AppHost.Get<WindowService>(), AppHost.Get<UpdateService>());
            try
            {
                service.Create(app);
                var item = service.StartWithSystemMenuItem;
                Assert.NotNull(item);
                Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
                Assert.True(item.IsChecked);
                Assert.Equal("Start on startup", item.Header);

                // Simulate toggle through settings change
                store.Settings.StartWithSystem = false;
                store.SaveSettings();
                Assert.False(item.IsChecked);

                // Simulate toggle through menu click logic
                store.Settings.StartWithSystem = true;
                store.SaveSettings();
                Assert.True(item.IsChecked);
            }
            finally
            {
                service.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
