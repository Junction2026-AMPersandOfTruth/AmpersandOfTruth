using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>One incident per inserted content, not per revert (S3-A, CONTRACT.md §3 claim-side counting).</summary>
public class IncidentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static IncidentEdit Edit(long rev, IncidentEditRole role) =>
        new(rev, rev - 1, 1256, "Someone", false, T0.AddMinutes(rev), role, null, null, []);

    /// <summary>One revert's incident as stage 4 assembles it: span + re-insertions, the revert + re-removals.</summary>
    private static IncidentRecord Incident(
        string id, long[] span, long revert, long[] reinsertions, long[] reRemovals,
        EvidenceTier tier = EvidenceTier.Contested, double score = 205, long page = 1256, string? claim = null) => new()
    {
        Id = id,
        PageId = page,
        InsertingRevIds = [.. span, .. reinsertions],
        RemovingRevIds = [revert, .. reRemovals],
        Tier = tier,
        Score = score,
        TierReason = $"because {id}",
        ClaimEn = claim ?? $"claim {id}",
        Signals = [new IncidentSignal("reinserted", id)],
        Articles = [new IncidentArticle(page, "Talvisota")],
        FirstInsertedAt = T0.AddMinutes(span[0]),
        LastRemovedAt = T0.AddMinutes(reRemovals.Length > 0 ? reRemovals[^1] : revert),
        TotalExposureSeconds = (revert - span[0]) * 60,
        ReinsertionCount = reinsertions.Length,
        Edits = [.. span.Select(r => Edit(r, IncidentEditRole.Inserted)), Edit(revert, IncidentEditRole.Removed),
                 .. reinsertions.Select(r => Edit(r, IncidentEditRole.Reinserted)), .. reRemovals.Select(r => Edit(r, IncidentEditRole.Removed))],
    };

    private static List<IncidentRecord> Merge(params IncidentRecord[] incidents) =>
        IncidentMerger.Merge([.. incidents.Select(i => new IncidentMerger.Item(i))]);

    [Fact]
    public void A_reinsertion_chain_of_three_reverts_becomes_one_incident_with_all_revisions()
    {
        // 1999 inserted, 2000 reverts; 2002 puts it back, 2003 reverts; 2004 puts it back, 2005 reverts.
        var first = Incident("1256-2000", [1999], 2000, [2002, 2004], [2003, 2005], score: 230);
        var second = Incident("1256-2003", [2002], 2003, [2004], [2005], score: 240, tier: EvidenceTier.Campaign);
        var third = Incident("1256-2005", [2004], 2005, [], [], score: 210);

        var merged = Assert.Single(Merge(third, first, second));

        Assert.Equal("1256-2003", merged.Id);                    // the strongest member: highest tier
        Assert.Equal("1256-2003", merged.ClusterId);
        Assert.Equal(EvidenceTier.Campaign, merged.Tier);
        Assert.Equal(240, merged.Score);
        Assert.Equal("claim 1256-2003", merged.ClaimEn);
        Assert.Equal("because 1256-2003", merged.TierReason);
        Assert.Equal([1999L, 2002, 2004], merged.InsertingRevIds);
        Assert.Equal([2000L, 2003, 2005], merged.RemovingRevIds);
        Assert.Equal([1999L, 2000, 2002, 2003, 2004, 2005], merged.Edits.Select(e => e.RevId));
        Assert.Equal(
            [IncidentEditRole.Inserted, IncidentEditRole.Removed, IncidentEditRole.Reinserted,
             IncidentEditRole.Removed, IncidentEditRole.Reinserted, IncidentEditRole.Removed],
            merged.Edits.Select(e => e.Role));                     // 2002 is "inserted" in one member, "reinserted" in another
        Assert.Equal(T0.AddMinutes(1999), merged.FirstInsertedAt);
        Assert.Equal(T0.AddMinutes(2005), merged.LastRemovedAt);
        Assert.Equal(60, merged.TotalExposureSeconds);
        Assert.Equal(2, merged.ReinsertionCount);
    }

    [Fact]
    public void Same_tier_picks_the_highest_score_as_the_lead()
    {
        var merged = Assert.Single(Merge(
            Incident("1256-2000", [1999], 2000, [2002], [], score: 210),
            Incident("1256-2003", [2002], 2003, [], [], score: 230)));

        Assert.Equal("1256-2003", merged.Id);
        Assert.Equal(230, merged.Score);
    }

    [Fact]
    public void Unrelated_incidents_on_the_same_page_stay_separate_and_unchanged()
    {
        var a = Incident("1256-2000", [1999], 2000, [], []);
        var b = Incident("1256-3000", [2999], 3000, [], []);

        var result = Merge(a, b);

        Assert.Equal(2, result.Count);
        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
        Assert.Null(result[0].ClusterId);
    }

    [Fact]
    public void Incidents_on_different_pages_never_merge()
    {
        var result = Merge(
            Incident("1256-2000", [1999], 2000, [2002], []),
            Incident("5263-2003", [2002], 2003, [], [], page: 5263));

        Assert.Equal(2, result.Count);
        Assert.Equal([1256L, 5263], result.Select(i => i.PageId));
    }

    [Fact]
    public void Identical_inserted_prose_merges_without_shared_revisions_but_an_empty_key_does_not()
    {
        var a = Incident("1256-2000", [1999], 2000, [], []);
        var b = Incident("1256-3000", [2999], 3000, [], []);
        var c = Incident("1256-4000", [3999], 4000, [], []);

        var result = IncidentMerger.Merge([
            new(a, IncidentMerger.ProseKey("Suomi hyökkäsi ensin.")),
            new(b, IncidentMerger.ProseKey("  SUOMI   hyökkäsi ensin  ")),
            new(c, IncidentMerger.ProseKey("…")),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal([1999L, 2999], result[0].InsertingRevIds);
        Assert.Same(c, result[1]);
    }

    [Fact]
    public void Never_merges_when_every_inserting_revision_would_also_be_a_removing_one()
    {
        // S3-A's guard: a revert of a revert lists the first revert as "inserting".
        var a = Incident("1256-2000", [2000], 2001, [], []);
        var b = Incident("1256-2001", [2001], 2000, [], []);

        Assert.Equal(2, IncidentMerger.Merge([new(a, "same"), new(b, "same")]).Count);
    }

    [Theory]
    [InlineData("Suomi hyökkäsi ensin.", "suomi hyökkäsi ensin")]
    [InlineData("  «Foo   bar» … baz! ", "foo bar» baz")]
    [InlineData("—", "")]
    [InlineData(null, "")]
    public void Prose_key_matches_the_python_normalisation(string? prose, string expected) =>
        Assert.Equal(expected, IncidentMerger.ProseKey(prose));
}
