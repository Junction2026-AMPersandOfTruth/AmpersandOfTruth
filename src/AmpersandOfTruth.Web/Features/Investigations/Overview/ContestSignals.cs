using System.Collections.Concurrent;
using System.Globalization;
using AmpersandOfTruth.Web.Features.Corpus;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Investigations.Overview;

public sealed record PageContest(
    long PageId,
    int EditWarEpisodes,
    int PingPongEpisodes,
    int BurstEpisodes,
    double ContestedScore,
    string? PeakMonth)
{
    public bool HasEpisodes => EditWarEpisodes + PingPongEpisodes + BurstEpisodes > 0;
}

public interface IContestSignals
{
    Task<IReadOnlyDictionary<long, PageContest>> PageContestsAsync(
        IReadOnlyList<long> pageIds,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>> RevertClassesAsync(
        IReadOnlyList<(long PageId, long RevertingRevId)> reverts,
        CancellationToken ct = default);
}

public static class ContestSignals
{
    public static string? FormatRailLine(PageContest? contest)
    {
        if (contest is null || !contest.HasEpisodes)
        {
            return null;
        }

        int backAndForth = contest.EditWarEpisodes + contest.PingPongEpisodes;
        string phrase = backAndForth > 0
            ? $"{backAndForth.ToString("N0", CultureInfo.InvariantCulture)} {(backAndForth == 1 ? "period" : "periods")} of back-and-forth edits"
            : $"{contest.BurstEpisodes.ToString("N0", CultureInfo.InvariantCulture)} {(contest.BurstEpisodes == 1 ? "burst" : "bursts")} of edits";

        if (!string.IsNullOrWhiteSpace(contest.PeakMonth) &&
            DateTime.TryParseExact(
                contest.PeakMonth.Trim(),
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var peak))
        {
            return $"Contested: {phrase} \u00b7 peak {peak.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        return $"Contested: {phrase}";
    }

    public static string? FormatRevertClass(string? revertClass)
    {
        if (string.IsNullOrWhiteSpace(revertClass))
        {
            return null;
        }

        return revertClass.Trim().ToLowerInvariant() switch
        {
            "graffiti_cleanup" => "vandalism cleanup",
            "self_revert" => "undone by the same account",
            "bot" => "bot",
            "dispute" => "disputed content",
            "edit_war" => "back-and-forth edits",
            _ => null,
        };
    }
}

/// <summary>
/// Reads optional P1.S4 page contestedness (<c>research.s4_page_contest</c>) and P1.S1 removal classes
/// (<c>research.s1_revert</c>) with a 1-hour in-memory cache. Degrades silently to empty results on failure.
/// </summary>
public sealed class MariaDbContestSignals : IContestSignals
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly Func<IReadOnlyList<long>, CancellationToken, Task<IReadOnlyDictionary<long, PageContest>>> _queryContests;
    private readonly Func<IReadOnlyList<(long PageId, long RevertingRevId)>, CancellationToken, Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>>> _queryClasses;
    private readonly TimeProvider _time;
    private readonly ILogger<MariaDbContestSignals> _log;
    private readonly string _serverLabel;

    private readonly ConcurrentDictionary<long, PageCacheEntry> _pageCache = new();
    private readonly ConcurrentDictionary<(long PageId, long RevertingRevId), ClassCacheEntry> _classCache = new();

    public MariaDbContestSignals(
        CorpusConnectionFactory corpus,
        TimeProvider time,
        ILogger<MariaDbContestSignals> log)
    {
        _time = time;
        _log = log;
        _serverLabel = corpus.Server;
        _queryContests = (ids, ct) => QueryPageContestsAsync(corpus, ids, ct);
        _queryClasses = (reverts, ct) => QueryRevertClassesAsync(corpus, reverts, ct);
    }

    internal MariaDbContestSignals(
        Func<IReadOnlyList<long>, CancellationToken, Task<IReadOnlyDictionary<long, PageContest>>> queryContests,
        Func<IReadOnlyList<(long PageId, long RevertingRevId)>, CancellationToken, Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>>> queryClasses,
        TimeProvider time,
        ILogger<MariaDbContestSignals> log)
    {
        _queryContests = queryContests;
        _queryClasses = queryClasses;
        _time = time;
        _log = log;
        _serverLabel = "fake";
    }

