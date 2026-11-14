using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public class InvestigationSeamDiTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public void Default_configuration_resolves_HybridFixtureRunner_FixtureIncidentSource_and_SampleIncidentWarmup()
    {
        var runner = app.Services.GetRequiredService<IInvestigationRunner>();
        var source = app.Services.GetRequiredService<IIncidentSource>();
        var opts = app.Services.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value;
        var hosted = app.Services.GetServices<IHostedService>();

        Assert.IsType<HybridFixtureRunner>(runner);
        Assert.IsType<FixtureIncidentSource>(source);
        Assert.Single(hosted.OfType<SampleIncidentWarmup>());
        Assert.Equal(IncidentSourceKind.Fixtures, opts.Source);
        Assert.True(opts.IsSampleData);
    }

    [Fact]
    public async Task Pipeline_configuration_resolves_InvestigationPipeline_and_omits_SampleIncidentWarmup()
    {
        await using var pipelineApp = app.WithWebHostBuilder(b =>
            b.UseSetting("Investigations:Source", "Pipeline"));

        var runner = pipelineApp.Services.GetRequiredService<IInvestigationRunner>();
        var source = pipelineApp.Services.GetRequiredService<IIncidentSource>();
        var opts = pipelineApp.Services.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value;
        var hosted = pipelineApp.Services.GetServices<IHostedService>();

        Assert.IsType<AmpersandOfTruth.Web.Features.Investigations.Cache.CachingInvestigationRunner>(runner);
        Assert.IsType<RecentRunIncidentSource>(source);
        Assert.Empty(hosted.OfType<SampleIncidentWarmup>());
        Assert.Equal(IncidentSourceKind.Pipeline, opts.Source);
        Assert.False(opts.IsSampleData);
    }

    [Fact]
    public async Task Warmup_false_configuration_omits_SampleIncidentWarmup()
    {
        await using var noWarmupApp = app.WithWebHostBuilder(b =>
            b.UseSetting("Investigations:Fixtures:Warmup", "false"));

        var hosted = noWarmupApp.Services.GetServices<IHostedService>();

        Assert.Empty(hosted.OfType<SampleIncidentWarmup>());
    }

    [Fact]
    public async Task FixtureIncidentSource_maps_ranked_incidents_and_returns_null_when_unknown()
    {
        var corpus = new FakeIncidentCorpus { Unavailable = true };
        var revisions = new FakeRevisionStore { Unavailable = true };
        var mapper = new IncidentMapper(corpus, revisions);

        var low = Record(10, 11, EvidenceTier.Reverted, 5.0, "low");
        var high = Record(20, 21, EvidenceTier.Contested, 25.0, "high");
        var samples = SampleIncidents.From(new SampleTopic("winter-war", "Winter War", ["talvisota"], [low, high]));

        var source = new FixtureIncidentSource(samples, mapper);

        var views = await source.ForTopicAsync("talvisota");
        Assert.NotNull(views);
        Assert.Equal([("high", 1), ("low", 2)], views.Select(v => (v.Id, v.Rank)));
        Assert.Null(await source.ForTopicAsync("unknown-topic"));
    }

    [Fact]
    public async Task RecentRuns_keeps_last_20_topics_thread_safely_and_RecentRunIncidentSource_maps_them()
    {
        var corpus = new FakeIncidentCorpus { Unavailable = true };
        var revisions = new FakeRevisionStore { Unavailable = true };
        var mapper = new IncidentMapper(corpus, revisions);
        var recent = new RecentRuns();
        var source = new RecentRunIncidentSource(recent, mapper);

        for (var i = 1; i <= 25; i++)
        {
            recent.Record($"topic-{i}", [Record(i, i + 100, EvidenceTier.Reverted, i)]);
        }

        // First 5 evicted, 6..25 retained.
        Assert.Null(await source.ForTopicAsync("topic-1"));
        Assert.Null(await source.ForTopicAsync("topic-5"));
        var sixth = await source.ForTopicAsync("topic-6");
        Assert.NotNull(sixth);
        Assert.Equal(1, Assert.Single(sixth).Rank);

        // Re-recording topic-6 refreshes its recency so topic-7 is evicted next.
        recent.Record("Topic 6", [Record(60, 61, EvidenceTier.Contested, 99)]);
        recent.Record("topic-26", [Record(26, 126, EvidenceTier.Reverted, 1)]);

        Assert.NotNull(await source.ForTopicAsync("topic-6"));
        Assert.Null(await source.ForTopicAsync("topic-7"));
    }

    private static IncidentRecord Record(long ins, long rem, EvidenceTier tier, double score, string? id = null) =>
        new()
        {
            Id = id,
            PageId = 1256,
            InsertingRevIds = [ins],
            RemovingRevIds = [rem],
            Tier = tier,
            Score = score,
        };
}
