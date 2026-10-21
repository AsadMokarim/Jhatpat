// Drop zone: files dropped on the Home page open the donut (click mode) at the drop position.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Filee.App.Services;
using Microsoft.Extensions.Logging;

namespace Filee.App.Views.Pages;

public partial class HomePage : UserControl
{
    /// <summary>
    /// The first drag over the drop zone of this run is logged: a log without it after "drops don't work" means the
    /// drag never reached Filee (Windows refuses drops from Explorer into a Filee running as administrator).
    /// </summary>
    private static int _dragEnterLogged;

    public HomePage()
    {
        InitializeComponent();
        DragDrop.AddDragEnterHandler(DropZone, (_, e) =>
        {
            DropZone.Classes.Add("over");
            var files = DraggedFiles.Offered(e.DataTransfer);
            e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
            if (Interlocked.Exchange(ref _dragEnterLogged, 1) == 0)
                AppHost.Get<ILogger<HomePage>>().LogInformation("First drag over the drop zone (files: {Files})", files);
        });
        DragDrop.AddDragOverHandler(DropZone, (_, e) =>
            e.DragEffects = DraggedFiles.Offered(e.DataTransfer) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDragLeaveHandler(DropZone, (_, _) => DropZone.Classes.Remove("over"));
        DragDrop.AddDropHandler(DropZone, OnDrop);

        // Clicking on the drop zone empty area opens file browser
        DropZone.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(DropZone).Properties.IsLeftButtonPressed &&
                e.Source is not Button && !(e.Source is Visual visual && visual.FindAncestorOfType<Button>() is not null))
            {
                if (DataContext is ViewModels.Pages.HomePageViewModel vm)
                {
                    vm.BrowseFilesCommand.Execute(null);
                    e.Handled = true;
                }
            }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is ViewModels.Pages.HomePageViewModel vm)
        {
            var isCtrlOrCmd = (e.KeyModifiers & (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) != 0;
            if (isCtrlOrCmd && e.Key == Key.V)
            {
                vm.PasteImageCommand.Execute(null);
                e.Handled = true;
            }
            else if (isCtrlOrCmd && e.Key == Key.O)
            {
                vm.BrowseFilesCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("over");
        var files = DraggedFiles.Local(e.DataTransfer);
        if (files.Count == 0)
            return;

        e.DragEffects = DragDropEffects.Copy;
        var screenPoint = this.PointToScreen(e.GetPosition(this));
        AppHost.Get<RadialController>().ShowForFiles(screenPoint.X, screenPoint.Y, files);
    }
}
