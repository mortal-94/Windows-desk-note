using System.ComponentModel;
using DeskNote.App.Interop;
using DeskNote.Core.Input;

namespace DeskNote.App.Services;

internal sealed class GlobalHotkeyService(nint windowHandle) : IDisposable
{
    private HotkeyGesture? _registeredGesture;

    public bool TryRegister(HotkeyGesture gesture, out string error)
    {
        HotkeyGesture? previous = _registeredGesture;
        if (previous == gesture)
        {
            error = string.Empty;
            return true;
        }

        Unregister();
        uint modifiers = (uint)(gesture.Modifiers | HotkeyModifiers.NoRepeat);
        if (NativeMethods.RegisterHotKey(windowHandle, NativeMethods.HotkeyId, modifiers, gesture.VirtualKey))
        {
            _registeredGesture = gesture;
            error = string.Empty;
            return true;
        }

        int win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (previous is HotkeyGesture previousGesture)
        {
            uint previousModifiers = (uint)(previousGesture.Modifiers | HotkeyModifiers.NoRepeat);
            if (NativeMethods.RegisterHotKey(windowHandle, NativeMethods.HotkeyId, previousModifiers, previousGesture.VirtualKey))
            {
                _registeredGesture = previousGesture;
            }
        }

        error = win32Error == 1409
            ? $"快捷键 {gesture} 已被其他程序占用。"
            : $"无法注册快捷键 {gesture}：{new Win32Exception(win32Error).Message}";
        return false;
    }

    public void Dispose() => Unregister();

    private void Unregister()
    {
        if (_registeredGesture is not null)
        {
            _ = NativeMethods.UnregisterHotKey(windowHandle, NativeMethods.HotkeyId);
            _registeredGesture = null;
        }
    }
}
