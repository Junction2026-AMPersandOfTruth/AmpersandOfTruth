using AmpersandOfTruth.Web.Features.Search;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Search;

public class SearchServiceStatusTests
{
    private sealed class CountingHealth : HealthCheckService
    {
        public int Calls;
        public override Task<HealthReport> CheckHealthAsync(Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task Caches_for_the_ttl_then_rechecks()
    {
        var health = new CountingHealth();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var status = new SearchServiceStatus(health, time);

        await status.GetAsync();
        await status.GetAsync();
        Assert.Equal(1, health.Calls);

        time.Advance(SearchServiceStatus.Ttl + TimeSpan.FromSeconds(1));
        await status.GetAsync();
        Assert.Equal(2, health.Calls);
    }
}
