namespace Mandarina;

/// <summary>
/// Small diagnostic trace in %LOCALAPPDATA%\Mandarina\mandarina.log (or the file named by MANDARINA_LOG),
/// so a problem the user reports can be read back afterwards. Restarts once it passes 512 KB.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    public static readonly string FilePath = Environment.GetEnvironmentVariable("MANDARINA_LOG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mandarina", "mandarina.log");

    static Log()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Delete(FilePath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(FilePath, $"{DateTime.Now:MM-dd HH:mm:ss.fff} [{Environment.ProcessId}:{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
