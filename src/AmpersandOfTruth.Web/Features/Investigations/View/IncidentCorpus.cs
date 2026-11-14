using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>
/// Corpus lookups used by <see cref="IncidentMapper"/> to enrich <see cref="IncidentRecord"/>s into <see cref="IncidentView"/>s.
/// Implementations throw <see cref="CorpusUnavailableException"/> when the corpus cannot be reached.
/// </summary>
public interface IIncidentCorpus
{
    /// <summary>Primary-key lookups of revisions on one page (includes <c>parent_id</c>).</summary>
    Task<IReadOnlyList<RevisionRow>> RevisionsAsync(long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default);

    /// <summary>
    /// Finnish article titles from <c>corpus.article</c> (<c>lang='fi'</c>), paired with the English sibling title
    /// (<c>lang='en'</c> sharing the same non-null <c>wikidata_id</c>) when one exists.
    /// </summary>
    Task<IReadOnlyDictionary<long, ArticleRef>> ArticlesAsync(IReadOnlyCollection<long> pageIds, CancellationToken ct = default);
}

/// <summary>Raw SQL against <c>corpus.revision</c> and <c>corpus.article</c>.</summary>
public sealed class MariaDbIncidentCorpus(CorpusConnectionFactory corpus, ILogger<MariaDbIncidentCorpus> log) : IIncidentCorpus
{
    // {revs} / {pages} are replaced with generated parameter names (@r0, @p0, …) — never with values.
    internal const string RevisionsSql = """
        SELECT rev_id, parent_id, ts, `user`, comment, bytes
        FROM revision
        WHERE page_id = @page AND rev_id IN ({revs})
        ORDER BY ts, rev_id
        """;

    internal const string ArticlesSql = """
        SELECT fi.page_id, fi.title, en.title
        FROM article fi
        LEFT JOIN article en
          ON fi.wikidata_id IS NOT NULL
          AND fi.wikidata_id <> ''
          AND en.wikidata_id = fi.wikidata_id
          AND en.lang = 'en'
        WHERE fi.lang = 'fi' AND fi.page_id IN ({pages})
        ORDER BY fi.page_id, en.page_id
        """;

    public async Task<IReadOnlyList<RevisionRow>> RevisionsAsync(
        long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default)
    {
        var distinct = revIds.Distinct().ToArray();
        if (distinct.Length == 0) return [];

        var rows = new List<RevisionRow>(distinct.Length);
        await RunAsync(async conn =>
        {
            var names = distinct.Select((_, i) => $"@r{i}").ToArray();
            await using var cmd = new MySqlCommand(RevisionsSql.Replace("{revs}", string.Join(", ", names)), conn);
            cmd.Parameters.AddWithValue("@page", pageId);
            foreach (var (name, id) in names.Zip(distinct))
                cmd.Parameters.AddWithValue(name, id);

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
                    Bytes: Convert.ToInt32(r.GetValue(5))));
            }
        }, ct);
        return rows;
    }

    public async Task<IReadOnlyDictionary<long, ArticleRef>> ArticlesAsync(
        IReadOnlyCollection<long> pageIds, CancellationToken ct = default)
    {
        var distinct = pageIds.Distinct().ToArray();
        if (distinct.Length == 0) return new Dictionary<long, ArticleRef>();

        var result = new Dictionary<long, ArticleRef>(distinct.Length);
        await RunAsync(async conn =>
        {
            var names = distinct.Select((_, i) => $"@p{i}").ToArray();
            await using var cmd = new MySqlCommand(ArticlesSql.Replace("{pages}", string.Join(", ", names)), conn);
            foreach (var (name, id) in names.Zip(distinct))
                cmd.Parameters.AddWithValue(name, id);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var pid = Convert.ToInt64(r.GetValue(0));
                var titleFi = r.GetString(1);
                var titleEn = r.IsDBNull(2) ? null : r.GetString(2);
                result.TryAdd(pid, new ArticleRef(pid, titleFi, titleEn));
            }
        }, ct);
        return result;
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
            log.LogWarning(ex, "Incident corpus query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
