namespace AmpersandOfTruth.Web.Features.WebSpread.Matching;

/// <summary>P3.4 stance. <see cref="Unknown"/> = not judged (LLM off, down, or over budget).</summary>
public enum Stance { Unknown, Asserts, RepeatsWithAttribution, Refutes, MentionsNeutrally, Unrelated }

public enum SourceType { Unknown, News, StateMedia, Blog, ForumSocial, Academic, Government, WikipediaMirror, ContentFarm, Other }

/// <summary>Typed output of the <c>match-claim</c> prompt (mirrors <c>Prompts/match-claim.schema.json</c>; snake_case strings).</summary>
public sealed record ClaimMatchJudgement(
    string Stance,
    string MatchType,
    string Quote,
    bool CitesWikipedia,
    string SourceType,
    string Reasoning);

/// <summary>P3.4 verdict for one page: lexical evidence plus, when run, the LLM's stance.</summary>
/// <param name="Quote">Verified to occur in the page text (LLM quotes that don't are dropped, and the lexical passage is used).</param>
public sealed record ClaimMatch(
    Stance Stance,
    MatchKind MatchType,
    string? Quote,
    bool CitesWikipedia,
    SourceType SourceType,
    double LexicalContainment,
    string? Reasoning)
{
    /// <summary>Worth showing: carries the claim or argues against it.</summary>
    /// <summary>The <c>match-claim</c> call was attempted and failed: the verdict is the lexical fallback only.</summary>
    public bool AssessmentFailed { get; init; }

    public bool IsRelevant => Stance is Stance.Asserts or Stance.RepeatsWithAttribution or Stance.Refutes
        || (Stance == Stance.Unknown && MatchType != MatchKind.None);
}

public static class MatchClaimPrompt
{
    public const string Id = "match-claim";

    public static Dictionary<string, string> Variables(string? claimEn, string? claimFi, string? insertedText, string url, string? title, string passage) => new()
    {
        ["claim_en"] = claimEn ?? "",
        ["claim_fi"] = claimFi ?? "",
        ["inserted_text"] = insertedText ?? "",
        ["url"] = url,
        ["title"] = title ?? "",
        ["passage"] = passage,
    };

    public static Stance ParseStance(string s) => s switch
    {
        "asserts" => Stance.Asserts,
        "repeats_with_attribution" => Stance.RepeatsWithAttribution,
        "refutes" => Stance.Refutes,
        "mentions_neutrally" => Stance.MentionsNeutrally,
        "unrelated" => Stance.Unrelated,
        _ => Stance.Unknown,
    };

    public static MatchKind ParseMatchType(string s) => s switch
    {
        "verbatim_copy" => MatchKind.VerbatimCopy,
        "close_paraphrase" => MatchKind.CloseParaphrase,
        "same_claim_different_wording" => MatchKind.SameClaimDifferentWording,
        _ => MatchKind.None,
    };

    public static SourceType ParseSourceType(string s) => s switch
    {
        "news" => SourceType.News,
        "state_media" => SourceType.StateMedia,
        "blog" => SourceType.Blog,
        "forum_social" => SourceType.ForumSocial,
        "academic" => SourceType.Academic,
        "government" => SourceType.Government,
        "wikipedia_mirror" => SourceType.WikipediaMirror,
        "content_farm" => SourceType.ContentFarm,
        "other" => SourceType.Other,
        _ => SourceType.Unknown,
    };
}
