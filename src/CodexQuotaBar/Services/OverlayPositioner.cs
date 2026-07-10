using System.Runtime.InteropServices;
using System.Windows;

namespace CodexQuotaBar.Services;

public static class OverlayPositioner
{
    public static void Position(Window bar, IntPtr target, Models.BarPosition mode)
    {
        if (target == IntPtr.Zero || !GetWindowRect(target, out var rect)) return;
        var scale = GetDpiForWindow(target) / 96d;
        var targetWidth = (rect.Right - rect.Left) / scale;
        var width = Math.Clamp(targetWidth - 420, 440, 620);
        var height = 34d;
        bar.Width = width;
        bar.Left = rect.Left / scale + (targetWidth - width) / 2;
        var placeAtBottom = mode == Models.BarPosition.Bottom;
        bar.Top = placeAtBottom ? rect.Bottom / scale - height - 8 : rect.Top / scale + 4;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
}
