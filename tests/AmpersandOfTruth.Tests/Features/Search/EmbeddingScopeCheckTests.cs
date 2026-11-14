using AmpersandOfTruth.Tests.Features.Embedding;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Search;

public sealed class EmbeddingScopeCheckTests
{
    [Fact]
    public async Task SearchFeature_registers_EmbeddingScopeCheck_while_keeping_AlwaysInScope_available()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IQueryEmbedder>(new FakeQueryEmbedder());
        services.AddSingleton<IInvestigationCorpus>(FakeInvestigationCorpus.WithWinterWar());
        services.AddSearchFeature();

        using var provider = services.BuildServiceProvider();
        var scopeCheck = provider.GetRequiredService<IScopeCheck>();

        Assert.IsType<EmbeddingScopeCheck>(scopeCheck);
        Assert.True((await new AlwaysInScope().CheckAsync("Climate change")).InScope);
    }

    [Fact]
    public async Task CheckAsync_returns_in_scope_without_embedding_chips_when_articles_are_within_threshold()
    {
        var embedder = new FakeQueryEmbedder();
        var corpus = FakeInvestigationCorpus.WithWinterWar();
        var logger = new RecordingLogger<EmbeddingScopeCheck>();
        var check = new EmbeddingScopeCheck(embedder, corpus, Options.Create(new SearchOptions()), logger);

        var result = await check.CheckAsync("Finnish neutrality in World War II");

        Assert.True(result.InScope);
        Assert.Empty(result.Suggestions);
        Assert.Equal(1, embedder.Calls);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CheckAsync_returns_only_close_nearest_chips_caps_at_max_and_caches_chip_vectors()
    {
        var embedder = new ControlledEmbedder(new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["American civil war"] = UnitVector(0, 1.0f),
            ["Broad war query"] = UnitVector(0, 1.0f),
            ["Climate change"] = UnitVector(4, 1.0f),
            ["Civil War 1918"] = UnitVector(0, 0.95f, 2, 0.31f),
            ["Winter War"] = UnitVector(0, 0.80f, 1, 0.60f),
            ["Continuation War"] = UnitVector(0, 0.70f, 1, 0.71f),
            ["Mainila shots"] = UnitVector(0, 0.62f, 2, 0.78f),
            ["Lapland War"] = UnitVector(0, 0.56f, 3, 0.83f),
            ["Mannerheim"] = UnitVector(1, 0.95f, 2, 0.31f),
            ["Ceded Karelia"] = UnitVector(2, 1.0f),
            ["Finlandization"] = UnitVector(3, 1.0f),
        });

        var corpus = new FakeInvestigationCorpus();
        corpus.Articles.AddRange([
            new TopicArticle("en", 1, "Battle of Fort Necessity", 0.59, "Q1"),
            new TopicArticle("fi", 2, "Fort William Henryn piiritys", 0.61, "Q2"),
            new TopicArticle("en", 3, "French and Indian War", 0.62, "Q3"),
            new TopicArticle("fi", 4, "Suuri siirtomaasota", 0.63, "Q4"),
            new TopicArticle("en", 5, "Migration Period", 0.64, "Q5"),
        ]);

        var options = new SearchOptions
        {
            SuggestedTopics =
            [
                new("Finlandization", "Finlandization"),
                new("Ceded Karelia", "Ceded Karelia"),
                new("Continuation War", "Continuation War"),
                new("Winter War", "Winter War"),
                new("Civil War 1918", "Civil War 1918"),
                new("Mannerheim", "Mannerheim"),
            ],
        };

        var logger = new RecordingLogger<EmbeddingScopeCheck>();
        var check = new EmbeddingScopeCheck(embedder, corpus, Options.Create(options), logger);

        // Only the 3 genuinely close chips (cos >= 0.54, dist <= 0.46) are returned; orthogonal chips are omitted.
        var first = await check.CheckAsync("american civil war");
        Assert.False(first.InScope);
        Assert.Equal(["Civil War 1918", "Winter War", "Continuation War"], first.Suggestions);
        Assert.Equal(7, embedder.Calls); // 1 query + 6 chips embedded once

        // Unrelated topic has no chip within MaxSuggestionDistance -> empty suggestions list.
        var unrelated = await check.CheckAsync("climate change");
        Assert.False(unrelated.InScope);
        Assert.Empty(unrelated.Suggestions);
        Assert.Equal(8, embedder.Calls); // only the new query was embedded; all 6 chips served from cache

        // When >4 chips are close, cap at MaxSuggestions (4) ordered by cosine similarity.
        var fiveCloseOptions = new SearchOptions
        {
            SuggestedTopics =
            [
                new("Lapland War", "Lapland War"),
                new("Mainila shots", "Mainila shots"),
                new("Continuation War", "Continuation War"),
                new("Winter War", "Winter War"),
                new("Civil War 1918", "Civil War 1918"),
                new("Finlandization", "Finlandization"),
            ],
        };
        var fiveCloseCheck = new EmbeddingScopeCheck(embedder, corpus, Options.Create(fiveCloseOptions), logger);
        var capped = await fiveCloseCheck.CheckAsync("Broad war query");
        Assert.False(capped.InScope);
        Assert.Equal(["Civil War 1918", "Winter War", "Continuation War", "Mainila shots"], capped.Suggestions);
    }

    [Fact]
    public async Task Chip_embedding_failure_returns_empty_suggestions_rather_than_static_order()
    {
        var embedder = new ChipFailingEmbedder();
        var corpus = new FakeInvestigationCorpus();
        corpus.Articles.Add(new TopicArticle("en", 1, "Wilderness", 0.60, "Q1"));

        var logger = new RecordingLogger<EmbeddingScopeCheck>();
        var check = new EmbeddingScopeCheck(embedder, corpus, Options.Create(new SearchOptions()), logger);

        var result = await check.CheckAsync("climate change");
        Assert.False(result.InScope);
        Assert.Empty(result.Suggestions);
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task Search_Scope_config_binds_MaxTopDistance_and_MaxMeanDistance()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:Scope:MaxTopDistance"] = "0.35",
                ["Search:Scope:MaxMeanDistance"] = "0.38",
            })
            .Build();

        var corpus = new FakeInvestigationCorpus();
        corpus.Articles.AddRange([
            new TopicArticle("fi", 10, "Kielitaistelu", 0.30, "Q10"),
            new TopicArticle("fi", 11, "Fennomania", 0.42, "Q11"),
            new TopicArticle("fi", 12, "Svekomania", 0.43, "Q12"),
            new TopicArticle("fi", 13, "Suomen kieli", 0.44, "Q13"),
            new TopicArticle("fi", 14, "Ruotsin kieli", 0.45, "Q14"),
        ]);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IQueryEmbedder>(new FakeQueryEmbedder());
        services.AddSingleton<IInvestigationCorpus>(corpus);
        services.AddSearchFeature();

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<SearchOptions>>().Value;
        Assert.Equal(0.35, opts.Scope.MaxTopDistance);
        Assert.Equal(0.38, opts.Scope.MaxMeanDistance);

        // top1 = 0.30 <= 0.35, but mean5 = 0.408 > 0.38 → out of scope under configured MaxMeanDistance.
        var scopeCheck = provider.GetRequiredService<IScopeCheck>();
        var result = await scopeCheck.CheckAsync("language politics in nineteenth-century schools");
        Assert.False(result.InScope);
    }

    [Fact]
    public async Task Short_corpus_article_title_receives_title_credit_while_unrelated_hits_do_not()
    {
        var embedder = new FakeQueryEmbedder();
        var logger = new RecordingLogger<EmbeddingScopeCheck>();

        var winterWarCorpus = new FakeInvestigationCorpus();
        winterWarCorpus.Articles.AddRange([
            new TopicArticle("fi", 1256, "Talvisota", 0.526, "Q134949"),
            new TopicArticle("fi", 1257, "Talvisodan pommitukset", 0.542, "Q2"),
            new TopicArticle("sv", 1258, "Vinterkriget", 0.543, "Q134949"),
            new TopicArticle("fi", 1259, "Talvisodan merisotatoimet", 0.549, "Q3"),
            new TopicArticle("fi", 1260, "Talvisodan ulkomainen apu", 0.552, "Q4"),
        ]);

        var inCheck = new EmbeddingScopeCheck(embedder, winterWarCorpus, Options.Create(new SearchOptions()), logger);
        Assert.True((await inCheck.CheckAsync("talvisota")).InScope);

        var climateCorpus = new FakeInvestigationCorpus();
        climateCorpus.Articles.AddRange([
            new TopicArticle("en", 396275, "Wilderness", 0.529, "Q911871"),
            new TopicArticle("fi", 1537033, "Ilmastoveivi2019-kampanja", 0.541, "Q97202846"),
            new TopicArticle("en", 579223, "Land reclamation", 0.552, "Q1130322"),
            new TopicArticle("fi", 999001, "ICE Pact", 0.576, "Q5"),
            new TopicArticle("en", 999002, "Kola Peninsula", 0.577, "Q6"),
        ]);

        var outCheck = new EmbeddingScopeCheck(embedder, climateCorpus, Options.Create(new SearchOptions()), logger);
        Assert.False((await outCheck.CheckAsync("climate change")).InScope);
    }

    [Fact]
    public async Task Embedder_unavailable_returns_in_scope_and_logs_warning_once()
    {
        var embedder = new FakeQueryEmbedder { Unavailable = true };
        var corpus = FakeInvestigationCorpus.WithWinterWar();
        var logger = new RecordingLogger<EmbeddingScopeCheck>();
        var check = new EmbeddingScopeCheck(embedder, corpus, Options.Create(new SearchOptions()), logger);

        var r1 = await check.CheckAsync("climate change");
        var r2 = await check.CheckAsync("Taylor Swift");
        var r3 = await check.CheckAsync("Bitcoin");

        Assert.True(r1.InScope);
        Assert.True(r2.InScope);
        Assert.True(r3.InScope);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task Corpus_unavailable_returns_in_scope_and_logs_warning_once()
    {
        var embedder = new FakeQueryEmbedder();
        var corpus = new FakeInvestigationCorpus { Unavailable = true };
        var logger = new RecordingLogger<EmbeddingScopeCheck>();
        var check = new EmbeddingScopeCheck(embedder, corpus, Options.Create(new SearchOptions()), logger);

        var r1 = await check.CheckAsync("climate change");
        var r2 = await check.CheckAsync("Roman Empire");

        Assert.True(r1.InScope);
        Assert.True(r2.InScope);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task Cancellation_propagates_without_logging_unavailable_warning()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var logger = new RecordingLogger<EmbeddingScopeCheck>();
        var check = new EmbeddingScopeCheck(new CancellingEmbedder(), FakeInvestigationCorpus.WithWinterWar(), Options.Create(new SearchOptions()), logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckAsync("Winter War", cts.Token));
        Assert.Empty(logger.Entries);
    }

    private static float[] UnitVector(int indexA, float valueA, int indexB = -1, float valueB = 0f)
    {
        var v = new float[EmbeddingOptions.Dimensions];
        v[indexA] = valueA;
        if (indexB >= 0)
        {
            v[indexB] = valueB;
        }
        return v;
    }

    private sealed class ControlledEmbedder(IReadOnlyDictionary<string, float[]> vectors) : IQueryEmbedder
    {
        private int _calls;
        public int Calls => _calls;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(vectors.TryGetValue(text, out var v) ? v : FakeQueryEmbedder.VectorFor(text));
        }
    }

    private sealed class CancellingEmbedder : IQueryEmbedder
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new float[EmbeddingOptions.Dimensions]);
        }
    }

    private sealed class ChipFailingEmbedder : IQueryEmbedder
    {
        private int _calls;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(FakeQueryEmbedder.VectorFor(text));
            }

            throw new EmbeddingUnavailableException("chip embed failed");
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}

[Trait("Category", "Corpus")]
[Trait("Category", "Embedding")]
public sealed class EmbeddingScopeCheckIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [CorpusEmbeddingFact]
    public async Task Climate_change_is_out_of_scope_and_Winter_War_and_talvisota_are_in_scope()
    {
        var scopeCheck = app.Services.GetRequiredService<IScopeCheck>();
        Assert.IsType<EmbeddingScopeCheck>(scopeCheck);

        var climateOut = await scopeCheck.CheckAsync("climate change");
        Assert.False(climateOut.InScope);
        Assert.Empty(climateOut.Suggestions);

        var civilWarOut = await scopeCheck.CheckAsync("American Civil War");
        Assert.False(civilWarOut.InScope);
        Assert.NotEmpty(civilWarOut.Suggestions);
        Assert.Equal("Civil War 1918", civilWarOut.Suggestions[0]);

        var winterWarIn = await scopeCheck.CheckAsync("Winter War");
        Assert.True(winterWarIn.InScope);
        Assert.Empty(winterWarIn.Suggestions);

        var talvisotaIn = await scopeCheck.CheckAsync("talvisota");
        Assert.True(talvisotaIn.InScope);
        Assert.Empty(talvisotaIn.Suggestions);
    }
}
