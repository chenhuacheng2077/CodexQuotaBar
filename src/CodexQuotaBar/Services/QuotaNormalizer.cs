using System.Globalization;
using System.Text.Json;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Services;

public static class QuotaNormalizer
{
    public static QuotaSnapshot Normalize(JsonElement result, DateTimeOffset now)
    {
        var pools = new List<(string Id, JsonElement Pool)>();
        string? planType = null;

        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            pools.AddRange(byId.EnumerateObject().Select(x => (x.Name, x.Value)));
        }

        if (pools.Count == 0 && result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            pools.Add((legacy.TryGetProperty("limitId", out var id) ? id.GetString() ?? "legacy" : "legacy", legacy));
        }

        if (result.TryGetProperty("rateLimits", out var topLevel) && topLevel.ValueKind == JsonValueKind.Object &&
            topLevel.TryGetProperty("planType", out var topPlan) && topPlan.ValueKind == JsonValueKind.String)
        {
            planType = topPlan.GetString();
        }

        var windows = pools
            .SelectMany(pool => ReadPoolWindows(pool.Id, pool.Pool))
            .GroupBy(window => window.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(window => window.WindowDurationMinutes)
            .ThenBy(window => window.Id, StringComparer.Ordinal)
            .ToList();

        if (planType is null)
        {
            foreach (var pool in pools)
            {
                if (pool.Pool.TryGetProperty("planType", out var poolPlan) && poolPlan.ValueKind == JsonValueKind.String)
                {
                    planType = poolPlan.GetString();
                    break;
                }
            }
        }

        decimal? balance = null;
        foreach (var pool in pools.Select(pool => pool.Pool).Append(TopLevelOrDefault(result)))
        {
            if (pool.ValueKind != JsonValueKind.Object) continue;
            if (!pool.TryGetProperty("credits", out var creditsObject) || creditsObject.ValueKind != JsonValueKind.Object) continue;
            if (creditsObject.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True)
            {
                balance = null;
                break;
            }
            if (creditsObject.TryGetProperty("balance", out var rawBalance) &&
                decimal.TryParse(rawBalance.GetString(), CultureInfo.InvariantCulture, out var parsed))
            {
                balance = parsed;
                break;
            }
        }

        return new QuotaSnapshot(now, windows, balance, planType);
    }

    private static JsonElement TopLevelOrDefault(JsonElement result) =>
        result.TryGetProperty("rateLimits", out var rateLimits) ? rateLimits : default;

    private static IEnumerable<QuotaWindow> ReadPoolWindows(string poolId, JsonElement pool)
    {
        if (pool.ValueKind != JsonValueKind.Object) yield break;

        foreach (var kind in new[] { "primary", "secondary" })
        {
            if (!pool.TryGetProperty(kind, out var value) || value.ValueKind != JsonValueKind.Object) continue;
            var window = ReadWindow(poolId, kind, value);
            if (window is not null) yield return window;
        }
    }

    private static QuotaWindow? ReadWindow(string poolId, string kind, JsonElement value)
    {
        if (!value.TryGetProperty("windowDurationMins", out var duration) || !duration.TryGetInt32(out var minutes) || minutes <= 0)
        {
            return null;
        }

        var used = value.TryGetProperty("usedPercent", out var rawUsed) && rawUsed.TryGetDouble(out var raw)
            ? Math.Clamp(raw, 0, 100)
            : 0;
        DateTimeOffset? resetsAt = value.TryGetProperty("resetsAt", out var reset) && reset.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

        return new QuotaWindow(
            $"{poolId}.{kind}",
            Label(minutes),
            used,
            Math.Clamp(100 - used, 0, 100),
            minutes,
            resetsAt);
    }

    private static string Label(int minutes) => minutes switch
    {
        >= 43000 and <= 45000 => "每月",
        >= 10020 and <= 10140 => "每周",
        >= 1400 and <= 1500 => "每天",
        >= 270 and <= 330 => "5小时",
        >= 50 and <= 70 => "1小时",
        _ when minutes >= 1440 => $"{Math.Round(minutes / 1440d):0}天",
        _ when minutes >= 60 => $"{Math.Round(minutes / 60d):0}小时",
        _ => $"{minutes}分钟"
    };
}
