using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.WikiDiff;

public class WikitextProseTests
{
    [Fact]
    public void Links_become_their_labels_and_formatting_disappears()
    {
        var view = WikitextProse.Of("'''Talvisota''' oli [[Suomi|Suomen]] ''sota'', ja neuvottelut käytiin [[Moskova]]ssa.");
        Assert.Equal("Talvisota oli Suomen sota, ja neuvottelut käytiin Moskovassa.", view.Prose);
    }

    [Fact]
    public void Templates_infoboxes_files_categories_interwiki_and_comments_are_dropped()
    {
        const string text = """
            {{Infobox sota
            | nimi = Talvisota
            | osapuoli1 = {{lippu|Suomi}}
            }}
            Sota alkoi.{{Lähde}}<!-- piilotettu kommentti -->
            [[Tiedosto:Kartta.png|pienois|Kartta, jossa [[Viipuri]] näkyy]]

            [[Luokka:Talvisota]]
            [[en:Winter War]]
            """;
        Assert.Equal("Sota alkoi.", WikitextProse.Of(text).Prose);
    }

    [Fact]
    public void References_are_kept_apart_with_their_citation_text()
    {
        var view = WikitextProse.Of(
            "Sota alkoi 1939.<ref>{{Kirjaviite | Tekijä = Leskinen, Jari | Nimeke = Talvisodan pikkujättiläinen | Vuosi = 1999}}</ref> " +
            "Rauha 1940.<ref name=\"a\">[https://example.fi/rauha Rauhan ehdot]</ref><ref name=\"a\" />");

        Assert.Equal("Sota alkoi 1939. Rauha 1940.", view.Prose);
        Assert.Equal(["Leskinen, Jari; Talvisodan pikkujättiläinen; 1999", "Rauhan ehdot", ""], view.References);
    }

    [Fact]
    public void Headings_and_list_items_keep_their_text()
    {
        var view = WikitextProse.Of("== Tausta ==\n* Ensimmäinen kohta\n# Toinen [[kohta]]\n");
        Assert.Equal("Tausta\n\nEnsimmäinen kohta\n\nToinen kohta", view.Prose);
    }

    [Fact]
    public void External_link_shows_its_label_and_a_bare_url_shows_itself()
    {
        Assert.Equal("Katso arkisto ja https://example.fi/x.", WikitextProse.Of("Katso [https://example.fi arkisto] ja https://example.fi/x.").Prose);
    }

    [Fact]
    public void Abbreviation_with_a_colon_is_a_link_not_an_interwiki()
    {
        Assert.Equal("YK:n turvallisuusneuvosto", WikitextProse.Of("[[YK:n turvallisuusneuvosto]]").Prose);
    }

    [Fact]
    public void Unbalanced_markup_does_not_swallow_the_page()
    {
        var view = WikitextProse.Of("Alku {{rikki ja [[puolikas\n\nToinen kappale.");
        Assert.Contains("Toinen kappale.", view.Prose);
    }
}
