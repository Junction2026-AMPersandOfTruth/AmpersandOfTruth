using AmpersandOfTruth.Web.Features.Articles;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Articles;

public sealed class ArticleTimelineServiceTests
{
    [Fact]
    public void MostContested_picks_best_up_to_six_month_window_and_formats_summary()
    {
        var aggregates = new ArticleAggregates(
            PageId: 1256,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 3104,
            FirstEdit: new DateTime(2003, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Months:
            [
                new MonthBucket(2009, 6, 15, 5, Removals: 12, MaxBytes: 30_000),
                new MonthBucket(2014, 11, 20, 10, Removals: 8, MaxBytes: 40_000),
                new MonthBucket(2014, 12, 18, 9, Removals: 7, MaxBytes: 41_000),
                new MonthBucket(2015, 1, 25, 12, Removals: 10, MaxBytes: 42_000),
                new MonthBucket(2015, 2, 14, 6, Removals: 5, MaxBytes: 42_500),
                new MonthBucket(2015, 3, 16, 4, Removals: 7, MaxBytes: 43_000),
                // May 2015 is month 7 from Nov 2014, so it cannot be combined with Nov 2014 in a 6-month window
                new MonthBucket(2015, 5, 10, 2, Removals: 6, MaxBytes: 43_200),
            ]);

        var contested = ArticleTimelineService.MostContested(aggregates);

        Assert.NotNull(contested);
        Assert.Equal(37, contested.Removals);
        Assert.Equal(
            new DateRange(
                new DateTime(2014, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2015, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            contested.Range);
        Assert.Equal("Most contested period: Nov 2014 \u2013 Mar 2015 (37 removals).", contested.Summary);
        Assert.Equal(contested.Summary, ArticleTimelineService.Summary(aggregates));
    }

    [Fact]
    public void MostContested_returns_null_and_empty_wording_when_no_removals()
    {
        var noRemovals = new ArticleAggregates(
            PageId: 42,
            TitleFi: "Rauhallinen artikkeli",
            TitleEn: null,
            Revisions: 10,
            FirstEdit: new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2012, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Months:
            [
                new MonthBucket(2010, 1, 5, 0, Removals: 0, MaxBytes: 5_000),
                new MonthBucket(2012, 1, 5, 0, Removals: 0, MaxBytes: 6_000),
            ]);

        Assert.Null(ArticleTimelineService.MostContested(noRemovals));
        Assert.Equal("No removed edits found in this article's history.", ArticleTimelineService.Summary(noRemovals));

        var emptyHistory = noRemovals with { Revisions = 0, Months = [] };
        Assert.Null(ArticleTimelineService.MostContested(emptyHistory));
        Assert.Equal("No removed edits found in this article's history.", ArticleTimelineService.Summary(emptyHistory));
    }

    [Fact]
    public async Task AggregatesAsync_caches_second_call_until_ttl_expires()
    {
        var corpus = new FakeArticleCorpus();
        corpus.Articles[1150] = new ArticleAggregates(
            PageId: 1150,
            TitleFi: "Suomen sisällissota",
            TitleEn: "Finnish Civil War",
            Revisions: 2500,
            FirstEdit: new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Months: [new MonthBucket(2018, 1, 20, 4, 3, 85_000)]);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var service = new ArticleTimelineService(corpus, clock);

        var first = await service.AggregatesAsync(1150);
        clock.Advance(TimeSpan.FromMinutes(30));
        var second = await service.AggregatesAsync(1150);

        Assert.Same(first, second);
        Assert.Equal(1, corpus.AggregatesCalls);

        // After 1h TTL expires, the next call refreshes from the corpus
        clock.Advance(TimeSpan.FromMinutes(31));
        await service.AggregatesAsync(1150);
        Assert.Equal(2, corpus.AggregatesCalls);
    }
}
