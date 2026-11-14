using System.Diagnostics;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Embedding;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Stage 1 = P1.R3 mode D, the R6 decision (#27), ported from <c>research/24-r3-query/variant.py</c>:
/// <list type="number">
/// <item>One cached <c>expand-topic</c> LLM call (job <c>QueryExpand</c>) turns the topic into Finnish, 3–5 reformulations,
/// fi title guesses and a scope verdict.</item>
/// <item>Title guesses are looked up on fi <c>article.title</c> (dash-normalised, accent-sensitive, case-insensitive); a guess
/// with no exact match falls back to the shortest titles starting with it (at most 2, guesses of 4+ characters).</item>
/// <item>Multi-query: the raw topic, <c>topic_fi</c> and the reformulations are each embedded (bge-m3) and searched like R1
/// (all languages, folded onto fi), and the arms are fused by reciprocal rank (k = 60).</item>
/// <item>Title hits come first, in guess order, then the fused vector hits; the top <c>ArticleLimit</c> are returned.</item>
/// </list>
/// The scope verdict is returned as a warning, never a block. If the LLM call fails (outcome ≠ Ok), the topic is answered
/// by R1 (<see cref="VectorTopicSearch"/>) and the result is marked <see cref="TopicSearchResult.Fallback"/>.
/// </summary>
public sealed class ExpandedTopicSearch(
    IAnalysisModel model,
    VectorTopicSearch r1,
    IQueryEmbedder embedder,
    IInvestigationCorpus corpus,
    PromptLibrary prompts,
    IOptions<GeminiOptions> gemini,
    IOptions<InvestigationPipelineOptions> options,
    ILogger<ExpandedTopicSearch> log) : ITopicSearch
{
    /// <summary>Bump when the search's logic changes what it returns; part of the run-cache fingerprint.</summary>
    public const int Version = 1;

    internal const double RrfK = 60;
    /// <summary>Each vector arm fetches max(limit, 20) × 3 nearest vectors before folding (as the research variant did).</summary>
    internal const int MinArmDepth = 20;
    internal const int PrefixRowLimit = 40;
    internal const int MaxPrefixPerMiss = 2;
    internal const int MinPrefixLength = 4;

    private string? _fingerprint;

    public string Fingerprint => _fingerprint ??=
        $"r3d.{Version};expand={prompts.Get(ExpandTopicPrompt.Id).FullVersion};model={gemini.Value.ModelFor(AnalysisJob.QueryExpand)}";

    public async Task<TopicSearchResult> SearchAsync(string topic, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var k = options.Value.ArticleLimit;

        var request = new AnalysisRequest(ExpandTopicPrompt.Id, ExpandTopicPrompt.Variables(topic))
        {
            Scope = $"topic-search:{CachedQueryEmbedder.Normalize(topic)}",
        };
        var expansion = await model.RunAsync<TopicExpansion>(request, ct);
        if (!expansion.IsOk)
        {
            log.LogWarning("expand-topic for {Topic} returned {Outcome} ({Error}); answering with R1 vector search",
                topic, expansion.Outcome, expansion.Error);
            return new TopicSearchResult(await r1.FindArticlesAsync(topic, ct)) { Fallback = true };
        }

        var x = expansion.Value;
        var queries = DistinctQueries([topic, x.TopicFi, .. x.Reformulations ?? []]);
        var titlesTask = TitleHitsAsync(CleanTitleGuesses(x.TitleGuesses), ct);
        var armsTask = VectorArmsAsync(queries, Math.Max(k, MinArmDepth) * 3, ct);
        await Task.WhenAll(titlesTask, armsTask);

        var articles = Merge(titlesTask.Result, armsTask.Result, k);
        log.LogInformation(
            "Topic search {Topic}: in_scope={InScope}, {Queries} queries, {Titles} title hits, {Articles} articles, llm cached={Cached}, {Ms:0} ms",
            topic, x.InScope, queries.Count, titlesTask.Result.Count, articles.Count, expansion.Info.FromCache,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new TopicSearchResult(articles) { Scope = new TopicScope(x.InScope, x.ScopeReason) };
    }

    /// <summary>
    /// One R1 search per query, folded onto fi. Embeddings run one at a time (the VPS Ollama serves one query at a time);
    /// each arm's SQL starts as soon as its vector is ready, so the searches overlap.
    /// </summary>
    private async Task<IReadOnlyList<IReadOnlyList<TopicArticle>>> VectorArmsAsync(IReadOnlyList<string> queries, int depth, CancellationToken ct)
    {
        var searches = new List<Task<IReadOnlyList<TopicArticle>>>(queries.Count);
        foreach (var q in queries)
        {
            var vector = await embedder.EmbedAsync(q, ct);
            searches.Add(corpus.NearestArticlesAsync(vector, depth, ct));
        }
        var hits = await Task.WhenAll(searches);
        var siblings = await VectorTopicSearch.SiblingsAsync(corpus, hits.SelectMany(h => h), ct);
        return [.. hits.Select(h => (IReadOnlyList<TopicArticle>)[.. VectorTopicSearch.Fold(h, siblings)])];
    }

    /// <summary><c>lookup_titles_fi</c>: exact matches in guess order, then up to 2 shortest prefix matches per unmatched guess.</summary>
    private async Task<IReadOnlyList<TopicArticle>> TitleHitsAsync(IReadOnlyList<string> guesses, CancellationToken ct)
    {
        if (guesses.Count == 0) return [];
        var normalized = guesses.Select(TitleKeys.NormalizeDashes).ToList();
        var exact = (await corpus.FiArticlesByTitleAsync(normalized, ct))
            .GroupBy(a => TitleKeys.Key(a.Title))
            .ToDictionary(g => g.Key, g => g.ToList());

        var hits = new List<TopicArticle>();
        var seen = new HashSet<long>();
        var unresolved = new List<string>();
        foreach (var guess in normalized)
        {
            if (exact.TryGetValue(TitleKeys.Key(guess), out var matched))
                hits.AddRange(matched.Where(m => seen.Add(m.PageId)));
            else if (guess.Length >= MinPrefixLength)
                unresolved.Add(guess);
        }

        if (unresolved.Count > 0)
        {
            var rows = await corpus.FiArticlesByTitlePrefixAsync(unresolved, PrefixRowLimit, ct);
            foreach (var guess in unresolved)
            {
                var prefix = TitleKeys.Key(guess);
                hits.AddRange(rows
                    .Where(r => TitleKeys.Key(r.Title).StartsWith(prefix, StringComparison.Ordinal) && !seen.Contains(r.PageId))
                    .Take(MaxPrefixPerMiss)
                    .Where(r => seen.Add(r.PageId)));
            }
        }
        return hits;
    }

    /// <summary>
    /// Mode D's ranking: title hits first (distance = their best vector distance in any arm, else 1), then the arms fused by
    /// reciprocal rank — ties by best rank, then best distance, then page id. The top <paramref name="k"/>.
    /// </summary>
    internal static IReadOnlyList<TopicArticle> Merge(
        IReadOnlyList<TopicArticle> titleHits, IReadOnlyList<IReadOnlyList<TopicArticle>> arms, int k)
    {
        var rrf = new Dictionary<long, double>();
        var bestRank = new Dictionary<long, int>();
        var closest = new Dictionary<long, TopicArticle>();
        foreach (var arm in arms)
        {
            for (var i = 0; i < arm.Count; i++)
            {
                var hit = arm[i];
                var rank = i + 1;
                rrf[hit.PageId] = rrf.GetValueOrDefault(hit.PageId) + 1.0 / (RrfK + rank);
                if (!bestRank.TryGetValue(hit.PageId, out var r) || rank < r) bestRank[hit.PageId] = rank;
                if (!closest.TryGetValue(hit.PageId, out var c) || hit.Distance < c.Distance) closest[hit.PageId] = hit;
            }
        }

        var fused = rrf.Keys
            .OrderByDescending(p => rrf[p]).ThenBy(p => bestRank[p]).ThenBy(p => closest[p].Distance).ThenBy(p => p)
            .Take(k)
            .Select(p => closest[p]);

        var result = new List<TopicArticle>(k);
        var seen = new HashSet<long>();
        foreach (var t in titleHits)
        {
            if (seen.Add(t.PageId))
                result.Add(t with { Distance = closest.TryGetValue(t.PageId, out var c) ? c.Distance : 1, ViaLang = null, TitleMatch = true });
        }
        result.AddRange(fused.Where(a => seen.Add(a.PageId)));
        return result.Count > k ? result[..k] : result;
    }

    /// <summary>Title guesses as the research variant cleaned them: underscores → spaces, brackets trimmed, whitespace
    /// collapsed, duplicates (dash-normalised, case-folded) dropped.</summary>
    internal static IReadOnlyList<string> CleanTitleGuesses(IReadOnlyList<string>? guesses)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in guesses ?? [])
        {
            if (raw is null) continue;
            var cleaned = Collapse(raw.Replace('_', ' ').Trim(' ', '[', ']'));
            if (cleaned.Length > 0 && seen.Add(TitleKeys.Key(cleaned))) result.Add(cleaned);
        }
        return result;
    }

    /// <summary>The queries embedded: whitespace collapsed, empty and case-insensitive duplicates dropped, order kept.</summary>
    internal static IReadOnlyList<string> DistinctQueries(IEnumerable<string?> queries)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in queries)
        {
            var norm = Collapse(q ?? "");
            if (norm.Length > 0 && seen.Add(norm.ToLowerInvariant())) result.Add(norm);
        }
        return result;
    }

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
