using System.Globalization;
using System.Text.Json;
using CodexQuotaBar.Models;

namespace CodexQuotaBar.Services;

public sealed class TokenUsageReader
{
    private readonly string _codexHome;
    private readonly Dictionary<string, string?> _cwdByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSummary> _summaryByFile = new(StringComparer.OrdinalIgnoreCase);

    public TokenUsageReader(string? codexHome = null)
    {
        _codexHome = codexHome ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    public TokenUsageSnapshot Read(DateTimeOffset now)
    {
        var projectPath = ReadActiveProject();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var monthEnd = monthStart.AddMonths(1);
        long monthTotal = 0;
        FileInfo? currentSession = null;

        foreach (var path in EnumerateSessionFiles())
        {
            var cwd = ReadCwd(path);
            var isProject = projectPath is not null && PathsEqual(cwd, projectPath);
            try
            {
                var info = new FileInfo(path);
                if ((projectPath is null || isProject) &&
                    (currentSession is null || info.LastWriteTimeUtc > currentSession.LastWriteTimeUtc))
                {
                    currentSession = info;
                }
                var mayContainThisMonth = info.LastWriteTimeUtc >= monthStart.UtcDateTime;
                if (!isProject && !mayContainThisMonth) continue;

                var summary = ReadSummary(info, monthStart, monthEnd);
                monthTotal += summary.MonthTokens;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        long sessionTotal = 0;
        if (currentSession is not null)
        {
            try
            {
                sessionTotal = ReadSummary(currentSession, monthStart, monthEnd).FinalTotal;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new TokenUsageSnapshot(
            now,
            monthTotal,
            sessionTotal,
            projectPath is null ? null : Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }

    private IEnumerable<string> EnumerateSessionFiles()
    {
        foreach (var directory in new[] { "sessions", "archived_sessions" })
        {
            var path = Path.Combine(_codexHome, directory);
            if (!Directory.Exists(path)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, "*.jsonl", SearchOption.AllDirectories).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files) yield return file;
        }
    }

    private string? ReadActiveProject()
    {
        try
        {
            using var stream = OpenRead(Path.Combine(_codexHome, ".codex-global-state.json"));
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("active-workspace-roots", out var roots)) return null;
            if (roots.ValueKind == JsonValueKind.String) return NormalizePath(roots.GetString());
            if (roots.ValueKind != JsonValueKind.Array) return null;
            foreach (var root in roots.EnumerateArray())
            {
                if (root.ValueKind == JsonValueKind.String) return NormalizePath(root.GetString());
            }
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? ReadCwd(string path)
    {
        if (_cwdByFile.TryGetValue(path, out var cached)) return cached;

        string? cwd = null;
        try
        {
            using var reader = new StreamReader(OpenRead(path));
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var type)) continue;
                    if (type.GetString() is not ("turn_context" or "session_meta")) continue;
                    if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("cwd", out var rawCwd)) continue;
                    cwd = NormalizePath(rawCwd.GetString());
                    break;
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (cwd is not null) _cwdByFile[path] = cwd;
        return cwd;
    }

    private FileSummary ReadSummary(FileInfo file, DateTimeOffset monthStart, DateTimeOffset monthEnd)
    {
        var monthKey = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (_summaryByFile.TryGetValue(file.FullName, out var cached) &&
            cached.Length == file.Length &&
            cached.LastWriteTimeUtc == file.LastWriteTimeUtc &&
            cached.MonthKey == monthKey)
        {
            return cached;
        }

        long monthTokens = 0;
        long finalTotal = 0;
        try
        {
            using var reader = new StreamReader(OpenRead(file.FullName));
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var type) || type.GetString() != "event_msg") continue;
                    if (!root.TryGetProperty("payload", out var payload) ||
                        !payload.TryGetProperty("type", out var payloadType) || payloadType.GetString() != "token_count" ||
                        !payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) continue;

                    if (info.TryGetProperty("total_token_usage", out var totalUsage) &&
                        totalUsage.TryGetProperty("total_tokens", out var rawTotal) && rawTotal.TryGetInt64(out var total))
                    {
                        finalTotal = total;
                    }

                    if (!root.TryGetProperty("timestamp", out var rawTimestamp) ||
                        !DateTimeOffset.TryParse(rawTimestamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ||
                        timestamp < monthStart || timestamp >= monthEnd) continue;
                    if (info.TryGetProperty("last_token_usage", out var lastUsage) &&
                        lastUsage.TryGetProperty("total_tokens", out var rawLast) && rawLast.TryGetInt64(out var last))
                    {
                        monthTokens += last;
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var summary = new FileSummary(file.Length, file.LastWriteTimeUtc, monthKey, monthTokens, finalTotal);
        _summaryByFile[file.FullName] = summary;
        return summary;
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record FileSummary(long Length, DateTime LastWriteTimeUtc, string MonthKey, long MonthTokens, long FinalTotal);
}
