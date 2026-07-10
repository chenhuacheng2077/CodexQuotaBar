using Microsoft.Win32;
using System.Windows;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using CodexQuotaBar.Models;
using CodexQuotaBar.Services;
using CodexQuotaBar.Views;

namespace CodexQuotaBar;

public sealed class App : System.Windows.Application
{
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = null!;
    private QuotaBarWindow _bar = null!;
    private TargetWindowTracker _tracker = null!;
    private CodexAppServerClient? _client;
    private NotifyIcon _tray = null!;
    private QuotaSnapshot _snapshot = QuotaSnapshot.Empty;
    private readonly Dictionary<string, int> _warned = new();

    [STAThread] public static void Main() => new App().Run();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _settings = _settingsService.Load();
        _bar = new QuotaBarWindow(_settings); _bar.RefreshRequested += () => _ = RefreshAsync();
        BuildBarMenu();
        _tracker = new TargetWindowTracker();
        _tracker.TargetChanged += target => Dispatcher.Invoke(() => SetTarget(target));
        _tracker.TargetMoved += () => Dispatcher.Invoke(PositionBar);
        _tracker.TargetVisibilityChanged += visible => Dispatcher.Invoke(() => { if (visible) { PositionBar(); _bar.Show(); } else _bar.Hide(); });
        BuildTray();
        SystemEvents.PowerModeChanged += (_, args) => { if (args.Mode == PowerModes.Resume) _ = RefreshAsync(); };
        try
        {
            _client = new CodexAppServerClient(_settings.CodexExecutablePath);
            _client.SnapshotUpdated += snapshot => Dispatcher.Invoke(() => UpdateSnapshot(snapshot));
            _client.StatusChanged += status => Dispatcher.Invoke(() => _bar.SetStatus(status));
            await _client.StartAsync(CancellationToken.None);
            _ = RefreshEveryMinuteAsync();
        }
        catch (Exception ex) { _bar.SetStatus(ex.Message); AppLog.Write(ex.Message); }
    }

    private void SetTarget(IntPtr target) { if (target == IntPtr.Zero) { _bar.Hide(); return; } PositionBar(); if (!_bar.IsVisible) _bar.Show(); _ = RefreshAsync(); }
    private void PositionBar() { OverlayPositioner.Position(_bar, _tracker.Target, _settings.Position); }
    private void UpdateSnapshot(QuotaSnapshot snapshot)
    {
        _snapshot = snapshot; _bar.Render(snapshot); Warn(snapshot.FiveHour); Warn(snapshot.Weekly);
    }
    private void Warn(QuotaWindow? quota)
    {
        if (quota?.ResetsAt is null) return;
        var key = $"{quota.Id}:{quota.ResetsAt:O}";
        var level = quota.RemainingPercent <= _settings.CriticalThreshold ? _settings.CriticalThreshold : quota.RemainingPercent <= _settings.WarningThreshold ? _settings.WarningThreshold : 0;
        if (level > 0 && (!_warned.TryGetValue(key, out var seen) || level < seen)) { _warned[key] = level; _tray.ShowBalloonTip(4000, "Codex 额度提醒", $"{quota.Label} 剩余 {quota.RemainingPercent:0}%", ToolTipIcon.Warning); }
    }
    private async Task RefreshAsync() { if (_client is not null) await _client.RefreshAsync(CancellationToken.None); }
    private async Task RefreshEveryMinuteAsync() { using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1)); while (await timer.WaitForNextTickAsync()) await RefreshAsync(); }
    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示额度条", null, (_, _) => { PositionBar(); _bar.Show(); }); menu.Items.Add("隐藏额度条", null, (_, _) => _bar.Hide());
        var position = new ToolStripMenuItem("位置"); foreach (var mode in Enum.GetValues<BarPosition>()) position.DropDownItems.Add(mode.ToString(), null, (_, _) => { _settings.Position = mode; _settingsService.Save(_settings); PositionBar(); }); menu.Items.Add(position);
        menu.Items.Add("立即刷新", null, (_, _) => _ = RefreshAsync());
        var settings = new ToolStripMenuItem("设置");
        var theme = new ToolStripMenuItem("主题"); foreach (var value in Enum.GetValues<AppTheme>()) theme.DropDownItems.Add(value.ToString(), null, (_, _) => { _settings.Theme = value; _settingsService.Save(_settings); _bar.SetStatus("主题将在下次启动时生效"); }); settings.DropDownItems.Add(theme);
        settings.DropDownItems.Add("显示重置时间", null, (_, _) => { _settings.ShowResetTime = !_settings.ShowResetTime; _settingsService.Save(_settings); _bar.Render(_snapshot); });
        settings.DropDownItems.Add("显示剩余百分比", null, (_, _) => { _settings.ShowRemainingPercent = !_settings.ShowRemainingPercent; _settingsService.Save(_settings); _bar.Render(_snapshot); }); menu.Items.Add(settings);
        menu.Items.Add("随 Codex 启动", null, (_, _) => ToggleFollowCodexStartup()); menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Codex Quota Bar", ContextMenuStrip = menu };
    }
    private void BuildBarMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var refresh = new System.Windows.Controls.MenuItem { Header = "立即刷新" }; refresh.Click += (_, _) => _ = RefreshAsync(); menu.Items.Add(refresh);
        var position = new System.Windows.Controls.MenuItem { Header = "位置" };
        foreach (var mode in Enum.GetValues<BarPosition>())
        {
            var item = new System.Windows.Controls.MenuItem { Header = mode.ToString(), IsCheckable = true, IsChecked = _settings.Position == mode };
            item.Click += (_, _) => { _settings.Position = mode; _settingsService.Save(_settings); PositionBar(); };
            position.Items.Add(item);
        }
        menu.Items.Add(position);
        var reset = new System.Windows.Controls.MenuItem { Header = "显示重置时间", IsCheckable = true, IsChecked = _settings.ShowResetTime };
        reset.Click += (_, _) => { _settings.ShowResetTime = reset.IsChecked; _settingsService.Save(_settings); _bar.Render(_snapshot); }; menu.Items.Add(reset);
        var percentage = new System.Windows.Controls.MenuItem { Header = "显示剩余百分比", IsCheckable = true, IsChecked = _settings.ShowRemainingPercent };
        percentage.Click += (_, _) => { _settings.ShowRemainingPercent = percentage.IsChecked; _settingsService.Save(_settings); _bar.Render(_snapshot); }; menu.Items.Add(percentage);
        menu.Items.Add(new System.Windows.Controls.Separator());
        var hide = new System.Windows.Controls.MenuItem { Header = "隐藏额度条" }; hide.Click += (_, _) => _bar.Hide(); menu.Items.Add(hide);
        var follow = new System.Windows.Controls.MenuItem { Header = "随 Codex 启动", IsCheckable = true, IsChecked = _settings.FollowCodexStartup };
        follow.Click += (_, _) => ToggleFollowCodexStartup(); menu.Items.Add(follow);
        _bar.ContextMenu = menu;
    }
    private void ToggleFollowCodexStartup()
    {
        _settings.FollowCodexStartup = !_settings.FollowCodexStartup;
        _settings.LaunchAtStartup = _settings.FollowCodexStartup;
        _settingsService.Save(_settings);
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true)!;
        if (_settings.FollowCodexStartup) key.SetValue("CodexQuotaBar", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("CodexQuotaBar", false);
    }
    protected override async void OnExit(ExitEventArgs e) { _tray?.Dispose(); _tracker?.Dispose(); if (_client is not null) await _client.DisposeAsync(); base.OnExit(e); }
}
