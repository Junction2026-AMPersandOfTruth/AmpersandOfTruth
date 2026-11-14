using AmpersandOfTruth.Web.Features.Corpus;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Revision metadata and wikitext from the corpus, for stages 2–3. Interface so tests can fake MariaDB.
/// Implementations throw <see cref="CorpusUnavailableException"/> when the corpus can't be reached.
/// </summary>
public interface IRevisionStore
{
    /// <summary>Number of revisions of the given pages (what "Scanning edit history" counts).</summary>
    Task<int> RevisionCountAsync(IReadOnlyCollection<long> pageIds, CancellationToken ct = default);

    /// <summary>
    /// The revisions strictly after <paramref name="restoredRevId"/> up to and including <paramref name="lastRevId"/> on one
    /// page, oldest first, without null revisions (same sha1 as their predecessor). At most <paramref name="limit"/> rows.
    /// </summary>
    Task<IReadOnlyList<RevisionRow>> SpanAsync(long pageId, long restoredRevId, long lastRevId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Revisions after (<paramref name="afterTs"/>, <paramref name="afterRevId"/>) on one page whose sha1 is one of
    /// <paramref name="sha1s"/>, oldest first, at most <paramref name="limit"/>. A revision with the same sha1 as the
    /// revision right before it (a null revision) is left out, so each restore counts once.
    /// </summary>
    Task<IReadOnlyList<RevisionRow>> RestoresAsync(
        long pageId, DateTime afterTs, long afterRevId, IReadOnlyCollection<string> sha1s, int limit, CancellationToken ct = default);

    /// <summary>
    /// Revisions after (<paramref name="afterTs"/>, <paramref name="afterRevId"/>) on one page whose content equals that of
    /// revision <paramref name="stateRevId"/> (same sha1), oldest first, at most <paramref name="limit"/>; null revisions
    /// left out as in <see cref="RestoresAsync"/>. With the restored revision of a revert: every later return to the page as
    /// it stood before the removed edit (P1.S10's re-removals).
    /// </summary>
    Task<IReadOnlyList<RevisionRow>> ReturnsToAsync(
        long pageId, DateTime afterTs, long afterRevId, long stateRevId, int limit, CancellationToken ct = default);

    /// <summary>Wikitext of one revision; null if the corpus has no text for it.</summary>
    Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default);
}

/// <summary>Raw SQL against <c>corpus.revision</c> / <c>corpus.revision_text</c> (PK lookups and one <c>idx_page_ts</c> range).</summary>
public sealed class MariaDbRevisionStore(CorpusConnectionFactory corpus, ILogger<MariaDbRevisionStore> log) : IRevisionStore
{
    // {pages} is replaced by generated parameter names (@p0, @p1, …) — never by values.
    internal const string CountSql = "SELECT COUNT(*) FROM revision WHERE page_id IN ({pages})";

    // The span between two revisions in page order (ts, rev_id) — the same order RevertsSql numbers positions in.
    // a.sha1 is the restored revision's hash, so the first row of the span can be checked for being a null revision too.
    internal const string SpanSql = """
        SELECT r.rev_id, r.parent_id, r.ts, r.`user`, r.comment, r.bytes, r.sha1, a.sha1 AS restored_sha1
        FROM revision r
        JOIN revision a ON a.page_id = r.page_id AND a.rev_id = @restored
        JOIN revision b ON b.page_id = r.page_id AND b.rev_id = @last
        WHERE r.page_id = @page
          AND (r.ts > a.ts OR (r.ts = a.ts AND r.rev_id > a.rev_id))
          AND (r.ts < b.ts OR (r.ts = b.ts AND r.rev_id <= b.rev_id))
        ORDER BY r.ts, r.rev_id
        LIMIT @limit
        """;

    // Window over the page's later history only (idx_page_ts), so LAG sees each row's real predecessor.
    // {sha1s} is replaced by generated parameter names (@s0, @s1, …) — never by values.
    internal const string RestoresSql = """
        SELECT rev_id, parent_id, ts, `user`, comment, bytes, sha1
        FROM (
            SELECT r.rev_id, r.parent_id, r.ts, r.`user`, r.comment, r.bytes, r.sha1,
                   LAG(r.sha1) OVER (ORDER BY r.ts, r.rev_id) AS prev_sha1
            FROM revision r
            WHERE r.page_id = @page AND (r.ts > @ts OR (r.ts = @ts AND r.rev_id > @rev))
        ) x
        WHERE x.sha1 IN ({sha1s}) AND (x.prev_sha1 IS NULL OR x.sha1 <> x.prev_sha1)
        ORDER BY ts, rev_id
        LIMIT @limit
        """;

    // As RestoresSql, with the one sha1 taken from the state revision (PK lookup).
    internal const string ReturnsToSql = """
        SELECT rev_id, parent_id, ts, `user`, comment, bytes, sha1
        FROM (
            SELECT r.rev_id, r.parent_id, r.ts, r.`user`, r.comment, r.bytes, r.sha1,
                   LAG(r.sha1) OVER (ORDER BY r.ts, r.rev_id) AS prev_sha1
            FROM revision r
            WHERE r.page_id = @page AND (r.ts > @ts OR (r.ts = @ts AND r.rev_id > @rev))
        ) x
        WHERE x.sha1 = (SELECT s.sha1 FROM revision s WHERE s.page_id = @page AND s.rev_id = @state)
          AND (x.prev_sha1 IS NULL OR x.sha1 <> x.prev_sha1)
        ORDER BY ts, rev_id
        LIMIT @limit
        """;

