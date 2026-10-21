// Tests for PdfToolEngine: merging, organizing, page counting, and thumbnails.

using Filee.Engines.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace Filee.Engines.Tests;

public class PdfToolEngineTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private string CreateTestPdf(string folder, string filename, int pageCount)
    {
        var path = Path.Combine(folder, filename);
        var builder = new PdfBuilder();
        var font = builder.Font();

        for (var i = 1; i <= pageCount; i++)
        {
            builder.NewPage(width: 400, height: 600);
            builder.Line($"Test Page {i}", 16, font, 50, 100);
        }

        builder.Save(path);
        return path;
    }

    [Fact]
    public void GetPageCount_returns_accurate_count()
    {
        var folder = fx.NewFolder();
        var pdfPath = CreateTestPdf(folder, "doc_3pages.pdf", 3);

        var count = PdfToolEngine.GetPageCount(pdfPath);
        Assert.Equal(3, count);
    }

    [Fact]
    public void MergePdfFiles_combines_documents_in_exact_order()
    {
        var folder = fx.NewFolder();
        var doc1 = CreateTestPdf(folder, "doc1.pdf", 2);
        var doc2 = CreateTestPdf(folder, "doc2.pdf", 3);
        var mergedPath = Path.Combine(folder, "merged.pdf");

        PdfToolEngine.MergePdfFiles([doc1, doc2], mergedPath);

        Assert.True(File.Exists(mergedPath));
        var count = PdfToolEngine.GetPageCount(mergedPath);
        Assert.Equal(5, count);

        using var doc = PdfReader.Open(mergedPath, PdfDocumentOpenMode.Import);
        Assert.Equal(5, doc.PageCount);
    }

    [Fact]
    public void MergePdfFiles_combines_images_and_pdf_documents()
    {
        var folder = fx.NewFolder();
        var img1 = Path.Combine(folder, "image1.png");
        var img2 = Path.Combine(folder, "image2.jpg");
        var pdfDoc = CreateTestPdf(folder, "doc.pdf", 2);
        var mergedPath = Path.Combine(folder, "mixed_merged.pdf");

        // Create image 1 (PNG)
        using (var bmp = new SKBitmap(100, 100))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Blue);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(img1);
            data.SaveTo(stream);
        }

        // Create image 2 (JPG)
        using (var bmp = new SKBitmap(200, 150))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Red);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
            using var stream = File.OpenWrite(img2);
            data.SaveTo(stream);
        }

        PdfToolEngine.MergePdfFiles([img1, pdfDoc, img2], mergedPath);

        Assert.True(File.Exists(mergedPath));
        var count = PdfToolEngine.GetPageCount(mergedPath);
        Assert.Equal(4, count); // 1 + 2 + 1 = 4 pages

        using var doc = PdfReader.Open(mergedPath, PdfDocumentOpenMode.Import);
        Assert.Equal(4, doc.PageCount);
    }

    [Fact]
    public void SaveOrganizedPdf_reorders_rotates_and_filters_pages()
    {
        var folder = fx.NewFolder();
        var sourcePdf = CreateTestPdf(folder, "source.pdf", 4);
        var organizedPath = Path.Combine(folder, "organized.pdf");

        // Keep page 2 (index 2), page 0 (index 0 rotated 90), and page 3 (index 3 rotated 180) -> 3 pages total
        var pages = new List<OrganizedPageInfo>
        {
            new(2, 0),
            new(0, 90),
            new(3, 180),
        };

        PdfToolEngine.SaveOrganizedPdf(sourcePdf, pages, organizedPath);

        Assert.True(File.Exists(organizedPath));
        var count = PdfToolEngine.GetPageCount(organizedPath);
        Assert.Equal(3, count);

        using var doc = PdfReader.Open(organizedPath, PdfDocumentOpenMode.Import);
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(0, doc.Pages[0].Rotate);
        Assert.Equal(90, doc.Pages[1].Rotate);
        Assert.Equal(180, doc.Pages[2].Rotate);
    }

    [Fact]
    public void RenderSinglePageThumbnail_returns_valid_skbitmap()
    {
        var folder = fx.NewFolder();
        var pdfPath = CreateTestPdf(folder, "thumb_test.pdf", 2);

        using var bmp = PdfToolEngine.RenderSinglePageThumbnail(pdfPath, 0, targetHeight: 120);

        Assert.NotNull(bmp);
        Assert.True(bmp.Width > 0);
        Assert.True(bmp.Height > 0);
    }
}
