using System.Diagnostics;
using Microsoft.Win32;
using Mandarina.Actions;
using Mandarina.Interop;

namespace Mandarina;

/// <summary>
/// Per-user install, no admin rights: copies the exe to %LOCALAPPDATA%\Programs\Mandarina and adds
/// the Explorer context-menu entry, the Start-menu shortcut and the start-with-Windows entry.
/// </summary>
internal static class Installer
{
    private const string MenuKey = @"Software\Classes\*\shell\Mandarina";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mandarina");
    public static string InstalledExe => Path.Combine(InstallDir, "Mandarina.exe");
    private static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mandarina.lnk");

    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static void Install()
    {
        StopRunningInstance();
        Directory.CreateDirectory(InstallDir);
        if (!IsRunningInstalled) CopyWithRetry(Environment.ProcessPath!, InstalledExe);

        var exe = InstalledExe;
        using (var menu = Registry.CurrentUser.CreateSubKey(MenuKey))
        {
            menu.SetValue("", "Convertir con Mandarina");
            menu.SetValue("Icon", $"\"{exe}\",0");
            menu.SetValue("MultiSelectModel", "Player");
            // Only show the entry for file types Mandarina can do something with.
            menu.SetValue("AppliesTo", string.Join(" OR ", FileKinds.All.Distinct().Select(e => $"System.FileExtension:={e}")));
            using var cmd = menu.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" --open \"%1\"");
        }
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue("Mandarina", $"\"{exe}\" --background");

        CreateShortcut(Shortcut, exe);
    }

    public static void Uninstall()
    {
        StopRunningInstance();
        Registry.CurrentUser.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.DeleteValue("Mandarina", throwOnMissingValue: false);
        try { File.Delete(Shortcut); } catch { }
        if (ClassicMenu.EnabledByMandarina) ClassicMenu.Set(false);

        if (IsRunningInstalled)
        {
            // A running exe cannot delete itself: let a short-lived cmd do it once we exit.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        else
        {
            try { Directory.Delete(InstallDir, recursive: true); } catch { }
        }
    }

    public static void LaunchInstalled() => Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });

    private static void StopRunningInstance()
    {
        if (!InstancePipe.TrySend("quit", [])) return;
        // Give it a moment to release the exe file.
        for (int i = 0; i < 20 && Process.GetProcessesByName("Mandarina").Any(p => p.Id != Environment.ProcessId); i++)
            Thread.Sleep(150);
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void CreateShortcut(string path, string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic lnk = shell.CreateShortcut(path);
        lnk.TargetPath = target;
        lnk.WorkingDirectory = Path.GetDirectoryName(target);
        lnk.Description = "Conversor de archivos por arrastre";
        lnk.IconLocation = $"{target},0";
        lnk.Save();
    }
}
