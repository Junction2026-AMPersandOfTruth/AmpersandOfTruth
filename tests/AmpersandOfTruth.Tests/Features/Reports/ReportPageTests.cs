using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using AmpersandOfTruth.Web.Features.Reports;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Reports;

public sealed class ReportPageTests : BunitContext
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero));
    private readonly FakeIncidentCorpus _incidentCorpus = new();
    private readonly FakeRevisionStore _revisionStore = new();
    private readonly FakeAnalysisModel _llm = new();
    private readonly LibraryStore _store;
    private readonly SampleTopic _winterWar;

    public ReportPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var samples = new SampleIncidents(NullLogger<SampleIncidents>.Instance);
        _winterWar = samples.Match("winter-war")
            ?? throw new InvalidOperationException("Expected winter-war sample fixture.");

        _db.AddTo(Services);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IIncidentCorpus>(_incidentCorpus);
        Services.AddSingleton<IRevisionStore>(_revisionStore);
        Services.AddSingleton<IncidentMapper>();
        Services.AddSingleton<IAnalysisModel>(_llm);
        Services.AddLibraryFeature();
        Services.AddReportsFeature();

        _store = Services.GetRequiredService<LibraryStore>();
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
    public void Unknown_id_renders_not_found_state()
    {
        var cut = Render<ReportPage>(p => p.Add(x => x.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            var notFound = cut.Find("[data-testid=report-not-found]");
            Assert.Contains("Saved investigation not found", notFound.TextContent);
            Assert.Empty(cut.FindAll("[data-testid=report-page]"));
        });
    }

    [Fact]
    public async Task Renders_summary_incidents_del_ins_appendix_and_supports_print_and_pinned_toggle()
    {
        var first = _winterWar.Incidents[0];
        var firstEdit = first.Edits[0];
        var parentRev = firstEdit.ParentRevId ?? 8954412L;

        _revisionStore.Texts[(first.PageId, parentRev)] = "Lopputulos: Moskovan rauha.";
        _revisionStore.Texts[(first.PageId, firstEdit.RevId)] =
            "Lopputulos: Suomen torjuntavoitto <script>alert(1)</script>.";

        _llm.Returns(
            SummarizeInvestigationPrompt.Id,
            """{"summary":"Contested additions to Winter War articles were removed by Wikipedia editors."}""");

        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            _winterWar.Incidents.Take(2).ToList(),
            new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));

        var savedId = await _store.SaveAsync("owner-ui", "Winter War Desk Report", snapshot);
        await _store.PinAsync(savedId, "owner-ui", first.Id!, "Check primary treaty wording.");

        var cut = Render<ReportPage>(p => p.Add(x => x.Id, savedId));

        cut.WaitForAssertion(() =>
        {
            var page = cut.Find("[data-testid=report-page]");
            Assert.Contains("Winter War Desk Report", page.TextContent);

            var summary = cut.Find("[data-testid=report-summary]");
            Assert.Contains("Automated summary", summary.TextContent);
            Assert.Contains("Contested additions to Winter War articles were removed by Wikipedia editors.", summary.TextContent);

            var incidents = cut.FindAll("[data-testid=report-incident]");
            Assert.Equal(2, incidents.Count);
            Assert.Contains("Check primary treaty wording.", incidents[0].TextContent);

            // Passage uses semantic <del> and <ins>, and encodes raw <script> tags instead of injecting DOM scripts
            Assert.NotEmpty(incidents[0].QuerySelectorAll("del"));
            Assert.NotEmpty(incidents[0].QuerySelectorAll("ins"));
            Assert.Empty(incidents[0].QuerySelectorAll("script"));
            Assert.Contains("<script>alert(1)</script>", incidents[0].TextContent);

            // Full revision URLs written out
            var revUrl = WikipediaLinks.Revision(firstEdit.RevId).AbsoluteUri;
            Assert.Contains(revUrl, incidents[0].TextContent);

            var appendix = cut.Find("[data-testid=report-appendix]");
            Assert.Contains(AmpersandOfTruth.Web.Features.Evidence.Methodology.Summary, appendix.TextContent);
            Assert.Contains(Attribution.License, appendix.TextContent);
            Assert.Contains(revUrl, appendix.TextContent);

            var downloadLink = cut.Find("[data-testid=report-download-md]");
            Assert.Equal($"/library/{savedId}/report.md", downloadLink.GetAttribute("href"));
        });

        // Print button calls window.print() via IJSRuntime.InvokeVoidAsync("print")
        cut.Find("[data-testid=report-print]").Click();
        Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "print");

        // Toggle pinned-only filter
        cut.Find("[data-testid=report-pinned-toggle]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[data-testid=report-incident]"));
            var downloadLink = cut.Find("[data-testid=report-download-md]");
            Assert.Equal($"/library/{savedId}/report.md?pinned=1", downloadLink.GetAttribute("href"));
            var nav = Services.GetRequiredService<NavigationManager>();
            Assert.EndsWith($"/library/{savedId}/report?pinned=1", nav.Uri, StringComparison.Ordinal);
        });

        // Rendered page copy passes CopyRules
        var doc = new HtmlParser().ParseDocument(cut.Markup);
        var visibleText = string.Join(" ", doc.Descendants<IText>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Assert.Empty(CopyRules.FindViolations(visibleText, includeJargon: false));
    }

    [Fact]
    public async Task Library_page_and_saved_banner_link_to_report_route()
    {
        var owner = Services.GetRequiredService<OwnerKey>();
        owner.Seed("owner-links");

        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            _winterWar.Incidents.Take(1).ToList(),
            _clock.GetUtcNow());

        var savedId = await _store.SaveAsync("owner-links", "Link Check", snapshot);

        var libCut = Render<LibraryPage>();
        libCut.WaitForAssertion(() =>
        {
            var reportLink = libCut.Find("[data-testid=library-report]");
            Assert.Equal("Report", reportLink.TextContent.Trim());
            Assert.Equal($"/library/{savedId}/report", reportLink.GetAttribute("href"));
        });

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/investigate/winter-war?saved={savedId}");

        var bannerCut = Render<SavedInvestigationBanner>(p => p.Add(x => x.AnalysedAt, _clock.GetUtcNow()));
        var bannerReport = bannerCut.Find("[data-testid=saved-report]");
        Assert.Equal("Report", bannerReport.TextContent.Trim());
        Assert.Equal($"/library/{savedId}/report", bannerReport.GetAttribute("href"));
    }
}
