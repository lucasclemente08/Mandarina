using System.Runtime.InteropServices;
using Mandarina.Interop;

namespace Mandarina.Actions;

/// <summary>
/// Document conversions. Uses Microsoft Office through COM when installed, otherwise LibreOffice
/// (soffice --headless); Markdown goes through Pandoc.
/// </summary>
public static class OfficeActions
{
    private static readonly Lazy<bool> HasWord = new(() => Type.GetTypeFromProgID("Word.Application") is not null);
    private static readonly Lazy<bool> HasExcel = new(() => Type.GetTypeFromProgID("Excel.Application") is not null);
    private static readonly Lazy<bool> HasPowerPoint = new(() => Type.GetTypeFromProgID("PowerPoint.Application") is not null);

    // Word WdSaveFormat values
    private static readonly Dictionary<string, int> WordFormats = new()
    {
        ["pdf"] = 17, ["docx"] = 16, ["odt"] = 23, ["rtf"] = 6, ["txt"] = 7, ["html"] = 10,
    };
    // Excel XlFileFormat values (pdf is exported separately)
    private static readonly Dictionary<string, int> ExcelFormats = new()
    {
        ["xlsx"] = 51, ["csv"] = 62, ["ods"] = 60,
    };
    // PowerPoint PpSaveAsFileType values
    private static readonly Dictionary<string, int> PowerPointFormats = new()
    {
        ["pdf"] = 32, ["pptx"] = 24, ["odp"] = 35,
    };

    public static IEnumerable<FileAction> For(IReadOnlyList<string> files)
    {
        if (files.All(f => FileKinds.Is(f, FileKinds.Markdown)))
        {
            if (Tools.Pandoc is null) yield break;
            foreach (var t in new[] { "docx", "html", "odt", "rtf" })
            {
                var target = t;
                yield return new FileAction($"md.to.{t}", t.ToUpperInvariant(), WheelMode.Convert, (fs, ct) => PandocConvert(fs, target, ct));
            }
            yield break;
        }

        string[] kind;
        Dictionary<string, int> formats;
        bool office;
        if (files.All(f => FileKinds.Is(f, FileKinds.Word))) { kind = FileKinds.Word; formats = WordFormats; office = HasWord.Value; }
        else if (files.All(f => FileKinds.Is(f, FileKinds.Excel))) { kind = FileKinds.Excel; formats = ExcelFormats; office = HasExcel.Value; }
        else if (files.All(f => FileKinds.Is(f, FileKinds.PowerPoint))) { kind = FileKinds.PowerPoint; formats = PowerPointFormats; office = HasPowerPoint.Value; }
        else yield break;

        if (!office && Tools.LibreOffice is null) yield break;

        var exts = files.Select(FileKinds.Ext).ToHashSet();
        var targets = formats.Keys.ToList();
        if (kind == FileKinds.Excel) targets.Insert(0, "pdf");
        foreach (var t in targets)
        {
            if (exts.Count == 1 && exts.Contains(t)) continue;
            var target = t;
            yield return new FileAction($"doc.to.{t}", t.ToUpperInvariant(), WheelMode.Convert, (fs, ct) => Convert(fs, target, ct));
        }

        if (kind == FileKinds.Word && Tools.Pandoc is not null)
            yield return new FileAction("doc.to.md", "Markdown", WheelMode.Convert, (fs, ct) => PandocConvert(fs, "md", ct));
    }

    public static async Task<IReadOnlyList<string>> Convert(IReadOnlyList<string> files, string target, CancellationToken ct)
    {
        var outputs = new List<string>();
        foreach (var input in files)
        {
            ct.ThrowIfCancellationRequested();
            var output = OutputPaths.Sibling(input, target);
            bool word = FileKinds.Is(input, FileKinds.Word);
            bool excel = FileKinds.Is(input, FileKinds.Excel);

            if (word && HasWord.Value) await WithWatchdog("WINWORD", () => WordConvert(input, output, WordFormats[target]));
            else if (excel && HasExcel.Value) await WithWatchdog("EXCEL", () => ExcelConvert(input, output, target));
            else if (!word && !excel && HasPowerPoint.Value) await WithWatchdog("POWERPNT", () => PowerPointConvert(input, output, PowerPointFormats[target]));
            else await LibreOfficeConvert(input, output, target, ct);

            outputs.Add(output);
        }
        return outputs;
    }

