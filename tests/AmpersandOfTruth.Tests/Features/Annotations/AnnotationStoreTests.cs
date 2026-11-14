using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Annotations;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Annotations;

public sealed class AnnotationStoreTests
{
    [Fact]
    public async Task Upsert_and_read_round_trips_and_updates_existing_annotation()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var store = new AnnotationStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-alice", "Saved Winter War", MakeSnapshot(clock.GetUtcNow()));

        var created = await store.UpsertAsync(
            id,
            "owner-alice",
            "ww-01",
            AnalystVerdict.Plausible,
            dismiss: null,
            note: "Initial analyst note",
            source: "analyst");

        Assert.True(created);

        var firstRead = await store.ForInvestigationAsync(id);
        var ann = Assert.Single(firstRead).Value;
        Assert.Equal(id, ann.InvestigationId);
        Assert.Equal("ww-01", ann.IncidentId);
        Assert.Equal(AnalystVerdict.Plausible, ann.Verdict);
        Assert.Null(ann.DismissReason);
        Assert.Equal("Initial analyst note", ann.Note);
        Assert.Equal("analyst", ann.Source);
        Assert.Equal(clock.GetUtcNow(), ann.UpdatedAt);

        clock.Advance(TimeSpan.FromMinutes(15));
        var updated = await store.UpsertAsync(
            id,
            "owner-alice",
            "ww-01",
            AnalystVerdict.NotAnIncident,
            DismissReason.LegitimateDispute,
            note: "Updated after reading talk page",
            source: "analyst");

        Assert.True(updated);

        var secondRead = await store.ForInvestigationAsync(id);
        var updatedAnn = Assert.Single(secondRead).Value;
        Assert.Equal(AnalystVerdict.NotAnIncident, updatedAnn.Verdict);
        Assert.Equal(DismissReason.LegitimateDispute, updatedAnn.DismissReason);
        Assert.Equal("Updated after reading talk page", updatedAnn.Note);
        Assert.Equal(clock.GetUtcNow(), updatedAnn.UpdatedAt);
    }

    [Fact]
    public async Task Upsert_and_clear_refuse_wrong_owner_and_unknown_investigation()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var store = new AnnotationStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-alice", "Saved Winter War", MakeSnapshot(clock.GetUtcNow()));

        // Unknown investigation id refused
        Assert.False(await store.UpsertAsync(
            Guid.NewGuid(),
            "owner-alice",
            "ww-01",
            AnalystVerdict.Confirmed,
            dismiss: null,
            note: "note",
            source: "analyst"));

        // Wrong owner refused on upsert
        Assert.False(await store.UpsertAsync(
            id,
            "owner-bob",
            "ww-01",
            AnalystVerdict.Confirmed,
            dismiss: null,
            note: "intruder",
            source: "analyst"));
        Assert.Empty(await store.ForInvestigationAsync(id));

        // Right owner succeeds
        Assert.True(await store.UpsertAsync(
            id,
            "owner-alice",
            "ww-01",
            AnalystVerdict.Confirmed,
            dismiss: null,
            note: "verified",
            source: "analyst"));
        Assert.Single(await store.ForInvestigationAsync(id));

        // Wrong owner refused on clear; unknown id refused on clear
        Assert.False(await store.ClearAsync(id, "owner-bob", "ww-01"));
        Assert.False(await store.ClearAsync(Guid.NewGuid(), "owner-alice", "ww-01"));
        Assert.Single(await store.ForInvestigationAsync(id));

        // Right owner clears annotation
        Assert.True(await store.ClearAsync(id, "owner-alice", "ww-01"));
        Assert.Empty(await store.ForInvestigationAsync(id));
    }

    [Fact]
    public async Task Deleting_saved_investigation_cascades_incident_annotations()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var store = new AnnotationStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-alice", "Saved Winter War", MakeSnapshot(clock.GetUtcNow()));

        Assert.True(await store.UpsertAsync(id, "owner-alice", "ww-01", AnalystVerdict.Confirmed, null, "one", "analyst"));
        Assert.True(await store.UpsertAsync(id, "owner-alice", "ww-02", AnalystVerdict.NotAnIncident, DismissReason.OrdinaryVandalism, "two", "analyst"));
        Assert.Equal(2, (await store.ForInvestigationAsync(id)).Count);

        Assert.True(await library.DeleteAsync(id, "owner-alice"));

        Assert.Empty(await store.ForInvestigationAsync(id));
        await using var ctx = db.CreateContext();
        Assert.Equal(0, await ctx.Set<IncidentAnnotation>().CountAsync());
    }

    private static SavedSnapshot MakeSnapshot(DateTimeOffset analysedAt) =>
        new(
            "Winter War",
            [new TopicArticle("fi", 1150, "Talvisota", 0.04)],
            [
                new IncidentRecord
                {
                    Id = "ww-01",
                    PageId = 1150,
                    InsertingRevIds = [1001, 1002],
                    RemovingRevIds = [1003],
                    Tier = EvidenceTier.Campaign,
                    Score = 90.0,
                    ClaimEn = "Claim one",
                    ClaimFi = "Väite yksi",
                },
                new IncidentRecord
                {
                    Id = "ww-02",
                    PageId = 1150,
                    InsertingRevIds = [2001],
                    RemovingRevIds = [2002],
                    Tier = EvidenceTier.Contested,
                    Score = 60.0,
                    ClaimEn = "Claim two",
                    ClaimFi = "Väite kaksi",
                },
            ],
            analysedAt);
}
