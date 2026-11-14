using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Tests.Features.Evidence;

/// <summary>
/// docs/evidence-standard.md and Features/Evidence must say the same thing (P0.7, #18),
/// and every user-facing phrase TierRules can produce must pass CopyRules.
/// </summary>
public sealed class EvidenceDocSyncTests
{
    private static readonly string Doc = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "evidence-standard.md"));

    public static TheoryData<string> CodeStringsQuotedInDoc()
    {
        var data = new TheoryData<string>();
        foreach (var tier in EvidenceTiers.All)
        {
            data.Add(tier.Id);
            data.Add(tier.Label);
            data.Add(tier.ShortLabel);
        }
        data.Add(Attribution.FooterText);
        data.Add(Methodology.Summary);
        foreach (var limitation in Methodology.Limitations) data.Add(limitation);
        data.Add(Methodology.AccountsCaveat);
        return data;
    }

    [Theory]
    [MemberData(nameof(CodeStringsQuotedInDoc))]
    public void Doc_quotes_the_code_string_verbatim(string text) => Assert.Contains(text, Doc);

    [Fact]
    public void Every_why_flagged_phrase_passes_copy_rules()
    {
        var claim = new ContentAssessment(ContentCategory.PovPush, 4, HasVerifiedQuote: true);
        var everything = new IncidentEvidence
        {
            IsSubstantive = true, RemovedByOtherAccount = true, Assessment = claim,
            ReinsertionCount = 1, InEditWar = true, DisputeTemplateAdded = true,
            DistinctInsertingEditors = 3, DistinctArticles = 2, SourceManipulation = true,
        };
        var cases = new List<IncidentEvidence>
        {
            everything with { Assessment = null },                                     // Reverted
            everything with { ReinsertionCount = 4, InsertionSpan = TimeSpan.FromDays(30) }, // Campaign + Persistence
        };
        foreach (var reason in new[] { RemovalReason.Accuracy, RemovalReason.Neutrality, RemovalReason.Sourcing })
            cases.Add(everything with { RemovalReason = reason });                     // Campaign + every dispute reason

        var sentences = cases.Select(e => TierRules.Assess(e).BecauseSentence!).ToList();
        var kinds = cases.SelectMany(e => TierRules.Assess(e).Reasons).Select(r => r.Kind).ToHashSet();

        Assert.Equal(Enum.GetValues<TierReasonKind>().ToHashSet(), kinds); // every reason kind was exercised
        Assert.All(sentences, s => Assert.Empty(CopyRules.FindViolations(s)));
    }

    [Fact]
    public void Editor_labels_pass_copy_rules()
    {
        foreach (var user in new[] { null, "Matti Meikäläinen", "~2025-26183-73", "85.76.123.45", "2001:14BB:51:D813:A1B9:8F12:23B6:8D13" })
            Assert.Empty(CopyRules.FindViolations(EditorDisplay.For(user).PlainText));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root (AmpersandOfTruth.sln) not found");
    }
}
