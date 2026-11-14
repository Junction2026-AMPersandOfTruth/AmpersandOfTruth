using AmpersandOfTruth.Web.Features.Articles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Articles;

public sealed class ArticlePageTests : BunitContext
{
    private readonly FakeArticleCorpus _corpus = new();

    public ArticlePageTests()
    {
        Services.AddSingleton<IArticleCorpus>(_corpus);
        Services.AddSingleton<ArticleTimelineService>();
    }

    private void SeedSampleArticle()
    {
        _corpus.Articles[1256] = new ArticleAggregates(
            PageId: 1256,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 3104,
            FirstEdit: new DateTime(2003, 5, 12, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
            Months:
            [
                new MonthBucket(2003, 5, NamedEdits: 4, AnonEdits: 0, Removals: 0, MaxBytes: 4_000),
                new MonthBucket(2014, 11, NamedEdits: 22, AnonEdits: 8, Removals: 15, MaxBytes: 62_000),
                new MonthBucket(2015, 3, NamedEdits: 30, AnonEdits: 12, Removals: 22, MaxBytes: 64_000),
                new MonthBucket(2026, 9, NamedEdits: 5, AnonEdits: 1, Removals: 0, MaxBytes: 78_000),
            ]);

        _corpus.Revisions[1256] =
        [
            new ArticleRevision(
                RevId: 9002,
                ParentId: 9001,
                Ts: new DateTime(2015, 3, 7, 14, 2, 0, DateTimeKind.Utc),
                User: "Matti Meikäläinen",
                Anonymous: false,
                Comment: "Kumottu muokkaus, jonka teki [[Special:Contributions/85.76.123.45|85.76.123.45]]",
                Bytes: 64_000,
                DeltaBytes: -420,
                WasRemoved: false),
            new ArticleRevision(
                RevId: 9001,
                ParentId: 9000,
                Ts: new DateTime(2014, 11, 3, 9, 15, 0, DateTimeKind.Utc),
                User: "85.76.123.45",
                Anonymous: true,
                Comment: "Lisätty väite",
                Bytes: 64_420,
                DeltaBytes: 420,
                WasRemoved: true),
        ];
    }

    [Fact]
    public void Renders_header_counts_summary_and_revisions_with_masked_ip_and_diff_link()
    {
        SeedSampleArticle();

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Winter War", cut.Find("[data-testid=article-header]").TextContent);
            var wikiLink = cut.Find("[data-testid=wiki-article-link]");
            Assert.Contains("Talvisota", wikiLink.TextContent);
            Assert.Equal("https://fi.wikipedia.org/wiki/Talvisota", wikiLink.GetAttribute("href"));

            Assert.Equal("3,104 revisions 2003\u20132026", cut.Find("[data-testid=article-revision-count]").TextContent.Trim());
            Assert.Equal(
                "Most contested period: Nov 2014 \u2013 Mar 2015 (37 removals).",
                cut.Find("[data-testid=timeline-summary]").TextContent.Trim());
            Assert.Equal(
                "Revisions 1 Nov 2014 \u2013 31 Mar 2015",
                cut.Find("[data-testid=revisions-heading]").TextContent.Trim());

            var rows = cut.FindAll("[data-testid=revision-row]");
            Assert.Equal(2, rows.Count);

            // Raw IP must never appear in rendered markup
            Assert.DoesNotContain("85.76.123.45", cut.Markup);
            Assert.Contains("85.76.x.x (anonymous)", cut.Markup);

            // First row: named editor has link, comment masks IP and keeps lang="fi", no removed badge
            var editor0 = rows[0].QuerySelector("[data-testid=revision-editor] a");
            Assert.NotNull(editor0);
            Assert.Equal("https://fi.wikipedia.org/wiki/Special:Contributions/Matti_Meik%C3%A4l%C3%A4inen", editor0.GetAttribute("href"));
            var summary0 = rows[0].QuerySelector("[data-testid=revision-summary]");
            Assert.NotNull(summary0);
            Assert.Equal("fi", summary0.GetAttribute("lang"));
            Assert.Equal("Kumottu muokkaus, jonka teki 85.76.x.x", summary0.TextContent.Trim());
            Assert.Null(rows[0].QuerySelector("[data-testid=removed-badge]"));
            Assert.Equal(
                "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=9002",
                rows[0].QuerySelector("[data-testid=wiki-diff-link]")!.GetAttribute("href"));

            // Second row: anonymous IP editor has no link, +420 bytes, "removed" badge, diff link
            var editor1 = rows[1].QuerySelector("[data-testid=revision-editor]")!;
            Assert.Null(editor1.QuerySelector("a"));
            Assert.Equal("85.76.x.x (anonymous)", editor1.TextContent.Trim());
            Assert.Equal("+420", rows[1].QuerySelector("[data-testid=revision-delta]")!.TextContent.Trim());
            Assert.Equal("removed", rows[1].QuerySelector("[data-testid=removed-badge]")!.TextContent.Trim());
            Assert.Equal(
                "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=9001",
                rows[1].QuerySelector("[data-testid=wiki-diff-link]")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void Selecting_empty_month_updates_url_and_shows_no_edits_in_range()
    {
        SeedSampleArticle();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/articles/1256?incident=inc_7");

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=revision-row]").Count));

        // Click first bar (May 2003, which has no revisions in FakeArticleCorpus.Revisions)
        cut.FindAll("[data-testid=timeline-bar]")[0].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("/articles/1256?from=2003-05&to=2003-06&incident=inc_7", nav.Uri);
            Assert.Equal("No edits in this range.", cut.Find("[data-testid=empty-state]").TextContent.Trim());
        });
    }

    [Fact]
    public void Corpus_down_shows_error_panel_and_retry_reloads()
    {
        SeedSampleArticle();
        _corpus.Unavailable = true;

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            var err = cut.Find("[data-testid=article-error]");
            Assert.Contains("The Wikipedia data is not reachable right now.", err.TextContent);
            Assert.Contains("connection refused", err.TextContent);
        });

        _corpus.Unavailable = false;
        cut.Find("[data-testid=article-retry]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=article-error]"));
            Assert.Equal("3,104 revisions 2003\u20132026", cut.Find("[data-testid=article-revision-count]").TextContent.Trim());
            Assert.Equal(2, cut.FindAll("[data-testid=revision-row]").Count);
        });
    }

    [Fact]
    public void Unknown_page_shows_empty_state()
    {
        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 999_999));

        cut.WaitForAssertion(() =>
            Assert.Equal("We don't have this article.", cut.Find("[data-testid=empty-state]").TextContent.Trim()));
    }
}
