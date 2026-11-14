using AmpersandOfTruth.Web.Features.WebSpread.Spread;

namespace AmpersandOfTruth.Web.Features.WebSpread.Planning;

public enum QueryKind
{
    /// <summary>A quoted distinctive span of the inserted wording: finds copies and mirrors — the strongest evidence of text travelling.</summary>
    ExactPhrase,
    /// <summary>Key entities + the contested detail, in the claim's language (LLM, or the rarest words as a fallback).</summary>
    Keywords,
    /// <summary>The claim rendered as a search in another language (LLM).</summary>
    Translation,
}

/// <param name="Text">The query as sent (quotes and exclusions included).</param>
public sealed record PlannedQuery(string Text, string Language, QueryKind Kind, SearchWindowKind WindowKind, DateWindow? Window);

/// <summary>P3.2 output: what a trace will search. <see cref="Dropped"/> = queries cut by <c>WebSpread:MaxSearches</c>.</summary>
/// <param name="Degraded">Why the plan is weaker than intended (LLM down, no inserted text, provider can't date-restrict), or null.</param>
public sealed record ClaimSearchPlan(
    IReadOnlyList<PlannedQuery> Queries,
    int Dropped,
    string? ContestedDetail,
    string? Notes,
    string? Degraded);

/// <summary>Typed output of the <c>plan-claim-search</c> prompt (mirrors <c>Prompts/plan-claim-search.schema.json</c>).</summary>
public sealed record ClaimQueryExpansion(string ContestedDetail, IReadOnlyList<ExpandedQuery> Queries, string Notes);

/// <param name="Kind"><c>keywords</c> or <c>translation</c>.</param>
public sealed record ExpandedQuery(string Lang, string Kind, string Text);

public static class PlanClaimSearchPrompt
{
    public const string Id = "plan-claim-search";

    public static Dictionary<string, string> Variables(WebSpreadClaim claim, IEnumerable<string> languages) => new()
    {
        ["article_title"] = claim.ArticleTitle,
        ["languages"] = string.Join(", ", languages),
        ["claim_fi"] = claim.ClaimFi ?? "",
        ["claim_en"] = claim.ClaimEn ?? "",
        ["inserted_text"] = claim.InsertedText ?? "",
    };
}
