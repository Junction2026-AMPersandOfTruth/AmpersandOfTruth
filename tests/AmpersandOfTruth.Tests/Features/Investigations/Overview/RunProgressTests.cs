using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class RunProgressTests
{
    [Fact]
    public void Happy_path_progresses_through_all_four_stages_and_replaces_incidents_with_ranked_list()
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);

        Assert.False(progress.IsRunning);
        Assert.False(progress.IsComplete);
        Assert.Equal(TimeSpan.Zero, progress.Elapsed);
        Assert.All(Enum.GetValues<InvestigationStage>(), s =>
        {
            Assert.Equal(StageState.Pending, progress.State(s));
            Assert.Null(progress.Count(s));
        });

        progress.Start();

        Assert.True(progress.IsRunning);
        Assert.False(progress.IsComplete);
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.FindArticles));
        Assert.Equal(StageState.Pending, progress.State(InvestigationStage.ScanHistory));
        Assert.Equal(StageState.Pending, progress.State(InvestigationStage.ReadEdits));
        Assert.Equal(StageState.Pending, progress.State(InvestigationStage.AssembleIncidents));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), progress.Elapsed);

        var articles = new[]
        {
            new TopicArticle("fi", 100, "Talvisota", 0.1),
            new TopicArticle("fi", 200, "Mainilan laukaukset", 0.2),
        };
        progress.Apply(new ArticlesFound(2, articles));

        Assert.Equal(StageState.Done, progress.State(InvestigationStage.FindArticles));
        Assert.Equal(2, progress.Count(InvestigationStage.FindArticles));
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.ScanHistory));
        Assert.Null(progress.Count(InvestigationStage.ScanHistory));
        Assert.Equal(articles, progress.Articles);

        var cand1 = MakeCandidate(articles[0], revId: 1010, revertedRevId: 1009);
        var cand2 = MakeCandidate(articles[0], revId: 1020, revertedRevId: 1019);
        var cand3 = MakeCandidate(articles[1], revId: 2010, revertedRevId: 2009);
        progress.Apply(new RevisionsScanned(4500, [cand1, cand2, cand3]));

        Assert.Equal(StageState.Done, progress.State(InvestigationStage.ScanHistory));
        Assert.Equal(4500, progress.Count(InvestigationStage.ScanHistory));
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.ReadEdits));
        Assert.Equal(0, progress.Count(InvestigationStage.ReadEdits));
        Assert.Equal(0, progress.Scored);
        Assert.Equal(3, progress.Of);
        Assert.Equal(StageState.Pending, progress.State(InvestigationStage.AssembleIncidents));
        Assert.Null(progress.Count(InvestigationStage.AssembleIncidents));

        // Candidate 1: arrived first (lower score)
        var incLow = MakeIncidentRecord("inc-low", pageId: 100, insertingRevIds: [1009], score: 10);
        progress.Apply(new CandidateScored(1, 3, cand1, incLow));
        Assert.Equal(1, progress.Scored);
        Assert.Equal(1, progress.Count(InvestigationStage.ReadEdits));
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.ReadEdits));
        Assert.Equal(["inc-low"], progress.Incidents.Select(i => i.Id));

        // Candidate 2: excluded
        progress.Apply(new CandidateScored(2, 3, cand2, null, "not_substantive"));
        Assert.Equal(2, progress.Scored);
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.ReadEdits));

        // Candidate 3: Index == Of → stage 3 Done, stage 4 Active; incidents kept in arrival order
        var incHigh = MakeIncidentRecord("inc-high", pageId: 200, insertingRevIds: [2009], score: 95);
        progress.Apply(new CandidateScored(3, 3, cand3, incHigh));
        Assert.Equal(3, progress.Scored);
        Assert.Equal(3, progress.Count(InvestigationStage.ReadEdits));
        Assert.Equal(StageState.Done, progress.State(InvestigationStage.ReadEdits));
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.AssembleIncidents));
        Assert.Equal(2, progress.Count(InvestigationStage.AssembleIncidents));
        Assert.Equal(["inc-low", "inc-high"], progress.Incidents.Select(i => i.Id));

        // Completed: all Done, replaces Incidents with ranked list, sets Elapsed and FromCache
        var completedElapsed = TimeSpan.FromSeconds(14.5);
        progress.Apply(new Completed(FromCache: true, [incHigh, incLow], completedElapsed));

        Assert.False(progress.IsRunning);
        Assert.True(progress.IsComplete);
        Assert.True(progress.FromCache);
        Assert.Null(progress.Failure);
        Assert.Equal(completedElapsed, progress.Elapsed);
        Assert.All(Enum.GetValues<InvestigationStage>(), s => Assert.Equal(StageState.Done, progress.State(s)));
        Assert.Equal(2, progress.Count(InvestigationStage.AssembleIncidents));
        Assert.Equal(["inc-high", "inc-low"], progress.Incidents.Select(i => i.Id));
    }

    [Fact]
    public void Zero_candidates_transitions_read_edits_to_done_and_assemble_to_active()
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);
        progress.Start();

        var article = new TopicArticle("fi", 100, "Talvisota", 0.1);
        progress.Apply(new ArticlesFound(1, [article]));
        progress.Apply(new RevisionsScanned(120, []));

        Assert.Equal(StageState.Done, progress.State(InvestigationStage.FindArticles));
        Assert.Equal(StageState.Done, progress.State(InvestigationStage.ScanHistory));
        Assert.Equal(StageState.Done, progress.State(InvestigationStage.ReadEdits));
        Assert.Equal(StageState.Active, progress.State(InvestigationStage.AssembleIncidents));
        Assert.Equal(0, progress.Of);
        Assert.Equal(0, progress.Scored);
        Assert.Equal(0, progress.Count(InvestigationStage.ReadEdits));
        Assert.Equal(0, progress.Count(InvestigationStage.AssembleIncidents));

        progress.Apply(new Completed(FromCache: false, [], TimeSpan.FromSeconds(1)));
        Assert.True(progress.IsComplete);
        Assert.False(progress.IsRunning);
        Assert.Equal(StageState.Done, progress.State(InvestigationStage.AssembleIncidents));
    }

    [Theory]
    [InlineData(InvestigationStage.FindArticles)]
    [InlineData(InvestigationStage.ScanHistory)]
    [InlineData(InvestigationStage.ReadEdits)]
    [InlineData(InvestigationStage.AssembleIncidents)]
    public void Failure_at_each_stage_marks_that_stage_failed_and_stops_running(InvestigationStage failingStage)
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);
        progress.Start();

        var article = new TopicArticle("fi", 100, "Talvisota", 0.1);
        var candidate = MakeCandidate(article, revId: 1000, revertedRevId: 999);

        if (failingStage >= InvestigationStage.ScanHistory)
        {
            progress.Apply(new ArticlesFound(1, [article]));
        }

        if (failingStage >= InvestigationStage.ReadEdits)
        {
            progress.Apply(new RevisionsScanned(300, [candidate]));
        }

        if (failingStage >= InvestigationStage.AssembleIncidents)
        {
            progress.Apply(new CandidateScored(1, 1, candidate, null, "not_substantive"));
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        var failure = new StageFailed(failingStage, StageFailureKind.CorpusUnavailable, "tunnel down");
        progress.Apply(failure);

        Assert.False(progress.IsRunning);
        Assert.False(progress.IsComplete);
        Assert.Same(failure, progress.Failure);
        Assert.Equal(StageState.Failed, progress.State(failingStage));

        foreach (var stage in Enum.GetValues<InvestigationStage>())
        {
            if (stage < failingStage)
            {
                Assert.Equal(StageState.Done, progress.State(stage));
            }
            else if (stage > failingStage)
            {
                Assert.Equal(StageState.Pending, progress.State(stage));
            }
        }

        // Elapsed stops advancing after failure
        var frozenElapsed = progress.Elapsed;
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(frozenElapsed, progress.Elapsed);
    }

    [Fact]
    public void Read_stage_failure_after_all_candidates_scored_unreadable_resets_assemble_to_pending()
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);
        progress.Start();

        var article = new TopicArticle("fi", 100, "Talvisota", 0.1);
        var candidate = MakeCandidate(article, revId: 1000, revertedRevId: 999);

        progress.Apply(new ArticlesFound(1, [article]));
        progress.Apply(new RevisionsScanned(50, [candidate]));
        progress.Apply(new CandidateScored(1, 1, candidate, null, "unreadable"));
        progress.Apply(new StageFailed(InvestigationStage.ReadEdits, StageFailureKind.CorpusUnavailable, "all unreadable"));

        Assert.Equal(StageState.Failed, progress.State(InvestigationStage.ReadEdits));
        Assert.Equal(StageState.Pending, progress.State(InvestigationStage.AssembleIncidents));
        Assert.False(progress.IsRunning);
    }

    [Fact]
    public void IsSlow_becomes_true_only_for_active_stage_over_twenty_seconds()
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);
        progress.Start();

        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.False(progress.IsSlow(InvestigationStage.FindArticles));
        Assert.False(progress.IsSlow(InvestigationStage.ScanHistory));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(progress.IsSlow(InvestigationStage.FindArticles));
        Assert.False(progress.IsSlow(InvestigationStage.ScanHistory));

        // Advancing to Stage 2 clears slow state on Stage 1 and starts fresh 20s timer on Stage 2
        progress.Apply(new ArticlesFound(1, [new TopicArticle("fi", 100, "Talvisota", 0.1)]));
        Assert.False(progress.IsSlow(InvestigationStage.FindArticles));
        Assert.False(progress.IsSlow(InvestigationStage.ScanHistory));

        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(progress.IsSlow(InvestigationStage.ScanHistory));

        // Failure clears slow state
        progress.Apply(new StageFailed(InvestigationStage.ScanHistory, StageFailureKind.Error, "timeout"));
        Assert.False(progress.IsSlow(InvestigationStage.ScanHistory));
    }

    [Fact]
    public void Unflagged_returns_candidates_whose_reverted_rev_id_is_not_in_any_incident_inserting_revs()
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);
        progress.Start();

        var article = new TopicArticle("fi", 100, "Talvisota", 0.1);
        var flaggedDirect = MakeCandidate(article, revId: 1010, revertedRevId: 1009);
        var flaggedMultiSpan = MakeCandidate(article, revId: 1020, revertedRevId: 1019);
        var unflaggedVandalism = MakeCandidate(article, revId: 1030, revertedRevId: 1029);
        var unflaggedSelfRevert = MakeCandidate(article, revId: 1040, revertedRevId: 1039);

        progress.Apply(new ArticlesFound(1, [article]));
        progress.Apply(new RevisionsScanned(400, [flaggedDirect, flaggedMultiSpan, unflaggedVandalism, unflaggedSelfRevert]));

        var incident = MakeIncidentRecord("clustered", pageId: 100, insertingRevIds: [1009, 1018, 1019], score: 50);
        progress.Apply(new CandidateScored(1, 4, flaggedDirect, incident));
        progress.Apply(new CandidateScored(2, 4, flaggedMultiSpan, null));
        progress.Apply(new CandidateScored(3, 4, unflaggedVandalism, null, "graffiti"));
        progress.Apply(new CandidateScored(4, 4, unflaggedSelfRevert, null, "self_removal"));
        progress.Apply(new Completed(FromCache: false, [incident], TimeSpan.FromSeconds(3)));

        var unflagged = progress.Unflagged();
        Assert.Equal([unflaggedVandalism, unflaggedSelfRevert], unflagged);
    }

    private static RemovedEdit MakeCandidate(TopicArticle article, long revId, long revertedRevId) =>
        new(article, new RevertEvent(
            PageId: article.PageId,
            RevId: revId,
            Timestamp: new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            User: "Reviewer",
            Comment: "Kumottu",
            RestoredRevId: revertedRevId - 1,
            RevertedCount: 1,
            RevertedRevId: revertedRevId,
            RevertedUser: "Inserter"));

    private static IncidentRecord MakeIncidentRecord(string id, long pageId, IReadOnlyList<long> insertingRevIds, double score) =>
        new()
        {
            Id = id,
            PageId = pageId,
            InsertingRevIds = insertingRevIds,
            RemovingRevIds = [insertingRevIds[^1] + 1],
            Tier = EvidenceTier.Contested,
            Score = score,
        };
}
