using System.Globalization;
using System.Text.Json;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Services;

public static class QuotaNormalizer
{
    public static QuotaSnapshot Normalize(JsonElement result, DateTimeOffset now)
    {
        var pools = new List<(string Id, JsonElement Pool)>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            pools.AddRange(byId.EnumerateObject().Select(x => (x.Name, x.Value)));
        }
        if (pools.Count == 0 && result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            pools.Add((legacy.TryGetProperty("limitId", out var id) ? id.GetString() ?? "legacy" : "legacy", legacy));
        }

        var windows = pools.SelectMany(pool => new[] { ("primary", pool.Pool), ("secondary", pool.Pool) }
            .Where(pair => pair.Item2.TryGetProperty(pair.Item1, out _))
            .Select(pair => ReadWindow(pool.Id, pair.Item1, pair.Item2.GetProperty(pair.Item1))))
            .Where(window => window is not null)
            .Cast<QuotaWindow>()
            .OrderBy(window => window.WindowDurationMinutes)
            .ToList();

        var fiveHour = windows.OrderBy(window => Math.Abs(window.WindowDurationMinutes - 300)).FirstOrDefault(window => Math.Abs(window.WindowDurationMinutes - 300) <= 90);
        var weekly = windows.OrderBy(window => Math.Abs(window.WindowDurationMinutes - 10080)).FirstOrDefault(window => Math.Abs(window.WindowDurationMinutes - 10080) <= 1440);
        fiveHour ??= windows.FirstOrDefault(window => window.WindowDurationMinutes > 0 && window.WindowDurationMinutes < 1440);
        weekly ??= windows.FirstOrDefault(window => window != fiveHour && window.WindowDurationMinutes >= 1440);

        var credits = pools.Select(pool => pool.Pool).FirstOrDefault(pool => pool.TryGetProperty("credits", out _));
        decimal? balance = null;
        if (credits.ValueKind == JsonValueKind.Object && credits.TryGetProperty("credits", out var creditsObject) && creditsObject.TryGetProperty("balance", out var rawBalance) && decimal.TryParse(rawBalance.GetString(), CultureInfo.InvariantCulture, out var parsed)) balance = parsed;
        return new QuotaSnapshot(now, fiveHour, weekly, null, balance);
    }

    private static QuotaWindow? ReadWindow(string poolId, string kind, JsonElement value)
    {
        if (!value.TryGetProperty("windowDurationMins", out var duration) || !duration.TryGetInt32(out var minutes)) return null;
        var used = value.TryGetProperty("usedPercent", out var rawUsed) && rawUsed.TryGetDouble(out var raw) ? Math.Clamp(raw, 0, 100) : 0;
        DateTimeOffset? resetsAt = value.TryGetProperty("resetsAt", out var reset) && reset.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        return new QuotaWindow($"{poolId}.{kind}", Label(minutes), used, Math.Clamp(100 - used, 0, 100), minutes, resetsAt);
    }

    private static string Label(int minutes) => minutes switch
    {
        >= 10020 and <= 10140 => "每周",
        >= 270 and <= 330 => "5小时",
        _ when minutes >= 1440 => $"{Math.Round(minutes / 1440d):0}天",
        _ when minutes >= 60 => $"{Math.Round(minutes / 60d):0}小时",
        _ => $"{minutes}分钟"
    };
}
