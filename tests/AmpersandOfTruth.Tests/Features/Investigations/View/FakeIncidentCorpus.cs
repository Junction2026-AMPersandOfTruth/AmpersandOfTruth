using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public sealed class FakeIncidentCorpus : IIncidentCorpus
{
    public List<RevisionRow> Revisions { get; } = [];
    public Dictionary<long, ArticleRef> Articles { get; } = [];
    public bool Unavailable { get; set; }
    public int RevisionCalls { get; private set; }
    public int ArticleCalls { get; private set; }

    public Task<IReadOnlyList<RevisionRow>> RevisionsAsync(
        long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default)
    {
        RevisionCalls++;
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<RevisionRow>>(Revisions
            .Where(r => r.PageId == pageId && revIds.Contains(r.RevId))
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.RevId)
            .ToList());
    }

    public Task<IReadOnlyDictionary<long, ArticleRef>> ArticlesAsync(
        IReadOnlyCollection<long> pageIds, CancellationToken ct = default)
    {
        ArticleCalls++;
        ThrowIfDown();
        var dict = new Dictionary<long, ArticleRef>();
        foreach (var id in pageIds)
        {
            if (Articles.TryGetValue(id, out var article))
            {
                dict[id] = article;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<long, ArticleRef>>(dict);
    }

    private void ThrowIfDown()
    {
        if (Unavailable)
        {
            throw new CorpusUnavailableException("fake incident corpus is down");
        }
    }
}
