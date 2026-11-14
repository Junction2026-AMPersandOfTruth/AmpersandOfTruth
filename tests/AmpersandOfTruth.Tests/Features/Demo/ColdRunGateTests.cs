using System.Runtime.CompilerServices;
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
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Demo;

public sealed class ColdRunGateTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly PipelineKit _kit = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Max1_second_cold_run_yields_RunQueued_1_then_proceeds_after_first_finishes()
    {
        using var gate = new ColdRunGate(maxConcurrent: 1);
        var blockingSearch = new GatedTopicSearch();
        var runner = BuildGatedRunner(gate, blockingSearch);

        // Start first cold run and hold it inside stage 1.
        blockingSearch.HoldNext();
        var firstEvents = new List<InvestigationEvent>();
        var firstTask = Task.Run(async () =>
        {
            await foreach (var e in runner.RunAsync("Topic One"))
            {
                firstEvents.Add(e);
            }
        });

        await blockingSearch.WaitUntilEnteredAsync();
        Assert.Equal(0, gate.AvailableSlots);

        // Start second cold run while first holds the gate: must immediately emit RunQueued(1).
        await using var secondEnum = runner.RunAsync("Topic Two").GetAsyncEnumerator();
        Assert.True(await secondEnum.MoveNextAsync());
        var queued = Assert.IsType<RunQueued>(secondEnum.Current);
        Assert.Equal(1, queued.Position);

        var secondRemainder = Task.Run(async () =>
        {
            var rest = new List<InvestigationEvent>();
            while (await secondEnum.MoveNextAsync())
            {
                rest.Add(secondEnum.Current);
            }
            return rest;
        });

        await WaitUntilAsync(() => gate.WaitingCount == 1);

        // Release first cold run; second must proceed once first finishes.
        blockingSearch.ReleaseCurrent();
        await firstTask;
        var secondEvents = await secondRemainder;

        Assert.DoesNotContain(firstEvents, e => e is RunQueued);
        Assert.IsType<Completed>(firstEvents[^1]);
        Assert.IsType<ArticlesFound>(secondEvents[0]);
        var secondDone = Assert.IsType<Completed>(secondEvents[^1]);
        Assert.False(secondDone.FromCache);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Cached_replay_never_queues_even_when_cold_gate_is_full()
    {
        using var gate = new ColdRunGate(maxConcurrent: 1);
        var blockingSearch = new GatedTopicSearch();
        var runner = BuildGatedRunner(gate, blockingSearch);

        // Warm "Talvisota" in SQLite run cache.
        var warmSetup = await PipelineKit.Collect(runner.RunAsync("Talvisota"));
        Assert.False(Assert.IsType<Completed>(warmSetup[^1]).FromCache);

        // Occupy the single cold slot with a different cold run.
        blockingSearch.HoldNext();
        var coldTask = Task.Run(async () => await PipelineKit.Collect(runner.RunAsync("Uncached Cold Topic")));
        await blockingSearch.WaitUntilEnteredAsync();
        Assert.Equal(0, gate.AvailableSlots);

        // Replay "Talvisota" while gate is full: completes immediately with no RunQueued.
        var replayEvents = await PipelineKit.Collect(runner.RunAsync("Talvisota"));
        Assert.DoesNotContain(replayEvents, e => e is RunQueued);
        Assert.True(Assert.IsType<Completed>(replayEvents[^1]).FromCache);

        blockingSearch.ReleaseCurrent();
        await coldTask;
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Cancellation_while_queued_releases_nothing_it_did_not_take()
    {
        using var gate = new ColdRunGate(maxConcurrent: 1);
        var blockingSearch = new GatedTopicSearch();
        var runner = BuildGatedRunner(gate, blockingSearch);

        blockingSearch.HoldNext();
        var firstTask = Task.Run(async () => await PipelineKit.Collect(runner.RunAsync("First Cold")));
        await blockingSearch.WaitUntilEnteredAsync();
        Assert.Equal(0, gate.AvailableSlots);

        // Queue second run, advance it into WaitAsync, then cancel it while first is still running.
        using var secondCts = new CancellationTokenSource();
        var secondObservedQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTask = Task.Run(async () =>
        {
            await foreach (var e in runner.RunAsync("Second Cancelled", ct: secondCts.Token))
            {
                if (e is RunQueued { Position: 1 })
                {
                    secondObservedQueued.TrySetResult();
                }
            }
        });

        await secondObservedQueued.Task;
        await WaitUntilAsync(() => gate.WaitingCount == 1);

        secondCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);

        // First run STILL holds the sole slot; nothing was released.
        Assert.Equal(0, gate.AvailableSlots);
        Assert.Equal(0, gate.WaitingCount);

        // A third cold run started now must still queue behind the first run!
        await using var thirdEnum = runner.RunAsync("Third Cold").GetAsyncEnumerator();
        Assert.True(await thirdEnum.MoveNextAsync());
        var thirdQueued = Assert.IsType<RunQueued>(thirdEnum.Current);
        Assert.Equal(1, thirdQueued.Position);

        var thirdRestTask = Task.Run(async () =>
        {
            var events = new List<InvestigationEvent>();
            while (await thirdEnum.MoveNextAsync())
            {
                events.Add(thirdEnum.Current);
            }
            return events;
        });

        blockingSearch.ReleaseCurrent();
        await firstTask;
        var thirdEvents = await thirdRestTask;

        Assert.IsType<Completed>(thirdEvents[^1]);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public void StageTracker_and_RunProgress_show_run_queued_until_first_stage_event_arrives()
    {
        using var bunit = new BunitContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var progress = new RunProgress(clock);
        progress.Start();
        progress.Apply(new RunQueued(1));

        Assert.Equal(1, progress.QueuedPosition);
        Assert.Equal(StageState.Pending, progress.State(InvestigationStage.FindArticles));

        var cut = bunit.Render<StageTracker>(p => p.Add(x => x.Progress, progress));
        var queuedBanner = cut.Find("[data-testid=run-queued]");
        Assert.Contains("Queued — 1 investigation ahead", queuedBanner.TextContent);
        Assert.Empty(CopyRules.FindViolations(queuedBanner.TextContent, includeJargon: true));

        // Update queue position to 2, then deliver first stage event.
        progress.Apply(new RunQueued(2));
        cut.Render();
        Assert.Contains("Queued — 2 investigations ahead", cut.Find("[data-testid=run-queued]").TextContent);

        progress.Apply(new ArticlesFound(1, [new TopicArticle("fi", 1256, "Talvisota", 0.05)]));
        cut.Render();

        Assert.Null(progress.QueuedPosition);
        Assert.Empty(cut.FindAll("[data-testid=run-queued]"));
        Assert.Equal("done", cut.Find("[data-testid=stage-1]").GetAttribute("data-state"));
    }

    private CachingInvestigationRunner BuildGatedRunner(ColdRunGate gate, ITopicSearch topicSearch)
    {
        var opts = Options.Create(_kit.Options);
        var pipeline = new InvestigationPipeline(
            topicSearch,
            new RevertCandidateSource(_kit.Corpus, _kit.Revisions, opts),
            new CorpusEditReader(_kit.Revisions, opts, _kit.Conflicts),
            new GeminiEditClassifier(_kit.Model, opts, NullLogger<GeminiEditClassifier>.Instance),
            opts,
            TimeProvider.System,
            NullLogger<InvestigationPipeline>.Instance);

        var gemini = Options.Create(_kit.Gemini);
        var fp = new PipelineFingerprint(opts, gemini, new PromptLibrary(gemini, NullLogger<PromptLibrary>.Instance));
        var cache = new InvestigationRunCache(
            _db.Factory, fp, TimeProvider.System, NullLogger<InvestigationRunCache>.Instance);

        return new CachingInvestigationRunner(pipeline, cache, TimeProvider.System, gate);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(predicate());
    }

    private sealed class GatedTopicSearch : ITopicSearch
    {
        private readonly object _lock = new();
        private TaskCompletionSource? _entered;
        private TaskCompletionSource? _release;

        public void HoldNext()
        {
            lock (_lock)
            {
                _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public Task WaitUntilEnteredAsync()
        {
            Task task;
            lock (_lock)
            {
                task = _entered?.Task ?? Task.CompletedTask;
            }
            return task;
        }

        public void ReleaseCurrent()
        {
            TaskCompletionSource? release;
            lock (_lock)
            {
                release = _release;
                _release = null;
                _entered = null;
            }
            release?.TrySetResult();
        }

        public async Task<IReadOnlyList<TopicArticle>> FindArticlesAsync(string topic, CancellationToken ct = default)
        {
            TaskCompletionSource? entered;
            TaskCompletionSource? release;
            lock (_lock)
            {
                entered = _entered;
                release = _release;
            }

            if (entered is not null && release is not null)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }

            return [new TopicArticle("fi", 1256, "Talvisota", 0.05)];
        }
    }
}
