using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class MtLabelTests : BunitContext
{
    [Fact]
    public void Short_form_hides_original_until_toggled_and_carries_lang_and_notice_tooltip()
    {
        const string en = "Finland started the Winter War by shelling the village of Mainila.";
        const string fi = "Suomi aloitti talvisodan ampumalla Mainilan kylään.";

        var cut = Render<MtLabel>(p => p
            .Add(x => x.Text, en)
            .Add(x => x.Original, fi));

        var root = cut.Find("[data-testid=mt-label]");
        Assert.Contains(en, root.TextContent);

        var tag = root.QuerySelector(".aot-mt__tag");
        Assert.NotNull(tag);
        Assert.Equal("Machine translation", tag.TextContent);
        Assert.Equal(Attribution.MachineTranslationNotice, tag.GetAttribute("title"));

        Assert.Empty(cut.FindAll("[data-testid=mt-original]"));

        var toggle = cut.Find("[data-testid=mt-show-original]");
        Assert.Equal("Show original", toggle.TextContent);
        toggle.Click();

        var original = cut.Find("[data-testid=mt-original]");
        Assert.Equal("fi", original.GetAttribute("lang"));
        Assert.Equal(fi, original.TextContent);

        cut.Find("[data-testid=mt-show-original]").Click();
        Assert.Empty(cut.FindAll("[data-testid=mt-original]"));
    }

    [Fact]
    public void Stacked_form_shows_both_immediately_with_custom_lang()
    {
        var cut = Render<MtLabel>(p => p
            .Add(x => x.Text, "Corrected historical fact")
            .Add(x => x.Original, "Korrigerad historisk uppgift")
            .Add(x => x.Lang, "sv")
            .Add(x => x.Stacked, true));

        var root = cut.Find("[data-testid=mt-label]");
        Assert.Contains("Corrected historical fact", root.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=mt-show-original]"));

        var original = cut.Find("[data-testid=mt-original]");
        Assert.Equal("sv", original.GetAttribute("lang"));
        Assert.Equal("Korrigerad historisk uppgift", original.TextContent);
    }
}
