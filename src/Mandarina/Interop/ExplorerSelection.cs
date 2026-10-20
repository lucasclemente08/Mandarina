using System.Runtime.InteropServices;

namespace Mandarina.Interop;

/// <summary>Reads the files selected in the foreground Explorer window (or on the desktop).</summary>
internal static class ExplorerSelection
{
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? obj);
    }

    // Only the first IOleWindow method is needed, so only it is declared.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
    }

    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    public static IReadOnlyList<string> Get()
    {
        var fg = Native.GetForegroundWindow();
        var cls = Native.ClassOf(fg);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            if (cls is "Progman" or "WorkerW") return FromDesktop(shell);
            if (cls is not ("CabinetWClass" or "ExploreWClass")) return [];

            // Windows 11 Explorer has tabs: every tab reports the same top-level HWND,
            // so match the active tab's ShellTabWindowClass child as well.
            var activeTab = Native.FindWindowEx(fg, IntPtr.Zero, "ShellTabWindowClass", null);
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    if ((IntPtr)(long)w.HWND != fg) continue;
                    if (activeTab != IntPtr.Zero && TabOf(w) is { } tab && tab != activeTab) continue;
                    return Paths(w.Document);
                }
                catch (COMException) { }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            }
            return [];
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static IReadOnlyList<string> FromDesktop(dynamic shell)
    {
        // ShellWindows.FindWindowSW(CSIDL_DESKTOP, …, SWC_DESKTOP, …, SWFO_NEEDDISPATCH) returns the desktop's browser.
        object loc = 0, root = Type.Missing;
        int hwnd = 0;
        dynamic windows = shell.Windows();
        dynamic? desktop = windows.FindWindowSW(ref loc, ref root, 8, ref hwnd, 1);
        return desktop is null ? [] : Paths(desktop.Document);
    }

    private static IntPtr? TabOf(object browserWindow)
    {
        if (browserWindow is not IServiceProvider sp) return null;
        Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
        if (sp.QueryService(ref sid, ref iid, out var obj) != 0 || obj is not IShellBrowser browser) return null;
        return browser.GetWindow(out var hwnd) == 0 ? hwnd : null;
    }

    private static IReadOnlyList<string> Paths(dynamic folderView)
    {
        var result = new List<string>();
        foreach (dynamic item in folderView.SelectedItems())
        {
            string path = item.Path;
            if (File.Exists(path)) result.Add(path);
        }
        return result;
    }
}
