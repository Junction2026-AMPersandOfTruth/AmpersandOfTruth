using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Web.Features.WebSpread.Spread;

/// <summary>
/// What a web-spread trace starts from: one incident's claim. Built from the UI read model (<see cref="IncidentView"/>),
/// because the inserted wording comes from the diff passage the mapper derives, not from the contract record.
/// </summary>
/// <param name="IncidentId">Also the trace's budget/ledger scope (<c>webspread:{IncidentId}</c>).</param>
/// <param name="InsertedText">The prose the first inserting edit added (Finnish, verbatim) — the wording fingerprint for
/// exact-phrase queries and lexical matching. Null when the corpus was down while mapping.</param>
public sealed record WebSpreadClaim(
    string IncidentId,
    string ArticleTitle,
    string? ClaimFi,
    string? ClaimEn,
    string? InsertedText,
    IReadOnlyList<ExposureWindow> Windows)
{
    public string Scope => $"webspread:{IncidentId}";

    /// <summary>The best text to search and match on: the inserted wording, else the extracted claim.</summary>
    public string? MatchText => !string.IsNullOrWhiteSpace(InsertedText) ? InsertedText
        : !string.IsNullOrWhiteSpace(ClaimFi) ? ClaimFi
        : ClaimEn;

    public static WebSpreadClaim From(IncidentView incident) => new(
        incident.Id,
        incident.PrimaryArticle.TitleFi,
        incident.ClaimOriginal ?? incident.Source.ClaimFi,
        incident.ClaimEn,
        incident.Passage?.Diff.AddedProse is { Length: > 0 } added ? added.Trim() : null,
        ExposureWindows.From(incident.Source));
}
