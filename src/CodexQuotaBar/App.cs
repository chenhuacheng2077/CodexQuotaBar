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
    private static readonly TimeSpan QuotaRefreshInterval = TimeSpan.FromSeconds(15);
    private readonly SettingsService _settingsService = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, int> _warned = new();
    private readonly TokenUsageReader _tokenUsageReader = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AppSettings _settings = null!;
    private QuotaBarWindow _bar = null!;
    private TargetWindowTracker _tracker = null!;
    private volatile CodexAppServerClient? _client;
    private NotifyIcon _tray = null!;
    private QuotaSnapshot _snapshot = QuotaSnapshot.Empty;
    private TokenUsageSnapshot _tokenUsage = TokenUsageSnapshot.Empty;
    private int _tokenRefreshBusy;
    private Task? _connectionLoop;
    private bool _userHidden;
    private ToolStripMenuItem? _trayStartupItem;
    private System.Windows.Controls.MenuItem? _barStartupItem;

    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "Local\\CodexQuotaBar", out var firstInstance);
        if (!firstInstance) return;
        new App().Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _settings = _settingsService.Load();
        _userHidden = _settings.BarHidden;
        _bar = new QuotaBarWindow(_settings);
        _bar.RefreshRequested += () => _ = RefreshAsync();
        BuildBarMenu();

        _tracker = new TargetWindowTracker();
        _tracker.TargetChanged += target => Dispatcher.BeginInvoke(() => SetTarget(target));
        _tracker.TargetMoved += () => Dispatcher.BeginInvoke(PositionBar);
        _tracker.TargetVisibilityChanged += visible => Dispatcher.BeginInvoke(() =>
        {
            if (visible) PositionBar();
            ApplyBarVisibility();
        });

        BuildTray();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _connectionLoop = RunConnectionLoopAsync(_lifetime.Token);
        _ = RefreshTokenUsageAsync(_lifetime.Token);
        _ = RefreshPeriodicallyAsync(_lifetime.Token);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) _ = RefreshAsync();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (_settings.Theme == AppTheme.System &&
            (args.Category == UserPreferenceCategory.General || args.Category == UserPreferenceCategory.Color))
        {
            Dispatcher.BeginInvoke(() =>
            {
                _bar.ApplyTheme(AppTheme.System);
                _bar.Render(_snapshot);
            });
        }
    }

    private void SetTarget(IntPtr target)
    {
        if (target == IntPtr.Zero)
        {
            ApplyBarVisibility();
            return;
        }

        _bar.AttachTo(target);
        PositionBar();
        ApplyBarVisibility();
        _ = RefreshAsync();
    }

    private void ApplyBarVisibility()
    {
        var shouldShow = !_userHidden && _tracker.Target != IntPtr.Zero && _tracker.TargetVisible;
        if (shouldShow)
        {
            PositionBar();
            if (!_bar.IsVisible) _bar.Show();
        }
        else if (_bar.IsVisible)
        {
            _bar.Hide();
        }
    }

    private void SetUserHidden(bool hidden)
    {
        _userHidden = hidden;
        _settings.BarHidden = hidden;
        _settingsService.Save(_settings);
        ApplyBarVisibility();
    }

    private void PositionBar() => OverlayPositioner.Position(_bar, _tracker.Target, _settings.Position);

    private void UpdateSnapshot(QuotaSnapshot snapshot)
    {
        _snapshot = snapshot;
        _bar.Render(snapshot);
        foreach (var window in snapshot.Windows) Warn(window);
    }

    private void Warn(QuotaWindow quota)
    {
        if (!quota.HasUsageData || quota.ResetsAt is null) return;
        var key = $"{quota.Id}:{quota.ResetsAt:O}";
        var level = quota.RemainingPercent <= _settings.CriticalThreshold
            ? _settings.CriticalThreshold
            : quota.RemainingPercent <= _settings.WarningThreshold
                ? _settings.WarningThreshold
                : 0;
        if (level > 0 && (!_warned.TryGetValue(key, out var seen) || level < seen))
        {
            _warned[key] = level;
            _tray.ShowBalloonTip(4000, "Codex 额度提醒", $"{quota.Label} 剩余 {quota.RemainingPercent:0}%", ToolTipIcon.Warning);
        }
    }

    private async Task RefreshAsync()
    {
        if (_lifetime.IsCancellationRequested || !await _refreshGate.WaitAsync(0)) return;
        try
        {
            var client = _client;
            if (client is not null) await client.RefreshAsync(_lifetime.Token);
            await RefreshTokenUsageAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write($"手动刷新失败: {ex.Message}");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        var retry = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            CodexAppServerClient? client = null;
            try
            {
                await Dispatcher.InvokeAsync(() => _bar.SetStatus(retry == 0 ? "正在连接 Codex…" : "连接中断，正在重试…"));
                client = new CodexAppServerClient(_settings.CodexExecutablePath);
                client.SnapshotUpdated += snapshot => Dispatcher.BeginInvoke(() => UpdateSnapshot(snapshot));
                client.StatusChanged += status => Dispatcher.BeginInvoke(() => _bar.SetStatus(status));
                await client.StartAsync(cancellationToken);
                _client = client;
                retry = 0;
                await client.WaitForDisconnectAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    retry = 1;
                    await Dispatcher.InvokeAsync(() => _bar.SetStatus("连接中断，正在重试…"));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                retry++;
                AppLog.Write($"连接 Codex 失败: {ex.Message}");
                await Dispatcher.InvokeAsync(() => _bar.SetStatus("连接失败，保留上次数据并重试"));
            }
            finally
            {
                if (ReferenceEquals(_client, client)) _client = null;
                if (client is not null) await client.DisposeAsync();
            }

            if (cancellationToken.IsCancellationRequested) break;
            var delay = TimeSpan.FromSeconds(retry switch { <= 1 => 2, 2 => 5, 3 => 15, _ => 30 });
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RefreshTokenUsageAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _tokenRefreshBusy, 1) != 0) return;
        try
        {
            var snapshot = await Task.Run(() => _tokenUsageReader.Read(DateTimeOffset.Now), cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                _tokenUsage = snapshot;
                _bar.RenderTokens(snapshot);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write($"读取 Token 用量失败: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _tokenRefreshBusy, 0);
        }
    }

    private async Task RefreshPeriodicallyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(QuotaRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示额度条", null, (_, _) => SetUserHidden(false));
        menu.Items.Add("隐藏额度条", null, (_, _) => SetUserHidden(true));

        var position = new ToolStripMenuItem("位置");
        foreach (var mode in Enum.GetValues<BarPosition>())
        {
            position.DropDownItems.Add(mode.ToString(), null, (_, _) =>
            {
                _settings.Position = mode;
                _settingsService.Save(_settings);
                PositionBar();
            });
        }
        menu.Items.Add(position);
        menu.Items.Add("立即刷新", null, (_, _) => _ = RefreshAsync());

        var settings = new ToolStripMenuItem("设置");
        var theme = new ToolStripMenuItem("主题");
        foreach (var value in Enum.GetValues<AppTheme>())
        {
            theme.DropDownItems.Add(value.ToString(), null, (_, _) =>
            {
                _settings.Theme = value;
                _settingsService.Save(_settings);
                _bar.ApplyTheme(value);
                _bar.Render(_snapshot);
            });
        }
        settings.DropDownItems.Add(theme);
        settings.DropDownItems.Add("显示重置时间", null, (_, _) =>
        {
            _settings.ShowResetTime = !_settings.ShowResetTime;
            _settingsService.Save(_settings);
            _bar.Render(_snapshot);
        });
        settings.DropDownItems.Add("显示剩余百分比", null, (_, _) =>
        {
            _settings.ShowRemainingPercent = !_settings.ShowRemainingPercent;
            _settingsService.Save(_settings);
            _bar.Render(_snapshot);
        });
        settings.DropDownItems.Add("显示额度币", null, (_, _) =>
        {
            _settings.ShowCredits = !_settings.ShowCredits;
            _settingsService.Save(_settings);
            _bar.Render(_snapshot);
        });
        settings.DropDownItems.Add("显示 Token", null, (_, _) =>
        {
            _settings.ShowTokens = !_settings.ShowTokens;
            _settingsService.Save(_settings);
            _bar.RenderTokens(_tokenUsage);
        });
        menu.Items.Add(settings);
        _trayStartupItem = new ToolStripMenuItem("开机启动额度条")
        {
            CheckOnClick = true,
            Checked = IsLaunchAtStartupEnabled()
        };
        _trayStartupItem.Click += (_, _) => SetLaunchAtStartup(_trayStartupItem.Checked);
        menu.Items.Add(_trayStartupItem);
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Codex Quota Bar",
            ContextMenuStrip = menu
        };
    }

    private void BuildBarMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var refresh = new System.Windows.Controls.MenuItem { Header = "立即刷新" };
        refresh.Click += (_, _) => _ = RefreshAsync();
        menu.Items.Add(refresh);

        var position = new System.Windows.Controls.MenuItem { Header = "位置" };
        foreach (var mode in Enum.GetValues<BarPosition>())
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = mode.ToString(),
                IsCheckable = true,
                IsChecked = _settings.Position == mode
            };
            item.Click += (_, _) =>
            {
                _settings.Position = mode;
                _settingsService.Save(_settings);
                PositionBar();
            };
            position.Items.Add(item);
        }
        menu.Items.Add(position);

        var reset = new System.Windows.Controls.MenuItem
        {
            Header = "显示重置时间",
            IsCheckable = true,
            IsChecked = _settings.ShowResetTime
        };
        reset.Click += (_, _) =>
        {
            _settings.ShowResetTime = reset.IsChecked;
            _settingsService.Save(_settings);
            _bar.Render(_snapshot);
        };
        menu.Items.Add(reset);

        var percentage = new System.Windows.Controls.MenuItem
        {
            Header = "显示剩余百分比",
            IsCheckable = true,
            IsChecked = _settings.ShowRemainingPercent
        };
        percentage.Click += (_, _) =>
        {
            _settings.ShowRemainingPercent = percentage.IsChecked;
            _settingsService.Save(_settings);
            _bar.Render(_snapshot);
        };
        menu.Items.Add(percentage);

        var credits = new System.Windows.Controls.MenuItem
        {
            Header = "显示额度币",
            IsCheckable = true,
            IsChecked = _settings.ShowCredits
        };
        credits.Click += (_, _) =>
        {
            _settings.ShowCredits = credits.IsChecked;
            _settingsService.Save(_settings);
            _bar.Render(_snapshot);
        };
        menu.Items.Add(credits);

        var tokens = new System.Windows.Controls.MenuItem
        {
            Header = "显示 Token",
            IsCheckable = true,
            IsChecked = _settings.ShowTokens
        };
        tokens.Click += (_, _) =>
        {
            _settings.ShowTokens = tokens.IsChecked;
            _settingsService.Save(_settings);
            _bar.RenderTokens(_tokenUsage);
        };
        menu.Items.Add(tokens);

        menu.Items.Add(new System.Windows.Controls.Separator());
        var hide = new System.Windows.Controls.MenuItem { Header = "隐藏额度条" };
        hide.Click += (_, _) => SetUserHidden(true);
        menu.Items.Add(hide);

        _barStartupItem = new System.Windows.Controls.MenuItem
        {
            Header = "开机启动额度条",
            IsCheckable = true,
            IsChecked = IsLaunchAtStartupEnabled()
        };
        _barStartupItem.Click += (_, _) => SetLaunchAtStartup(_barStartupItem.IsChecked);
        menu.Items.Add(_barStartupItem);
        _bar.ContextMenu = menu;
    }

    private bool IsLaunchAtStartupEnabled() => _settings.LaunchAtStartup || _settings.FollowCodexStartup;

    private void SetLaunchAtStartup(bool enabled)
    {
        _settings.FollowCodexStartup = enabled;
        _settings.LaunchAtStartup = enabled;
        _settingsService.Save(_settings);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)!;
        if (enabled)
        {
            key.SetValue("CodexQuotaBar", $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue("CodexQuotaBar", false);
        }
        if (_trayStartupItem is not null) _trayStartupItem.Checked = enabled;
        if (_barStartupItem is not null) _barStartupItem.IsChecked = enabled;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _lifetime.Cancel();
        _tray?.Dispose();
        _tracker?.Dispose();
        if (_client is not null) await _client.DisposeAsync();
        if (_connectionLoop is not null)
        {
            try { await _connectionLoop; }
            catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
        base.OnExit(e);
    }
}
