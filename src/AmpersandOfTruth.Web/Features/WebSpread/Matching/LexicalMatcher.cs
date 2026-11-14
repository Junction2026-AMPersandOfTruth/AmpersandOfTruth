using System.Text.RegularExpressions;

namespace AmpersandOfTruth.Web.Features.WebSpread.Matching;

/// <summary>How a page's text relates to the inserted wording (P3.4 <c>match_type</c>).</summary>
public enum MatchKind { None, SameClaimDifferentWording, CloseParaphrase, VerbatimCopy }

/// <param name="Containment">Share of the claim's word shingles found in the page (0–1).</param>
/// <param name="Passage">The page passage with the most shared shingles (verbatim from the page text), or null.</param>
/// <param name="PassageStart">Character offset of <see cref="Passage"/> in the page text.</param>
public sealed record LexicalMatch(MatchKind Type, double Containment, string? Passage, int PassageStart);

/// <summary>
/// P3.4 approach A: word-shingle overlap between the inserted wording and a page. Catches copies and Wikipedia mirrors
/// cheaply in any language; it can't see a translated or reworded claim (that is B/C's job). Pure; thread-safe.
/// </summary>
public static partial class LexicalMatcher
{
    public const int ShingleSize = 4;
    public const double VerbatimThreshold = 0.8;
    public const double ParaphraseThreshold = 0.35;

    public static LexicalMatch Match(string? claim, string? page, int passageChars = 600)
    {
        if (string.IsNullOrWhiteSpace(claim) || string.IsNullOrWhiteSpace(page)) return new(MatchKind.None, 0, null, -1);

        var claimTokens = Tokens(claim);
        var size = Math.Min(ShingleSize, Math.Max(1, claimTokens.Count));
        var claimShingles = Shingles(claimTokens, size);
        if (claimShingles.Count == 0) return new(MatchKind.None, 0, null, -1);

        var pageTokens = Tokens(page);
        var hitsAt = new List<int>(); // token index of each page shingle that is in the claim
        var found = new HashSet<string>();
        for (var i = 0; i + size <= pageTokens.Count; i++)
        {
            var s = Key(pageTokens, i, size);
            if (claimShingles.Contains(s))
            {
                hitsAt.Add(i);
                found.Add(s);
            }
        }

        var containment = (double)found.Count / claimShingles.Count;
        var type = containment >= VerbatimThreshold ? MatchKind.VerbatimCopy
            : containment >= ParaphraseThreshold ? MatchKind.CloseParaphrase
            : MatchKind.None;
        if (hitsAt.Count == 0) return new(type, containment, null, -1);

        // Densest run of hits within one claim-length (+ slack) of tokens → the passage to quote.
        var span = claimTokens.Count + 10;
        int bestStart = hitsAt[0], bestCount = 0;
        for (int lo = 0, hi = 0; hi < hitsAt.Count; hi++)
        {
            while (hitsAt[hi] - hitsAt[lo] > span) lo++;
            if (hi - lo + 1 > bestCount) (bestCount, bestStart) = (hi - lo + 1, hitsAt[lo]);
        }

        var from = pageTokens[bestStart].Start;
        var lastToken = pageTokens[Math.Min(pageTokens.Count - 1, bestStart + claimTokens.Count + size)];
        var to = Math.Min(page.Length, Math.Max(lastToken.Start + lastToken.Length, from + 1));
        if (to - from > passageChars) to = from + passageChars;
        return new(type, containment, page[from..to].Trim(), from);
    }

    /// <summary>The passage of <paramref name="page"/> (about <paramref name="chars"/> long) sharing the most content
    /// words with <paramref name="terms"/> — the candidate chunk handed to the LLM when there is no lexical copy.</summary>
    public static string? BestPassage(string page, IEnumerable<string> terms, int chars = 1200)
    {
        var wanted = terms.SelectMany(t => Tokens(t)).Select(t => t.Norm).Where(t => t.Length >= 4)
            .Select(t => t[..Math.Min(t.Length, 6)]).ToHashSet(); // crude stem: Finnish inflects the ending
        if (wanted.Count == 0 || page.Length == 0) return null;
        var tokens = Tokens(page);
        if (tokens.Count == 0) return null;

        int bestStart = 0, bestScore = -1, lo = 0, score = 0;
        bool Hit(Token t) => t.Norm.Length >= 4 && wanted.Contains(t.Norm[..Math.Min(t.Norm.Length, 6)]);
        for (var hi = 0; hi < tokens.Count; hi++)
        {
            if (Hit(tokens[hi])) score++;
            while (tokens[hi].Start - tokens[lo].Start > chars) { if (Hit(tokens[lo])) score--; lo++; }
            if (score > bestScore) (bestScore, bestStart) = (score, tokens[lo].Start);
        }
        if (bestScore <= 0) return null;
        var end = Math.Min(page.Length, bestStart + chars);
        return page[bestStart..end].Trim();
    }

    public sealed record Token(string Norm, int Start, int Length);

    public static List<Token> Tokens(string text) =>
        WordRegex().Matches(text).Select(m => new Token(m.Value.ToLowerInvariant(), m.Index, m.Length)).ToList();

    private static HashSet<string> Shingles(List<Token> tokens, int size)
    {
        var set = new HashSet<string>();
        for (var i = 0; i + size <= tokens.Count; i++) set.Add(Key(tokens, i, size));
        return set;
    }

    private static string Key(List<Token> tokens, int start, int size) =>
        string.Join(' ', tokens.Skip(start).Take(size).Select(t => t.Norm));

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
