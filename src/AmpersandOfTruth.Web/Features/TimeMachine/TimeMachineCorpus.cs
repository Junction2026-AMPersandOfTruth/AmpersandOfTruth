using System.Globalization;
using System.Text;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Investigations;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.TimeMachine;

public sealed record RevisionAt(
    long PageId,
    long RevId,
    long? ParentId,
    DateTime Ts,
    string? User,
    bool Anonymous,
    int Bytes);

public interface ITimeMachineCorpus
{
    Task<RevisionAt?> RevisionAtAsync(long pageId, DateTime atUtc, CancellationToken ct = default); // latest ts <= at; null before first

    Task<(DateTime First, DateTime Last, int Count)?> RangeAsync(long pageId, CancellationToken ct = default);

    Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default);               // revision_text by PK

    Task<RevisionAt?> RevisionByIdAsync(long pageId, long revId, CancellationToken ct = default) =>
        Task.FromResult<RevisionAt?>(null);

    Task<(string TitleFi, string? TitleEn)?> ArticleTitleAsync(long pageId, CancellationToken ct = default) =>
        Task.FromResult<(string, string?)?>(null);
}

public sealed class MariaDbTimeMachineCorpus(
    CorpusConnectionFactory corpus,
    ILogger<MariaDbTimeMachineCorpus> log) : ITimeMachineCorpus
{
    internal const string RevisionAtSql = """
        SELECT rev_id, parent_id, ts, `user`, anonymous, bytes
        FROM revision
        WHERE page_id = @p AND ts <= @at
        ORDER BY ts DESC, rev_id DESC
        LIMIT 1
        """;

    internal const string RevisionByIdSql = """
        SELECT rev_id, parent_id, ts, `user`, anonymous, bytes
        FROM revision
        WHERE page_id = @p AND rev_id = @revId
        LIMIT 1
        """;

    internal const string RangeSql = """
        SELECT MIN(ts), MAX(ts), COUNT(*)
        FROM revision
        WHERE page_id = @p
        """;

    internal const string TextSql = """
        SELECT `text`
        FROM revision_text
        WHERE page_id = @p AND rev_id = @revId
        LIMIT 1
        """;

    internal const string ArticleTitleSql = """
        SELECT COALESCE(
                   afi.title,
                   (SELECT r.title FROM revision r WHERE r.page_id = @p ORDER BY r.ts DESC, r.rev_id DESC LIMIT 1)
               ) AS title_fi,
               (SELECT aen.title
                FROM article aen
                WHERE aen.lang = 'en'
                  AND afi.wikidata_id IS NOT NULL
                  AND afi.wikidata_id <> ''
                  AND aen.wikidata_id = afi.wikidata_id
                LIMIT 1) AS title_en
        FROM (SELECT 1) AS d
        LEFT JOIN article afi ON afi.lang = 'fi' AND afi.page_id = @p
        """;

    public async Task<RevisionAt?> RevisionAtAsync(long pageId, DateTime atUtc, CancellationToken ct = default)
    {
        if (pageId <= 0) return null;

        RevisionAt? result = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(RevisionAtSql, conn);
            cmd.Parameters.AddWithValue("@p", pageId);
            cmd.Parameters.AddWithValue("@at", ToDbUtc(atUtc));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                result = ReadRevisionRow(pageId, r);
            }
        }, ct);

        return result;
    }

    public async Task<RevisionAt?> RevisionByIdAsync(long pageId, long revId, CancellationToken ct = default)
    {
        if (pageId <= 0 || revId <= 0) return null;

        RevisionAt? result = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(RevisionByIdSql, conn);
            cmd.Parameters.AddWithValue("@p", pageId);
            cmd.Parameters.AddWithValue("@revId", revId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                result = ReadRevisionRow(pageId, r);
            }
        }, ct);

        return result;
    }

    public async Task<(DateTime First, DateTime Last, int Count)?> RangeAsync(long pageId, CancellationToken ct = default)
    {
        if (pageId <= 0) return null;

        (DateTime First, DateTime Last, int Count)? result = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(RangeSql, conn);
            cmd.Parameters.AddWithValue("@p", pageId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct) && !r.IsDBNull(0) && !r.IsDBNull(1))
            {
                var count = Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    var first = DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc);
                    var last = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
                    result = (first, last, count);
                }
            }
        }, ct);

        return result;
    }

    public async Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default)
    {
        if (pageId <= 0 || revId <= 0) return null;

        string? text = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(TextSql, conn);
            cmd.Parameters.AddWithValue("@p", pageId);
            cmd.Parameters.AddWithValue("@revId", revId);

            text = await cmd.ExecuteScalarAsync(ct) switch
            {
                null or DBNull => null,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                var value => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
        }, ct);

        return text;
    }

    public async Task<(string TitleFi, string? TitleEn)?> ArticleTitleAsync(long pageId, CancellationToken ct = default)
    {
        if (pageId <= 0) return null;

        (string TitleFi, string? TitleEn)? result = null;
        await RunAsync(async conn =>
        {
            await using var cmd = new MySqlCommand(ArticleTitleSql, conn);
            cmd.Parameters.AddWithValue("@p", pageId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct) && !r.IsDBNull(0))
            {
                var titleFi = r.GetString(0);
                var titleEn = r.IsDBNull(1) ? null : r.GetString(1);
                result = (titleFi, titleEn);
            }
        }, ct);

        return result;
    }

    private static RevisionAt ReadRevisionRow(long pageId, MySqlDataReader r) =>
        new(
            PageId: pageId,
            RevId: Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
            ParentId: r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1), CultureInfo.InvariantCulture),
            Ts: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
            User: r.IsDBNull(3) ? null : r.GetString(3),
            Anonymous: Convert.ToBoolean(r.GetValue(4), CultureInfo.InvariantCulture),
            Bytes: Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture));

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
            log.LogWarning(ex, "Time machine corpus query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
