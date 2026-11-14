using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>Edit summaries as the classifier prompt sees them: no account names (evidence standard §3, #38 red team).</summary>
public class PromptEditorsTests
{
    // Span author, reverter, re-inserter — numbered in that order.
    private readonly PromptEditors _editors = new(["Anrikki", "Pekka Palauttaja", "192.0.2.44", "Anrikki", "Kolmas_Käyttäjä"]);

    [Theory]
    // MediaWiki undo (modern): the undone editor in a link + talk link.
    [InlineData("Kumottu muokkaus 16554709, jonka teki [[Special:Contributions/Anrikki|Anrikki]] ([[User talk:Anrikki|keskustelu]]) lähteetön",
        "Kumottu muokkaus 16554709, jonka teki Editor 1 (keskustelu) lähteetön")]
    // The same template with a plain-text name (mobile / older software).
    [InlineData("Kumottu muokkaus 16554709, jonka teki Anrikki (keskustelu)", "Kumottu muokkaus 16554709, jonka teki Editor 1 (keskustelu)")]
    // A name nobody involved has → [editor].
    [InlineData("Kumottu muokkaus 123, jonka teki Joku Muu (keskustelu)", "Kumottu muokkaus 123, jonka teki [editor] (keskustelu)")]
    // FlaggedRevs reject: "(tehneet X, Y)" list and "jonka on tehnyt Z:".
    [InlineData("Hylättiin viimeiset 2 tekstimuutosta (tehneet Anrikki ja Vieras Nimi) ja palautettiin versio 15952023, jonka on tehnyt Kolmas Käyttäjä: liiallista poistoa",
        "Hylättiin viimeiset 2 tekstimuutosta (tehneet Editor 1 ja [editor]) ja palautettiin versio 15952023, jonka on tehnyt Editor 3: liiallista poistoa")]
    [InlineData("Hylättiin viimeisin tekstimuutos (tehnyt 188.238.12.34) ja palautettiin versio 15952023, jonka on tehnyt Tuntematon: liiallista poistoa",
        "Hylättiin viimeisin tekstimuutos (tehnyt 188.238.x.x) ja palautettiin versio 15952023, jonka on tehnyt [editor]: liiallista poistoa")]
    // Rollback: both the undone editor and the editor of the restored version.
    [InlineData("Käyttäjän [[Special:Contributions/Vandaali|Vandaali]] ([[Keskustelu käyttäjästä:Vandaali|keskustelu]]) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän Pekka Palauttaja tekemään versioon.",
        "Käyttäjän [editor] (keskustelu) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän Editor 2 tekemään versioon.")]
    [InlineData("Käyttäjän Vandaali (keskustelu) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän Joku Toinen tekemään versioon.",
        "Käyttäjän [editor] (keskustelu) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän [editor] tekemään versioon.")]
    // Block evasion names the blocked account in parentheses.
    [InlineData("rv, eston kiertoa (Estetty Tili)", "rv, eston kiertoa ([editor])")]
    // English gadgets.
    [InlineData("Reverted edits by [[Special:Contributions/Someone|Someone]] ([[User talk:Someone|talk]]) to last revision by Pekka_Palauttaja",
        "Reverted edits by [editor] (talk) to last revision by Editor 2")]
    [InlineData("Undid revision 123 by Someone (talk) pov", "Undid revision 123 by [editor] (talk) pov")]
    [InlineData("Revert to revision 123 dated 2016-10-16 19:06:33 by Joku using [[:en:Wikipedia:Tools/Navigation_popups|popups]]",
        "Revert to revision 123 dated 2016-10-16 19:06:33 by [editor] using popups")]
    // IPs: masked as everywhere else, never relabelled; temporary accounts go.
    [InlineData("Kumottu muokkaus 5, jonka teki [[Special:Contributions/192.0.2.44|192.0.2.44]] (keskustelu)", "Kumottu muokkaus 5, jonka teki 192.0.x.x (keskustelu)")]
    [InlineData("Kumottu muokkaus 5, jonka teki ~2025-26183-73 (keskustelu)", "Kumottu muokkaus 5, jonka teki [editor] (keskustelu)")]
    // A summary cut at 255 bytes inside a user link.
    [InlineData("Kumottu muokkaus 5, jonka teki [[Special:Contributions/Pitkänimine", "Kumottu muokkaus 5, jonka teki [editor]")]
    // No names: untouched apart from wikilinks.
    [InlineData("/* Sotatoimet */ lähteetöntä [[Ošta]]sta", "/* Sotatoimet */ lähteetöntä Oštasta")]
    [InlineData(null, "")]
    public void Masks_every_account_name(string? comment, string expected) =>
        Assert.Equal(expected, _editors.Mask(comment));

    [Fact]
    public void The_same_account_keeps_its_number_and_spaces_or_underscores_do_not_matter()
    {
        Assert.Equal("Editor 1", _editors.LabelOf("Anrikki"));
        Assert.Equal("Editor 2", _editors.LabelOf("Pekka_Palauttaja"));
        Assert.Equal("Editor 3", _editors.LabelOf("Kolmas Käyttäjä"));
        Assert.Null(_editors.LabelOf("192.0.2.44"));
    }

    [Fact]
    public void An_involved_name_inside_free_text_is_replaced_with_its_finnish_case_ending()
    {
        Assert.Equal("Editor 1:n lisäys oli väärä, ks. Editor 2n huomautus",
            _editors.Mask("Anrikki:n lisäys oli väärä, ks. Pekka Palauttajan huomautus"));
    }
}
