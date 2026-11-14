using AmpersandOfTruth.Tests.Features.Translation;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Translation;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class IncidentSubcomponentsTests : BunitContext
{
    private readonly FakeEvidenceTranslator _translator = new();

    public IncidentSubcomponentsTests()
    {
        Services.AddSingleton<IEvidenceTranslator>(_translator);
    }

    [Fact]
    public void EditSequence_renders_glyphs_action_words_utc_time_masked_ip_diff_links_empty_summary_gap_and_batch_mt()
    {
        _translator
            .Returns("korjattu historiallinen fakta", "corrected historical fact")
            .Returns("lähteetön väite", "unsourced claim");

        var t0 = new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero);
        IReadOnlyList<EditView> edits =
        [
            new EditView(
                RevId: 1001,
                ParentRevId: 1000,
                PageId: 1256,
                Timestamp: t0,
                Editor: EditorDisplay.For("Pohjantähti77"),
                Summary: "korjattu historiallinen fakta",
                Action: EditAction.Inserted,
                Live: TimeSpan.FromDays(4)),
            new EditView(
                RevId: 1002,
                ParentRevId: 1001,
                PageId: 1256,
                Timestamp: new DateTimeOffset(2014, 3, 7, 9, 41, 0, TimeSpan.Zero),
                Editor: EditorDisplay.For("Kumoaja"),
                Summary: "lähteetön väite",
                Action: EditAction.Removed,
                Live: null),
            new EditView(
                RevId: 1003,
                ParentRevId: 1002,
                PageId: 1256,
                Timestamp: new DateTimeOffset(2015, 2, 2, 22, 15, 0, TimeSpan.Zero),
                Editor: EditorDisplay.For("85.76.123.45"),
                Summary: "",
                Action: EditAction.Reinserted,
                Live: TimeSpan.FromDays(41)),
        ];

        var cut = Render<EditSequence>(p => p
            .Add(x => x.Edits, edits)
            .Add(x => x.Scope, "winter-war"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, _translator.TranslateManyCalls);
            Assert.Equal(2, cut.FindAll("[data-testid=mt-label]").Count);
        });

        var rows = cut.FindAll("[data-testid=event-row]");
        Assert.Equal(3, rows.Count);

        // Row 0: Added (+), UTC time, named editor link, quoted EN summary on first line + FI original underneath, live 4 days, diff link
        Assert.Equal("+", rows[0].QuerySelector(".aot-seq__dot")?.TextContent);
        Assert.Contains("Added", rows[0].TextContent);
        Assert.Contains("3 Mar 2014, 14:02 UTC", rows[0].TextContent);
        var mt0 = rows[0].QuerySelector("[data-testid=mt-label]");
        Assert.NotNull(mt0);
        Assert.Contains("aot-seq__summary", mt0.ClassList);
        Assert.Equal("\u201Ccorrected historical fact\u201D", mt0.TextContent);
        var orig0 = rows[0].QuerySelector("[data-testid=mt-original]");
        Assert.NotNull(orig0);
        Assert.Contains("aot-original", orig0.ClassList);
        Assert.Equal("fi", orig0.GetAttribute("lang"));
        Assert.Equal("korjattu historiallinen fakta", orig0.TextContent);
        Assert.Contains("live 4 days", rows[0].TextContent);
        Assert.Equal(
            "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=1001",
            rows[0].QuerySelector("[data-testid=wiki-diff-link]")?.GetAttribute("href"));

        // Row 1: Removed (−), quoted EN summary + FI original underneath, no "live" duration
        Assert.Equal("\u2212", rows[1].QuerySelector(".aot-seq__dot")?.TextContent);
        Assert.Contains("Removed", rows[1].TextContent);
        Assert.Contains("7 Mar 2014, 09:41 UTC", rows[1].TextContent);
        Assert.Equal("\u201Cunsourced claim\u201D", rows[1].QuerySelector("[data-testid=mt-label]")?.TextContent);
        Assert.Equal("lähteetön väite", rows[1].QuerySelector("[data-testid=mt-original]")?.TextContent);
        Assert.DoesNotContain("live ", rows[1].QuerySelector(".aot-seq__live")?.TextContent ?? "");

        // Gap > 30 days between 7 Mar 2014 and 2 Feb 2015 → "… 11 months later"
        var gap = Assert.Single(cut.FindAll("[data-testid=event-gap]"));
        Assert.Equal("\u2026 11 months later", gap.TextContent);

        // Row 2: Put back (+), masked IP without link, muted "(no edit summary)", live 41 days
        Assert.Equal("+", rows[2].QuerySelector(".aot-seq__dot")?.TextContent);
        Assert.Contains("Put back", rows[2].TextContent);
        Assert.DoesNotContain("85.76.123.45", rows[2].InnerHtml);
        Assert.Contains("85.76.x.x", rows[2].TextContent);
        Assert.Contains("(anonymous)", rows[2].TextContent);
        Assert.Null(rows[2].QuerySelector("[data-testid=editor-label] a"));
        var emptySummary = rows[2].QuerySelector(".aot-seq__summary.aot-muted");
        Assert.NotNull(emptySummary);
        Assert.Equal("(no edit summary)", emptySummary.TextContent);
        Assert.Contains("live 41 days", rows[2].TextContent);
    }

    [Fact]
    public void WhyFlagged_renders_tier_badge_because_lead_spaced_signal_details_limitation_and_levels_link()
    {
        var incident = FakeIncidentSource.BuildIncident();

        var cut = Render<WhyFlagged>(p => p.Add(x => x.Incident, incident));

        var panelBadge = cut.Find("[data-testid=why-flagged] [data-testid=tier-badge]");
        Assert.Contains(EvidenceTiers.Get(incident.Tier).Label, panelBadge.TextContent);

        var lead = cut.Find("[data-testid=why-lead]");
        Assert.Equal("Because: put back after removal, removal reason given.", lead.TextContent);
        Assert.DoesNotContain(EvidenceTiers.Get(incident.Tier).Label, lead.TextContent, StringComparison.Ordinal);

        var details = cut.FindAll("[data-testid=why-signal]");
        Assert.Equal(3, details.Count);
        Assert.True(details[0].HasAttribute("open"));
        Assert.False(details[1].HasAttribute("open"));
        Assert.False(details[2].HasAttribute("open"));

        var detailBody0 = details[0].QuerySelector("div");
        Assert.NotNull(detailBody0);
        Assert.Equal(
            $"{incident.Signals[0].Phrase.Why} {incident.Signals[0].Evidence}",
            detailBody0.TextContent.Trim());

        Assert.Contains(Methodology.Limitations[0], cut.Markup);
        var howLink = cut.Find("a[href='/methodology#levels']");
        Assert.Equal("How levels work", howLink.TextContent);
        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=why-flagged]").TextContent, includeJargon: true));
    }

    [Theory]
    [InlineData(ContentCategory.ContentDispute, 1, "a content dispute", "low", 1)]
    [InlineData(ContentCategory.PovPush, 2, "one-sided wording", "low", 1)]
    [InlineData(ContentCategory.FalseFactualClaim, 3, "a disputed factual claim", "medium", 2)]
    [InlineData(ContentCategory.SourceManipulation, 4, "a change of sources", "high", 3)]
    [InlineData(ContentCategory.GoodFaithError, 5, "a good-faith error", "high", 3)]
    [InlineData(ContentCategory.Other, 3, "other", "medium", 2)]
    public void AssessmentNote_maps_category_quotes_and_confidence_meter_steps(
        ContentCategory category,
        int confidence,
        string expectedCategory,
        string expectedWord,
        int expectedOnSteps)
    {
        var view = new AssessmentView(
            Category: category,
            Confidence: confidence,
            Reasoning: "Changed how the event is described.",
            Quotes: ["First span", "Second span"],
            HasVerifiedQuote: true,
            Uncertainty: "Talk page not read.");

        var cut = Render<AssessmentNote>(p => p.Add(x => x.Assessment, view));

        var root = cut.Find("[data-testid=assessment]");
        Assert.Contains(Attribution.AutomatedAssessmentLabel, root.TextContent);
        Assert.Contains(expectedCategory, root.TextContent);
        Assert.Contains("Changed how the event is described.", root.TextContent);

        var quoteBox = Assert.Single(cut.FindAll("blockquote[data-testid=assessment-quotes]"));
        Assert.Contains("Finnish original", quoteBox.TextContent);
        var quotes = quoteBox.QuerySelectorAll("q[data-testid=assessment-quote]");
        Assert.Equal(2, quotes.Length);
        Assert.All(quotes, q => Assert.Equal("fi", q.GetAttribute("lang")));
        Assert.Equal("First span", quotes[0].TextContent);
        Assert.Equal("Second span", quotes[1].TextContent);

        var conf = cut.Find("[data-testid=assessment-confidence]");
        Assert.Contains(expectedWord, conf.TextContent);
        Assert.Equal(3, conf.QuerySelectorAll(".aot-meter i").Length);
        Assert.Equal(expectedOnSteps, conf.QuerySelectorAll(".aot-meter i.on").Length);
        Assert.Contains("Uncertainty: Talk page not read.", root.TextContent);
        Assert.Empty(CopyRules.FindViolations(root.TextContent, includeJargon: true));
    }

    [Fact]
    public void AssessmentNote_null_uncertainty_renders_plain_edit_uncertainty_without_jargon()
    {
        var view = new AssessmentView(
            Category: ContentCategory.FalseFactualClaim,
            Confidence: 3,
            Reasoning: "Changed how the event is described.",
            Quotes: [],
            HasVerifiedQuote: false,
            Uncertainty: null);

        var cut = Render<AssessmentNote>(p => p.Add(x => x.Assessment, view));

        var root = cut.Find("[data-testid=assessment]");
        Assert.Contains("Uncertainty: generated automatically from the edit above; it can be wrong.", root.TextContent);
        Assert.DoesNotContain("diff", root.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(CopyRules.FindViolations(root.TextContent, includeJargon: true));
    }

    [Fact]
    public void AssessmentNote_null_renders_exact_unavailable_sentence()
    {
        var cut = Render<AssessmentNote>(p => p.Add(x => x.Assessment, null));

        var root = cut.Find("[data-testid=assessment]");
        Assert.Contains(Attribution.AutomatedAssessmentLabel, root.TextContent);
        Assert.Contains(
            "Assessment unavailable. The automated assessment could not be run for this incident. The removal history above still stands.",
            root.TextContent);
        Assert.Empty(CopyRules.FindViolations(root.TextContent, includeJargon: true));
    }

    [Fact]
    public void EditSequence_normalizes_prequoted_translation_and_falls_back_to_fi_original_when_unavailable()
    {
        _translator.Returns("lainausmerkitty", "\"already quoted\"");

        IReadOnlyList<EditView> edits =
        [
            new EditView(1, 0, 1256, new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero), EditorDisplay.For("A"), "lainausmerkitty", EditAction.Inserted, TimeSpan.FromDays(1)),
            new EditView(2, 1, 1256, new DateTimeOffset(2014, 3, 4, 14, 2, 0, TimeSpan.Zero), EditorDisplay.For("B"), "kääntämätön", EditAction.Removed, null),
        ];

        _translator.FailAll = true;
        var failedCut = Render<EditSequence>(p => p.Add(x => x.Edits, edits));
        failedCut.WaitForAssertion(() => Assert.Equal(1, _translator.TranslateManyCalls));
        Assert.Empty(failedCut.FindAll("[data-testid=mt-label]"));
        var fallbackOrig = failedCut.FindAll("[data-testid=event-row]")[0].QuerySelector(".aot-original[lang='fi']");
        Assert.NotNull(fallbackOrig);
        Assert.Equal("lainausmerkitty", fallbackOrig.TextContent);
        Assert.Equal("Original Finnish edit summary", fallbackOrig.GetAttribute("title"));
        Assert.Equal("View change", failedCut.FindAll("[data-testid=wiki-diff-link]")[0].TextContent.Trim());

        _translator.FailAll = false;
        var okCut = Render<EditSequence>(p => p.Add(x => x.Edits, [edits[0]]));
        okCut.WaitForAssertion(() =>
        {
            Assert.Equal("\u201Calready quoted\u201D", okCut.Find("[data-testid=mt-label]").TextContent);
        });
    }

    [Fact]
    public void RelatedIncidents_lists_same_cluster_first()
    {
        var six = FakeIncidentSource.BuildSix();
        // ww-06 shares a cluster with ww-02 but nothing else; it must come before article/editor matches.
        var incidents = six.Select(v => v.Id is "ww-02" or "ww-06" ? v with { ClusterId = "c-7" } : v).ToList();
        var current = incidents.Single(v => v.Id == "ww-02");

        var cut = Render<RelatedIncidents>(p => p
            .Add(x => x.Current, current)
            .Add(x => x.Incidents, incidents)
            .Add(x => x.Topic, "winter-war"));

        var ids = cut.FindAll("[data-testid=incident-card]").Select(c => c.GetAttribute("data-incident")).ToList();
        Assert.NotEmpty(ids);
        Assert.Equal("ww-06", ids[0]);
        Assert.DoesNotContain("ww-02", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AssessmentNote_and_RelatedIncidents_strip_wiki_infobox_markup_and_decode_entities()
    {
        var view = new AssessmentView(
            Category: ContentCategory.FalseFactualClaim,
            Confidence: 4,
            Reasoning: "Unsupported book quote and figure change.",
            Quotes:
            [
                "Leninin Helsingissä kirjoittama kirja \"[[Valtio ja vallankumous]]\"",
                "| tappiot2 = 1&nbsp;000–1&nbsp;500 &amp; '''muut'''",
            ],
            HasVerifiedQuote: true,
            Uncertainty: null);

        var assessCut = Render<AssessmentNote>(p => p.Add(x => x.Assessment, view));
        var quotes = assessCut.FindAll("q[data-testid=assessment-quote]");
        Assert.Equal(2, quotes.Count);
        Assert.Equal("Leninin Helsingissä kirjoittama kirja \"Valtio ja vallankumous\"", quotes[0].TextContent);
        Assert.Equal("1 000–1 500 & muut", quotes[1].TextContent);

        var six = FakeIncidentSource.BuildSix();
        var current = six[0];
        var relatedWithMarkup = six[1] with
        {
            ClaimOriginal = "| kenttä = [[Valtio ja vallankumous|Valtio]] &amp; '''valta'''",
        };

        var relCut = Render<RelatedIncidents>(p => p
            .Add(x => x.Current, current)
            .Add(x => x.Incidents, [current, relatedWithMarkup])
            .Add(x => x.Topic, "civil-war-1918"));

        var cardOrig = relCut.Find("[data-testid=incident-card] .aot-original");
        Assert.Equal("Valtio & valta", cardOrig.TextContent);
    }

    [Fact]
    public void WhyFlagged_never_splices_Finnish_removal_summary_into_English_and_renders_MtLabel_with_fi_line()
    {
        _translator.Returns("Vapaussota on puolueellinen termi", "War of Independence is a biased term");

        var pipelineIncident = FakeIncidentSource.BuildIncident(
            id: "cw-pipe-1",
            rank: 1,
            signals:
            [
                new SignalView(SignalCatalog.For("removal_reason"), "'''Vapaussota''' on puolueellinen termi"),
                new SignalView(SignalCatalog.For("claim_content"), "Suomen vapaussotaa"),
            ]);

        var cut = Render<WhyFlagged>(p => p
            .Add(x => x.Incident, pipelineIncident)
            .Add(x => x.Scope, "civil-war-1918"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, _translator.TranslateCalls);
            Assert.NotEmpty(cut.FindAll("[data-testid=mt-label]"));
        });

        var signals = cut.FindAll("[data-testid=why-signal]");
        Assert.Equal(2, signals.Count);

        // Plain-language reason stands alone in English, never concatenated with raw Finnish
        var reasonLine = signals[0].QuerySelector("[data-testid=why-signal-reason]");
        Assert.NotNull(reasonLine);
        Assert.Equal(
            "The editor who removed it gave a reason about accuracy, neutrality or sources.",
            reasonLine.TextContent);
        Assert.DoesNotContain("Vapaussota", reasonLine.TextContent, StringComparison.OrdinalIgnoreCase);

        // Machine translation + FI original on its own line (lang="fi")
        var mt = signals[0].QuerySelector("[data-testid=mt-label]");
        Assert.NotNull(mt);
        Assert.Contains("\u201CWar of Independence is a biased term\u201D", mt.TextContent);
        Assert.NotNull(mt.QuerySelector(".aot-mt__tag"));

        var fiLine = signals[0].QuerySelector("[data-testid=mt-original]");
        Assert.NotNull(fiLine);
        Assert.Equal("fi", fiLine.GetAttribute("lang"));
        Assert.Equal("Vapaussota on puolueellinen termi", fiLine.TextContent);

        // Claim-content quote also sits on its own lang="fi" line rather than spliced into English
        var claimReasonLine = signals[1].QuerySelector("[data-testid=why-signal-reason]");
        Assert.NotNull(claimReasonLine);
        Assert.DoesNotContain("Suomen vapaussotaa", claimReasonLine.TextContent, StringComparison.Ordinal);
        var claimFiLine = signals[1].QuerySelector(".aot-original[lang='fi']");
        Assert.NotNull(claimFiLine);
        Assert.Equal("Suomen vapaussotaa", claimFiLine.TextContent);

        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=why-flagged]").TextContent, includeJargon: true));
    }

    [Fact]
    public void WhyFlagged_when_translator_fails_still_isolates_Finnish_summary_on_own_fi_line()
    {
        _translator.FailAll = true;

        var pipelineIncident = FakeIncidentSource.BuildIncident(
            id: "cw-pipe-2",
            rank: 1,
            signals:
            [
                new SignalView(SignalCatalog.For("removal_reason"), "Vapaussota on puolueellinen termi"),
            ]);

        var cut = Render<WhyFlagged>(p => p
            .Add(x => x.Incident, pipelineIncident)
            .Add(x => x.Scope, "civil-war-1918"));

        cut.WaitForAssertion(() => Assert.Equal(1, _translator.TranslateCalls));

        var reasonLine = cut.Find("[data-testid=why-signal-reason]");
        Assert.Equal(
            "The editor who removed it gave a reason about accuracy, neutrality or sources.",
            reasonLine.TextContent);

        var fiFallback = cut.Find("[data-testid=why-removal-original]");
        Assert.Equal("fi", fiFallback.GetAttribute("lang"));
        Assert.Equal("Vapaussota on puolueellinen termi", fiFallback.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=mt-label]"));
    }

    [Fact]
    public void RelatedIncidents_leads_with_English_title_when_known_and_renders_Finnish_under_it()
    {
        var current = FakeIncidentSource.BuildIncident(
            id: "cw-01",
            rank: 1,
            articles: [new ArticleRef(401, "Vilppulan rintama", "Battle of Vilppula")]);
        var siblingTitled = FakeIncidentSource.BuildIncident(
            id: "cw-02",
            rank: 2,
            articles: [new ArticleRef(401, "Vilppulan rintama", "Battle of Vilppula")]);
        var untitled = FakeIncidentSource.BuildIncident(
            id: "cw-03",
            rank: 3,
            articles: [new ArticleRef(402, "Suomen sisällissodan nimet", null)]);

        var cut = Render<RelatedIncidents>(p => p
            .Add(x => x.Current, current)
            .Add(x => x.Incidents, [current, siblingTitled, untitled])
            .Add(x => x.Topic, "civil-war-1918"));

        var cards = cut.FindAll("[data-testid=incident-card]");
        Assert.Equal(2, cards.Count);

        var enTitle = cards[0].QuerySelector("[data-testid=related-article-en]");
        var fiTitle = cards[0].QuerySelector("[data-testid=related-article-fi]");
        Assert.NotNull(enTitle);
        Assert.NotNull(fiTitle);
        Assert.Equal("Battle of Vilppula", enTitle.TextContent);
        Assert.Equal("fi", fiTitle.GetAttribute("lang"));
        Assert.Equal("Vilppulan rintama", fiTitle.TextContent);
        Assert.True(
            cards[0].InnerHtml.IndexOf("Battle of Vilppula", StringComparison.Ordinal)
            < cards[0].InnerHtml.IndexOf("Vilppulan rintama", StringComparison.Ordinal));

        // Untitled article falls back to Finnish title + (Finnish Wikipedia)
        var untitledHeader = cards[1].QuerySelector("[data-testid=related-article]");
        Assert.NotNull(untitledHeader);
        Assert.Contains("Suomen sisällissodan nimet", untitledHeader.TextContent);
        Assert.Contains("(Finnish Wikipedia)", untitledHeader.TextContent);
    }
}
