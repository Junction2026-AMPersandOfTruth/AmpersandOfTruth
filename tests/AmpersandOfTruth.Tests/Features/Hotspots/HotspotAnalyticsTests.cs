using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Hotspots;

namespace AmpersandOfTruth.Tests.Features.Hotspots;

public sealed class HotspotAnalyticsTests
{
    [Fact]
    public void ParseYearlyReverts_parses_comma_separated_counts_and_clamps_negatives()
    {
        var parsed = HotspotAnalytics.ParseYearlyReverts("0, 3, 12, -4, bad, 7");

        Assert.Equal([0, 3, 12, 0, 7], parsed);
        Assert.Empty(HotspotAnalytics.ParseYearlyReverts(null));
        Assert.Empty(HotspotAnalytics.ParseYearlyReverts("   "));
    }

    [Theory]
    [InlineData("2003-01", true, 2003, 1)]
    [InlineData("2026-12", true, 2026, 12)]
    [InlineData(" 2014-07 ", true, 2014, 7)]
    [InlineData("2014-00", false, 0, 0)]
    [InlineData("2014-13", false, 0, 0)]
    [InlineData("2014/05", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    public void TryParseMonth_validates_yyyy_mm_strings(string raw, bool expectedOk, int expectedYear, int expectedMonth)
    {
        var ok = HotspotAnalytics.TryParseMonth(raw, out var yr, out var mo);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedYear, yr);
        Assert.Equal(expectedMonth, mo);
    }

    [Fact]
    public void AggregateByYear_buckets_months_chronologically_sums_counts_and_picks_dominant_article()
    {
        var months = new List<HotMonth>
        {
            new("2015-09", Reverts: 90, Episodes: 10, Edits: 800, TopPageId: 10, TopTitleFi: "Talvisota", TopTitleEn: "Winter War"),
            new("2014-02", Reverts: 40, Episodes: 5, Edits: 300, TopPageId: 20, TopTitleFi: "Jatkosota", TopTitleEn: "Continuation War"),
            new("2014-06", Reverts: 50, Episodes: 8, Edits: 400, TopPageId: 20, TopTitleFi: "Jatkosota", TopTitleEn: "Continuation War"),
            new("2014-11", Reverts: 75, Episodes: 12, Edits: 500, TopPageId: 30, TopTitleFi: "Lapin sota", TopTitleEn: "Lapland War"),
            new("2003-01", Reverts: 0, Episodes: 0, Edits: 12, TopPageId: null, TopTitleFi: null, TopTitleEn: null),
            new("invalid", Reverts: 999, Episodes: 99, Edits: 9_999, TopPageId: 99, TopTitleFi: "Ignored", TopTitleEn: "Ignored"),
        };

        var years = HotspotAnalytics.AggregateByYear(months);

        Assert.Equal([2003, 2014, 2015], years.Select(y => y.Year).ToArray());

        var y2003 = years[0];
        Assert.Equal(0, y2003.Reverts);
        Assert.Equal(12, y2003.Edits);
        Assert.Null(y2003.TopPageId);
        Assert.Null(y2003.TopDisplayTitle);

        // In 2014, Jatkosota led two months (40 + 50 = 90 reverts) beating Lapland War's single month (75 reverts)
        var y2014 = years[1];
        Assert.Equal(165, y2014.Reverts);
        Assert.Equal(25, y2014.Episodes);
        Assert.Equal(1_200, y2014.Edits);
        Assert.Equal(20L, y2014.TopPageId);
        Assert.Equal("Continuation War", y2014.TopDisplayTitle);

        var y2015 = years[2];
        Assert.Equal(90, y2015.Reverts);
        Assert.Equal(10L, y2015.TopPageId);
        Assert.Equal("Winter War", y2015.TopDisplayTitle);
    }

    [Fact]
    public void PickTopSpikes_selects_top_five_years_with_articles_and_orders_chronologically()
    {
        var years = new List<HotYear>
        {
            new(2004, Reverts: 500, Episodes: 50, Edits: 5_000, TopPageId: null, TopTitleFi: null, TopTitleEn: null), // skipped: no top article
            new(2006, Reverts: 120, Episodes: 20, Edits: 2_000, TopPageId: 1, TopTitleFi: "A", TopTitleEn: "Article A"),
            new(2007, Reverts: 890, Episodes: 150, Edits: 14_000, TopPageId: 2, TopTitleFi: "Kalevala", TopTitleEn: "Kalevala"),
            new(2008, Reverts: 410, Episodes: 70, Edits: 6_000, TopPageId: 3, TopTitleFi: "B", TopTitleEn: "Article B"),
            new(2012, Reverts: 350, Episodes: 55, Edits: 4_000, TopPageId: 4, TopTitleFi: "C", TopTitleEn: "Article C"),
            new(2015, Reverts: 310, Episodes: 30, Edits: 3_000, TopPageId: 5, TopTitleFi: "D", TopTitleEn: null),
            new(2020, Reverts: 450, Episodes: 25, Edits: 3_500, TopPageId: 6, TopTitleFi: "E", TopTitleEn: "Article E"),
            new(2024, Reverts: 180, Episodes: 19, Edits: 2_200, TopPageId: 7, TopTitleFi: "F", TopTitleEn: "Article F"),
        };

        var spikes = HotspotAnalytics.PickTopSpikes(years, count: 5);

        Assert.Equal(5, spikes.Count);
        Assert.Equal([2007, 2008, 2012, 2015, 2020], spikes.Select(s => s.Year).ToArray());
        Assert.Equal(["Kalevala", "Article B", "Article C", "D", "Article E"], spikes.Select(s => s.DisplayTitle).ToArray());
    }

    [Fact]
    public void ActivitySummary_describes_peak_year_without_banned_jargon()
    {
        var data = FakeHotspotStore.SampleData();
        var years = HotspotAnalytics.AggregateByYear(data.Months);
        var spikes = HotspotAnalytics.PickTopSpikes(years, count: 5);

        var summary = HotspotAnalytics.ActivitySummary(years, spikes);

        Assert.Contains("2007", summary);
        Assert.Contains("893 removed edits", summary);
        Assert.Contains("156 conflict episodes", summary);
        Assert.Contains("Kalevala", summary);
        Assert.Empty(CopyRules.FindViolations(summary, includeJargon: true));
    }
}
