using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Search;

/// <summary>
/// Decides whether a search topic is covered by our Finnish-history corpus from its <c>bge-m3</c> nearest-article
/// cosine distances, and suggests the closest configured <see cref="SearchOptions.SuggestedTopics"/> when out of scope.
/// Degrades to in-scope if the embedder or corpus cannot be reached so transient outages never block search.
/// </summary>
public sealed partial class EmbeddingScopeCheck(
    IQueryEmbedder? embedder,
    IInvestigationCorpus? corpus,
    IOptions<SearchOptions> options,
    ILogger<EmbeddingScopeCheck> log) : IScopeCheck
{
    private const int NearestArticleCount = 5;

    // Short 1–3 word titles ("Talvisota", "Suomen sisällissota", "Civil War 1918") sit ~0.05–0.16 further from full
    // `# Title` + body article vectors in bge-m3 than sentence-length queries; see research/98-scope/CALIBRATION.md §2–3.
    private const double DirectTitleCredit = 0.165;

    private static readonly ScopeResult InScopeResult = new(true, []);

    private readonly ConcurrentDictionary<string, float[]> _chipVectors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _chipGate = new(1, 1);
    private int _warnedUnavailable;

    public async Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default)
    {
        var normalized = NormalizeWhitespace(topic);
        if (normalized.Length == 0)
        {
            return InScopeResult;
        }

        if (embedder is null || corpus is null)
        {
            WarnOnce(null, "Scope check dependencies are not registered; allowing search to proceed in-scope.");
            return InScopeResult;
        }

        float[] topicVector;
        IReadOnlyList<TopicArticle> hits;
        try
        {
            topicVector = await embedder.EmbedAsync(NormalizeForEmbedding(normalized), ct);
            hits = await corpus.NearestArticlesAsync(topicVector, NearestArticleCount, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WarnOnce(ex, "Scope check unavailable; allowing search to proceed in-scope.");
            return InScopeResult;
        }

        var cfg = options.Value;
        if (hits.Count == 0)
        {
            return new ScopeResult(false, await SuggestNearestAsync(topicVector, cfg, ct));
        }

        var credit = MatchesSuggestedTopic(normalized, cfg.SuggestedTopics) || MatchesArticleTitle(normalized, hits)
            ? DirectTitleCredit
            : 0.0;

        var top1 = hits[0].Distance - credit;
        var mean = hits.Average(static h => h.Distance) - credit;

        if (top1 <= cfg.Scope.MaxTopDistance && mean <= cfg.Scope.MaxMeanDistance)
        {
            return InScopeResult;
        }

        var suggestions = await SuggestNearestAsync(topicVector, cfg, ct);
        return new ScopeResult(false, suggestions);
    }

    private async Task<IReadOnlyList<string>> SuggestNearestAsync(
        float[] topicVector,
        SearchOptions cfg,
        CancellationToken ct)
    {
        var maxCount = Math.Max(0, cfg.Scope.MaxSuggestions);
        if (maxCount == 0 || cfg.SuggestedTopics.Count == 0)
        {
            return [];
        }

        var chips = cfg.SuggestedTopics
            .Where(static c => !string.IsNullOrWhiteSpace(c.Label) && !string.IsNullOrWhiteSpace(c.Topic))
            .ToList();
        if (chips.Count == 0)
        {
            return [];
        }

        try
        {
            if (chips.Any(c => !_chipVectors.ContainsKey(NormalizeForEmbedding(c.Topic))))
            {
                await _chipGate.WaitAsync(ct);
                try
                {
                    foreach (var chip in chips)
                    {
                        var key = NormalizeForEmbedding(chip.Topic);
                        if (!_chipVectors.ContainsKey(key))
                        {
                            _chipVectors[key] = await embedder!.EmbedAsync(key, ct);
                        }
                    }
                }
                finally
                {
                    _chipGate.Release();
                }
            }

            return chips
                .OrderBy(c => CosineDistance(topicVector, _chipVectors[NormalizeForEmbedding(c.Topic)]))
                .Select(static c => c.Label.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WarnOnce(ex, "Could not embed suggested topic chips; falling back to configured order.");
            return chips
                .Select(static c => c.Label.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();
        }
    }

    private static bool MatchesSuggestedTopic(string normalizedTopic, IReadOnlyList<SuggestedTopic> suggested)
    {
        foreach (var item in suggested)
        {
            if (string.Equals(NormalizeWhitespace(item.Topic), normalizedTopic, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeWhitespace(item.Label), normalizedTopic, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesArticleTitle(string normalizedTopic, IReadOnlyList<TopicArticle> hits)
    {
        if (normalizedTopic.Length < 4)
        {
            return false;
        }

        var multiWord = normalizedTopic.Contains(' ', StringComparison.Ordinal);
        foreach (var hit in hits)
        {
            var title = NormalizeWhitespace(hit.Title);
            if (string.Equals(title, normalizedTopic, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (multiWord &&
                (title.StartsWith(normalizedTopic + " ", StringComparison.OrdinalIgnoreCase) ||
                 title.StartsWith(normalizedTopic + " (", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    internal static string NormalizeForEmbedding(string text)
    {
        var norm = NormalizeWhitespace(text);
        if (norm.Length == 0 || !char.IsLower(norm[0]))
        {
            return norm;
        }
        return string.Concat(char.ToUpperInvariant(norm[0]).ToString(), norm.AsSpan(1));
    }

    private static string NormalizeWhitespace(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : Whitespace().Replace(text.Normalize(NormalizationForm.FormC).Trim(), " ");

    private static double CosineDistance(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 1.0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA * normB);
        return denom <= 0 ? 1.0 : 1.0 - (dot / denom);
    }

    private void WarnOnce(Exception? ex, string message)
    {
        if (Interlocked.Exchange(ref _warnedUnavailable, 1) == 0)
        {
            if (ex is not null)
            {
                log.LogWarning(ex, "{Message}", message);
            }
            else
            {
                log.LogWarning("{Message}", message);
            }
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
