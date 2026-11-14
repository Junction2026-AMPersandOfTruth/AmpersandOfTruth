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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Reports;

public sealed class MarkdownReportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 16, 30, 0, TimeSpan.Zero));
    private readonly FakeIncidentCorpus _incidentCorpus = new();
    private readonly FakeRevisionStore _revisionStore = new();
    private readonly FakeAnalysisModel _llm = new();
    private readonly LibraryStore _store;
    private readonly ReportBuilder _builder;
    private readonly SampleTopic _winterWar;

    public MarkdownReportTests()
    {
        var samples = new SampleIncidents(NullLogger<SampleIncidents>.Instance);
        _winterWar = samples.Match("winter-war")
            ?? throw new InvalidOperationException("Expected winter-war sample fixture.");

        _store = new LibraryStore(_db.Factory, _clock);
        var mapper = new IncidentMapper(_incidentCorpus, _revisionStore);
        _builder = new ReportBuilder(_store, mapper, _llm, _clock);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Renders_sections_in_order_tier_counts_revision_urls_masked_ips_and_passage()
    {
        // Seed raw corpus rows with an unmasked IPv4 + IPv6 to verify full masking end-to-end, plus revision prose.
        var firstIncident = _winterWar.Incidents[0];
        var firstEdit = firstIncident.Edits[0];
        var parentRevId = firstEdit.ParentRevId ?? 8954412L;

        _incidentCorpus.Revisions.Add(new RevisionRow(
            PageId: firstIncident.PageId,
            RevId: firstEdit.RevId,
            ParentRevId: parentRevId,
            Timestamp: new DateTime(2010, 8, 27, 9, 53, 8, DateTimeKind.Utc),
            User: "85.76.12.34",
            Comment: "Edited by 2001:db8:abcd:1234::99",
            Bytes: 512,
            Sha1: "abc123"));

        _revisionStore.Texts[(firstIncident.PageId, parentRevId)] = "Lopputulos: [[Moskovan rauha]].";
        _revisionStore.Texts[(firstIncident.PageId, firstEdit.RevId)] =
            "Lopputulos: Neuvostoliiton sotilaallinen voitto, Suomen poliittinen torjuntavoitto.";

        _llm.Returns(
            SummarizeInvestigationPrompt.Id,
            """{"summary":"Multiple contested edits modified outcome and attribution phrasing on Finnish Winter War articles and were removed by editors."}""");

        var analysedAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var articles = new[]
        {
            new TopicArticle("fi", 1256, "Talvisota", 0.05),
            new TopicArticle("fi", 8295, "Mainilan laukaukset", 0.09),
        };
        var snapshot = new SavedSnapshot(_winterWar.Title, articles, _winterWar.Incidents, analysedAt);
        var savedId = await _store.SaveAsync("owner-1", "Winter War Investigation Report", snapshot);

        var secondIncident = _winterWar.Incidents[1];
        await _store.PinAsync(savedId, "owner-1", secondIncident.Id!, "Key lead-paragraph dispute for desk editor.");

        var report = await _builder.BuildAsync(savedId, new ReportOptions(PinnedOnly: false, IncludeSummary: true));
        Assert.NotNull(report);

        // Pinned incident is ordered first
        Assert.Equal(secondIncident.Id, report.Incidents[0].Id);
        Assert.True(report.Incidents[0].IsPinned);
        Assert.Equal("Key lead-paragraph dispute for desk editor.", report.Incidents[0].PinNote);

        // Scope on LLM call equals savedId
        var req = Assert.Single(_llm.Requests);
        Assert.Equal(SummarizeInvestigationPrompt.Id, req.PromptId);
        Assert.Equal(savedId.ToString(), req.Scope);

        var md = MarkdownReport.Render(report);

        // 1. Header & coverage -> 2. Executive summary -> 3. Findings -> 4. Appendix in strict order
        var idxTitle = md.IndexOf("# Winter War Investigation Report", StringComparison.Ordinal);
        var idxGenerated = md.IndexOf("Generated 2026-11-14 16:30 UTC", StringComparison.Ordinal);
        var idxAnalysed = md.IndexOf("Analysed 2026-11-14 12:00 UTC", StringComparison.Ordinal);
        var idxCoverage = md.IndexOf("2 Finnish Wikipedia articles", StringComparison.Ordinal);
        var idxSummary = md.IndexOf("## Executive summary", StringComparison.Ordinal);
        var idxAutoSummary = md.IndexOf("**Automated summary:** Multiple contested edits", StringComparison.Ordinal);
        var idxFindings = md.IndexOf("## Findings", StringComparison.Ordinal);
        var idxAppendix = md.IndexOf("## Appendix", StringComparison.Ordinal);
        var idxMethodology = md.IndexOf(AmpersandOfTruth.Web.Features.Evidence.Methodology.Summary, StringComparison.Ordinal);
        var idxLicense = md.IndexOf(Attribution.License, StringComparison.Ordinal);
        var idxCited = md.IndexOf("### Cited revisions", StringComparison.Ordinal);

        Assert.True(idxTitle >= 0);
        Assert.True(idxGenerated > idxTitle);
        Assert.True(idxAnalysed > idxGenerated);
        Assert.True(idxCoverage > idxAnalysed);
        Assert.True(idxSummary > idxCoverage);
        Assert.True(idxAutoSummary > idxSummary);
        Assert.True(idxFindings > idxAutoSummary);
        Assert.True(idxAppendix > idxFindings);
        Assert.True(idxMethodology > idxAppendix);
        Assert.True(idxLicense > idxMethodology);
        Assert.True(idxCited > idxLicense);

        // Tier counts match fixture
        var expectedCampaign = _winterWar.Incidents.Count(i => i.Tier == EvidenceTier.Campaign);
        var expectedContested = _winterWar.Incidents.Count(i => i.Tier == EvidenceTier.Contested);
        var expectedReverted = _winterWar.Incidents.Count(i => i.Tier == EvidenceTier.Reverted);
        Assert.Contains($"- **{EvidenceTiers.Campaign.Label}:** {expectedCampaign}", md);
        Assert.Contains($"- **{EvidenceTiers.Contested.Label}:** {expectedContested}", md);
        Assert.Contains($"- **{EvidenceTiers.Reverted.Label}:** {expectedReverted}", md);

        // Full revision URLs written out in timeline and appendix
        foreach (var revId in firstIncident.InsertingRevIds.Concat(firstIncident.RemovingRevIds))
        {
            Assert.Contains(WikipediaLinks.Revision(revId).AbsoluteUri, md);
        }

        // Passage prose + pin note rendered
        Assert.Contains("Lopputulos: Moskovan rauha.", md);
        Assert.Contains("Neuvostoliiton sotilaallinen voitto", md);
        Assert.Contains("> **Note:** Key lead-paragraph dispute for desk editor.", md);

        // Raw IPs never leaked; masked forms shown
        Assert.DoesNotContain("85.76.12.34", md);
        Assert.DoesNotContain("2001:db8:abcd:1234::99", md);
        Assert.Contains("85.76.x.x (anonymous)", md);
        Assert.Contains("2001:DB8:\u2026", md);
    }

    [Fact]
    public async Task Pinned_only_filters_incidents_counts_and_revisions()
    {
        _llm.Returns(SummarizeInvestigationPrompt.Id, """{"summary":"One pinned finding."}""");

        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.05)],
            _winterWar.Incidents,
            _clock.GetUtcNow());

        var savedId = await _store.SaveAsync("owner-1", "Pinned Dossier", snapshot);
        var target = _winterWar.Incidents[1];
        await _store.PinAsync(savedId, "owner-1", target.Id!, "Cite this in section 2.");

        var report = await _builder.BuildAsync(savedId, new ReportOptions(PinnedOnly: true, IncludeSummary: true));
        Assert.NotNull(report);
        var single = Assert.Single(report.Incidents);
        Assert.Equal(target.Id, single.Id);

        var md = MarkdownReport.Render(report);
        Assert.Contains(target.ClaimEn!, md);
        Assert.DoesNotContain(_winterWar.Incidents[0].ClaimEn!, md);
        Assert.Contains("Cite this in section 2.", md);

        // Tier counts reflect only the 1 pinned incident
        var totalCounted = report.ExecutiveSummary.TierCounts.Values.Sum();
        Assert.Equal(1, totalCounted);

        // Cited revisions only contain revisions from the pinned incident
        foreach (var revId in target.InsertingRevIds)
        {
            Assert.Contains(WikipediaLinks.Revision(revId).AbsoluteUri, md);
        }

        foreach (var otherRevId in _winterWar.Incidents[0].InsertingRevIds)
        {
            Assert.DoesNotContain(WikipediaLinks.Revision(otherRevId).AbsoluteUri, md);
        }
    }

    [Theory]
    [InlineData(AnalysisOutcome.Failed)]
    [InlineData(AnalysisOutcome.Blocked)]
    [InlineData(AnalysisOutcome.BudgetExceeded)]
    public async Task Omits_automated_summary_when_model_fails_but_keeps_tier_counts(AnalysisOutcome outcome)
    {
        _llm.Returns(SummarizeInvestigationPrompt.Id, outcome);

        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.05)],
            _winterWar.Incidents,
            _clock.GetUtcNow());

        var savedId = await _store.SaveAsync("owner-1", "Degraded Summary Report", snapshot);

        var report = await _builder.BuildAsync(savedId, new ReportOptions(PinnedOnly: false, IncludeSummary: true));
        Assert.NotNull(report);
        Assert.Null(report.ExecutiveSummary.AutomatedSummary);

        var md = MarkdownReport.Render(report);
        Assert.DoesNotContain("Automated summary", md);
        Assert.Contains("## Executive summary", md);
        Assert.Contains(EvidenceTiers.Contested.Label, md);
    }
}
