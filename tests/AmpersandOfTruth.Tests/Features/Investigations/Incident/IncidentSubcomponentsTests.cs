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

        // Row 0: Added (+), UTC time, named editor link, translated summary with MtLabel + FI original, live 4 days, diff link
        Assert.Equal("+", rows[0].QuerySelector(".aot-seq__dot")?.TextContent);
        Assert.Contains("Added", rows[0].TextContent);
        Assert.Contains("3 Mar 2014, 14:02 UTC", rows[0].TextContent);
        Assert.Contains("corrected historical fact", rows[0].TextContent);
        var orig0 = rows[0].QuerySelector("[data-testid=mt-original]");
        Assert.NotNull(orig0);
        Assert.Equal("fi", orig0.GetAttribute("lang"));
        Assert.Equal("korjattu historiallinen fakta", orig0.TextContent);
        Assert.Contains("live 4 days", rows[0].TextContent);
        Assert.Equal(
            "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=1001",
            rows[0].QuerySelector("[data-testid=wiki-diff-link]")?.GetAttribute("href"));

        // Row 1: Removed (−), no "live" duration
        Assert.Equal("\u2212", rows[1].QuerySelector(".aot-seq__dot")?.TextContent);
        Assert.Contains("Removed", rows[1].TextContent);
        Assert.Contains("7 Mar 2014, 09:41 UTC", rows[1].TextContent);
        Assert.Contains("unsourced claim", rows[1].TextContent);
        Assert.DoesNotContain("live ", rows[1].QuerySelector(".aot-seq__live")?.TextContent ?? "");

        // Gap > 30 days between 7 Mar 2014 and 2 Feb 2015 → "… 11 months later"
        var gap = Assert.Single(cut.FindAll("[data-testid=event-gap]"));
        Assert.Equal("\u2026 11 months later", gap.TextContent);

        // Row 2: Put back (+), masked IP without link, "(no edit summary)", live 41 days
        Assert.Equal("+", rows[2].QuerySelector(".aot-seq__dot")?.TextContent);
        Assert.Contains("Put back", rows[2].TextContent);
        Assert.DoesNotContain("85.76.123.45", rows[2].InnerHtml);
        Assert.Contains("85.76.x.x", rows[2].TextContent);
        Assert.Contains("(anonymous)", rows[2].TextContent);
        Assert.Null(rows[2].QuerySelector("[data-testid=editor-label] a"));
        Assert.Contains("(no edit summary)", rows[2].TextContent);
        Assert.Contains("live 41 days", rows[2].TextContent);
    }

    [Fact]
    public void WhyFlagged_renders_lead_details_with_first_open_limitation_and_levels_link()
    {
        var incident = FakeIncidentSource.BuildIncident();

        var cut = Render<WhyFlagged>(p => p.Add(x => x.Incident, incident));

        Assert.Equal(incident.TierReason, cut.Find("[data-testid=why-lead]").TextContent);

        var details = cut.FindAll("[data-testid=why-signal]");
        Assert.Equal(3, details.Count);
        Assert.True(details[0].HasAttribute("open"));
        Assert.False(details[1].HasAttribute("open"));
        Assert.False(details[2].HasAttribute("open"));

        Assert.Contains(incident.Signals[0].Phrase.Why, details[0].TextContent);
        Assert.Contains("Put back on 2 Feb 2015 after removal.", details[0].TextContent);

        Assert.Contains(Methodology.Limitations[0], cut.Markup);
        var howLink = cut.Find("a[href='/methodology#levels']");
        Assert.Equal("How levels work", howLink.TextContent);
    }

    [Theory]
    [InlineData(ContentCategory.ContentDispute, 1, "a content dispute", "low", 1)]
    [InlineData(ContentCategory.PovPush, 2, "one-sided wording", "low", 1)]
    [InlineData(ContentCategory.FalseFactualClaim, 3, "a false factual claim", "medium", 2)]
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

        var quotes = cut.FindAll("q[data-testid=assessment-quote]");
        Assert.Equal(2, quotes.Count);
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
}
