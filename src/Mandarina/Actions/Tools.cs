using System.Diagnostics;

namespace Mandarina.Actions;

/// <summary>Locates and runs the external command-line tools Mandarina relies on.</summary>
public static class Tools
{
    private static readonly Lazy<string?> _ffmpeg = new(() => Find("ffmpeg.exe"));
    private static readonly Lazy<string?> _pandoc = new(() => Find("pandoc.exe", @"Pandoc\pandoc.exe"));
    private static readonly Lazy<string?> _soffice = new(() => Find("soffice.exe", @"LibreOffice\program\soffice.exe"));

    public static string? FFmpeg => _ffmpeg.Value;
    public static string? Pandoc => _pandoc.Value;
    public static string? LibreOffice => _soffice.Value;

    /// <summary>Looks next to Mandarina.exe (tools\), then on PATH, then under Program Files.</summary>
    private static string? Find(string exe, string? programFilesRelative = null)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "tools", exe);
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }

        if (programFilesRelative is not null)
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs" })
            {
                var p = Path.Combine(root, programFilesRelative);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    public static async Task RunAsync(string exe, IEnumerable<string> args, CancellationToken ct, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {exe}");
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var err = await stderr;
        await stdout;
        if (proc.ExitCode != 0)
        {
            var tail = string.Join('\n', err.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(4));
            throw new InvalidOperationException($"{Path.GetFileName(exe)} falló ({proc.ExitCode}): {tail}");
        }
    }

    public static Task FFmpegAsync(CancellationToken ct, params string[] args)
    {
        var exe = FFmpeg ?? throw new InvalidOperationException("No se encontró ffmpeg. Instalalo con: winget install Gyan.FFmpeg");
        return RunAsync(exe, ["-hide_banner", "-loglevel", "error", "-y", .. args], ct);
    }
}
