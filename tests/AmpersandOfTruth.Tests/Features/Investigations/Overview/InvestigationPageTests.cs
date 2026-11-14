using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Features.Articles;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class InvestigationPageTests : BunitContext
{
    private readonly ScriptedInvestigationRunner _runner = new();
    private readonly FakeIncidentCorpus _incidentCorpus = new();
    private readonly FakeRevisionStore _revisionStore = new();
    private readonly FakeArticleCorpus _articleCorpus = new();
    private readonly RecentRuns _recentRuns = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly InvestigationSourceOptions _sourceOptions = new() { Source = IncidentSourceKind.Fixtures };

    public InvestigationPageTests()
    {
        SeedCorpusAndRevisions();

        Services.AddSingleton<IInvestigationRunner>(_runner);
        Services.AddSingleton<IIncidentCorpus>(_incidentCorpus);
        Services.AddSingleton<IRevisionStore>(_revisionStore);
        Services.AddSingleton<IncidentMapper>();
        Services.AddSingleton(_recentRuns);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(Options.Create(_sourceOptions));
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IArticleCorpus>(_articleCorpus);
        Services.AddSingleton<ArticleTimelineService>();
    }

    [Fact]
    public async Task Prerender_before_OnAfterRender_does_not_start_run_and_shows_three_skeleton_cards()
    {
        var services = new ServiceCollection();
        var runner = new ScriptedInvestigationRunner();
        services.AddLogging();
        services.AddSingleton<IInvestigationRunner>(runner);
        services.AddSingleton<IIncidentCorpus>(_incidentCorpus);
        services.AddSingleton<IRevisionStore>(_revisionStore);
        services.AddSingleton<IncidentMapper>();
        services.AddSingleton(_recentRuns);
        services.AddSingleton<IOptions<InvestigationSourceOptions>>(Options.Create(_sourceOptions));
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<NavigationManager>(new TestPrerenderNav("http://localhost/investigate/winter-war"));

        await using var provider = services.BuildServiceProvider();
        await using var htmlRenderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        var html = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InvestigationPage.Topic)] = "winter-war",
            });
            var output = await htmlRenderer.RenderComponentAsync<InvestigationPage>(parameters);
            return output.ToHtmlString();
        });

        Assert.Equal(0, runner.RunCount);
        var doc = new HtmlParser().ParseDocument(html);
        Assert.Equal(3, doc.QuerySelectorAll("[data-testid=incident-skeleton]").Length);
    }

    [Fact]
    public async Task Streams_events_into_tracker_rail_ranked_cards_unflagged_list_recent_runs_and_collapsed_summary()
    {
        var session = _runner.EnqueueChannelRun();

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        // Started in OnAfterRenderAsync with slug converted to query text
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, _runner.RunCount);
            Assert.Equal("winter war", session.Topic);
            Assert.Equal(3, cut.FindAll("[data-testid=incident-skeleton]").Count);
            Assert.NotNull(cut.Find("[data-testid=cancel-run]"));
        });

        var fiMain = new TopicArticle("fi", 1150, "Talvisota", 0.04);
        var fiSub = new TopicArticle("fi", 1200, "Mainilan laukaukset", 0.08);
        var enOther = new TopicArticle("en", 9001, "Winter War", 0.03);

        await session.WriteAsync(new ArticlesFound(3, [fiMain, fiSub, enOther]));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("3 articles", cut.Find("[data-testid=stage-1]").TextContent);
            var railItems = cut.FindAll("[data-testid=article-rail-item]");
            Assert.Equal(3, railItems.Count);
            Assert.Contains("no edit history in our data", railItems[2].TextContent);
        });

        var candLow = MakeCandidate(fiSub, revId: 2002, revertedRevId: 2001, user: "EditorLow", comment: "Poistettu");
        var candHigh = MakeCandidate(fiMain, revId: 1002, revertedRevId: 1001, user: "EditorHigh", comment: "Kumottu");
        var candUnflagged = MakeCandidate(fiMain, revId: 3002, revertedRevId: 3001, user: "85.76.123.45", comment: "Vandalismia");

        await session.WriteAsync(new RevisionsScanned(9214, [candLow, candHigh, candUnflagged]));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("9,214 revisions · 3 removals", cut.Find("[data-testid=stage-2]").TextContent);
            Assert.Contains("9,214 revisions scanned", cut.Find("[data-testid=summary-header]").TextContent);
        });

        // Lower-score incident arrives first, higher-score incident arrives second -> cards stay ranked by Score
        var incLow = MakeRecord("inc-low", pageId: 1200, insertRev: 2001, removeRev: 2002, tier: EvidenceTier.Contested, score: 25.0, claimEn: "Lower score claim");
        var incHigh = MakeRecord("inc-high", pageId: 1150, insertRev: 1001, removeRev: 1002, tier: EvidenceTier.Campaign, score: 92.0, claimEn: "Higher score claim");

        await session.WriteAsync(new CandidateScored(1, 3, candLow, incLow));
        await session.WriteAsync(new CandidateScored(2, 3, candHigh, incHigh));

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll("[data-testid=incident-card]");
            Assert.Equal(2, cards.Count);
            Assert.Equal("inc-high", cards[0].GetAttribute("data-incident"));
            Assert.Equal("inc-low", cards[1].GetAttribute("data-incident"));
            // Partial state: cards clickable while running
            Assert.Equal("/investigate/winter-war/incidents/inc-high", cards[0].GetAttribute("href"));
            Assert.NotNull(cut.Find("[data-testid=cancel-run]"));
        });

        await session.WriteAsync(new CandidateScored(3, 3, candUnflagged, null, "graffiti"));
        await session.WriteAsync(new Completed(FromCache: true, [incHigh, incLow], TimeSpan.Zero)
        {
            AnalysedAt = new DateTimeOffset(2026, 11, 14, 22, 45, 0, TimeSpan.Zero),
        });
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            var done = cut.Find("[data-testid=stage-tracker-done]");
            Assert.DoesNotContain("Analysed in", done.TextContent);
            Assert.Contains("Loaded from previous analysis (analysed 14 Nov 2026, 22:45 UTC)", done.TextContent);
            Assert.NotNull(cut.Find("[data-testid=rerun-link]"));

            var header = cut.Find("[data-testid=summary-header]").TextContent;
            Assert.Contains("3 articles found", header);
            Assert.Contains("2 with edit history", header);
            Assert.Contains("Articles (2)", cut.Find("[data-testid=article-rail] > summary").TextContent);
            Assert.Contains("9,214 revisions scanned", header);
            Assert.Contains("2 incidents:", header);

            // Unflagged list excludes 1001 and 2001 (in incidents) and keeps 3001
            var unflagged = cut.Find("[data-testid=unflagged-toggle]");
            Assert.Contains("Removed edits we did not flag (1)", unflagged.QuerySelector("summary")!.TextContent);
            var unflaggedRow = Assert.Single(cut.FindAll("[data-testid=unflagged-row]"));
            Assert.DoesNotContain("85.76.123.45", cut.Markup);
            Assert.Contains("85.76.x.x", unflaggedRow.TextContent);
            Assert.Contains("(anonymous)", unflaggedRow.TextContent);

            // Lazy sparkline loaded for page 1150 (page 1200 absent -> no crash, just 1 sparkline)
            Assert.Single(cut.FindAll("[data-testid=article-timeline]"));
        });

        var recorded = _recentRuns.TryGet("winter-war");
        Assert.NotNull(recorded);
        Assert.Equal(["inc-high", "inc-low"], recorded.Select(r => r.Id));
    }

    [Fact]
    public async Task StageFailed_corpus_and_embedder_render_human_messages_and_retry_reruns_investigation()
    {
        var firstAttempt = _runner.EnqueueChannelRun();
        var secondAttempt = _runner.EnqueueChannelRun();

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal(1, _runner.RunCount));

        await firstAttempt.WriteAsync(new StageFailed(
            InvestigationStage.ScanHistory,
            StageFailureKind.CorpusUnavailable,
            "MySQL connection refused on 127.0.0.1:3307"));
        firstAttempt.Complete();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("The Wikipedia data is not reachable right now.", banner.TextContent);
            Assert.Contains("MySQL connection refused", banner.TextContent);
        });

        // Clicking Retry re-runs the investigation; test embedder message on second run
        cut.Find("[data-testid=status-retry]").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _runner.RunCount));

        await secondAttempt.WriteAsync(new StageFailed(
            InvestigationStage.FindArticles,
            StageFailureKind.EmbeddingUnavailable,
            "Ollama endpoint down"));
        secondAttempt.Complete();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("Topic search is not available right now.", banner.TextContent);
        });
    }

    [Fact]
    public void Cancel_button_and_Dispose_both_cancel_active_runner_token()
    {
        var run1 = _runner.EnqueueChannelRun();
        var run2 = _runner.EnqueueChannelRun();

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForElement("[data-testid=cancel-run]");

        Assert.False(run1.CancellationToken.IsCancellationRequested);
        cut.Find("[data-testid=cancel-run]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(run1.CancellationToken.IsCancellationRequested);
            Assert.Empty(cut.FindAll("[data-testid=cancel-run]"));
        });

        // Re-run, then dispose component -> run2 token is cancelled
        cut.Find("[data-testid=rerun-link]").Click();
        cut.WaitForElement("[data-testid=cancel-run]");
        Assert.False(run2.CancellationToken.IsCancellationRequested);

        cut.Instance.Dispose();
        Assert.True(run2.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Changing_Topic_parameter_cancels_previous_run_and_starts_new_run()
    {
        var run1 = _runner.EnqueueChannelRun();
        var run2 = _runner.EnqueueChannelRun();

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal("winter war", run1.Topic));

        cut.Render(p => p.Add(x => x.Topic, "continuation-war"));

        cut.WaitForAssertion(() =>
        {
            Assert.True(run1.CancellationToken.IsCancellationRequested);
            Assert.Equal(2, _runner.RunCount);
            Assert.Equal("continuation war", run2.Topic);
        });
    }

    [Fact]
    public async Task Sample_banner_is_shown_in_Fixtures_mode_and_hidden_in_Pipeline_mode()
    {
        var s1 = _runner.EnqueueChannelRun();
        var cutFixtures = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        Assert.NotNull(cutFixtures.Find("[data-testid=sample-data-banner]"));
        s1.Complete();

        await using var pipelineCtx = new BunitContext();
        pipelineCtx.Services.AddSingleton<IInvestigationRunner>(_runner);
        pipelineCtx.Services.AddSingleton<IIncidentCorpus>(_incidentCorpus);
        pipelineCtx.Services.AddSingleton<IRevisionStore>(_revisionStore);
        pipelineCtx.Services.AddSingleton<IncidentMapper>();
        pipelineCtx.Services.AddSingleton(_recentRuns);
        pipelineCtx.Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));
        pipelineCtx.Services.AddSingleton<TimeProvider>(_clock);

        var s2 = _runner.EnqueueChannelRun();
        var cutPipeline = pipelineCtx.Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        Assert.Empty(cutPipeline.FindAll("[data-testid=sample-data-banner]"));
        s2.Complete();
    }

    [Fact]
    public async Task Out_of_scope_verdict_shows_a_warning_and_the_articles_anyway()
    {
        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "bitcoin-price-history"));
        cut.WaitForAssertion(() => Assert.Equal(1, _runner.RunCount));
        Assert.Empty(cut.FindAll("[data-testid=scope-warning]"));

        var article = new TopicArticle("fi", 1150, "Talvisota", 0.4);
        await session.WriteAsync(new ArticlesFound(1, [article]) { Scope = new TopicScope(false, "Cryptocurrency prices are not about Finland.") });

        cut.WaitForAssertion(() =>
        {
            var warning = cut.Find("[data-testid=scope-warning]");
            Assert.Contains("may be outside the corpus", warning.TextContent);
            Assert.Contains("Cryptocurrency prices are not about Finland.", warning.TextContent);
            Assert.Contains("1 article", cut.Find("[data-testid=summary-header]").TextContent);
        });
        session.Complete();
    }

    [Fact]
    public async Task In_scope_verdict_shows_no_warning()
    {
        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal(1, _runner.RunCount));

        await session.WriteAsync(new ArticlesFound(1, [new TopicArticle("fi", 1150, "Talvisota", 0.1)]) { Scope = new TopicScope(true, "Finnish history.") });

        cut.WaitForAssertion(() => Assert.Contains("1 article", cut.Find("[data-testid=summary-header]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=scope-warning]"));
        session.Complete();
    }

    [Fact]
    public async Task Completed_with_zero_incidents_renders_empty_state_with_what_was_checked()
    {
        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "quiet-topic"));
        cut.WaitForAssertion(() => Assert.Equal(1, _runner.RunCount));

        var article = new TopicArticle("fi", 1150, "Talvisota", 0.1);
        var candidate = MakeCandidate(article, 3002, 3001, "Editor1", "Typo");

        await session.WriteAsync(new ArticlesFound(1, [article]));
        await session.WriteAsync(new RevisionsScanned(1420, [candidate]));
        await session.WriteAsync(new CandidateScored(1, 1, candidate, null, "not_substantive"));
        await session.WriteAsync(new Completed(FromCache: false, [], TimeSpan.FromSeconds(5)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-testid=empty-state]");
            Assert.Contains("No disputed insertions found for this topic", empty.TextContent);
            Assert.Contains("1 article checked", empty.TextContent);
            Assert.Contains("1,420 revisions scanned", empty.TextContent);
            Assert.Contains("1 removed edit inspected", empty.TextContent);
        });
    }

    [Fact]
    public async Task Url_filters_and_sorts_update_navigation_preserve_card_links_and_pass_CopyRules()
    {
        var session = _runner.EnqueueChannelRun();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/investigate/winter-war");

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal(1, _runner.RunCount));

        var fiMain = new TopicArticle("fi", 1150, "Talvisota", 0.04);
        var fiSub = new TopicArticle("fi", 1200, "Mainilan laukaukset", 0.08);
        var cand1 = MakeCandidate(fiMain, 1002, 1001, "EditorHigh", "Kumottu");
        var cand2 = MakeCandidate(fiSub, 2002, 2001, "EditorLow", "Poistettu");

        var inc1 = MakeRecord("inc-1", 1150, 1001, 1002, EvidenceTier.Campaign, score: 90, claimEn: "Mainila shelling claim", signals: ["multiple_editors"]);
        var inc2 = MakeRecord("inc-2", 1200, 2001, 2002, EvidenceTier.Contested, score: 40, claimEn: "Casualty number claim", signals: ["removal_reason"]);

        await session.WriteAsync(new ArticlesFound(2, [fiMain, fiSub]));
        await session.WriteAsync(new RevisionsScanned(500, [cand1, cand2]));
        await session.WriteAsync(new CandidateScored(1, 2, cand1, inc1));
        await session.WriteAsync(new CandidateScored(2, 2, cand2, inc2));
        await session.WriteAsync(new Completed(FromCache: false, [inc1, inc2], TimeSpan.FromSeconds(12)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=stage-tracker-done]"));
            Assert.Equal(2, cut.FindAll("[data-testid=incident-card]").Count);
            Assert.Single(cut.FindAll("[data-testid=article-timeline]"));
        });

        // Sort change updates URL and card href query without triggering a new run
        cut.Find("[data-testid=sort-select]").Change("recent");
        cut.WaitForAssertion(() => Assert.EndsWith("/investigate/winter-war?sort=recent", nav.Uri));
        Assert.Equal(1, _runner.RunCount);

        // Tier filter toggle + signal toggle + article rail count filter + year inputs
        cut.Find("[data-testid=tier-filter][data-tier=campaign]").Click();
        cut.WaitForAssertion(() => Assert.Contains("tier=3", nav.Uri));

        cut.Find("[data-testid=signal-filters] [data-testid=signal-chip][data-signal=multiple_editors]").Click();
        cut.WaitForAssertion(() => Assert.Contains("signal=multiple_editors", nav.Uri));

        cut.Find("[data-testid=article-rail-item][data-page-id='1150'] [data-testid=article-rail-count]").Click();
        cut.WaitForAssertion(() => Assert.Contains("article=1150", nav.Uri));

        cut.Find("[data-testid=from-year-input]").Change("2013");
        cut.WaitForAssertion(() => Assert.Contains("from=2013", nav.Uri));

        cut.Find("[data-testid=to-year-input]").Change("2016");
        cut.WaitForAssertion(() => Assert.Contains("to=2016", nav.Uri));

        cut.WaitForAssertion(() =>
        {
            var card = Assert.Single(cut.FindAll("[data-testid=incident-card]"));
            Assert.Equal("inc-1", card.GetAttribute("data-incident"));
            Assert.Contains("?sort=recent&tier=3&signal=multiple_editors&article=1150&from=2013&to=2016", card.GetAttribute("href"));
        });

        // Hover card highlights timeline mark
        cut.Find("[data-testid=incident-card]").MouseEnter();
        cut.WaitForAssertion(() =>
            Assert.Contains("is-hot", cut.Find("[data-testid=timeline-mark][data-incident=inc-1]").ClassList));

        // Rendered text and accessible labels pass CopyRules including jargon
        var doc = new HtmlParser().ParseDocument(cut.Markup);
        var renderedText = string.Join(
            " ",
            doc.Descendants<IText>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Assert.Empty(CopyRules.FindViolations(renderedText, includeJargon: true));

        foreach (var el in cut.FindAll("[title], [aria-label]"))
        {
            var title = el.GetAttribute("title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                Assert.Empty(CopyRules.FindViolations(title, includeJargon: true));
            }

            var aria = el.GetAttribute("aria-label");
            if (!string.IsNullOrWhiteSpace(aria))
            {
                Assert.Empty(CopyRules.FindViolations(aria, includeJargon: true));
            }
        }
    }

    private void SeedCorpusAndRevisions()
    {
        _incidentCorpus.Articles[1150] = new ArticleRef(1150, "Talvisota", "Winter War");
        _incidentCorpus.Articles[1200] = new ArticleRef(1200, "Mainilan laukaukset", "Shelling of Mainila");

        SeedRevisionTriple(1150, parentRev: 1000, insertRev: 1001, removeRev: 1002, year: 2014);
        SeedRevisionTriple(1200, parentRev: 2000, insertRev: 2001, removeRev: 2002, year: 2019);

        _articleCorpus.Articles[1150] = new ArticleAggregates(
            PageId: 1150,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 500,
            FirstEdit: new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Months: [new MonthBucket(2014, 3, NamedEdits: 15, AnonEdits: 4, Removals: 5, MaxBytes: 40_000)]);
    }

    private void SeedRevisionTriple(long pageId, long parentRev, long insertRev, long removeRev, int year)
    {
        var t0 = new DateTime(year, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        const string before = "Talvisota alkoi marraskuussa 1939.";
        const string after = "Talvisota alkoi marraskuussa 1939. Lisätty kiistanalainen väite.";

        _incidentCorpus.Revisions.AddRange([
            new RevisionRow(pageId, parentRev, parentRev - 1, t0, "BaseAuthor", "Pohja", before.Length),
            new RevisionRow(pageId, insertRev, parentRev, t0.AddDays(2), "Inserter", "Lisäys", after.Length),
            new RevisionRow(pageId, removeRev, insertRev, t0.AddDays(10), "Remover", "Kumottu", before.Length),
        ]);

        _revisionStore.Texts[(pageId, parentRev)] = before;
        _revisionStore.Texts[(pageId, insertRev)] = after;
        _revisionStore.Texts[(pageId, removeRev)] = before;
    }

    [Fact]
    public void State_overview_loading()
    {
        _runner.EnqueueChannelRun();

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=stage-tracker]"));
            Assert.NotNull(cut.Find("[data-testid=cancel-run]"));
            Assert.Equal(3, cut.FindAll("[data-testid=incident-skeleton]").Count);
        });
    }

    [Fact]
    public async Task State_overview_empty()
    {
        var session = _runner.EnqueueChannelRun();
        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        var candidate = MakeCandidate(article, revId: 902, revertedRevId: 901, user: "Editor", comment: "Kumottu");

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal("winter war", session.Topic));

        await session.WriteAsync(new ArticlesFound(2, [article]));
        await session.WriteAsync(new RevisionsScanned(1420, [candidate]));
        await session.WriteAsync(new CandidateScored(1, 1, candidate, null, Exclusion: "typo"));
        await session.WriteAsync(new Completed(false, [], TimeSpan.FromSeconds(2)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-testid=empty-state]");
            Assert.Contains("No disputed insertions found for this topic", empty.TextContent);
            Assert.Contains("2 articles", empty.TextContent);
            Assert.Contains("1,420 revisions", empty.TextContent);
            Assert.Contains("1 removed edit", empty.TextContent);
        });
    }

    [Fact]
    public async Task State_overview_corpus_down()
    {
        // 1. Without a cached run -> StatusBanner error + Retry
        var coldFail = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal("winter war", coldFail.Topic));

        await coldFail.WriteAsync(new StageFailed(
            InvestigationStage.ScanHistory,
            StageFailureKind.CorpusUnavailable,
            "Corpus 127.0.0.1:1 not available"));
        coldFail.Complete();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--error", banner.ClassList);
            Assert.Contains("The Wikipedia data is not reachable right now.", banner.TextContent);
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });

        // 2. With a cached run in RecentRuns -> StatusBanner info + cached incidents rendered
        SeedCorpusAndRevisions();
        var cachedRecord = MakeRecord("ww-01", 1150, 1001, 1002, EvidenceTier.Contested, 85, "Saved claim");
        _recentRuns.Record("winter-war", [cachedRecord]);

        var warmFail = _runner.EnqueueChannelRun();
        cut.Find("[data-testid=status-retry]").Click();
        cut.WaitForAssertion(() => Assert.Equal("winter war", warmFail.Topic));

        await warmFail.WriteAsync(new StageFailed(
            InvestigationStage.FindArticles,
            StageFailureKind.CorpusUnavailable,
            "Corpus 127.0.0.1:1 not available"));
        warmFail.Complete();

        cut.WaitForAssertion(() =>
        {
            var infoBanner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--info", infoBanner.ClassList);
            Assert.Contains("Showing saved analysis", infoBanner.TextContent);
            Assert.Single(cut.FindAll("[data-testid=incident-card]"));
        });
    }

    [Fact]
    public async Task State_overview_embedder_down()
    {
        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal("winter war", session.Topic));

        await session.WriteAsync(new StageFailed(
            InvestigationStage.FindArticles,
            StageFailureKind.EmbeddingUnavailable,
            "no Ollama with 'bge-m3'"));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--error", banner.ClassList);
            Assert.Contains("Topic search is not available right now.", banner.TextContent);
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });
    }

    [Fact]
    public async Task State_overview_llm_down()
    {
        SeedCorpusAndRevisions();
        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        var candidate = MakeCandidate(article, revId: 1002, revertedRevId: 1001, user: "Editor", comment: "Kumottu");
        var unassessed = MakeRecord("ww-llm", 1150, 1001, 1002, EvidenceTier.Reverted, 40, claimEn: null);

        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal("winter war", session.Topic));

        await session.WriteAsync(new ArticlesFound(1, [article]));
        await session.WriteAsync(new RevisionsScanned(500, [candidate]));
        await session.WriteAsync(new CandidateScored(1, 1, candidate, unassessed) { AssessmentUnavailable = true });
        await session.WriteAsync(new Completed(false, [unassessed], TimeSpan.FromSeconds(1)) { Degraded = 1 });
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--warn", banner.ClassList);
            Assert.Contains("Automated assessment unavailable — showing edit and removal history only.", banner.TextContent);

            var card = cut.Find("[data-testid=incident-card]");
            Assert.Equal("reverted", card.QuerySelector("[data-testid=tier-badge]")?.GetAttribute("data-tier"));
        });
    }

    [Fact]
    public async Task State_overview_slow()
    {
        var session = _runner.EnqueueChannelRun();
        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForAssertion(() => Assert.Equal("winter war", session.Topic));

        await session.WriteAsync(new ArticlesFound(1, [article]));
        cut.WaitForAssertion(() =>
            Assert.Equal("active", cut.Find("[data-testid=stage-2]").GetAttribute("data-state")));

        _clock.Advance(TimeSpan.FromSeconds(21));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Still working…", cut.Find("[data-testid=stage-2]").TextContent);
            Assert.NotNull(cut.Find("[data-testid=cancel-run]"));
        });
    }

    [Fact]
    public void State_overview_run_failed()
    {
        _runner.EnqueueFault(new InvalidOperationException("unexpected pipeline boom"));
        var retrySession = _runner.EnqueueChannelRun();

        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--error", banner.ClassList);
            Assert.Contains("Something went wrong in this step.", banner.TextContent);
            Assert.Empty(cut.FindAll("[data-testid=incident-skeleton]"));
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });

        cut.Find("[data-testid=status-retry]").Click();
        cut.WaitForAssertion(() => Assert.Equal("winter war", retrySession.Topic));
    }

    private static RemovedEdit MakeCandidate(TopicArticle article, long revId, long revertedRevId, string user, string comment) =>
        new(article, new RevertEvent(
            PageId: article.PageId,
            RevId: revId,
            Timestamp: new DateTime(2015, 3, 7, 12, 0, 0, DateTimeKind.Utc),
            User: "Reviewer",
            Comment: comment,
            RestoredRevId: revertedRevId - 1,
            RevertedCount: 1,
            RevertedRevId: revertedRevId,
            RevertedUser: user));

    private static IncidentRecord MakeRecord(
        string id,
        long pageId,
        long insertRev,
        long removeRev,
        EvidenceTier tier,
        double score,
        string? claimEn,
        IReadOnlyList<string>? signals = null) =>
        new()
        {
            Id = id,
            PageId = pageId,
            InsertingRevIds = [insertRev],
            RemovingRevIds = [removeRev],
            Tier = tier,
            Signals = (signals ?? []).Select(s => new IncidentSignal(s, null, null)).ToList(),
            Score = score,
            ClaimEn = claimEn,
            ClaimFi = "Lisätty kiistanalainen väite.",
        };

    [Fact]
    public async Task Contest_signals_load_once_per_run_and_render_rail_contest_and_unflagged_reasons()
    {
        var fakeSignals = new FakeContestSignals();
        fakeSignals.Contests[1256] = new PageContest(1256, EditWarEpisodes: 2, PingPongEpisodes: 3, BurstEpisodes: 1, ContestedScore: 15.0, PeakMonth: "2014-03");
        fakeSignals.Contests[8295] = new PageContest(8295, EditWarEpisodes: 0, PingPongEpisodes: 0, BurstEpisodes: 0, ContestedScore: 0.0, PeakMonth: null);
        fakeSignals.Classes[(1256, 5002)] = "graffiti_cleanup";
        fakeSignals.Classes[(8295, 6002)] = "other";
        Services.AddSingleton<IContestSignals>(fakeSignals);

        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        var fiTalvisota = new TopicArticle("fi", 1256, "Talvisota", 0.02);
        var fiMainila = new TopicArticle("fi", 8295, "Mainilan laukaukset", 0.05);
        var enArticle = new TopicArticle("en", 9999, "Winter War", 0.03);

        var candFlagged = new RemovedEdit(
            fiTalvisota,
            new RevertEvent(1256, 1002, new DateTime(2014, 3, 4, 12, 0, 0, DateTimeKind.Utc), "Reviewer", "Kumottu", 1000, 1, 1001, "Editor1"));
        var candGraffiti = new RemovedEdit(
            fiTalvisota,
            new RevertEvent(1256, 5002, new DateTime(2015, 5, 1, 12, 0, 0, DateTimeKind.Utc), "Reviewer", "Sotkua", 5000, 1, 5001, "85.76.123.45"));
        var candOther = new RemovedEdit(
            fiMainila,
            new RevertEvent(8295, 6002, new DateTime(2016, 6, 1, 12, 0, 0, DateTimeKind.Utc), "Reviewer", "Korjaus", 6000, 1, 6001, "Editor2"));

        await session.WriteAsync(new ArticlesFound(3, [fiTalvisota, fiMainila, enArticle]));
        await session.WriteAsync(new RevisionsScanned(420, [candFlagged, candGraffiti, candOther]));

        var incident = MakeRecord("inc-1", pageId: 1256, insertRev: 1001, removeRev: 1002, EvidenceTier.Contested, 85.0, "Disputed claim");
        await session.WriteAsync(new CandidateScored(1, 3, candFlagged, incident));
        await session.WriteAsync(new CandidateScored(2, 3, candGraffiti, null));
        await session.WriteAsync(new CandidateScored(3, 3, candOther, null));
        await session.WriteAsync(new Completed(FromCache: false, [incident], TimeSpan.FromSeconds(4)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, fakeSignals.PageContestsCalls);
            Assert.Equal([1256L, 8295L], fakeSignals.LastPageIds);
            Assert.Equal(1, fakeSignals.RevertClassesCalls);
            Assert.Equal(3, fakeSignals.LastRevertKeys.Count);

            var railItems = cut.FindAll("[data-testid=article-rail-item]");
            Assert.Equal(3, railItems.Count);
            Assert.Single(cut.FindAll("[data-testid=rail-contest]"));
            Assert.Equal(
                "Contested: 5 periods of back-and-forth edits \u00b7 peak Mar 2014",
                cut.Find("[data-testid=rail-contest]").TextContent.Trim());

            var unflaggedRows = cut.FindAll("[data-testid=unflagged-row]");
            Assert.Equal(2, unflaggedRows.Count);
            Assert.Equal("vandalism cleanup", unflaggedRows[0].QuerySelector("[data-testid=unflagged-reason]")?.TextContent);
            Assert.Null(unflaggedRows[1].QuerySelector("[data-testid=unflagged-reason]"));
        });
    }

    [Fact]
    public async Task Contest_signals_throwing_leaves_investigation_page_unchanged()
    {
        Services.AddSingleton<IContestSignals>(new FakeContestSignals
        {
            Failure = new InvalidOperationException("research schema unavailable"),
        });

        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        var fiTalvisota = new TopicArticle("fi", 1256, "Talvisota", 0.02);
        var unflaggedEdit = new RemovedEdit(
            fiTalvisota,
            new RevertEvent(1256, 5002, new DateTime(2015, 5, 1, 12, 0, 0, DateTimeKind.Utc), "Reviewer", "Kumottu", 5000, 1, 5001, "Editor2"));
        var incident = MakeRecord("inc-1", pageId: 1256, insertRev: 1001, removeRev: 1002, EvidenceTier.Contested, 85.0, "Disputed claim");

        await session.WriteAsync(new ArticlesFound(1, [fiTalvisota]));
        await session.WriteAsync(new RevisionsScanned(200, [unflaggedEdit]));
        await session.WriteAsync(new CandidateScored(1, 1, unflaggedEdit, incident));
        await session.WriteAsync(new Completed(FromCache: false, [incident], TimeSpan.FromSeconds(3)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[data-testid=incident-card]"));
            Assert.Single(cut.FindAll("[data-testid=article-rail-item]"));
            Assert.Single(cut.FindAll("[data-testid=unflagged-row]"));
            Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
            Assert.Empty(cut.FindAll("[data-testid=rail-contest]"));
            Assert.Empty(cut.FindAll("[data-testid=unflagged-reason]"));
        });
    }

    [Fact]
    public async Task Overview_collapses_phone_disclosures_folds_zero_incident_rail_entries_loads_sibling_titles_and_uses_signal_menu()
    {
        _incidentCorpus.Articles[1150] = new ArticleRef(1150, "Talvisota", "Winter War");
        _incidentCorpus.Articles[1200] = new ArticleRef(1200, "Mainilan laukaukset", "Shelling of Mainila");
        _incidentCorpus.Articles[1300] = new ArticleRef(1300, "Paikallinen artikkeli", null);

        var session = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        var fiMain = new TopicArticle("fi", 1150, "Talvisota", 0.02);
        var fiZeroWithEn = new TopicArticle("fi", 1200, "Mainilan laukaukset", 0.05);
        var fiZeroNoEn = new TopicArticle("fi", 1300, "Paikallinen artikkeli", 0.08);
        var inc = MakeRecord(
            "inc-1",
            pageId: 1150,
            insertRev: 1001,
            removeRev: 1002,
            EvidenceTier.Campaign,
            90.0,
            "Coordinated claim",
            signals: ["multiple_editors", "removed_as_unsourced"]);

        var candidate = MakeCandidate(fiMain, 1002, 1001, "Editor1", "Kumottu");
        await session.WriteAsync(new ArticlesFound(3, [fiZeroNoEn, fiZeroWithEn, fiMain]));
        await session.WriteAsync(new RevisionsScanned(300, [candidate]));
        await session.WriteAsync(new CandidateScored(1, 1, candidate, inc));
        await session.WriteAsync(new Completed(FromCache: false, [inc], TimeSpan.FromSeconds(2)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            // R1-05: rail and filter disclosures render without `open` so <992px phone starts collapsed
            var railDetails = cut.Find("[data-testid=article-rail]");
            Assert.False(railDetails.HasAttribute("open"));
            Assert.Contains("Articles (3)", railDetails.QuerySelector("summary")!.TextContent);

            var filterDetails = cut.Find("[data-testid=filters-disclosure]");
            Assert.False(filterDetails.HasAttribute("open"));
            Assert.Equal("Filters", cut.Find("[data-testid=filters-summary]").TextContent.Trim());

            // R1-08: 0-incident rail entries are tucked behind collapsed "Show 2 more articles",
            // and English sibling titles from IIncidentCorpus are shown with Finnish under them
            var moreDetails = cut.Find("[data-testid=rail-more-articles]");
            Assert.False(moreDetails.HasAttribute("open"));
            Assert.Equal("Show 2 more articles", cut.Find("[data-testid=rail-more-summary]").TextContent.Trim());

            var directRailItems = railDetails.Children.Where(static el => el.GetAttribute("data-testid") == "article-rail-item").ToList();
            var visibleItem = Assert.Single(directRailItems);
            Assert.Equal("1150", visibleItem.GetAttribute("data-page-id"));
            Assert.Equal("Winter War", visibleItem.QuerySelector(".aot-art__title")?.TextContent);

            var hiddenEnItem = moreDetails.QuerySelector("[data-testid=article-rail-item][data-page-id='1200']")!;
            Assert.Equal("Shelling of Mainila", hiddenEnItem.QuerySelector(".aot-art__title")?.TextContent);
            Assert.Contains("Mainilan laukaukset", hiddenEnItem.TextContent);

            var hiddenFiOnlyItem = moreDetails.QuerySelector("[data-testid=article-rail-item][data-page-id='1300']")!;
            Assert.Equal("Paikallinen artikkeli", hiddenFiOnlyItem.QuerySelector(".aot-art__title")?.TextContent);
            Assert.Contains("Finnish Wikipedia", hiddenFiOnlyItem.TextContent);

            // R1-12: summary header shows full EvidenceTiers labels; filter chips expose full labels in aria/title
            var summaryHeader = cut.Find("[data-testid=summary-header]");
            Assert.Contains(EvidenceTiers.Campaign.Label, summaryHeader.TextContent);
            Assert.Contains(EvidenceTiers.Contested.Label, summaryHeader.TextContent);
            Assert.Contains(EvidenceTiers.Reverted.Label, summaryHeader.TextContent);

            var campaignFilter = cut.Find("[data-testid=tier-filter][data-tier=campaign]");
            Assert.Equal($"Filter by level: {EvidenceTiers.Campaign.Label}", campaignFilter.GetAttribute("aria-label"));
            Assert.Contains(EvidenceTiers.Campaign.Label, campaignFilter.GetAttribute("title"));

            // R1-06: compact Signal ▾ menu is closed by default and no removable chips show until selected
            var signalMenu = cut.Find("[data-testid=signal-menu]");
            Assert.False(signalMenu.HasAttribute("open"));
            Assert.Equal("Signal", cut.Find("[data-testid=signal-menu-toggle]").TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid=active-filters]"));
        });

        // Select a signal -> Signal (1), Filters (1), and removable active-signal-filter chip
        cut.Find("[data-testid=signal-filters] [data-testid=signal-chip][data-signal=multiple_editors]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Signal (1)", cut.Find("[data-testid=signal-menu-toggle]").TextContent.Trim());
            Assert.Equal("Filters (1)", cut.Find("[data-testid=filters-summary]").TextContent.Trim());
            var removable = Assert.Single(cut.FindAll("[data-testid=active-signal-filter]"));
            Assert.Equal("multiple_editors", removable.GetAttribute("data-signal"));
            Assert.Contains("×", removable.TextContent);
        });

        // Clicking removable chip clears the signal filter
        cut.Find("[data-testid=active-signal-filter][data-signal=multiple_editors]").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Signal", cut.Find("[data-testid=signal-menu-toggle]").TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid=active-signal-filter]"));
        });
    }

    [Fact]
    public async Task Summary_header_article_count_matches_rail_or_explains_found_vs_edit_history()
    {
        var fiArticles = Enumerable.Range(1, 10)
            .Select(i => new TopicArticle("fi", 1000 + i, $"Artikkeli {i}", 0.05 * i))
            .ToList();

        // 1. 18 found across languages, 10 fi articles with edit history -> "18 articles found · 10 with edit history" and "Articles (10)"
        var mixedSession = _runner.EnqueueChannelRun();
        var cut = Render<InvestigationPage>(p => p.Add(x => x.Topic, "civil-war-1918"));
        cut.WaitForAssertion(() => Assert.Equal("civil war 1918", mixedSession.Topic));

        await mixedSession.WriteAsync(new ArticlesFound(18, fiArticles));
        await mixedSession.WriteAsync(new RevisionsScanned(4724, []));
        await mixedSession.WriteAsync(new Completed(FromCache: false, [], TimeSpan.FromSeconds(5)));
        mixedSession.Complete();

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find("[data-testid=summary-header]").TextContent;
            Assert.Contains("18 articles found", header);
            Assert.Contains("10 with edit history", header);
            Assert.Contains("Articles (10)", cut.Find("[data-testid=article-rail] > summary").TextContent);
        });

        // 2. Re-run where all 10 found articles have edit history -> "10 articles" and "Articles (10)"
        var equalSession = _runner.EnqueueChannelRun();
        cut.Find("[data-testid=rerun-link]").Click();
        cut.WaitForAssertion(() => Assert.Equal("civil war 1918", equalSession.Topic));

        await equalSession.WriteAsync(new ArticlesFound(10, fiArticles));
        await equalSession.WriteAsync(new RevisionsScanned(4724, []));
        await equalSession.WriteAsync(new Completed(FromCache: false, [], TimeSpan.FromSeconds(5)));
        equalSession.Complete();

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find("[data-testid=summary-header]").TextContent;
            Assert.Contains("10 articles", header);
            Assert.DoesNotContain("with edit history", header);
            Assert.Contains("Articles (10)", cut.Find("[data-testid=article-rail] > summary").TextContent);
        });
    }

    private sealed class FakeContestSignals : IContestSignals
    {
        public Dictionary<long, PageContest> Contests { get; } = [];
        public Dictionary<(long PageId, long RevertingRevId), string> Classes { get; } = [];
        public Exception? Failure { get; set; }
        public int PageContestsCalls { get; private set; }
        public int RevertClassesCalls { get; private set; }
        public IReadOnlyList<long> LastPageIds { get; private set; } = [];
        public IReadOnlyList<(long PageId, long RevertingRevId)> LastRevertKeys { get; private set; } = [];

        public Task<IReadOnlyDictionary<long, PageContest>> PageContestsAsync(
            IReadOnlyList<long> pageIds,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            PageContestsCalls++;
            LastPageIds = pageIds.ToList();
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyDictionary<long, PageContest>>(Failure);
            }

            var result = pageIds
                .Where(Contests.ContainsKey)
                .ToDictionary(id => id, id => Contests[id]);
            return Task.FromResult<IReadOnlyDictionary<long, PageContest>>(result);
        }

        public Task<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>> RevertClassesAsync(
            IReadOnlyList<(long PageId, long RevertingRevId)> reverts,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RevertClassesCalls++;
            LastRevertKeys = reverts.ToList();
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>>(Failure);
            }

            var result = reverts
                .Where(Classes.ContainsKey)
                .ToDictionary(k => k, k => Classes[k]);
            return Task.FromResult<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>>(result);
        }
    }

    private sealed class ScriptedInvestigationRunner : IInvestigationRunner
    {
        private readonly Queue<object> _sessions = new();

        public int RunCount { get; private set; }

        public ChannelRunSession EnqueueChannelRun()
        {
            var session = new ChannelRunSession();
            _sessions.Enqueue(session);
            return session;
        }

        public void EnqueueFault(Exception exception) => _sessions.Enqueue(exception);

        public async IAsyncEnumerable<InvestigationEvent> RunAsync(
            string topic, bool refresh,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            RunCount++;
            var next = _sessions.Count > 0 ? _sessions.Dequeue() : new ChannelRunSession(completeImmediately: true);
            if (next is Exception ex)
            {
                await Task.Yield();
                throw ex;
            }

            var session = (ChannelRunSession)next;
            session.Attach(topic, ct);

            await foreach (var evt in session.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }
    }

    private sealed class ChannelRunSession
    {
        private readonly Channel<InvestigationEvent> _channel = Channel.CreateUnbounded<InvestigationEvent>();

        public ChannelRunSession(bool completeImmediately = false)
        {
            if (completeImmediately)
            {
                _channel.Writer.TryComplete();
            }
        }

        public string? Topic { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public ChannelReader<InvestigationEvent> Reader => _channel.Reader;

        public void Attach(string topic, CancellationToken ct)
        {
            Topic = topic;
            CancellationToken = ct;
        }

        public ValueTask WriteAsync(InvestigationEvent evt) => _channel.Writer.WriteAsync(evt);

        public void Complete() => _channel.Writer.TryComplete();
    }

    private sealed class TestPrerenderNav : NavigationManager
    {
        public TestPrerenderNav(string uri)
        {
            Initialize("http://localhost/", uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
