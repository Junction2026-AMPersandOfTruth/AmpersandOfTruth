using System.Globalization;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Investigations;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Hotspots;

/// <summary>
/// Singleton store for <c>/hotspots</c>. Reads all four <c>research.hotspot_*</c> tables in one connection
/// and one multi-result batch, caching a clean snapshot in memory for 10 minutes.
/// Missing tables return <c>null</c>; unreachable corpus throws <see cref="CorpusUnavailableException"/>.
/// </summary>
public sealed class MariaDbHotspotStore : IHotspotStore
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    internal const string LoadAllSql = """
        SELECT revisions, articles, reverts, episodes, anon_reverted_share
        FROM research.hotspot_headline
        LIMIT 1;

        SELECT page_id, title_fi, title_en, contest_score, reverts,
               reverts_dispute, reverts_vandalism, reverts_other,
               episodes, anon_share, first_ts, last_ts, yearly_reverts
        FROM research.hotspot_article
        ORDER BY contest_score DESC, reverts DESC, page_id ASC;

        SELECT `month`, reverts, episodes, edits, top_page_id, top_title_fi, top_title_en
        FROM research.hotspot_month
        ORDER BY `month` ASC;

        SELECT category, articles, contest_score, reverts, dispute_reverts, episodes
        FROM research.hotspot_category
        ORDER BY contest_score DESC, dispute_reverts DESC, category ASC;
        """;

    private readonly Func<CancellationToken, Task<HotspotData?>> _loader;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CacheEntry? _cache;

    public MariaDbHotspotStore(
        CorpusConnectionFactory corpus,
        ILogger<MariaDbHotspotStore> log,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(log);
        _time = time ?? TimeProvider.System;
        _loader = ct => QueryMariaDbAsync(corpus, log, ct);
    }

    internal MariaDbHotspotStore(
        Func<CancellationToken, Task<HotspotData?>> loader,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _time = time ?? TimeProvider.System;
    }

    public async Task<HotspotData?> LoadAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (Volatile.Read(ref _cache) is { } hit && hit.ExpiresAt > now)
        {
            return hit.Value;
        }

        await _gate.WaitAsync(ct);
        try
        {
            now = _time.GetUtcNow();
            if (Volatile.Read(ref _cache) is { } secondHit && secondHit.ExpiresAt > now)
            {
                return secondHit.Value;
            }

            var fresh = await _loader(ct);
            if (fresh is not null)
            {
                Volatile.Write(ref _cache, new CacheEntry(fresh, _time.GetUtcNow() + CacheTtl));
            }

            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool IsMissingTable(MySqlException ex) =>
        ex.ErrorCode == MySqlErrorCode.NoSuchTable || ex.Number == 1146;

    private static async Task<HotspotData?> QueryMariaDbAsync(
        CorpusConnectionFactory corpus,
        ILogger<MariaDbHotspotStore> log,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await corpus.OpenAsync(ct);
            await using var cmd = new MySqlCommand(LoadAllSql, conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);

            // 1. research.hotspot_headline
            if (!await r.ReadAsync(ct))
            {
                log.LogWarning("research.hotspot_headline is empty on {Server}", corpus.Server);
                return null;
            }

            var headline = new HotspotHeadline(
                Revisions: Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
                Articles: Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                Reverts: Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                Episodes: Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                AnonRevertedShare: r.IsDBNull(4) ? 0.0 : Convert.ToDouble(r.GetValue(4), CultureInfo.InvariantCulture));

            // 2. research.hotspot_article
            var articles = new List<HotArticle>(200);
            if (await r.NextResultAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    articles.Add(new HotArticle(
                        PageId: Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
                        TitleFi: r.GetString(1),
                        TitleEn: r.IsDBNull(2) ? null : r.GetString(2),
                        ContestScore: Convert.ToDouble(r.GetValue(3), CultureInfo.InvariantCulture),
                        Reverts: Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture),
                        RevertsDispute: Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture),
                        RevertsVandalism: Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture),
                        RevertsOther: Convert.ToInt32(r.GetValue(7), CultureInfo.InvariantCulture),
                        Episodes: Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture),
                        AnonShare: r.IsDBNull(9) ? 0.0 : Convert.ToDouble(r.GetValue(9), CultureInfo.InvariantCulture),
                        FirstTs: r.IsDBNull(10) ? DateTime.UnixEpoch : DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc),
                        LastTs: r.IsDBNull(11) ? DateTime.UnixEpoch : DateTime.SpecifyKind(r.GetDateTime(11), DateTimeKind.Utc),
                        YearlyReverts: HotspotAnalytics.ParseYearlyReverts(r.IsDBNull(12) ? null : r.GetString(12))));
                }
            }

            // 3. research.hotspot_month
            var months = new List<HotMonth>(288);
            if (await r.NextResultAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    months.Add(new HotMonth(
                        Month: r.GetString(0),
                        Reverts: Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                        Episodes: Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                        Edits: Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                        TopPageId: r.IsDBNull(4) ? null : Convert.ToInt64(r.GetValue(4), CultureInfo.InvariantCulture),
                        TopTitleFi: r.IsDBNull(5) ? null : r.GetString(5),
                        TopTitleEn: r.IsDBNull(6) ? null : r.GetString(6)));
                }
            }

            // 4. research.hotspot_category
            var categories = new List<HotCategory>(50);
            if (await r.NextResultAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    categories.Add(new HotCategory(
                        Category: r.GetString(0),
                        Articles: Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                        ContestScore: Convert.ToDouble(r.GetValue(2), CultureInfo.InvariantCulture),
                        Reverts: Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                        DisputeReverts: Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture),
                        Episodes: Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture)));
                }
            }

            return new HotspotData(headline, articles, months, categories);
        }
        catch (MySqlException ex) when (IsMissingTable(ex))
        {
            log.LogWarning("Hotspot precompute tables missing on {Server} ({Message}); returning empty state", corpus.Server, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is MySqlException or TimeoutException)
        {
            log.LogWarning(ex, "Hotspot store query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }

    private sealed record CacheEntry(HotspotData Value, DateTimeOffset ExpiresAt);
}
