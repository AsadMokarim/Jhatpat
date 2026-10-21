// Tests for extended multimedia and document tool ViewModels.

using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Pages;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Photo;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class ExtendedMediaViewModelsTests
{
    [AvaloniaFact]
    public void PdfMergeViewModel_manages_document_order()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        var tempA = Path.GetTempFileName() + ".pdf";
        var tempB = Path.GetTempFileName() + ".pdf";
        File.WriteAllBytes(tempA, [0x25, 0x50, 0x44, 0x46]); // %PDF
        File.WriteAllBytes(tempB, [0x25, 0x50, 0x44, 0x46]); // %PDF

        try
        {
            var vm = new PdfMergeViewModel([tempA, tempB], null, loc, store);

            Assert.Equal(2, vm.Items.Count);
            Assert.True(vm.CanMerge);

            // Move item 1 up
            var item1 = vm.Items[1];
            vm.MoveUpCommand.Execute(item1);
            Assert.Equal(tempB, vm.Items[0].FilePath);

            // Move item 0 down
            vm.MoveDownCommand.Execute(vm.Items[0]);
            Assert.Equal(tempB, vm.Items[1].FilePath);

            // Remove item
            vm.RemoveItemCommand.Execute(vm.Items[0]);
            Assert.Single(vm.Items);
            Assert.False(vm.CanMerge);
        }
        finally
        {
            try { File.Delete(tempA); } catch { }
            try { File.Delete(tempB); } catch { }
        }
    }

    [AvaloniaFact]
    public void PdfOrganizeViewModel_handles_page_manipulation()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        using var vm = new PdfOrganizeViewModel("dummy.pdf", null, loc, store);

        // Populate dummy pages
        vm.Pages.Add(new PdfPageItemViewModel(0, 1));
        vm.Pages.Add(new PdfPageItemViewModel(1, 2));
        vm.Pages.Add(new PdfPageItemViewModel(2, 3));

        // Move page 2 (index 1) left
        vm.MoveLeftCommand.Execute(vm.Pages[1]);
        Assert.Equal(1, vm.Pages[0].OriginalPageIndex);

        // Rotate CW
        vm.RotateClockwiseCommand.Execute(vm.Pages[0]);
        Assert.Equal(90, vm.Pages[0].RotationDegrees);

        // Rotate CCW
        vm.RotateCounterClockwiseCommand.Execute(vm.Pages[0]);
        Assert.Equal(0, vm.Pages[0].RotationDegrees);

        // Delete page
        vm.DeletePageCommand.Execute(vm.Pages[0]);
        Assert.Equal(2, vm.Pages.Count);
    }

    [AvaloniaFact]
    public void EditPhotoViewModel_adjustments_and_reset()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        using var vm = new EditPhotoViewModel("sample.png", null, loc, store);

        vm.Brightness = 35;
        vm.Contrast = 20;
        vm.Temperature = -15;

        Assert.Equal(35, vm.Brightness);
        Assert.Equal(20, vm.Contrast);
        Assert.Equal(-15, vm.Temperature);

        vm.ResetAllCommand.Execute(null);

        Assert.Equal(0, vm.Brightness);
        Assert.Equal(0, vm.Contrast);
        Assert.Equal(0, vm.Temperature);
    }

    [AvaloniaFact]
    public void CreateCollageViewModel_layout_and_options()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        var tempA = Path.GetTempFileName() + ".png";
        var tempB = Path.GetTempFileName() + ".png";
        File.WriteAllBytes(tempA, [0x89, 0x50, 0x4E, 0x47]); // PNG header
        File.WriteAllBytes(tempB, [0x89, 0x50, 0x4E, 0x47]);

        try
        {
            using var vm = new CreateCollageViewModel([tempA, tempB], null, loc, store);

            Assert.Equal(2, vm.Photos.Count);

            vm.SetLayoutCommand.Execute("Featured");
            Assert.Equal(CollageLayout.Featured, vm.Layout);

            vm.SetAspectRatioCommand.Execute("16:9");
            Assert.InRange(vm.AspectRatio, 1.77, 1.78);

            vm.RemovePhotoCommand.Execute(vm.Photos[0]);
            Assert.Single(vm.Photos);
        }
        finally
        {
            try { File.Delete(tempA); } catch { }
            try { File.Delete(tempB); } catch { }
        }
    }

    [AvaloniaFact]
    public void AnnotateViewModel_tools_and_history()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        using var vm = new AnnotateViewModel("annot.png", null, loc, store);

        Assert.Equal(AnnotationTool.Pen, vm.ActiveTool);

        vm.SetToolCommand.Execute("Arrow");
        Assert.Equal(AnnotationTool.Arrow, vm.ActiveTool);

        // Verify clean canvas has no undo
        Assert.False(vm.CanUndo);
    }

    [AvaloniaFact]
    public void VideoTrimViewModel_updates_trim_times()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        using var vm = new VideoTrimViewModel("clip.mp4", null, loc, store);

        vm.EndTimeSeconds = 15.0;
        vm.StartTimeSeconds = 5.0;

        Assert.Equal(10.0, vm.EndTimeSeconds - vm.StartTimeSeconds);
        Assert.Contains("00:10", vm.TrimDurationText);

        Assert.False(vm.IsPlaying);
        vm.TogglePlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        vm.TogglePlayCommand.Execute(null);
        Assert.False(vm.IsPlaying);
    }

    [AvaloniaFact]
    public void VideoToolsViewModel_tabs_and_speed()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        using var vm = new VideoToolsViewModel(["video.mp4"], null, loc, store);

        Assert.Equal("Crop", vm.ActiveTab);

        vm.SelectTabCommand.Execute("Speed");
        Assert.Equal("Speed", vm.ActiveTab);

        vm.SetSpeedCommand.Execute(2.0);
        Assert.Equal(2.0, vm.Speed);

        vm.SelectTabCommand.Execute("Join");
        Assert.Equal("Join", vm.ActiveTab);
    }

    [AvaloniaFact]
    public void AudioToolsViewModel_modes_and_presets()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        using var vm = new AudioToolsViewModel("audio.mp3", null, loc, store);

        Assert.True(vm.IsTabNormalize);
        Assert.False(vm.IsTabTrim);

        vm.SelectTabCommand.Execute("Trim");
        Assert.False(vm.IsTabNormalize);
        Assert.True(vm.IsTabTrim);

        vm.SelectTabCommand.Execute("Normalize");
        Assert.True(vm.IsTabNormalize);

        // Podcast preset (-16 LUFS)
        vm.SetStandardLufsCommand.Execute("-16");
        Assert.Equal(-16.0, vm.TargetLufs);
    }

    [AvaloniaFact]
    public void PdfMergeViewModel_supports_images_and_pdf_documents()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        var tempPng = Path.GetTempFileName() + ".png";
        var tempPdf = Path.GetTempFileName() + ".pdf";

        using (var bmp = new SKBitmap(50, 50))
        using (var img = SKImage.FromBitmap(bmp))
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.OpenWrite(tempPng))
        {
            data.SaveTo(stream);
        }

        File.WriteAllBytes(tempPdf, [0x25, 0x50, 0x44, 0x46]); // %PDF

        try
        {
            var vm = new PdfMergeViewModel([tempPng, tempPdf], null, loc, store);

            Assert.Equal(2, vm.Items.Count);
            Assert.False(vm.Items[0].IsPdf);
            Assert.Contains("Image", vm.Items[0].DetailsText);
            Assert.True(vm.Items[1].IsPdf);
            Assert.True(vm.CanMerge);
            Assert.True(PdfMergeViewModel.IsSupportedMergeFile(tempPng));
            Assert.True(PdfMergeViewModel.IsSupportedMergeFile(tempPdf));
        }
        finally
        {
            try { File.Delete(tempPng); } catch { }
            try { File.Delete(tempPdf); } catch { }
        }
    }

    [AvaloniaFact]
    public void PresetAvailability_checks_tools_and_image_merge()
    {
        TestServices.EnsureInitialized("en");
        var availability = AppHost.Get<PresetAvailability>();
        var videoFmt = FormatRegistry.Get("mp4");
        var audioFmt = FormatRegistry.Get("mp3");
        var imageFmt = FormatRegistry.Get("png");
        var pdfFmt = FormatRegistry.Get("pdf");

        var trim = BuiltInData.CreatePresets().First(p => p.Id == "tool-video-trim");
        var videoTools = BuiltInData.CreatePresets().First(p => p.Id == "tool-video-tools");
        var audioTools = BuiltInData.CreatePresets().First(p => p.Id == "tool-audio-tools");
        var pdfOrganize = BuiltInData.CreatePresets().First(p => p.Id == "tool-pdf-organize");
        var mergePdf = BuiltInData.CreatePresets().First(p => p.Id == "merge-pdf");

        // Video tools
        Assert.Null(availability.Check(trim, [videoFmt]));
        Assert.NotNull(availability.Check(trim, [imageFmt]));
        Assert.Null(availability.Check(videoTools, [videoFmt]));

        // Audio tools
        Assert.Null(availability.Check(audioTools, [audioFmt]));
        Assert.Null(availability.Check(audioTools, [videoFmt])); // audio from video allowed

        // PDF organize
        Assert.Null(availability.Check(pdfOrganize, [pdfFmt]));
        Assert.Null(availability.Check(pdfOrganize, [imageFmt]));

        // Merge PDF with multiple images
        Assert.Null(availability.Check(mergePdf, [imageFmt, imageFmt]));
        Assert.Null(availability.Check(mergePdf, [imageFmt, pdfFmt]));
    }

    [AvaloniaFact]
    public void PresetsPageViewModel_includes_interactive_tools()
    {
        TestServices.EnsureInitialized("en");
        var loc = AppHost.Get<ILocalizer>();
        var store = AppHost.Get<UserDataStore>();
        var windows = AppHost.Get<WindowService>();

        var vm = new PresetsPageViewModel(store, loc, windows);

        Assert.Contains(vm.Presets, p => p.Preset.Id == "tool-video-trim");
        Assert.Contains(vm.Presets, p => p.Preset.Id == "tool-video-tools");
        Assert.Contains(vm.Presets, p => p.Preset.Id == "tool-audio-tools");
        Assert.Contains(vm.Presets, p => p.Preset.Id == "tool-pdf-organize");

        var trimItem = vm.Presets.First(p => p.Preset.Id == "tool-video-trim");
        Assert.Equal("Interactive Tool", trimItem.Target);
    }
}
