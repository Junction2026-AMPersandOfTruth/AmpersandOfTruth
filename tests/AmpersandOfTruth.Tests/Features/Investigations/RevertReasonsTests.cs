using System.Text.Json;
using AmpersandOfTruth.Tests.Features.WikiDiff;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

public class RevertReasonsTests
{
    public sealed record Case(string Comment, string ReasonText, string[] Reasons, string[] RawReasons)
    {
        public override string ToString() => Comment.Length <= 60 ? Comment : Comment[..60] + "…";
    }

    public static TheoryData<Case> Parity()
    {
        var path = Path.Combine(WikitextDiffTests.RepoRoot(), "tests", "AmpersandOfTruth.Tests", "Features", "Investigations", "Fixtures", "s5-parity.jsonl");
        var data = new TheoryData<Case>();
        foreach (var line in File.ReadLines(path).Where(l => l.Length > 0))
            data.Add(JsonSerializer.Deserialize<Case>(line, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Parity))]
    public void Matches_the_python_lexicon(Case c)
    {
        Assert.Equal(c.ReasonText, RevertReasons.ReasonText(c.Comment));
        Assert.Equal(c.Reasons, RevertReasons.Categories(RevertReasons.ReasonText(c.Comment)).Order(StringComparer.Ordinal));
        Assert.Equal(c.RawReasons, RevertReasons.Categories(c.Comment).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Kumottu muokkaus 123, jonka teki 192.0.2.1 (keskustelu) väärää tietoa", RemovalReason.Accuracy)]
    [InlineData("rv, puolueellista propagandaa", RemovalReason.Neutrality)]
    [InlineData("lähteetön väite", RemovalReason.Sourcing)]
    [InlineData("omaa tutkimusta", RemovalReason.Sourcing)]
    [InlineData("väärä ja lähteetön", RemovalReason.Accuracy)]
    [InlineData("rvv", RemovalReason.Vandalism)]
    [InlineData("kh", RemovalReason.Other)]
    [InlineData("Hylättiin viimeisin tekstimuutos (tehnyt 192.0.2.1) ja palautettiin versio 123 käyttäjältä Matti", RemovalReason.None)]
    [InlineData(null, RemovalReason.None)]
    public void Maps_categories_to_the_evidence_standard_removal_reason(string? comment, RemovalReason expected) =>
        Assert.Equal(expected, RevertReasons.Of(comment));

    [Fact]
    public void A_username_inside_a_rollback_template_is_not_a_reason()
    {
        const string rollback = "Käyttäjän [[Special:Contributions/Väärä|Väärä]] ([[User talk:Väärä|keskustelu]]) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän Matti tekemään versioon.";

        Assert.Equal(RemovalReason.None, RevertReasons.Of(rollback));
    }
}
