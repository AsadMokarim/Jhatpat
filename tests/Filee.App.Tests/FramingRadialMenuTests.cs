using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;
using SkiaSharp;
using Xunit;

namespace Filee.App.Tests;

public class FramingRadialMenuTests
{
    private static string CreateSampleImage(string extension = ".png")
    {
        var temp = Path.Combine(Path.GetTempPath(), $"filee-frame-radial-{Guid.NewGuid():N}{extension}");
        using var bitmap = new SKBitmap(180, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkSlateBlue);
        using var stream = File.OpenWrite(temp);
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
        return temp;
    }

    [AvaloniaFact]
    public void Contextual_framing_is_present_and_enabled_for_supported_image_files()
    {
        TestServices.EnsureInitialized("en");
        var vm = AppHost.Get<RadialViewModel>();
        var files = new[] { @"C:\photos\sample.png", @"C:\photos\sample.jpg" };

        vm.Load(files, clickMode: true);

        // Framing item is present contextually and enabled
        var frame = Assert.Single(vm.Items, i => i.Id == "tool-frame");
        Assert.True(frame.IsEnabled);
        Assert.Null(frame.DisabledReason);
        Assert.Equal("Frame", frame.Label);

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
    public void Framing_action_is_not_offered_for_unsupported_file_types(string unsupportedFile)
    {
        TestServices.EnsureInitialized("en");
        var vm = AppHost.Get<RadialViewModel>();

        vm.Load([unsupportedFile], clickMode: true);

        Assert.DoesNotContain(vm.Items, i => i.Id == "tool-frame");
        Assert.DoesNotContain(vm.Items, i => i.Id == "image-tools");
    }

    [AvaloniaFact]
    public void Framing_action_is_not_offered_for_mixed_unsupported_files()
    {
        TestServices.EnsureInitialized("en");
        var vm = AppHost.Get<RadialViewModel>();

        vm.Load([@"C:\photos\photo.png", @"C:\docs\doc.pdf"], clickMode: true);

        Assert.DoesNotContain(vm.Items, i => i.Id == "tool-frame");
        Assert.DoesNotContain(vm.Items, i => i.Id == "image-tools");
    }

    [AvaloniaFact]
    public void When_radial_menu_has_insufficient_space_image_tools_submenu_contains_framing()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var images = store.Profiles.Single(p => p.Id == "images");
        var before = images.PresetIds.ToList();

        try
        {
            images.PresetIds = ["to-png", "to-jpg", "to-webp", "to-pdf", "trace-svg", "trace-line-art", "to-tiff", "to-bmp", "to-ico", "half-size", "to-heic", "to-gif"];
            Assert.Equal(ToolbarProfile.MaxSlices, images.PresetIds.Count);

            var vm = AppHost.Get<RadialViewModel>();
            vm.Load([@"C:\photos\photo.jpg"], clickMode: true);

            Assert.InRange(vm.Items.Count, 1, ToolbarProfile.MaxSlices);

            var toolsItem = Assert.Single(vm.Items, i => i.Id == "image-tools");
            Assert.True(toolsItem.IsGroup);
            Assert.True(toolsItem.IsEnabled);
            Assert.NotNull(toolsItem.Children);
            Assert.Contains(toolsItem.Children!, c => c.Id == "tool-frame" && c.IsEnabled);
        }
        finally
        {
            images.PresetIds.Clear();
            images.PresetIds.AddRange(before);
        }
    }

    [AvaloniaFact]
    public void Activating_framing_slice_routes_to_framing_overlay_window()
    {
        TestServices.EnsureInitialized("en");
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var vm = AppHost.Get<RadialViewModel>();
        var tempFile = CreateSampleImage(".png");

        try
        {
            vm.Load([tempFile], clickMode: true);
            var frameIndex = vm.Items.ToList().FindIndex(i => i.Id == "tool-frame");
            Assert.True(frameIndex >= 0, "Frame action should be in radial menu");

            radial.Convert(frameIndex, -1, [tempFile]);

            Assert.NotNull(windows.FramingOverlay);
            var overlayVm = Assert.IsType<FramingOverlayViewModel>(windows.FramingOverlay.DataContext);

            // Verify framing options and live properties
            Assert.Equal(48, overlayVm.Padding);
            Assert.Equal(16.0, overlayVm.CornerRadius);
            Assert.True(overlayVm.HasShadow);

            // Verify aspect ratio switching
            overlayVm.SetAspectCommand.Execute("1:1");
            Assert.True(overlayVm.IsAspectSquare);

            overlayVm.SetAspectCommand.Execute("16:9");
            Assert.True(overlayVm.IsAspect169);

            windows.FramingOverlay.Close();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [AvaloniaFact]
    public void Activating_framing_from_image_tools_submenu_routes_to_framing_overlay_window()
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
            images.PresetIds = ["to-png", "to-jpg", "to-webp", "to-pdf", "trace-svg", "trace-line-art", "to-tiff", "to-bmp", "to-ico", "half-size", "to-heic", "to-gif"];
            vm.Load([tempFile], clickMode: true);

            var groupIndex = vm.Items.ToList().FindIndex(i => i.Id == "image-tools");
            Assert.True(groupIndex >= 0, "Image tools group should be present");

            var subIndex = vm.Items[groupIndex].Children!.ToList().FindIndex(c => c.Id == "tool-frame");
            Assert.True(subIndex >= 0, "tool-frame should be inside image tools group");

            radial.Convert(groupIndex, subIndex, [tempFile]);

            Assert.NotNull(windows.FramingOverlay);
            Assert.IsType<FramingOverlayViewModel>(windows.FramingOverlay.DataContext);

            windows.FramingOverlay.Close();
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
