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
            var beta = Path.Combine(home, "sessions", "2026", "07", "01", "beta.jsonl");
            File.WriteAllLines(beta,
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
            File.SetLastWriteTimeUtc(beta, new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc));

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
    public void UsesMostRecentlyWrittenSessionWhenActiveWorkspaceStateIsStale()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CodexQuotaBar-{Guid.NewGuid():N}");
        try
        {
            var sessions = Path.Combine(home, "sessions", "2026", "08", "02");
            Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), """{"active-workspace-roots":"F:\\projects\\old"}""");

            var oldSession = Path.Combine(sessions, "old.jsonl");
            File.WriteAllLines(oldSession,
            [
                """{"timestamp":"2026-08-02T08:00:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\old"}}""",
                TokenLine("2026-08-02T08:01:00Z", 11700000, 11700000)
            ]);
            var currentSession = Path.Combine(sessions, "current.jsonl");
            File.WriteAllLines(currentSession,
            [
                """{"timestamp":"2026-08-02T09:00:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\current"}}""",
                TokenLine("2026-08-02T09:01:00Z", 12345678, 12345678)
            ]);
            File.SetLastWriteTimeUtc(oldSession, new DateTime(2026, 8, 2, 8, 2, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(currentSession, new DateTime(2026, 8, 2, 9, 2, 0, DateTimeKind.Utc));

            var snapshot = new TokenUsageReader(home).Read(new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.FromHours(8)));

            Assert.AreEqual(12345678L, snapshot.SessionTotal);
            Assert.AreEqual("current", snapshot.ProjectName);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [TestMethod]
    public void CalculatesMonthUsageFromCumulativeTotalsWithoutDoubleCountingLastUsage()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CodexQuotaBar-{Guid.NewGuid():N}");
        try
        {
            var sessions = Path.Combine(home, "sessions", "2026", "08", "02");
            Directory.CreateDirectory(sessions);
            var session = Path.Combine(sessions, "cumulative.jsonl");
            File.WriteAllLines(session,
            [
                """{"timestamp":"2026-08-02T08:00:00Z","type":"turn_context","payload":{"cwd":"F:\\projects\\current"}}""",
                TokenLine("2026-08-02T08:01:00Z", 100, 100),
                TokenLine("2026-08-02T08:02:00Z", 200, 250)
            ]);
            var snapshot = new TokenUsageReader(home).Read(new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.FromHours(8)));

            Assert.AreEqual(250L, snapshot.MonthTotal);
            Assert.AreEqual(250L, snapshot.SessionTotal);
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
