using System.Collections.Concurrent;
using System.Globalization;

namespace AmpersandOfTruth.Web.Features.Articles;

public sealed record ContestedPeriod(DateRange Range, int Removals, string Summary);

/// <summary>
/// Singleton service behind <c>/articles/{pageId}</c> and <c>ArticleTimeline</c>: caches 20-year monthly aggregates
/// for 1 hour and computes the most contested up-to-6-month span and plain-language summary.
/// </summary>
public sealed class ArticleTimelineService(IArticleCorpus corpus, TimeProvider? time = null)
{
    public const string EmptyRemovalsSummary = "No removed edits found in this article's history.";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<long, CacheEntry> _cache = new();

    public async Task<ArticleAggregates?> AggregatesAsync(long pageId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (_cache.TryGetValue(pageId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Value;
        }

        var fresh = await corpus.AggregatesAsync(pageId, ct);
        _cache[pageId] = new CacheEntry(fresh, _time.GetUtcNow() + CacheTtl);
        return fresh;
    }

    public Task<IReadOnlyList<ArticleRevision>> RevisionsAsync(
        long pageId,
        DateRange range,
        int limit = 500,
        CancellationToken ct = default) =>
        corpus.RevisionsAsync(pageId, range, limit, ct);

    /// <summary>
    /// Finds the span of up to 6 consecutive calendar months with the most removals (trimmed to months with removals &gt; 0).
    /// Returns <c>null</c> when the article has no removals.
    /// </summary>
    public static ContestedPeriod? MostContested(ArticleAggregates aggregates)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        if (aggregates.Months.Count == 0) return null;

        var ordered = aggregates.Months
            .OrderBy(m => m.Year)
            .ThenBy(m => m.Month)
            .ToList();

        int bestSum = 0;
        int bestStartIdx = -1;
        int bestEndIdx = -1;
        int bestSpanMonths = int.MaxValue;

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Removals <= 0) continue;

            int startMonthIndex = MonthIndex(ordered[i].Year, ordered[i].Month);
            int runningSum = 0;

            for (int j = i; j < ordered.Count; j++)
            {
                int endMonthIndex = MonthIndex(ordered[j].Year, ordered[j].Month);
                int spanMonths = endMonthIndex - startMonthIndex + 1;
                if (spanMonths > 6) break;

                runningSum += ordered[j].Removals;
                if (ordered[j].Removals <= 0) continue;

                if (runningSum > bestSum || (runningSum == bestSum && spanMonths < bestSpanMonths))
                {
                    bestSum = runningSum;
                    bestStartIdx = i;
                    bestEndIdx = j;
                    bestSpanMonths = spanMonths;
                }
            }
        }

        if (bestSum <= 0 || bestStartIdx < 0) return null;

        var first = ordered[bestStartIdx];
        var last = ordered[bestEndIdx];
        var from = new DateTime(first.Year, first.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(last.Year, last.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var range = new DateRange(from, to);

        var startLabel = from.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var endLabel = new DateTime(last.Year, last.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var periodLabel = first.Year == last.Year && first.Month == last.Month
            ? startLabel
            : $"{startLabel} \u2013 {endLabel}";
        var noun = bestSum == 1 ? "removal" : "removals";
        var summary = $"Most contested period: {periodLabel} ({bestSum.ToString("N0", CultureInfo.InvariantCulture)} {noun}).";

        return new ContestedPeriod(range, bestSum, summary);
    }

    public static string Summary(ArticleAggregates aggregates) =>
        MostContested(aggregates)?.Summary ?? EmptyRemovalsSummary;

    public static DateRange AllRange(ArticleAggregates aggregates)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        if (aggregates.Months.Count == 0)
        {
            var start = new DateTime(aggregates.FirstEdit.Year, aggregates.FirstEdit.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(aggregates.LastEdit.Year, aggregates.LastEdit.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            return new DateRange(start, end > start ? end : start.AddMonths(1));
        }

        var min = aggregates.Months.MinBy(m => MonthIndex(m.Year, m.Month))!;
        var max = aggregates.Months.MaxBy(m => MonthIndex(m.Year, m.Month))!;
        var from = new DateTime(min.Year, min.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(max.Year, max.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return new DateRange(from, to);
    }

    public static DateRange LastFiveYearsRange(ArticleAggregates aggregates)
    {
        var all = AllRange(aggregates);
        var fiveYearsAgo = all.To.AddYears(-5);
        var from = fiveYearsAgo > all.From ? fiveYearsAgo : all.From;
        return new DateRange(from, all.To);
    }

    internal static int MonthIndex(int year, int month) => year * 12 + (month - 1);

    private sealed record CacheEntry(ArticleAggregates? Value, DateTimeOffset ExpiresAt);
}
