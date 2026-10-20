using System.Windows.Media.Imaging;
using Mandarina.Interop;

namespace Mandarina.Actions;

public static class ImageActions
{
    private static readonly (string ext, string label)[] Targets =
    [
        ("png", "PNG"), ("jpg", "JPG"), ("webp", "WEBP"), ("gif", "GIF"),
        ("bmp", "BMP"), ("tiff", "TIFF"), ("ico", "ICO"), ("avif", "AVIF"),
    ];

    public static IEnumerable<FileAction> For(IReadOnlyList<string> files)
    {
        if (!files.All(f => FileKinds.Is(f, FileKinds.Image))) yield break;

        var exts = files.Select(FileKinds.Ext).Select(e => e == "jpeg" || e == "jfif" ? "jpg" : e == "tif" ? "tiff" : e).ToHashSet();
        foreach (var (ext, label) in Targets)
        {
            if (exts.Count == 1 && exts.Contains(ext)) continue;
            var target = ext;
            yield return new FileAction($"img.to.{target}", label, WheelMode.Convert,
                (fs, ct) => Each(fs, ct, (src, input) => (src, OutputPaths.Sibling(input, target))));
        }
        yield return new FileAction("img.to.pdf", files.Count > 1 ? "PDF (unir)" : "PDF", WheelMode.Convert, PdfActions.ImagesToPdf);

        if (files.Count == 1 && FileKinds.Ext(files[0]) == "gif")
        {
            yield return new FileAction("img.gif.to.mp4", "MP4", WheelMode.Convert, (fs, ct) => MediaActions.Convert(fs, "mp4", ct));
            yield return new FileAction("img.gif.to.webm", "WEBM", WheelMode.Convert, (fs, ct) => MediaActions.Convert(fs, "webm", ct));
        }

        yield return Edit("img.half", "50 %", "50%", (s, _) => ImageIO.Resize(s, 0.5));
        yield return Edit("img.fhd", "Máx 1920 px", "1920px", (s, _) => ImageIO.FitWithin(s, 1920));
        yield return Edit("img.rotr", "Rotar ↻", "rotada", (s, _) => ImageIO.Rotate(s, 90));
        yield return Edit("img.rotl", "Rotar ↺", "rotada", (s, _) => ImageIO.Rotate(s, 270));
        yield return Edit("img.flip", "Espejo", "espejo", (s, _) => ImageIO.Flip(s, horizontal: true));
        yield return Edit("img.gray", "Blanco y negro", "ByN", (s, _) => ImageIO.Grayscale(s));
        yield return Edit("img.strip", "Sin metadatos", "limpia", (s, _) => s);
        yield return new FileAction("img.compress", "Comprimir", WheelMode.Edit, (fs, ct) => Each(fs, ct, (src, input) =>
        {
            // Lossy re-encode: JPEG for opaque images, WebP when transparency must survive.
            var ext = ImageIO.HasAlpha(src) ? "webp" : "jpg";
            return (src, OutputPaths.Sibling(input, ext, "comprimida"));
        }, quality: 70));
    }

    private static FileAction Edit(string id, string label, string suffix, Func<BitmapSource, string, BitmapSource> transform) =>
        new(id, label, WheelMode.Edit, (fs, ct) => Each(fs, ct, (src, input) =>
        {
            var ext = FileKinds.Ext(input);
            // Keep the format when we can write it; otherwise fall back to PNG.
            if (ext is "heic" or "heif" or "jfif") ext = ext == "jfif" ? "jpg" : "png";
            return (transform(src, input), OutputPaths.Sibling(input, ext, suffix));
        }));

    private static async Task<IReadOnlyList<string>> Each(
        IReadOnlyList<string> files, CancellationToken ct,
        Func<BitmapSource, string, (BitmapSource image, string output)> work, int quality = 92)
    {
        var outputs = new List<string>();
        foreach (var input in files)
        {
            ct.ThrowIfCancellationRequested();
            var output = await ImageIO.Sta(() =>
            {
                var (image, path) = work(ImageIO.Load(input), input);
                try
                {
                    ImageIO.Save(image, path, quality);
                }
                catch
                {
                    try { File.Delete(path); } catch { }
                    throw;
                }
                return path;
            });
            outputs.Add(output);
        }
        return outputs;
    }
}
