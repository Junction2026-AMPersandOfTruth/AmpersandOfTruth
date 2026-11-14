using System.Net;
using System.Text.Json;
using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Tests.Features.Translation;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Annotations;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using AmpersandOfTruth.Web.Features.Translation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Annotations;

public sealed class AnnotationUiTests : BunitContext
{
    private const char Lf = (char)10;

    private readonly TestDb _db = new();
    private readonly FakeIncidentSource _source = new();
    private readonly FakeEvidenceTranslator _translator = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero));
    private readonly LibraryStore _library;
    private readonly AnnotationStore _annotations;

    public AnnotationUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _db.AddTo(Services);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IIncidentSource>(_source);
        Services.AddSingleton<IEvidenceTranslator>(_translator);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));
        Services.AddLogging();
        Services.AddLibraryFeature();
        Services.AddAnnotationsFeature();

        Services.GetRequiredService<OwnerKey>().Seed("owner-1");
        _library = Services.GetRequiredService<LibraryStore>();
        _annotations = Services.GetRequiredService<AnnotationStore>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _db.Dispose();
        }
    }

    [Fact]
    public async Task IncidentPage_with_saved_renders_panel_saves_verdict_and_note_and_shows_chip_next_to_unchanged_TierBadge()
    {
        var savedId = await SaveSampleInvestigationAsync();
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"http://localhost/investigate/winter-war/incidents/ww-02?saved={savedId}");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        // Annotation panel rendered when ?saved= is set
        var panel = cut.Find("[data-testid=annotation-panel]");
        Assert.NotNull(panel);

        // TierBadge initially shown, no VerdictChip yet
        var header = cut.Find("[data-testid=incident-header]");
        var tierBadgeBefore = header.QuerySelector("[data-testid=tier-badge]")!;
        var expectedTierText = EvidenceTiers.Get(EvidenceTier.Campaign).Label;
        Assert.Contains(expectedTierText, tierBadgeBefore.TextContent);
        Assert.Null(header.QuerySelector("[data-testid=verdict-chip]"));

        // Select verdict + enter note + save
        cut.Find("[data-testid=verdict-select]").Change("Confirmed");
        cut.Find("[data-testid=annotation-note]").Change("Verified both edits in Finnish Wikipedia.");
        cut.Find("[data-testid=annotation-save]").Click();

        cut.WaitForAssertion(() =>
        {
            var updatedHeader = cut.Find("[data-testid=incident-header]");
            var tierBadgeAfter = updatedHeader.QuerySelector("[data-testid=tier-badge]");
            var chip = updatedHeader.QuerySelector("[data-testid=verdict-chip]");

            Assert.NotNull(tierBadgeAfter);
            Assert.Contains(expectedTierText, tierBadgeAfter!.TextContent);
            Assert.Equal("campaign", tierBadgeAfter.GetAttribute("data-tier"));

            Assert.NotNull(chip);
            Assert.Equal("Analyst: confirmed", chip!.TextContent.Trim());
        });

        var stored = await _annotations.ForInvestigationAsync(savedId);
        var savedAnn = Assert.Single(stored).Value;
        Assert.Equal(AnalystVerdict.Confirmed, savedAnn.Verdict);
        Assert.Equal("Verified both edits in Finnish Wikipedia.", savedAnn.Note);
        Assert.Equal("analyst", savedAnn.Source);

        // CopyRules check including jargon passes
        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=incident-page]").TextContent, includeJargon: true));

        // Clear removes annotation and hides VerdictChip
        cut.Find("[data-testid=annotation-clear]").Click();
        cut.WaitForAssertion(() =>
            Assert.Null(cut.Find("[data-testid=incident-header]").QuerySelector("[data-testid=verdict-chip]")));
        Assert.Empty(await _annotations.ForInvestigationAsync(savedId));
    }

    [Fact]
    public async Task SavedIncidentList_shows_VerdictChip_carries_saved_query_and_collapses_dismissed_incidents()
    {
        var savedId = await SaveSampleInvestigationAsync();
        await _annotations.UpsertAsync(
            savedId,
            "owner-1",
            "ww-01",
            AnalystVerdict.Plausible,
            dismiss: null,
            note: "Worth keeping",
            source: "analyst");
        await _annotations.UpsertAsync(
            savedId,
            "owner-1",
            "ww-02",
            AnalystVerdict.NotAnIncident,
            DismissReason.OrdinaryVandalism,
            note: "Simple vandalism",
            source: "analyst");

        var incidents = FakeIncidentSource.BuildSix().Take(3).ToList(); // ww-01, ww-02, ww-03

        var cut = Render<SavedIncidentList>(p => p
            .Add(x => x.SavedId, savedId)
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.Incidents, incidents)
            .Add(x => x.Query, "?sort=recent"));

        cut.WaitForAssertion(() =>
        {
            // Dismissed group collapsed (<details> without open attribute) with "Dismissed (1)"
            var dismissedDetails = cut.Find("details[data-testid=dismissed-group]");
            Assert.False(dismissedDetails.HasAttribute("open"));
            Assert.Contains("Dismissed (1)", dismissedDetails.QuerySelector("summary")!.TextContent);

            var dismissedCard = Assert.Single(dismissedDetails.QuerySelectorAll("[data-testid=incident-card]"));
            Assert.Equal("ww-02", dismissedCard.GetAttribute("data-incident"));
            var dismissedChip = Assert.Single(dismissedDetails.QuerySelectorAll("[data-testid=verdict-chip]"));
            Assert.Equal("Analyst: not an incident", dismissedChip.TextContent.Trim());

            // All 3 cards preserve their TierBadge and carry ?saved={id}
            var allCards = cut.FindAll("[data-testid=incident-card]");
            Assert.Equal(3, allCards.Count);
            foreach (var card in allCards)
            {
                var href = card.GetAttribute("href")!;
                Assert.Contains($"saved={savedId}", href);
                Assert.Contains("sort=recent", href);
                Assert.NotNull(card.QuerySelector("[data-testid=tier-badge]"));
            }

            // Active list has ww-01 (with "Analyst: plausible" chip) and ww-03, not ww-02
            var chips = cut.FindAll("[data-testid=verdict-chip]");
            Assert.Contains(chips, c => c.TextContent.Trim() == "Analyst: plausible");
        });
    }

    [Fact]
    public async Task Looks_wrong_with_saved_query_writes_note_only_annotation_and_without_saved_does_not()
    {
        var savedId = await SaveSampleInvestigationAsync();
        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();

        // 1. Without ?saved= -> no panel, "This looks wrong" does NOT write to AnnotationStore
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-01");
        var unsavedCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-01"));

        Assert.Empty(unsavedCut.FindAll("[data-testid=annotation-panel]"));

        unsavedCut.Find("[data-testid=looks-wrong]").Click();
        unsavedCut.Find("[data-testid=looks-wrong-input]").Change("Not saved investigation feedback.");
        unsavedCut.Find("[data-testid=looks-wrong-send]").Click();

        Assert.Equal("Thanks — noted.", unsavedCut.Find("[data-testid=looks-wrong-thanks]").TextContent);
        Assert.Empty(await _annotations.ForInvestigationAsync(savedId));

        // 2. With ?saved= -> writes annotation with Source = "looks_wrong" and Verdict = null (NOT NotAnIncident)
        nav.NavigateTo($"http://localhost/investigate/winter-war/incidents/ww-01?saved={savedId}");
        var savedCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-01"));

        Assert.NotEmpty(savedCut.FindAll("[data-testid=annotation-panel]"));

        savedCut.Find("[data-testid=looks-wrong]").Click();
        savedCut.Find("[data-testid=looks-wrong-input]").Change("The translation inverted the sentence.");
        savedCut.Find("[data-testid=looks-wrong-send]").Click();

        savedCut.WaitForAssertion(() =>
            Assert.Equal("Thanks — noted.", savedCut.Find("[data-testid=looks-wrong-thanks]").TextContent));

        var stored = await _annotations.ForInvestigationAsync(savedId);
        var ann = Assert.Single(stored).Value;
        Assert.Equal("ww-01", ann.IncidentId);
        Assert.Null(ann.Verdict);
        Assert.Null(ann.DismissReason);
        Assert.Equal("The translation inverted the sentence.", ann.Note);
        Assert.Equal("looks_wrong", ann.Source);
    }

    [Fact]
    public async Task Dev_annotations_review_page_filters_disagreements_exports_jsonl_and_returns_404_in_Production()
    {
        await using var app = new AppFactory();
        var lib = app.Services.GetRequiredService<LibraryStore>();
        var annStore = app.Services.GetRequiredService<AnnotationStore>();

        var snapshot = new SavedSnapshot(
            "Winter War",
            [new TopicArticle("fi", 1150, "Talvisota", 0.04)],
            [
                new IncidentRecord
                {
                    Id = "ww-high-fp",
                    PageId = 1150,
                    InsertingRevIds = [1001],
                    RemovingRevIds = [1002],
                    Tier = EvidenceTier.Campaign,
                    Score = 91.0,
                    ClaimEn = "High automated level dismissed by analyst.",
                },
                new IncidentRecord
                {
                    Id = "ww-low-tp",
                    PageId = 1150,
                    InsertingRevIds = [2001],
                    RemovingRevIds = [2002],
                    Tier = EvidenceTier.Reverted,
                    Score = 22.0,
                    ClaimEn = "Lowest automated level confirmed by analyst.",
                },
                new IncidentRecord
                {
                    Id = "ww-agree",
                    PageId = 1150,
                    InsertingRevIds = [3001],
                    RemovingRevIds = [3002],
                    Tier = EvidenceTier.Campaign,
                    Score = 94.0,
                    ClaimEn = "High automated level agreed by analyst.",
                },
            ],
            DateTimeOffset.UtcNow);

        var savedId = await lib.SaveAsync("owner-dev", "Dev Review Test", snapshot);
        await annStore.UpsertAsync(savedId, "owner-dev", "ww-high-fp", AnalystVerdict.NotAnIncident, DismissReason.LegitimateDispute, "Normal editorial dispute", "analyst");
        await annStore.UpsertAsync(savedId, "owner-dev", "ww-low-tp", AnalystVerdict.Confirmed, null, "Genuine disputed claim", "analyst");
        await annStore.UpsertAsync(savedId, "owner-dev", "ww-agree", AnalystVerdict.Confirmed, null, "Matches campaign level", "analyst");

        // Development (default in AppFactory): /dev/annotations shows only the 2 disagreements + export link
        using var devClient = app.CreateClient();
        var reviewResp = await devClient.GetAsync("/dev/annotations");
        Assert.Equal(HttpStatusCode.OK, reviewResp.StatusCode);
        var reviewHtml = await reviewResp.Content.ReadAsStringAsync();
        Assert.Contains("annotations-review", reviewHtml);
        Assert.Contains("annotations-export", reviewHtml);
        Assert.Contains("ww-high-fp", reviewHtml);
        Assert.Contains("ww-low-tp", reviewHtml);
        Assert.DoesNotContain("ww-agree", reviewHtml);

        // Development: GET /dev/annotations.jsonl exports all 3 labelled items
        var exportResp = await devClient.GetAsync("/dev/annotations.jsonl");
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        var jsonl = await exportResp.Content.ReadAsStringAsync();
        var lines = jsonl.Split(Lf, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("id", out _));
            Assert.True(doc.RootElement.TryGetProperty("label", out _));
        }

        // Production: both /dev/annotations and /dev/annotations.jsonl return 404
        await using var prodApp = app.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var prodClient = prodApp.CreateClient();
        var prodPageResp = await prodClient.GetAsync("/dev/annotations");
        Assert.Equal(HttpStatusCode.NotFound, prodPageResp.StatusCode);
        var prodExportResp = await prodClient.GetAsync("/dev/annotations.jsonl");
        Assert.Equal(HttpStatusCode.NotFound, prodExportResp.StatusCode);
    }

    private Task<Guid> SaveSampleInvestigationAsync()
    {
        var views = FakeIncidentSource.BuildSix();
        var records = views.Select(static v => v.Source with { Id = v.Id }).ToList();
        var snapshot = new SavedSnapshot(
            "Winter War",
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            records,
            _clock.GetUtcNow());
        return _library.SaveAsync("owner-1", "Saved Winter War", snapshot);
    }
}
