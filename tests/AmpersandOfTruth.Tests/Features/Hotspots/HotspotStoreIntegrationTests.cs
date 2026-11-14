using System.Diagnostics;
using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Hotspots;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AmpersandOfTruth.Tests.Features.Hotspots;

[Trait("Category", "Corpus")]
public sealed class HotspotStoreIntegrationTests(AppFactory factory, ITestOutputHelper output) : IClassFixture<AppFactory>
{
    [CorpusFact]
    public async Task Loads_all_four_precomputed_tables_in_one_batch_within_budget()
    {
        var connFactory = factory.Services.GetRequiredService<CorpusConnectionFactory>();
        await using (await connFactory.OpenAsync())
        {
            // Warm pooled SSH tunnel connection before measuring SQL batch latency.
        }

        var store = factory.Services.GetRequiredService<MariaDbHotspotStore>();

        var sw = Stopwatch.StartNew();
        var data = await store.LoadAsync();
        sw.Stop();

        Assert.NotNull(data);
        output.WriteLine(
            $"Loaded hotspot tables in {sw.Elapsed.TotalMilliseconds:F0} ms: " +
            $"{data.Articles.Count} articles, {data.Months.Count} months, {data.Categories.Count} categories");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"LoadAsync took {sw.Elapsed}");
        Assert.True(data.Headline.Revisions > 800_000);
        Assert.True(data.Headline.Articles > 19_000);
        Assert.True(data.Headline.Reverts > 40_000);
        Assert.True(data.Headline.Episodes > 4_000);
        Assert.InRange(data.Headline.AnonRevertedShare, 0.01, 0.99);

        Assert.Equal(200, data.Articles.Count);
        Assert.All(data.Articles, a =>
        {
            Assert.True(a.PageId > 0);
            Assert.False(string.IsNullOrWhiteSpace(a.TitleFi));
            Assert.Equal(24, a.YearlyReverts.Count);
        });

        Assert.Equal(288, data.Months.Count);
        Assert.Equal("2003-01", data.Months[0].Month);
        Assert.Equal("2026-12", data.Months[^1].Month);

        Assert.Equal(50, data.Categories.Count);

        var warmSw = Stopwatch.StartNew();
        var cached = await store.LoadAsync();
        warmSw.Stop();
        Assert.Same(data, cached);
        Assert.True(warmSw.Elapsed < TimeSpan.FromMilliseconds(50), $"Cached LoadAsync took {warmSw.Elapsed}");
    }
}
