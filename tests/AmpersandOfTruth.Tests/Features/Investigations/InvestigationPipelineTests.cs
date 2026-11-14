using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

public class InvestigationPipelineTests
{
    private readonly PipelineKit _kit = new();

    [Fact]
    public async Task Streams_articles_then_scan_then_one_score_per_candidate_then_completed()
    {
        var events = await _kit.RunAsync("  Talvisota  ");

        Assert.Collection(events,
            e => Assert.Equal(["Talvisota", "Winter War", "Jatkosota"], Assert.IsType<ArticlesFound>(e).Items.Select(a => a.Title)),
            e =>
            {
                var scan = Assert.IsType<RevisionsScanned>(e);
                Assert.Equal(4, scan.Count);
                Assert.Equal([2000L, 1000L], scan.Candidates.Select(c => c.Revert.RevId)); // newest first
            },
            e => Assert.Equal((1, 2), (Assert.IsType<CandidateScored>(e).Index, ((CandidateScored)e).Of)),
            e => Assert.Equal((2, 2), (Assert.IsType<CandidateScored>(e).Index, ((CandidateScored)e).Of)),
            e => Assert.False(Assert.IsType<Completed>(e).FromCache));
        Assert.Equal(1, _kit.Embedder.Calls);
    }

    [Fact]
    public async Task Claim_edit_becomes_an_incident_with_the_assessment_and_masked_editors()
    {
        var events = await _kit.RunAsync();

        var incident = Assert.Single(Assert.IsType<Completed>(events[^1]).Incidents);
        Assert.Equal("1256-2000", incident.Id);
        Assert.Equal(1256, incident.PageId);
        Assert.Equal([1999L], incident.InsertingRevIds);
        Assert.Equal([2000L], incident.RemovingRevIds);
        Assert.Equal(EvidenceTier.Reverted, incident.Tier); // no removal reason / re-insertion wired yet → level 1
        Assert.Equal(PipelineKit.ClaimEn, incident.ClaimEn);
        Assert.Equal(ContentCategory.FalseFactualClaim, incident.Category);
        Assert.True(incident.Assessment!.HasVerifiedQuote);
        Assert.Equal(4, incident.Assessment.Confidence);
        Assert.StartsWith(EvidenceTiers.Reverted.Label, incident.TierReason);
        Assert.Equal([IncidentEditRole.Inserted, IncidentEditRole.Removed], incident.Edits.Select(e => e.Role));
        Assert.Equal("192.0.x.x (anonymous)", incident.Edits[0].UserDisplay);
        Assert.Equal("https://fi.wikipedia.org/w/index.php?diff=prev&oldid=1999", incident.Edits[0].DiffRef);
        Assert.Equal(60, incident.TotalExposureSeconds);
        Assert.Equal(["removed_by_others"], incident.Signals.Select(s => s.Kind));
    }

    [Fact]
    public async Task Not_substantive_edit_is_excluded_without_an_llm_call()
    {
        var events = await _kit.RunAsync();

        var excluded = events.OfType<CandidateScored>().Single(c => c.Candidate.Revert.RevId == 1000);
        Assert.Null(excluded.Incident);
        Assert.Equal("not_substantive", excluded.Exclusion);
        var call = Assert.Single(_kit.Model.Requests);
        Assert.Contains("Suomi hyökkäsi ensin", call.Variables["inserted_text"]);
        Assert.StartsWith("investigation:", call.Scope);
    }

    [Fact]
    public async Task Llm_down_degrades_the_incident_to_the_lowest_level_instead_of_failing()
    {
        _kit.Model.Returns("classify-edit", AnalysisOutcome.Failed, "503");

        var events = await _kit.RunAsync();

        var incident = Assert.Single(Assert.IsType<Completed>(events[^1]).Incidents);
        Assert.Equal(EvidenceTier.Reverted, incident.Tier);
        Assert.Null(incident.Assessment);
        Assert.Null(incident.ClaimEn);
    }

    [Fact]
    public async Task Confident_graffiti_is_excluded()
    {
        _kit.Model.Returns("classify-edit", PipelineKit.ClassificationJson(category: "vandalism", confidence: 5));

        var events = await _kit.RunAsync();

        Assert.Empty(Assert.IsType<Completed>(events[^1]).Incidents);
        Assert.Contains(events.OfType<CandidateScored>(), c => c.Exclusion == "graffiti");
    }

