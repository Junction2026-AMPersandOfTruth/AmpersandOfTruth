using System.Globalization;
using AmpersandOfTruth.Web.Features.Corpus;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>The three kinds of P1.S4 conflict episode (<c>research.s4_episode.kind</c>).</summary>
public enum ConflictKind
{
    /// <summary>At least three undos between named editors within seven days, at least one pair undoing each other.</summary>
    EditWar,

    /// <summary>The page alternated between the same exact versions (A → B → A → B), two or more editors.</summary>
    PingPong,

    /// <summary>An unusually busy day. Two thirds of these have no undo at all: attention, not conflict.</summary>
    Burst,
}

/// <summary>
/// One precomputed S4 episode (#31) that holds revisions of a removed edit.
/// </summary>
/// <param name="UndoCount">Identity reverts inside the episode that are not bot or self reverts (<c>revert_count</c>).</param>
/// <param name="RevIds">Which of the asked revisions lie inside the episode.</param>
public sealed record ConflictEpisode(
    long EpisodeId, ConflictKind Kind, DateTime Start, DateTime End, int UndoCount, int EditCount, IReadOnlyList<long> RevIds);

/// <summary>
/// P1.S4's conflict episodes, read where the experiment left them: <c>research.s4_episode</c> /
/// <c>research.s4_episode_rev</c> (built by <c>research/31-s4-conflict/build.py</c>). Interface so tests can fake MariaDB.
/// Implementations throw <see cref="CorpusUnavailableException"/> when the tables can't be read.
/// </summary>
public interface IConflictEpisodes
{
    /// <summary>The default episodes (7-day edit wars, ping-pongs, bursts) of one page that contain any of
    /// <paramref name="revIds"/>, by episode id.</summary>
    Task<IReadOnlyList<ConflictEpisode>> ContainingAsync(long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default);
}

/// <summary>
/// P1.S4 as a tier input: when is a removed edit "part of a back-and-forth over the passage" (evidence standard §1,
/// <see cref="Evidence.IncidentEvidence.InEditWar"/>)? Pure.
/// <list type="bullet">
/// <item>Only edit wars and ping-pongs count. A burst alone is attention, not conflict (S4 README, caveat 1).</item>
/// <item>The removal <b>and</b> at least one of the removed revisions must lie inside the same episode, so the whole
/// add-and-remove cycle happened during the back-and-forth. An episode holds every revision in its time range, so an
/// unrelated edit made and removed during someone else's dispute still qualifies; the tier rules ask for a content
/// assessment on top for exactly that reason (the asymmetry rule).</item>
/// </list>
/// </summary>
public static class EditWars
{
    /// <summary>The episode that puts this removed edit in a back-and-forth, or null. With several, an edit war goes
    /// before a ping-pong, then the one with more undos.</summary>
    public static ConflictEpisode? Of(IReadOnlyList<ConflictEpisode>? episodes, IReadOnlyList<RevisionRow> span, long removingRevId)
    {
        if (episodes is null || episodes.Count == 0) return null;
        var inserted = span.Select(r => r.RevId).ToHashSet();
        return episodes
            .Where(e => e.Kind is ConflictKind.EditWar or ConflictKind.PingPong
                && e.RevIds.Contains(removingRevId) && e.RevIds.Any(inserted.Contains))
            .OrderBy(e => e.Kind)
            .ThenByDescending(e => e.UndoCount)
            .ThenBy(e => e.EpisodeId)
            .FirstOrDefault();
    }

    /// <summary>The episode in plain words, for the signal's evidence: counts and dates only, no editor.</summary>
    public static string Describe(ConflictEpisode episode)
    {
        static string Day(DateTime t) => t.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var undos = episode.UndoCount == 1 ? "1 undoing edit" : $"{episode.UndoCount} undoing edits";
        return episode.Start.Date == episode.End.Date
            ? $"{undos} on {Day(episode.Start)}"
            : $"{undos} between {Day(episode.Start)} and {Day(episode.End)}";
    }
}

/// <summary>Raw SQL against the scratch schema <c>research</c> (<c>corpus_ro</c> may read it); one PK/index lookup per call.</summary>
public sealed class MariaDbConflictEpisodes(CorpusConnectionFactory corpus, ILogger<MariaDbConflictEpisodes> log) : IConflictEpisodes
{
    // idx_rev_id finds the memberships; is_default = 1 keeps edit_war '7d' + ping_pong + burst (S4 README, usage pattern 1).
    // {revs} is replaced by generated parameter names (@r0, @r1, …) — never by values.
    internal const string ContainingSql = """
        SELECT e.episode_id, e.kind, e.start_ts, e.end_ts, e.revert_count, e.edit_count, er.rev_id
        FROM research.s4_episode_rev er
        JOIN research.s4_episode e ON e.episode_id = er.episode_id
        WHERE er.rev_id IN ({revs}) AND e.page_id = @page AND e.is_default = 1
        ORDER BY e.episode_id, er.rev_id
        """;

    private int _failures;

    public async Task<IReadOnlyList<ConflictEpisode>> ContainingAsync(long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default)
    {
        if (revIds.Count == 0) return [];
        var rows = new List<(long Episode, ConflictKind? Kind, DateTime Start, DateTime End, int Undos, int Edits, long Rev)>();
        try
        {
            await using var conn = await corpus.OpenAsync(ct);
            var names = revIds.Select((_, i) => $"@r{i}").ToArray();
            await using var cmd = new MySqlCommand(ContainingSql.Replace("{revs}", string.Join(", ", names)), conn);
            foreach (var (name, id) in names.Zip(revIds))
                cmd.Parameters.AddWithValue(name, id);
            cmd.Parameters.AddWithValue("@page", pageId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add((
                    Convert.ToInt64(r.GetValue(0)),
                    ParseKind(r.GetString(1)),
                    DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc),
                    Convert.ToInt32(r.GetValue(4)),
                    Convert.ToInt32(r.GetValue(5)),
                    Convert.ToInt64(r.GetValue(6))));
            }
        }
        catch (Exception ex) when (ex is MySqlException or TimeoutException)
        {
            // Loud once (a missing research.s4_* table switches the signal off everywhere), quiet afterwards.
            if (Interlocked.Increment(ref _failures) == 1)
                log.LogWarning(ex, "S4 conflict episodes on {Server} can't be read; the edit-war signal is off until they can", corpus.Server);
            else
                log.LogDebug(ex, "S4 conflict episode lookup failed");
            throw new CorpusUnavailableException($"Conflict episodes on {corpus.Server} not available: {ex.Message}", ex);
        }

        return rows
            .Where(x => x.Kind is not null)
            .GroupBy(x => x.Episode)
            .Select(g => new ConflictEpisode(
                g.Key, g.First().Kind!.Value, g.First().Start, g.First().End, g.First().Undos, g.First().Edits, [.. g.Select(x => x.Rev)]))
            .ToList();
    }

    /// <summary>A kind a later S4 build might add is skipped, not an error.</summary>
    internal static ConflictKind? ParseKind(string kind) => kind switch
    {
        "edit_war" => ConflictKind.EditWar,
        "ping_pong" => ConflictKind.PingPong,
        "burst" => ConflictKind.Burst,
        _ => null,
    };
}
