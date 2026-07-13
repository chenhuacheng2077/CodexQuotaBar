using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly string _codexPath;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process? _process;
    private long _nextId;
    private int _disposed;

    public event Action<QuotaSnapshot>? SnapshotUpdated;
    public event Action<string>? StatusChanged;

    public CodexAppServerClient(string? configuredPath)
    {
        _codexPath = FindCodex(configuredPath) ?? throw new FileNotFoundException("未找到 Codex CLI");
    }

    public static string? FindCodex(string? configuredPath)
    {
        var candidates = new[]
        {
            configuredPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin", "codex.exe"),
            Environment.GetEnvironmentVariable("CODEX_PATH")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _process = Process.Start(new ProcessStartInfo(_codexPath, "app-server")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("无法启动 codex app-server");

        _ = Task.Run(ReadLoopAsync, CancellationToken.None);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";
        await RequestAsync("initialize", new { clientInfo = new { name = "Codex Quota Bar", version }, capabilities = new { } }, cancellationToken);
        await NotifyAsync("initialized", new { });
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var account = await RequestAsync("account/read", new { }, cancellationToken);
            if (!account.TryGetProperty("result", out var accountResult) || !accountResult.TryGetProperty("account", out var accountInfo))
            {
                StatusChanged?.Invoke("未检测到 ChatGPT Codex 登录状态");
                return;
            }
            if (accountInfo.TryGetProperty("type", out var type) && string.Equals(type.GetString(), "apiKey", StringComparison.OrdinalIgnoreCase))
            {
                StatusChanged?.Invoke("当前为 API Key 模式，无法读取 ChatGPT 套餐额度");
                return;
            }

            var response = await RequestAsync("account/rateLimits/read", new { }, cancellationToken);
            if (response.TryGetProperty("result", out var result))
            {
                Publish(result);
            }
            else
            {
                StatusChanged?.Invoke("额度接口未返回数据");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write($"刷新额度失败: {ex.Message}");
            StatusChanged?.Invoke("连接 Codex 失败，保留上次数据");
        }
    }

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);
            using var registration = timeoutCts.Token.Register(() =>
                completion.TrySetException(new TimeoutException($"codex app-server 请求超时: {method}")));
            return await completion.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task NotifyAsync(string method, object parameters) =>
        SendAsync(new { jsonrpc = "2.0", method, @params = parameters }, CancellationToken.None);

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited) throw new InvalidOperationException("app-server 未启动");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_process is not null && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone();
                if (message.TryGetProperty("id", out var rawId) && rawId.TryGetInt64(out var id) && _pending.TryGetValue(id, out var completion))
                {
                    completion.TrySetResult(message);
                }
                else if (message.TryGetProperty("method", out var method) &&
                         method.GetString() == "account/rateLimits/updated" &&
                         message.TryGetProperty("params", out var parameters))
                {
                    Publish(parameters);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"app-server 读取失败: {ex.Message}");
        }
        finally
        {
            FailPending("codex app-server 已断开");
        }
    }

    private void Publish(JsonElement payload)
    {
        var snapshot = QuotaNormalizer.Normalize(payload, DateTimeOffset.Now);
        SnapshotUpdated?.Invoke(snapshot);
        if (snapshot.Windows.Count == 0 && snapshot.CreditsRemaining is null)
        {
            StatusChanged?.Invoke("当前无返回额度窗口");
        }
        else
        {
            StatusChanged?.Invoke("已连接 Codex");
        }
    }

    private void FailPending(string reason)
    {
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var completion))
            {
                completion.TrySetException(new InvalidOperationException(reason));
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        FailPending("Codex 客户端已关闭");
        try
        {
            if (_process is { HasExited: false }) _process.Kill(true);
        }
        catch { }
        _process?.Dispose();
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
