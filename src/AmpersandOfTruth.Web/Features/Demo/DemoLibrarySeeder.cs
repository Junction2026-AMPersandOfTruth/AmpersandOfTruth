using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Demo;

/// <summary>
/// Seeds read-only <c>"demo"</c>-owned saved investigations from already-warmed <see cref="InvestigationRunCache"/> entries
/// at startup when <see cref="DemoOptions.SeedLibrary"/> is enabled. Never runs the pipeline.
/// </summary>
public sealed class DemoLibrarySeeder : IHostedService
{
    private readonly Func<DemoOptions> _getOptions;
    private readonly InvestigationRunCache _cache;
    private readonly LibraryStore _store;
    private readonly ILogger<DemoLibrarySeeder> _log;

    [ActivatorUtilitiesConstructor]
    public DemoLibrarySeeder(
        IOptionsMonitor<DemoOptions> options,
        InvestigationRunCache cache,
        LibraryStore store,
        ILogger<DemoLibrarySeeder> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _getOptions = () => options.CurrentValue;
        _cache = cache;
        _store = store;
        _log = log;
    }

    public DemoLibrarySeeder(
        DemoOptions options,
        InvestigationRunCache cache,
        LibraryStore store,
        ILogger<DemoLibrarySeeder> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _getOptions = () => options;
        _cache = cache;
        _store = store;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken) => SeedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var opts = _getOptions();
        if (!opts.SeedLibrary || opts.SeedTopics.Count == 0)
        {
            return;
        }

        var existing = await _store.ListExamplesAsync(ct);
        var seededTopics = existing
            .Select(static x => x.Topic.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seededSlugs = existing
            .Select(static x => x.TopicSlug)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.Ordinal);

        var skipped = new List<string>();
        foreach (var raw in opts.SeedTopics)
        {
            var topic = raw?.Trim();
            if (string.IsNullOrEmpty(topic))
            {
                continue;
            }

            var slug = TopicSlug.From(topic);
            if (seededTopics.Contains(topic) || (slug.Length > 0 && seededSlugs.Contains(slug)))
            {
                continue;
            }

            var cached = await _cache.TryGetAsync(topic, ct);
            if (cached is null)
            {
                skipped.Add(topic);
                continue;
            }

            var snapshot = new SavedSnapshot(topic, cached.Articles, cached.Incidents, cached.AnalysedAt);
            await _store.SaveAsync(LibraryStore.DemoOwner, topic, snapshot, ct);
            seededTopics.Add(topic);
            if (slug.Length > 0)
            {
                seededSlugs.Add(slug);
            }
        }

        if (skipped.Count > 0)
        {
            _log.LogInformation(
                "DemoLibrarySeeder skipped {Count} unwarmed topic(s): {Topics}",
                skipped.Count,
                string.Join(", ", skipped));
        }
    }
}
