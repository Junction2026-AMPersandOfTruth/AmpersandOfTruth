using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Hotspots;
using AmpersandOfTruth.Web.Features.Investigations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Hotspots;

public sealed class MariaDbHotspotStoreTests
{
    [Fact]
    public async Task Caches_non_null_result_for_ten_minutes_and_refreshes_after_expiry()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var calls = 0;
        var sample = FakeHotspotStore.SampleData();

        var store = new MariaDbHotspotStore(_ =>
        {
            calls++;
            return Task.FromResult<HotspotData?>(sample);
        }, clock);

        var first = await store.LoadAsync();
        Assert.Same(sample, first);
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        var cached = await store.LoadAsync();
        Assert.Same(sample, cached);
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        var refreshed = await store.LoadAsync();
        Assert.Same(sample, refreshed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Does_not_cache_null_or_exceptions_so_recovery_succeeds_immediately()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var mode = "missing";
        var sample = FakeHotspotStore.SampleData();

        var store = new MariaDbHotspotStore(_ => mode switch
        {
            "missing" => Task.FromResult<HotspotData?>(null),
            "down" => Task.FromException<HotspotData?>(FakeHotspotStore.DownException()),
            _ => Task.FromResult<HotspotData?>(sample),
        }, clock);

        Assert.Null(await store.LoadAsync());

        mode = "down";
        await Assert.ThrowsAsync<CorpusUnavailableException>(() => store.LoadAsync());

        mode = "ok";
        var loaded = await store.LoadAsync();
        Assert.Same(sample, loaded);
    }

    [Fact]
    public async Task Unreachable_mariadb_throws_CorpusUnavailableException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Corpus"] = "Server=127.0.0.1;Port=1;Database=corpus;User ID=x;Connection Timeout=1",
            })
            .Build();

        await using var factory = new CorpusConnectionFactory(config);
        var store = new MariaDbHotspotStore(factory, NullLogger<MariaDbHotspotStore>.Instance);

        await Assert.ThrowsAsync<CorpusUnavailableException>(() => store.LoadAsync());
    }
}
