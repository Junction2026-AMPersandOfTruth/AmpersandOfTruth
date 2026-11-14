using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AmpersandOfTruth.Web.Features.Search;

/// <summary>
/// The corpus + embedding health the landing page shows, cached for <see cref="Ttl"/> so a page load never waits on a DB/Ollama
/// ping (they took 2–3 s through the tunnel). Singleton, thread-safe; concurrent callers share one in-flight check.
/// </summary>
public sealed class SearchServiceStatus(HealthCheckService health, TimeProvider time)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private Task<HealthReport>? _inFlight;
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    public Task<HealthReport> GetAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var fresh = time.GetUtcNow() - _checkedAt < Ttl;
            if (_inFlight is { } current && (fresh || !current.IsCompleted) && !current.IsFaulted && !current.IsCanceled)
                return current.WaitAsync(ct);

            _checkedAt = time.GetUtcNow();
            _inFlight = health.CheckHealthAsync(
                static reg => reg.Name is "corpus" or "embedding" || reg.Tags.Contains("corpus") || reg.Tags.Contains("embedding"),
                CancellationToken.None);
            return _inFlight.WaitAsync(ct);
        }
    }
}
