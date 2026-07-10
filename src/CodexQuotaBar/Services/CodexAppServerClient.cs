using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string _codexPath;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private long _nextId;
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
        _process = Process.Start(new ProcessStartInfo(_codexPath, "app-server") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("无法启动 codex app-server");
        _ = Task.Run(ReadLoopAsync);
        await RequestAsync("initialize", new { clientInfo = new { name = "Codex Quota Bar", version = "1.0.0" }, capabilities = new { } }, cancellationToken);
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
            if (response.TryGetProperty("result", out var result)) Publish(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"刷新额度失败: {ex.Message}");
            StatusChanged?.Invoke("连接 Codex 失败，保留上次数据");
        }
    }

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters });
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task;
    }

    private Task NotifyAsync(string method, object parameters) => SendAsync(new { jsonrpc = "2.0", method, @params = parameters });
    private async Task SendAsync(object message)
    {
        if (_process is null) throw new InvalidOperationException("app-server 未启动");
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await _process.StandardInput.FlushAsync();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_process is not null && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line is null) break;
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone();
                if (message.TryGetProperty("id", out var rawId) && rawId.TryGetInt64(out var id) && _pending.Remove(id, out var completion)) completion.TrySetResult(message);
                else if (message.TryGetProperty("method", out var method) && method.GetString() == "account/rateLimits/updated" && message.TryGetProperty("params", out var parameters)) Publish(parameters);
            }
        }
        catch (Exception ex) { AppLog.Write($"app-server 读取失败: {ex.Message}"); }
    }

    private void Publish(JsonElement payload)
    {
        var snapshot = QuotaNormalizer.Normalize(payload, DateTimeOffset.Now);
        SnapshotUpdated?.Invoke(snapshot);
        StatusChanged?.Invoke("已连接 Codex");
    }

    public ValueTask DisposeAsync()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
        _process?.Dispose();
        return ValueTask.CompletedTask;
    }
}
