using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>Progress of one trace, for P3.7's staged progress (building queries → searching → reading pages → done).</summary>
public abstract record WebSpreadEvent;

public sealed record WebSpreadPlanned(ClaimSearchPlan Plan) : WebSpreadEvent;

public sealed record WebSpreadSearched(PlannedQuery Query, WebCallOutcome Outcome, int Results, bool FromCache, string? Error) : WebSpreadEvent;

/// <summary>Pages about to be read (after de-duplication and the skip list).</summary>
public sealed record WebSpreadReading(int Pages, int Mirrors) : WebSpreadEvent;

/// <summary>A page confirmed to carry or refute the claim — streamed as soon as it is classified.</summary>
public sealed record WebSpreadSourceFound(WebSpreadSource Source) : WebSpreadEvent;

/// <summary>Always the last event (cancellation ends the stream with <see cref="OperationCanceledException"/> instead).</summary>
public sealed record WebSpreadCompleted(WebSpreadResult Result) : WebSpreadEvent;

/// <summary>
/// One web-spread trace for one incident (P3, issue #5): plan (P3.2) → dated searches (P3.3) → fetch → claim match (P3.4)
/// → dating (P3.5) → before/during/after (P3.6). Runs only when a user asks, per incident. Never throws for provider or
/// LLM problems: they degrade the result (<see cref="WebSpreadStatus.Partial"/>, notes). Singleton; thread-safe.
/// </summary>
public sealed class WebSpreadTracer(
    ClaimSearchPlanner planner,
    IWebSearchClient search,
    IPageFetcher fetcher,
    ClaimMatcher matcher,
    IWebArchive archive,
    IOptionsMonitor<WebSpreadOptions> options,
    TimeProvider time,
    ILogger<WebSpreadTracer> log)
{
    /// <summary>Bump when a trace's output changes: stored results (<see cref="Store.WebSpreadResults"/>) of other versions are ignored.</summary>
    public const int Version = 2;

    private sealed class Candidate(string url)
    {
        public string Url { get; } = url;
        public int BestRank { get; set; } = int.MaxValue;
        public string? Title { get; set; }
        public DateOnly? SerpDate { get; set; }
        public HashSet<QueryKind> FoundBy { get; } = [];
        public bool ByExactPhrase => FoundBy.Contains(QueryKind.ExactPhrase);
    }

    public async IAsyncEnumerable<WebSpreadEvent> TraceAsync(WebSpreadClaim claim, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var opt = options.CurrentValue;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var notes = new List<string>();

        // ── 1. Plan ──
        var plan = await planner.PlanAsync(claim, ct);
        if (plan.ExpansionFailed) notes.Add("The automated assessment was unavailable while planning, so only simple keyword searches ran.");
        if (plan.NoInsertedText && plan.Queries.Count > 0) notes.Add("The incident's inserted wording wasn't available, so no word-for-word search ran.");
        if (plan.Dropped > 0) notes.Add($"{plan.Dropped} planned searches were skipped by the per-trace search limit.");
        yield return new WebSpreadPlanned(plan);

        // ── 2. Search (sequential: the plan is small and ordered by priority, and the provider has its own limit) ──
        var candidates = new Dictionary<string, Candidate>();
        int ok = 0, failed = 0, budget = 0, notConfigured = 0;
        foreach (var q in plan.Queries)
        {
            var response = await search.SearchAsync(new WebSearchRequest(q.Text, q.Language, q.Window, opt.ResultsPerSearch) { Scope = claim.Scope }, ct);
            switch (response.Outcome)
            {
                case WebCallOutcome.Ok: ok++; break;
                case WebCallOutcome.BudgetExceeded: budget++; break;
                case WebCallOutcome.NotConfigured: notConfigured++; break;
                default: failed++; break;
            }
            foreach (var r in response.Results)
            {
                var key = UrlPolicy.Normalize(r.Url);
                if (!candidates.TryGetValue(key, out var c)) candidates[key] = c = new Candidate(r.Url);
                c.BestRank = Math.Min(c.BestRank, r.Rank);
                c.Title ??= r.Title;
                c.SerpDate ??= r.SerpDate;
                c.FoundBy.Add(q.Kind);
            }
            yield return new WebSpreadSearched(q, response.Outcome, response.Results.Count, response.FromCache, response.Error);
            if (response.Outcome is WebCallOutcome.NotConfigured or WebCallOutcome.BudgetExceeded) break; // the rest would fail the same way
        }

        if (ok == 0)
        {
            var status = notConfigured > 0 ? WebSpreadStatus.NotConfigured : budget > 0 ? WebSpreadStatus.BudgetExceeded : WebSpreadStatus.Failed;
            notes.Add(status switch
            {
                WebSpreadStatus.NotConfigured => "Web search is not configured on this deployment.",
                WebSpreadStatus.BudgetExceeded => "The web request limit was reached before any search ran.",
                _ => plan.Queries.Count == 0 ? "There was nothing to search for: the incident has no claim text." : "Every web search failed.",
            });
            yield return new WebSpreadCompleted(new WebSpreadResult(claim, status, plan, [], null,
                SpreadClassifier.Headline([], 0, opt.EffectiveLanguages), 0, 0, notes)
            {
                AssessmentDegraded = plan.ExpansionFailed,
                TracedAt = time.GetUtcNow(),
            });
            yield break;
        }
        if (failed > 0) notes.Add($"{failed} of {ok + failed + budget} searches failed.");
        if (budget > 0) notes.Add("The web request limit for this trace was reached; results are partial.");

        // ── 3. Choose pages: exact-phrase hits first, then by rank; Wikipedia and files never; a few mirrors ──
        var ordered = candidates.Values
            .Select(c => (c, Kind: UrlPolicy.Classify(c.Url)))
            .Where(x => x.Kind is UrlKind.Page or UrlKind.Mirror)
            .OrderByDescending(x => x.c.ByExactPhrase).ThenBy(x => x.c.BestRank)
            .ToList();
        var mirrors = ordered.Where(x => x.Kind == UrlKind.Mirror).Take(opt.MaxMirrorsToFetch).Select(x => x.c).ToList();
        var pages = ordered.Where(x => x.Kind == UrlKind.Page).Take(opt.MaxPagesToFetch).Select(x => x.c).ToList();
        yield return new WebSpreadReading(pages.Count, mirrors.Count);

        // ── 4. Fetch → match → date → bucket, in parallel; sources stream out as they are confirmed ──
        var channel = Channel.CreateUnbounded<WebSpreadSource>();
        var fetchFailures = 0;
        var pagesRead = 0;
        var unassessed = 0;
        var sources = new ConcurrentBag<WebSpreadSource>();
        var work = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(mirrors.Concat(pages), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, opt.FetchParallelism),
                    CancellationToken = ct,
                }, async (c, token) =>
                {
                    var read = await ReadAsync(claim, c, today, opt, token);
                    if (!read.Read) { Interlocked.Increment(ref fetchFailures); return; }
                    Interlocked.Increment(ref pagesRead);
                    if (read.AssessmentFailed) Interlocked.Increment(ref unassessed);
                    if (read.Source is { } s)
                    {
                        sources.Add(s);
                        await channel.Writer.WriteAsync(s, token);
                    }
                });
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        await foreach (var s in channel.Reader.ReadAllAsync(ct))
            yield return new WebSpreadSourceFound(s);
        await work;

        if (fetchFailures > 0) notes.Add(fetchFailures == 1 ? "1 page could not be read." : $"{fetchFailures} pages could not be read.");
        if (unassessed > 0) notes.Add(unassessed == 1
            ? "1 page was compared on wording only: the automated assessment was unavailable for it."
            : $"{unassessed} pages were compared on wording only: the automated assessment was unavailable for them.");
        if (!search.SupportsDateWindow) notes.Add("The search provider can't restrict by date, so older pages may be under-represented.");
        notes.Add("Timing shows sequence, not causation.");

        // ── 5. Result ──
        var list = sources.OrderBy(s => s.Bucket).ThenBy(s => s.Dating.EffectiveDate ?? DateOnly.MaxValue).ToList();
        var resultStatus = failed > 0 || budget > 0 || fetchFailures > 0 ? WebSpreadStatus.Partial : WebSpreadStatus.Completed;
        yield return new WebSpreadCompleted(new WebSpreadResult(claim, resultStatus, plan, list, SpreadClassifier.Earliest(list),
            SpreadClassifier.Headline(list, ok, opt.EffectiveLanguages), ok, pagesRead, notes)
        {
            AssessmentDegraded = plan.ExpansionFailed || unassessed > 0,
            TracedAt = time.GetUtcNow(),
        });
    }

    /// <param name="Read">False = the page couldn't be fetched.</param>
    /// <param name="Source">Null = read, but it doesn't carry or refute the claim.</param>
    /// <param name="AssessmentFailed">The <c>match-claim</c> call failed for this page (lexical fallback only).</param>
    private sealed record PageRead(bool Read, WebSpreadSource? Source, bool AssessmentFailed = false);

    private async Task<PageRead> ReadAsync(WebSpreadClaim claim, Candidate c, DateOnly today, WebSpreadOptions opt, CancellationToken ct)
    {
        var fetched = await fetcher.FetchAsync(c.Url, claim.Scope, ct);
        if (fetched is not { Outcome: WebCallOutcome.Ok, Page: { } page })
        {
            log.LogInformation("Web spread: {Url} not read ({Outcome}: {Error})", c.Url, fetched.Outcome, fetched.Error);
            return new PageRead(false, null);
        }

        var match = await matcher.MatchAsync(claim, page, ct);
        if (!match.IsRelevant) return new PageRead(true, null, match.AssessmentFailed);

        var firstCapture = opt.UseArchive ? await archive.FirstCaptureAsync(c.Url, ct) : null;
        var dating = PageDateExtractor.Extract(page.FinalUrl ?? c.Url, page.Html, c.SerpDate, firstCapture);
        var (bucket, near) = SpreadClassifier.Bucket(dating.EffectiveDate, claim.Windows, today);
        return new PageRead(true, new WebSpreadSource(
            c.Url, UrlPolicy.Domain(c.Url), page.Title ?? c.Title, match.SourceType, match.Stance, match.MatchType, match.Quote,
            match.CitesWikipedia, bucket, near, SpreadClassifier.LikelyFromWikipedia(bucket, match), dating, [.. c.FoundBy]),
            match.AssessmentFailed);
    }
}
