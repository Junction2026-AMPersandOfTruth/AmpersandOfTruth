using System.Collections.Concurrent;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>Fixture-mode <see cref="IIncidentSource"/>: maps <see cref="SampleIncidents.Match"/> incidents ranked 1..n.</summary>
public sealed class FixtureIncidentSource(SampleIncidents samples, IncidentMapper mapper) : IIncidentSource
{
    private const int MaxInFlight = 4;

    private readonly ConcurrentDictionary<string, IReadOnlyList<IncidentView>> _cache = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default)
    {
        var topic = samples.Match(topicSlug);
        if (topic is null)
        {
            return null;
        }

        var slug = TopicSlug.From(topic.Slug);
        if (_cache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        var ordered = topic.Incidents.OrderByDescending(i => i.Score).ToList();
        using var gate = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        var tasks = new Task<IncidentView>[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            tasks[i] = MapThrottledAsync(ordered[i], i + 1, gate, ct);
        }

        var views = await Task.WhenAll(tasks);
        if (views.All(v => v.Passage is not null))
        {
            _cache.TryAdd(slug, views);
        }

        return views;
    }

    private async Task<IncidentView> MapThrottledAsync(
        IncidentRecord incident,
        int rank,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await mapper.MapAsync(incident, rank, ct);
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>
/// Thread-safe in-memory LRU buffer of the last <see cref="MaxTopics"/> completed pipeline runs in this process
/// (until persistent run cache #40 lands).
/// </summary>
public sealed class RecentRuns
{
    public const int MaxTopics = 20;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<IncidentRecord>> _bySlug = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();

    public IReadOnlyList<string> Topics
    {
        get
        {
            lock (_gate)
            {
                var slugs = new string[_order.Count];
                var i = 0;
                for (var node = _order.Last; node is not null; node = node.Previous)
                {
                    slugs[i++] = node.Value;
                }

                return slugs;
            }
        }
    }

    public void Record(string slug, IReadOnlyList<IncidentRecord> incidents)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(incidents);

        var key = TopicSlug.From(slug);
        if (key.Length == 0)
        {
            return;
        }

        var snapshot = incidents.OrderByDescending(i => i.Score).ToArray();
        lock (_gate)
        {
            if (_bySlug.ContainsKey(key))
            {
                _order.Remove(key);
            }

            _bySlug[key] = snapshot;
            _order.AddLast(key);

            while (_order.Count > MaxTopics)
            {
                var oldest = _order.First!.Value;
                _order.RemoveFirst();
                _bySlug.Remove(oldest);
            }
        }
    }

    public IReadOnlyList<IncidentRecord>? TryGet(string topicOrSlug)
    {
        if (string.IsNullOrWhiteSpace(topicOrSlug))
        {
            return null;
        }

        var key = TopicSlug.From(topicOrSlug);
        if (key.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            return _bySlug.GetValueOrDefault(key);
        }
    }
}

/// <summary>Pipeline-mode <see cref="IIncidentSource"/> backed by <see cref="RecentRuns"/>.</summary>
public sealed class RecentRunIncidentSource(RecentRuns runs, IncidentMapper mapper) : IIncidentSource
{
    public async Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default)
    {
        var incidents = runs.TryGet(topicSlug);
        if (incidents is null)
        {
            return null;
        }

        var views = new List<IncidentView>(incidents.Count);
        for (var i = 0; i < incidents.Count; i++)
        {
            views.Add(await mapper.MapAsync(incidents[i], i + 1, ct));
        }

        return views;
    }
}
