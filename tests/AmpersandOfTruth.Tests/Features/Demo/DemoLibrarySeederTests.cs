using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Features.Articles;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Demo;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Demo;

public sealed class DemoLibrarySeederTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly PipelineKit _kit = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Seeder_creates_once_is_idempotent_skips_unwarmed_topics_and_protects_demo_rows()
    {
        var cache = BuildCache();
        var store = new LibraryStore(_db.Factory, _clock);
        var logger = new ListLogger<DemoLibrarySeeder>();

        // Seed only "Winter War" and "Continuation War" in InvestigationRunCache; leave "Finnish Civil War" unwarmed.
        await cache.StoreAsync("Winter War", MakeRunSnapshot("Winter War", "inc-ww", EvidenceTier.Campaign));
        await cache.StoreAsync("Continuation War", MakeRunSnapshot("Continuation War", "inc-cw", EvidenceTier.Contested));

        // SeedLibrary = false does nothing.
        var disabledSeeder = new DemoLibrarySeeder(
            new DemoOptions { SeedLibrary = false }, cache, store, logger);
        await disabledSeeder.StartAsync(CancellationToken.None);
        Assert.Empty(await store.ListExamplesAsync());

        // SeedLibrary = true creates 2 demo rows and logs the skipped unwarmed topic.
        var enabledOptions = new DemoOptions
        {
            SeedLibrary = true,
            SeedTopics = ["Winter War", "Continuation War", "Finnish Civil War"],
        };
        var seeder = new DemoLibrarySeeder(enabledOptions, cache, store, logger);
        await seeder.StartAsync(CancellationToken.None);

        var examples = await store.ListExamplesAsync();
        Assert.Equal(2, examples.Count);
        Assert.Equal(["Continuation War", "Winter War"], examples.Select(x => x.Title).OrderBy(x => x).ToArray());
        Assert.Contains(logger.Messages, m => m.Contains("Finnish Civil War", StringComparison.Ordinal));
        Assert.Empty(_kit.Model.Requests); // Never ran the pipeline!

        var firstIds = examples.Select(x => x.Id).OrderBy(x => x).ToArray();

        // Running again is idempotent: no duplicates created.
        await seeder.StartAsync(CancellationToken.None);
        var secondExamples = await store.ListExamplesAsync();
        Assert.Equal(firstIds, secondExamples.Select(x => x.Id).OrderBy(x => x).ToArray());

        // Demo-owned rows cannot be renamed, deleted, pinned, or overwritten (even with "demo" owner key).
        var targetId = examples[0].Id;
        Assert.True(await store.IsDemoAsync(targetId));
        Assert.False(await store.RenameAsync(targetId, "visitor-1", "Hacked"));
        Assert.False(await store.RenameAsync(targetId, LibraryStore.DemoOwner, "Hacked"));
        Assert.False(await store.DeleteAsync(targetId, "visitor-1"));
        Assert.False(await store.DeleteAsync(targetId, LibraryStore.DemoOwner));
        await store.PinAsync(targetId, LibraryStore.DemoOwner, "inc-ww", "note");
        Assert.Empty(await store.PinsAsync(targetId));
    }

    [Fact]
    public async Task LibraryPage_and_InvestigationPage_show_Examples_read_only_to_any_visitor()
    {
        var cache = BuildCache();
        var store = new LibraryStore(_db.Factory, _clock);

        await cache.StoreAsync("Winter War", MakeRunSnapshot("Winter War", "inc-ww", EvidenceTier.Campaign));
        var seeder = new DemoLibrarySeeder(
            new DemoOptions { SeedLibrary = true, SeedTopics = ["Winter War"] },
            cache,
            store,
            new ListLogger<DemoLibrarySeeder>());
        await seeder.StartAsync(CancellationToken.None);
        var demoItem = Assert.Single(await store.ListExamplesAsync());

        using var bunit = new BunitContext();
        bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        _db.AddTo(bunit.Services);
        bunit.Services.AddSingleton<TimeProvider>(_clock);
        bunit.Services.AddLibraryFeature();
        bunit.Services.AddSingleton<IInvestigationRunner>(new UnusedRunner());
        bunit.Services.AddSingleton<IIncidentCorpus>(new FakeIncidentCorpus());
        bunit.Services.AddSingleton<IRevisionStore>(new FakeRevisionStore());
        bunit.Services.AddSingleton<IncidentMapper>();
        bunit.Services.AddSingleton<RecentRuns>();
        bunit.Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));
        bunit.Services.AddSingleton<IArticleCorpus>(new FakeArticleCorpus());
        bunit.Services.AddSingleton<ArticleTimelineService>();

        bunit.Services.GetRequiredService<OwnerKey>().Seed("random-visitor-key");

        // 1. Fresh visitor on /library sees Examples without empty-state, rename, or delete buttons.
        var libCut = bunit.Render<LibraryPage>();
        libCut.WaitForAssertion(() =>
        {
            Assert.Empty(libCut.FindAll("[data-testid=library-empty]"));
            Assert.Empty(libCut.FindAll("[data-testid=library-item]"));

            var examplesSection = libCut.Find("[data-testid=library-examples]");
            Assert.Contains("Examples", examplesSection.TextContent);

            var row = Assert.Single(libCut.FindAll("[data-testid=library-example]"));
            Assert.Contains("Winter War", row.TextContent);
            var open = row.QuerySelector("[data-testid=library-open]");
            Assert.NotNull(open);
            Assert.Equal($"/investigate/winter-war?saved={demoItem.Id}", open.GetAttribute("href"));

            Assert.Empty(row.QuerySelectorAll("[data-testid=library-rename]"));
            Assert.Empty(row.QuerySelectorAll("[data-testid=library-delete]"));
        });

        var doc = new HtmlParser().ParseDocument(libCut.Markup);
        var text = string.Join(" ", doc.Descendants<IText>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Assert.Empty(CopyRules.FindViolations(text, includeJargon: true));

        // 2. Opening the demo saved example on InvestigationPage renders read-only cards with no Pin buttons.
        var nav = bunit.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo($"/investigate/winter-war?saved={demoItem.Id}");
        var invCut = bunit.Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));

        invCut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(invCut.FindAll("[data-testid=incident-card]"));
            Assert.Empty(invCut.FindAll("[data-testid=pin-incident]"));
        });
    }

    [Fact]
    public void Appsettings_defaults_keep_existing_behaviour_and_do_not_duplicate_SeedTopics()
    {
        var repoRoot = FindRepoRoot();
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repoRoot, "src", "AmpersandOfTruth.Web", "appsettings.json"))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(TimeProvider.System);
        _db.AddTo(services);
        services.AddLibraryFeature();
        services.AddSingleton(BuildCache());
        services.AddDemoFeature();

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptionsMonitor<DemoOptions>>().CurrentValue;

        Assert.False(opts.SafeMode);
        Assert.Equal(2, opts.MaxConcurrentColdRuns);
        Assert.Equal(6, opts.StartsPerClientPerMinute);
        Assert.False(opts.SeedLibrary);
        Assert.Equal(["Winter War", "Continuation War", "Finnish Civil War"], opts.SeedTopics);
    }

    private InvestigationRunCache BuildCache()
    {
        var opts = Options.Create(_kit.Options);
        var gemini = Options.Create(_kit.Gemini);
        var prompts = new PromptLibrary(gemini, Microsoft.Extensions.Logging.Abstractions.NullLogger<PromptLibrary>.Instance);
        var fp = new PipelineFingerprint(opts, gemini, prompts);
        return new InvestigationRunCache(
            _db.Factory,
            fp,
            _clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InvestigationRunCache>.Instance);
    }

    private RunSnapshot MakeRunSnapshot(string topic, string incidentId, EvidenceTier tier)
    {
        var article = new TopicArticle("fi", 1150, topic, 0.04);
        var incident = new IncidentRecord
        {
            Id = incidentId,
            PageId = 1150,
            InsertingRevIds = [1001],
            RemovingRevIds = [1002],
            Tier = tier,
            Score = 88,
            ClaimEn = $"Disputed claim in {topic}",
        };
        return new RunSnapshot(
            [article],
            RevisionsScanned: 120,
            Candidates: [],
            Scored: [],
            Incidents: [incident],
            AnalysedAt: _clock.GetUtcNow(),
            ElapsedMs: 1200,
            StageMs: new Dictionary<InvestigationStage, double>(),
            FirstIncidentMs: 400);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find AmpersandOfTruth.sln");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class UnusedRunner : IInvestigationRunner
    {
        public IAsyncEnumerable<InvestigationEvent> RunAsync(
            string topic, bool refresh = false, CancellationToken ct = default) =>
            throw new InvalidOperationException("Pipeline runner should not be called when replaying a saved example.");
    }
}
