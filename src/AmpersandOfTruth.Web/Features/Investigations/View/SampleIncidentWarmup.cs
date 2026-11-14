using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>
/// Warms <see cref="IIncidentSource"/> for all embedded sample topics shortly after startup so the first demo click
/// on an incident page is served from memory without delaying startup or <c>/health</c>.
/// </summary>
public sealed class SampleIncidentWarmup(
    SampleIncidents samples,
    IIncidentSource source,
    IOptions<InvestigationSourceOptions> sourceOptions,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SampleIncidentWarmup> log) : BackgroundService
{
    public const string WarmupConfigKey = "Investigations:Fixtures:Warmup";
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);

    internal static IHostedService Create(IServiceProvider sp) =>
        IsEnabled(
            sp.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value,
            sp.GetRequiredService<IConfiguration>())
            ? ActivatorUtilities.CreateInstance<SampleIncidentWarmup>(sp)
            : DisabledHostedService.Instance;

    internal static bool IsEnabled(InvestigationSourceOptions sourceOptions, IConfiguration configuration) =>
        sourceOptions.Source == IncidentSourceKind.Fixtures
        && configuration.GetValue(WarmupConfigKey, defaultValue: true);

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
    }

    private sealed class DisabledHostedService : IHostedService
    {
        public static readonly DisabledHostedService Instance = new();

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
