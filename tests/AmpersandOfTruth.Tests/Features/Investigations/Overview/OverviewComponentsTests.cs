using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class OverviewComponentsTests : BunitContext
{
    [Fact]
    public void StageTracker_renders_four_stages_slow_message_cancel_and_collapsed_done_banner()
    {
        var clock = new FakeTimeProvider();
        var progress = new RunProgress(clock);
        progress.Start();

        var cancelled = false;
        var rerun = false;

        clock.Advance(TimeSpan.FromSeconds(21));

        var cut = Render<StageTracker>(p => p
            .Add(x => x.Progress, progress)
            .Add(x => x.OnCancel, () => cancelled = true)
            .Add(x => x.OnRerun, () => rerun = true));

        var tracker = cut.Find("[data-testid=stage-tracker]");
        Assert.Equal("status", tracker.GetAttribute("role"));
        Assert.Equal("polite", tracker.GetAttribute("aria-live"));

        Assert.Contains("Finding articles", cut.Find("[data-testid=stage-1]").TextContent);
        Assert.Contains("Still working…", cut.Find("[data-testid=stage-1]").TextContent);
        Assert.Contains("Scanning edit history", cut.Find("[data-testid=stage-2]").TextContent);
        Assert.Contains("Reading suspicious edits", cut.Find("[data-testid=stage-3]").TextContent);
        Assert.Contains("Assembling incidents", cut.Find("[data-testid=stage-4]").TextContent);

        cut.Find("[data-testid=cancel-run]").Click();
        Assert.True(cancelled);

        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        progress.Apply(new ArticlesFound(1, [article]));
        progress.Apply(new RevisionsScanned(9214, []));
        progress.Apply(new Completed(FromCache: true, [], TimeSpan.FromSeconds(38)));
        cut.Render();

        var done = cut.Find("[data-testid=stage-tracker-done]");
        Assert.Contains("Analysed in 38 s", done.TextContent);
        Assert.Contains("loaded from previous analysis", done.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=cancel-run]"));

        cut.Find("[data-testid=rerun-link]").Click();
        Assert.True(rerun);
    }

    [Fact]
    public void ArticleRailItem_renders_fi_with_sparkline_and_non_fi_muted_note()
    {
        var fiArticle = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        var spark = new ArticleAggregates(
            PageId: 1150,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 100,
            FirstEdit: new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Months: [new MonthBucket(2014, 3, NamedEdits: 10, AnonEdits: 2, Removals: 3, MaxBytes: 12000)]);

        var selectedClicked = false;
        var fiCut = Render<ArticleRailItem>(p => p
            .Add(x => x.Article, fiArticle)
            .Add(x => x.TitleEn, "Winter War")
            .Add(x => x.IncidentCount, 3)
            .Add(x => x.TimelineData, spark)
            .Add(x => x.OnSelectArticle, () => selectedClicked = true));

        var titleLink = fiCut.Find("a.aot-art__title");
        Assert.Equal("/articles/1150", titleLink.GetAttribute("href"));
        Assert.Equal("Winter War", titleLink.TextContent);
        Assert.Contains("Talvisota", fiCut.Markup);
        Assert.Contains("similar text", fiCut.Markup);
        Assert.NotNull(fiCut.Find("[data-testid=article-timeline]"));

        fiCut.Find("[data-testid=article-rail-count]").Click();
        Assert.True(selectedClicked);

        var enArticle = new TopicArticle("en", 9999, "Winter War", 0.04);
        var enCut = Render<ArticleRailItem>(p => p.Add(x => x.Article, enArticle));
        Assert.Contains("no edit history in our data", enCut.Find("[data-testid=article-rail-item]").TextContent);
        Assert.Empty(enCut.FindAll("a.aot-art__title"));
    }

    [Fact]
    public void TimelineStrip_renders_axis_top_five_lanes_plus_other_hot_mark_and_short_tier_badges()
    {
        var incidents = Enumerable.Range(1, 6)
            .Select(idx => MakeIncident(
                id: $"inc-{idx}",
                rank: idx,
                pageId: 1000 + idx,
                titleFi: $"Artikkeli {idx}",
                titleEn: $"Article {idx}",
                tier: idx == 1 ? EvidenceTier.Campaign : idx == 2 ? EvidenceTier.Contested : EvidenceTier.Reverted,
                year: 2010 + idx))
            .ToList();

        string? clickedId = null;
        (int From, int To)? selectedRange = null;

        var cut = Render<TimelineStrip>(p => p
            .Add(x => x.Incidents, incidents)
            .Add(x => x.HotId, "inc-2")
            .Add(x => x.OnMarkClick, id => clickedId = id)
            .Add(x => x.OnRangeSelected, r => selectedRange = r));

        Assert.Contains("2003", cut.Markup);
        Assert.Contains("2026", cut.Markup);
        Assert.Contains("Article 1", cut.Markup);
        Assert.Contains("Article 5", cut.Markup);
        Assert.Contains("other", cut.Markup);
        Assert.DoesNotContain("Article 6", cut.Markup);

        var marks = cut.FindAll("[data-testid=timeline-mark]");
        Assert.Equal(6, marks.Count);
        var hotMark = cut.Find("[data-testid=timeline-mark][data-incident=inc-2]");
        Assert.Contains("is-hot", hotMark.ClassList);

        // Legend uses 3 TierBadge Short components
        Assert.Equal(3, cut.FindAll(".aot-legend [data-testid=tier-badge]").Count);

        hotMark.Click();
        Assert.Equal("inc-2", clickedId);

        cut.Find("[data-testid=timeline-year][data-year='2014']").Click();
        Assert.Equal((2014, 2014), selectedRange);
    }

    [Fact]
    public void UnflaggedList_masks_ip_in_editor_and_summary_and_links_to_article_and_diff()
    {
        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        var candidate = new RemovedEdit(
            article,
            new RevertEvent(
                PageId: 1150,
                RevId: 5002,
                Timestamp: new DateTime(2015, 3, 7, 14, 2, 0, DateTimeKind.Utc),
                User: "Reviewer",
                Comment: "Kumottu muokkaus, jonka teki [[Special:Contributions/85.76.123.45|85.76.123.45]]",
                RestoredRevId: 5000,
                RevertedCount: 1,
                RevertedRevId: 5001,
                RevertedUser: "85.76.123.45"));

        var cut = Render<UnflaggedList>(p => p.Add(x => x.Items, [candidate]));

        var details = cut.Find("[data-testid=unflagged-toggle]");
        Assert.Contains("Removed edits we did not flag (1)", details.QuerySelector("summary")!.TextContent);
        Assert.DoesNotContain("85.76.123.45", cut.Markup);
        var editorLabel = cut.Find("[data-testid=editor-label]");
        Assert.Contains("85.76.x.x", editorLabel.TextContent);
        Assert.Contains("(anonymous)", editorLabel.TextContent);
        Assert.Contains("Kumottu muokkaus, jonka teki 85.76.x.x", cut.Markup);
        Assert.Contains("7 Mar 2015", cut.Markup);
        Assert.NotNull(cut.Find("a[href='/articles/1150']"));
        Assert.NotNull(cut.Find("a[href='https://fi.wikipedia.org/w/index.php?diff=prev&oldid=5001']"));
    }

    private static IncidentView MakeIncident(
        string id,
        int rank,
        long pageId,
        string titleFi,
        string? titleEn,
        EvidenceTier tier,
        int year)
    {
        var firstSeen = new DateTimeOffset(year, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var record = new IncidentRecord
        {
            Id = id,
            PageId = pageId,
            InsertingRevIds = [rank * 10L],
            RemovingRevIds = [(rank * 10L) + 1],
            Tier = tier,
            Score = 100 - rank,
        };

        return new IncidentView(
            Id: id,
            Rank: rank,
            Tier: tier,
            TierReason: EvidenceTiers.Get(tier).Label,
            ClaimEn: $"Claim {id}",
            ClaimOriginal: null,
            Lang: "fi",
            Kind: IncidentKind.Insertion,
            Articles: [new ArticleRef(pageId, titleFi, titleEn)],
            FirstSeen: firstSeen,
            LastRemoved: firstSeen.AddDays(3),
            TotalLive: TimeSpan.FromDays(3),
            AccountCount: 1,
            InsertCount: 1,
            Signals: [],
            Edits: [],
            Passage: null,
            Assessment: null,
            Relevance: 100 - rank,
            ClusterId: null,
            Source: record);
    }
}
