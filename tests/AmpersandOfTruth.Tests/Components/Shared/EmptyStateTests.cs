using AmpersandOfTruth.Web.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class EmptyStateTests : BunitContext
{
    [Fact]
    public void Title_Message_ChildContent_and_Actions_render_with_design_classes_and_icons()
    {
        var cut = Render<EmptyState>(p => p
            .Add(x => x.Title, "No disputed insertions found for “Lapland War”")
            .Add(x => x.Message, "Nothing met the minimum level.")
            .Add(x => x.Icon, "info")
            .AddChildContent("<ul><li>9 articles checked</li></ul>")
            .Add(x => x.Actions, (RenderFragment)(b =>
            {
                b.OpenElement(0, "button");
                b.AddAttribute(1, "type", "button");
                b.AddAttribute(2, "data-testid", "try-winter-war");
                b.AddContent(3, "Try “Winter War”");
                b.CloseElement();
            })));

        var empty = cut.Find("[data-testid=empty-state]");
        Assert.Contains("aot-empty", empty.ClassList);
        Assert.NotNull(empty.QuerySelector("svg"));
        Assert.Equal("No disputed insertions found for “Lapland War”", empty.QuerySelector("h3")?.TextContent);
        Assert.Contains("Nothing met the minimum level.", empty.TextContent);
        Assert.Contains("9 articles checked", empty.TextContent);
        Assert.Equal("Try “Winter War”", cut.Find("[data-testid=try-winter-war]").TextContent);
    }

    [Fact]
    public void Legacy_Message_only_usage_and_Icon_none_work()
    {
        var legacyCut = Render<EmptyState>(p => p.Add(x => x.Message, "No notes yet."));
        var legacyEmpty = legacyCut.Find("[data-testid=empty-state]");
        Assert.Equal("No notes yet.", legacyEmpty.TextContent.Trim());

        var noIconCut = Render<EmptyState>(p => p
            .Add(x => x.Title, "Outside dataset")
            .Add(x => x.Icon, "none"));
        Assert.Null(noIconCut.Find("[data-testid=empty-state]").QuerySelector("svg"));
    }
}
