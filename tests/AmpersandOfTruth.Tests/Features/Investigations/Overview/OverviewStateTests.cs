using System.Globalization;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.AspNetCore.WebUtilities;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class OverviewStateTests
{
    [Fact]
    public void Default_filter_renders_empty_query_string_and_all_null_query_values()
    {
        var filter = OverviewFilter.Default;

        Assert.True(filter.IsDefault);
        Assert.Equal("", filter.ToQueryString());

        var query = filter.ToQuery();
        Assert.Equal(["sort", "tier", "signal", "article", "from", "to"], query.Keys);
        Assert.All(query.Values, Assert.Null);
    }

    [Fact]
    public void Parse_and_ToQueryString_round_trip_all_fields()
    {
        var built = OverviewFilter.Default
            .WithSort(IncidentSort.Recent)
            .ToggleTier(EvidenceTier.Campaign)
            .ToggleTier(EvidenceTier.Contested)
            .ToggleSignal("reinserted_after_revert")
            .ToggleSignal("citation-removed")
            .WithArticle(12345)
            .WithYears(2014, 2016);

        Assert.False(built.IsDefault);
        Assert.Equal("?sort=recent&tier=2,3&signal=citation_removed,reinserted&article=12345&from=2014&to=2016", built.ToQueryString());

        var reparsed = ReparseFromQueryString(built.ToQueryString());
        Assert.Equal(built, reparsed);
        Assert.Equal(built.GetHashCode(), reparsed.GetHashCode());
        Assert.Equal(built.ToQueryString(), reparsed.ToQueryString());

        var longestLive = OverviewFilter.Default.WithSort(IncidentSort.LongestLive);
        Assert.Equal("?sort=longest-live", longestLive.ToQueryString());
        Assert.Equal(longestLive, ReparseFromQueryString(longestLive.ToQueryString()));
    }

    [Fact]
    public void Parse_is_lenient_and_ignores_junk_values()
    {
        var parsed = OverviewFilter.Parse(
            sort: "not-a-sort",
            tier: "0, 4, -1, banana, 2, 2",
            signal: " , ???, <bad>, citation-removed ",
            article: -42,
            from: 0,
            to: 12000);

        Assert.Equal(IncidentSort.Relevance, parsed.Sort);
        Assert.Equal([EvidenceTier.Contested], parsed.Tiers);
        Assert.Equal(["citation_removed"], parsed.Signals);
        Assert.Null(parsed.ArticleId);
        Assert.Null(parsed.FromYear);
        Assert.Null(parsed.ToYear);

        var allJunk = OverviewFilter.Parse("bogus", "99,foo", " , !!! ", 0, -5, 0);
        Assert.True(allJunk.IsDefault);
        Assert.Equal("", allJunk.ToQueryString());
    }

    [Fact]
    public void Parse_and_WithYears_order_inverted_year_ranges()
    {
        var parsed = OverviewFilter.Parse(null, null, null, null, from: 2018, to: 2014);
        Assert.Equal(2014, parsed.FromYear);
        Assert.Equal(2018, parsed.ToYear);

        var updated = OverviewFilter.Default.WithYears(2020, 2015);
        Assert.Equal(2015, updated.FromYear);
        Assert.Equal(2020, updated.ToYear);
    }

    [Fact]
    public void Toggle_methods_add_and_remove_items_back_to_default()
    {
        var filter = OverviewFilter.Default
            .ToggleTier(EvidenceTier.Reverted)
            .ToggleSignal("cross_article")
            .WithArticle(999)
            .WithYears(2010, 2012)
            .WithSort(IncidentSort.Recent);

        Assert.False(filter.IsDefault);
        Assert.Contains("multiple_articles", filter.Signals);

        var cleared = filter
            .ToggleTier(EvidenceTier.Reverted)
            .ToggleSignal("multiple_articles")
            .WithArticle(null)
            .WithYears(null, null)
            .WithSort(IncidentSort.Relevance);

        Assert.True(cleared.IsDefault);
        Assert.Equal(OverviewFilter.Default, cleared);
        Assert.Equal("", cleared.ToQueryString());
    }

    [Fact]
    public void Apply_filters_by_tier_signal_article_and_overlapping_years()
    {
        var inc1 = MakeIncident(
            id: "inc-1",
            rank: 1,
            tier: EvidenceTier.Campaign,
            articles: [new ArticleRef(100, "Talvisota", "Winter War"), new ArticleRef(200, "Mainilan laukaukset", "Shelling of Mainila")],
            firstSeen: Utc(2014, 3, 3),
            lastRemoved: Utc(2016, 1, 9),
            totalLive: TimeSpan.FromDays(61),
            signals: ["reinserted", "citation_removed"]);

        var inc2 = MakeIncident(
            id: "inc-2",
            rank: 2,
            tier: EvidenceTier.Contested,
            articles: [new ArticleRef(100, "Talvisota", "Winter War")],
            firstSeen: Utc(2009, 9, 2),
            lastRemoved: Utc(2009, 9, 21),
            totalLive: TimeSpan.FromDays(12),
            signals: ["number_changed", "removal_reason"]);

        var inc3 = MakeIncident(
            id: "inc-3",
            rank: 3,
            tier: EvidenceTier.Reverted,
            articles: [new ArticleRef(300, "Terijoen hallitus", null)],
            firstSeen: Utc(2015, 4, 14),
            lastRemoved: null,
            totalLive: TimeSpan.FromHours(6),
            signals: ["anonymous"]);

        var all = new[] { inc3, inc1, inc2 };

        // Default: all incidents in Rank asc order
        Assert.Equal(["inc-1", "inc-2", "inc-3"], Ids(OverviewState.Apply(all, OverviewFilter.Default)));

        // Tier filter
        var byTier = OverviewFilter.Default.ToggleTier(EvidenceTier.Campaign).ToggleTier(EvidenceTier.Reverted);
        Assert.Equal(["inc-1", "inc-3"], Ids(OverviewState.Apply(all, byTier)));

        // Signal filter (ANY match + alias normalization)
        var bySignal = OverviewFilter.Default.ToggleSignal("reinserted_after_revert").ToggleSignal("anonymous");
        Assert.Equal(["inc-1", "inc-3"], Ids(OverviewState.Apply(all, bySignal)));

        // Article filter (matches secondary article too)
        Assert.Equal(["inc-1"], Ids(OverviewState.Apply(all, OverviewFilter.Default.WithArticle(200))));
        Assert.Equal(["inc-1", "inc-2"], Ids(OverviewState.Apply(all, OverviewFilter.Default.WithArticle(100))));

        // Year overlap: inc1 spans [2014, 2016], inc3 has LastRemoved=null so [2015, 2015]
        Assert.Equal(["inc-1", "inc-3"], Ids(OverviewState.Apply(all, OverviewFilter.Default.WithYears(2015, 2015))));
        Assert.Equal(["inc-1"], Ids(OverviewState.Apply(all, OverviewFilter.Default.WithYears(2016, null))));
        Assert.Equal(["inc-1", "inc-2"], Ids(OverviewState.Apply(all, OverviewFilter.Default.WithYears(null, 2014))));

        // Combined filter: tier + signal + article + year range
        var combined = OverviewFilter.Default
            .ToggleTier(EvidenceTier.Campaign)
            .ToggleTier(EvidenceTier.Contested)
            .ToggleSignal("citation-removed")
            .WithArticle(100)
            .WithYears(2015, 2015);
        Assert.Equal(["inc-1"], Ids(OverviewState.Apply(all, combined)));
    }

    [Fact]
    public void Apply_sorts_by_relevance_recent_and_longest_live_breaking_ties_by_rank()
    {
        var sameDay = Utc(2015, 5, 1);
        var a = MakeIncident("a", rank: 2, tier: EvidenceTier.Contested, firstSeen: sameDay, totalLive: TimeSpan.FromDays(10));
        var b = MakeIncident("b", rank: 1, tier: EvidenceTier.Campaign, firstSeen: sameDay, totalLive: TimeSpan.FromDays(10));
        var c = MakeIncident("c", rank: 3, tier: EvidenceTier.Reverted, firstSeen: Utc(2018, 1, 1), totalLive: TimeSpan.FromDays(40));

        var input = new[] { a, c, b };

        Assert.Equal(
            ["b", "a", "c"],
            Ids(OverviewState.Apply(input, OverviewFilter.Default.WithSort(IncidentSort.Relevance))));

        // Recent: 2018 first ("c"), then tie on sameDay broken by Rank asc ("b" rank 1 before "a" rank 2)
        Assert.Equal(
            ["c", "b", "a"],
            Ids(OverviewState.Apply(input, OverviewFilter.Default.WithSort(IncidentSort.Recent))));

        // LongestLive: 40d first ("c"), then tie on 10d broken by Rank asc ("b" rank 1 before "a" rank 2)
        Assert.Equal(
            ["c", "b", "a"],
            Ids(OverviewState.Apply(input, OverviewFilter.Default.WithSort(IncidentSort.LongestLive))));
    }

    [Fact]
    public void TierCounts_and_SignalCounts_aggregate_across_incidents()
    {
        var incidents = new[]
        {
            MakeIncident("1", rank: 1, tier: EvidenceTier.Contested, signals: ["reinserted", "citation_removed"]),
            MakeIncident("2", rank: 2, tier: EvidenceTier.Contested, signals: ["reinserted_after_revert", "reinserted"]),
            MakeIncident("3", rank: 3, tier: EvidenceTier.Reverted, signals: ["anonymous"]),
        };

        var tiers = OverviewState.TierCounts(incidents);
        Assert.Equal(1, tiers[EvidenceTier.Reverted]);
        Assert.Equal(2, tiers[EvidenceTier.Contested]);
        Assert.Equal(0, tiers[EvidenceTier.Campaign]);

        var emptyTiers = OverviewState.TierCounts([]);
        Assert.Equal(0, emptyTiers[EvidenceTier.Reverted]);
        Assert.Equal(0, emptyTiers[EvidenceTier.Contested]);
        Assert.Equal(0, emptyTiers[EvidenceTier.Campaign]);

        var signals = OverviewState.SignalCounts(incidents);
        Assert.Equal(
            [("reinserted", 2), ("anonymous", 1), ("citation_removed", 1)],
            signals.Select(s => (s.Phrase.Id, s.Count)));
    }

    [Theory]
    [InlineData(0, "under an hour")]
    [InlineData(59 * 60, "under an hour")]
    [InlineData(3600, "1 hour")]
    [InlineData(6 * 3600, "6 hours")]
    [InlineData(23 * 3600 + 1800, "23 hours")]
    [InlineData(86400, "1 day")]
    [InlineData(61 * 86400L, "61 days")]
    [InlineData(364 * 86400L, "364 days")]
    [InlineData(365 * 86400L, "1 year")]
    [InlineData((365 + 30) * 86400L, "1 year 1 month")]
    [InlineData((2 * 365) * 86400L, "2 years")]
    [InlineData((2 * 365 + 3 * 30) * 86400L, "2 years 3 months")]
    [InlineData((365 + 364) * 86400L, "1 year 11 months")]
    public void Duration_formats_human_spans_and_passes_copy_rules(long seconds, string expected)
    {
        var actual = OverviewState.Duration(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, actual);
        Assert.Empty(CopyRules.FindViolations(actual));
    }

    [Fact]
    public void WhatHappened_formats_plural_singular_and_insert_only_span_passing_copy_rules()
    {
        var firstInsert = Utc(2014, 3, 3);
        var lastInsert = firstInsert.AddDays(2 * 365 + 5);
        var muchLaterRemoval = lastInsert.AddDays(500);

        var multi = MakeIncident(
            id: "multi",
            rank: 1,
            tier: EvidenceTier.Campaign,
            firstSeen: firstInsert,
            lastRemoved: muchLaterRemoval,
            totalLive: TimeSpan.FromDays(61),
            accountCount: 3,
            insertCount: 4,
            edits:
            [
                MakeEdit(1, firstInsert, EditAction.Inserted),
                MakeEdit(2, firstInsert.AddDays(4), EditAction.Removed),
                MakeEdit(3, firstInsert.AddDays(100), EditAction.Reinserted),
                MakeEdit(4, lastInsert, EditAction.Reinserted),
                MakeEdit(5, muchLaterRemoval, EditAction.Removed),
            ]);

        var multiText = OverviewState.WhatHappened(multi);
        Assert.Equal("Inserted 4 times by 3 accounts over 2 years; live for 61 days in total.", multiText);
        Assert.Empty(CopyRules.FindViolations(multiText));

        var single = MakeIncident(
            id: "single",
            rank: 2,
            tier: EvidenceTier.Reverted,
            firstSeen: firstInsert,
            lastRemoved: firstInsert.AddHours(6),
            totalLive: TimeSpan.FromHours(6),
            accountCount: 1,
            insertCount: 1,
            edits:
            [
                MakeEdit(10, firstInsert, EditAction.Inserted),
                MakeEdit(11, firstInsert.AddHours(6), EditAction.Removed),
            ]);

        var singleText = OverviewState.WhatHappened(single);
        Assert.Equal("Inserted once by 1 account; live for 6 hours.", singleText);
        Assert.Empty(CopyRules.FindViolations(singleText));

        var rapid = MakeIncident(
            id: "rapid",
            rank: 3,
            tier: EvidenceTier.Contested,
            firstSeen: firstInsert,
            lastRemoved: firstInsert.AddMinutes(40),
            totalLive: TimeSpan.FromMinutes(30),
            accountCount: 1,
            insertCount: 2,
            edits:
            [
                MakeEdit(20, firstInsert, EditAction.Inserted),
                MakeEdit(21, firstInsert.AddMinutes(20), EditAction.Reinserted),
                MakeEdit(22, firstInsert.AddMinutes(40), EditAction.Removed),
            ]);

        var rapidText = OverviewState.WhatHappened(rapid);
        Assert.Equal("Inserted 2 times by 1 account in under an hour; live for under an hour in total.", rapidText);
        Assert.DoesNotContain("over under", rapidText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(CopyRules.FindViolations(rapidText));
    }

    private static OverviewFilter ReparseFromQueryString(string queryString)
    {
        var parsed = QueryHelpers.ParseQuery(queryString);
        string? Get(string key) => parsed.TryGetValue(key, out var v) ? v.ToString() : null;
        long? GetLong(string key) => long.TryParse(Get(key), CultureInfo.InvariantCulture, out var n) ? n : null;
        int? GetInt(string key) => int.TryParse(Get(key), CultureInfo.InvariantCulture, out var n) ? n : null;

        return OverviewFilter.Parse(
            Get("sort"),
            Get("tier"),
            Get("signal"),
            GetLong("article"),
            GetInt("from"),
            GetInt("to"));
    }

    private static string[] Ids(IEnumerable<IncidentView> views) => views.Select(v => v.Id).ToArray();

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static EditView MakeEdit(long revId, DateTimeOffset ts, EditAction action) =>
        new(revId, revId - 1, 100, ts, EditorDisplay.For("TestUser"), null, action, null);

    private static IncidentView MakeIncident(
        string id,
        int rank,
        EvidenceTier tier,
        IReadOnlyList<ArticleRef>? articles = null,
        DateTimeOffset? firstSeen = null,
        DateTimeOffset? lastRemoved = null,
        TimeSpan? totalLive = null,
        int accountCount = 1,
        int insertCount = 1,
        IReadOnlyList<string>? signals = null,
        IReadOnlyList<EditView>? edits = null)
    {
        var fs = firstSeen ?? Utc(2015, 1, 1);
        var art = articles ?? [new ArticleRef(100, "Talvisota", "Winter War")];
        var sigs = (signals ?? []).Select(s => new SignalView(SignalCatalog.For(s), null)).ToList();
        var record = new IncidentRecord
        {
            Id = id,
            PageId = art[0].PageId,
            InsertingRevIds = [1],
            RemovingRevIds = [2],
            Tier = tier,
            Score = 10,
        };

        return new IncidentView(
            Id: id,
            Rank: rank,
            Tier: tier,
            TierReason: EvidenceTiers.Get(tier).Label,
            ClaimEn: "Claim " + id,
            ClaimOriginal: "Väite " + id,
            Lang: "fi",
            Kind: IncidentKind.Insertion,
            Articles: art,
            FirstSeen: fs,
            LastRemoved: lastRemoved,
            TotalLive: totalLive ?? TimeSpan.FromDays(1),
            AccountCount: accountCount,
            InsertCount: insertCount,
            Signals: sigs,
            Edits: edits ?? [],
            Passage: null,
            Assessment: null,
            Relevance: 1.0,
            ClusterId: null,
            Source: record);
    }
}
