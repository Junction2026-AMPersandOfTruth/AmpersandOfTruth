using System.Diagnostics;
using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Articles;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Articles;

[Trait("Category", "Corpus")]
public sealed class ArticleCorpusIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    private const long SuomenSisallissota = 1150;

    private IArticleCorpus Corpus => app.Services.GetRequiredService<IArticleCorpus>();
    private IConflictEpisodes ConflictEpisodesService => app.Services.GetRequiredService<IConflictEpisodes>();

    [CorpusFact]
    public async Task Suomen_sisallissota_aggregates_in_under_three_seconds_with_over_hundred_months()
    {
        var sw = Stopwatch.StartNew();
        var agg = await Corpus.AggregatesAsync(SuomenSisallissota);
        sw.Stop();

        Assert.NotNull(agg);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"AggregatesAsync took {sw.Elapsed.TotalMilliseconds:F0} ms (> 3000 ms).");
        Assert.Equal(SuomenSisallissota, agg.PageId);
        Assert.Equal("Suomen sisällissota", agg.TitleFi);
        Assert.True(agg.Months.Count > 100, $"Expected > 100 months, got {agg.Months.Count}.");
        Assert.True(agg.Revisions >= agg.Months.Sum(m => m.NamedEdits + m.AnonEdits));

        var contested = ArticleTimelineService.MostContested(agg);
        Assert.NotNull(contested);

        var revs = await Corpus.RevisionsAsync(SuomenSisallissota, contested.Range, limit: 50);
        Assert.NotEmpty(revs);
        Assert.Equal(revs.OrderByDescending(r => r.Ts).ThenByDescending(r => r.RevId).Select(r => r.RevId), revs.Select(r => r.RevId));
    }

    [CorpusFact]
    public async Task Unknown_page_returns_null_aggregates()
    {
        var agg = await Corpus.AggregatesAsync(999_999_999);
        Assert.Null(agg);
    }

    [CorpusFact]
    public async Task Suomen_sisallissota_1150_has_default_s4_conflict_episodes()
    {
        var episodes = await ConflictEpisodesService.ForPageAsync(SuomenSisallissota);

        Assert.NotEmpty(episodes);
        Assert.Contains(episodes, e => e.Kind == "edit_war");
        Assert.Contains(episodes, e => e.Kind == "ping_pong");
        Assert.Contains(episodes, e => e.Kind == "burst");
        Assert.All(episodes, e =>
        {
            Assert.Equal(SuomenSisallissota, e.PageId);
            Assert.True(e.EndTs >= e.StartTs);
            Assert.True(e.EditCount >= 1);
        });

        var summary = ConflictEpisodes.TryFormatBusiestSummary(episodes);
        Assert.NotNull(summary);
        Assert.StartsWith("Most contested period:", summary, StringComparison.Ordinal);
        Assert.Contains("back-and-forth edits", summary, StringComparison.Ordinal);
    }
}
