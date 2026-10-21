// Decides whether a preset can run for a set of files and why not — used to grey out donut slices.

using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;

namespace Filee.App.Services;

public sealed class PresetAvailability(ConverterCatalog catalog, ILocalizer loc, EngineDownloadService downloads)
{
    private static readonly HashSet<string> WritableSingleFrameRasterFormats =
    [
        "jpg", "png", "webp", "bmp", "avif", "jxl", "jp2", "tga", "qoi", "dds", "pcx",
    ];
    public static bool IsWritableSingleFrameRaster(string formatId) =>
        WritableSingleFrameRasterFormats.Contains(formatId.ToLowerInvariant());

    public bool CanCrop(IReadOnlyList<FileFormat> formats) =>
        formats.Count > 0 && formats.All(f => WritableSingleFrameRasterFormats.Contains(f.Id));

    /// <summary>
    /// Returns <c>null</c> when at least one of <paramref name="formats"/> can be converted with the preset,
    /// otherwise a localized reason — naming the optional engine to download when that would help.
    /// </summary>
    public string? Check(Preset preset, IReadOnlyList<FileFormat> formats)
    {
        if (preset.IsTool)
        {
            if (formats.Count == 0)
                return loc["error.unsupported_source"];

            switch (preset.Id)
            {
                case "tool-video-trim":
                case "tool-video-tools":
                    if (formats.All(f => f.Category == FormatCategory.Video))
                        return null;
                    return loc["error.unsupported_source"];

                case "tool-audio-tools":
                    if (formats.All(f => f.Category == FormatCategory.Audio || f.Category == FormatCategory.Video))
                        return null;
                    return loc["error.unsupported_source"];

                case "tool-pdf-organize":
                    if (formats.All(f => f.Id == "pdf" || f.Category == FormatCategory.Pdf || f.Category == FormatCategory.Image || IsWritableSingleFrameRaster(f.Id)))
                        return null;
                    return loc["error.unsupported_source"];

                case "tool-crop":
                case "tool-frame":
                case "tool-resize":
                case "tool-compression":
                    if (CanCrop(formats))
                        return null;
                    return loc["error.unsupported_source"];

                case "tool-collage":
                case "tool-photo-edit":
                case "tool-annotate":
                default:
                    if (CanCrop(formats) || formats.All(f => f.Category == FormatCategory.Image || IsWritableSingleFrameRaster(f.Id)))
                        return null;
                    return loc["error.unsupported_source"];
            }
        }

        // "Compress into one archive" packs the files as they are, so any file works, even of an unknown type.
        if (preset.PacksIntoOneArchive)
        {
            var archive = FormatRegistry.Get(preset.TargetFormat).Id;
            return catalog.All.OfType<IFileCombiner>().Any(c => c.CanCombineInto(archive)) ? null : loc["error.no_route"];
        }

        if (formats.Count == 0)
            return loc["error.unsupported_source"];

        var merge = (preset.Pdf.MergeIntoSingle || preset.Id is "merge-pdf" or "tool-pdf-merge") && preset.TargetFormat == "pdf";
        var turns = preset.Image.TurnsOrMirrors;
        string TargetFor(FileFormat f) => preset.TargetFormat == BuiltInData.SameAsSource ? f.Id : preset.TargetFormat;
        var copiesUnturned = false;
        bool Possible(RoutePlanner planner) => formats.Any(f =>
        {
            var target = TargetFor(f);
            if (merge && (f.Id == "pdf" || f.Category == FormatCategory.Pdf || f.Category == FormatCategory.Image || IsWritableSingleFrameRaster(f.Id)) && !turns)
                return true; // merged as it is
            var frames = preset.SavesFramesOf(f.Id);
            if (!turns)
                return planner.CanConvert(f.Id, target, frames);
            // Rotate / flip: a route only counts when an engine on it turns images (not a PDF or a video copied).
            if (planner.Plan(f.Id, target, frames) is not { } route)
                return false;
            if (route.FirstTransformStep() >= 0)
                return true;
            copiesUnturned = true;
            return false;
        });

        if (Possible(catalog.CreatePlanner()))
            return null;
        // It converts these files, just without turning them: say that, not "no engine" or "download one".
        if (copiesUnturned)
            return loc["error.cannot_turn"];
        return downloads.PackageEnabling(Possible) is { } package
            ? loc.Format("engine.reason.needs_package", package.Name)
            : loc["error.no_route"];
    }
}
