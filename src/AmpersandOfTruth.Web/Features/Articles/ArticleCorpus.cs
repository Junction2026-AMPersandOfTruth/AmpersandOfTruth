using System.Globalization;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Investigations;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Articles;

/// <summary>
/// Corpus queries behind <c>/articles/{pageId}</c> and the shared <c>ArticleTimeline</c>.
/// Implementations throw <see cref="CorpusUnavailableException"/> when MariaDB cannot be reached.
/// </summary>
public interface IArticleCorpus
{
    Task<ArticleAggregates?> AggregatesAsync(long pageId, CancellationToken ct = default);

    Task<IReadOnlyList<ArticleRevision>> RevisionsAsync(
        long pageId,
        DateRange range,
        int limit = 500,
        CancellationToken ct = default);
}

public sealed class MariaDbArticleCorpus(CorpusConnectionFactory corpus, ILogger<MariaDbArticleCorpus> log)
    : IArticleCorpus
{
    // Single grouped query per page:
    // - scans corpus.revision on idx_page_ts (page_id, ts)
    // - drops null edits (sha1 == prev_sha1) before numbering positions for identity-revert detection,
    //   matching MariaDbInvestigationCorpus.RevertsSql
    // - fetches fi + en sibling title via wikidata_id from corpus.article
    internal const string AggregatesSql = """
        WITH all_revs AS (
            SELECT rev_id, ts, anonymous, bytes, sha1, title,
                   LAG(sha1) OVER (ORDER BY ts, rev_id) AS prev_sha1
            FROM revision
            WHERE page_id = @pageId
        ),
        m AS (
            SELECT rev_id, ts, sha1,
                   ROW_NUMBER() OVER (ORDER BY ts, rev_id) AS pos
            FROM all_revs
            WHERE sha1 IS NULL OR prev_sha1 IS NULL OR sha1 <> prev_sha1
        ),
        s AS (
            SELECT ts, pos,
                   LAG(pos) OVER (PARTITION BY sha1 ORDER BY pos) AS prev_pos
            FROM m
            WHERE sha1 IS NOT NULL
        ),
        revert_months AS (
            SELECT YEAR(ts) AS yr, MONTH(ts) AS mo, COUNT(*) AS removals
            FROM s
            WHERE prev_pos < pos - 1
            GROUP BY YEAR(ts), MONTH(ts)
        ),
        month_agg AS (
            SELECT YEAR(ts) AS yr,
                   MONTH(ts) AS mo,
                   SUM(CASE WHEN anonymous = 0 THEN 1 ELSE 0 END) AS named_edits,
                   SUM(CASE WHEN anonymous <> 0 THEN 1 ELSE 0 END) AS anon_edits,
                   MAX(bytes) AS max_bytes
            FROM all_revs
            GROUP BY YEAR(ts), MONTH(ts)
        ),
        totals AS (
            SELECT COUNT(*) AS total_revs,
                   MIN(ts) AS first_edit,
                   MAX(ts) AS last_edit,
                   MAX(title) AS rev_title
            FROM all_revs
        ),
        titles AS (
            SELECT afi.title AS title_fi,
                   (SELECT aen.title
                    FROM article aen
                    WHERE aen.lang = 'en'
                      AND afi.wikidata_id IS NOT NULL
                      AND afi.wikidata_id <> ''
                      AND aen.wikidata_id = afi.wikidata_id
                    LIMIT 1) AS title_en
            FROM article afi
            WHERE afi.lang = 'fi' AND afi.page_id = @pageId
        )
        SELECT COALESCE(t.title_fi, tot.rev_title) AS title_fi,
               t.title_en,
               tot.total_revs,
               tot.first_edit,
               tot.last_edit,
               ma.yr,
               ma.mo,
               ma.named_edits,
               ma.anon_edits,
               COALESCE(rm.removals, 0) AS removals,
               ma.max_bytes
        FROM totals tot
        LEFT JOIN titles t ON 1 = 1
        LEFT JOIN month_agg ma ON 1 = 1
        LEFT JOIN revert_months rm ON rm.yr = ma.yr AND rm.mo = ma.mo
        WHERE tot.total_revs > 0 OR t.title_fi IS NOT NULL
        ORDER BY ma.yr, ma.mo
        """;

    // Computes DeltaBytes against the immediately preceding revision on the page and WasRemoved
    // when an identity revert later restored an earlier revision (r.prev_pos < m.pos < r.pos, excluding
    // 0-byte blanking collisions and wide spans > 50 revisions).
    internal const string RevisionsSql = """
        WITH all_revs AS (
            SELECT rev_id, parent_id, ts, `user`, anonymous, comment, bytes, sha1,
                   LAG(bytes) OVER (ORDER BY ts, rev_id) AS prev_bytes,
                   LAG(sha1)  OVER (ORDER BY ts, rev_id) AS prev_sha1
            FROM revision
            WHERE page_id = @pageId
        ),
        m AS (
            SELECT rev_id, sha1, bytes,
                   ROW_NUMBER() OVER (ORDER BY ts, rev_id) AS pos
            FROM all_revs
            WHERE sha1 IS NULL OR prev_sha1 IS NULL OR sha1 <> prev_sha1
        ),
        s AS (
            SELECT rev_id, pos, bytes,
                   CASE WHEN sha1 IS NOT NULL
                        THEN LAG(pos) OVER (PARTITION BY sha1 ORDER BY pos)
                   END AS prev_pos
            FROM m
        ),
        undone AS (
            SELECT rev_id,
                   pos,
                   MIN(CASE WHEN prev_pos IS NOT NULL
                                 AND prev_pos < pos - 1
                                 AND bytes > 0
                                 AND (pos - prev_pos - 1) <= 50
                            THEN prev_pos END)
                       OVER (ORDER BY pos ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS min_restored_pos
            FROM s
        )
        SELECT ar.rev_id,
               ar.parent_id,
               ar.ts,
               ar.`user`,
               ar.anonymous,
               ar.comment,
               ar.bytes,
               CAST(ar.bytes AS SIGNED) - CAST(COALESCE(ar.prev_bytes, 0) AS SIGNED) AS delta_bytes,
               CASE WHEN u.min_restored_pos IS NOT NULL AND u.min_restored_pos < u.pos THEN 1 ELSE 0 END AS was_removed
        FROM all_revs ar
        LEFT JOIN undone u ON u.rev_id = ar.rev_id
        WHERE ar.ts >= @from AND ar.ts < @to
        ORDER BY ar.ts DESC, ar.rev_id DESC
        LIMIT @limit
        """;

    public async Task<ArticleAggregates?> AggregatesAsync(long pageId, CancellationToken ct = default)
    {
        if (pageId <= 0) return null;

        ArticleAggregates? result = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(AggregatesSql, conn);
            cmd.Parameters.AddWithValue("@pageId", pageId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? titleFi = null;
            string? titleEn = null;
            int totalRevs = 0;
            DateTime firstEdit = DateTime.UnixEpoch;
            DateTime lastEdit = DateTime.UnixEpoch;
            var months = new List<MonthBucket>();
            var hasRows = false;

            while (await r.ReadAsync(ct))
            {
                if (!hasRows)
                {
                    hasRows = true;
                    titleFi = r.IsDBNull(0) ? $"Page {pageId.ToString(CultureInfo.InvariantCulture)}" : r.GetString(0);
                    titleEn = r.IsDBNull(1) ? null : r.GetString(1);
                    totalRevs = Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture);
                    firstEdit = r.IsDBNull(3) ? DateTime.UnixEpoch : DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc);
                    lastEdit = r.IsDBNull(4) ? DateTime.UnixEpoch : DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc);
                }

                if (!r.IsDBNull(5))
                {
                    months.Add(new MonthBucket(
                        Year: Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture),
                        Month: Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture),
                        NamedEdits: Convert.ToInt32(r.GetValue(7), CultureInfo.InvariantCulture),
                        AnonEdits: Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture),
                        Removals: Convert.ToInt32(r.GetValue(9), CultureInfo.InvariantCulture),
                        MaxBytes: Convert.ToInt32(r.GetValue(10), CultureInfo.InvariantCulture)));
                }
            }

            if (hasRows)
            {
                result = new ArticleAggregates(pageId, titleFi!, titleEn, totalRevs, firstEdit, lastEdit, months);
            }
        }, ct);

        return result;
    }

    public async Task<IReadOnlyList<ArticleRevision>> RevisionsAsync(
        long pageId,
        DateRange range,
        int limit = 500,
        CancellationToken ct = default)
    {
        var rows = new List<ArticleRevision>();
        if (pageId <= 0 || limit <= 0 || range.To <= range.From) return rows;

        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(RevisionsSql, conn);
            cmd.Parameters.AddWithValue("@pageId", pageId);
            cmd.Parameters.AddWithValue("@from", ToDbUtc(range.From));
            cmd.Parameters.AddWithValue("@to", ToDbUtc(range.To));
            cmd.Parameters.AddWithValue("@limit", limit);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new ArticleRevision(
                    RevId: Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
                    ParentId: r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1), CultureInfo.InvariantCulture),
                    Ts: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    User: r.IsDBNull(3) ? null : r.GetString(3),
                    Anonymous: Convert.ToBoolean(r.GetValue(4), CultureInfo.InvariantCulture),
                    Comment: r.IsDBNull(5) ? null : r.GetString(5),
                    Bytes: Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture),
                    DeltaBytes: Convert.ToInt32(r.GetValue(7), CultureInfo.InvariantCulture),
                    WasRemoved: Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture) != 0));
            }
        }, ct);

        return rows;
    }

    private static DateTime ToDbUtc(DateTime dt) =>
        DateTime.SpecifyKind(dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt, DateTimeKind.Unspecified);

    private async Task RunAsync(Func<MySqlConnection, Task> query, CancellationToken ct)
    {
        try
        {
            await using var conn = await corpus.OpenAsync(ct);
            await query(conn);
        }
        catch (Exception ex) when (ex is MySqlException or TimeoutException)
        {
            log.LogWarning(ex, "Article corpus query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
