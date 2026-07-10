using System.Text.Json;
using CodexQuotaBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodexQuotaBar.Tests;

[TestClass]
public sealed class QuotaNormalizerTests
{
    [TestMethod]
    public void IdentifiesFiveHourWeeklyAndRemainingPercentage()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":1783665243},"secondary":{"usedPercent":9,"windowDurationMins":10080,"resetsAt":1784252043}}}}""");
        Assert.AreEqual("5小时", snapshot.FiveHour?.Label);
        Assert.AreEqual("每周", snapshot.Weekly?.Label);
        Assert.AreEqual(75d, snapshot.FiveHour?.RemainingPercent);
    }

    [TestMethod]
    public void UsesLegacyRateLimitsWhenPoolMapIsAbsent()
    {
        var snapshot = Normalize("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":40,"windowDurationMins":300}}}""");
        Assert.AreEqual(60d, snapshot.FiveHour?.RemainingPercent);
        Assert.IsNull(snapshot.Weekly);
    }

    [TestMethod]
    public void HandlesMissingFieldsAndUnknownWindows()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"other":{"primary":{},"secondary":{"usedPercent":5,"windowDurationMins":4320}}}}""");
        Assert.IsNull(snapshot.FiveHour);
        Assert.AreEqual("3天", snapshot.Weekly?.Label);
    }

    [TestMethod]
    public void FindsWindowsAcrossMultiplePools()
    {
        var snapshot = Normalize("""{"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":1,"windowDurationMins":60}},"codex":{"secondary":{"usedPercent":2,"windowDurationMins":10080},"primary":{"usedPercent":3,"windowDurationMins":300}}}}""");
        Assert.AreEqual("codex.primary", snapshot.FiveHour?.Id);
        Assert.AreEqual("codex.secondary", snapshot.Weekly?.Id);
    }

    [TestMethod]
    public void HandlesRateLimitUpdatedPayload()
    {
        var snapshot = Normalize("""{"rateLimits":{"primary":{"usedPercent":80,"windowDurationMins":300},"secondary":{"usedPercent":20,"windowDurationMins":10080}}}""");
        Assert.AreEqual(20d, snapshot.FiveHour?.RemainingPercent);
        Assert.AreEqual(80d, snapshot.Weekly?.RemainingPercent);
    }

    [TestMethod]
    public void ClampsInvalidPercentage()
    {
        var snapshot = Normalize("""{"rateLimits":{"primary":{"usedPercent":120,"windowDurationMins":300}}}""");
        Assert.AreEqual(0d, snapshot.FiveHour?.RemainingPercent);
    }

    private static CodexQuotaBar.Models.QuotaSnapshot Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return QuotaNormalizer.Normalize(document.RootElement, DateTimeOffset.UtcNow);
    }
}
