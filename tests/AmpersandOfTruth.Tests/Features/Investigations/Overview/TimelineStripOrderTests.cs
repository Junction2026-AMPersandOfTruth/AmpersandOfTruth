using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using Bunit;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class TimelineStripOrderTests : BunitContext
{
    [Fact]
    public void Highest_level_mark_is_drawn_last_so_it_stays_on_top_when_marks_overlap()
    {
        // Same article, same day: three overlapping marks at levels 3, 1, 2 (rank order).
        var six = FakeIncidentSource.BuildSix();
        var at = six[0].FirstSeen;
        var article = six[0].Articles;
        var incidents = new[]
        {
            six[0] with { Id = "high", Rank = 1, Tier = EvidenceTier.Campaign, FirstSeen = at, Articles = article },
            six[1] with { Id = "low", Rank = 2, Tier = EvidenceTier.Reverted, FirstSeen = at, Articles = article },
            six[2] with { Id = "mid", Rank = 3, Tier = EvidenceTier.Contested, FirstSeen = at, Articles = article },
        };

        var cut = Render<TimelineStrip>(p => p.Add(x => x.Incidents, incidents));

        var order = cut.FindAll("[data-testid=timeline-mark]").Select(m => m.GetAttribute("data-incident")).ToList();
        Assert.Equal(["low", "mid", "high"], order);
    }

    [Fact]
    public void Hovered_mark_is_drawn_on_top_of_everything()
    {
        var six = FakeIncidentSource.BuildSix();
        var at = six[0].FirstSeen;
        var article = six[0].Articles;
        var incidents = new[]
        {
            six[0] with { Id = "high", Rank = 1, Tier = EvidenceTier.Campaign, FirstSeen = at, Articles = article },
            six[1] with { Id = "low", Rank = 2, Tier = EvidenceTier.Reverted, FirstSeen = at, Articles = article },
        };

        var cut = Render<TimelineStrip>(p => p.Add(x => x.Incidents, incidents).Add(x => x.HotId, "low"));

        Assert.Equal("low", cut.FindAll("[data-testid=timeline-mark]")[^1].GetAttribute("data-incident"));
    }
}
