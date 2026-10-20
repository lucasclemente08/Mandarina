using System.Windows.Interop;

namespace Mandarina.Interop;

/// <summary>A system-wide hotkey delivered to a hidden message-only window.</summary>
internal sealed class Hotkey : IDisposable
{
    private const int Id = 0x4D41; // "MA"
    private readonly HwndSource _window;
    private readonly bool _registered;

    public event Action? Pressed;

    public Hotkey(uint modifiers, uint vk)
    {
        _window = new HwndSource(new HwndSourceParameters("Mandarina hotkey") { ParentWindow = new IntPtr(-3) }); // HWND_MESSAGE
        _window.AddHook(WndProc);
        _registered = Native.RegisterHotKey(_window.Handle, Id, modifiers | Native.MOD_NOREPEAT, vk);
    }

    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) Native.UnregisterHotKey(_window.Handle, Id);
        _window.Dispose();
    }
}
