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
            e => Assert.Equal(["Talvisota", "Jatkosota", "Lapin sota"], Assert.IsType<ArticlesFound>(e).Items.Select(a => a.Title)),
            e =>
            {
                var scan = Assert.IsType<RevisionsScanned>(e);
                Assert.Equal(4, scan.Count);
                Assert.Equal([2000L, 1000L], scan.Candidates.Select(c => c.Revert.RevId)); // newest first
            },
            e => Assert.Equal((1, 2), (Assert.IsType<CandidateScored>(e).Index, ((CandidateScored)e).Of)),
            e => Assert.Equal((2, 2), (Assert.IsType<CandidateScored>(e).Index, ((CandidateScored)e).Of)),
            e =>
            {
                var done = Assert.IsType<Completed>(e);
                Assert.False(done.FromCache);
                Assert.Equal(0, done.Degraded);
                Assert.NotNull(done.FirstIncidentAfter);
                Assert.Equal(Enum.GetValues<InvestigationStage>(), done.StageTimings.Keys.Order());
            });
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
    public async Task Topic_with_no_finnish_article_completes_with_nothing_to_read()
    {
        _kit.Corpus.Articles.RemoveAll(a => a.Title != "Soviet propaganda");

        var events = await _kit.RunAsync();

        Assert.Collection(events,
            e => Assert.Empty(Assert.IsType<ArticlesFound>(e).Items),
            e => Assert.Empty(Assert.IsType<RevisionsScanned>(e).Candidates),
            e => Assert.Empty(Assert.IsType<Completed>(e).Incidents));
        Assert.Empty(_kit.Corpus.RevertQueries);
    }

    [Fact]
    public async Task En_hits_fold_onto_their_fi_sibling_keeping_the_best_rank()
    {
        var found = Assert.IsType<ArticlesFound>((await _kit.RunAsync())[0]);

        Assert.All(found.Items, a => Assert.Equal("fi", a.Lang));
        var lapland = Assert.Single(found.Items, a => a.PageId == 9012);
        Assert.Equal(("en", 0.25), (lapland.ViaLang, lapland.Distance));
        Assert.Null(Assert.Single(found.Items, a => a.PageId == 1256).ViaLang); // its own fi hit ranked first
    }

    [Fact]
    public async Task Article_limit_applies_after_folding()
    {
        _kit.Options.ArticleLimit = 2;

        var found = Assert.IsType<ArticlesFound>((await _kit.RunAsync())[0]);

        Assert.Equal([1256L, 5263L], found.Items.Select(a => a.PageId));
    }

    [Fact]
    public async Task Given_articles_skip_topic_search()
    {
        var articles = await _kit.Corpus.FiArticlesAsync([1256]);

        var events = await PipelineKit.Collect(_kit.Build().RunOnArticlesAsync("Winter War causes", articles));

        Assert.Equal(0, _kit.Embedder.Calls);
        Assert.Equal([1256L], Assert.IsType<ArticlesFound>(events[0]).Items.Select(a => a.PageId));
        Assert.Single(Assert.IsType<Completed>(events[^1]).Incidents);
    }

    [Fact]
    public async Task Llm_failure_counts_as_degraded_and_marks_the_candidate()
    {
        _kit.Model.Returns("classify-edit", AnalysisOutcome.BudgetExceeded);

        var events = await _kit.RunAsync();

        Assert.Equal(1, Assert.IsType<Completed>(events[^1]).Degraded);
        Assert.Single(events.OfType<CandidateScored>(), c => c.AssessmentUnavailable);
    }

    [Fact]
    public async Task Cancelling_mid_run_ends_the_stream_with_OperationCanceledException()
    {
        _kit.Model.Delay = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource();
        var seen = new List<InvestigationEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in _kit.Build().RunAsync("Talvisota", ct: cts.Token))
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

    private async Task<IncidentRecord> TheIncident() =>
        Assert.Single(Assert.IsType<Completed>((await _kit.RunAsync())[^1]).Incidents);

    private void RevertComment(string comment)
    {
        _kit.Corpus.Reverts.RemoveAll(r => r.RevId == 2000);
        _kit.Corpus.Reverts.Add(FakeInvestigationCorpus.Revert(1256, 2000, comment: comment));
    }

    [Fact]
    public async Task A_claim_removed_as_inaccurate_is_disputed()
    {
        RevertComment("Kumottu muokkaus 1999, jonka teki 192.0.2.1 (keskustelu) väärää tietoa");

        var incident = await TheIncident();

        Assert.Equal(EvidenceTier.Contested, incident.Tier);
        Assert.Equal(["removal_reason", "claim_content"], incident.Signals.Select(s => s.Kind));
        Assert.Equal("väärää tietoa", incident.Signals[0].Evidence);
    }

    [Fact]
    public async Task A_removal_reason_without_a_verified_claim_stays_at_level_one()
    {
        RevertComment("rv, väärää tietoa");
        _kit.Model.Returns("classify-edit", PipelineKit.ClassificationJson(quote: "not in the edit"));

        Assert.Equal(EvidenceTier.Reverted, (await TheIncident()).Tier);
    }

    [Fact]
    public async Task Content_put_back_after_removal_is_disputed_and_listed_as_reinserted()
    {
        _kit.Revisions.Later(1256, 2001, "Someone", PipelineKit.Before + " muu", FakeRevisionStore.T0.AddMinutes(2001));
        _kit.Revisions.Later(1256, 2002, "192.0.2.1", PipelineKit.After, FakeRevisionStore.T0.AddMinutes(2002));
        _kit.Revisions.Later(1256, 2003, "VakauttajaBot", PipelineKit.After, FakeRevisionStore.T0.AddMinutes(2003)); // null revision

        var incident = await TheIncident();

        Assert.Equal(EvidenceTier.Contested, incident.Tier);
        Assert.Equal(1, incident.ReinsertionCount);
        Assert.Equal([1999L, 2002L], incident.InsertingRevIds);
        Assert.Equal([IncidentEditRole.Inserted, IncidentEditRole.Removed, IncidentEditRole.Reinserted], incident.Edits.Select(e => e.Role));
        Assert.Contains("reinserted", incident.Signals.Select(s => s.Kind));
    }

    [Fact]
    public async Task Put_back_by_other_editors_over_more_than_a_week_is_a_possible_campaign()
    {
        _kit.Revisions.Later(1256, 2002, "198.51.100.7", PipelineKit.After, FakeRevisionStore.T0.AddDays(3));
        _kit.Revisions.Later(1256, 2003, "Someone", PipelineKit.Before, FakeRevisionStore.T0.AddDays(4));
        _kit.Revisions.Later(1256, 2004, "203.0.113.9", PipelineKit.After, FakeRevisionStore.T0.AddDays(12));

        var incident = await TheIncident();

        Assert.Equal(EvidenceTier.Campaign, incident.Tier);
        Assert.Equal(2, incident.ReinsertionCount);
        Assert.Equal(["multiple_editors", "persistence", "claim_content"], incident.Signals.Select(s => s.Kind));
        Assert.All(incident.Edits, e => Assert.DoesNotMatch(@"\d+\.\d+\.\d+\.\d+", e.UserDisplay!));
    }
}
