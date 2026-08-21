using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Graphics;

namespace Ente.Auth.App;

internal static class WindowSizing
{
    private const double DefaultDpi = 96d;

    public static SizeInt32 ToPixels(Window window, int width, int height)
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));
        var scale = dpi == 0 ? 1d : dpi / DefaultDpi;
        return new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    public static void PlaceAtWorkAreaBottomRight(Window window, int logicalWidth, int logicalHeight, int logicalMargin)
    {
        var size = ToPixels(window, logicalWidth, logicalHeight);
        var margin = ToPixels(window, logicalMargin, logicalMargin).Width;
        var display = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - size.Width - margin,
            workArea.Y + workArea.Height - size.Height - margin,
            size.Width,
            size.Height));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
