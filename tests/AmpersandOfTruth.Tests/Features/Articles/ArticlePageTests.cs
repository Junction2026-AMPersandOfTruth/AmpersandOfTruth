using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Articles;

public sealed class ArticlePageTests : BunitContext
{
    private readonly FakeArticleCorpus _corpus = new();
    private readonly TestIncidentSource _incidentSource = new();
    private readonly InvestigationSourceOptions _sourceOptions = new() { Source = IncidentSourceKind.Fixtures };

    public ArticlePageTests()
    {
        Services.AddSingleton<IArticleCorpus>(_corpus);
        Services.AddSingleton<ArticleTimelineService>();
        Services.AddSingleton<IIncidentSource>(_incidentSource);
        Services.AddSingleton(Options.Create(_sourceOptions));
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

            Assert.Empty(cut.FindAll("[data-testid=timeline-mark]"));
            Assert.Empty(cut.FindAll("[data-testid=sample-data-banner]"));
            Assert.Equal(0, _incidentSource.Calls);

            var rows = cut.FindAll("[data-testid=revision-row]");
            Assert.Equal(2, rows.Count);

            // Raw IP must never appear in rendered markup
            Assert.DoesNotContain("85.76.123.45", cut.Markup);

            // First row: shared EditorTag renders named editor link, comment masks IP and keeps lang="fi", no removed badge
            var editorTag0 = rows[0].QuerySelector("[data-testid=revision-editor] [data-testid=editor-label]");
            Assert.NotNull(editorTag0);
            var editor0 = editorTag0.QuerySelector("a");
            Assert.NotNull(editor0);
            Assert.Equal("Matti Meikäläinen", editor0.TextContent);
            Assert.Equal("https://fi.wikipedia.org/wiki/Special:Contributions/Matti_Meik%C3%A4l%C3%A4inen", editor0.GetAttribute("href"));
            var summary0 = rows[0].QuerySelector("[data-testid=revision-summary]");
            Assert.NotNull(summary0);
            Assert.Equal("fi", summary0.GetAttribute("lang"));
            Assert.Equal("Kumottu muokkaus, jonka teki 85.76.x.x", summary0.TextContent.Trim());
            Assert.Null(rows[0].QuerySelector("[data-testid=removed-badge]"));
            Assert.Equal(
                "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=9002",
                rows[0].QuerySelector("[data-testid=wiki-diff-link]")!.GetAttribute("href"));

            // Second row: shared EditorTag renders masked anonymous IP editor with no link, +420 bytes, "removed" badge, diff link
            var editorTag1 = rows[1].QuerySelector("[data-testid=revision-editor] [data-testid=editor-label]")!;
            Assert.Contains("aot-editor--anon", editorTag1.ClassList);
            Assert.Null(editorTag1.QuerySelector("a"));
            Assert.Contains("85.76.x.x", editorTag1.TextContent);
            Assert.Contains("(anonymous)", editorTag1.TextContent);
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
            var empty = cut.Find("[data-testid=empty-state]");
            Assert.Equal("No edits in this range.", empty.QuerySelector("h3")?.TextContent);
            Assert.NotEmpty(empty.QuerySelector("p")?.TextContent?.Trim() ?? "");
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
            Assert.Contains("aot-banner--error", err.ClassList);
            Assert.Equal("alert", err.GetAttribute("role"));
            Assert.Contains("The Wikipedia data is not reachable right now.", err.TextContent);
            Assert.Contains("connection refused", err.TextContent);
        });

        _corpus.Unavailable = false;
        cut.Find("[data-testid=status-retry]").Click();

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
        {
            var empty = cut.Find("[data-testid=empty-state]");
            Assert.Equal("We don't have this article.", empty.QuerySelector("h3")?.TextContent);
            Assert.NotEmpty(empty.QuerySelector("p")?.TextContent?.Trim() ?? "");
        });
    }

    [Fact]
    public void Topic_query_renders_marks_for_matching_article_only_highlights_incident_and_shows_sample_banner_in_fixtures_mode()
    {
        SeedSampleArticle();
        _sourceOptions.Source = IncidentSourceKind.Fixtures;
        _incidentSource.WithTopic("winter-war",
        [
            FakeIncidentSource.BuildIncident(
                id: "ww-01",
                rank: 1,
                tier: EvidenceTier.Contested,
                articles: [new ArticleRef(1256, "Talvisota", "Winter War")],
                firstSeen: new DateTimeOffset(2014, 11, 10, 8, 0, 0, TimeSpan.Zero)),
            FakeIncidentSource.BuildIncident(
                id: "ww-02",
                rank: 2,
                tier: EvidenceTier.Campaign,
                articles:
                [
                    new ArticleRef(2048, "Mainilan laukaukset", "Shelling of Mainila"),
                    new ArticleRef(1256, "Talvisota", "Winter War"),
                ],
                firstSeen: new DateTimeOffset(2015, 3, 5, 12, 30, 0, TimeSpan.Zero)),
            FakeIncidentSource.BuildIncident(
                id: "ww-other",
                rank: 3,
                tier: EvidenceTier.Reverted,
                articles: [new ArticleRef(9999, "Lapin sota", "Lapland War")],
                firstSeen: new DateTimeOffset(2015, 1, 15, 10, 0, 0, TimeSpan.Zero)),
        ]);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/articles/1256?topic=winter-war&incident=ww-02");

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            var marks = cut.FindAll("[data-testid=timeline-mark]");
            Assert.Equal(2, marks.Count);

            Assert.Equal("/investigate/winter-war/incidents/ww-01", marks[0].GetAttribute("href"));
            var shape0 = marks[0].QuerySelector("rect.aot-mk.aot-mk--contested");
            Assert.NotNull(shape0);
            Assert.DoesNotContain("is-hot", shape0.GetAttribute("class") ?? "");

            Assert.Equal("/investigate/winter-war/incidents/ww-02", marks[1].GetAttribute("href"));
            var shape1 = marks[1].QuerySelector("rect.aot-mk.aot-mk--campaign");
            Assert.NotNull(shape1);
            Assert.Contains("is-hot", shape1.GetAttribute("class") ?? "");

            // SampleDataBanner appears above ArticleTimeline in Fixtures mode when marks are present
            var banner = cut.Find("[data-testid=sample-data-banner]");
            Assert.Contains("Sample incidents: not findings", banner.TextContent);
            var bannerPos = cut.Markup.IndexOf("data-testid=\"sample-data-banner\"", StringComparison.Ordinal);
            var chartPos = cut.Markup.IndexOf("data-testid=\"article-timeline\"", StringComparison.Ordinal);
            Assert.True(bannerPos >= 0 && bannerPos < chartPos);
        });
    }

    [Fact]
    public void Sample_banner_is_hidden_in_pipeline_mode_or_when_no_incidents_match_article()
    {
        SeedSampleArticle();
        _sourceOptions.Source = IncidentSourceKind.Pipeline;
        _incidentSource.WithTopic("winter-war",
        [
            FakeIncidentSource.BuildIncident(
                id: "ww-01",
                rank: 1,
                tier: EvidenceTier.Reverted,
                articles: [new ArticleRef(1256, "Talvisota", "Winter War")],
                firstSeen: new DateTimeOffset(2014, 11, 10, 8, 0, 0, TimeSpan.Zero)),
        ]);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/articles/1256?topic=winter-war");

        var pipelineCut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        pipelineCut.WaitForAssertion(() =>
        {
            Assert.Single(pipelineCut.FindAll("[data-testid=timeline-mark]"));
            Assert.Empty(pipelineCut.FindAll("[data-testid=sample-data-banner]"));
        });

        // Switch back to Fixtures mode, but with incidents only on a different article
        _sourceOptions.Source = IncidentSourceKind.Fixtures;
        _incidentSource.WithTopic("lapland-war",
        [
            FakeIncidentSource.BuildIncident(
                id: "lw-01",
                rank: 1,
                articles: [new ArticleRef(9999, "Lapin sota", "Lapland War")]),
        ]);

        nav.NavigateTo("/articles/1256?topic=lapland-war");
        var emptyMarksCut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        emptyMarksCut.WaitForAssertion(() =>
        {
            Assert.Equal("3,104 revisions 2003\u20132026", emptyMarksCut.Find("[data-testid=article-revision-count]").TextContent.Trim());
            Assert.Empty(emptyMarksCut.FindAll("[data-testid=timeline-mark]"));
            Assert.Empty(emptyMarksCut.FindAll("[data-testid=sample-data-banner]"));
        });
    }

    [Fact]
    public void Incident_source_failure_renders_timeline_without_marks_or_error()
    {
        SeedSampleArticle();
        _incidentSource.Failure = new CorpusUnavailableException("incident source offline");

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/articles/1256?topic=winter-war");

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("3,104 revisions 2003\u20132026", cut.Find("[data-testid=article-revision-count]").TextContent.Trim());
            Assert.Equal(2, cut.FindAll("[data-testid=revision-row]").Count);
            Assert.Empty(cut.FindAll("[data-testid=article-error]"));
            Assert.Empty(cut.FindAll("[data-testid=timeline-mark]"));
            Assert.Empty(cut.FindAll("[data-testid=sample-data-banner]"));
        });
    }

    [Fact]
    public void State_article_loading()
    {
        var pending = new PendingArticleCorpus();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IArticleCorpus>(pending);
        ctx.Services.AddSingleton<ArticleTimelineService>();
        ctx.Services.AddSingleton<IIncidentSource>(_incidentSource);
        ctx.Services.AddSingleton(Options.Create(_sourceOptions));

        var cut = ctx.Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        var loading = cut.Find("[data-testid=article-loading]");
        Assert.Equal("status", loading.GetAttribute("role"));
        Assert.Equal(36, loading.QuerySelectorAll("rect").Length);
    }

    [Fact]
    public void State_article_empty()
    {
        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 404404));

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-testid=empty-state]");
            Assert.Contains("We don't have this article.", empty.TextContent);
        });
    }

    [Fact]
    public void State_article_corpus_down()
    {
        _corpus.Unavailable = true;

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            var error = cut.Find("[data-testid=article-error]");
            Assert.Contains("aot-banner--error", error.ClassList);
            Assert.Contains("The Wikipedia data is not reachable right now.", error.TextContent);
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });
    }

    [Fact]
    public void State_article_embedder_down()
    {
        SeedSampleArticle();
        Services.AddSingleton<AmpersandOfTruth.Web.Features.Embedding.IQueryEmbedder>(
            new AmpersandOfTruth.Tests.Features.Embedding.FakeQueryEmbedder { Unavailable = true });

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=article-header]"));
            Assert.Empty(cut.FindAll("[data-testid=article-error]"));
        });
    }

    [Fact]
    public void State_article_llm_down()
    {
        SeedSampleArticle();
        Services.AddSingleton<AmpersandOfTruth.Web.Features.Analysis.IAnalysisModel>(
            new AmpersandOfTruth.Tests.Infrastructure.FakeAnalysisModel());

        var cut = Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=article-header]"));
            Assert.Empty(cut.FindAll("[data-testid=article-error]"));
        });
    }

    [Fact]
    public void State_article_slow()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var pending = new PendingArticleCorpus();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<TimeProvider>(clock);
        ctx.Services.AddSingleton<IArticleCorpus>(pending);
        ctx.Services.AddSingleton<ArticleTimelineService>();
        ctx.Services.AddSingleton<IIncidentSource>(_incidentSource);
        ctx.Services.AddSingleton(Options.Create(_sourceOptions));

        var cut = ctx.Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));
        Assert.DoesNotContain("Still loading", cut.Find("[data-testid=article-loading]").TextContent);

        clock.Advance(TimeSpan.FromSeconds(21));

        cut.WaitForAssertion(() =>
            Assert.Contains("Still loading article history…", cut.Find("[data-testid=article-loading]").TextContent));
    }

    [Fact]
    public void State_article_run_failed()
    {
        var throwing = new PendingArticleCorpus { Fault = new InvalidOperationException("unexpected query error") };
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IArticleCorpus>(throwing);
        ctx.Services.AddSingleton<ArticleTimelineService>();
        ctx.Services.AddSingleton<IIncidentSource>(_incidentSource);
        ctx.Services.AddSingleton(Options.Create(_sourceOptions));

        var cut = ctx.Render<ArticlePage>(p => p.Add(x => x.PageId, 1256));

        cut.WaitForAssertion(() =>
        {
            var error = cut.Find("[data-testid=article-error]");
            Assert.Contains("aot-banner--error", error.ClassList);
            Assert.Contains("Something went wrong loading this article.", error.TextContent);
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });
    }

    private sealed class PendingArticleCorpus : IArticleCorpus
    {
        private readonly TaskCompletionSource<ArticleAggregates?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Fault { get; init; }

        public Task<ArticleAggregates?> AggregatesAsync(long pageId, CancellationToken ct = default) =>
            Fault is not null
                ? Task.FromException<ArticleAggregates?>(Fault)
                : _tcs.Task.WaitAsync(ct);

        public Task<IReadOnlyList<ArticleRevision>> RevisionsAsync(
            long pageId, DateRange range, int limit = 500, CancellationToken ct = default) =>
            Fault is not null
                ? Task.FromException<IReadOnlyList<ArticleRevision>>(Fault)
                : Task.FromResult<IReadOnlyList<ArticleRevision>>([]);
    }

    private sealed class TestIncidentSource : IIncidentSource
    {
        private readonly Dictionary<string, IReadOnlyList<IncidentView>?> _byTopic =
            new(StringComparer.OrdinalIgnoreCase);

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public void WithTopic(string slug, IReadOnlyList<IncidentView>? incidents) =>
            _byTopic[slug] = incidents;

        public Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;

            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<IncidentView>?>(Failure);
            }

            return Task.FromResult(_byTopic.TryGetValue(topicSlug, out var list) ? list : null);
        }
    }
}
