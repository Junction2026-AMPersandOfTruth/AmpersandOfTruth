using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.TimeMachine;

namespace AmpersandOfTruth.Tests.Features.TimeMachine;

public sealed class FakeTimeMachineCorpus : ITimeMachineCorpus
{
    private readonly Dictionary<long, List<RevisionAt>> _revisions = new();
    private readonly Dictionary<(long PageId, long RevId), string> _texts = new();
    private readonly Dictionary<long, (string TitleFi, string? TitleEn)> _titles = new();

    public bool Unavailable { get; set; }

    public FakeTimeMachineCorpus WithTitle(long pageId, string titleFi, string? titleEn = null)
    {
        _titles[pageId] = (titleFi, titleEn);
        return this;
    }

    public FakeTimeMachineCorpus AddRevision(
        long pageId,
        long revId,
        long? parentId,
        DateTime tsUtc,
        string? user,
        bool anonymous,
        string wikitext,
        int? bytes = null)
    {
        var utc = DateTime.SpecifyKind(tsUtc, DateTimeKind.Utc);
        var computedBytes = bytes ?? System.Text.Encoding.UTF8.GetByteCount(wikitext);

        if (!_revisions.TryGetValue(pageId, out var list))
        {
            list = [];
            _revisions[pageId] = list;
        }

        list.RemoveAll(r => r.RevId == revId);
        list.Add(new RevisionAt(pageId, revId, parentId, utc, user, anonymous, computedBytes));
        _texts[(pageId, revId)] = wikitext;
        return this;
    }

    public Task<RevisionAt?> RevisionAtAsync(long pageId, DateTime atUtc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        if (!_revisions.TryGetValue(pageId, out var list) || list.Count == 0)
        {
            return Task.FromResult<RevisionAt?>(null);
        }

        var utc = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        var match = list
            .Where(r => r.Ts <= utc)
            .OrderByDescending(r => r.Ts)
            .ThenByDescending(r => r.RevId)
            .FirstOrDefault();

        return Task.FromResult(match);
    }

    public Task<RevisionAt?> RevisionByIdAsync(long pageId, long revId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        if (!_revisions.TryGetValue(pageId, out var list))
        {
            return Task.FromResult<RevisionAt?>(null);
        }

        return Task.FromResult(list.FirstOrDefault(r => r.RevId == revId));
    }

    public Task<(DateTime First, DateTime Last, int Count)?> RangeAsync(long pageId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        if (!_revisions.TryGetValue(pageId, out var list) || list.Count == 0)
        {
            return Task.FromResult<(DateTime First, DateTime Last, int Count)?>(null);
        }

        var first = list.Min(r => r.Ts);
        var last = list.Max(r => r.Ts);
        return Task.FromResult<(DateTime First, DateTime Last, int Count)?>((first, last, list.Count));
    }

    public Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        return Task.FromResult(_texts.TryGetValue((pageId, revId), out var text) ? text : null);
    }

    public Task<(string TitleFi, string? TitleEn)?> ArticleTitleAsync(long pageId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        return Task.FromResult(_titles.TryGetValue(pageId, out var title) ? ((string, string?)?)title : null);
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new CorpusUnavailableException("Corpus 127.0.0.1:3307/corpus not available: connection refused");
        }
    }
}