    public async Task<IReadOnlyDictionary<long, PageContest>> PageContestsAsync(
        IReadOnlyList<long> pageIds,
        CancellationToken ct = default)
    {
        if (pageIds.Count == 0)
        {
            return new Dictionary<long, PageContest>();
        }

        var now = _time.GetUtcNow();
        var result = new Dictionary<long, PageContest>(pageIds.Count);
        var missing = new List<long>();
        var seen = new HashSet<long>();

        foreach (var id in pageIds)
        {
            if (!seen.Add(id)) continue;

            if (_pageCache.TryGetValue(id, out var cached) && cached.ExpiresAt > now)
            {
                if (cached.Value is not null)
                {
                    result[id] = cached.Value;
                }
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count == 0)
        {
            return result;
        }

        try
        {
            var fetched = await _queryContests(missing, ct);
            var expiresAt = _time.GetUtcNow() + CacheTtl;
            foreach (var id in missing)
            {
                fetched.TryGetValue(id, out var val);
                _pageCache[id] = new PageCacheEntry(val, expiresAt);
                if (val is not null)
                {
                    result[id] = val;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Optional S4 page contest query against {Server} failed", _serverLabel);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>> RevertClassesAsync(
        IReadOnlyList<(long PageId, long RevertingRevId)> reverts,
        CancellationToken ct = default)
    {
        if (reverts.Count == 0)
        {
            return new Dictionary<(long PageId, long RevertingRevId), string>();
        }

        var now = _time.GetUtcNow();
        var result = new Dictionary<(long PageId, long RevertingRevId), string>(reverts.Count);
        var missing = new List<(long PageId, long RevertingRevId)>();
        var seen = new HashSet<(long PageId, long RevertingRevId)>();

        foreach (var key in reverts)
        {
            if (!seen.Add(key)) continue;

            if (_classCache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
            {
                if (cached.Value is not null)
                {
                    result[key] = cached.Value;
                }
            }
            else
            {
                missing.Add(key);
            }
        }

        if (missing.Count == 0)
        {
            return result;
        }

        try
        {
            var fetched = await _queryClasses(missing, ct);
            var expiresAt = _time.GetUtcNow() + CacheTtl;
            foreach (var key in missing)
            {
                fetched.TryGetValue(key, out var val);
                _classCache[key] = new ClassCacheEntry(val, expiresAt);
                if (val is not null)
                {
                    result[key] = val;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Optional S1 revert class query against {Server} failed", _serverLabel);
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<long, PageContest>> QueryPageContestsAsync(
        CorpusConnectionFactory corpus,
        IReadOnlyList<long> pageIds,
        CancellationToken ct)
    {
        await using var conn = await corpus.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var paramNames = new string[pageIds.Count];
        for (int i = 0; i < pageIds.Count; i++)
        {
            var name = $"@p{i.ToString(CultureInfo.InvariantCulture)}";
            paramNames[i] = name;
            cmd.Parameters.AddWithValue(name, pageIds[i]);
        }

        cmd.CommandText = $"""
            SELECT page_id, episodes_edit_war, episodes_ping_pong, episodes_burst, contested_score, peak_month
            FROM research.s4_page_contest
            WHERE page_id IN ({string.Join(", ", paramNames)});
            """;

        var map = new Dictionary<long, PageContest>(pageIds.Count);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var pageId = Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture);
            map[pageId] = new PageContest(
                PageId: pageId,
                EditWarEpisodes: Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                PingPongEpisodes: Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                BurstEpisodes: Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                ContestedScore: r.GetDouble(4),
                PeakMonth: r.IsDBNull(5) ? null : r.GetString(5));
        }

        return map;
    }

    private static async Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>> QueryRevertClassesAsync(
        CorpusConnectionFactory corpus,
        IReadOnlyList<(long PageId, long RevertingRevId)> reverts,
        CancellationToken ct)
    {
        var requested = reverts.ToHashSet();
        var revIds = reverts.Select(static r => r.RevertingRevId).Distinct().ToList();

        await using var conn = await corpus.OpenAsync(ct);
        try
        {
            return await ExecuteRevertClassSelectAsync(conn, revIds, requested, useClassV2: true, ct);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.BadFieldError)
        {
            return await ExecuteRevertClassSelectAsync(conn, revIds, requested, useClassV2: false, ct);
        }
    }

    private static async Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>> ExecuteRevertClassSelectAsync(
        MySqlConnection conn,
        IReadOnlyList<long> revIds,
        HashSet<(long PageId, long RevertingRevId)> requested,
        bool useClassV2,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        var paramNames = new string[revIds.Count];
        for (int i = 0; i < revIds.Count; i++)
        {
            var name = $"@r{i.ToString(CultureInfo.InvariantCulture)}";
            paramNames[i] = name;
            cmd.Parameters.AddWithValue(name, revIds[i]);
        }

        var classExpr = useClassV2 ? "COALESCE(class_v2, class)" : "class";
        cmd.CommandText = $"""
            SELECT page_id, reverting_rev_id, {classExpr}
            FROM research.s1_revert
            WHERE reverting_rev_id IN ({string.Join(", ", paramNames)});
            """;

        var map = new Dictionary<(long PageId, long RevertingRevId), string>(requested.Count);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var pageId = Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture);
            var revertingRevId = Convert.ToInt64(r.GetValue(1), CultureInfo.InvariantCulture);
            if (!requested.Contains((pageId, revertingRevId)) || r.IsDBNull(2))
            {
                continue;
            }

            map[(pageId, revertingRevId)] = r.GetString(2);
        }

        return map;
    }

    private sealed record PageCacheEntry(PageContest? Value, DateTimeOffset ExpiresAt);

    private sealed record ClassCacheEntry(string? Value, DateTimeOffset ExpiresAt);
}
