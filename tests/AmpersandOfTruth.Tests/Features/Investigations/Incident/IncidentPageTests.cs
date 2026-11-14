using AmpersandOfTruth.Tests.Features.Translation;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Translation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class IncidentPageTests : BunitContext
{
    private readonly FakeIncidentSource _source = new();
    private readonly FakeEvidenceTranslator _translator = new();
    private readonly InvestigationSourceOptions _sourceOptions = new() { Source = IncidentSourceKind.Fixtures };
    private readonly CapturingLoggerProvider _loggerProvider = new();

    public IncidentPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IIncidentSource>(_source);
        Services.AddSingleton<IEvidenceTranslator>(_translator);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(_ => Options.Create(_sourceOptions));
        Services.AddLogging(b => b.AddProvider(_loggerProvider));
    }

    [Fact]
    public void Renders_header_tier_badge_finnish_original_rank_facts_article_links_and_prev_next_hrefs()
    {
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02?sort=recent&tier=2");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        // Sample banner in fixture mode
        Assert.NotEmpty(cut.FindAll("[data-testid=sample-data-banner]"));

        // Breadcrumbs & rank "2 of 6"
        Assert.Equal("2 of 6", cut.Find("[data-testid=incident-rank]").TextContent);
        Assert.Equal("/investigate/winter-war?sort=recent&tier=2", cut.Find("[data-testid=breadcrumb-topic]").GetAttribute("href"));

        // Prev / next hrefs
        Assert.Equal("/investigate/winter-war/incidents/ww-01?sort=recent&tier=2", cut.Find("[data-testid=prev-incident]").GetAttribute("href"));
        Assert.Equal("/investigate/winter-war/incidents/ww-03?sort=recent&tier=2", cut.Find("[data-testid=next-incident]").GetAttribute("href"));

        // Header, TierBadge with WhyHref="#why", claim, FI original with lang="fi"
        var header = cut.Find("[data-testid=incident-header]");
        var tierBadge = header.QuerySelector("[data-testid=tier-badge]");
        Assert.NotNull(tierBadge);
        Assert.Contains(EvidenceTiers.Get(EvidenceTier.Campaign).Label, tierBadge.TextContent);
        Assert.Equal("#why", header.QuerySelector("[data-testid=tier-why]")?.GetAttribute("href"));

        var claim = cut.Find("[data-testid=incident-claim]");
        Assert.Contains("t-claim-lg", claim.ClassList);
        Assert.Equal("Finland started the Winter War by shelling the village of Mainila.", claim.TextContent);

        var original = cut.Find("[data-testid=claim-original]");
        Assert.Equal("fi", original.GetAttribute("lang"));
        Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", original.TextContent);

        // Key facts (.aot-facts grid) & article links with EN/FI titles
        var facts = cut.Find("dl.aot-facts[data-testid=incident-facts]");
        var dts = facts.QuerySelectorAll("dt").Select(static e => e.TextContent.Trim()).ToArray();
        var dds = facts.QuerySelectorAll("dd").Select(static e => e.TextContent.Trim()).ToArray();
        Assert.Equal(["First inserted", "Last removed", "Live in total", "Accounts", "Articles"], dts);
        Assert.Equal("3 Mar 2014", dds[0]);
        Assert.Equal("9 Jan 2016", dds[1]);
        Assert.Equal("61 days", dds[2]);
        Assert.Equal("3 (1 anonymous)", dds[3]);

        var articles = cut.Find("[data-testid=incident-articles]");
        Assert.NotNull(articles.QuerySelector("a[href='/articles/1256']"));
        Assert.NotNull(articles.QuerySelector("a[href='/articles/2048']"));
        Assert.Contains("Talvisota", articles.TextContent);
        Assert.Contains("Winter War", articles.TextContent);
        Assert.Contains("Mainilan laukaukset", articles.TextContent);
        Assert.Contains("Shelling of Mainila", articles.TextContent);

        // Only "Sequence of events" is rendered as a section <h2> (no duplicate "What changed" <h2> above diff)
        var h2s = cut.FindAll("h2");
        var seqHeading = Assert.Single(h2s);
        Assert.Equal("Sequence of events", seqHeading.TextContent);

        // Copy rules (including jargon) pass on rendered page text
        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=incident-page]").TextContent, includeJargon: true));
    }

    [Fact]
    public void Keyboard_ArrowRight_ArrowLeft_and_Escape_navigate_and_preserve_overview_query()
    {
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02?sort=recent&tier=2,3&view=split");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        var wrapper = cut.Find("[data-testid=incident-page]");
        Assert.Equal("-1", wrapper.GetAttribute("tabindex"));

        // ArrowRight → next incident
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.EndsWith("/investigate/winter-war/incidents/ww-03?sort=recent&tier=2,3&view=split", nav.Uri);

        // Reset URI back to ww-02 with query and test ArrowLeft → prev incident
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02?sort=recent&tier=2,3&view=split");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.EndsWith("/investigate/winter-war/incidents/ww-01?sort=recent&tier=2,3&view=split", nav.Uri);

        // Reset URI and test Escape → overview with overview query preserved (and diff view stripped)
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02?sort=recent&tier=2,3&view=split");
        wrapper.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.EndsWith("/investigate/winter-war?sort=recent&tier=2,3", nav.Uri);
    }

    [Fact]
    public void Deletion_and_null_claim_format_headlines_according_to_spec()
    {
        var deletion = FakeIncidentSource.BuildIncident(
            id: "del-1",
            rank: 1,
            kind: IncidentKind.Deletion,
            claimEn: "The shots were staged by the NKVD.");
        var nullClaim = FakeIncidentSource.BuildIncident(
            id: "null-1",
            rank: 2,
            kind: IncidentKind.Insertion,
            claimEn: null);

        _source.WithTopic("winter-war", [deletion, nullClaim]);

        var delCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "del-1"));
        Assert.Equal("Removed: The shots were staged by the NKVD.", delCut.Find("[data-testid=incident-claim]").TextContent);
        Assert.Null(delCut.Find("[data-testid=prev-incident]").GetAttribute("href"));
        Assert.True(delCut.Find("[data-testid=prev-incident]").HasAttribute("disabled"));
        Assert.NotNull(delCut.Find("[data-testid=next-incident]").GetAttribute("href"));

        var nullCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "null-1"));
        Assert.Equal("Text added to Winter War", nullCut.Find("[data-testid=incident-claim]").TextContent);
        Assert.NotNull(nullCut.Find("[data-testid=prev-incident]").GetAttribute("href"));
        Assert.Null(nullCut.Find("[data-testid=next-incident]").GetAttribute("href"));
        Assert.True(nullCut.Find("[data-testid=next-incident]").HasAttribute("disabled"));
    }

    [Fact]
    public void Null_source_and_unknown_incident_render_expected_empty_states_and_pipeline_mode_hides_sample_banner()
    {
        _sourceOptions.Source = IncidentSourceKind.Pipeline;

        // 1. Null source → "This investigation isn't loaded yet."
        var unloadedCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "continuation-war")
            .Add(x => x.IncidentId, "cw-01"));

        Assert.Empty(unloadedCut.FindAll("[data-testid=sample-data-banner]"));
        var unloadedEmpty = unloadedCut.Find("[data-testid=empty-state]");
        Assert.Contains("This investigation isn't loaded yet.", unloadedEmpty.TextContent);
        Assert.Equal("/investigate/continuation-war", unloadedCut.Find("[data-testid=back-to-overview]").GetAttribute("href"));

        // 2. Loaded topic, unknown incident id → "We can't find this incident."
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var missingCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "does-not-exist"));

        var missingEmpty = missingCut.Find("[data-testid=empty-state]");
        Assert.Contains("We can't find this incident.", missingEmpty.TextContent);
        Assert.Equal("/investigate/winter-war", missingCut.Find("[data-testid=back-to-overview]").GetAttribute("href"));
    }

    [Fact]
    public async Task Pending_source_renders_skeleton_header_and_diff_until_completed()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<IncidentView>?>();
        _source.Pending = tcs;

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        Assert.NotEmpty(cut.FindAll("[data-testid=incident-loading]"));
        Assert.Empty(cut.FindAll("[data-testid=incident-header]"));

        tcs.SetResult(FakeIncidentSource.BuildSix());

        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=incident-loading]"));
            Assert.NotEmpty(cut.FindAll("[data-testid=incident-header]"));
        });
    }

    [Fact]
    public void Null_assessment_shows_warn_banner_and_unavailable_assessment_note()
    {
        var noAssessment = FakeIncidentSource.BuildIncident(id: "ww-02", rank: 2, includeAssessment: false);
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix(noAssessment));

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        Assert.NotEmpty(cut.FindAll("[data-testid=assessment-unavailable-banner]"));
        var assessment = cut.Find("[data-testid=assessment]");
        Assert.Contains(
            "Assessment unavailable. The automated assessment could not be run for this incident. The removal history above still stands.",
            assessment.TextContent);
    }

    [Fact]
    public void Related_incidents_caps_at_three_and_actions_log_feedback_without_arrow_key_leak()
    {
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        // ww-01 (same article 1256), ww-03 (same editor Pohjantähti77), ww-04 (same article 2048), ww-05 (same article 1256) → capped at 3!
        var related = cut.Find("[data-testid=related-incidents]");
        var cards = related.QuerySelectorAll("[data-testid=incident-card]");
        Assert.Equal(3, cards.Length);
        Assert.Equal("ww-01", cards[0].GetAttribute("data-incident"));
        Assert.Equal("ww-03", cards[1].GetAttribute("data-incident"));
        Assert.Equal("ww-04", cards[2].GetAttribute("data-incident"));

        // Copy link button
        cut.Find("[data-testid=copy-link]").Click();
        Assert.Equal("Copied", cut.Find("[data-testid=copy-link]").TextContent.Trim());

        // "This looks wrong" opens inline textarea, stops ArrowRight propagation, logs and shows "Thanks — noted."
        cut.Find("[data-testid=looks-wrong]").Click();
        var input = cut.Find("[data-testid=looks-wrong-input]");
        Assert.EndsWith("/investigate/winter-war/incidents/ww-02", nav.Uri);

        input.Change("The removal was actually a merge.");
        cut.Find("[data-testid=looks-wrong-send]").Click();

        Assert.Equal("Thanks — noted.", cut.Find("[data-testid=looks-wrong-thanks]").TextContent);
        Assert.Contains(_loggerProvider.Messages, m => m.Contains("The removal was actually a merge.", StringComparison.Ordinal));
    }

    [Fact]
    public void Hostile_claim_summary_signals_and_assessment_render_encoded_with_no_script_elements()
    {
        const string payload = "<script>alert('xss')</script>";
        var hostile = FakeIncidentSource.BuildIncident(
            id: "ww-02",
            rank: 2,
            tierReason: $"Disputed and removed \u2014 because: {payload}",
            claimEn: payload,
            claimOriginal: payload,
            articles: [new ArticleRef(1256, payload, payload)],
            signals: [new SignalView(SignalCatalog.For("custom", payload), payload)],
            edits:
            [
                new EditView(
                    RevId: 1001,
                    ParentRevId: 1000,
                    PageId: 1256,
                    Timestamp: new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero),
                    Editor: EditorDisplay.For(payload),
                    Summary: payload,
                    Action: EditAction.Inserted,
                    Live: TimeSpan.FromDays(1)),
            ],
            assessment: new AssessmentView(
                ContentCategory.FalseFactualClaim,
                Confidence: 4,
                Reasoning: payload,
                Quotes: [payload],
                HasVerifiedQuote: true,
                Uncertainty: payload));

        _translator.Returns(payload, payload);
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix(hostile));

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid=mt-label]"));
        });

        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;", cut.Markup);
        Assert.Equal(payload, cut.Find("[data-testid=incident-claim]").TextContent);
        Assert.Equal(payload, cut.Find("[data-testid=claim-original]").TextContent);
    }

    [Fact]
    public void Key_facts_omits_anonymous_suffix_when_zero_anonymous_editors_and_renders_dash_when_not_removed()
    {
        var incident = new IncidentView(
            Id: "ww-01",
            Rank: 1,
            Tier: EvidenceTier.Contested,
            TierReason: "Disputed and removed \u2014 because: put back after removal.",
            ClaimEn: "Named-only claim.",
            ClaimOriginal: "Vain nimetty muokkaaja.",
            Lang: "fi",
            Kind: IncidentKind.Insertion,
            Articles: [new ArticleRef(1256, "Talvisota", "Winter War")],
            FirstSeen: new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero),
            LastRemoved: null,
            TotalLive: TimeSpan.FromDays(5),
            AccountCount: 2,
            InsertCount: 2,
            Signals: [],
            Edits:
            [
                new EditView(901, 900, 1256, new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero), EditorDisplay.For("NamedUser1"), "lisäys", EditAction.Inserted, TimeSpan.FromDays(5)),
            ],
            Passage: null,
            Assessment: null,
            Relevance: 0.9,
            ClusterId: null,
            Source: new AmpersandOfTruth.Web.Features.Investigations.IncidentRecord
            {
                Id = "ww-01",
                PageId = 1256,
                InsertingRevIds = [901],
                RemovingRevIds = [],
                Tier = EvidenceTier.Contested,
                Score = 90,
            });

        _source.WithTopic("winter-war", [incident]);

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-01"));

        var facts = cut.Find("dl.aot-facts[data-testid=incident-facts]");
        var dds = facts.QuerySelectorAll("dd").Select(static e => e.TextContent.Trim()).ToArray();
        Assert.Equal("3 Mar 2014", dds[0]);
        Assert.Equal("\u2014", dds[1]);
        Assert.Equal("5 days", dds[2]);
        Assert.Equal("2", dds[3]);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }
}
