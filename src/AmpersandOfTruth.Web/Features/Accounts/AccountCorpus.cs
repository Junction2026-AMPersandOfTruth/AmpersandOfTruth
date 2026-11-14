using System.Collections.Concurrent;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Accounts;

/// <summary>
/// Observable corpus facts for a named Wikipedia account (<c>/accounts/{Name}</c>).
/// Implementations throw <see cref="CorpusUnavailableException"/> when the corpus cannot be reached.
/// </summary>
public interface IAccountCorpus
{
    Task<AccountFacts> FactsAsync(string user, CancellationToken ct = default);
}

/// <summary>
/// In-memory 1-hour cache over <see cref="IAccountCorpus"/>. Singleton, thread-safe.
/// Failures are not cached so retrying after corpus recovery succeeds immediately.
/// </summary>
public sealed class CachedAccountCorpus(IAccountCorpus inner, TimeProvider clock) : IAccountCorpus
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private const int MaxEntries = 5_000;

    private readonly ConcurrentDictionary<string, (AccountFacts Facts, DateTimeOffset ExpiresAt)> _cache =
        new(StringComparer.Ordinal);

    public async Task<AccountFacts> FactsAsync(string user, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var key = user.Trim();
        var now = clock.GetUtcNow();

        if (_cache.TryGetValue(key, out var hit) && hit.ExpiresAt > now)
            return hit.Facts;

        var facts = await inner.FactsAsync(key, ct);
        if (_cache.Count >= MaxEntries)
            _cache.Clear();

        _cache[key] = (facts, clock.GetUtcNow() + Ttl);
        return facts;
    }
}

