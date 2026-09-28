using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using InstructMe.Native;

namespace InstructMe.Input;

/// <summary>Registers a system-wide shortcut on a hidden message-only window.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x4D49; // "MI"
    private readonly HwndSource _source;

    public event Action? Pressed;

    public GlobalHotkey(HotkeyGesture gesture)
    {
        var parameters = new HwndSourceParameters("InstructMe.Hotkey")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, gesture.Modifiers | NativeMethods.MOD_NOREPEAT, gesture.VirtualKey))
        {
            int error = Marshal.GetLastWin32Error();
            _source.Dispose();
            throw new Win32Exception(error, $"The shortcut {gesture.Text} is not available. Another app may use it.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.Dispose();
    }
}