    /// <summary>
    /// Office can block forever on a dialog nobody sees. If the conversion takes too long,
    /// kill only the Office processes that were started after it began (never the user's own).
    /// </summary>
    private static async Task WithWatchdog(string processName, Action work)
    {
        var started = DateTime.Now.AddSeconds(-1);
        var task = ImageIO.Sta(work);
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromMinutes(3))) != task)
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try { if (p.StartTime >= started) p.Kill(); } catch { }
            }
            throw new TimeoutException("Office no respondió a tiempo (¿un diálogo oculto?).");
        }
        await task;
    }

    private static void WordConvert(string input, string output, int format)
    {
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
        dynamic? doc = null;
        try
        {
            app.DisplayAlerts = 0; // wdAlertsNone
            doc = app.Documents.Open(FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            if (format == 7) doc.SaveAs2(FileName: output, FileFormat: format, Encoding: 65001);
            else doc.SaveAs2(FileName: output, FileFormat: format, AddToRecentFiles: false);
        }
        finally
        {
            if (doc is not null) { doc.Close(SaveChanges: 0); Marshal.FinalReleaseComObject(doc); }
            // If Word was already open with the user's documents, leave it running.
            if (app.Documents.Count == 0) app.Quit(SaveChanges: 0);
            Marshal.FinalReleaseComObject(app);
        }
    }

    private static void ExcelConvert(string input, string output, string target)
    {
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application")!)!;
        dynamic? wb = null;
        try
        {
            app.DisplayAlerts = false;
            wb = app.Workbooks.Open(Filename: input, ReadOnly: true, AddToMru: false);
            if (target == "pdf") wb.ExportAsFixedFormat(Type: 0, Filename: output);
            else wb.SaveAs(Filename: output, FileFormat: ExcelFormats[target]);
        }
        finally
        {
            if (wb is not null) { wb.Close(SaveChanges: false); Marshal.FinalReleaseComObject(wb); }
            if (app.Workbooks.Count == 0) app.Quit();
            Marshal.FinalReleaseComObject(app);
        }
    }

    private static void PowerPointConvert(string input, string output, int format)
    {
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Open(FileName: input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
            pres.SaveAs(FileName: output, FileFormat: format);
        }
        finally
        {
            if (pres is not null) { pres.Close(); Marshal.FinalReleaseComObject(pres); }
            if (app.Presentations.Count == 0) app.Quit();
            Marshal.FinalReleaseComObject(app);
        }
    }

    private static async Task LibreOfficeConvert(string input, string output, string target, CancellationToken ct)
    {
        var soffice = Tools.LibreOffice ?? throw new InvalidOperationException(
            "Hace falta Microsoft Office o LibreOffice (winget install TheDocumentFoundation.LibreOffice).");
        // soffice always names the result after the input, so convert into a scratch folder and move it.
        var scratch = Directory.CreateTempSubdirectory("mandarina-").FullName;
        try
        {
            await Tools.RunAsync(soffice, ["--headless", "--norestore", "--convert-to", target, "--outdir", scratch, input], ct);
            var produced = Directory.GetFiles(scratch).FirstOrDefault()
                ?? throw new InvalidOperationException("LibreOffice no generó ningún archivo.");
            File.Move(produced, output);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { }
        }
    }

    private static async Task<IReadOnlyList<string>> PandocConvert(IReadOnlyList<string> files, string target, CancellationToken ct)
    {
        var outputs = new List<string>();
        foreach (var input in files)
        {
            var output = OutputPaths.Sibling(input, target);
            string[] extra = target == "html" ? ["--standalone", "--embed-resources"] : [];
            await Tools.RunAsync(Tools.Pandoc!, [input, "-o", output, .. extra], ct, Path.GetDirectoryName(input));
            outputs.Add(output);
        }
        return outputs;
    }
}
