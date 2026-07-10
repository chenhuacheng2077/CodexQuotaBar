namespace CodexQuotaBar.Models;

public enum BarPosition { Auto, Top, Bottom }
public enum AppTheme { System, Dark, Light }

public sealed class AppSettings
{
    public BarPosition Position { get; set; } = BarPosition.Auto;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool LaunchAtStartup { get; set; }
    public bool FollowCodexStartup { get; set; }
    public bool ShowResetTime { get; set; } = true;
    public bool ShowRemainingPercent { get; set; } = true;
    public int WarningThreshold { get; set; } = 20;
    public int CriticalThreshold { get; set; } = 10;
    public string? CodexExecutablePath { get; set; }
}
