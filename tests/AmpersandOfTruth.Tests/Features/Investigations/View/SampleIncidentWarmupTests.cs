using System.Diagnostics;
using AmpersandOfTruth.Web.Features.Articles;
using System.Collections.Concurrent;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public class SampleIncidentWarmupTests
{
    private static readonly TimeSpan TextDelay = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Six_incidents_fetch_texts_concurrently_within_and_across_incidents_and_keep_rank_order()
    {
        var (singleCorpus, singleRevisions, singleRecords) = BuildSixIncidentFixture(TimeSpan.Zero);
        var singleMapper = new IncidentMapper(singleCorpus, singleRevisions);

        // Text fetches are held open, so the parent text is in flight only if it was requested before the revision
        // text came back. One after the other would stop at 1 and fail the wait.
        singleRevisions.TextCalls.Hold();
        var mappingOne = singleMapper.MapAsync(singleRecords[0], rank: 1);
        await singleRevisions.TextCalls.WaitForAsync(2);
        singleRevisions.TextCalls.Release();
        var one = await mappingOne;

        Assert.NotNull(one.Passage);
        Assert.Equal(2, singleRevisions.TextCalls.Max);

        var (corpus, revisions, records) = BuildSixIncidentFixture(TimeSpan.Zero);
        // Shuffle input order to verify rank ordering by descending score is preserved under concurrency.
        var shuffled = new[] { records[3], records[0], records[5], records[2], records[1], records[4] };
        var samples = SampleIncidents.From(new SampleTopic("winter-war", "Winter War", ["talvisota"], shuffled));
        var source = new FixtureIncidentSource(samples, new IncidentMapper(corpus, revisions));

        // One incident fetches two texts, so a third in flight means a second incident is being mapped at the same time.
        revisions.TextCalls.Hold();
        var mappingSix = source.ForTopicAsync("winter-war");
        await revisions.TextCalls.WaitForAsync(3);
        revisions.TextCalls.Release();
        var views = await mappingSix;

        Assert.NotNull(views);
        Assert.Equal(6, views.Count);

        // Expected order is records[0..5] (scores 60, 50, 40, 30, 20, 10) with ranks 1..6.
        Assert.Equal(
            records.Select((r, idx) => (r.Id!, idx + 1)),
            views.Select(v => (v.Id, v.Rank)));
        Assert.All(views, v => Assert.NotNull(v.Passage));

        // Max 4 incidents in flight × 2 concurrent text calls per incident = at most 8 concurrent text reads.
        Assert.InRange(revisions.TextCalls.Max, 3, 8);
    }

    [Fact]
    public async Task Concurrent_ForTopicAsync_calls_hit_fake_corpus_once_per_text_and_evict_failed_inflight_entries()
    {
        var (corpus, revisions, records) = BuildSixIncidentFixture(TextDelay);
        corpus.Delay = TimeSpan.FromMilliseconds(50);
        var samples = SampleIncidents.From(new SampleTopic("winter-war", "Winter War", ["talvisota"], records));
        var mapper = new IncidentMapper(corpus, revisions);
        var source = new FixtureIncidentSource(samples, mapper);

        var callers = await Task.WhenAll(
            source.ForTopicAsync("winter-war"),
            source.ForTopicAsync("Talvisota"),
            source.ForTopicAsync("winter-war"),
            source.ForTopicAsync("talvisota"));

        foreach (var result in callers)
        {
            Assert.NotNull(result);
            Assert.Equal(6, result.Count);
            Assert.All(result, v => Assert.NotNull(v.Passage));
        }

        // 6 incidents × 2 texts (inserting + parent) = 12 distinct texts, each fetched strictly once.
        Assert.Equal(12, revisions.TotalTextCalls);
        Assert.Equal(12, revisions.CallsPerText.Count);
        Assert.All(revisions.CallsPerText.Values, calls => Assert.Equal(1, calls));
        Assert.Equal(6, corpus.ArticleCalls);
        Assert.Equal(6, corpus.RevisionCalls);

        // Also verify failed/passage-less entries in IncidentMapper are evicted so a later call retries.
        var extra = MakeIncident(index: 99, score: 1.0);
        revisions.Fault = new InvalidOperationException("transient boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.WhenAll(mapper.MapAsync(extra, 1), mapper.MapAsync(extra, 2)));

        revisions.Fault = null;
        revisions.Unavailable = true;
        var degradedPair = await Task.WhenAll(mapper.MapAsync(extra, 1), mapper.MapAsync(extra, 2));
        Assert.All(degradedPair, v => Assert.Null(v.Passage));

        // Populate text for incident 99 and bring store up: retry must succeed and cache.
        revisions.Unavailable = false;
        SeedIncidentData(corpus, revisions, extra, index: 99);

        var recovered = await mapper.MapAsync(extra, 4);
        Assert.NotNull(recovered.Passage);
        Assert.Equal(4, recovered.Rank);
    }

    [Fact]
    public async Task Source_list_is_cached_after_success_and_not_cached_after_failure()
    {
        var (corpus, revisions, records) = BuildSixIncidentFixture(TimeSpan.Zero);
        var samples = SampleIncidents.From(new SampleTopic("winter-war", "Winter War", ["talvisota"], records));
        var source = new FixtureIncidentSource(samples, new IncidentMapper(corpus, revisions));

        // 1. Corpus completely down -> none have a Passage -> list NOT cached.
        corpus.Unavailable = true;
        revisions.Unavailable = true;

        var whenDown = await source.ForTopicAsync("winter-war");
        Assert.NotNull(whenDown);
        Assert.All(whenDown, v => Assert.Null(v.Passage));

        // 2. Partial failure (1 of 6 missing text) -> 5 have Passage, 1 does not -> topic list STILL not cached.
        corpus.Unavailable = false;
        revisions.Unavailable = false;
        var missingKey = (1256L, records[5].InsertingRevIds[0]);
        Assert.True(revisions.Texts.TryRemove(missingKey, out var savedText));

        var partial = await source.ForTopicAsync("winter-war");
        Assert.NotNull(partial);
        Assert.Equal(5, partial.Count(v => v.Passage is not null));
        Assert.Null(partial[5].Passage);

        // 3. Restore missing text -> retry fetches only the 1 missing incident and caches the full topic list.
        revisions.Texts[missingKey] = savedText;
        var callsBeforeRetry = revisions.TotalTextCalls;

        var complete = await source.ForTopicAsync("winter-war");
        Assert.NotNull(complete);
        Assert.All(complete, v => Assert.NotNull(v.Passage));
        Assert.Equal(callsBeforeRetry + 2, revisions.TotalTextCalls); // only the 6th incident's 2 texts fetched

        // 4. Break corpus completely again -> cached topic list is returned directly without touching corpus or revisions.
        corpus.Unavailable = true;
        revisions.Unavailable = true;
        var articleCallsBefore = corpus.ArticleCalls;
        var revisionCallsBefore = corpus.RevisionCalls;
        var textCallsBefore = revisions.TotalTextCalls;

        var cachedBySlug = await source.ForTopicAsync("winter-war");
        var cachedByAlias = await source.ForTopicAsync("talvisota");

        Assert.Same(complete, cachedBySlug);
        Assert.Same(complete, cachedByAlias);
        Assert.Equal(articleCallsBefore, corpus.ArticleCalls);
        Assert.Equal(revisionCallsBefore, corpus.RevisionCalls);
        Assert.Equal(textCallsBefore, revisions.TotalTextCalls);
    }

    [Fact]
    public async Task Warmup_waits_2s_warms_each_topic_and_logs_duration()
    {
        var (corpus, revisions, records) = BuildSixIncidentFixture(TimeSpan.Zero);
        var samples = SampleIncidents.From(
            new SampleTopic("winter-war", "Winter War", [], records.Take(2).ToList()),
            new SampleTopic("continuation-war", "Continuation War", [], records.Skip(2).Take(2).ToList()));
        var source = new FixtureIncidentSource(samples, new IncidentMapper(corpus, revisions));
        var clock = new SignalingTimeProvider();
        var logger = new CapturingLogger<SampleIncidentWarmup>();

        using var warmup = new SampleIncidentWarmup(
            samples,
            source,
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }),
            BuildConfig(),
            clock,
            logger);

        await warmup.StartAsync(CancellationToken.None);
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Before the 2-second delay elapses, startup returned immediately and nothing hit the corpus yet.
        clock.Advance(TimeSpan.FromMilliseconds(1900));
        await Task.Yield();
        Assert.Equal(0, revisions.TotalTextCalls);
        Assert.NotNull(warmup.ExecuteTask);
        Assert.False(warmup.ExecuteTask.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(200));
        await warmup.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
        await warmup.StopAsync(CancellationToken.None);

        // Both sample topics (4 incidents × 2 texts = 8 texts) were warmed and cached.
        Assert.Equal(8, revisions.TotalTextCalls);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, e =>
        {
            Assert.Equal(LogLevel.Information, e.Level);
            Assert.Contains("ms", e.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Warmup_with_corpus_throwing_logs_one_warning_per_topic_and_completes()
    {
        var records = Enumerable.Range(1, 3).Select(i => MakeIncident(i, 100 - i)).ToList();
        var samples = SampleIncidents.From(
            new SampleTopic("winter-war", "Winter War", [], records),
            new SampleTopic("civil-war-1918", "Civil War 1918", [], records));

        // Case 1: Corpus throws an unhandled exception out of ForTopicAsync.
        var throwingCorpus = new FakeIncidentCorpus { Fault = new InvalidOperationException("tunnel exploded") };
        var throwingRevisions = new DelayedRevisionStore();
        var throwingSource = new FixtureIncidentSource(samples, new IncidentMapper(throwingCorpus, throwingRevisions));
        var clock1 = new SignalingTimeProvider();
        var logger1 = new CapturingLogger<SampleIncidentWarmup>();

        using (var warmup1 = new SampleIncidentWarmup(
            samples,
            throwingSource,
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }),
            BuildConfig(),
            clock1,
            logger1))
        {
            await warmup1.StartAsync(CancellationToken.None);
            await clock1.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(warmup1.ExecuteTask);
            clock1.Advance(TimeSpan.FromMilliseconds(2100));
            await warmup1.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
            await warmup1.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, logger1.Entries.Count);
        Assert.All(logger1.Entries, e =>
        {
            Assert.Equal(LogLevel.Warning, e.Level);
            Assert.IsType<InvalidOperationException>(e.Exception);
            Assert.Contains("ms", e.Message, StringComparison.Ordinal);
        });

        // Case 2: Corpus throws CorpusUnavailableException (degrades to Passage == null) -> still 1 warning per topic, no spam.
        var downCorpus = new FakeIncidentCorpus { Unavailable = true };
        var downRevisions = new DelayedRevisionStore { Unavailable = true };
        var downSource = new FixtureIncidentSource(samples, new IncidentMapper(downCorpus, downRevisions));
        var clock2 = new SignalingTimeProvider();
        var logger2 = new CapturingLogger<SampleIncidentWarmup>();

        using (var warmup2 = new SampleIncidentWarmup(
            samples,
            downSource,
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }),
            BuildConfig(),
            clock2,
            logger2))
        {
            await warmup2.StartAsync(CancellationToken.None);
            await clock2.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(warmup2.ExecuteTask);
            clock2.Advance(TimeSpan.FromMilliseconds(2100));
            await warmup2.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
            await warmup2.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, logger2.Entries.Count);
        Assert.All(logger2.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    private static (FakeIncidentCorpus Corpus, DelayedRevisionStore Revisions, List<IncidentRecord> Records) BuildSixIncidentFixture(
        TimeSpan textDelay)
    {
        var corpus = new FakeIncidentCorpus();
        corpus.Articles[1256] = new ArticleRef(1256, "Talvisota", "Winter War");
        var revisions = new DelayedRevisionStore { TextDelay = textDelay };
        var records = new List<IncidentRecord>(6);

        for (var i = 1; i <= 6; i++)
        {
            var record = MakeIncident(i, score: (7 - i) * 10.0);
            SeedIncidentData(corpus, revisions, record, i);
            records.Add(record);
        }

        return (corpus, revisions, records);
    }

    private static IncidentRecord MakeIncident(int index, double score)
    {
        var insRev = index * 10L + 1;
        var remRev = index * 10L + 2;
        return new IncidentRecord
        {
            Id = $"ww-{index:00}",
            PageId = 1256,
            InsertingRevIds = [insRev],
            RemovingRevIds = [remRev],
            Tier = EvidenceTier.Contested,
            Score = score,
            ClaimEn = $"Claim {index}",
            ClaimFi = $"Väite {index}",
        };
    }

    private static void SeedIncidentData(
        FakeIncidentCorpus corpus,
        DelayedRevisionStore revisions,
        IncidentRecord record,
        int index)
    {
        var insRev = record.InsertingRevIds[0];
        var parentRev = insRev - 1;
        var remRev = record.RemovingRevIds[0];
        var ts = new DateTime(2024, 2, 1, 12, 0, 0, DateTimeKind.Utc).AddHours(index);

        corpus.Revisions.AddRange([
            new RevisionRow(1256, insRev, parentRev, ts, "192.0.2.10", "lisäys", 150),
            new RevisionRow(1256, remRev, insRev, ts.AddMinutes(10), "Reviewer", "kumottu", 100),
        ]);
        revisions.Texts[(1256, parentRev)] = $"Talvisota kappale {index}.";
        revisions.Texts[(1256, insRev)] = $"Talvisota kappale {index}. Lisätty kiistanalainen väite {index}.";
    }

    private sealed class DelayedRevisionStore : IRevisionStore
    {
        private int _totalTextCalls;

        public ConcurrentDictionary<(long Page, long Rev), string> Texts { get; } = new();
        public ConcurrentDictionary<(long Page, long Rev), int> CallsPerText { get; } = new();
        public InFlightCalls TextCalls { get; } = new();
        public TimeSpan TextDelay { get; set; }

        public Task<IReadOnlyList<RevisionRow>> RestoresAsync(
            long pageId, DateTime afterTs, long afterRevId, IReadOnlyCollection<string> sha1s, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RevisionRow>>([]);
        public Task<IReadOnlyList<RevisionRow>> ReturnsToAsync(
            long pageId, DateTime afterTs, long afterRevId, long stateRevId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RevisionRow>>([]);
        public bool Unavailable { get; set; }
        public Exception? Fault { get; set; }
        public int TotalTextCalls => Volatile.Read(ref _totalTextCalls);

        public Task<int> RevisionCountAsync(IReadOnlyCollection<long> pageIds, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<RevisionRow>> SpanAsync(
            long pageId, long restoredRevId, long lastRevId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RevisionRow>>([]);

        public async Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _totalTextCalls);
            CallsPerText.AddOrUpdate((pageId, revId), 1, static (_, c) => c + 1);

            using var call = await TextCalls.EnterAsync(ct);
            if (TextDelay > TimeSpan.Zero)
            {
                await Task.Delay(TextDelay, ct);
            }

            if (Fault is not null)
            {
                throw Fault;
            }

            if (Unavailable)
            {
                throw new CorpusUnavailableException("delayed fake revision store is down");
            }

            return Texts.GetValueOrDefault((pageId, revId));
        }
    }

    private sealed class SignalingTimeProvider : FakeTimeProvider
    {
        public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }


    [Fact]
    public async Task Warmup_warms_article_aggregates_sequentially_after_incidents_for_all_referenced_pages()
    {
        var (corpus, revisions, records) = BuildSixIncidentFixture(TimeSpan.Zero);
        var inc1 = records[0] with
        {
            PageId = 1256,
            OtherPageIds = [2048],
            Articles =
            [
                new IncidentArticle(1256, "Talvisota", "Winter War"),
                new IncidentArticle(2048, "Mainilan laukaukset", "Shelling of Mainila"),
            ],
        };
        var inc2 = records[1] with
        {
            Edits =
            [
                new IncidentEdit(9001, 9000, 3099, "UserA", false, DateTimeOffset.UtcNow, IncidentEditRole.Inserted, "added", null, []),
            ],
        };
        corpus.Articles[2048] = new ArticleRef(2048, "Mainilan laukaukset", "Shelling of Mainila");

        var samples = SampleIncidents.From(new SampleTopic("winter-war", "Winter War", [], [inc1, inc2]));
        var events = new ConcurrentQueue<string>();
        var trackingSource = new EventRecordingIncidentSource(
            new FixtureIncidentSource(samples, new IncidentMapper(corpus, revisions)),
            events);
        var articleCorpus = new TrackingArticleCorpus(events);
        var clock = new SignalingTimeProvider();
        var timeline = new ArticleTimelineService(articleCorpus, clock);
        var logger = new CapturingLogger<SampleIncidentWarmup>();

        using var warmup = new SampleIncidentWarmup(
            samples,
            trackingSource,
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }),
            BuildConfig(),
            clock,
            logger,
            timeline);

        await warmup.StartAsync(CancellationToken.None);
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(warmup.ExecuteTask);
        clock.Advance(TimeSpan.FromMilliseconds(2100));
        await warmup.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
        await warmup.StopAsync(CancellationToken.None);

        // Incident warm-up ran first, followed sequentially by distinct page IDs [1256, 2048, 3099].
        Assert.Equal(["topic:winter-war", "page:1256", "page:2048", "page:3099"], events.ToArray());
        Assert.Equal(1, articleCorpus.MaxConcurrent);
        Assert.True(timeline.TryGetCachedAggregates(1256, out var cached1256) && cached1256 is not null);
        Assert.True(timeline.TryGetCachedAggregates(2048, out var cached2048) && cached2048 is not null);
        Assert.True(timeline.TryGetCachedAggregates(3099, out var cached3099) && cached3099 is not null);

        // Subsequent calls are served from ArticleTimelineService cache with 0 extra corpus queries.
        await timeline.AggregatesAsync(1256);
        Assert.Equal(3, articleCorpus.Calls);
        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
    }

    [Fact]
    public async Task Warmup_logs_article_aggregate_failures_once_across_all_sample_pages()
    {
        var (corpus, revisions, records) = BuildSixIncidentFixture(TimeSpan.Zero);
        var inc1 = records[0] with { PageId = 1256, OtherPageIds = [2048, 3099] };
        var samples = SampleIncidents.From(new SampleTopic("winter-war", "Winter War", [], [inc1]));
        var source = new FixtureIncidentSource(samples, new IncidentMapper(corpus, revisions));
        var articleCorpus = new TrackingArticleCorpus(new ConcurrentQueue<string>())
        {
            Fault = new CorpusUnavailableException("corpus offline"),
        };
        var clock = new SignalingTimeProvider();
        var timeline = new ArticleTimelineService(articleCorpus, clock);
        var logger = new CapturingLogger<SampleIncidentWarmup>();

        using var warmup = new SampleIncidentWarmup(
            samples,
            source,
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }),
            BuildConfig(),
            clock,
            logger,
            timeline);

        await warmup.StartAsync(CancellationToken.None);
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(warmup.ExecuteTask);
        clock.Advance(TimeSpan.FromMilliseconds(2100));
        await warmup.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
        await warmup.StopAsync(CancellationToken.None);

        Assert.Equal(3, articleCorpus.Calls);
        var entries = logger.Entries.ToList();
        // 1 info for topic incident warm-up + strictly 1 warning summarizing all 3 failed article pages.
        Assert.Equal(2, entries.Count);
        Assert.Equal(LogLevel.Information, entries[0].Level);
        Assert.Equal(LogLevel.Warning, entries[1].Level);
        Assert.IsType<CorpusUnavailableException>(entries[1].Exception);
        Assert.Contains("3/3", entries[1].Message, StringComparison.Ordinal);
    }

    private sealed class EventRecordingIncidentSource(IIncidentSource inner, ConcurrentQueue<string> events) : IIncidentSource
    {
        public async Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default)
        {
            var res = await inner.ForTopicAsync(topicSlug, ct);
            events.Enqueue($"topic:{topicSlug}");
            return res;
        }
    }

    private sealed class TrackingArticleCorpus(ConcurrentQueue<string> events) : IArticleCorpus
    {
        private int _calls;
        private int _inFlight;
        private int _maxConcurrent;

        public Exception? Fault { get; init; }
        public int Calls => Volatile.Read(ref _calls);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public async Task<ArticleAggregates?> AggregatesAsync(long pageId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            var current = Interlocked.Increment(ref _inFlight);
            while (true)
            {
                var snap = Volatile.Read(ref _maxConcurrent);
                if (current <= snap || Interlocked.CompareExchange(ref _maxConcurrent, current, snap) == snap)
                {
                    break;
                }
            }

            try
            {
                await Task.Yield();
                events.Enqueue($"page:{pageId}");
                if (Fault is not null)
                {
                    throw Fault;
                }

                return new ArticleAggregates(
                    pageId,
                    $"Artikkeli {pageId}",
                    $"Article {pageId}",
                    Revisions: 10,
                    FirstEdit: new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    LastEdit: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Months: [new MonthBucket(2020, 1, 5, 1, 1, 1_000)]);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<IReadOnlyList<ArticleRevision>> RevisionsAsync(
            long pageId, DateRange range, int limit = 500, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ArticleRevision>>([]);
    }
}
