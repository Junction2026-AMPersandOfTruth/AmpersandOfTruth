using AmpersandOfTruth.Web.Features.Corpus;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Investigations;

/// <summary>
/// The corpus queries behind <c>/investigate</c>. Interface only so tests can fake MariaDB.
/// Implementations throw <see cref="CorpusUnavailableException"/> when the corpus can't be reached.
/// </summary>
public interface IInvestigationCorpus
{
    /// <summary>Nearest articles (all languages) to a bge-m3 query vector, closest first.</summary>
    Task<IReadOnlyList<TopicArticle>> NearestArticlesAsync(float[] vector, int limit, CancellationToken ct = default);

    /// <summary>Identity reverts on the given fi pages, newest first, at most <paramref name="perPage"/> per page.</summary>
    Task<IReadOnlyList<RevertEvent>> RevertsAsync(IReadOnlyCollection<long> pageIds, int perPage, CancellationToken ct = default);
}

public sealed class CorpusUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Raw SQL against the shared MariaDB corpus. Singleton; one pooled connection per call.</summary>
public sealed class MariaDbInvestigationCorpus(CorpusConnectionFactory corpus, ILogger<MariaDbInvestigationCorpus> log)
    : IInvestigationCorpus
{
    // Top-k inside a subquery so the HNSW index is used, then join for titles (shape verified with EXPLAIN, see deploy/corpus/README.md).
    internal const string NearestSql = """
        SELECT a.lang, a.page_id, a.title, v.distance
        FROM (
            SELECT lang, page_id, VEC_DISTANCE_COSINE(embedding, @q) AS distance
            FROM article_vector
            ORDER BY distance
            LIMIT @limit
        ) AS v
        JOIN article a USING (lang, page_id)
        ORDER BY v.distance
        """;

    // The upstream identity-revert rule (research/p0.5-gemini/extract_sample.py): revision R_k whose sha1 equals the
    // sha1 of an earlier revision R_j with j < k-1. R_j = most recent earlier occurrence; R_{k-1} = last reverted edit.
    // Drops null edits (sha1 == prev_sha1, e.g. FlaggedRevs/VakauttajaBot dummy revisions, #78) before numbering positions.
    // {pages} is replaced by generated parameter names (@p0, @p1, …) — never by values.
    internal const string RevertsSql = """
        WITH v AS (
            SELECT page_id, rev_id, ts, `user`, comment, sha1,
                   LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_sha1
            FROM revision
            WHERE page_id IN ({pages})
        ), m AS (
            SELECT page_id, rev_id, ts, `user`, comment, sha1,
                   ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS pos,
                   LAG(rev_id)  OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_rev_id,
                   LAG(`user`)  OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_user
            FROM v
            WHERE sha1 IS NULL OR prev_sha1 IS NULL OR sha1 <> prev_sha1
        ), s AS (
            SELECT page_id, rev_id, pos,
                   LAG(pos)    OVER (PARTITION BY page_id, sha1 ORDER BY pos) AS prev_pos,
                   LAG(rev_id) OVER (PARTITION BY page_id, sha1 ORDER BY pos) AS restored_rev_id
            FROM m
            WHERE sha1 IS NOT NULL
        ), r AS (
            SELECT m.page_id, m.rev_id, m.ts, m.`user`, m.comment, s.restored_rev_id,
                   CAST(s.pos AS SIGNED) - CAST(s.prev_pos AS SIGNED) - 1 AS reverted_count,
                   m.prev_rev_id, m.prev_user,
                   ROW_NUMBER() OVER (PARTITION BY m.page_id ORDER BY m.ts DESC, m.rev_id DESC) AS recent
            FROM s
            JOIN m ON m.page_id = s.page_id AND m.rev_id = s.rev_id
            WHERE s.prev_pos < s.pos - 1
        )
        SELECT page_id, rev_id, ts, `user`, comment, restored_rev_id, reverted_count, prev_rev_id, prev_user
        FROM r
        WHERE recent <= @perPage
        ORDER BY page_id, ts DESC, rev_id DESC
        """;

    public async Task<IReadOnlyList<TopicArticle>> NearestArticlesAsync(float[] vector, int limit, CancellationToken ct = default)
    {
        var rows = new List<TopicArticle>();
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(NearestSql, conn);
            cmd.Parameters.AddWithValue("@q", CorpusVectors.ToBytes(vector));
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add(new TopicArticle(r.GetString(0), Convert.ToInt64(r.GetValue(1)), r.GetString(2), Convert.ToDouble(r.GetValue(3))));
        }, ct);
        return rows;
    }

    public async Task<IReadOnlyList<RevertEvent>> RevertsAsync(IReadOnlyCollection<long> pageIds, int perPage, CancellationToken ct = default)
    {
        var rows = new List<RevertEvent>();
        if (pageIds.Count == 0) return rows;

        await RunAsync(async conn =>
        {
            var names = pageIds.Select((_, i) => $"@p{i}").ToArray();
            await using var cmd = new MySqlCommand(RevertsSql.Replace("{pages}", string.Join(", ", names)), conn);
            foreach (var (name, id) in names.Zip(pageIds))
                cmd.Parameters.AddWithValue(name, id);
            cmd.Parameters.AddWithValue("@perPage", perPage);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new RevertEvent(
                    PageId: Convert.ToInt64(r.GetValue(0)),
                    RevId: Convert.ToInt64(r.GetValue(1)),
                    Timestamp: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    User: r.IsDBNull(3) ? null : r.GetString(3),
                    Comment: r.IsDBNull(4) ? null : r.GetString(4),
                    RestoredRevId: Convert.ToInt64(r.GetValue(5)),
                    RevertedCount: Convert.ToInt32(r.GetValue(6)),
                    RevertedRevId: Convert.ToInt64(r.GetValue(7)),
                    RevertedUser: r.IsDBNull(8) ? null : r.GetString(8)));
            }
        }, ct);
        return rows;
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
            log.LogWarning(ex, "Corpus query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
