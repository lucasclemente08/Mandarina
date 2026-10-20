using System.Windows.Media;

namespace Mandarina.Actions;

/// <summary>Icon glyph (Segoe Fluent Icons / MDL2) and color for an action, so it can be recognised at a glance.</summary>
public static class ActionStyle
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // One color per kind of result.
    public static readonly Color Image = Color.FromRgb(46, 160, 87);
    public static readonly Color Video = Color.FromRgb(140, 82, 214);
    public static readonly Color Audio = Color.FromRgb(36, 132, 219);
    public static readonly Color Document = Color.FromRgb(214, 69, 65);
    public static readonly Color Text = Color.FromRgb(96, 108, 124);
    public static readonly Color Edit = Color.FromRgb(242, 133, 34);

    private static readonly HashSet<string> ImageExts = ["png", "jpg", "webp", "gif", "bmp", "tiff", "ico", "avif"];
    private static readonly HashSet<string> VideoExts = ["mp4", "mkv", "webm", "mov", "avi"];
    private static readonly HashSet<string> AudioExts = ["mp3", "wav", "m4a", "ogg", "flac", "opus"];
    private static readonly HashSet<string> DocExts = ["pdf", "docx", "odt", "rtf", "xlsx", "csv", "ods", "pptx", "odp"];

    public static (string glyph, Color color) For(FileAction action)
    {
        var id = action.Id;
        int to = id.IndexOf(".to.", StringComparison.Ordinal);
        if (to >= 0)
        {
            var ext = id[(to + 4)..];
            // GIF from a video is an animation: show it as video.
            if (ext == "gif" && id.StartsWith("media", StringComparison.Ordinal)) return ("", Video);
            if (ImageExts.Contains(ext)) return ("", Image);
            if (VideoExts.Contains(ext)) return ("", Video);
            if (AudioExts.Contains(ext)) return ("", Audio);
            if (ext == "pdf") return ("", Document);
            if (ext is "html" or "md") return ("", Text);
            if (ext is "txt") return ("", Text);
            if (DocExts.Contains(ext)) return ("", Document);
            return ("", Text);
        }

        string glyph = id switch
        {
            _ when id.EndsWith("rotr") || id.EndsWith("rotl") => "",
            _ when id.EndsWith("compress") => "",
            _ when id.EndsWith("half") || id.EndsWith("fhd") || id.EndsWith("720") || id.EndsWith("480") => "",
            _ when id.EndsWith("flip") => "",
            _ when id.EndsWith("gray") => "",
            _ when id.EndsWith("strip") => "",
            _ when id.EndsWith("mute") => "",
            _ when id.EndsWith("mono") || id.EndsWith("norm") => "",
            _ when id.EndsWith("trim") => "",
            _ when id.EndsWith("fast") => "",
            _ when id.EndsWith("frame") => "",
            _ when id.EndsWith("merge") || id.EndsWith("concat") => "",
            _ when id.EndsWith("split") => "",
            _ when id.EndsWith("reverse") => "",
            _ => "",
        };
        return (glyph, Edit);
    }
}
