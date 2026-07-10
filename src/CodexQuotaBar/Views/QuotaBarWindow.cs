using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Views;

public sealed class QuotaBarWindow : Window
{
    private readonly TextBlock _status = Text("加载额度…", 12, Brushes.White);
    private readonly TextBlock _fiveHour = Text("5小时 暂未返回", 12, Brushes.White);
    private readonly TextBlock _weekly = Text("每周 暂未返回", 12, Brushes.White);
    private readonly Border _fiveProgress = new();
    private readonly Border _weeklyProgress = new();
    private readonly AppSettings _settings;

    public QuotaBarWindow(AppSettings settings)
    {
        _settings = settings;
        Height = 34; MinWidth = 440; Width = 620; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; AllowsTransparency = true; Background = Brushes.Transparent;
        var dark = settings.Theme != AppTheme.Light;
        var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#202123" : "#FFFFFF"));
        var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#ECECF1" : "#202123"));
        _status.Foreground = foreground; _fiveHour.Foreground = foreground; _weekly.Foreground = foreground;
        var content = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 0, 12, 0) };
        var title = Text("Codex", 12, foreground); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 14, 0); DockPanel.SetDock(title, Dock.Left); content.Children.Add(title);
        var refresh = new Button { Content = "↻", Width = 25, Height = 24, FontSize = 14, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = foreground, ToolTip = "立即刷新" }; refresh.Click += (_, _) => RefreshRequested?.Invoke(); DockPanel.SetDock(refresh, Dock.Right); content.Children.Add(refresh);
        var groups = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        groups.Children.Add(Group(_fiveHour, _fiveProgress, dark));
        groups.Children.Add(new Border { Width = 1, Height = 16, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#4D4D57" : "#D9D9E3")), Margin = new Thickness(12, 0, 12, 0) });
        groups.Children.Add(Group(_weekly, _weeklyProgress, dark));
        content.Children.Add(groups);
        Content = new Border { Background = background, CornerRadius = new CornerRadius(8), Child = content };
    }

    public event Action? RefreshRequested;
    public void Render(QuotaSnapshot snapshot)
    {
        _status.Text = snapshot.UpdatedAt == DateTimeOffset.MinValue ? "加载额度…" : $"更新于 {snapshot.UpdatedAt.LocalDateTime:t}";
        RenderWindow(_fiveHour, _fiveProgress, snapshot.FiveHour);
        RenderWindow(_weekly, _weeklyProgress, snapshot.Weekly);
        ToolTip = $"{_status.Text}。点击刷新按钮可立即更新。";
    }
    public void SetStatus(string status) { _status.Text = status; ToolTip = status; }

    private void RenderWindow(TextBlock text, Border fill, QuotaWindow? quota)
    {
        if (quota is null) { text.Text = "暂未返回"; fill.Width = 0; return; }
        var reset = !_settings.ShowResetTime || quota.ResetsAt is null ? "" : FormatReset(quota);
        text.Text = _settings.ShowRemainingPercent ? $"{quota.Label}  {quota.RemainingPercent:0}% {reset}" : $"{quota.Label}  {reset}";
        fill.Width = 76 * quota.RemainingPercent / 100;
    }
    private static string FormatReset(QuotaWindow quota)
    {
        var reset = quota.ResetsAt!.Value.LocalDateTime;
        return quota.WindowDurationMinutes >= 1440
            ? $"{reset:M月d日}重置"
            : $"{reset:HH:mm}重置";
    }
    private static FrameworkElement Group(TextBlock text, Border progress, bool dark)
    {
        progress.Height = 5; progress.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10A37F")); progress.HorizontalAlignment = HorizontalAlignment.Left;
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { new Grid { Width = 76, Height = 5, Margin = new Thickness(0, 0, 7, 0), Children = { new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#4D4D57" : "#D9D9E3")), CornerRadius = new CornerRadius(3) }, progress } }, text } };
    }
    private static TextBlock Text(string value, double size, System.Windows.Media.Brush color) => new() { Text = value, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI") };
}
