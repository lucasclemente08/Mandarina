using Mandarina.Interop;

namespace Mandarina.Actions;

public static class Catalog
{
    public static IReadOnlyList<FileAction> For(IReadOnlyList<string> files)
    {
        if (files.Count == 0 || files.Any(f => !File.Exists(f))) return [];
        return ImageActions.For(files)
            .Concat(MediaActions.For(files))
            .Concat(PdfActions.For(files))
            .Concat(OfficeActions.For(files))
            .ToList();
    }

    public static IReadOnlyList<FileAction> For(IReadOnlyList<string> files, WheelMode mode) =>
        For(files).Where(a => a.Mode == mode).ToList();
}
