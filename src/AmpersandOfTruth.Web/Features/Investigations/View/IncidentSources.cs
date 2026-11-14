using AmpersandOfTruth.Web.Features.Investigations.Fixtures;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>Fixture-mode <see cref="IIncidentSource"/>: maps <see cref="SampleIncidents.Match"/> incidents ranked 1..n.</summary>
public sealed class FixtureIncidentSource(SampleIncidents samples, IncidentMapper mapper) : IIncidentSource
{
    public async Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default)
    {
        var topic = samples.Match(topicSlug);
        if (topic is null)
        {
            return null;
        }

        var ordered = topic.Incidents.OrderByDescending(i => i.Score).ToList();
        var views = new List<IncidentView>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            views.Add(await mapper.MapAsync(ordered[i], i + 1, ct));
        }

        return views;
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
