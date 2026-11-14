using System.Globalization;
using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class ArticleTimelineTests : BunitContext
{
    private static ArticleAggregates SampleAggregates() =>
        new(
            PageId: 1256,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 45,
            FirstEdit: new DateTime(2014, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2020, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            Months:
            [
                new MonthBucket(2014, 1, NamedEdits: 6, AnonEdits: 2, Removals: 0, MaxBytes: 10_000),
                new MonthBucket(2014, 3, NamedEdits: 12, AnonEdits: 3, Removals: 2, MaxBytes: 14_000),
                new MonthBucket(2014, 6, NamedEdits: 24, AnonEdits: 6, Removals: 5, MaxBytes: 20_000),
                new MonthBucket(2020, 6, NamedEdits: 4, AnonEdits: 1, Removals: 0, MaxBytes: 25_000),
            ]);

    [Fact]
    public void Renders_bar_groups_proportional_heights_size_lane_and_tooltip()
    {
        var data = SampleAggregates();

        var cut = Render<ArticleTimeline>(p => p
            .Add(x => x.Data, data));

        var svg = cut.Find("[data-testid=article-timeline]");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Most contested period: Mar 2014 \u2013 Jun 2014 (7 removals).", svg.GetAttribute("aria-label"));

        var bars = cut.FindAll("[data-testid=timeline-bar]");
        Assert.Equal(4, bars.Count);

        // Tooltip text matches spec format
        var marTooltip = bars[1].QuerySelector("title");
        Assert.NotNull(marTooltip);
        Assert.Equal("Mar 2014: 12 named, 3 anonymous edits, 2 removed", marTooltip.TextContent);

        // Bar 2 (Jun 2014: 24 named, 6 anon = 30 max) vs Bar 1 (Mar 2014: 12 named, 3 anon = 15) -> 2:1 proportional height
        double marNamedH = ParseDouble(bars[1].QuerySelector("rect.named")!.GetAttribute("height")!);
        double marAnonH = ParseDouble(bars[1].QuerySelector("rect.anon")!.GetAttribute("height")!);
        double junNamedH = ParseDouble(bars[2].QuerySelector("rect.named")!.GetAttribute("height")!);
        double junAnonH = ParseDouble(bars[2].QuerySelector("rect.anon")!.GetAttribute("height")!);

        Assert.Equal(junNamedH, marNamedH * 2, precision: 2);
        Assert.Equal(junAnonH, marAnonH * 2, precision: 2);
        Assert.Equal(marNamedH, marAnonH * 4, precision: 2);

        // Size lane and removal ticks render
        Assert.NotNull(cut.Find("[data-testid=size-lane]"));
        Assert.Equal(2, cut.FindAll("[data-testid=removal-tick]").Count);
    }

    [Fact]
    public void Spark_mode_has_bars_only_and_no_text_or_axes_or_presets()
    {
        var data = SampleAggregates();

        var cut = Render<ArticleTimeline>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.Spark, true));

        var svg = cut.Find("[data-testid=article-timeline]");
        Assert.Contains("aot-spark", svg.GetAttribute("class"));
        Assert.Equal("20", svg.GetAttribute("height"));
        Assert.Equal(4, cut.FindAll("[data-testid=timeline-bar]").Count);

        Assert.Empty(cut.FindAll("text"));
        Assert.Empty(cut.FindAll(".grid"));
        Assert.Empty(cut.FindAll("[data-testid=size-lane]"));
        Assert.Empty(cut.FindAll("[data-testid=range-preset]"));
    }

    [Fact]
    public void Marks_render_with_tier_classes_and_optional_highlight()
    {
        var data = SampleAggregates();
        IReadOnlyList<IncidentMark> marks =
        [
            new IncidentMark(new DateTime(2014, 3, 15, 0, 0, 0, DateTimeKind.Utc), EvidenceTier.Reverted, "inc_1", "/investigate/winter-war/incidents/inc_1"),
            new IncidentMark(new DateTime(2014, 6, 10, 0, 0, 0, DateTimeKind.Utc), EvidenceTier.Contested, "inc_2", ""),
            new IncidentMark(new DateTime(2020, 6, 2, 0, 0, 0, DateTimeKind.Utc), EvidenceTier.Campaign, "inc_3", "/investigate/winter-war/incidents/inc_3"),
        ];

        var cut = Render<ArticleTimeline>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.Marks, marks)
            .Add(x => x.HighlightedIncidentId, "inc_3"));

        var markNodes = cut.FindAll("[data-testid=timeline-mark]");
        Assert.Equal(3, markNodes.Count);

        Assert.NotNull(markNodes[0].QuerySelector("circle.aot-mk.aot-mk--reverted"));
        Assert.Equal("/investigate/winter-war/incidents/inc_1", markNodes[0].GetAttribute("href"));

        Assert.NotNull(markNodes[1].QuerySelector("rect.aot-mk.aot-mk--contested"));

        var campaignShape = markNodes[2].QuerySelector("rect.aot-mk.aot-mk--campaign");
        Assert.NotNull(campaignShape);
        Assert.Contains("is-hot", campaignShape.GetAttribute("class"));
    }

    [Fact]
    public void Clicking_bar_selects_month_and_second_or_shift_click_extends_range()
    {
        var data = SampleAggregates();
        DateRange? selected = null;

        var cut = Render<ArticleTimeline>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.OnRangeSelected, EventCallback.Factory.Create<DateRange>(this, r => selected = r)));

        // First click on Mar 2014 selects that single month
        cut.FindAll("[data-testid=timeline-bar]")[1].Click();
        Assert.Equal(
            new DateRange(
                new DateTime(2014, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2014, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            selected);

        // Second click on Jun 2014 extends the range to Mar 2014 – Jun 2014 (exclusive Jul 2014)
        cut.FindAll("[data-testid=timeline-bar]")[2].Click();
        Assert.Equal(
            new DateRange(
                new DateTime(2014, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2014, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            selected);

        // Shift-click on Jan 2014 extends backwards from anchor (Mar 2014) to Jan 2014 – Mar 2014
        cut.FindAll("[data-testid=timeline-bar]")[0].Click(new MouseEventArgs { ShiftKey = true });
        Assert.Equal(
            new DateRange(
                new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2014, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            selected);
    }

    [Fact]
    public void Presets_fire_expected_ranges()
    {
        var data = SampleAggregates();
        var fired = new List<DateRange>();

        var cut = Render<ArticleTimeline>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.OnRangeSelected, EventCallback.Factory.Create<DateRange>(this, r => fired.Add(r))));

        Assert.Equal(3, cut.FindAll("[data-testid=range-preset]").Count);

        // All: Jan 2014 -> Jul 2020
        cut.FindAll("[data-testid=range-preset]")[0].Click();
        Assert.Equal(
            new DateRange(
                new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            fired[0]);

        // Most contested: Mar 2014 -> Jul 2014
        cut.FindAll("[data-testid=range-preset]")[1].Click();
        Assert.Equal(
            new DateRange(
                new DateTime(2014, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2014, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            fired[1]);

        // Last 5 years: Jul 2015 -> Jul 2020
        cut.FindAll("[data-testid=range-preset]")[2].Click();
        Assert.Equal(
            new DateRange(
                new DateTime(2015, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            fired[2]);
    }

    private static double ParseDouble(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
