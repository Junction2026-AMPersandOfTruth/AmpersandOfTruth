using System.Collections.Concurrent;
using System.Globalization;
using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Corpus;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Articles;

public sealed record ConflictEpisode(
    long EpisodeId,
    long PageId,
    string Kind,
    DateTime StartTs,
    DateTime EndTs,
    int RevertCount,
    int NamedParticipants,
    int AnonParticipants,
    int EditCount,
    bool ProtectionHint,
    double Score);

public interface IConflictEpisodes
{
    Task<IReadOnlyList<ConflictEpisode>> ForPageAsync(long pageId, CancellationToken ct = default);
}

public static class ConflictEpisodes
{
    public static IReadOnlyList<ConflictBand> ToBands(IReadOnlyList<ConflictEpisode> episodes)
    {
        if (episodes.Count == 0) return [];
        var bands = new List<ConflictBand>(episodes.Count);
        foreach (var ep in episodes)
        {
            bands.Add(ToBand(ep));
        }

        return bands;
    }

    public static ConflictBand ToBand(ConflictEpisode episode) =>
        new(episode.StartTs, episode.EndTs, episode.Kind, FormatLabel(episode));

    public static string FormatLabel(ConflictEpisode episode)
    {
        var kindText = string.Equals(episode.Kind, "burst", StringComparison.OrdinalIgnoreCase)
            ? "Burst of edits"
            : "Back-and-forth edits";
        var dates = FormatEpisodeDates(episode.StartTs, episode.EndTs);
        var removals = $"{episode.RevertCount.ToString("N0", CultureInfo.InvariantCulture)} {(episode.RevertCount == 1 ? "removal" : "removals")}";
        var accounts = FormatParticipants(episode.NamedParticipants, episode.AnonParticipants);
        return $"{kindText} \u00b7 {dates} \u00b7 {removals} \u00b7 {accounts}";
    }

    public static string? TryFormatBusiestSummary(IReadOnlyList<ConflictEpisode> episodes)
    {
        var busiest = episodes
            .Where(static e => string.Equals(e.Kind, "edit_war", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static e => e.RevertCount)
            .ThenByDescending(static e => e.Score)
            .ThenByDescending(static e => e.EditCount)
            .ThenByDescending(static e => e.StartTs)
            .FirstOrDefault();

        if (busiest is null)
        {
            return null;
        }

        var start = DateTime.SpecifyKind(busiest.StartTs, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(busiest.EndTs, DateTimeKind.Utc);
        if (end < start) end = start;

        var startMonth = start.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var endMonth = end.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var period = start.Year == end.Year && start.Month == end.Month
            ? startMonth
            : $"{startMonth} \u2013 {endMonth}";
        var removals = $"{busiest.RevertCount.ToString("N0", CultureInfo.InvariantCulture)} {(busiest.RevertCount == 1 ? "removal" : "removals")}";
        return $"Most contested period: {period} (back-and-forth edits, {removals}).";
    }

    internal static string FormatEpisodeDates(DateTime startTs, DateTime endTs)
    {
        var start = DateTime.SpecifyKind(startTs, DateTimeKind.Utc).Date;
        var end = DateTime.SpecifyKind(endTs, DateTimeKind.Utc).Date;
        if (end < start) end = start;

        if (start == end)
        {
            return start.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        if (start.Year == end.Year && start.Month == end.Month)
        {
            return $"{start.Day.ToString(CultureInfo.InvariantCulture)}\u2013{end.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        if (start.Year == end.Year)
        {
            return $"{start.ToString("d MMM", CultureInfo.InvariantCulture)} \u2013 {end.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        return $"{start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} \u2013 {end.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
    }

    internal static string FormatParticipants(int named, int anon)
    {
        if (named > 0 && anon > 0)
        {
            return $"{named.ToString("N0", CultureInfo.InvariantCulture)} named and {anon.ToString("N0", CultureInfo.InvariantCulture)} anonymous accounts";
        }

        if (named > 0)
        {
            return $"{named.ToString("N0", CultureInfo.InvariantCulture)} named {(named == 1 ? "account" : "accounts")}";
        }

        if (anon > 0)
        {
            return $"{anon.ToString("N0", CultureInfo.InvariantCulture)} anonymous {(anon == 1 ? "account" : "accounts")}";
        }

        return "0 accounts";
    }
}

/// <summary>
/// Reads default P1.S4 conflict episodes (<c>research.s4_episode</c>, <c>is_default = 1</c>) with a 1-hour cache.
/// Never reads <c>participant_users</c>. Degrades to an empty list when the optional table/columns are absent or unreachable.
/// </summary>
public sealed class MariaDbConflictEpisodes : IConflictEpisodes
{
    private const string Sql = """
        SELECT episode_id, kind, start_ts, end_ts, revert_count,
               named_participants, anon_participants, edit_count, protection_hint, score
        FROM research.s4_episode
        WHERE page_id = @pageId AND is_default = 1
        ORDER BY start_ts ASC, episode_id ASC;
        """;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly Func<long, CancellationToken, Task<IReadOnlyList<ConflictEpisode>>> _query;
    private readonly TimeProvider _time;
    private readonly ILogger<MariaDbConflictEpisodes> _log;
    private readonly ConcurrentDictionary<long, CacheEntry> _cache = new();
    private readonly string _serverLabel;

    public MariaDbConflictEpisodes(
        CorpusConnectionFactory corpus,
        TimeProvider time,
        ILogger<MariaDbConflictEpisodes> log)
    {
        _time = time;
        _log = log;
        _serverLabel = corpus.Server;
        _query = (pageId, ct) => QueryMariaDbAsync(corpus, pageId, ct);
    }

    internal MariaDbConflictEpisodes(
        Func<long, CancellationToken, Task<IReadOnlyList<ConflictEpisode>>> query,
        TimeProvider time,
        ILogger<MariaDbConflictEpisodes> log)
    {
        _query = query;
        _time = time;
        _log = log;
        _serverLabel = "fake";
    }

    public async Task<IReadOnlyList<ConflictEpisode>> ForPageAsync(long pageId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (_cache.TryGetValue(pageId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Value;
        }

        try
        {
            var episodes = await _query(pageId, ct);
            _cache[pageId] = new CacheEntry(episodes, _time.GetUtcNow() + CacheTtl);
            return episodes;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Optional S4 conflict episode query for page {PageId} against {Server} failed", pageId, _serverLabel);
            return [];
        }
    }

    private static async Task<IReadOnlyList<ConflictEpisode>> QueryMariaDbAsync(
        CorpusConnectionFactory corpus,
        long pageId,
        CancellationToken ct)
    {
        await using var conn = await corpus.OpenAsync(ct);
        await using var cmd = new MySqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("@pageId", pageId);

        var list = new List<ConflictEpisode>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ConflictEpisode(
                EpisodeId: Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
                PageId: pageId,
                Kind: r.GetString(1),
                StartTs: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                EndTs: DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc),
                RevertCount: Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture),
                NamedParticipants: Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture),
                AnonParticipants: Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture),
                EditCount: Convert.ToInt32(r.GetValue(7), CultureInfo.InvariantCulture),
                ProtectionHint: Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture) != 0,
                Score: r.GetDouble(9)));
        }

        return list;
    }

    private sealed record CacheEntry(IReadOnlyList<ConflictEpisode> Value, DateTimeOffset ExpiresAt);
}
