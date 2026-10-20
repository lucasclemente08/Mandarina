using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandarina.Actions;

/// <summary>Image decode/encode on top of WIC (WPF imaging), with ffmpeg as fallback for formats WIC lacks.</summary>
public static class ImageIO
{
    /// <summary>WPF imaging objects want an STA thread; this runs work on a throwaway one.</summary>
    public static Task<T> Sta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }

    public static Task Sta(Action work) => Sta(() => { work(); return 0; });

    /// <summary>Decodes the first frame. Falls back to ffmpeg → temporary PNG when no WIC codec is installed.</summary>
    public static BitmapSource Load(string path)
    {
        try
        {
            return LoadWic(path);
        }
        catch (Exception wicError) when (wicError is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"mandarina-{Guid.NewGuid():N}.png");
            try
            {
                Tools.FFmpegAsync(CancellationToken.None, "-i", path, "-frames:v", "1", tmp).GetAwaiter().GetResult();
                return LoadWic(tmp);
            }
            catch (Exception ffError)
            {
                throw new NotSupportedException($"No se pudo leer la imagen ({wicError.Message} / {ffError.Message})");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }

    private static BitmapSource LoadWic(string path)
    {
        using var fs = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        // Normalize DPI to 96 so later transforms work in pixels.
        if (Math.Abs(frame.DpiX - 96) > 0.1 || Math.Abs(frame.DpiY - 96) > 0.1)
        {
            var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            frame = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, frame.Format, frame.Palette, pixels, stride);
        }
        frame.Freeze();
        return frame;
    }

    public static bool HasAlpha(BitmapSource src) =>
        src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32 ||
        src.Format == PixelFormats.Rgba64 || src.Format == PixelFormats.Prgba64 || src.Format == PixelFormats.Rgba128Float ||
        src.Format == PixelFormats.Prgba128Float || (src.Palette?.Colors.Any(c => c.A < 255) ?? false);

    /// <summary>Paints the image over a solid background (for JPEG/BMP, which have no transparency).</summary>
    public static BitmapSource Flatten(BitmapSource src, Color background)
    {
        if (!HasAlpha(src)) return src;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, src.PixelWidth, src.PixelHeight);
            dc.DrawRectangle(new SolidColorBrush(background), null, rect);
            dc.DrawImage(src, rect);
        }
        var rtb = new RenderTargetBitmap(src.PixelWidth, src.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var result = new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0);
        result.Freeze();
        return result;
    }

    public static BitmapSource Resize(BitmapSource src, double scale)
    {
        var t = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        t.Freeze();
        return t;
    }

    public static BitmapSource FitWithin(BitmapSource src, int maxSide)
    {
        int longest = Math.Max(src.PixelWidth, src.PixelHeight);
        return longest <= maxSide ? src : Resize(src, (double)maxSide / longest);
    }

    public static BitmapSource Rotate(BitmapSource src, int degrees)
    {
        var t = new TransformedBitmap(src, new RotateTransform(degrees));
        t.Freeze();
        return t;
    }

    public static BitmapSource Flip(BitmapSource src, bool horizontal)
    {
        var t = new TransformedBitmap(src, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));
        t.Freeze();
        return t;
    }

    public static BitmapSource Grayscale(BitmapSource src)
    {
        var g = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
        g.Freeze();
        return g;
    }

    /// <summary>Encodes to the given extension. WebP goes through ffmpeg; ICO is written by hand.</summary>
    public static void Save(BitmapSource src, string path, int jpegQuality = 92)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".webp":
                SaveViaFFmpeg(src, path, ["-c:v", "libwebp", "-quality", jpegQuality.ToString(), "-lossless", "0"]);
                return;
            case ".avif":
                SaveViaFFmpeg(src, path, ["-c:v", "libaom-av1", "-still-picture", "1", "-crf", "30"]);
                return;
            case ".ico":
                SaveIco(src, path);
                return;
        }

        BitmapEncoder encoder = ext switch
        {
            ".png" => new PngBitmapEncoder(),
            ".jpg" or ".jpeg" or ".jfif" => new JpegBitmapEncoder { QualityLevel = jpegQuality },
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
            ".jxr" or ".wdp" => new WmpBitmapEncoder(),
            _ => throw new NotSupportedException($"Formato de salida no soportado: {ext}"),
        };
        if (ext is ".jpg" or ".jpeg" or ".jfif" or ".bmp") src = Flatten(src, Colors.White);
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static void SaveViaFFmpeg(BitmapSource src, string path, string[] codecArgs)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mandarina-{Guid.NewGuid():N}.png");
        try
        {
            Save(src, tmp);
            Tools.FFmpegAsync(CancellationToken.None, ["-i", tmp, .. codecArgs, path]).GetAwaiter().GetResult();
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    /// <summary>Multi-size .ico with PNG-compressed entries (supported since Windows Vista).</summary>
    private static void SaveIco(BitmapSource src, string path)
    {
        int[] sizes = [256, 128, 64, 48, 32, 16];
        var square = SquareCanvas(src);
        var images = sizes.Select(s =>
        {
            var scaled = Resize(square, (double)s / square.PixelWidth);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (size: s, data: ms.ToArray());
        }).ToList();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
    }

    /// <summary>Centers the image on a transparent square so icons are not distorted.</summary>
    private static BitmapSource SquareCanvas(BitmapSource src)
    {
        int side = Math.Max(src.PixelWidth, src.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(src, new Rect((side - src.PixelWidth) / 2.0, (side - src.PixelHeight) / 2.0, src.PixelWidth, src.PixelHeight));
        var rtb = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
