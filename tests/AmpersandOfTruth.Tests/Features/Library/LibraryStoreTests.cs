using System.Buffers.Text;
using System.Globalization;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Library;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Library;

public sealed class LibraryStoreTests
{
    [Fact]
    public async Task Save_lists_only_for_matching_owner_and_round_trips_snapshot()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero));
        var store = new LibraryStore(db.Factory, clock);

        var analysedAt = new DateTimeOffset(2026, 11, 14, 8, 15, 0, TimeSpan.Zero);
        var articles = new List<TopicArticle>
        {
            new("fi", 1150, "Talvisota", 0.04),
            new("fi", 1200, "Mainilan laukaukset", 0.08),
        };
        var incidents = new List<IncidentRecord>
        {
            MakeIncident("inc-campaign", 1150, [1001, 1002], [1003], EvidenceTier.Campaign, 92.5),
            MakeIncident("inc-contested", 1200, [2001], [2002], EvidenceTier.Contested, 55.0),
            MakeIncident("inc-reverted", 1150, [3001], [3002], EvidenceTier.Reverted, 18.0),
        };
        var inputSnapshot = new SavedSnapshot("Winter War", articles, incidents, analysedAt);

        var id = await store.SaveAsync("owner-alice", "My Winter War notes", inputSnapshot);

        var aliceItems = await store.ListAsync("owner-alice");
        var bobItems = await store.ListAsync("owner-bob");

        Assert.Empty(bobItems);
        var summary = Assert.Single(aliceItems);
        Assert.Equal(id, summary.Id);
        Assert.Equal("My Winter War notes", summary.Title);
        Assert.Equal("Winter War", summary.Topic);
        Assert.Equal("winter-war", summary.TopicSlug);
        Assert.Equal(3, summary.IncidentCount);
        Assert.Equal(1, summary.TierCounts[EvidenceTier.Campaign]);
        Assert.Equal(1, summary.TierCounts[EvidenceTier.Contested]);
        Assert.Equal(1, summary.TierCounts[EvidenceTier.Reverted]);

        // GetAsync works by id regardless of owner (link = capability)
        var loaded = await store.GetAsync(id);
        Assert.NotNull(loaded);
        var (loadedSummary, loadedSnapshot) = loaded.Value;

        Assert.Equal(id, loadedSummary.Id);
        Assert.Equal("Winter War", loadedSnapshot.Topic);
        Assert.Equal(analysedAt, loadedSnapshot.AnalysedAt);
        Assert.Equal(
            articles.Select(a => (a.Lang, a.PageId, a.Title)),
            loadedSnapshot.Articles.Select(a => (a.Lang, a.PageId, a.Title)));
        Assert.Equal(
            incidents.Select(i => i.Id),
            loadedSnapshot.Incidents.Select(i => i.Id));
        Assert.Equal(
            incidents.Select(i => i.Tier),
            loadedSnapshot.Incidents.Select(i => i.Tier));
        Assert.Equal(
            incidents.Select(i => i.InsertingRevIds),
            loadedSnapshot.Incidents.Select(i => i.InsertingRevIds));
        Assert.Equal(
            incidents.Select(i => i.RemovingRevIds),
            loadedSnapshot.Incidents.Select(i => i.RemovingRevIds));

        await using var ctx = db.CreateContext();
        var entity = await ctx.Set<SavedInvestigation>().AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(InvestigationPipeline.Version.ToString(CultureInfo.InvariantCulture), entity.PipelineVersion);

        Assert.Null(await store.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Rename_and_delete_refuse_wrong_owner_and_delete_cascades_pins()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var store = new LibraryStore(db.Factory, clock);

        var snapshot = new SavedSnapshot(
            "Talvisota",
            [new TopicArticle("fi", 1150, "Talvisota", 0.05)],
            [MakeIncident("inc-1", 1150, [101], [102], EvidenceTier.Campaign, 80.0)],
            clock.GetUtcNow());

        var id = await store.SaveAsync("owner-1", "Original title", snapshot);

        // Pin by wrong owner is ignored; pin by owner succeeds
        await store.PinAsync(id, "owner-2", "inc-1", "intruder note");
        Assert.Empty(await store.PinsAsync(id));

        await store.PinAsync(id, "owner-1", "inc-1", "important");
        var pin = Assert.Single(await store.PinsAsync(id));
        Assert.Equal("inc-1", pin.IncidentId);
        Assert.Equal("important", pin.Note);

        // Unpin by wrong owner is ignored
        await store.UnpinAsync(id, "owner-2", "inc-1");
        Assert.Single(await store.PinsAsync(id));

        // Rename & delete by wrong owner return false and leave entity intact
        Assert.False(await store.RenameAsync(id, "owner-2", "Hacked title"));
        Assert.False(await store.DeleteAsync(id, "owner-2"));

        var afterRefused = await store.GetAsync(id);
        Assert.NotNull(afterRefused);
        Assert.Equal("Original title", afterRefused.Value.Summary.Title);

        // Rename by right owner succeeds
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await store.RenameAsync(id, "owner-1", "Updated title"));
        var renamed = await store.GetAsync(id);
        Assert.NotNull(renamed);
        Assert.Equal("Updated title", renamed.Value.Summary.Title);

        // Delete by right owner removes investigation AND cascades PinnedIncidents
        Assert.True(await store.DeleteAsync(id, "owner-1"));
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.PinsAsync(id));

        await using var verifyCtx = db.CreateContext();
        Assert.Equal(0, await verifyCtx.Set<PinnedIncident>().CountAsync());
    }

    [Fact]
    public async Task List_orders_by_UpdatedAt_descending_including_after_UpdateSnapshot()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var store = new LibraryStore(db.Factory, clock);

        var snap1 = new SavedSnapshot("Topic A", [], [], clock.GetUtcNow());
        var idA = await store.SaveAsync("owner-1", "First", snap1);

        clock.Advance(TimeSpan.FromMinutes(10));
        var snap2 = new SavedSnapshot("Topic B", [], [], clock.GetUtcNow());
        var idB = await store.SaveAsync("owner-1", "Second", snap2);

        clock.Advance(TimeSpan.FromMinutes(10));
        var snap3 = new SavedSnapshot("Topic C", [], [], clock.GetUtcNow());
        var idC = await store.SaveAsync("owner-1", "Third", snap3);

        var initialOrder = await store.ListAsync("owner-1");
        Assert.Equal([idC, idB, idA], initialOrder.Select(x => x.Id));

        // Updating snapshot of idA bumps its UpdatedAt to the top (and wrong owner does nothing)
        clock.Advance(TimeSpan.FromMinutes(15));
        var refreshedSnapA = new SavedSnapshot(
            "Topic A",
            [new TopicArticle("fi", 1150, "Talvisota", 0.02)],
            [MakeIncident("inc-new", 1150, [501], [502], EvidenceTier.Contested, 60.0)],
            clock.GetUtcNow());

        await store.UpdateSnapshotAsync(idA, "wrong-owner", refreshedSnapA);
        Assert.Equal([idC, idB, idA], (await store.ListAsync("owner-1")).Select(x => x.Id));

        await store.UpdateSnapshotAsync(idA, "owner-1", refreshedSnapA);
        var reordered = await store.ListAsync("owner-1");
        Assert.Equal([idA, idC, idB], reordered.Select(x => x.Id));
        Assert.Equal(1, reordered[0].IncidentCount);
        Assert.Equal(1, reordered[0].TierCounts[EvidenceTier.Contested]);
    }

    [Fact]
    public async Task OwnerKey_generates_128_bit_url_safe_id_and_persists_to_localStorage()
    {
        var generated = OwnerKey.GenerateKey();
        Assert.Equal(22, generated.Length);
        var decoded = Base64Url.DecodeFromChars(generated);
        Assert.Equal(16, decoded.Length); // 128 bits

        await using var ctx = new BunitContext();
        ctx.JSInterop.Setup<string?>("localStorage.getItem", OwnerKey.StorageKey).SetResult((string?)null);
        var setInvocation = ctx.JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();

        var ownerKey = new OwnerKey(ctx.JSInterop.JSRuntime);
        var key1 = await ownerKey.GetOrCreateAsync();
        var key2 = await ownerKey.GetOrCreateAsync();

        Assert.Equal(key1, key2);
        Assert.Equal(22, key1.Length);
        var call = Assert.Single(setInvocation.Invocations);
        Assert.Equal(OwnerKey.StorageKey, call.Arguments[0]);
        Assert.Equal(key1, call.Arguments[1]);
    }

    private static IncidentRecord MakeIncident(
        string id,
        long pageId,
        IReadOnlyList<long> insertingRevIds,
        IReadOnlyList<long> removingRevIds,
        EvidenceTier tier,
        double score) =>
        new()
        {
            Id = id,
            PageId = pageId,
            InsertingRevIds = insertingRevIds,
            RemovingRevIds = removingRevIds,
            Tier = tier,
            Score = score,
            ClaimEn = $"Claim for {id}",
            ClaimFi = $"Väite {id}",
        };
}
