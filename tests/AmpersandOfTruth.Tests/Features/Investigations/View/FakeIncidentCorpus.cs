using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public sealed class FakeIncidentCorpus : IIncidentCorpus
{
    private int _revisionCalls;
    private int _articleCalls;
    private int _inFlight;
    private int _maxInFlight;

    public List<RevisionRow> Revisions { get; } = [];
    public Dictionary<long, ArticleRef> Articles { get; } = [];
    public bool Unavailable { get; set; }
    public Exception? Fault { get; set; }
    public TimeSpan Delay { get; set; }
    public int RevisionCalls => Volatile.Read(ref _revisionCalls);
    public int ArticleCalls => Volatile.Read(ref _articleCalls);

    /// <summary>Most article/revision lookups that were in flight at the same time (concurrency proof without wall-clock timing).</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    private async Task DelayAsync(CancellationToken ct)
    {
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen) { }
        try
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public async Task<IReadOnlyList<RevisionRow>> RevisionsAsync(
        long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _revisionCalls);
        await DelayAsync(ct);

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
        await DelayAsync(ct);

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
