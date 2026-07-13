using System.Windows.Media;

namespace CodexQuotaBar.Services;

public static class UiTokens
{
    public const double BarHeight = 34;
    public const double BarMinWidth = 360;
    public const double BarMaxWidth = 640;
    public const double ProgressWidth = 76;
    public const double HorizontalPadding = 12;
    public const double CornerRadius = 8;

    public static readonly Color Accent = (Color)ColorConverter.ConvertFromString("#10A37F");
    public static readonly Color Warning = (Color)ColorConverter.ConvertFromString("#F59E0B");
    public static readonly Color Critical = (Color)ColorConverter.ConvertFromString("#EF4444");
    public static readonly Color DarkBackground = (Color)ColorConverter.ConvertFromString("#202123");
    public static readonly Color LightBackground = (Color)ColorConverter.ConvertFromString("#FFFFFF");
    public static readonly Color DarkText = (Color)ColorConverter.ConvertFromString("#ECECF1");
    public static readonly Color LightText = (Color)ColorConverter.ConvertFromString("#202123");
    public static readonly Color DarkMuted = (Color)ColorConverter.ConvertFromString("#A7A7B4");
    public static readonly Color LightMuted = (Color)ColorConverter.ConvertFromString("#6B6B75");
    public static readonly Color DarkTrack = (Color)ColorConverter.ConvertFromString("#4D4D57");
    public static readonly Color LightTrack = (Color)ColorConverter.ConvertFromString("#D9D9E3");

    public static bool IsDark(Models.AppTheme theme)
    {
        if (theme == Models.AppTheme.Dark) return true;
        if (theme == Models.AppTheme.Light) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is not int light || light == 0;
        }
        catch
        {
            return true;
        }
    }

    public static SolidColorBrush ProgressBrush(double remainingPercent, int warningThreshold, int criticalThreshold)
    {
        if (remainingPercent <= criticalThreshold) return new SolidColorBrush(Critical);
        if (remainingPercent <= warningThreshold) return new SolidColorBrush(Warning);
        return new SolidColorBrush(Accent);
    }
}
