using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CodexQuotaBar.Models;
using CodexQuotaBar.Services;

namespace CodexQuotaBar.Views;

public sealed class QuotaBarWindow : Window
{
    private readonly TextBlock _title = CreateText("Codex", 12, FontWeights.SemiBold);
    private readonly TextBlock _status = CreateText("加载额度…", 11, FontWeights.Normal);
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _shell = new() { CornerRadius = new CornerRadius(UiTokens.CornerRadius) };
    private readonly StackPanel _left;
    private readonly Button _refresh;
    private readonly AppSettings _settings;
    private IntPtr _owner;
    private bool _sourceInitialized;
    private bool _dark;
    private QuotaSnapshot _quota = QuotaSnapshot.Empty;
    private TokenUsageSnapshot _tokens = TokenUsageSnapshot.Empty;
    private bool _compact;

    public QuotaBarWindow(AppSettings settings)
    {
        _settings = settings;
        _dark = UiTokens.IsDark(settings.Theme);
        Height = UiTokens.BarHeight;
        MinWidth = UiTokens.BarMinWidth;
        Width = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SourceInitialized += (_, _) => { _sourceInitialized = true; ApplyOwner(); };

        _refresh = new Button
        {
            Content = "↻",
            Width = 25,
            Height = 24,
            FontSize = 14,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ToolTip = "立即刷新"
        };
        _refresh.Click += (_, _) => RefreshRequested?.Invoke();

        var content = new DockPanel { LastChildFill = true, Margin = new Thickness(UiTokens.HorizontalPadding, 0, UiTokens.HorizontalPadding, 0) };
        _left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        _left.Children.Add(_title);
        _status.Margin = new Thickness(10, 0, 0, 0);
        _left.Children.Add(_status);
        DockPanel.SetDock(_left, Dock.Left);
        content.Children.Add(_left);
        DockPanel.SetDock(_refresh, Dock.Right);
        content.Children.Add(_refresh);
        content.Children.Add(_groups);
        _shell.Child = content;
        Content = _shell;
        SizeChanged += (_, _) =>
        {
            var compact = ActualWidth < 520;
            if (compact == _compact) return;
            _compact = compact;
            _left.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            RenderContent();
        };
        ApplyThemeColors();
    }

    public event Action? RefreshRequested;

    public void AttachTo(IntPtr owner)
    {
        _owner = owner;
        if (_sourceInitialized) ApplyOwner();
    }

    public void ApplyTheme(AppTheme theme)
    {
        _dark = UiTokens.IsDark(theme);
        ApplyThemeColors();
        if (_groups.Children.Count > 0)
        {
            // Re-render happens via Render; colors for chrome only here.
        }
    }

    public void Render(QuotaSnapshot snapshot)
    {
        _quota = snapshot;
        RenderContent();
    }

    public void RenderTokens(TokenUsageSnapshot snapshot)
    {
        _tokens = snapshot;
        RenderContent();
    }

    private void RenderContent()
    {
        var snapshot = _quota;
        if (snapshot.UpdatedAt == DateTimeOffset.MinValue)
        {
            _status.Text = "加载额度…";
        }
        else
        {
            var plan = string.IsNullOrWhiteSpace(snapshot.PlanType) ? "" : $" · {snapshot.PlanType}";
            _status.Text = $"更新于 {snapshot.UpdatedAt.LocalDateTime:t}{plan}";
        }

        _groups.Children.Clear();
        var windows = snapshot.Windows;
        if (windows.Count == 0)
        {
            _groups.Children.Add(CreateText("当前无额度窗口", 12, FontWeights.Normal));
        }
        else
        {
            for (var index = 0; index < windows.Count; index++)
            {
                if (index > 0)
                {
                    _groups.Children.Add(new Border
                    {
                        Width = 1,
                        Height = 16,
                        Background = new SolidColorBrush(_dark ? UiTokens.DarkTrack : UiTokens.LightTrack),
                        Margin = new Thickness(10, 0, 10, 0)
                    });
                }
                _groups.Children.Add(BuildGroup(windows[index]));
            }
        }

        if (_settings.ShowCredits && snapshot.CreditsRemaining is > 0)
        {
            if (_groups.Children.Count > 0)
            {
                _groups.Children.Add(new Border
                {
                    Width = 1,
                    Height = 16,
                    Background = new SolidColorBrush(_dark ? UiTokens.DarkTrack : UiTokens.LightTrack),
                    Margin = new Thickness(10, 0, 10, 0)
                });
            }
            _groups.Children.Add(CreateText($"额度币 {snapshot.CreditsRemaining:0.##}", 12, FontWeights.Normal));
        }

        if (_settings.ShowTokens && _tokens.UpdatedAt != DateTimeOffset.MinValue)
        {
            AddDivider();
            var session = $" · {(_compact ? "会" : "会话")} {FormatTokens(_tokens.SessionTotal)}";
            var prefix = _compact ? "月" : "Token 本月";
            _groups.Children.Add(CreateText($"{prefix} {FormatTokens(_tokens.MonthTotal)}{session}", 12, FontWeights.Normal));
        }

        ToolTip = BuildTooltip(snapshot, _tokens);
        ApplyThemeColors();
    }

