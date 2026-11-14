using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class TierBadgeTests : BunitContext
{
    [Theory]
    [InlineData(EvidenceTier.Reverted)]
    [InlineData(EvidenceTier.Contested)]
    [InlineData(EvidenceTier.Campaign)]
    public void Each_tier_shows_exact_EvidenceTiers_label_meaning_and_level_pips(EvidenceTier tier)
    {
        var info = EvidenceTiers.Get(tier);

        var cut = Render<TierBadge>(p => p.Add(x => x.Tier, tier));

        var badge = cut.Find("[data-testid=tier-badge]");
        Assert.Equal(info.Label, badge.TextContent);
        Assert.Equal(info.Id, badge.GetAttribute("data-tier"));
        Assert.Equal(info.Meaning, badge.GetAttribute("title"));
        Assert.Contains($"aot-tier--{info.Id}", badge.ClassList);

        var pips = badge.QuerySelectorAll(".aot-tier__pips i");
        Assert.Equal(3, pips.Length);
        Assert.Equal(info.Level, badge.QuerySelectorAll(".aot-tier__pips i.on").Length);
        Assert.Empty(cut.FindAll("[data-testid=tier-why]"));
        Assert.Empty(CopyRules.FindViolations(badge.TextContent, includeJargon: true));
    }

    [Theory]
    [InlineData(EvidenceTier.Reverted)]
    [InlineData(EvidenceTier.Contested)]
    [InlineData(EvidenceTier.Campaign)]
    public void Short_uses_ShortLabel(EvidenceTier tier)
    {
        var info = EvidenceTiers.Get(tier);

        var cut = Render<TierBadge>(p => p
            .Add(x => x.Tier, tier)
            .Add(x => x.Short, true));

        var badge = cut.Find("[data-testid=tier-badge]");
        Assert.Equal(info.ShortLabel, badge.TextContent);
    }

    [Fact]
    public void ShowWhy_renders_why_link_with_default_and_custom_href()
    {
        var defaultCut = Render<TierBadge>(p => p
            .Add(x => x.Tier, EvidenceTier.Campaign)
            .Add(x => x.ShowWhy, true));

        var defaultLink = defaultCut.Find("[data-testid=tier-why]");
        Assert.Equal("why this level?", defaultLink.TextContent);
        Assert.Equal("/methodology#levels", defaultLink.GetAttribute("href"));
        Assert.Empty(CopyRules.FindViolations(defaultCut.Find("[data-testid=tier-badge]").TextContent, includeJargon: true));

        var customCut = Render<TierBadge>(p => p
            .Add(x => x.Tier, EvidenceTier.Contested)
            .Add(x => x.ShowWhy, true)
            .Add(x => x.WhyHref, "#why"));

        var customLink = customCut.Find("[data-testid=tier-why]");
        Assert.Equal("#why", customLink.GetAttribute("href"));
    }
}
