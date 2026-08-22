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
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const int HOTKEY_ID = 9001;

    private static IntPtr _hWnd;
    private static SubclassProc? _subclassProc;
    private static Action? _onTriggered;

    public static void Register(Window window, string shortcut, Action onTriggered)
    {
        if (_hWnd != IntPtr.Zero) Unregister();
        
        uint modifiers = 0;
        uint vk = 0;
        
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_CONTROL;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_SHIFT;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_ALT;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_WIN;
            else if (part.Length == 1) vk = part.ToUpperInvariant()[0];
            else if (Enum.TryParse<Windows.System.VirtualKey>(part, true, out var key)) vk = (uint)key;
        }
        if (vk == 0) return; // Invalid shortcut

        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _onTriggered = onTriggered;
        _subclassProc = WindowProc;
        
        SetWindowSubclass(_hWnd, _subclassProc, 1, IntPtr.Zero);
        RegisterHotKey(_hWnd, HOTKEY_ID, modifiers, vk);
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
