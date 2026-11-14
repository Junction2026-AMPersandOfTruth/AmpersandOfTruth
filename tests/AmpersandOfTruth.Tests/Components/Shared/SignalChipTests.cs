using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class SignalChipTests : BunitContext
{
    [Fact]
    public void Renders_span_with_catalog_chip_text_canonical_id_and_tooltip()
    {
        var phrase = SignalCatalog.For("reinserted_after_revert");

        var cut = Render<SignalChip>(p => p
            .Add(x => x.Phrase, phrase)
            .Add(x => x.Evidence, "Put back 3 times in 2015."));

        var chip = cut.Find("[data-testid=signal-chip]");
        Assert.Equal("SPAN", chip.TagName);
        Assert.Equal(phrase.Chip, chip.TextContent);
        Assert.Equal("reinserted", chip.GetAttribute("data-signal"));
        Assert.Equal($"{phrase.Why} Put back 3 times in 2015.", chip.GetAttribute("title"));
    }

    [Fact]
    public void Normalizes_non_canonical_phrase_id_on_data_signal()
    {
        var rawPhrase = new SignalPhrase("reinserted_after_revert", "Put back after removal", "Put back.");

        var cut = Render<SignalChip>(p => p.Add(x => x.Phrase, rawPhrase));

        var chip = cut.Find("[data-testid=signal-chip]");
        Assert.Equal("reinserted", chip.GetAttribute("data-signal"));
    }

    [Fact]
    public void Renders_button_with_aria_pressed_and_invokes_callback_when_OnClick_is_set()
    {
        var clicked = 0;
        var phrase = SignalCatalog.For("citation_removed");

        var cut = Render<SignalChip>(p => p
            .Add(x => x.Phrase, phrase)
            .Add(x => x.Selected, true)
            .Add(x => x.OnClick, () => clicked++));

        var btn = cut.Find("[data-testid=signal-chip]");
        Assert.Equal("BUTTON", btn.TagName);
        Assert.Equal("true", btn.GetAttribute("aria-pressed"));
        Assert.Equal(phrase.Chip, btn.TextContent);

        btn.Click();
        Assert.Equal(1, clicked);
    }
}
