using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Tests.Features.Investigations;

public class InvestigationCacheTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly PipelineKit _kit = new();

    public void Dispose() => _db.Dispose();

    private Task<List<InvestigationEvent>> Run(bool refresh = false) =>
        PipelineKit.Collect(_kit.BuildCaching(_db).RunAsync("Talvisota", refresh));

    [Fact]
    public async Task Second_run_replays_the_same_events_from_cache_without_the_corpus_or_llm()
    {
        var cold = await Run();
        Assert.Equal(0, Assert.IsType<Completed>(cold[^1]).Degraded); // a degraded run isn't cached, so nothing could replay
        _kit.Corpus.Unavailable = true;
        _kit.Revisions.Unavailable = true;
        _kit.Embedder.Unavailable = true;

        var warm = await Run();

        Assert.False(((Completed)cold[^1]).FromCache);
        var done = Assert.IsType<Completed>(warm[^1]);
        Assert.True(done.FromCache);
        Assert.Equal(((Completed)cold[^1]).AnalysedAt, done.AnalysedAt);
        Assert.Equal(cold.Select(e => e.GetType()), warm.Select(e => e.GetType()));
        Assert.Equal(
            ((Completed)cold[^1]).Incidents.Select(i => System.Text.Json.JsonSerializer.Serialize(i, IncidentJson.Options)),
            done.Incidents.Select(i => System.Text.Json.JsonSerializer.Serialize(i, IncidentJson.Options)));
        Assert.Equal(
            cold.OfType<CandidateScored>().Select(c => (c.Candidate.Key, c.Incident?.Id, c.Exclusion)),
            warm.OfType<CandidateScored>().Select(c => (c.Candidate.Key, c.Incident?.Id, c.Exclusion)));
        Assert.Single(_kit.Model.Requests);
    }

    [Fact]
    public async Task Topic_whitespace_does_not_matter_but_case_does()
    {
        await Run();

        var same = await PipelineKit.Collect(_kit.BuildCaching(_db).RunAsync("  Talvisota "));
        var other = await PipelineKit.Collect(_kit.BuildCaching(_db).RunAsync("talvisota"));

        Assert.True(((Completed)same[^1]).FromCache);
        Assert.False(((Completed)other[^1]).FromCache);
    }

    [Fact]
    public async Task Refresh_runs_again_and_replaces_the_cached_run()
    {
        await Run();

        var again = await Run(refresh: true);

        Assert.False(((Completed)again[^1]).FromCache);
        await using var db = _db.CreateContext();
        Assert.Equal(1, await db.Set<InvestigationRunRow>().CountAsync());
    }

    [Fact]
    public async Task A_degraded_run_is_not_cached()
    {
        _kit.Model.Returns("classify-edit", AnalysisOutcome.Failed, "503");
        await Run();
        _kit.Model.Returns("classify-edit", PipelineKit.ClassificationJson());

        var second = await Run();

        Assert.False(((Completed)second[^1]).FromCache);
        Assert.NotNull(Assert.Single(((Completed)second[^1]).Incidents).Assessment);
    }

    [Fact]
    public async Task A_failed_run_is_not_cached()
    {
        _kit.Revisions.Unavailable = true;
        await Run();
        _kit.Revisions.Unavailable = false;

        Assert.False(((Completed)(await Run())[^1]).FromCache);
    }

    [Fact]
    public async Task Changing_the_classify_model_changes_the_fingerprint()
    {
        await Run();
        _kit.Gemini.Models["Classify"] = "another-model";

        Assert.False(((Completed)(await Run())[^1]).FromCache);
    }

    [Fact]
    public async Task Readings_are_cached_by_revision_so_a_rerun_reads_no_wikitext()
    {
        await Run();
        var reads = _kit.Revisions.TextReads;

        await Run(refresh: true);

        Assert.True(reads > 0);
        Assert.Equal(reads, _kit.Revisions.TextReads);
        await using var db = _db.CreateContext();
        Assert.Equal(2, await db.Set<EditReadingCacheRow>().CountAsync());
    }

    [Fact]
    public async Task A_reading_cache_storage_problem_does_not_cost_the_reading()
    {
        await using (var db = _db.CreateContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE EditReadingCache");

        var done = Assert.IsType<Completed>((await Run())[^1]);

        Assert.Equal(0, done.Degraded);
        Assert.NotNull(Assert.Single(done.Incidents).Assessment);
    }

    [Fact]
    public async Task A_cached_reading_round_trips_the_diff()
    {
        var o = Microsoft.Extensions.Options.Options.Create(_kit.Options);
        var reader = new CachedEditReader(new CorpusEditReader(_kit.Revisions, o), o, _db.Factory, TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CachedEditReader>.Instance);
        var edit = new RemovedEdit(new TopicArticle("fi", 1256, "Talvisota", 0), FakeInvestigationCorpus.Revert(1256, 2000));

        var fresh = await reader.ReadAsync(edit);
        var cached = await reader.ReadAsync(edit);

        Assert.Equal(fresh.Diff.Segments, cached.Diff.Segments);
        Assert.Equal(fresh.Diff.Stats, cached.Diff.Stats);
        Assert.Equal(fresh.Span, cached.Span);
        Assert.Equal((fresh.InsertedText, fresh.RemovedText, fresh.Context), (cached.InsertedText, cached.RemovedText, cached.Context));
    }

    [Fact]
    public async Task Topics_lists_at_most_50_for_current_fingerprint_newest_first()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var o = Microsoft.Extensions.Options.Options.Create(_kit.Options);
        var gemini = Microsoft.Extensions.Options.Options.Create(_kit.Gemini);
        var prompts = new AmpersandOfTruth.Web.Features.Analysis.Prompts.PromptLibrary(
            gemini, Microsoft.Extensions.Logging.Abstractions.NullLogger<AmpersandOfTruth.Web.Features.Analysis.Prompts.PromptLibrary>.Instance);
        var cache = new InvestigationRunCache(
            _db.Factory,
            new PipelineFingerprint(o, gemini, prompts),
            clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InvestigationRunCache>.Instance);

        var snapshot = new RunSnapshot([], 0, [], [], [], clock.GetUtcNow(), 10, new Dictionary<InvestigationStage, double>(), null);
        for (var i = 1; i <= 55; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await cache.StoreAsync($"topic {i}", snapshot);
        }

        var topics = await cache.TopicsAsync();
        Assert.Equal(50, topics.Count);
        Assert.Equal("topic 55", topics[0]);
        Assert.Equal("topic 6", topics[^1]);

        _kit.Gemini.Models["Classify"] = "another-model";
        var otherCache = new InvestigationRunCache(
            _db.Factory,
            new PipelineFingerprint(o, gemini, prompts),
            clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InvestigationRunCache>.Instance);
        Assert.Empty(await otherCache.TopicsAsync());

        await using (var db = _db.CreateContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE InvestigationRuns");

        Assert.Empty(await cache.TopicsAsync());
    }
}
