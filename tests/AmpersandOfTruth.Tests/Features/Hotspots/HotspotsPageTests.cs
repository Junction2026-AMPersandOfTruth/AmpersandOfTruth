using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Hotspots;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Hotspots;

public sealed class HotspotsPageTests : BunitContext
{
    private readonly FakeHotspotStore _store = new();

    public HotspotsPageTests()
    {
        Services.AddSingleton<IHotspotStore>(_store);
    }

    [Fact]
    public void Renders_headline_ranked_articles_activity_chart_spikes_categories_and_caveat()
    {
        _store.Data = FakeHotspotStore.SampleData();

        var cut = Render<HotspotsPage>();

        var page = cut.Find("[data-testid=hotspots-page]");

        // 1. Caveat present with required wording and zero copy-rule / jargon violations
        var caveat = cut.Find("[data-testid=hotspots-caveat]");
        Assert.Contains(
            "shows that editors disagreed, not that the article contains misinformation",
            caveat.TextContent,
            StringComparison.OrdinalIgnoreCase);

        // 2. Headline numbers
        var headline = cut.Find("[data-testid=headline-numbers]").TextContent;
        Assert.Contains("862,076", headline);
        Assert.Contains("19,742", headline);
        Assert.Contains("41,606", headline);
        Assert.Contains("5,178", headline);
        Assert.Contains("70.1%", headline);

        // 3. Ranked articles in order with /articles/{pageId} and TopicSlug.Route(title) links + sparklines
        var articlesContainer = cut.Find("[data-testid=contested-articles]");
        var articleRows = articlesContainer.QuerySelectorAll("[data-testid=contested-article]");
        Assert.Equal(3, articleRows.Length);

        var row0Link = articleRows[0].QuerySelector("[data-testid=article-link]")!;
        Assert.Equal("Continuation War", row0Link.TextContent.Trim());
        Assert.Equal("/articles/1213", row0Link.GetAttribute("href"));
        Assert.Contains("Jatkosota", articleRows[0].TextContent);
        Assert.Equal(
            TopicSlug.Route("Continuation War"),
            articleRows[0].QuerySelector("[data-testid=investigate-link]")!.GetAttribute("href"));
        Assert.NotNull(articleRows[0].QuerySelector("[data-testid=article-sparkline]"));

        var row1Link = articleRows[1].QuerySelector("[data-testid=article-link]")!;
        Assert.Equal("Winter War", row1Link.TextContent.Trim());
        Assert.Equal("/articles/1256", row1Link.GetAttribute("href"));
        Assert.Contains("Talvisota", articleRows[1].TextContent);
        Assert.Equal(
            TopicSlug.Route("Winter War"),
            articleRows[1].QuerySelector("[data-testid=investigate-link]")!.GetAttribute("href"));

        // Article without English sibling title falls back to Finnish title for both display and Investigate link
        var row2Link = articleRows[2].QuerySelector("[data-testid=article-link]")!;
        Assert.Equal("Karjalan palautus", row2Link.TextContent.Trim());
        Assert.Equal("/articles/4099", row2Link.GetAttribute("href"));
        Assert.Equal(
            TopicSlug.Route("Karjalan palautus"),
            articleRows[2].QuerySelector("[data-testid=investigate-link]")!.GetAttribute("href"));

        // 4. Activity-over-time chart renders bars by year and labels the 5 biggest spikes by top article
        var chart = cut.Find("[data-testid=activity-chart]");
        var bars = chart.QuerySelectorAll("rect[data-testid=activity-bar]");
        Assert.Equal(7, bars.Length);
        Assert.Equal(
            ["2006", "2007", "2008", "2012", "2015", "2020", "2024"],
            bars.Select(b => b.GetAttribute("data-year") ?? "").ToArray());

        var spikes = chart.QuerySelectorAll("[data-testid=spike-label]");
        Assert.Equal(5, spikes.Length);
        Assert.Equal(
            ["2007", "2008", "2012", "2015", "2020"],
            spikes.Select(s => s.GetAttribute("data-year") ?? "").ToArray());
        var spikesText = string.Join(" | ", spikes.Select(s => s.TextContent));
        Assert.Contains("Kalevala", spikesText);
        Assert.Contains("Historical provinces", spikesText);
        Assert.Contains("Pekka Haavisto", spikesText);
        Assert.Contains("Sauli Niinistö", spikesText);
        Assert.Contains("COVID-19 pandemic", spikesText);

        // 5. Contested categories ranked horizontal bar list
        var categories = cut.Find("[data-testid=contested-categories]");
        var catRows = categories.QuerySelectorAll("[data-testid=contested-category]");
        Assert.Equal(3, catRows.Length);
        Assert.Contains("Kansanedustajat", catRows[0].TextContent);
        Assert.Contains("Suomen sisällissota", catRows[1].TextContent);
        Assert.Contains("Suomen historia", catRows[2].TextContent);
        Assert.All(catRows, r => Assert.NotNull(r.QuerySelector("svg rect.named")));

        // 6. Entire rendered page has zero evidence-standard copy or jargon violations
        Assert.Empty(CopyRules.FindViolations(page.TextContent, includeJargon: true));
    }

