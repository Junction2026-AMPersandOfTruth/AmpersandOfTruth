using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public sealed class FakeIncidentCorpus : IIncidentCorpus
{
    private int _revisionCalls;
    private int _articleCalls;

    public List<RevisionRow> Revisions { get; } = [];
    public Dictionary<long, ArticleRef> Articles { get; } = [];
    public bool Unavailable { get; set; }
    public Exception? Fault { get; set; }
    public TimeSpan Delay { get; set; }
    public int RevisionCalls => Volatile.Read(ref _revisionCalls);
    public int ArticleCalls => Volatile.Read(ref _articleCalls);

    /// <summary>Article and revision lookups in flight, counted together.</summary>
    public InFlightCalls Calls { get; } = new();

    public async Task<IReadOnlyList<RevisionRow>> RevisionsAsync(
        long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _revisionCalls);
        using var call = await Calls.EnterAsync(ct);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }

        ThrowIfDown();
        return Revisions
            .Where(r => r.PageId == pageId && revIds.Contains(r.RevId))
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.RevId)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<long, ArticleRef>> ArticlesAsync(
        IReadOnlyCollection<long> pageIds, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _articleCalls);
        using var call = await Calls.EnterAsync(ct);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }

        ThrowIfDown();
        var dict = new Dictionary<long, ArticleRef>();
        foreach (var id in pageIds)
        {
            if (Articles.TryGetValue(id, out var article))
            {
                dict[id] = article;
            }
        }

        return dict;
    }

    private void ThrowIfDown()
    {
        if (Fault is not null)
        {
            throw Fault;
        }

        if (Unavailable)
        {
            throw new CorpusUnavailableException("fake incident corpus is down");
        }
    }
}
