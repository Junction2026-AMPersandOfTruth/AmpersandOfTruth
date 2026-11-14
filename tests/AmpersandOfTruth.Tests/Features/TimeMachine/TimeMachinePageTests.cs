using System.Net;
using AmpersandOfTruth.Tests.Features.Articles;
using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.TimeMachine;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.TimeMachine;

public sealed class TimeMachinePageTests : BunitContext
{
    private const long PageId = 1256;

    private readonly FakeTimeMachineCorpus _corpus = new();
    private readonly FakeIncidentSource _incidentSource = new();
    private readonly InvestigationSourceOptions _sourceOptions = new() { Source = IncidentSourceKind.Fixtures };

    public TimeMachinePageTests()
    {
        Services.AddSingleton<ITimeMachineCorpus>(_corpus);
        Services.AddSingleton<TimeMachineService>();
        Services.AddSingleton<IIncidentSource>(_incidentSource);
        Services.AddSingleton(Options.Create(_sourceOptions));
    }

    private void SeedSampleArticle()
    {
        _corpus.WithTitle(PageId, "Talvisota", "Winter War");

        _corpus.AddRevision(
            pageId: PageId,
            revId: 1000,
            parentId: null,
            tsUtc: new DateTime(2014, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            user: "Matti Meikäläinen",
            anonymous: false,
            wikitext: """
                '''Talvisota''' alkoi 30. marraskuuta 1939.

                == Tausta ==
                * Ensimmäinen kohta
                * Toinen kohta
                """);

        _corpus.AddRevision(
            pageId: PageId,
            revId: 1001,
            parentId: 1000,
            tsUtc: new DateTime(2014, 3, 3, 14, 2, 0, DateTimeKind.Utc),
            user: "85.76.123.45",
            anonymous: true,
            wikitext: """
                '''Talvisota''' alkoi 30. marraskuuta 1939. Suomi aloitti talvisodan ampumalla Mainilan kylään.

                == Tausta ==
                * Ensimmäinen kohta
                * Toinen kohta
                """);

        _corpus.AddRevision(
            pageId: PageId,
            revId: 1002,
            parentId: 1001,
            tsUtc: new DateTime(2015, 2, 10, 8, 45, 0, DateTimeKind.Utc),
            user: "Palauttaja",
            anonymous: false,
            wikitext: """
                '''Talvisota''' alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.

                == Tausta ==
                * Ensimmäinen kohta
                * Toinen kohta
                """);
    }

    [Fact]
    public void Prose_renders_encoded_script_as_text_without_script_tags()
    {
        _corpus.WithTitle(PageId, "Testisivu");
        _corpus.AddRevision(
            pageId: PageId,
            revId: 500,
            parentId: null,
            tsUtc: new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            user: "Muokkaaja",
            anonymous: false,
            wikitext: "Turvallinen alku &lt;script&gt;alert('xss')&lt;/script&gt; ja raaka <script>evil()</script> loppu.");

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            var prose = cut.Find("[data-testid=tm-prose]");
            Assert.Empty(prose.QuerySelectorAll("script"));
            Assert.Contains("<script>alert('xss')</script>", prose.TextContent);
            Assert.Contains("&lt;script&gt;alert(", prose.InnerHtml);
        });
    }

    [Fact]
    public void Meta_line_masks_ip_editor_and_renders_historical_wikipedia_link()
    {
        SeedSampleArticle();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/articles/{PageId}/at?date=2014-03-03");

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("85.76.123.45", cut.Markup);

            var meta = cut.Find("[data-testid=tm-revision-meta]");
            Assert.Contains("3 Mar 2014, 14:02 UTC", meta.TextContent);
            Assert.Contains("85.76.x.x", meta.TextContent);
            Assert.Contains("(anonymous)", meta.TextContent);

            var wikiLink = cut.Find("[data-testid=tm-wiki-link]");
            Assert.Equal("https://fi.wikipedia.org/w/index.php?oldid=1001", wikiLink.GetAttribute("href"));
        });
    }

    [Fact]
    public void Incident_jump_buttons_appear_and_clicking_jumps_to_insertion_and_highlights_passage()
    {
        SeedSampleArticle();
        var before = "'''Talvisota''' alkoi 30. marraskuuta 1939.";
        var after = "'''Talvisota''' alkoi 30. marraskuuta 1939. Suomi aloitti talvisodan ampumalla Mainilan kylään.";

        _incidentSource.WithTopic("winter-war",
        [
            FakeIncidentSource.BuildIncident(
                id: "ww-02",
                rank: 1,
                claimEn: "Finland started the Winter War by shelling Mainila.",
                articles: [new ArticleRef(PageId, "Talvisota", "Winter War")],
                firstSeen: new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero),
                passage: new DiffPassage(1001, 1000, before, after, WikitextDiff.Compare(before, after))),
            FakeIncidentSource.BuildIncident(
                id: "other-article",
                rank: 2,
                claimEn: "Unrelated Lapland War claim.",
                articles: [new ArticleRef(9999, "Lapin sota", "Lapland War")]),
        ]);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/articles/{PageId}/at?topic=winter-war");

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            // Initially on latest revision (2015-02-10, rev 1002)
            Assert.Equal("https://fi.wikipedia.org/w/index.php?oldid=1002", cut.Find("[data-testid=tm-wiki-link]").GetAttribute("href"));

            var jumps = cut.FindAll("[data-testid=tm-incident-jump]");
            Assert.Single(jumps);
            Assert.Contains("Finland started the Winter War", jumps[0].TextContent);
            Assert.NotNull(cut.Find("[data-testid=sample-data-banner]"));
        });

        cut.Find("[data-testid=tm-incident-jump]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("date=2014-03-03", nav.Uri);
            Assert.Contains("rev=1001", nav.Uri);
            Assert.Contains("highlight=ww-02", nav.Uri);

            Assert.Equal("https://fi.wikipedia.org/w/index.php?oldid=1001", cut.Find("[data-testid=tm-wiki-link]").GetAttribute("href"));

            var highlight = cut.Find("[data-testid=tm-prose] mark[data-testid=tm-highlight]");
            Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", highlight.TextContent);
        });
    }

    [Fact]
    public void Compare_date_renders_diff_with_added_and_removed_spans()
    {
        SeedSampleArticle();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/articles/{PageId}/at?date=2014-03-03");

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=tm-diff]")));

        cut.Find("[data-testid=tm-compare-input]").Change("2015-02-10");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("compare=2015-02-10", nav.Uri);
            var diff = cut.Find("[data-testid=tm-diff]");
            var delText = string.Concat(diff.QuerySelectorAll("del").Select(e => e.TextContent));
            var insText = string.Concat(diff.QuerySelectorAll("ins").Select(e => e.TextContent));

            Assert.Contains("Suomi aloitti talvisodan ampumalla Mainilan kylään", delText);
            Assert.Contains("kun Neuvostoliitto hyökkäsi Suomeen", insText);
        });
    }

    [Fact]
    public void Date_before_first_revision_and_slider_navigation_work()
    {
        SeedSampleArticle();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/articles/{PageId}/at?date=2010-01-01");

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            var before = cut.Find("[data-testid=tm-before-first]");
            Assert.Contains("This article did not exist yet on that date.", before.TextContent);
            Assert.Empty(cut.FindAll("[data-testid=tm-prose]"));
        });

        // Move slider to month offset 2 (Jan 2014 + 2 months = Mar 2014 -> rev 1001)
        cut.Find("[data-testid=tm-slider]").Change("2");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("date=2014-03-31", nav.Uri);
            Assert.Empty(cut.FindAll("[data-testid=tm-before-first]"));
            Assert.Equal("https://fi.wikipedia.org/w/index.php?oldid=1001", cut.Find("[data-testid=tm-wiki-link]").GetAttribute("href"));
        });
    }

    [Fact]
    public void Corpus_down_renders_unavailable_banner_and_retry_reloads()
    {
        SeedSampleArticle();
        _corpus.Unavailable = true;

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=tm-unavailable]");
            Assert.Contains("The Wikipedia data is not reachable right now.", banner.TextContent);
        });

        _corpus.Unavailable = false;
        cut.Find("[data-testid=status-retry]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=tm-unavailable]"));
            Assert.NotNull(cut.Find("[data-testid=tm-prose]"));
        });
    }

    [Fact]
    public void Article_page_header_links_to_time_machine_route()
    {
        var articleCorpus = new FakeArticleCorpus();
        articleCorpus.Articles[PageId] = new ArticleAggregates(
            PageId: PageId,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 10,
            FirstEdit: new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2015, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Months: [new MonthBucket(2014, 1, 5, 0, 0, 1000)]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IArticleCorpus>(articleCorpus);
        ctx.Services.AddSingleton<ArticleTimelineService>();
        ctx.Services.AddSingleton<IIncidentSource>(_incidentSource);
        ctx.Services.AddSingleton(Options.Create(_sourceOptions));

        var cut = ctx.Render<ArticlePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find("[data-testid=article-header]");
            var link = header.QuerySelectorAll("a")
                .Single(a => a.TextContent.Trim() == "Read as of a date");
            Assert.Equal($"/articles/{PageId}/at", link.GetAttribute("href"));
        });
    }

    [Fact]
    public void Highlight_not_found_in_prose_renders_fallback_box_above_prose()
    {
        SeedSampleArticle();
        var before = "'''Talvisota''' alkoi 30. marraskuuta 1939.";
        var after = "'''Talvisota''' alkoi 30. marraskuuta 1939. Suomi aloitti talvisodan ampumalla Mainilan kylään.";

        _incidentSource.WithTopic("winter-war",
        [
            FakeIncidentSource.BuildIncident(
                id: "ww-02",
                rank: 1,
                articles: [new ArticleRef(PageId, "Talvisota", "Winter War")],
                firstSeen: new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero),
                passage: new DiffPassage(1001, 1000, before, after, WikitextDiff.Compare(before, after))),
        ]);

        var nav = Services.GetRequiredService<NavigationManager>();
        // View rev 1002 (2015-02-10, after the claim was removed) while requesting highlight=ww-02
        nav.NavigateTo($"/articles/{PageId}/at?date=2015-02-10&rev=1002&highlight=ww-02&topic=winter-war");

        var cut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, PageId));

        cut.WaitForAssertion(() =>
        {
            var fallback = cut.Find("[data-testid=tm-highlight-fallback]");
            var mark = fallback.QuerySelector("mark[data-testid=tm-highlight]");
            Assert.NotNull(mark);
            Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", mark.TextContent);

            var fallbackIdx = cut.Markup.IndexOf("data-testid=\"tm-highlight-fallback\"", StringComparison.Ordinal);
            var proseIdx = cut.Markup.IndexOf("data-testid=\"tm-prose\"", StringComparison.Ordinal);
            Assert.True(fallbackIdx >= 0 && fallbackIdx < proseIdx);
        });
    }

    [Fact]
    public void Unknown_article_and_truncated_prose_render_expected_elements()
    {
        var unknownCut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, 999_999));
        unknownCut.WaitForAssertion(() =>
        {
            Assert.Contains("We don't have this article.", unknownCut.Find("[data-testid=empty-state]").TextContent);
        });

        var longWikitext = string.Join(' ', Enumerable.Repeat("Pitkä historiallinen kappale jatkuu.", 1_200));
        _corpus.AddRevision(
            pageId: 88,
            revId: 700,
            parentId: null,
            tsUtc: new DateTime(2021, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            user: "Kirjoittaja",
            anonymous: false,
            wikitext: longWikitext);

        var longCut = Render<TimeMachinePage>(p => p.Add(x => x.PageId, 88));
        longCut.WaitForAssertion(() =>
        {
            var prose = longCut.Find("[data-testid=tm-prose]");
            Assert.Contains("continue on Wikipedia", prose.TextContent);
        });
    }

    [Fact]
    public async Task Route_renders_unavailable_state_when_corpus_is_absent()
    {
        await using var app = new AppFactory();
        await using var unreachApp = app.WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Corpus", "Server=127.0.0.1;Port=1;Database=corpus;User ID=corpus_ro;Connection Timeout=1"));

        using var client = unreachApp.CreateClient();
        var response = await client.GetAsync("/articles/1/at");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"tm-unavailable\"", html);
    }
}