    [Fact]
    public void Null_data_from_store_renders_empty_state()
    {
        _store.Data = null;

        var cut = Render<HotspotsPage>();

        Assert.NotNull(cut.Find("[data-testid=hotspots-page]"));
        Assert.NotNull(cut.Find("[data-testid=hotspots-caveat]"));
        var empty = cut.Find("[data-testid=hotspots-empty]");
        Assert.Contains("Hot-spot aggregates have not been built yet.", empty.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=headline-numbers]"));
        Assert.Empty(cut.FindAll("[data-testid=hotspots-error]"));
    }

    [Fact]
    public void Throwing_store_renders_error_state_and_retry_recovers()
    {
        _store.Failure = FakeHotspotStore.DownException();

        var cut = Render<HotspotsPage>();

        var err = cut.Find("[data-testid=hotspots-error]");
        Assert.Contains("aot-banner--error", err.ClassList);
        Assert.Equal("alert", err.GetAttribute("role"));
        Assert.Contains("connection refused", err.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=headline-numbers]"));
        Assert.Equal(1, _store.Calls);

        _store.Failure = null;
        _store.Data = FakeHotspotStore.SampleData();
        cut.Find("[data-testid=status-retry]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=hotspots-error]"));
            Assert.Contains("862,076", cut.Find("[data-testid=headline-numbers]").TextContent);
            Assert.Equal(3, cut.FindAll("[data-testid=contested-article]").Count);
        });
        Assert.Equal(2, _store.Calls);
    }

    [Fact]
    public void Expanding_large_article_and_category_lists_shows_all_items()
    {
        var sample = FakeHotspotStore.SampleData();
        var manyArticles = Enumerable.Range(1, 30)
            .Select(i => new HotArticle(
                PageId: 10_000 + i,
                TitleFi: $"Artikkeli {i}",
                TitleEn: $"Article {i}",
                ContestScore: 200.0 - i,
                Reverts: 100 - i,
                RevertsDispute: 50,
                RevertsVandalism: 45,
                RevertsOther: 5,
                Episodes: 20,
                AnonShare: 0.5,
                FirstTs: new DateTime(2005, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastTs: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                YearlyReverts: [1, 2, 3, 4]))
            .ToList();
        var manyCategories = Enumerable.Range(1, 18)
            .Select(i => new HotCategory(
                Category: $"Luokka {i}",
                Articles: 10 + i,
                ContestScore: 100.0 - i,
                Reverts: 80,
                DisputeReverts: 40,
                Episodes: 12))
            .ToList();

        _store.Data = sample with { Articles = manyArticles, Categories = manyCategories };

        var cut = Render<HotspotsPage>();

        Assert.Equal(25, cut.FindAll("[data-testid=contested-article]").Count);
        Assert.Equal(15, cut.FindAll("[data-testid=contested-category]").Count);

        cut.Find("[data-testid=toggle-all-articles]").Click();
        Assert.Equal(30, cut.FindAll("[data-testid=contested-article]").Count);

        cut.Find("[data-testid=toggle-all-categories]").Click();
        Assert.Equal(18, cut.FindAll("[data-testid=contested-category]").Count);
    }
}
