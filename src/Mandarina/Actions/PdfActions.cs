using System.Windows.Media.Imaging;
using Mandarina.Interop;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Windows.Data.Pdf;
using PdfDocument = PdfSharp.Pdf.PdfDocument;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Mandarina.Actions;

public static class PdfActions
{
    public static IEnumerable<FileAction> For(IReadOnlyList<string> files)
    {
        if (!files.All(f => FileKinds.Is(f, FileKinds.Pdf))) yield break;

        yield return new FileAction("pdf.to.png", "PNG", WheelMode.Convert, (fs, ct) => Render(fs, "png", ct));
        yield return new FileAction("pdf.to.jpg", "JPG", WheelMode.Convert, (fs, ct) => Render(fs, "jpg", ct));

        if (files.Count > 1)
            yield return new FileAction("pdf.merge", "Unir", WheelMode.Edit, Merge);
        yield return new FileAction("pdf.split", "Separar páginas", WheelMode.Edit, Split);
        yield return new FileAction("pdf.rotr", "Rotar ↻", WheelMode.Edit, (fs, ct) => Rotate(fs, 90, ct));
        yield return new FileAction("pdf.rotl", "Rotar ↺", WheelMode.Edit, (fs, ct) => Rotate(fs, 270, ct));
        yield return new FileAction("pdf.reverse", "Invertir orden", WheelMode.Edit, Reverse);
        yield return new FileAction("pdf.compress", "Comprimir", WheelMode.Edit, Compress);
    }

    /// <summary>Renders every page with the PDF engine built into Windows (Windows.Data.Pdf).</summary>
    private static async Task<IReadOnlyList<string>> Render(IReadOnlyList<string> files, string ext, CancellationToken ct)
    {
        var outputs = new List<string>();
        foreach (var input in files)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input));
            var doc = await WinPdfDocument.LoadFromFileAsync(file);
            if (doc.IsPasswordProtected) throw new InvalidOperationException("El PDF está protegido con contraseña.");

            if (doc.PageCount > 1)
            {
                var dir = OutputPaths.Unique(Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + " (páginas)"));
                Directory.CreateDirectory(dir);
                for (uint i = 0; i < doc.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await RenderPage(doc, i, Path.Combine(dir, $"Página {i + 1:000}.{ext}"), ext);
                }
                outputs.Add(dir);
            }
            else
            {
                var o = OutputPaths.Sibling(input, ext);
                await RenderPage(doc, 0, o, ext);
                outputs.Add(o);
            }
        }
        return outputs;
    }

    private static async Task RenderPage(WinPdfDocument doc, uint index, string output, string ext)
    {
        using var page = doc.GetPage(index);
        var options = new PdfPageRenderOptions
        {
            // 2× the 96-dpi page size ≈ 192 dpi: sharp enough to read, small enough to share.
            DestinationWidth = (uint)Math.Round(page.Size.Width * 2),
            DestinationHeight = (uint)Math.Round(page.Size.Height * 2),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        using var ras = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(ras, options);
        var png = new byte[ras.Size];
        using (var reader = new DataReader(ras.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)ras.Size);
            reader.ReadBytes(png);
        }

        if (ext == "png")
        {
            await File.WriteAllBytesAsync(output, png);
            return;
        }
        await ImageIO.Sta(() =>
        {
            using var ms = new MemoryStream(png);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            ImageIO.Save(frame, output);
        });
    }

    public static async Task<IReadOnlyList<string>> ImagesToPdf(IReadOnlyList<string> files, CancellationToken ct)
    {
        var ordered = files.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        var output = OutputPaths.Sibling(ordered[0], "pdf");
        using var pdf = new PdfDocument();
        foreach (var input in ordered)
        {
            ct.ThrowIfCancellationRequested();
            // Re-encode through WIC so any decodable format (WebP, HEIC, …) ends up as JPEG/PNG, which PDFsharp embeds.
            var (bytes, w, h) = await ImageIO.Sta(() =>
            {
                var src = ImageIO.Load(input);
                var enc = ImageIO.HasAlpha(src) ? (BitmapEncoder)new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return (ms.ToArray(), src.PixelWidth, src.PixelHeight);
            });

            var page = pdf.AddPage();
            // One image per page, page sized to the image at 150 dpi.
            page.Width = XUnit.FromPoint(w * 72.0 / 150);
            page.Height = XUnit.FromPoint(h * 72.0 / 150);
            using var ms = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            using var img = XImage.FromStream(ms);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
        }
        pdf.Save(output);
        return [output];
    }

    private static Task<IReadOnlyList<string>> Merge(IReadOnlyList<string> files, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var ordered = files.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        var output = OutputPaths.Sibling(ordered[0], "pdf", "unido");
        using var outDoc = new PdfDocument();
        foreach (var f in ordered)
        {
            ct.ThrowIfCancellationRequested();
            using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
            foreach (var p in inDoc.Pages) outDoc.AddPage(p);
        }
        outDoc.Save(output);
        return [output];
    }, ct);

    private static Task<IReadOnlyList<string>> Split(IReadOnlyList<string> files, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var outputs = new List<string>();
        foreach (var f in files)
        {
            using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
            var dir = OutputPaths.Unique(Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + " (páginas)"));
            Directory.CreateDirectory(dir);
            for (int i = 0; i < inDoc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var one = new PdfDocument();
                one.AddPage(inDoc.Pages[i]);
                one.Save(Path.Combine(dir, $"Página {i + 1:000}.pdf"));
            }
            outputs.Add(dir);
        }
        return outputs;
    }, ct);

    private static Task<IReadOnlyList<string>> Rotate(IReadOnlyList<string> files, int degrees, CancellationToken ct) =>
        Modify(files, "rotado", doc => { foreach (var p in doc.Pages) p.Rotate = (p.Rotate + degrees) % 360; }, ct);

    private static Task<IReadOnlyList<string>> Reverse(IReadOnlyList<string> files, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var outputs = new List<string>();
        foreach (var f in files)
        {
            using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
            using var outDoc = new PdfDocument();
            for (int i = inDoc.PageCount - 1; i >= 0; i--) outDoc.AddPage(inDoc.Pages[i]);
            var o = OutputPaths.Sibling(f, "pdf", "invertido");
            outDoc.Save(o);
            outputs.Add(o);
        }
        return outputs;
    }, ct);

    private static Task<IReadOnlyList<string>> Compress(IReadOnlyList<string> files, CancellationToken ct) =>
        Modify(files, "comprimido", doc =>
        {
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
            doc.Options.CompressContentStreams = true;
            doc.Options.NoCompression = false;
        }, ct);

    private static Task<IReadOnlyList<string>> Modify(IReadOnlyList<string> files, string suffix, Action<PdfDocument> change, CancellationToken ct) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var outputs = new List<string>();
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                using var doc = PdfReader.Open(f, PdfDocumentOpenMode.Modify);
                change(doc);
                var o = OutputPaths.Sibling(f, "pdf", suffix);
                doc.Save(o);
                outputs.Add(o);
            }
            return outputs;
        }, ct);
}
