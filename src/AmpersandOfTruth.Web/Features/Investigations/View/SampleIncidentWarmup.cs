using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Translation;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>
/// Warms <see cref="IIncidentSource"/> and EN translations for all embedded sample topics, then
/// <see cref="ArticleTimelineService"/> aggregates for every referenced article, shortly after startup so the first demo
/// click is served from cache without delaying startup or <c>/health</c>.
/// </summary>
public sealed class SampleIncidentWarmup(
    SampleIncidents samples,
    IIncidentSource source,
    IOptions<InvestigationSourceOptions> sourceOptions,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SampleIncidentWarmup> log,
    ArticleTimelineService? timeline = null,
    IConflictEpisodes? conflictEpisodes = null,
    IEvidenceTranslator? translator = null) : BackgroundService
{
    public const string WarmupConfigKey = "Investigations:Fixtures:Warmup";
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private const int MaxConcurrentIncidentTranslations = 4;

    internal static IHostedService Create(IServiceProvider sp) =>
        IsEnabled(
            sp.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value,
            sp.GetRequiredService<IConfiguration>())
            ? new SampleIncidentWarmup(
                sp.GetRequiredService<SampleIncidents>(),
                sp.GetRequiredService<IIncidentSource>(),
                sp.GetRequiredService<IOptions<InvestigationSourceOptions>>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<SampleIncidentWarmup>>(),
                sp.GetService<ArticleTimelineService>(),
                sp.GetService<IConflictEpisodes>(),
                sp.GetService<IEvidenceTranslator>())
            : DisabledHostedService.Instance;

    internal static bool IsEnabled(InvestigationSourceOptions sourceOptions, IConfiguration configuration) =>
        sourceOptions.Source == IncidentSourceKind.Fixtures
        && configuration.GetValue(WarmupConfigKey, defaultValue: true);

    internal static IReadOnlyList<long> CollectSamplePageIds(SampleIncidents samples)
    {
        var seen = new HashSet<long>();
        var result = new List<long>();

        void Add(long pageId)
        {
            if (pageId > 0 && seen.Add(pageId))
            {
                result.Add(pageId);
            }
        }

        foreach (var topic in samples.Topics)
        {
            foreach (var incident in topic.Incidents)
            {
                Add(incident.PageId);
                foreach (var otherId in incident.OtherPageIds)
                {
                    Add(otherId);
                }

                foreach (var article in incident.Articles)
                {
                    Add(article.PageId);
                }

                foreach (var edit in incident.Edits)
                {
                    Add(edit.PageId);
                }
            }
        }

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled(sourceOptions.Value, configuration) || samples.Topics.Count == 0)
        {
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        foreach (var topic in samples.Topics)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var started = time.GetTimestamp();
            try
            {
                var views = await source.ForTopicAsync(topic.Slug, stoppingToken);
                if (translator is not null && views is { Count: > 0 })
                {
                    await WarmTranslationsAsync(topic.Slug, views, translator, stoppingToken);
                }

                var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
                if (views is not null && views.Count > 0 && views.Any(v => v.Passage is null))
                {
                    log.LogWarning(
                        "Sample incident warm-up for '{Topic}' degraded (corpus unavailable) in {ElapsedMs:F0} ms",
                        topic.Slug,
                        elapsedMs);
                }
                else
                {
                    log.LogInformation(
                        "Warmed {Count} sample incidents for '{Topic}' in {ElapsedMs:F0} ms",
                        views?.Count ?? 0,
                        topic.Slug,
                        elapsedMs);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
                log.LogWarning(
                    ex,
                    "Sample incident warm-up for '{Topic}' failed after {ElapsedMs:F0} ms",
                    topic.Slug,
                    elapsedMs);
            }
        }

        if (timeline is null || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var pageIds = CollectSamplePageIds(samples);
        if (pageIds.Count == 0)
        {
            return;
        }

        var articlesStarted = time.GetTimestamp();
        var warmedPages = 0;
        var failedPages = 0;
        Exception? firstFailure = null;

        foreach (var pageId in pageIds)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var agg = await timeline.AggregatesAsync(pageId, stoppingToken);
                if (agg is not null)
                {
                    var defaultRange = ArticleTimelineService.MostContested(agg)?.Range
                        ?? ArticleTimelineService.AllRange(agg);
                    await timeline.RevisionsAsync(pageId, defaultRange, limit: 500, stoppingToken);
                }

                if (conflictEpisodes is not null)
                {
                    await conflictEpisodes.ForPageAsync(pageId, stoppingToken);
                }

                warmedPages++;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failedPages++;
                firstFailure ??= ex;
            }
        }

        var articlesElapsedMs = time.GetElapsedTime(articlesStarted).TotalMilliseconds;
        if (failedPages > 0)
        {
            log.LogWarning(
                firstFailure,
                "Sample article aggregate warm-up failed for {Failed}/{Total} page(s) after {ElapsedMs:F0} ms",
                failedPages,
                pageIds.Count,
                articlesElapsedMs);
        }
        else
        {
            log.LogInformation(
                "Warmed article aggregates for {Count} sample page(s) in {ElapsedMs:F0} ms",
                warmedPages,
                articlesElapsedMs);
        }
    }

    private static Task WarmTranslationsAsync(
        string slug,
        IReadOnlyList<IncidentView> views,
        IEvidenceTranslator translator,
        CancellationToken ct)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentIncidentTranslations,
            CancellationToken = ct,
        };

        return Parallel.ForEachAsync(views, options, async (view, innerCt) =>
        {
            try
            {
                if (view.Passage is not null)
                {
                    var input = DiffModel.BuildTranslationInput(view.Passage);
                    if (!input.IsEmpty)
                    {
                        await translator.TranslatePassageAsync(
                            input.Before,
                            input.After,
                            input.ChangedInAfter,
                            input.ChangedInBefore,
                            sourceLang: "fi",
                            scope: slug,
                            ct: innerCt);
                    }
                }

                var summaries = view.Edits
                    .Select(static e => e.Summary)
                    .Where(static s => !string.IsNullOrWhiteSpace(s))
                    .Select(static s => s!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (summaries.Count > 0)
                {
                    await translator.TranslateManyAsync(summaries, "fi", slug, innerCt);
                }
            }
            catch (OperationCanceledException) when (innerCt.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Translation warm-up is best-effort; pages degrade cleanly per item if unavailable.
            }
        });
    }

    private sealed class DisabledHostedService : IHostedService
    {
        public static readonly DisabledHostedService Instance = new();

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
