using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using Mandarina.Interop;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Mandarina.Actions;

/// <summary>Plain-text conversions written directly, so they work without Office or Pandoc.</summary>
public static class TextActions
{
    static TextActions()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
    }

    public static IEnumerable<FileAction> For(IReadOnlyList<string> files)
    {
        if (!files.All(f => FileKinds.Is(f, FileKinds.Text))) yield break;

        yield return Convert("pdf", "PDF", ToPdf);
        yield return Convert("docx", "DOCX", ToDocx);
        yield return Convert("html", "HTML", ToHtml);
        yield return Convert("md", "Markdown", (text, _, output) => File.WriteAllText(output, text, new UTF8Encoding(false)));
        yield return Convert("rtf", "RTF", ToRtf);
        yield return Convert("csv", "CSV", ToCsv);
        if (files.Count > 1)
            yield return new FileAction("txt.merge", "Unir", WheelMode.Edit, Merge);
    }

    private static FileAction Convert(string ext, string label, Action<string, string, string> write) =>
        new($"txt.to.{ext}", label, WheelMode.Convert, (files, ct) => Task.Run<IReadOnlyList<string>>(() =>
        {
            var outputs = new List<string>();
            foreach (var input in files)
            {
                ct.ThrowIfCancellationRequested();
                var output = OutputPaths.Sibling(input, ext);
                try
                {
                    write(Read(input), Path.GetFileNameWithoutExtension(input), output);
                }
                catch
                {
                    try { File.Delete(output); } catch { }
                    throw;
                }
                outputs.Add(output);
            }
            return outputs;
        }, ct));

    /// <summary>Reads UTF-8 (with or without BOM) or UTF-16, falling back to Windows-1252 for old Spanish files.</summary>
    public static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return text.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static void ToPdf(string text, string title, string output)
    {
        const double margin = 56, fontSize = 10;
        using var pdf = new PdfDocument();
        pdf.Info.Title = title;
        var font = new XFont("Consolas", fontSize);
        double lineHeight = fontSize * 1.35;

        PdfPage? page = null;
        XGraphics? gfx = null;
        double y = 0;
        int maxChars = 0;

        void NewPage()
        {
            gfx?.Dispose();
            page = pdf.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            y = margin;
            // Consolas is monospaced, so wrapping by character count keeps columns aligned.
            maxChars = Math.Max(20, (int)((page.Width.Point - 2 * margin) / gfx.MeasureString("M", font).Width));
        }

        NewPage();
        foreach (var raw in Lines(text))
        {
            var line = raw.Replace("\t", "    ");
            var pieces = line.Length == 0 ? [""] : Enumerable.Range(0, (line.Length + maxChars - 1) / maxChars)
                .Select(i => line.Substring(i * maxChars, Math.Min(maxChars, line.Length - i * maxChars)));
            foreach (var piece in pieces)
            {
                if (y + lineHeight > page!.Height.Point - margin) NewPage();
                gfx!.DrawString(piece, font, XBrushes.Black, margin, y + fontSize);
                y += lineHeight;
            }
        }
        gfx?.Dispose();
        pdf.Save(output);
    }

    private static void ToDocx(string text, string title, string output)
    {
        // XML forbids most control characters; drop them instead of producing a file Word refuses to open.
        string X(string s) => SecurityElement.Escape(new string(s.Where(c => c >= ' ' || c == '\t').ToArray())) ?? "";
        var body = new StringBuilder();
        foreach (var line in Lines(text))
            body.Append($"<w:p><w:r><w:t xml:space=\"preserve\">{X(line.Replace("\t", "    "))}</w:t></w:r></w:p>");

        using var fs = File.Create(output);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Entry(string name, string content)
        {
            using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Entry("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "</Types>");
        Entry("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
            "</Relationships>");
        Entry("docProps/core.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            $"<dc:title>{X(title)}</dc:title></cp:coreProperties>");
        Entry("word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            body +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr>" +
            "</w:body></w:document>");
    }

    private static void ToHtml(string text, string title, string output)
    {
        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              body { margin: 2rem auto; max-width: 52rem; padding: 0 1rem; background: #fff; color: #222; }
              pre { white-space: pre-wrap; word-wrap: break-word; font: 15px/1.55 Consolas, "Cascadia Mono", monospace; }
              @media (prefers-color-scheme: dark) { body { background: #1b1b1d; color: #e6e6e6; } }
            </style>
            </head>
            <body>
            <pre>{{WebUtility.HtmlEncode(text)}}</pre>
            </body>
            </html>
            """;
        File.WriteAllText(output, html, new UTF8Encoding(false));
    }

    private static void ToRtf(string text, string title, string output)
    {
        var sb = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fmodern Consolas;}}\f0\fs20 ");
        foreach (var line in Lines(text))
        {
            foreach (var ch in line)
            {
                if (ch is '\\' or '{' or '}') sb.Append('\\').Append(ch);
                else if (ch == '\t') sb.Append(@"\tab ");
                else if (ch > 127) sb.Append(@"\u").Append((short)ch).Append('?');
                else sb.Append(ch);
            }
            sb.Append(@"\par").Append("\r\n");
        }
        sb.Append('}');
        File.WriteAllText(output, sb.ToString(), Encoding.ASCII);
    }

    /// <summary>
    /// Tab- or semicolon-separated text becomes proper columns; anything else becomes one column per line.
    /// Written with a UTF-8 BOM and ";" so Excel in Spanish opens it with accents and columns intact.
    /// </summary>
    private static void ToCsv(string text, string title, string output)
    {
        var lines = Lines(text).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        char? sep = lines.Count(l => l.Contains('\t')) > lines.Count / 2 ? '\t'
            : lines.Count(l => l.Contains(';')) > lines.Count / 2 ? ';' : null;

        string Cell(string c) => c.IndexOfAny([';', '"', '\n', ',']) >= 0 ? $"\"{c.Replace("\"", "\"\"")}\"" : c;
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var cells = sep is { } s ? line.Split(s) : [line];
            sb.Append(string.Join(';', cells.Select(c => Cell(c.Trim())))).Append("\r\n");
        }
        File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
    }

    private static Task<IReadOnlyList<string>> Merge(IReadOnlyList<string> files, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var ordered = files.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        var output = OutputPaths.Sibling(ordered[0], "txt", "unido");
        var parts = ordered.Select(f => Read(f).TrimEnd('\r', '\n'));
        File.WriteAllText(output, string.Join("\r\n\r\n", parts) + "\r\n", new UTF8Encoding(false));
        return [output];
    }, ct);
}

/// <summary>Serves Consolas from C:\Windows\Fonts to PDFsharp (its core build ships no font lookup).</summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new((bold, italic) switch
        {
            (true, true) => "consolaz.ttf",
            (true, false) => "consolab.ttf",
            (false, true) => "consolai.ttf",
            _ => "consola.ttf",
        });

    public byte[]? GetFont(string faceName)
    {
        var path = Path.Combine(Fonts, faceName);
        return File.Exists(path) ? File.ReadAllBytes(path) : File.ReadAllBytes(Path.Combine(Fonts, "consola.ttf"));
    }
}
