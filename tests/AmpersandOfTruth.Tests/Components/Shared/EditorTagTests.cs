using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class EditorTagTests : BunitContext
{
    [Fact]
    public void Named_user_renders_link_to_fi_wikipedia_contributions_in_new_tab()
    {
        var cut = Render<EditorTag>(p => p.Add(x => x.User, "Pohjantähti77"));

        var root = cut.Find("[data-testid=editor-label]");
        Assert.DoesNotContain("aot-editor--anon", root.ClassList);
        Assert.Equal("P", root.QuerySelector(".aot-editor__glyph")?.TextContent);

        var link = root.QuerySelector("a.aot-editor__name");
        Assert.NotNull(link);
        Assert.Equal("Pohjantähti77", link.TextContent);
        Assert.Equal("https://fi.wikipedia.org/wiki/Special:Contributions/Pohjant%C3%A4hti77", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener", link.GetAttribute("rel"));
    }

    [Fact]
    public void Ipv4_masks_address_renders_no_link_and_never_leaks_raw_ip()
    {
        var cut = Render<EditorTag>(p => p.Add(x => x.User, "85.76.12.34"));

        var root = cut.Find("[data-testid=editor-label]");
        Assert.Contains("aot-editor--anon", root.ClassList);
        Assert.Null(root.QuerySelector("a"));
        Assert.Contains("85.76.x.x", root.TextContent);
        Assert.Contains("(anonymous)", root.TextContent);
        Assert.DoesNotContain("85.76.12.34", cut.Markup);
        Assert.DoesNotContain("12.34", cut.Markup);
    }

    [Fact]
    public void Temporary_and_hidden_accounts_render_without_link_and_Label_wins_over_User()
    {
        var tempCut = Render<EditorTag>(p => p.Add(x => x.User, "~2025-26183-73"));
        var tempRoot = tempCut.Find("[data-testid=editor-label]");
        Assert.Contains("aot-editor--anon", tempRoot.ClassList);
        Assert.Null(tempRoot.QuerySelector("a"));
        Assert.Contains("~2025-26183-73", tempRoot.TextContent);
        Assert.Contains("(temporary account)", tempRoot.TextContent);

        var precomputed = EditorDisplay.For("Kumoaja");
        var overrideCut = Render<EditorTag>(p => p
            .Add(x => x.User, "85.76.12.34")
            .Add(x => x.Label, precomputed));

        var link = overrideCut.Find("[data-testid=editor-label] a.aot-editor__name");
        Assert.Equal("Kumoaja", link.TextContent);
        Assert.DoesNotContain("85.76", overrideCut.Markup);
    }
}
