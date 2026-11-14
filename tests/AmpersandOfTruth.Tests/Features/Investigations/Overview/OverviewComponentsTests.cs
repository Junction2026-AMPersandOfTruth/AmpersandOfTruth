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
        Assert.Contains("Reading removed edits", cut.Find("[data-testid=stage-3]").TextContent);
        Assert.Contains("Assembling incidents", cut.Find("[data-testid=stage-4]").TextContent);

        cut.Find("[data-testid=cancel-run]").Click();
        Assert.True(cancelled);

        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        progress.Apply(new ArticlesFound(1, [article]));
        progress.Apply(new RevisionsScanned(9214, []));
        progress.Apply(new Completed(FromCache: false, [], TimeSpan.FromSeconds(38)));
        cut.Render();

        var freshDone = cut.Find("[data-testid=stage-tracker-done]");
        Assert.Contains("Analysed in 38 s", freshDone.TextContent);
        Assert.DoesNotContain("previous analysis", freshDone.TextContent);

        progress.Apply(new Completed(FromCache: true, [], TimeSpan.Zero)
        {
            AnalysedAt = new DateTimeOffset(2026, 11, 14, 22, 45, 0, TimeSpan.Zero),
        });
        cut.Render();

        var done = cut.Find("[data-testid=stage-tracker-done]");
        Assert.DoesNotContain("Analysed in", done.TextContent);
        Assert.Contains("Loaded from previous analysis (analysed 14 Nov 2026, 22:45 UTC) · Re-run", done.TextContent);
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

        var firstMark = cut.Find("[data-testid=timeline-mark][data-incident=inc-1]");
        Assert.Equal("0", firstMark.GetAttribute("tabindex"));
        Assert.Equal("button", firstMark.GetAttribute("role"));
        firstMark.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("inc-1", clickedId);

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

    [Fact]
    public void ArticleRailItem_renders_contest_line_only_when_fi_article_has_episodes()
    {
        var fiArticle = new TopicArticle("fi", 1150, "Suomen sisällissota", 0.04);
        var contested = new PageContest(1150, EditWarEpisodes: 2, PingPongEpisodes: 3, BurstEpisodes: 4, ContestedScore: 28.1, PeakMonth: "2014-03");

        var withContest = Render<ArticleRailItem>(p => p
            .Add(x => x.Article, fiArticle)
            .Add(x => x.Contest, contested));
        Assert.Equal(
            "Contested: 5 periods of back-and-forth edits \u00b7 peak Mar 2014",
            withContest.Find("[data-testid=rail-contest]").TextContent.Trim());

        var zeroEpisodes = Render<ArticleRailItem>(p => p
            .Add(x => x.Article, fiArticle)
            .Add(x => x.Contest, new PageContest(1150, 0, 0, 0, 1.2, "2014-03")));
        Assert.Empty(zeroEpisodes.FindAll("[data-testid=rail-contest]"));

        var enArticle = new TopicArticle("en", 9999, "Finnish Civil War", 0.04);
        var nonFi = Render<ArticleRailItem>(p => p
            .Add(x => x.Article, enArticle)
            .Add(x => x.Contest, contested));
        Assert.Empty(nonFi.FindAll("[data-testid=rail-contest]"));
    }

    [Fact]
    public void ArticleRailItem_uses_aggregates_TitleEn_fallback_and_renders_finnish_wikipedia_tag_when_unknown()
    {
        var fiArticle = new TopicArticle("fi", 1150, "Talvisota", 0.04);
        var aggregates = new ArticleAggregates(
            PageId: 1150,
            TitleFi: "Talvisota",
            TitleEn: "Winter War",
            Revisions: 100,
            FirstEdit: new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastEdit: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Months: [new MonthBucket(2014, 3, NamedEdits: 10, AnonEdits: 2, Removals: 3, MaxBytes: 12000)]);

        var withAggEn = Render<ArticleRailItem>(p => p
            .Add(x => x.Article, fiArticle)
            .Add(x => x.TimelineData, aggregates));
        Assert.Equal("Winter War", withAggEn.Find(".aot-art__title").TextContent);
        Assert.Equal("Talvisota", withAggEn.Find(".aot-art__why span[lang=fi]").TextContent);
        Assert.Empty(withAggEn.FindAll("[data-testid=rail-fi-tag]"));

        var withoutEn = Render<ArticleRailItem>(p => p.Add(x => x.Article, fiArticle));
        Assert.Equal("Talvisota", withoutEn.Find(".aot-art__title").TextContent);
        Assert.Equal("fi", withoutEn.Find(".aot-art__title").GetAttribute("lang"));
        Assert.Equal("Finnish Wikipedia", withoutEn.Find("[data-testid=rail-fi-tag]").TextContent);
    }

    [Fact]
    public void TimelineStrip_renders_phone_svg_with_stacked_lanes_fewer_ticks_and_12px_text_and_full_legend_aria()
    {
        var incidents = Enumerable.Range(1, 5)
            .Select(idx => MakeIncident(
                id: $"inc-{idx}",
                rank: idx,
                pageId: 2000 + idx,
                titleFi: $"Artikkeli {idx}",
                titleEn: $"Article {idx}",
                tier: idx == 1 ? EvidenceTier.Campaign : idx == 2 ? EvidenceTier.Contested : EvidenceTier.Reverted,
                year: 2012 + idx))
            .ToList();

        string? clickedId = null;
        (int From, int To)? selectedRange = null;

        var cut = Render<TimelineStrip>(p => p
            .Add(x => x.Incidents, incidents)
            .Add(x => x.HotId, "inc-1")
            .Add(x => x.OnMarkClick, id => clickedId = id)
            .Add(x => x.OnRangeSelected, r => selectedRange = r));

        var phoneSvg = cut.Find("[data-testid=timeline-svg-phone]");
        Assert.StartsWith("0 0 320 ", phoneSvg.GetAttribute("viewBox"), StringComparison.Ordinal);

        var phoneTexts = phoneSvg.QuerySelectorAll("text");
        Assert.NotEmpty(phoneTexts);
        Assert.All(phoneTexts, t => Assert.Equal("12", t.GetAttribute("font-size")));

        var textValues = phoneTexts.Select(static t => t.TextContent).ToList();
        Assert.Contains("2003", textValues);
        Assert.Contains("2009", textValues);
        Assert.Contains("2015", textValues);
        Assert.Contains("2021", textValues);
        Assert.Contains("2026", textValues);
        Assert.DoesNotContain("2006", textValues);
        Assert.Contains("Article 1", textValues);
        Assert.Contains("Article 3", textValues);
        Assert.Contains("other", textValues);
        Assert.DoesNotContain("Article 4", textValues);

        var phoneMarks = cut.FindAll("[data-testid=timeline-mark-phone]");
        Assert.Equal(5, phoneMarks.Count);
        var hotPhoneMark = cut.Find("[data-testid=timeline-mark-phone][data-incident=inc-1]");
        Assert.Contains("is-hot", hotPhoneMark.ClassList);
        Assert.Equal("16", hotPhoneMark.QuerySelector("circle[fill=transparent]")?.GetAttribute("r"));

        hotPhoneMark.Click();
        Assert.Equal("inc-1", clickedId);

        cut.Find("[data-testid=timeline-year-phone][data-year='2015']").Click();
        Assert.Equal((2015, 2015), selectedRange);

        var legendBadges = cut.FindAll(".aot-legend [data-testid=tier-badge]");
        Assert.Equal(
            [
                EvidenceTiers.Reverted.Label,
                EvidenceTiers.Contested.Label,
                EvidenceTiers.Campaign.Label,
            ],
            legendBadges.Select(static b => b.GetAttribute("aria-label") ?? "").ToArray());
    }

    [Fact]
    public void UnflaggedList_renders_quiet_reason_tags_and_omits_other_and_excluded()
    {
        var article = new TopicArticle("fi", 1150, "Talvisota", 0.05);
        RemovedEdit MakeItem(long revertRevId, string? comment = "Kumottu") =>
            new(
                article,
                new RevertEvent(
                    PageId: 1150,
                    RevId: revertRevId,
                    Timestamp: new DateTime(2015, 3, 7, 14, 2, 0, DateTimeKind.Utc),
                    User: "Reviewer",
                    Comment: comment,
                    RestoredRevId: revertRevId - 2,
                    RevertedCount: 1,
                    RevertedRevId: revertRevId - 1,
                    RevertedUser: "EditorA"));

        IReadOnlyList<RemovedEdit> items =
        [
            MakeItem(1001),
            MakeItem(1002),
            MakeItem(1003),
            MakeItem(1004),
            MakeItem(1005),
            MakeItem(1006),
            MakeItem(1007),
        ];

        var classes = new Dictionary<(long PageId, long RevertingRevId), string>
        {
            [(1150, 1001)] = "graffiti_cleanup",
            [(1150, 1002)] = "self_revert",
            [(1150, 1003)] = "bot",
            [(1150, 1004)] = "dispute",
            [(1150, 1005)] = "edit_war",
            [(1150, 1006)] = "other",
            [(1150, 1007)] = "excluded",
        };

        var cut = Render<UnflaggedList>(p => p
            .Add(x => x.Items, items)
            .Add(x => x.Classes, classes)
            .Add(x => x.Open, true));

        var rows = cut.FindAll("[data-testid=unflagged-row]");
        Assert.Equal(7, rows.Count);

        Assert.Equal("vandalism cleanup", rows[0].QuerySelector("[data-testid=unflagged-reason]")?.TextContent);
        Assert.Equal("undone by the same account", rows[1].QuerySelector("[data-testid=unflagged-reason]")?.TextContent);
        Assert.Equal("bot", rows[2].QuerySelector("[data-testid=unflagged-reason]")?.TextContent);
        Assert.Equal("disputed content", rows[3].QuerySelector("[data-testid=unflagged-reason]")?.TextContent);
        Assert.Equal("back-and-forth edits", rows[4].QuerySelector("[data-testid=unflagged-reason]")?.TextContent);
        Assert.Null(rows[5].QuerySelector("[data-testid=unflagged-reason]"));
        Assert.Null(rows[6].QuerySelector("[data-testid=unflagged-reason]"));
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
