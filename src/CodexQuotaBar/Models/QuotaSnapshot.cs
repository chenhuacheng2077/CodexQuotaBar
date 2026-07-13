namespace CodexQuotaBar.Models;

public sealed record QuotaSnapshot(
    DateTimeOffset UpdatedAt,
    IReadOnlyList<QuotaWindow> Windows,
    decimal? CreditsRemaining,
    string? PlanType)
{
    public static QuotaSnapshot Empty { get; } = new(DateTimeOffset.MinValue, Array.Empty<QuotaWindow>(), null, null);

    public QuotaWindow? ShortWindow => Windows.FirstOrDefault(window => window.WindowDurationMinutes > 0 && window.WindowDurationMinutes < 1440);
    public QuotaWindow? LongWindow => Windows.FirstOrDefault(window => window.WindowDurationMinutes >= 1440);
}

public sealed record QuotaWindow(
    string Id,
    string Label,
    double UsedPercent,
    double RemainingPercent,
    int WindowDurationMinutes,
    DateTimeOffset? ResetsAt);

public sealed record TokenUsageSnapshot(
    DateTimeOffset UpdatedAt,
    long MonthTotal,
    long SessionTotal,
    string? ProjectName)
{
    public static TokenUsageSnapshot Empty { get; } = new(DateTimeOffset.MinValue, 0, 0, null);
}
