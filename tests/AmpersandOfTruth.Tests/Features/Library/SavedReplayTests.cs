using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Library;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Library;

public sealed class SavedReplayTests
{
    [Fact]
    public async Task Replays_events_in_order_with_matching_incident_count_and_analysed_timestamp()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var store = new LibraryStore(db.Factory, clock);
        var replay = new SavedReplay(store);

        var analysedAt = new DateTimeOffset(2026, 11, 13, 18, 45, 0, TimeSpan.Zero);
        var articles = new List<TopicArticle>
        {
            new("fi", 1150, "Talvisota", 0.04),
            new("fi", 1200, "Mainilan laukaukset", 0.08),
        };
        var incidents = new List<IncidentRecord>
        {
            new()
            {
                Id = "inc-1",
                PageId = 1150,
                InsertingRevIds = [1001, 1002],
                RemovingRevIds = [1003],
                Tier = EvidenceTier.Campaign,
                Score = 90.0,
                ClaimEn = "First saved claim",
            },
            new()
            {
                Id = "inc-2",
                PageId = 1200,
                InsertingRevIds = [2001],
                RemovingRevIds = [2002],
                Tier = EvidenceTier.Contested,
                Score = 45.0,
                ClaimEn = "Second saved claim",
            },
        };

        var id = await store.SaveAsync("owner-1", "Winter War saved", new SavedSnapshot("Winter War", articles, incidents, analysedAt));

        var events = new List<InvestigationEvent>();
        await foreach (var evt in replay.EventsAsync(id))
        {
            events.Add(evt);
        }

        Assert.Equal(5, events.Count);

        var found = Assert.IsType<ArticlesFound>(events[0]);
        Assert.Equal(2, found.Count);
        Assert.Equal([1150L, 1200L], found.Items.Select(a => a.PageId));

        var scanned = Assert.IsType<RevisionsScanned>(events[1]);
        Assert.Equal(0, scanned.Count);
        Assert.Empty(scanned.Candidates);

        var scored1 = Assert.IsType<CandidateScored>(events[2]);
        Assert.Equal(1, scored1.Index);
        Assert.Equal(2, scored1.Of);
        Assert.Equal("inc-1", scored1.Incident?.Id);
        Assert.Equal(1150, scored1.Candidate.Article.PageId);
        Assert.Equal(1003, scored1.Candidate.Revert.RevId);
        Assert.Equal(1002, scored1.Candidate.Revert.RevertedRevId);

        var scored2 = Assert.IsType<CandidateScored>(events[3]);
        Assert.Equal(2, scored2.Index);
        Assert.Equal(2, scored2.Of);
        Assert.Equal("inc-2", scored2.Incident?.Id);
        Assert.Equal(1200, scored2.Candidate.Article.PageId);
        Assert.Equal(2002, scored2.Candidate.Revert.RevId);
        Assert.Equal(2001, scored2.Candidate.Revert.RevertedRevId);

        var completed = Assert.IsType<Completed>(events[4]);
        Assert.True(completed.FromCache);
        Assert.Equal(TimeSpan.Zero, completed.Elapsed);
        Assert.Equal(analysedAt, completed.AnalysedAt);
        Assert.Equal(incidents.Count, completed.Incidents.Count);
        Assert.Equal(["inc-1", "inc-2"], completed.Incidents.Select(i => i.Id));
    }

    [Fact]
    public async Task Unknown_id_emits_single_StageFailed_event()
    {
        using var db = new TestDb();
        var store = new LibraryStore(db.Factory, new FakeTimeProvider());
        var replay = new SavedReplay(store);

        var events = new List<InvestigationEvent>();
        await foreach (var evt in replay.EventsAsync(Guid.NewGuid()))
        {
            events.Add(evt);
        }

        var failed = Assert.Single(events);
        var stageFailed = Assert.IsType<StageFailed>(failed);
        Assert.Equal(InvestigationStage.FindArticles, stageFailed.Stage);
        Assert.Equal(StageFailureKind.Error, stageFailed.Kind);
        Assert.Equal("Saved investigation not found", stageFailed.Error);
    }
}
