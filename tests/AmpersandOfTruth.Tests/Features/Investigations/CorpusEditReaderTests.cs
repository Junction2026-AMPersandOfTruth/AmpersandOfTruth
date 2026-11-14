using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.Investigations;

public class CorpusEditReaderTests
{
    private const string Before = "Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.\n\nToinen kappale.";
    private const string After = "Talvisota alkoi 30. marraskuuta 1939, kun Suomi hyökkäsi ensin Neuvostoliittoon.\n\nUusi kappale.\n\nToinen kappale.";

    [Fact]
    public void A_paragraph_changed_in_place_is_shown_whole_and_a_new_paragraph_as_itself()
    {
        var (inserted, removed) = CorpusEditReader.Passages(Before, After, WikitextDiff.Compare(Before, After));

        Assert.Equal("Talvisota alkoi 30. marraskuuta 1939, kun Suomi hyökkäsi ensin Neuvostoliittoon.\n…\nUusi kappale", inserted);
        Assert.Equal("Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.", removed);
    }

    [Fact]
    public void A_pure_insertion_inside_a_long_paragraph_stays_word_level()
    {
        var para = string.Join(" ", Enumerable.Repeat("Pitkä lause ilman muutoksia.", 80));
        var before = para;
        var after = para + " Lisätty väite.";

        var (inserted, removed) = CorpusEditReader.Passages(before, after, WikitextDiff.Compare(before, after));

        Assert.Equal("Lisätty väite.", inserted);
        Assert.Equal("", removed);
    }

    [Fact]
    public void Context_is_the_touched_paragraphs_before_the_edit()
    {
        var context = CorpusEditReader.Context(Before, WikitextDiff.Compare(Before, After), 6000);

        Assert.Equal("Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.", context);
    }

    [Fact]
    public void Exposure_reads_like_a_person_wrote_it()
    {
        Assert.Equal("less than a minute", GeminiEditClassifier.Exposure(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 minute", GeminiEditClassifier.Exposure(TimeSpan.FromSeconds(60)));
        Assert.Equal("3 hours", GeminiEditClassifier.Exposure(TimeSpan.FromMinutes(200)));
        Assert.Equal("12 days", GeminiEditClassifier.Exposure(TimeSpan.FromDays(12.5)));
        Assert.Equal("2 years", GeminiEditClassifier.Exposure(TimeSpan.FromDays(800)));
    }

    [Fact]
    public void Bot_rule_matches_s1()
    {
        Assert.True(IncidentAssembler.IsBot("VakauttajaBot"));
        Assert.True(IncidentAssembler.IsBot("Zxbotti"));
        Assert.True(IncidentAssembler.IsBot("CommonsDelinker"));
        Assert.False(IncidentAssembler.IsBot("Bothnia"));
        Assert.False(IncidentAssembler.IsBot("192.0.2.1"));
        Assert.False(IncidentAssembler.IsBot(null));
    }
}
