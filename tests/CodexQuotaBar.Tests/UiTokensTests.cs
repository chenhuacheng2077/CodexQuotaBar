using CodexQuotaBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodexQuotaBar.Tests;

[TestClass]
public sealed class UiTokensTests
{
    [TestMethod]
    public void FormatsQuotaDownwardSoDisplayedRemainingNeverOverstatesBalance()
    {
        Assert.AreEqual("94", UiTokens.FormatPercent(94.9));
        Assert.AreEqual("94", UiTokens.FormatPercent(94.01));
        Assert.AreEqual("100", UiTokens.FormatPercent(100.0));
        Assert.AreEqual("0", UiTokens.FormatPercent(-0.1));
    }
}
