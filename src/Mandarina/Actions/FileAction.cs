using Mandarina.Interop;

namespace Mandarina.Actions;

/// <summary>One slice of the wheel: something that can be done to the dropped files.</summary>
public sealed record FileAction(
    string Id,
    string Label,
    WheelMode Mode,
    Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> Run);

public static class FileKinds
{
    public static readonly string[] Image = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif", ".jfif"];
    public static readonly string[] Video = [".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts"];
    public static readonly string[] Audio = [".mp3", ".wav", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".flac", ".wma", ".aiff", ".aif"];
    public static readonly string[] Pdf = [".pdf"];
    public static readonly string[] Word = [".docx", ".doc", ".odt", ".rtf", ".txt", ".htm", ".html"];
    public static readonly string[] Excel = [".xlsx", ".xls", ".ods", ".csv"];
    public static readonly string[] PowerPoint = [".pptx", ".ppt", ".odp"];
    public static readonly string[] Markdown = [".md", ".markdown"];

    public static bool Is(string path, string[] kind) =>
        kind.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
}

public static class OutputPaths
{
    /// <summary>"C:\x\Foto.png" + "jpg" → "C:\x\Foto.jpg", or "Foto (2).jpg" if that already exists.</summary>
    public static string Sibling(string input, string newExt, string? suffix = null)
    {
        var dir = Path.GetDirectoryName(input)!;
        var stem = Path.GetFileNameWithoutExtension(input) + (suffix is null ? "" : " " + suffix);
        return Unique(Path.Combine(dir, $"{stem}.{newExt}"));
    }

    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}
