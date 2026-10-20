using System.Diagnostics;
using Microsoft.Win32;

namespace Mandarina.Interop;

/// <summary>
/// Windows 11 only shows packaged (MSIX) apps in its short right-click menu. The documented per-user
/// switch back to the full classic menu makes "Convertir con Mandarina" appear directly, without
/// "Mostrar más opciones". It affects every right-click menu, so it is opt-in and reversible.
/// </summary>
internal static class ClassicMenu
{
    private const string Key = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string MarkerKey = @"Software\Mandarina";

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k is not null;
        }
    }

    /// <summary>Whether Mandarina turned it on (so uninstall knows to turn it back off).</summary>
    public static bool EnabledByMandarina
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(MarkerKey);
            return k?.GetValue("ClassicMenu") is 1;
        }
    }

    public static void Set(bool on)
    {
        if (on)
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            k.SetValue("", "");
            using var m = Registry.CurrentUser.CreateSubKey(MarkerKey);
            m.SetValue("ClassicMenu", 1, RegistryValueKind.DWord);
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(Key[..Key.LastIndexOf('\\')], throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(MarkerKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>The change takes effect once Explorer restarts (open folder windows close).</summary>
    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { }
        }
        Thread.Sleep(800);
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }
}
