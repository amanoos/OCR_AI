using PDFtoImage;
using SkiaSharp;

namespace SteelCertExtraction.Services;

/// <summary>
/// Rasterizes PDF pages to PNG using PDFtoImage (pure managed, wraps
/// PDFium — no separate native install/runtime headaches on a corporate
/// machine, unlike some alternatives that shell out to Ghostscript/poppler).
///
/// NuGet: PDFtoImage, SkiaSharp
/// </summary>
public class PdfToImageRasterizer : IPdfRasterizer
{
    public async Task<List<RasterizedPage>> RasterizePagesAsync(
        Stream pdfStream, int dpi, CancellationToken ct)
    {
        // PDFtoImage needs a seekable stream; if you're handed a
        // non-seekable upload stream, buffer it first.
        Stream seekableStream = pdfStream;
        if (!pdfStream.CanSeek)
        {
            seekableStream = new MemoryStream();
            await pdfStream.CopyToAsync(seekableStream, ct);
            seekableStream.Position = 0;
        }

        // 150 DPI default keeps file sizes reasonable for a vision-LLM call;
        // bump per-call if you see misreads on dense chemistry tables, since
        // that's a quality/payload-size tradeoff worth tuning empirically
        // against your actual vendor certs rather than guessing up front.
        var options = new RenderOptions(Dpi: dpi);
        var pages = new List<RasterizedPage>();

        // ToImages renders every page in one pass — avoids the fragility of
        // manually rewinding the stream and re-decoding the PDF per page.
        var pageIndex = 0;
        foreach (var bitmap in PDFtoImage.Conversion.ToImages(seekableStream, options: options))
        {
            ct.ThrowIfCancellationRequested();
            using (bitmap)
            {
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
                pages.Add(new RasterizedPage(
                    PageNumber: ++pageIndex, // 1-indexed to match the bbox schema convention
                    PngBytes: data.ToArray(),
                    WidthPx: bitmap.Width,
                    HeightPx: bitmap.Height));
            }
        }

        return pages;
    }
}
