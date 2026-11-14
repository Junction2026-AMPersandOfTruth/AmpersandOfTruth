using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread.Planning;

/// <summary>
/// P3.2: claim → searches. One cached <c>plan-claim-search</c> LLM call (keywords + cross-language) plus the
/// deterministic distinctive fragment (exact phrase), each run per time window when the provider can date-restrict
/// (before / during / after the Wikipedia exposure), ordered by priority and cut at <c>WebSpread:MaxSearches</c>.
/// The LLM failing only weakens the plan (rarest-words fallback); it never ends the trace. Singleton.
/// </summary>
public sealed class ClaimSearchPlanner(
    IAnalysisModel llm,
    IWebSearchClient search,
    IOptionsMonitor<WebSpreadOptions> options,
    TimeProvider time,
    ILogger<ClaimSearchPlanner> log)
{
    public async Task<ClaimSearchPlan> PlanAsync(WebSpreadClaim claim, CancellationToken ct = default)
    {
        var opt = options.CurrentValue;
        var degraded = new List<string>();
        var basics = new List<(string Text, string Lang, QueryKind Kind)>();

        // 1. Exact phrase from the inserted wording (Finnish).
        var fragment = DistinctiveFragment.Pick(claim.InsertedText, opt.FragmentMinWords, opt.FragmentMaxWords);
        if (fragment is not null) basics.Add(($"\"{fragment}\"", "fi", QueryKind.ExactPhrase));
        else degraded.Add("no inserted wording to quote");

        // 2. Keywords + translations from the LLM.
        string? detail = null, notes = null;
        if (opt.UseLlmPlan)
        {
            var result = await llm.RunAsync<ClaimQueryExpansion>(
                new AnalysisRequest(PlanClaimSearchPrompt.Id, PlanClaimSearchPrompt.Variables(claim, opt.Languages)) { Scope = claim.Scope }, ct);
            if (result.IsOk)
            {
                detail = result.Value.ContestedDetail;
                notes = string.IsNullOrWhiteSpace(result.Value.Notes) ? null : result.Value.Notes;
                foreach (var q in result.Value.Queries.Where(q => opt.Languages.Contains(q.Lang, StringComparer.OrdinalIgnoreCase)))
                {
                    var kind = string.Equals(q.Kind, "translation", StringComparison.OrdinalIgnoreCase) ? QueryKind.Translation : QueryKind.Keywords;
                    basics.Add((q.Text.Replace("\"", "").Trim(), q.Lang.ToLowerInvariant(), kind));
                }
            }
            else
            {
                log.LogWarning("plan-claim-search for {Incident} returned {Outcome} ({Error}); using rarest-word queries", claim.IncidentId, result.Outcome, result.Error);
                degraded.Add($"query expansion unavailable ({result.Outcome})");
            }
        }
        if (!basics.Any(b => b.Kind != QueryKind.ExactPhrase))
        {
            var words = DistinctiveFragment.Keywords(claim.InsertedText ?? claim.ClaimFi);
            if (words.Count > 0) basics.Add((string.Join(' ', words), "fi", QueryKind.Keywords));
            if (!string.IsNullOrWhiteSpace(claim.ClaimEn) && opt.Languages.Contains("en")) basics.Add((claim.ClaimEn!, "en", QueryKind.Keywords));
        }

        // 3. × time windows.
        var windows = search.SupportsDateWindow
            ? ExposureWindows.SearchWindows(claim.Windows, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
            : [(SearchWindowKind.Any, new DateWindow(null, null))];
        if (!search.SupportsDateWindow) degraded.Add("search can't be date-restricted; one search per query, dated afterwards");

        var all = basics
            .DistinctBy(b => (b.Text.ToLowerInvariant(), b.Lang))
            .Select((b, i) => (b, Priority: Priority(b.Kind, b.Lang, i)))
            .OrderBy(x => x.Priority)
            .SelectMany(x => windows.Select(w => new PlannedQuery(
                opt.ExcludeWikipedia ? $"{x.b.Text} -site:wikipedia.org" : x.b.Text,
                x.b.Lang, x.b.Kind, w.Item1, w.Item1 == SearchWindowKind.Any ? null : w.Item2)))
            .ToList();

        var kept = all.Take(Math.Max(0, opt.MaxSearches)).ToList();
        return new ClaimSearchPlan(kept, all.Count - kept.Count, detail, notes, degraded.Count == 0 ? null : string.Join("; ", degraded));
    }

    /// <summary>Exact phrase first (copies are the strongest evidence), then Finnish keywords, then other languages.</summary>
    private static int Priority(QueryKind kind, string lang, int order) => kind switch
    {
        QueryKind.ExactPhrase => 0,
        QueryKind.Keywords when lang == "fi" => 100 + order,
        QueryKind.Keywords => 200 + order,
        _ => 300 + order,
    };
}
