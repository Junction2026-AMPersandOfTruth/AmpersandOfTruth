using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Articles;

public sealed class FakeArticleCorpus : IArticleCorpus
{
    public bool Unavailable { get; set; }

    public int AggregatesCalls { get; private set; }

    public int RevisionsCalls { get; private set; }

    public Dictionary<long, ArticleAggregates> Articles { get; } = [];

    public Dictionary<long, List<ArticleRevision>> Revisions { get; } = [];

    public Task<ArticleAggregates?> AggregatesAsync(long pageId, CancellationToken ct = default)
    {
        AggregatesCalls++;
        if (Unavailable)
        {
            throw new CorpusUnavailableException("Corpus 127.0.0.1:3307/corpus not available: connection refused");
        }

        Articles.TryGetValue(pageId, out var agg);
        return Task.FromResult(agg);
    }

    public Task<IReadOnlyList<ArticleRevision>> RevisionsAsync(
        long pageId,
        DateRange range,
        int limit = 500,
        CancellationToken ct = default)
    {
        RevisionsCalls++;
        if (Unavailable)
        {
            throw new CorpusUnavailableException("Corpus 127.0.0.1:3307/corpus not available: connection refused");
        }

        if (!Revisions.TryGetValue(pageId, out var list))
        {
            return Task.FromResult<IReadOnlyList<ArticleRevision>>([]);
        }

        IReadOnlyList<ArticleRevision> filtered = list
            .Where(r => r.Ts >= range.From && r.Ts < range.To)
            .OrderByDescending(r => r.Ts)
            .ThenByDescending(r => r.RevId)
            .Take(limit)
            .ToList();

        return Task.FromResult(filtered);
    }
}
