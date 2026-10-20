namespace Mandarina;

/// <summary>Debug trace, written only when the MANDARINA_LOG environment variable names a file.</summary>
internal static class Log
{
    private static readonly string? FilePath = Environment.GetEnvironmentVariable("MANDARINA_LOG");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (FilePath is null) return;
        lock (Gate) File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
    }
}