    internal const string TextSql = "SELECT text FROM revision_text WHERE page_id = @page AND rev_id = @rev";

    public async Task<int> RevisionCountAsync(IReadOnlyCollection<long> pageIds, CancellationToken ct = default)
    {
        if (pageIds.Count == 0) return 0;
        var count = 0;
        await RunAsync(async conn =>
        {
            var names = pageIds.Select((_, i) => $"@p{i}").ToArray();
            await using var cmd = new MySqlCommand(CountSql.Replace("{pages}", string.Join(", ", names)), conn);
            foreach (var (name, id) in names.Zip(pageIds))
                cmd.Parameters.AddWithValue(name, id);
            count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }, ct);
        return count;
    }

    public async Task<IReadOnlyList<RevisionRow>> SpanAsync(long pageId, long restoredRevId, long lastRevId, int limit, CancellationToken ct = default)
    {
        var rows = new List<RevisionRow>();
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(SpanSql, conn);
            cmd.Parameters.AddWithValue("@page", pageId);
            cmd.Parameters.AddWithValue("@restored", restoredRevId);
            cmd.Parameters.AddWithValue("@last", lastRevId);
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? previousSha1 = null;
            var first = true;
            while (await r.ReadAsync(ct))
            {
                var sha1 = r.IsDBNull(6) ? null : r.GetString(6);
                if (first) previousSha1 = r.IsDBNull(7) ? null : r.GetString(7);
                first = false;

                var isNullRevision = sha1 is not null && sha1 == previousSha1;
                previousSha1 = sha1;
                if (isNullRevision) continue;

                rows.Add(new RevisionRow(
                    PageId: pageId,
                    RevId: Convert.ToInt64(r.GetValue(0)),
                    ParentRevId: r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1)),
                    Timestamp: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    User: r.IsDBNull(3) ? null : r.GetString(3),
                    Comment: r.IsDBNull(4) ? null : r.GetString(4),
                    Bytes: Convert.ToInt32(r.GetValue(5)),
                    Sha1: sha1));
            }
        }, ct);
        return rows;
    }

    public async Task<IReadOnlyList<RevisionRow>> RestoresAsync(
        long pageId, DateTime afterTs, long afterRevId, IReadOnlyCollection<string> sha1s, int limit, CancellationToken ct = default)
    {
        var rows = new List<RevisionRow>();
        if (sha1s.Count == 0) return rows;
        await RunAsync(async conn =>
        {
            var names = sha1s.Select((_, i) => $"@s{i}").ToArray();
            await using var cmd = new MySqlCommand(RestoresSql.Replace("{sha1s}", string.Join(", ", names)), conn);
            foreach (var (name, sha1) in names.Zip(sha1s))
                cmd.Parameters.AddWithValue(name, sha1);
            cmd.Parameters.AddWithValue("@page", pageId);
            cmd.Parameters.AddWithValue("@ts", afterTs);
            cmd.Parameters.AddWithValue("@rev", afterRevId);
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new RevisionRow(
                    PageId: pageId,
                    RevId: Convert.ToInt64(r.GetValue(0)),
                    ParentRevId: r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1)),
                    Timestamp: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    User: r.IsDBNull(3) ? null : r.GetString(3),
                    Comment: r.IsDBNull(4) ? null : r.GetString(4),
                    Bytes: Convert.ToInt32(r.GetValue(5)),
                    Sha1: r.IsDBNull(6) ? null : r.GetString(6)));
            }
        }, ct);
        return rows;
    }

    public async Task<IReadOnlyList<RevisionRow>> ReturnsToAsync(
        long pageId, DateTime afterTs, long afterRevId, long stateRevId, int limit, CancellationToken ct = default)
    {
        var rows = new List<RevisionRow>();
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(ReturnsToSql, conn);
            cmd.Parameters.AddWithValue("@page", pageId);
            cmd.Parameters.AddWithValue("@ts", afterTs);
            cmd.Parameters.AddWithValue("@rev", afterRevId);
            cmd.Parameters.AddWithValue("@state", stateRevId);
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new RevisionRow(
                    PageId: pageId,
                    RevId: Convert.ToInt64(r.GetValue(0)),
                    ParentRevId: r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1)),
                    Timestamp: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    User: r.IsDBNull(3) ? null : r.GetString(3),
                    Comment: r.IsDBNull(4) ? null : r.GetString(4),
                    Bytes: Convert.ToInt32(r.GetValue(5)),
                    Sha1: r.IsDBNull(6) ? null : r.GetString(6)));
            }
        }, ct);
        return rows;
    }

    public async Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default)
    {
        string? text = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(TextSql, conn);
            cmd.Parameters.AddWithValue("@page", pageId);
            cmd.Parameters.AddWithValue("@rev", revId);
            text = await cmd.ExecuteScalarAsync(ct) switch
            {
                null or DBNull => null,
                byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
                var value => Convert.ToString(value),
            };
        }, ct);
        return text;
    }

    private async Task RunAsync(Func<MySqlConnection, Task> query, CancellationToken ct)
    {
        try
        {
            await using var conn = await corpus.OpenAsync(ct);
            await query(conn);
        }
        catch (Exception ex) when (ex is MySqlException or TimeoutException)
        {
            log.LogWarning(ex, "Revision query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
