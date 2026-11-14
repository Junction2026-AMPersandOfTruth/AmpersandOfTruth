using AmpersandOfTruth.Tests.Features.Analysis.Store;
using AmpersandOfTruth.Tests.Features.Articles;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Components.Layout;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Demo;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Demo;

public sealed class SafeModeTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly PipelineKit _kit = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SafeMode_miss_emits_StageFailed_with_demo_message_and_hit_replays_from_cache()
    {
        var demoMonitor = new TestOptionsMonitor<DemoOptions>(new DemoOptions { SafeMode = false });
        var runner = BuildRunner(demoMonitor);

        // Warm "Talvisota" while SafeMode is off.
        var cold = await PipelineKit.Collect(runner.RunAsync("Talvisota"));
        Assert.False(Assert.IsType<Completed>(cold[^1]).FromCache);

        // Flip SafeMode on dynamically without rebuild; take corpus/embedder offline to prove no cold work runs.
        demoMonitor.CurrentValue = new DemoOptions { SafeMode = true };
        _kit.Corpus.Unavailable = true;
        _kit.Revisions.Unavailable = true;
        _kit.Embedder.Unavailable = true;

        // Miss -> StageFailed(FindArticles, Error, SafeModeMissMessage).
        var miss = await PipelineKit.Collect(runner.RunAsync("Unanalysed Topic"));
        var failure = Assert.Single(miss.OfType<StageFailed>());
        Assert.Single(miss);
        Assert.Equal(InvestigationStage.FindArticles, failure.Stage);
        Assert.Equal(StageFailureKind.Error, failure.Kind);
        Assert.Equal(DemoOptions.SafeModeMissMessage, failure.Error);

        // Hit -> replays cached run (even if refresh: true is passed).
        var hit = await PipelineKit.Collect(runner.RunAsync("Talvisota", refresh: true));
        var done = Assert.IsType<Completed>(hit[^1]);
        Assert.True(done.FromCache);
        Assert.NotEmpty(done.Incidents);
    }

    [Fact]
    public async Task InvestigationPage_and_Footer_in_SafeMode_hide_timings_rerun_and_dev_hints_and_show_miss_banner()
    {
        using var bunit = new BunitContext();
        bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 14, 0, 0, TimeSpan.Zero));
        var demoMonitor = new TestOptionsMonitor<DemoOptions>(new DemoOptions { SafeMode = true });
        var runner = BuildRunner(demoMonitor);

        bunit.Services.AddSingleton<TimeProvider>(clock);
        bunit.Services.AddSingleton<IOptionsMonitor<DemoOptions>>(demoMonitor);
        bunit.Services.AddSingleton<IInvestigationRunner>(runner);
        bunit.Services.AddSingleton<IIncidentCorpus>(new FakeIncidentCorpus());
        bunit.Services.AddSingleton<IRevisionStore>(new FakeRevisionStore());
        bunit.Services.AddSingleton<IncidentMapper>();
        bunit.Services.AddSingleton<RecentRuns>();
        bunit.Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));
        bunit.Services.AddSingleton<IArticleCorpus>(new FakeArticleCorpus());
        bunit.Services.AddSingleton<ArticleTimelineService>();

        // 1. SafeMode miss renders safe-mode-miss banner with no Retry or Re-run.
        var missCut = bunit.Render<InvestigationPage>(p => p.Add(x => x.Topic, "unknown-topic"));
        missCut.WaitForAssertion(() =>
        {
            var banner = missCut.Find("[data-testid=safe-mode-miss]");
            Assert.Contains(DemoOptions.SafeModeMissMessage, banner.TextContent);
            Assert.Empty(missCut.FindAll("[data-testid=rerun-link]"));
            Assert.Empty(missCut.FindAll("[data-testid=status-retry]"));
            Assert.Empty(CopyRules.FindViolations(banner.TextContent, includeJargon: true));
        });

        // 2. Warm "winter war" in cache, then render InvestigationPage in SafeMode: timings and Re-run are hidden.
        demoMonitor.CurrentValue = new DemoOptions { SafeMode = false };
        await PipelineKit.Collect(runner.RunAsync("winter war"));
        demoMonitor.CurrentValue = new DemoOptions { SafeMode = true };

        var hitCut = bunit.Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        hitCut.WaitForAssertion(() =>
        {
            var doneBanner = hitCut.Find("[data-testid=stage-tracker-done]");
            Assert.DoesNotContain("Analysed in", doneBanner.TextContent);
            Assert.Empty(hitCut.FindAll("[data-testid=rerun-link]"));
            Assert.Empty(CopyRules.FindViolations(doneBanner.TextContent, includeJargon: true));
        });

        // 3. Footer hides dev links (/analysis, /investigate) in SafeMode while keeping attribution.
        var footerCut = bunit.Render<Footer>();
        var footer = footerCut.Find("[data-testid=site-footer]");
        Assert.Contains(Attribution.FooterText, footer.TextContent);
        Assert.Empty(footer.QuerySelectorAll("a[href='/analysis']"));
        Assert.Empty(footer.QuerySelectorAll("a[href='/investigate']"));
    }

    private CachingInvestigationRunner BuildRunner(IOptionsMonitor<DemoOptions> demo)
    {
        var opts = Options.Create(_kit.Options);
        var gemini = Options.Create(_kit.Gemini);
        var fp = new PipelineFingerprint(opts, gemini, new PromptLibrary(gemini, NullLogger<PromptLibrary>.Instance));
        var cache = new InvestigationRunCache(
            _db.Factory, fp, TimeProvider.System, NullLogger<InvestigationRunCache>.Instance);
        return new CachingInvestigationRunner(_kit.Build(), cache, TimeProvider.System, gate: null, demo: demo);
    }
}
