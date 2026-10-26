using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Mandarina.Interop;

public enum WheelMode { Convert, Edit }

/// <summary>
/// Watches global mouse and keyboard input to spot the gesture "start dragging something,
/// then tap Shift (convert) or Alt (edit tools)". The hooks only observe; they never swallow input.
/// </summary>
internal sealed class DragWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyProc;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;

    private bool _leftDown;
    private bool _dragging;
    private Native.POINT _downPt;
    private Native.POINT _lastPt;
    private bool _shiftHeld;
    private bool _altHeld;

    /// <summary>Raised on the UI thread when the gesture fires, with the cursor position in physical pixels.</summary>
    public event Action<WheelMode, Native.POINT>? Triggered;
    public event Action? MouseReleased;
    public event Action<Native.POINT>? MousePressed;
    public event Action? Escape;

    public bool Enabled { get; set; } = true;

    public DragWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _mouseProc = MouseHook;
        _keyProc = KeyHook;
    }

    public void Start()
    {
        var module = Native.GetModuleHandle(null);
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, module, 0);
        if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
            throw new InvalidOperationException($"No se pudieron instalar los hooks de entrada (error {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case Native.WM_LBUTTONDOWN:
                    _leftDown = true;
                    _dragging = false;
                    _downPt = data.pt;
                    var downPt = data.pt;
                    _dispatcher.BeginInvoke(() => MousePressed?.Invoke(downPt));
                    break;
                case Native.WM_MOUSEMOVE:
                    _lastPt = data.pt;
                    if (_leftDown && !_dragging)
                    {
                        int dx = Math.Abs(data.pt.X - _downPt.X), dy = Math.Abs(data.pt.Y - _downPt.Y);
                        if (dx > Native.GetSystemMetrics(Native.SM_CXDRAG) || dy > Native.GetSystemMetrics(Native.SM_CYDRAG))
                            _dragging = true;
                    }
                    break;
                case Native.WM_LBUTTONUP:
                    _leftDown = false;
                    _dragging = false;
                    _dispatcher.BeginInvoke(() => MouseReleased?.Invoke());
                    break;
            }
        }
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            int vk = (int)data.vkCode;

            if (vk is Native.VK_LSHIFT or Native.VK_RSHIFT)
            {
                // Only the first press counts; auto-repeat keeps sending keydowns while held.
                if (down && !_shiftHeld) { _shiftHeld = true; Fire(WheelMode.Convert); }
                if (up) _shiftHeld = false;
            }
            else if (vk is Native.VK_LMENU or Native.VK_RMENU)
            {
                if (down && !_altHeld) { _altHeld = true; Fire(WheelMode.Edit); }
                if (up) _altHeld = false;
            }
            else if (vk == Native.VK_ESCAPE && down)
            {
                _dispatcher.BeginInvoke(() => Escape?.Invoke());
            }
        }
        return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private void Fire(WheelMode mode)
    {
        Log.Write($"fire {mode} enabled={Enabled} dragging={_dragging}");
        if (!Enabled || !_dragging) return;
        var pt = _lastPt;
        _dispatcher.BeginInvoke(() => Triggered?.Invoke(mode, pt));
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
    }
}
