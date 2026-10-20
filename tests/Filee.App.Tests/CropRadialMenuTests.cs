using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class CropRadialMenuTests
{
    private static string CreateSampleImage(string extension = ".png")
    {
        var temp = Path.Combine(Path.GetTempPath(), $"filee-crop-radial-{Guid.NewGuid():N}{extension}");
        using var bitmap = new SKBitmap(200, 150);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var stream = File.OpenWrite(temp);
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
        return temp;
    }

    [AvaloniaFact]
    public void Contextual_crop_is_present_in_image_tools_and_promoted_for_supported_image_files()
    {
        TestServices.EnsureInitialized("en");
        var vm = AppHost.Get<RadialViewModel>();
        var files = new[] { @"C:\photos\a.png", @"C:\photos\b.jpg" };

        vm.Load(files, clickMode: true);

        // Crop is moved to image options (image-tools) - not directly on root ring
        Assert.DoesNotContain(vm.Items, i => i.Id == "tool-crop");

        // Image options group is present and promoted (larger slice)
        var tools = Assert.Single(vm.Items, i => i.Id == "image-tools");
        Assert.True(tools.IsEnabled);
        Assert.True(tools.IsPromoted);
        Assert.Equal("Image Options", tools.Label);
        Assert.NotNull(tools.Children);

        // Crop item is present inside Image Options and enabled
        var crop = Assert.Single(tools.Children!, c => c.Id == "tool-crop");
        Assert.True(crop.IsEnabled);
        Assert.Null(crop.DisabledReason);
        Assert.Equal("Crop", crop.Label);

        // Core conversion presets are all preserved
        Assert.Contains(vm.Items, i => i.Id == "to-png");
        Assert.Contains(vm.Items, i => i.Id == "to-jpg");
        Assert.Contains(vm.Items, i => i.Id == "to-webp");
        Assert.Contains(vm.Items, i => i.Id == "to-pdf");
    }

    [AvaloniaTheory]
    [InlineData(@"C:\docs\doc.pdf")]
    [InlineData(@"C:\vectors\graphic.svg")]
    [InlineData(@"C:\documents\paper.docx")]
    [InlineData(@"C:\movies\clip.mp4")]
    public void Crop_action_is_not_offered_for_unsupported_file_types(string unsupportedFile)
    {
        TestServices.EnsureInitialized("en");
        var vm = AppHost.Get<RadialViewModel>();

        vm.Load([unsupportedFile], clickMode: true);

        Assert.DoesNotContain(vm.Items, i => i.Id == "tool-crop");
        Assert.DoesNotContain(vm.Items, i => i.Id == "image-tools");
    }

    [AvaloniaFact]
    public void Crop_action_is_not_offered_for_mixed_unsupported_files()
    {
        TestServices.EnsureInitialized("en");
        var vm = AppHost.Get<RadialViewModel>();

        vm.Load([@"C:\photos\photo.png", @"C:\docs\doc.pdf"], clickMode: true);

        Assert.DoesNotContain(vm.Items, i => i.Id == "tool-crop");
        Assert.DoesNotContain(vm.Items, i => i.Id == "image-tools");
    }

    [AvaloniaFact]
    public void When_radial_menu_has_insufficient_space_image_tools_submenu_is_used()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var images = store.Profiles.Single(p => p.Id == "images");
        var before = images.PresetIds.ToList();

        try
        {
            // Max out slices to ToolbarProfile.MaxSlices (12)
            images.PresetIds = ["to-png", "to-jpg", "to-webp", "to-pdf", "trace-svg", "trace-line-art", "to-tiff", "to-bmp", "to-ico", "half-size", "to-heic", "to-gif"];
            Assert.Equal(ToolbarProfile.MaxSlices, images.PresetIds.Count);

            var vm = AppHost.Get<RadialViewModel>();
            vm.Load([@"C:\photos\pic.jpg"], clickMode: true);

            // Menu does not exceed maximum slice capacity
            Assert.InRange(vm.Items.Count, 1, ToolbarProfile.MaxSlices);

            // Slices should contain the Image Tools group submenu rather than overflowing
            var toolsItem = Assert.Single(vm.Items, i => i.Id == "image-tools");
            Assert.True(toolsItem.IsGroup);
            Assert.True(toolsItem.IsEnabled);
            Assert.NotNull(toolsItem.Children);
            Assert.Contains(toolsItem.Children!, c => c.Id == "tool-crop" && c.IsEnabled);
        }
        finally
        {
            images.PresetIds.Clear();
            images.PresetIds.AddRange(before);
        }
    }

    [AvaloniaFact]
    public void Activating_crop_slice_routes_to_crop_overlay_window()
    {
        TestServices.EnsureInitialized("en");
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var vm = AppHost.Get<RadialViewModel>();
        var tempFile = CreateSampleImage(".png");

        try
        {
            vm.Load([tempFile], clickMode: true);
            var groupIndex = vm.Items.ToList().FindIndex(i => i.Id == "image-tools");
            Assert.True(groupIndex >= 0, "Image tools group should be in radial menu");
            var subIndex = vm.Items[groupIndex].Children!.ToList().FindIndex(c => c.Id == "tool-crop");
            Assert.True(subIndex >= 0, "Crop action should be in image tools submenu");

            radial.Convert(groupIndex, subIndex, [tempFile]);

            Assert.NotNull(windows.CropOverlay);
            var overlayVm = Assert.IsType<CropOverlayViewModel>(windows.CropOverlay.DataContext);

            // Verify Milestone 1 aspect ratios are supported in the launched editor
            overlayVm.SetAspectCommand.Execute("9:16");
            Assert.True(overlayVm.IsAspect916);
            Assert.Equal("9:16", overlayVm.SelectedAspect);

            overlayVm.SetAspectCommand.Execute("4:5");
            Assert.True(overlayVm.IsAspect45);

            overlayVm.SetAspectCommand.Execute("3:2");
            Assert.True(overlayVm.IsAspect32);

            overlayVm.SetAspectCommand.Execute("2:3");
            Assert.True(overlayVm.IsAspect23);

            windows.CropOverlay.Close();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [AvaloniaFact]
    public void Activating_crop_from_image_tools_submenu_routes_to_crop_overlay_window()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var vm = AppHost.Get<RadialViewModel>();
        var images = store.Profiles.Single(p => p.Id == "images");
        var before = images.PresetIds.ToList();
        var tempFile = CreateSampleImage(".jpg");

        try
        {
            // Fill 12 slices so Image Tools submenu is used
            images.PresetIds = ["to-png", "to-jpg", "to-webp", "to-pdf", "trace-svg", "trace-line-art", "to-tiff", "to-bmp", "to-ico", "half-size", "to-heic", "to-gif"];
            vm.Load([tempFile], clickMode: true);

            var groupIndex = vm.Items.ToList().FindIndex(i => i.Id == "image-tools");
            Assert.True(groupIndex >= 0, "Image tools group should be present");

            var subIndex = vm.Items[groupIndex].Children!.ToList().FindIndex(c => c.Id == "tool-crop");
            Assert.True(subIndex >= 0, "tool-crop should be inside image tools group");

            radial.Convert(groupIndex, subIndex, [tempFile]);

            Assert.NotNull(windows.CropOverlay);
            Assert.IsType<CropOverlayViewModel>(windows.CropOverlay.DataContext);

            windows.CropOverlay.Close();
        }
        finally
        {
            images.PresetIds.Clear();
            images.PresetIds.AddRange(before);
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
