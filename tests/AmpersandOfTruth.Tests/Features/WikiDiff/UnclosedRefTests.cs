using System.Diagnostics;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.WikiDiff;

/// <summary>
/// An unclosed <c>&lt;ref&gt;</c> must not run on to the next reference's closing tag (S7 report §6.1, #29): the edit that
/// broke the tag would otherwise "add" a reference holding every paragraph in between.
/// Fixtures/unclosed-ref.new.txt = base.txt with the lead's <c>&lt;/ref&gt;</c> replaced by a few words.
/// </summary>
public class UnclosedRefTests
{
    private static readonly string Base = WikitextDiffTests.Fixture("base");
    private static readonly string Broken = WikitextDiffTests.Fixture("unclosed-ref.new");

    [Fact]
    public void Breaking_a_closing_tag_reports_only_what_the_edit_changed()
    {
        var r = WikitextDiff.Compare(Base, Broken);

        Assert.Equal("myös termi motti tuli tällöin tunnetuksi", r.AddedProse);   // an inline citation template is no prose
        Assert.Equal("", r.RemovedProse);
        Assert.Equal((0, 1), (r.Stats.AddedRefs, r.Stats.RemovedRefs));   // the citation is no longer a reference
        Assert.False(r.Stats.IsBlanking);
        Assert.All(r.Segments, s => Assert.Equal((1, 1), (s.OldParagraph, s.NewParagraph)));   // all inside the lead
        Assert.DoesNotContain(r.Segments, s => s.Text.Contains("Tausta") || s.Text.Contains("alueluovutuksia"));
    }

    [Fact]
    public void Repairing_the_tag_is_the_mirror_image()
    {
        var broke = WikitextDiff.Compare(Base, Broken);
        var repaired = WikitextDiff.Compare(Broken, Base);

        Assert.Equal(broke.AddedProse, repaired.RemovedProse);
        Assert.Equal("", repaired.AddedProse);
        Assert.Equal((1, 0), (repaired.Stats.AddedRefs, repaired.Stats.RemovedRefs));
        Assert.False(repaired.Stats.IsBlanking);
    }

    [Fact]
    public void The_sections_after_an_unclosed_ref_stay_blocks_of_their_own()
    {
        var before = WikitextBlocks.Split(Base);
        var after = WikitextBlocks.Split(Broken);

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before.Select(b => b.IsHeading), after.Select(b => b.IsHeading));
        Assert.Equal("== Tausta ==", Broken[after[2].Start..after[2].End]);
    }

    [Fact]
    public void The_prose_view_keeps_the_text_after_an_unclosed_ref_as_text()
    {
        var view = WikitextProse.Of(Broken);

        Assert.Contains("Neuvostoliitto vaati syksyllä 1939 Suomelta alueluovutuksia", view.Prose);
        Assert.Contains("myös termi motti tuli tällöin tunnetuksi", view.Prose);
        Assert.Equal(WikitextProse.Of(Base).References.Skip(1), view.References);   // the later references are untouched
        Assert.All(view.References, c => Assert.DoesNotContain("alueluovutuksia", c));
    }

    [Theory]
    // Another reference opens before the closing tag: the first one was never closed.
    [InlineData("Alku.<ref>Lähde 1 Väli.<ref>Lähde 2</ref> Loppu.", "Alku.Lähde 1 Väli. Loppu.", new[] { "Lähde 2" })]
    // A heading before the closing tag: the same, even when no other reference opens in between.
    [InlineData("Alku.<ref>Lähde 1\n\n== Otsikko ==\nTeksti.</ref>", "Alku.Lähde 1\n\nOtsikko\n\nTeksti.", new string[0])]
    // </references> closes the reference list, not a reference.
    [InlineData("Alku.<ref>Lähde 1\n\n<references>\n</references>", "Alku.Lähde 1", new string[0])]
    // Still references: several lines, blanks inside the closing tag, a reuse inside the body, a level-1 "=" line.
    [InlineData("Alku.<ref>{{Kirjaviite\n | Tekijä = Meikäläinen\n | Vuosi = 1999\n}}</ref > Loppu.", "Alku. Loppu.", new[] { "Meikäläinen; 1999" })]
    [InlineData("Alku.<REF name=a>Lähde 1 <ref name=\"b\" /> jatkuu</Ref> Loppu.", "Alku. Loppu.", new[] { "Lähde 1 jatkuu" })]
    [InlineData("Alku.<ref>Lähde 1\n= 2 + 2 =\njatkuu</ref> Loppu.", "Alku. Loppu.", new[] { "Lähde 1 = 2 + 2 = jatkuu" })]
    public void A_reference_ends_at_its_own_closing_tag_or_not_at_all(string wikitext, string prose, string[] references)
    {
        var view = WikitextProse.Of(wikitext);

        Assert.Equal(prose, view.Prose);
        Assert.Equal(references, view.References);
    }

    [Fact]
    public void An_unclosed_ref_between_two_sections_changes_neither()
    {
        // The case from the report: the tag breaks at the end of one section; the next </ref> is far down the next one.
        const string intact = "Haataja kuvaa neuvotteluja.<ref>Haataja 1989.</ref>\n\n==Sodan alku==\nNeuvottelujen päätyttyä marraskuussa "
            + "tuloksettomina Neuvostoliiton poliittinen painostus Suomea kohtaan lisääntyi.<ref>Manninen 1994, s. 28.</ref> Lopulta "
            + "Neuvostoliitto lavasti Mainilan laukaukset.\n";
        var broken = intact.Replace("Haataja 1989.</ref>", "Haataja 1989.myös termi kives tuli tällöin tunnetuksi");

        var r = WikitextDiff.Compare(intact, broken);

        Assert.Equal("Haataja 1989.myös termi kives tuli tällöin tunnetuksi", r.AddedProse);
        Assert.DoesNotContain(r.Segments, s => s.Kind == SegmentKind.Reference && s.Op == DiffOp.Added);
        Assert.DoesNotContain(r.Segments, s => s.Text.Contains("Neuvottelujen") || s.Text.Contains("Sodan alku"));
        Assert.Equal(0, r.Stats.RemovedProseChars);
    }

    [Fact]
    public void Thousands_of_unclosed_refs_are_read_once_each()   // #91: no rescan to the end of the page per tag
    {
        var page = string.Concat(Enumerable.Range(0, 20_000).Select(i => $"Väite {i}.<ref>Lähde {i} jäi auki. "));
        var clock = Stopwatch.StartNew();

        var view = WikitextProse.Of(page);
        var diff = WikitextDiff.Compare(page, page + "Lisäys.");

        Assert.Empty(view.References);
        Assert.Equal("Lisäys.", diff.AddedProse);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed.TotalMilliseconds} ms");
    }
}