    private void AddDivider()
    {
        if (_groups.Children.Count == 0) return;
        _groups.Children.Add(new Border
        {
            Width = 1,
            Height = 16,
            Background = new SolidColorBrush(_dark ? UiTokens.DarkTrack : UiTokens.LightTrack),
            Margin = new Thickness(_compact ? 6 : 10, 0, _compact ? 6 : 10, 0)
        });
    }

    public void SetStatus(string status)
    {
        _status.Text = status;
        ToolTip = status;
    }

    private void ApplyOwner()
    {
        if (_owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = _owner;
    }

    private FrameworkElement BuildGroup(QuotaWindow quota)
    {
        if (_compact) return CreateText($"{quota.Label} {quota.RemainingPercent:0}%", 12, FontWeights.Normal);

        var text = CreateText(FormatWindowText(quota), 12, FontWeights.Normal);
        var fill = new Border
        {
            Height = 5,
            Width = UiTokens.ProgressWidth * quota.RemainingPercent / 100,
            Background = UiTokens.ProgressBrush(quota.RemainingPercent, _settings.WarningThreshold, _settings.CriticalThreshold),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3)
        };
        var track = new Border
        {
            Background = new SolidColorBrush(_dark ? UiTokens.DarkTrack : UiTokens.LightTrack),
            CornerRadius = new CornerRadius(3)
        };
        var rail = new Grid { Width = UiTokens.ProgressWidth, Height = 5, Margin = new Thickness(0, 0, 7, 0) };
        rail.Children.Add(track);
        rail.Children.Add(fill);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { rail, text }
        };
    }

    private string FormatWindowText(QuotaWindow quota)
    {
        var reset = !_settings.ShowResetTime || quota.ResetsAt is null ? "" : $" {FormatReset(quota)}";
        return _settings.ShowRemainingPercent
            ? $"{quota.Label}  {quota.RemainingPercent:0}%{reset}"
            : $"{quota.Label}{reset}";
    }

    private static string FormatReset(QuotaWindow quota)
    {
        var resetAt = quota.ResetsAt!.Value;
        var remaining = resetAt - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        if (remaining < TimeSpan.FromDays(1))
        {
            if (remaining.TotalHours >= 1) return $"{Math.Ceiling(remaining.TotalHours):0}小时后重置";
            if (remaining.TotalMinutes >= 1) return $"{Math.Ceiling(remaining.TotalMinutes):0}分钟后重置";
            return "即将重置";
        }

        var local = resetAt.LocalDateTime;
        return quota.WindowDurationMinutes >= 20000
            ? $"{local:M月d日}重置"
            : $"{local:M月d日 HH:mm}重置";
    }

    private string BuildTooltip(QuotaSnapshot snapshot, TokenUsageSnapshot tokens)
    {
        if (snapshot.Windows.Count == 0 && snapshot.CreditsRemaining is null &&
            (!_settings.ShowTokens || tokens.UpdatedAt == DateTimeOffset.MinValue))
        {
            return _status.Text;
        }

        var lines = new List<string> { _status.Text };
        foreach (var window in snapshot.Windows)
        {
            var reset = window.ResetsAt is null ? "重置时间未知" : $"重置 {window.ResetsAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}";
            lines.Add($"{window.Label}: 剩余 {window.RemainingPercent:0}% · 已用 {window.UsedPercent:0}% · {reset}");
        }
        if (snapshot.CreditsRemaining is not null)
        {
            lines.Add($"额度币余额: {snapshot.CreditsRemaining:0.##}");
        }
        if (_settings.ShowTokens && tokens.UpdatedAt != DateTimeOffset.MinValue)
        {
            lines.Add($"本月 Token: {tokens.MonthTotal:N0}");
            var project = tokens.ProjectName is null ? "" : $"（{tokens.ProjectName}）";
            lines.Add($"当前会话{project}: {tokens.SessionTotal:N0}");
        }
        lines.Add("点击 ↻ 可立即刷新。窗口数量会随 Codex 当前返回的额度策略变化。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatTokens(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.#}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
        >= 1_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString()
    };

    private void ApplyThemeColors()
    {
        var foreground = new SolidColorBrush(_dark ? UiTokens.DarkText : UiTokens.LightText);
        var muted = new SolidColorBrush(_dark ? UiTokens.DarkMuted : UiTokens.LightMuted);
        _shell.Background = new SolidColorBrush(_dark ? UiTokens.DarkBackground : UiTokens.LightBackground);
        _title.Foreground = foreground;
        _status.Foreground = muted;
        _refresh.Foreground = foreground;
        foreach (var child in _groups.Children.OfType<FrameworkElement>())
        {
            ApplyForeground(child, foreground);
        }
    }

    private static void ApplyForeground(FrameworkElement element, Brush foreground)
    {
        if (element is TextBlock text) text.Foreground = foreground;
        if (element is Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>()) ApplyForeground(child, foreground);
        }
    }

    private static TextBlock CreateText(string value, double size, FontWeight weight) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = new FontFamily("Segoe UI")
    };
}
