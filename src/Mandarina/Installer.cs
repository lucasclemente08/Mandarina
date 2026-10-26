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
    private const string ProgId = "Mandarina.Convertir";
    private const string ClassesKey = @"Software\Classes";

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
        RegisterOpenWith(exe);
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue("Mandarina", $"\"{exe}\" --background");

        CreateShortcut(Shortcut, exe);
        NotifyAssociationsChanged();
    }

    /// <summary>
    /// Windows 11's short right-click menu only takes packaged apps, but it always has "Abrir con".
    /// Listing Mandarina there (OpenWithProgids) puts it one click away without changing any
    /// file's default program.
    /// </summary>
    private static void RegisterOpenWith(string exe)
    {
        using (var prog = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{ProgId}"))
        {
            prog.SetValue("", "Convertir con Mandarina");
            prog.SetValue("FriendlyTypeName", "Convertir con Mandarina");
            using (var app = prog.CreateSubKey("Application"))
            {
                app.SetValue("ApplicationName", "Mandarina");
                app.SetValue("ApplicationDescription", "Convertir y editar archivos");
                app.SetValue("ApplicationIcon", $"\"{exe}\",0");
            }
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using var open = prog.CreateSubKey(@"shell\open");
            open.SetValue("FriendlyAppName", "Mandarina");
            open.SetValue("MultiSelectModel", "Player");
            using var cmd = open.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" --open \"%1\"");
        }
        // Also as an "application" so the Open With dialog shows a proper name and icon.
        using (var appKey = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\Applications\Mandarina.exe"))
        {
            appKey.SetValue("FriendlyAppName", "Mandarina");
            using var cmd = appKey.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" --open \"%1\"");
            using var types = appKey.CreateSubKey("SupportedTypes");
            foreach (var ext in FileKinds.All.Distinct()) types.SetValue(ext, "");
        }
        foreach (var ext in FileKinds.All.Distinct())
        {
            using (var list = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{ext}\OpenWithProgids"))
                list.SetValue(ProgId, "");
            AddToOpenWithHistory(ext);
        }
    }

    private const string FileExtsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    /// <summary>
    /// Windows 11's "Abrir con" submenu only lists apps from the type's recently-used list, so a
    /// newly registered app stays hidden until picked once through "Elegir otra aplicación".
    /// Putting Mandarina.exe at the front of that list (Explorer's own MRU, not the default-app
    /// choice) makes it show up right away.
    /// </summary>
    private static void AddToOpenWithHistory(string ext)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{FileExtsKey}\{ext}\OpenWithList");
        var names = key.GetValueNames().Where(n => n.Length == 1 && char.IsLower(n[0])).ToList();
        var slot = names.FirstOrDefault(n => string.Equals(key.GetValue(n) as string, "Mandarina.exe", StringComparison.OrdinalIgnoreCase));
        if (slot is null)
        {
            var free = Enumerable.Range('a', 26).Select(c => ((char)c).ToString()).FirstOrDefault(c => !names.Contains(c));
            if (free is null) return; // list full: leave Explorer's history alone
            slot = free;
            key.SetValue(slot, "Mandarina.exe");
        }
        var mru = (key.GetValue("MRUList") as string ?? "").Replace(slot, "");
        key.SetValue("MRUList", slot + mru);
    }

    private static void RemoveFromOpenWithHistory(string ext)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{FileExtsKey}\{ext}\OpenWithList", writable: true);
        if (key is null) return;
        foreach (var n in key.GetValueNames().Where(n => n.Length == 1 && string.Equals(key.GetValue(n) as string, "Mandarina.exe", StringComparison.OrdinalIgnoreCase)))
        {
            key.DeleteValue(n);
            if (key.GetValue("MRUList") is string mru) key.SetValue("MRUList", mru.Replace(n, ""));
        }
    }

    private static void UnregisterOpenWith()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\Applications\Mandarina.exe", throwOnMissingSubKey: false);
        foreach (var ext in FileKinds.All.Distinct())
        {
            using var list = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{ext}\OpenWithProgids", writable: true);
            list?.DeleteValue(ProgId, throwOnMissingValue: false);
            RemoveFromOpenWithHistory(ext);
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Tells Explorer to reread file associations so the menus update without a restart.</summary>
    private static void NotifyAssociationsChanged() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    public static void Uninstall()
    {
        StopRunningInstance();
        Registry.CurrentUser.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
        UnregisterOpenWith();
        NotifyAssociationsChanged();
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
