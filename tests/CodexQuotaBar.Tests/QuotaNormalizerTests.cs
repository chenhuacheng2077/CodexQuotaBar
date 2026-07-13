using System.Text.Json;
using CodexQuotaBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodexQuotaBar.Tests;

[TestClass]
public sealed class QuotaNormalizerTests
{
    [TestMethod]
    public void IdentifiesFiveHourAndWeeklyWhenBothPresent()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":1783665243},"secondary":{"usedPercent":9,"windowDurationMins":10080,"resetsAt":1784252043}}}}""");
        Assert.AreEqual(2, snapshot.Windows.Count);
        Assert.AreEqual("5小时", snapshot.ShortWindow?.Label);
        Assert.AreEqual("每周", snapshot.LongWindow?.Label);
        Assert.AreEqual(75d, snapshot.ShortWindow?.RemainingPercent);
    }

    [TestMethod]
    public void HandlesCurrentWeeklyOnlyPayloadWithNullSecondary()
    {
        var snapshot = Normalize("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":4,"windowDurationMins":10080,"resetsAt":1784513513},"secondary":null,"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"planType":"plus"},"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":4,"windowDurationMins":10080,"resetsAt":1784513513},"secondary":null,"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"planType":"plus"}}}""");
        Assert.AreEqual(1, snapshot.Windows.Count);
        Assert.IsNull(snapshot.ShortWindow);
        Assert.AreEqual("每周", snapshot.LongWindow?.Label);
        Assert.AreEqual(96d, snapshot.LongWindow?.RemainingPercent);
        Assert.AreEqual("plus", snapshot.PlanType);
        Assert.AreEqual(0m, snapshot.CreditsRemaining);
    }

    [TestMethod]
    public void UsesLegacyRateLimitsWhenPoolMapIsAbsent()
    {
        var snapshot = Normalize("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":40,"windowDurationMins":300}}}""");
        Assert.AreEqual(1, snapshot.Windows.Count);
        Assert.AreEqual(60d, snapshot.ShortWindow?.RemainingPercent);
        Assert.IsNull(snapshot.LongWindow);
    }

    [TestMethod]
    public void HandlesMissingFieldsAndUnknownWindows()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"other":{"primary":{},"secondary":{"usedPercent":5,"windowDurationMins":4320}}}}""");
        Assert.IsNull(snapshot.ShortWindow);
        Assert.AreEqual("3天", snapshot.LongWindow?.Label);
    }

    [TestMethod]
    public void FindsWindowsAcrossMultiplePools()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":1,"windowDurationMins":60}},"codex":{"secondary":{"usedPercent":2,"windowDurationMins":10080},"primary":{"usedPercent":3,"windowDurationMins":300}}}}""");
        Assert.AreEqual(3, snapshot.Windows.Count);
        Assert.AreEqual("codex.primary", snapshot.Windows.Single(window => window.Label == "5小时").Id);
        Assert.AreEqual("codex.secondary", snapshot.Windows.Single(window => window.Label == "每周").Id);
    }

    [TestMethod]
    public void HandlesRateLimitUpdatedPayload()
    {
        var snapshot = Normalize("""{"rateLimits":{"primary":{"usedPercent":80,"windowDurationMins":300},"secondary":{"usedPercent":20,"windowDurationMins":10080}}}""");
        Assert.AreEqual(20d, snapshot.ShortWindow?.RemainingPercent);
        Assert.AreEqual(80d, snapshot.LongWindow?.RemainingPercent);
    }

    [TestMethod]
    public void ClampsInvalidPercentage()
    {
        var snapshot = Normalize("""{"rateLimits":{"primary":{"usedPercent":120,"windowDurationMins":300}}}""");
        Assert.AreEqual(0d, snapshot.ShortWindow?.RemainingPercent);
    }

    [TestMethod]
    public void LabelsMonthlyWindows()
    {
        var snapshot = Normalize("""{"rateLimits":{"primary":{"usedPercent":10,"windowDurationMins":43200,"resetsAt":1786000000},"secondary":null}}""");
        Assert.AreEqual("每月", snapshot.LongWindow?.Label);
        Assert.AreEqual(90d, snapshot.LongWindow?.RemainingPercent);
    }

    [TestMethod]
    public void ReadsCreditsBalance()
    {
        var snapshot = Normalize("""{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":10080},"credits":{"balance":"12.5","hasCredits":true,"unlimited":false}}}""");
        Assert.AreEqual(12.5m, snapshot.CreditsRemaining);
    }

    [TestMethod]
    public void IgnoresNullSecondaryWithoutPlaceholderWindow()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":50,"windowDurationMins":10080},"secondary":null}}}""");
        Assert.AreEqual(1, snapshot.Windows.Count);
        Assert.AreEqual("每周", snapshot.Windows[0].Label);
    }

    [TestMethod]
    public void NormalizesCheckedInWeeklyOnlyFixture()
    {
        var path = FindFixture("real-rate-limits.sanitized.json");
        var snapshot = Normalize(File.ReadAllText(path));
        Assert.AreEqual(1, snapshot.Windows.Count);
        Assert.AreEqual("每周", snapshot.Windows[0].Label);
        Assert.AreEqual("plus", snapshot.PlanType);
    }

    [TestMethod]
    public void NormalizesCheckedInLegacyDualWindowFixture()
    {
        var path = FindFixture("legacy-dual-windows.sanitized.json");
        var snapshot = Normalize(File.ReadAllText(path));
        Assert.AreEqual(2, snapshot.Windows.Count);
        Assert.AreEqual("5小时", snapshot.ShortWindow?.Label);
        Assert.AreEqual("每周", snapshot.LongWindow?.Label);
    }

    private static string FindFixture(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Fixture not found: {fileName}");
    }

    private static CodexQuotaBar.Models.QuotaSnapshot Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return QuotaNormalizer.Normalize(document.RootElement, DateTimeOffset.UtcNow);
    }
}
