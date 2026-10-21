// PDF tool engine: merging documents, organizing pages, page rotation and thumbnail rendering.

using Filee.Engines.Infrastructure;
using PDFtoImage;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace Filee.Engines.Pdf;

public sealed record OrganizedPageInfo(int SourcePageIndex, int RotationDegrees = 0);

public static class PdfToolEngine
{
    /// <summary>
    /// Merges multiple PDF files in the specified order into one output file.
    /// </summary>
    public static void MergePdfFiles(IReadOnlyList<string> inputFiles, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);
        ArgumentNullException.ThrowIfNull(outputPath);

        if (inputFiles.Count == 0)
            throw new ArgumentException("At least one input file must be specified.", nameof(inputFiles));

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var output = new PdfDocument();
        foreach (var input in inputFiles)
        {
            if (!File.Exists(input))
                continue;

            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            for (var i = 0; i < source.PageCount; i++)
            {
                output.AddPage(source.Pages[i]);
            }
        }

        output.Save(outputPath);
    }

    /// <summary>
    /// Reads the total page count of a PDF document.
    /// </summary>
    public static int GetPageCount(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            return 0;

        try
        {
            var bytes = File.ReadAllBytes(pdfPath);
            return Conversion.GetPageCount(bytes, null);
        }
        catch
        {
            try
            {
                using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                return doc.PageCount;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Renders thumbnail images for all pages of a PDF document.
    /// </summary>
    public static List<SKBitmap> RenderPageThumbnails(string pdfPath, int targetHeight = 240)
    {
        var thumbnails = new List<SKBitmap>();
        if (!File.Exists(pdfPath))
            return thumbnails;

        try
        {
            var bytes = File.ReadAllBytes(pdfPath);
            var pageCount = Conversion.GetPageCount(bytes, null);
            var pages = Enumerable.Range(0, pageCount).ToList();
            var options = new RenderOptions(Dpi: 72, WithAnnotations: true, WithFormFill: true, BackgroundColor: SKColors.White);

            foreach (var img in Conversion.ToImages(bytes, pages, null, options))
            {
                // Scale down to thumbnail size for fast UI rendering
                if (img.Height > targetHeight)
                {
                    var scale = (float)targetHeight / img.Height;
                    var targetWidth = Math.Max(1, (int)(img.Width * scale));
                    var scaled = new SKBitmap(targetWidth, targetHeight, img.ColorType, img.AlphaType);
                    using var canvas = new SKCanvas(scaled);
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(img, new SKRect(0, 0, targetWidth, targetHeight));
                    img.Dispose();
                    thumbnails.Add(scaled);
                }
                else
                {
                    thumbnails.Add(img);
                }
            }
        }
        catch
        {
            // Fallback for corrupted/encrypted PDFs
        }

        return thumbnails;
    }

    /// <summary>
    /// Renders a thumbnail for a single page of a PDF document.
    /// </summary>
    public static SKBitmap? RenderSinglePageThumbnail(string pdfPath, int pageIndex, int targetHeight = 240)
    {
        if (!File.Exists(pdfPath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(pdfPath);
            var options = new RenderOptions(Dpi: 72, WithAnnotations: true, WithFormFill: true, BackgroundColor: SKColors.White);
            var img = Conversion.ToImage(bytes, pageIndex, null, options);
            if (img is null)
                return null;

            if (img.Height > targetHeight)
            {
                var scale = (float)targetHeight / img.Height;
                var targetWidth = Math.Max(1, (int)(img.Width * scale));
                var scaled = new SKBitmap(targetWidth, targetHeight, img.ColorType, img.AlphaType);
                using var canvas = new SKCanvas(scaled);
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(img, new SKRect(0, 0, targetWidth, targetHeight));
                img.Dispose();
                return scaled;
            }

            return img;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves a reorganized, reordered, and rotated selection of pages into a new PDF.
    /// </summary>
    public static void SaveOrganizedPdf(string inputPdf, IReadOnlyList<OrganizedPageInfo> pages, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(inputPdf);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(outputPath);

        if (!File.Exists(inputPdf))
            throw new FileNotFoundException("Source PDF not found.", inputPdf);

        if (pages.Count == 0)
            throw new ArgumentException("At least one page must be specified.", nameof(pages));

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var source = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();

        foreach (var pageInfo in pages)
        {
            if (pageInfo.SourcePageIndex < 0 || pageInfo.SourcePageIndex >= source.PageCount)
                continue;

            var sourcePage = source.Pages[pageInfo.SourcePageIndex];
            var newPage = output.AddPage(sourcePage);

            var rotation = (sourcePage.Rotate + pageInfo.RotationDegrees) % 360;
            if (rotation < 0) rotation += 360;
            newPage.Rotate = rotation;
        }

        output.Save(outputPath);
    }
}
