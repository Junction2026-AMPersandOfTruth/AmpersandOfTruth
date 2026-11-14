namespace AmpersandOfTruth.Web.Features.Evidence;

using System.Text.RegularExpressions;

public enum CopyRuleKind
{
    Accusation,
    Certainty,
    Intent,
    Jargon,
}

public sealed record CopyRule(CopyRuleKind Kind, string Term, string Advice, Regex Pattern);

public sealed record CopyViolation(CopyRule Rule, string Match, int Index);

public static class CopyRules
{
    private const string DescribeEditsAdvice = "Describe what the edits did, never the person.";
    private const string CertaintyAdvice = "Say \"removed\", \"disputed\" or \"consistent with\".";
    private const string IntentAdvice = "Say what was added, removed or put back.";

    public static IReadOnlyList<CopyRule> All { get; } =
    [
        // Accusations
        Create(CopyRuleKind.Accusation, "propagandist", @"propagandists?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "disinformation agent", @"disinformation\s+agents?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "troll", @"trolls?|trolling", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "sockpuppet", @"sock\s?puppets?|sock\s?puppetry", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "bad actor", @"bad\s+actors?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "liar", @"liars?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "paid editor", @"paid\s+(?:editor|agent)s?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "fake account", @"fake\s+accounts?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "bot farm", @"bot\s+farms?", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "guilty", @"guilty", DescribeEditsAdvice),
        Create(CopyRuleKind.Accusation, "malicious", @"malicious(?:ly)?", DescribeEditsAdvice),

        // Certainty
        Create(CopyRuleKind.Certainty, "proven", @"proven", CertaintyAdvice),
        Create(CopyRuleKind.Certainty, "confirmed false", @"confirmed\s+false", CertaintyAdvice),

        // Intent
        Create(CopyRuleKind.Intent, "tried to", @"tried\s+to", IntentAdvice),
        Create(CopyRuleKind.Intent, "deliberately", @"deliberate(?:ly)?", IntentAdvice),
        Create(CopyRuleKind.Intent, "intentionally", @"intentional(?:ly)?", IntentAdvice),
        Create(CopyRuleKind.Intent, "in bad faith", @"in\s+bad\s+faith", IntentAdvice),

        // Jargon
        Create(CopyRuleKind.Jargon, "revert", @"revert(?:s|ed|ing)?", "Say \"removed\"."),
        Create(CopyRuleKind.Jargon, "sha1", @"sha-?1", "Don't show hashes."),
        Create(CopyRuleKind.Jargon, "tier", @"tiers?", "Say \"level\"."),
        Create(CopyRuleKind.Jargon, "LLM", @"llms?", "Say \"automated assessment\"."),
    ];

    private static CopyRule Create(CopyRuleKind kind, string term, string body, string advice) =>
        new(kind, term, advice, new Regex($@"\b(?:{body})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    /// <summary>
    /// All matches, ordered by Index. includeJargon:false skips Kind == Jargon.
    /// </summary>
    public static IReadOnlyList<CopyViolation> FindViolations(string text, bool includeJargon = true)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var violations = new List<CopyViolation>();
        foreach (var rule in All)
        {
            if (!includeJargon && rule.Kind == CopyRuleKind.Jargon)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in rule.Pattern.Matches(text))
            {
                violations.Add(new CopyViolation(rule, match.Value, match.Index));
            }
        }

        return violations.OrderBy(v => v.Index).ToList();
    }
}
