// Home: drop zone (handled in the view) and recent conversions.

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Conversion;
using Filee.Core.History;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.ViewModels.Pages;

public sealed partial class HistoryItemViewModel : ObservableObject
{
    private readonly HistoryEntry _entry;
    private readonly IPlatformServices _platform;
    private readonly ConversionService? _conversions;
    private readonly Action<HistoryEntry>? _onRemove;

    public HistoryItemViewModel(HistoryEntry entry, ILocalizer loc, IPlatformServices platform, ConversionService? conversions = null, Action<HistoryEntry>? onRemove = null)
    {
        _entry = entry;
        _platform = platform;
        _conversions = conversions;
        _onRemove = onRemove;
        Title = entry.PresetName;
        Subtitle = entry.Sources.Count == 1 ? Path.GetFileName(entry.Sources[0]) : loc.Format("home.files", entry.Sources.Count);
        Time = entry.FinishedAt.Date == DateTime.Today ? entry.FinishedAt.ToString("t") : entry.FinishedAt.ToString("d");
        Failures = entry.Failures.Count > 0
            ? entry.Failures.Select(f => new FailureItem(Path.GetFileName(f.Source), FailureText.Reason(loc, f.ErrorKey, f.ErrorDetail))).ToList()
            : entry.Errors.Select(line => FailureText.FromLegacy(loc, line)).ToList();
        HasErrors = Failures.Count > 0 || entry.State == JobState.Failed;
        FailureSummary = loc.Format("home.failed_files", Failures.Count);
        HasOutputs = entry.Outputs.Any(ConversionService.Exists);
        PrimaryOutput = entry.Outputs.FirstOrDefault(ConversionService.Exists);
        CanOpenOutput = PrimaryOutput is not null && File.Exists(PrimaryOutput);
        // Only entries that remember their preset (Filee 1.8 and later) and still have a failed file to convert.
        CanRetry = conversions is not null && entry.Preset is not null && entry.Failures.Any(f => ConversionService.Exists(f.Source));
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Time { get; }
    public bool HasErrors { get; }
    public bool HasOutputs { get; }
    public string? PrimaryOutput { get; }
    public bool CanOpenOutput { get; }

    /// <summary>The files that failed and why.</summary>
    public IReadOnlyList<FailureItem> Failures { get; }

    public bool HasFailures => Failures.Count > 0;

    /// <summary>"2 files failed": the button that shows the list.</summary>
    public string FailureSummary { get; }

    public bool CanRetry { get; }

    [ObservableProperty] private bool _showFailures;

    [RelayCommand]
    private void ToggleFailures() => ShowFailures = !ShowFailures;

    /// <summary>Converts the failed files again the way the job ran them; progress shows in the toast.</summary>
    [RelayCommand]
    private void Retry()
    {
        if (_conversions is not null && _entry.Preset is not null)
            _conversions.Retry(_entry.Failures.Select(f => f.Source), _entry.Preset, _entry.WatchRuleId);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        // An output may be a folder ("Extract"): revealing it opens the extracted files.
        var output = _entry.Outputs.FirstOrDefault(ConversionService.Exists);
        if (output is not null)
            _platform.RevealInFileManager(output);
    }

    internal static System.Diagnostics.ProcessStartInfo CreateOpenProcessStartInfo(string path)
    {
        if (OperatingSystem.IsWindows())
            return new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };

        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var start = new System.Diagnostics.ProcessStartInfo(opener) { UseShellExecute = false };
        start.ArgumentList.Add(path);
        return start;
    }

    [RelayCommand]
    private void OpenOutput()
    {
        if (PrimaryOutput is not null && File.Exists(PrimaryOutput))
        {
            try
            {
                var start = CreateOpenProcessStartInfo(PrimaryOutput);
                System.Diagnostics.Process.Start(start)?.Dispose();
            }
            catch
            {
                _platform.RevealInFileManager(PrimaryOutput);
            }
        }
    }

    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(_entry);
}

