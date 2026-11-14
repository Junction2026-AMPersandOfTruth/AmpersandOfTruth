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
        await session.WriteAsync(new Completed(FromCache: true, [incHigh, incLow], TimeSpan.FromSeconds(38)));
        session.Complete();

        cut.WaitForAssertion(() =>
        {
            var done = cut.Find("[data-testid=stage-tracker-done]");
            Assert.Contains("Analysed in 38 s", done.TextContent);
            Assert.Contains("loaded from previous analysis", done.TextContent);
            Assert.NotNull(cut.Find("[data-testid=rerun-link]"));

            var header = cut.Find("[data-testid=summary-header]").TextContent;
            Assert.Contains("3 articles", header);
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
