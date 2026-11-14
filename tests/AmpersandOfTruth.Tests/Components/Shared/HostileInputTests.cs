using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class HostileInputTests : BunitContext
{
    private const string Payload = "<script>alert(1)</script>";

    [Fact]
    public void MtLabel_encodes_hostile_text_in_both_translation_and_original()
    {
        var cut = Render<MtLabel>(p => p
            .Add(x => x.Text, Payload)
            .Add(x => x.Original, Payload)
            .Add(x => x.Stacked, true));

        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);
        Assert.Equal(Payload, cut.Find("[data-testid=mt-original]").TextContent);
    }

    [Fact]
    public void EditorTag_encodes_hostile_username_without_creating_elements()
    {
        var cut = Render<EditorTag>(p => p.Add(x => x.User, Payload));

        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);
        Assert.Equal(Payload, cut.Find("[data-testid=editor-label] .aot-editor__name").TextContent);
    }

    [Fact]
    public void StatusBanner_encodes_hostile_title_and_detail()
    {
        var cut = Render<StatusBanner>(p => p
            .Add(x => x.Kind, StatusKind.Error)
            .Add(x => x.Title, Payload)
            .Add(x => x.Detail, Payload));

        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);
        Assert.Equal(Payload, cut.Find("[data-testid=status-banner] b").TextContent);
        Assert.Equal(Payload, cut.Find("[data-testid=status-banner] pre").TextContent);
    }

    [Fact]
    public void SignalChip_and_EmptyState_encode_hostile_strings()
    {
        var chipCut = Render<SignalChip>(p => p
            .Add(x => x.Phrase, SignalCatalog.For("custom_signal", Payload))
            .Add(x => x.Evidence, Payload));

        Assert.Empty(chipCut.FindAll("script"));
        Assert.Equal(Payload, chipCut.Find("[data-testid=signal-chip]").TextContent);

        var emptyCut = Render<EmptyState>(p => p
            .Add(x => x.Title, Payload)
            .Add(x => x.Message, Payload));

        Assert.Empty(emptyCut.FindAll("script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", emptyCut.Markup);
    }
}
