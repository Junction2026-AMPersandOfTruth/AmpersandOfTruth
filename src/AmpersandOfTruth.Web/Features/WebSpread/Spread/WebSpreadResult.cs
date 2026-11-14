using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;

namespace AmpersandOfTruth.Web.Features.WebSpread.Spread;

/// <summary>Where a source sits relative to the Wikipedia exposure (P3.6). Refutations keep their timing bucket and are
/// grouped separately by <see cref="WebSpreadSource.Stance"/>.</summary>
public enum SpreadBucket
{
    /// <summary>"Already circulating" — present before the first Wikipedia insertion; a possible origin.</summary>
    Before,
    /// <summary>"Appeared while it was on Wikipedia".</summary>
    During,
    /// <summary>"Appeared after Wikipedia removed it".</summary>
    AfterRemoval,
    /// <summary>No reliable date — shown separately, never forced into a bucket.</summary>
    Undated,
}

/// <summary>One web page that carries (or refutes) the claim. All text fields are untrusted page content: render as text.</summary>
/// <param name="NearBoundary">The date is within a day of an insertion/removal: the order is uncertain at day granularity.</param>
/// <param name="LikelyFromWikipedia">During/after + wording overlap, a Wikipedia link/credit, or a mirror (P3.6's upgrade rule).
/// Timing alone never sets it: "after" is not "because of".</param>
/// <param name="FoundBy">Which planned queries returned it (for "what was searched").</param>
public sealed record WebSpreadSource(
    string Url,
    string Domain,
    string? Title,
    SourceType SourceType,
    Stance Stance,
    MatchKind MatchType,
    string? Quote,
    bool CitesWikipedia,
    SpreadBucket Bucket,
    bool NearBoundary,
    bool LikelyFromWikipedia,
    PageDating Dating,
    IReadOnlyList<QueryKind> FoundBy)
{
    public bool IsRefutation => Stance == Stance.Refutes;
}

/// <summary>Trace status for the UI states of P3.7.</summary>
public enum WebSpreadStatus
{
    Completed,
    /// <summary>Finished, but some searches/fetches failed or were cut by the budget: results are partial.</summary>
    Partial,
    /// <summary>No credentials: web spread is off on this deployment.</summary>
    NotConfigured,
    /// <summary>The request cap was hit before anything could be searched.</summary>
    BudgetExceeded,
    /// <summary>Every search failed.</summary>
    Failed,
}

/// <summary>P3.6 output contract (<c>WebSpreadResult</c> in issue #59), consumed by the P3.7 UI.</summary>
/// <param name="Earliest">The earliest dated source that asserts or repeats the claim (candidate origin), if any.</param>
/// <param name="Headline">Generated from the data; neutral wording only.</param>
/// <param name="Notes">Degradations and limits, one sentence each (LLM down, provider without date search, cap hit, …).</param>
public sealed record WebSpreadResult(
    WebSpreadClaim Claim,
    WebSpreadStatus Status,
    ClaimSearchPlan Plan,
    IReadOnlyList<WebSpreadSource> Sources,
    WebSpreadSource? Earliest,
    string Headline,
    int SearchesRun,
    int PagesRead,
    IReadOnlyList<string> Notes);