/// <summary>
/// Raw SQL against <c>corpus.revision</c> (<c>idx_user</c>) and <c>corpus.article</c>.
/// Reuses the canonical identity-revert shape of <see cref="MariaDbInvestigationCorpus.RevertsSql"/>
/// restricted to the pages edited by the account.
/// </summary>
public sealed class MariaDbAccountCorpus(CorpusConnectionFactory corpus, ILogger<MariaDbAccountCorpus> log)
    : IAccountCorpus
{
    internal const int TopArticleLimit = 20;

    // Uses KEY idx_user (user), then refines with utf8mb4_bin so MediaWiki case/accent-distinct usernames never collide.
    internal const string PerYearSql = """
        SELECT YEAR(ts) AS yr, COUNT(*) AS edits, MIN(ts) AS first_ts, MAX(ts) AS last_ts
        FROM revision
        WHERE `user` = @u AND `user` COLLATE utf8mb4_bin = @u
        GROUP BY YEAR(ts)
        ORDER BY yr
        """;

    // Reuses MariaDbInvestigationCorpus.RevertsSql's CTE shape (v -> m -> s) restricted to the account's pages
    // that have at least one subsequent revision. A revision at pos is strictly between a restored_pos and reverting_pos
    // iff the minimum restored_pos among later identity reverts (prev_pos < pos - 1) on the page is < pos.
    internal const string ArticlesSql = """
        WITH u_pages AS (
            SELECT page_id,
                   COUNT(*) AS u_edits,
                   MIN(ts) AS min_u_ts,
                   MIN(rev_id) AS min_u_rev_id,
                   MAX(ts) AS max_u_ts,
                   MAX(rev_id) AS max_u_rev_id,
                   MAX(title) AS rev_title
            FROM revision
            WHERE `user` = @u AND `user` COLLATE utf8mb4_bin = @u
            GROUP BY page_id
        ), active_pages AS (
            SELECT up.page_id
            FROM u_pages up
            WHERE EXISTS (
                SELECT 1
                FROM revision r2
                WHERE r2.page_id = up.page_id
                  AND r2.ts >= up.min_u_ts
                  AND (r2.ts > up.min_u_ts OR r2.rev_id > up.min_u_rev_id)
                  AND r2.sha1 IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM revision r1
                      WHERE r1.page_id = r2.page_id
                        AND r1.sha1 = r2.sha1
                        AND r1.rev_id <> r2.rev_id
                        AND r1.ts <= up.max_u_ts
                        AND (r1.ts < up.max_u_ts OR r1.rev_id < up.max_u_rev_id)
                  )
            )
        ), v AS (
            SELECT r.page_id,
                   r.rev_id,
                   r.ts,
                   (r.`user` = @u AND r.`user` COLLATE utf8mb4_bin = @u) AS is_u,
                   r.sha1,
                   LAG(r.sha1) OVER (PARTITION BY r.page_id ORDER BY r.ts, r.rev_id) AS prev_sha1
            FROM active_pages ap
            JOIN revision r ON r.page_id = ap.page_id
        ), m AS (
            SELECT page_id,
                   is_u,
                   sha1,
                   ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS pos
            FROM v
            WHERE sha1 IS NULL OR prev_sha1 IS NULL OR sha1 <> prev_sha1
        ), s AS (
            SELECT page_id,
                   is_u,
                   pos,
                   CASE
                       WHEN sha1 IS NOT NULL THEN LAG(pos) OVER (PARTITION BY page_id, sha1 ORDER BY pos)
                   END AS prev_pos
            FROM m
        ), t AS (
            SELECT page_id,
                   is_u,
                   pos,
                   MIN(CASE WHEN prev_pos < pos - 1 THEN prev_pos END) OVER (
                       PARTITION BY page_id
                       ORDER BY pos
                       ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING
                   ) AS min_future_restored_pos
            FROM s
        ), removed_by_page AS (
            SELECT page_id,
                   SUM(CASE WHEN min_future_restored_pos < pos THEN 1 ELSE 0 END) AS removed_edits
            FROM t
            WHERE is_u = 1
            GROUP BY page_id
        ), ranked AS (
            SELECT up.page_id,
                   COALESCE(a.title, up.rev_title) AS title,
                   up.u_edits,
                   COALESCE(rbp.removed_edits, 0) AS removed_edits,
                   COUNT(*) OVER () AS total_articles,
                   SUM(COALESCE(rbp.removed_edits, 0)) OVER () AS total_removed,
                   ROW_NUMBER() OVER (
                       ORDER BY up.u_edits DESC, COALESCE(rbp.removed_edits, 0) DESC, up.page_id ASC
                   ) AS rn
            FROM u_pages up
            LEFT JOIN removed_by_page rbp ON rbp.page_id = up.page_id
            LEFT JOIN article a ON a.lang = 'fi' AND a.page_id = up.page_id
        )
        SELECT page_id, title, u_edits, removed_edits, total_articles, total_removed
        FROM ranked
        WHERE rn <= @topLimit
        ORDER BY rn
        """;

    public async Task<AccountFacts> FactsAsync(string user, CancellationToken ct = default)
    {
        var label = EditorDisplay.For(user);
        if (label.IsAnonymous || label.Kind != EditorKind.Named)
            return new AccountFacts(label.DisplayName, 0, 0, 0, null, null, [], []);

        var account = label.DisplayName;
        AccountFacts? result = null;

        await RunAsync(async conn =>
        {
            var perYear = new List<(int Year, int Edits)>();
            var totalEdits = 0;
            DateTime? first = null;
            DateTime? last = null;

            await using (var yearCmd = new MySqlCommand(PerYearSql, conn))
            {
                yearCmd.Parameters.AddWithValue("@u", account);
                await using var r = await yearCmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var year = Convert.ToInt32(r.GetValue(0));
                    var edits = Convert.ToInt32(r.GetValue(1));
                    var minTs = DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc);
                    var maxTs = DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc);

                    perYear.Add((year, edits));
                    totalEdits += edits;
                    if (first is null || minTs < first.Value) first = minTs;
                    if (last is null || maxTs > last.Value) last = maxTs;
                }
            }

            if (totalEdits == 0)
            {
                result = new AccountFacts(account, 0, 0, 0, null, null, [], []);
                return;
            }

            var topArticles = new List<AccountArticle>(TopArticleLimit);
            var totalArticles = 0;
            var totalRemoved = 0;

            await using (var artCmd = new MySqlCommand(ArticlesSql, conn))
            {
                artCmd.Parameters.AddWithValue("@u", account);
                artCmd.Parameters.AddWithValue("@topLimit", TopArticleLimit);
                await using var r = await artCmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    topArticles.Add(new AccountArticle(
                        PageId: Convert.ToInt64(r.GetValue(0)),
                        Title: r.GetString(1),
                        Edits: Convert.ToInt32(r.GetValue(2)),
                        RemovedEdits: Convert.ToInt32(r.GetValue(3))));
                    totalArticles = Convert.ToInt32(r.GetValue(4));
                    totalRemoved = Convert.ToInt32(r.GetValue(5));
                }
            }

            result = new AccountFacts(
                User: account,
                Edits: totalEdits,
                Articles: totalArticles,
                RemovedEdits: totalRemoved,
                First: first,
                Last: last,
                PerYear: perYear,
                TopArticles: topArticles);
        }, ct);

        return result!;
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
            log.LogWarning(ex, "Account corpus query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
