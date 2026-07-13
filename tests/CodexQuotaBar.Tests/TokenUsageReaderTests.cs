using CodexQuotaBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace CodexQuotaBar.Tests;

[TestClass]
public sealed class TokenUsageReaderTests
{
    [TestMethod]
    public void ReadsCalendarMonthAndCurrentSessionTotal()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CodexQuotaBar-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(home, "sessions", "2026", "07", "01"));
            File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), """{"active-workspace-roots":"F:\\projects\\alpha"}""");
            var alpha = Path.Combine(home, "sessions", "2026", "07", "01", "alpha.jsonl");
            File.WriteAllLines(alpha,
            [
                """{"timestamp":"2026-06-30T12:00:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\alpha"}}""",
                TokenLine("2026-06-30T12:01:00Z", 100, 100),
                TokenLine("2026-07-01T12:01:00Z", 50, 150)
            ]);
            File.WriteAllLines(Path.Combine(home, "sessions", "2026", "07", "01", "beta.jsonl"),
            [
                """{"timestamp":"2026-07-02T12:00:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\beta"}}""",
                TokenLine("2026-07-02T12:01:00Z", 200, 200)
            ]);
            var olderAlpha = Path.Combine(home, "sessions", "2026", "07", "01", "older-alpha.jsonl");
            File.WriteAllLines(olderAlpha,
            [
                """{"timestamp":"2026-07-03T12:00:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\alpha"}}""",
                TokenLine("2026-07-03T12:01:00Z", 40, 40)
            ]);
            File.SetLastWriteTimeUtc(olderAlpha, new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(alpha, new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc));

            var reader = new TokenUsageReader(home);
            var snapshot = reader.Read(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.FromHours(8)));

            Assert.AreEqual(290L, snapshot.MonthTotal);
            Assert.AreEqual(150L, snapshot.SessionTotal);
            Assert.AreEqual("alpha", snapshot.ProjectName);

            File.AppendAllText(alpha, Environment.NewLine + TokenLine("2026-07-13T04:01:00Z", 25, 175));
            snapshot = reader.Read(new DateTimeOffset(2026, 7, 13, 12, 5, 0, TimeSpan.FromHours(8)));

            Assert.AreEqual(315L, snapshot.MonthTotal);
            Assert.AreEqual(175L, snapshot.SessionTotal);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [TestMethod]
    public void FindsProjectWhenCwdIsAppendedAfterFirstRead()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CodexQuotaBar-{Guid.NewGuid():N}");
        try
        {
            var sessions = Path.Combine(home, "sessions", "2026", "07", "13");
            Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), """{"active-workspace-roots":"F:\\projects\\alpha"}""");
            var session = Path.Combine(sessions, "active.jsonl");
            File.WriteAllText(session, TokenLine("2026-07-13T04:00:00Z", 10, 10));
            var reader = new TokenUsageReader(home);

            Assert.AreEqual(0L, reader.Read(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.FromHours(8))).SessionTotal);

            File.AppendAllText(session, Environment.NewLine + """{"timestamp":"2026-07-13T04:01:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\alpha"}}""");

            Assert.AreEqual(10L, reader.Read(new DateTimeOffset(2026, 7, 13, 12, 1, 0, TimeSpan.FromHours(8))).SessionTotal);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    private static string TokenLine(string timestamp, long last, long total) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    last_token_usage = new { total_tokens = last },
                    total_token_usage = new { total_tokens = total }
                }
            }
        });
}