    [Fact]
    public async Task One_unreadable_edit_is_skipped_and_the_run_completes()
    {
        _kit.Revisions.BrokenRevisions.Add(999);

        var events = await _kit.RunAsync();

        Assert.Contains(events.OfType<CandidateScored>(), c => c.Exclusion == "unreadable");
        Assert.Single(Assert.IsType<Completed>(events[^1]).Incidents);
    }

    [Fact]
    public async Task Every_edit_unreadable_fails_the_read_stage()
    {
        _kit.Revisions.BrokenRevisions.UnionWith([999, 1999]);

        var events = await _kit.RunAsync();

        var failed = Assert.IsType<StageFailed>(events[^1]);
        Assert.Equal(InvestigationStage.ReadEdits, failed.Stage);
        Assert.Equal(StageFailureKind.CorpusUnavailable, failed.Kind);
        Assert.DoesNotContain(events, e => e is Completed);
    }

    [Fact]
    public async Task Embedder_down_fails_stage_one()
    {
        _kit.Embedder.Unavailable = true;

        var events = await _kit.RunAsync();

        var failed = Assert.IsType<StageFailed>(Assert.Single(events));
        Assert.Equal((InvestigationStage.FindArticles, StageFailureKind.EmbeddingUnavailable), (failed.Stage, failed.Kind));
    }

    [Fact]
    public async Task Corpus_down_fails_stage_one()
    {
        _kit.Corpus.Unavailable = true;

        var failed = Assert.IsType<StageFailed>(Assert.Single(await _kit.RunAsync()));

        Assert.Equal((InvestigationStage.FindArticles, StageFailureKind.CorpusUnavailable), (failed.Stage, failed.Kind));
    }

    [Fact]
    public async Task Revision_store_down_fails_stage_two_after_the_articles_arrived()
    {
        _kit.Revisions.Unavailable = true;

        var events = await _kit.RunAsync();

        Assert.IsType<ArticlesFound>(events[0]);
        var failed = Assert.IsType<StageFailed>(events[1]);
        Assert.Equal((InvestigationStage.ScanHistory, StageFailureKind.CorpusUnavailable), (failed.Stage, failed.Kind));
    }

    [Fact]
    public async Task Topic_with_only_non_finnish_articles_completes_with_nothing_to_read()
    {
        _kit.Corpus.Articles.RemoveAll(a => a.HasHistory);

        var events = await _kit.RunAsync();

        Assert.Collection(events,
            e => Assert.Single(Assert.IsType<ArticlesFound>(e).Items),
            e => Assert.Empty(Assert.IsType<RevisionsScanned>(e).Candidates),
            e => Assert.Empty(Assert.IsType<Completed>(e).Incidents));
        Assert.Empty(_kit.Corpus.RevertQueries);
    }

    [Fact]
    public async Task Cancelling_mid_run_ends_the_stream_with_OperationCanceledException()
    {
        _kit.Model.Delay = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource();
        var seen = new List<InvestigationEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in _kit.Build().RunAsync("Talvisota", cts.Token))
            {
                seen.Add(e);
                if (e is RevisionsScanned) await cts.CancelAsync();
            }
        });
        Assert.DoesNotContain(seen, e => e is Completed);
    }

    [Fact]
    public async Task Candidate_cap_round_robins_over_articles()
    {
        _kit.Corpus.Reverts.Clear();
        _kit.Corpus.Reverts.AddRange([
            FakeInvestigationCorpus.Revert(1256, 100), FakeInvestigationCorpus.Revert(1256, 200), FakeInvestigationCorpus.Revert(1256, 300),
            FakeInvestigationCorpus.Revert(5263, 400),
            FakeInvestigationCorpus.Revert(5263, 500) with { RevertedCount = 16 }, // wide span → skipped
        ]);
        _kit.Options.MaxCandidates = 3;

        var scan = (await _kit.RunAsync()).OfType<RevisionsScanned>().Single();

        Assert.Equal([300L, 400L, 200L], scan.Candidates.Select(c => c.Revert.RevId));
    }
}