public sealed partial class HomePageViewModel : ObservableObject, IDisposable
{
    private readonly UserDataStore _store;
    private readonly ILocalizer _loc;
    private readonly IPlatformServices _platform;
    private readonly Services.Triggers.TriggerService? _triggers;
    private readonly ConversionService? _conversions;
    private readonly ElevationService? _elevation;

    public HomePageViewModel(UserDataStore store, ILocalizer loc, IPlatformServices platform, Services.Triggers.TriggerService? triggers = null,
        ConversionService? conversions = null, ElevationService? elevation = null)
    {
        _store = store;
        _loc = loc;
        _platform = platform;
        _triggers = triggers;
        _conversions = conversions;
        _elevation = elevation;
        if (triggers is not null)
            triggers.StatusChanged += OnTriggerStatusChanged;
        if (elevation is not null)
            elevation.PropertyChanged += OnElevationChanged;
        var drag = store.Settings.Triggers.FirstOrDefault(t => t.Enabled && t.Kind == TriggerKind.Drag);
        DropHint = loc.Format(OperatingSystem.IsMacOS() ? "home.drop_hint_macos" : OperatingSystem.IsLinux() ? "home.drop_hint_linux" : "home.drop_hint", drag is null ? "—" : GestureText.Modifiers(drag.Modifiers));
        store.HistoryChanged += OnHistoryChanged;
        Reload();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reload);

    public void Dispose()
    {
        _store.HistoryChanged -= OnHistoryChanged;
        if (_triggers is not null)
            _triggers.StatusChanged -= OnTriggerStatusChanged;
        if (_elevation is not null)
            _elevation.PropertyChanged -= OnElevationChanged;
    }

