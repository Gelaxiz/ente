using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Ente.Auth.App.Services;

public static class GlobalHotkey
{
    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_E = 0x45;
    private const int HOTKEY_ID = 9001;

    private static IntPtr _hWnd;
    private static SubclassProc _subclassProc;
    private static Action _onTriggered;

    public static void Register(Window window, Action onTriggered)
    {
        if (_hWnd != IntPtr.Zero) return;
        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _onTriggered = onTriggered;
        _subclassProc = WindowProc;
        
        SetWindowSubclass(_hWnd, _subclassProc, 1, IntPtr.Zero);
        RegisterHotKey(_hWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_E);
    }

    public static void Unregister()
    {
        if (_hWnd == IntPtr.Zero) return;
        UnregisterHotKey(_hWnd, HOTKEY_ID);
        RemoveWindowSubclass(_hWnd, _subclassProc, 1);
        _hWnd = IntPtr.Zero;
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _onTriggered?.Invoke();
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
