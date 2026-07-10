namespace CodexQuotaBar.Models;

public sealed record QuotaSnapshot(
    DateTimeOffset UpdatedAt,
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    int? ResetCreditCount,
    decimal? CreditsRemaining)
{
    public static QuotaSnapshot Empty { get; } = new(DateTimeOffset.MinValue, null, null, null, null);
}

public sealed record QuotaWindow(
    string Id,
    string Label,
    double UsedPercent,
    double RemainingPercent,
    int WindowDurationMinutes,
    DateTimeOffset? ResetsAt);
