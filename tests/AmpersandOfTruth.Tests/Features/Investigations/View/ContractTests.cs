using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public class ContractTests
{
    [Theory]
    [InlineData("Winter War", "winter-war")]
    [InlineData("  Suomen sisällissota 1918 ", "suomen-sisällissota-1918")]
    [InlineData("C. G. E. Mannerheim", "c-g-e-mannerheim")]
    [InlineData("!!!", "")]
    public void TopicSlug_From(string topic, string slug) => Assert.Equal(slug, TopicSlug.From(topic));

    [Fact]
    public void TopicSlug_ToQuery_and_Route() 
    {
        Assert.Equal("winter war", TopicSlug.ToQuery("winter-war"));
        Assert.Equal("/investigate/winter-war", TopicSlug.Route("Winter War"));
    }

    [Fact]
    public void Every_tier_reason_kind_has_a_known_signal_phrase()
    {
        foreach (var kind in Enum.GetValues<TierReasonKind>())
            Assert.True(SignalCatalog.For(SignalCatalog.IdOf(kind)).IsKnown, kind.ToString());
    }

    [Theory]
    [InlineData("reinserted_after_revert", "reinserted")]
    [InlineData("cross_article", "multiple_articles")]
    [InlineData("Removal-Reason", "removal_reason")]
    public void Aliases_resolve(string id, string canonical) => Assert.Equal(canonical, SignalCatalog.For(id).Id);

    [Fact]
    public void Unknown_signal_falls_back_to_label_without_throwing()
    {
        var p = SignalCatalog.For("weird_new_thing", "Weird thing");
        Assert.False(p.IsKnown);
        Assert.Equal("Weird thing", p.Chip);
        Assert.Equal("Weird new thing", SignalCatalog.For("weird_new_thing").Chip);
    }

    [Fact]
    public void Signal_wording_passes_the_copy_rules()
    {
        foreach (var p in SignalCatalog.All)
        {
            Assert.Empty(CopyRules.FindViolations(p.Chip));
            Assert.Empty(CopyRules.FindViolations(p.Why));
        }
    }
}
