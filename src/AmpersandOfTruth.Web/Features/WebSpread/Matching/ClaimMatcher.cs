using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread.Matching;

/// <summary>
/// P3.4, the A → C pipeline: lexical shingles catch copies; the passage that best matches (lexically, or by the claim's
/// content words) goes to the <c>match-claim</c> LLM for stance. Approach B (bge-m3 chunk retrieval) is the experiment
/// to add between them if A+C misses paraphrases. Quotes are verified against the page text. Singleton.
/// </summary>
public sealed partial class ClaimMatcher(IAnalysisModel llm, IOptionsMonitor<WebSpreadOptions> options, ILogger<ClaimMatcher> log)
{
    public async Task<ClaimMatch> MatchAsync(WebSpreadClaim claim, FetchedPage page, CancellationToken ct = default)
    {
        var lexical = LexicalMatcher.Match(claim.InsertedText, page.Text);
        var citesWikipedia = MentionsWikipedia(page.Html ?? page.Text);
        var isMirror = UrlPolicy.Classify(page.Url) == UrlKind.Mirror;
        var fallback = new ClaimMatch(Stance.Unknown, lexical.Type, lexical.Passage, citesWikipedia || isMirror,
            isMirror ? SourceType.WikipediaMirror : SourceType.Unknown, lexical.Containment, null);

        var passage = lexical.Passage is not null && lexical.Type != MatchKind.None
            ? Around(page.Text, lexical.PassageStart, 1200)
            : LexicalMatcher.BestPassage(page.Text, [claim.InsertedText ?? "", claim.ClaimFi ?? "", claim.ClaimEn ?? ""]);
        if (passage is null) return fallback with { Stance = Stance.Unrelated };
        if (!options.CurrentValue.UseLlmMatch) return fallback;

        var result = await llm.RunAsync<ClaimMatchJudgement>(new AnalysisRequest(MatchClaimPrompt.Id,
            MatchClaimPrompt.Variables(claim.ClaimEn, claim.ClaimFi, claim.InsertedText, page.Url, page.Title, passage)) { Scope = claim.Scope }, ct);
        if (!result.IsOk)
        {
            log.LogWarning("match-claim for {Url} returned {Outcome} ({Error}); keeping the lexical match only", page.Url, result.Outcome, result.Error);
            return fallback with { AssessmentFailed = true };
        }

        var j = result.Value;
        var quote = !string.IsNullOrWhiteSpace(j.Quote) && Normalize(page.Text).Contains(Normalize(j.Quote), StringComparison.Ordinal)
            ? j.Quote.Trim()
            : lexical.Passage; // an unverifiable quote is never shown
        var llmType = MatchClaimPrompt.ParseMatchType(j.MatchType);
        return new ClaimMatch(
            MatchClaimPrompt.ParseStance(j.Stance),
            lexical.Type > llmType ? lexical.Type : llmType, // shingles are proof of copying; keep the stronger
            quote,
            j.CitesWikipedia || citesWikipedia || isMirror,
            isMirror ? SourceType.WikipediaMirror : MatchClaimPrompt.ParseSourceType(j.SourceType),
            lexical.Containment,
            j.Reasoning);
    }

    /// <summary>Links to or credits Wikipedia anywhere on the page (a coarse P3.6 signal; the LLM checks near the passage).</summary>
    public static bool MentionsWikipedia(string? htmlOrText) => htmlOrText is not null && WikipediaRef().IsMatch(htmlOrText);

    private static string Around(string text, int start, int chars)
    {
        var from = Math.Max(0, start - chars / 4);
        var to = Math.Min(text.Length, from + chars);
        return text[from..to];
    }

    private static string Normalize(string s) => Whitespace().Replace(s, " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"wikipedia\.org/wiki/|\bWikipedia\b|\bWikipediasta\b|\bВикипеди", RegexOptions.IgnoreCase)]
    private static partial Regex WikipediaRef();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
