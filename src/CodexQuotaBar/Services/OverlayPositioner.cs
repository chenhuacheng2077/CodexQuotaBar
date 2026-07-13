using System.Runtime.InteropServices;
using System.Windows;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Services;

public static class OverlayPositioner
{
    public static void Position(Window bar, IntPtr target, BarPosition mode)
    {
        if (target == IntPtr.Zero || !GetWindowRect(target, out var rect)) return;

        var scale = Math.Max(1d, GetDpiForWindow(target) / 96d);
        var targetWidth = (rect.Right - rect.Left) / scale;
        var targetHeight = (rect.Bottom - rect.Top) / scale;
        var width = Math.Clamp(targetWidth - 420, UiTokens.BarMinWidth, UiTokens.BarMaxWidth);
        var height = UiTokens.BarHeight;

        bar.Width = width;
        bar.Left = rect.Left / scale + (targetWidth - width) / 2;

        var placeAtBottom = mode switch
        {
            BarPosition.Bottom => true,
            BarPosition.Top => false,
            _ => targetHeight < 720
        };

        bar.Top = placeAtBottom
            ? rect.Bottom / scale - height - 8
            : rect.Top / scale + 4;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