    private void OnTriggerStatusChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(NeedsAccessibility));

    private void OnElevationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(ElevationRestartFailed));

    /// <summary>
    /// Windows: Filee runs as administrator while Explorer doesn't, so drops, the Explorer menu and the shortcut can't
    /// reach it (the warning above the drop zone).
    /// </summary>
    public bool IsElevated => _elevation?.ShellCannotReachFilee == true;

    /// <summary>The restart button didn't work: the warning says what to do instead.</summary>
    public bool ElevationRestartFailed => _elevation?.RestartFailed == true;

    [RelayCommand]
    private void RestartWithoutAdministratorRights() => _elevation?.RestartWithoutAdministratorRights();

    /// <summary>macOS: gestures wait for the Accessibility permission (the reminder above the drop zone).</summary>
    public bool NeedsAccessibility => _triggers?.NeedsPermission == true;

    [RelayCommand]
    private void OpenAccessibility()
    {
        if (OperatingSystem.IsMacOS() && _platform is Filee.Platform.MacOS.MacOSPlatformServices mac)
            mac.OpenAccessibilitySettings();
    }

    public string DropHint { get; }

    /// <summary>How many conversions the page lists: the newest, or the newest that match the search.</summary>
    internal const int Shown = 40;

    /// <summary>The conversions listed (each looks for its outputs on disk, so only <see cref="Shown"/> of them).</summary>
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    /// <summary>
    /// What was typed in the search box: the list shows the conversions whose preset or a source file name contains
    /// it, from the whole history (<see cref="AppSettings.HistoryLimit"/>), not just the ones listed without it.
    /// </summary>
    [ObservableProperty] private string _search = "";

    partial void OnSearchChanged(string value) => Reload();

    /// <summary>No history at all: no search box, and the hint that converted files show up here.</summary>
    [ObservableProperty] private bool _isEmpty;

    /// <summary>A search that matches nothing.</summary>
    [ObservableProperty] private bool _noMatches;

    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _isPasting;
    [ObservableProperty] private bool _isBrowsing;

    public bool IsScreenshotSupported => AppHost.Get<IScreenshotService>().IsSupported;

    private static PixelPoint GetMainWindowCenter(out Window? mainWindow)
    {
        mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow is not null && mainWindow.IsVisible)
        {
            return mainWindow.PointToScreen(new Point(mainWindow.Bounds.Width / 2, mainWindow.Bounds.Height / 2));
        }
        return new PixelPoint(0, 0);
    }

    [RelayCommand]
    public async Task PasteImageAsync()
    {
        if (IsPasting)
            return;

        IsPasting = true;
        try
        {
            var clipboard = AppHost.Get<IClipboardService>();
            var path = await clipboard.PasteImageToTempFileAsync();
            if (path is not null && File.Exists(path))
            {
                var radial = AppHost.Get<RadialController>();
                var center = GetMainWindowCenter(out _);
                radial.ShowForFiles(center.X, center.Y, [path]);
            }
            else
            {
                AppHost.Get<WindowService>().ShowToastNotification(_loc["clipboard.no_image_in_clipboard"]);
            }
        }
        catch (Exception)
        {
            AppHost.Get<WindowService>().ShowToastNotification(_loc["clipboard.paste_failed"]);
        }
        finally
        {
            IsPasting = false;
        }
    }

    [RelayCommand]
    public async Task CaptureScreenshotAsync()
    {
        if (IsCapturing)
            return;

        IsCapturing = true;
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var main = desktop?.MainWindow;
        var previousState = main?.WindowState ?? WindowState.Normal;

        try
        {
            if (main is not null && main.IsVisible)
            {
                main.WindowState = WindowState.Minimized;
                await Task.Delay(300);
            }

            var screenshot = AppHost.Get<IScreenshotService>();
            var path = await screenshot.CaptureInteractiveAsync();

            if (main is not null && main.IsVisible)
            {
                main.WindowState = previousState;
                main.Show();
                main.Activate();
            }

            if (path is not null && File.Exists(path))
            {
                var radial = AppHost.Get<RadialController>();
                var center = GetMainWindowCenter(out _);
                radial.ShowForFiles(center.X, center.Y, [path]);
            }
        }
        catch (OperationCanceledException)
        {
            if (main is not null && main.IsVisible)
            {
                main.WindowState = previousState;
                main.Show();
                main.Activate();
            }
        }
        catch (Exception)
        {
            if (main is not null && main.IsVisible)
            {
                main.WindowState = previousState;
                main.Show();
                main.Activate();
            }
            AppHost.Get<WindowService>().ShowToastNotification(_loc["screenshot.failed"]);
        }
        finally
        {
            IsCapturing = false;
        }
    }

    [RelayCommand]
    public async Task BrowseFilesAsync()
    {
        if (IsBrowsing)
            return;

        IsBrowsing = true;
        try
        {
            var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var main = desktop?.MainWindow;
            var topLevel = main is not null ? TopLevel.GetTopLevel(main) : null;
            if (topLevel is null)
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _loc["home.browse_title"],
                AllowMultiple = true
            });

            if (files is { Count: > 0 })
            {
                var paths = files
                    .Select(f => f.Path.LocalPath)
                    .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
                    .ToList();

                if (paths.Count > 0)
                {
                    var radial = AppHost.Get<RadialController>();
                    var center = GetMainWindowCenter(out _);
                    radial.ShowForFiles(center.X, center.Y, paths);
                }
            }
        }
        catch (Exception)
        {
            // Cancelled or dismissed
        }
        finally
        {
            IsBrowsing = false;
        }
    }

    private void Reload()
    {
        var query = Search.Trim();
        // The entries are picked first, then built: building one looks for its outputs on disk.
        var entries = _store.History
            .Where(entry => query.Length == 0 || Matches(entry, query))
            .Take(Shown)
            .ToList();
        History.Clear();
        foreach (var entry in entries)
            History.Add(new HistoryItemViewModel(entry, _loc, _platform, _conversions, e => { _store.RemoveHistory(e.JobId); Reload(); }));
        IsEmpty = _store.History.Count == 0;
        NoMatches = !IsEmpty && History.Count == 0;
    }

    private static bool Matches(HistoryEntry entry, string query)
    {
        return Contains(entry.PresetName) || entry.Sources.Any(source => Contains(Path.GetFileName(source)));

        bool Contains(string? text) => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        Search = "";
        _store.ClearHistory();
        Reload();
    }
}
