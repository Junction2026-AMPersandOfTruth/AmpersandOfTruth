using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AmpersandOfTruth.Web.Features.Embedding;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// P1.P1 (#39): topic → articles → removed edits → (read → classify → assemble) per edit with bounded parallelism,
/// streamed as <see cref="InvestigationEvent"/>s. Stateless singleton; every run is independent and uncached at the run
/// level (<see cref="Cache.CachingInvestigationRunner"/> wraps it for the app).
/// <list type="bullet">
/// <item>A missing embedder or corpus in stage 1–2 ends the run with <see cref="StageFailed"/>, never an exception.</item>
/// <item>One unreadable edit is skipped (<c>CandidateScored</c> with no incident); only if every edit fails does stage 3 fail.</item>
/// <item>An LLM failure on one edit degrades that incident to the lowest tier (the classifier returns null).</item>
/// <item>Edits the evidence standard excludes without an assessment (not substantive, self-removal, bot, blanking) are
/// never sent to the LLM.</item>
/// <item>Cancelling the token (new search, tab closed) stops all in-flight work and ends the stream with
/// <see cref="OperationCanceledException"/>.</item>
/// </list>
/// </summary>
public sealed class InvestigationPipeline(
    ITopicSearch search,
    ICandidateSource candidates,
    IEditReader reader,
    IEditClassifier classifier,
    IOptions<InvestigationPipelineOptions> options,
    TimeProvider time,
    ILogger<InvestigationPipeline> log) : IInvestigationRunner
{
    /// <summary>Bump when the pipeline's logic changes what a run produces; part of the run-cache fingerprint.</summary>
    public const int Version = 2;   // 2: S10 — passage-level edit war, source change, MinCoordinationSignals 3

    /// <inheritdoc />
    /// <remarks><paramref name="refresh"/> is a no-op here: this class caches nothing at the run level.</remarks>
    public IAsyncEnumerable<InvestigationEvent> RunAsync(string topic, bool refresh = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        topic = topic.Trim();
        return StreamAsync(topic, token => search.FindArticlesAsync(topic, token), ct);
    }

    /// <summary>Stages 2–4 on given articles (the eval's oracle retrieval, contract §2.2). Stage 1 just echoes them.</summary>
    public IAsyncEnumerable<InvestigationEvent> RunOnArticlesAsync(string topic, IReadOnlyList<TopicArticle> articles, CancellationToken ct = default) =>
        StreamAsync(topic, _ => Task.FromResult(articles), ct);

    private async IAsyncEnumerable<InvestigationEvent> StreamAsync(
        string topic, Func<CancellationToken, Task<IReadOnlyList<TopicArticle>>> findArticles, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<InvestigationEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = Task.Run(() => ProduceAsync(topic, findArticles, channel.Writer, run.Token), CancellationToken.None);
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(ct))
                yield return e;
        }
        finally
        {
            await run.CancelAsync(); // the consumer stopped early or was cancelled → stop the producer too
            await producer;          // never throws: failures are completed into the channel
        }
    }

    private async Task ProduceAsync(
        string topic, Func<CancellationToken, Task<IReadOnlyList<TopicArticle>>> findArticles,
        ChannelWriter<InvestigationEvent> writer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var startedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)); // whole seconds, as stored (IncidentJson)
        var started = time.GetTimestamp();
        var timings = new Dictionary<InvestigationStage, TimeSpan>();
        var stage = InvestigationStage.FindArticles;
        var stageStarted = started;

        void Done(InvestigationStage next)
        {
            var now = time.GetTimestamp();
            timings[stage] = time.GetElapsedTime(stageStarted, now);
            stage = next;
            stageStarted = now;
        }

        try
        {
            var articles = await findArticles(ct);
            writer.TryWrite(new ArticlesFound(articles.Count, articles));
            Done(InvestigationStage.ScanHistory);

            var scan = await candidates.ScanAsync(articles, ct);
            writer.TryWrite(new RevisionsScanned(scan.RevisionsScanned, scan.Candidates));
            Done(InvestigationStage.ReadEdits);

            var scored = await ScoreAllAsync(scan.Candidates, $"investigation:{Guid.NewGuid():N}", started, writer, ct);
            if (scored is null) return; // StageFailed already written
            Done(InvestigationStage.AssembleIncidents);

            var ranked = scored.Incidents.OrderByDescending(i => i.Score).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
            Done(InvestigationStage.AssembleIncidents);
            var completed = new Completed(FromCache: false, ranked, time.GetElapsedTime(started))
            {
                AnalysedAt = startedAt,
                Degraded = scored.Degraded,
                StageTimings = timings,
                FirstIncidentAfter = scored.FirstIncidentAfter,
            };
            log.LogInformation(
                "Investigation {Topic}: {Articles} articles, {Candidates} removed edits, {Incidents} incidents, {Degraded} degraded; " +
                "find {Find:0} ms, scan {Scan:0} ms, read {Read:0} ms, first incident {First:0} ms, total {Total:0} ms",
                topic, articles.Count, scan.Candidates.Count, ranked.Count, scored.Degraded,
                Ms(InvestigationStage.FindArticles), Ms(InvestigationStage.ScanHistory), Ms(InvestigationStage.ReadEdits),
                scored.FirstIncidentAfter?.TotalMilliseconds, completed.Elapsed.TotalMilliseconds);
            writer.TryWrite(completed);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            writer.TryComplete(ex);
        }
        catch (Exception ex)
        {
            var kind = Classify(ex);
            if (kind == StageFailureKind.Error) log.LogError(ex, "Investigation of {Topic} failed in {Stage}", topic, stage);
            writer.TryWrite(new StageFailed(stage, kind, ex.Message));
        }
        finally
        {
            writer.TryComplete();
        }

        double Ms(InvestigationStage s) => timings.TryGetValue(s, out var t) ? t.TotalMilliseconds : 0;
    }

    private sealed record Scored(List<IncidentRecord> Incidents, int Degraded, TimeSpan? FirstIncidentAfter);

    /// <returns>The incidents, or null when every candidate failed (a <see cref="StageFailed"/> has been written).</returns>
    private async Task<Scored?> ScoreAllAsync(
        IReadOnlyList<RemovedEdit> edits, string scope, long runStarted, ChannelWriter<InvestigationEvent> writer, CancellationToken ct)
    {
        var incidents = new List<IncidentRecord>();
        var gate = new Lock();
        int done = 0, failed = 0, unassessed = 0;
        TimeSpan? firstIncident = null;
        Exception? lastFailure = null;

        await Parallel.ForEachAsync(edits,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.Parallelism), CancellationToken = ct },
            async (edit, token) =>
            {
                Outcome outcome;
                try
                {
                    outcome = await ScoreAsync(edit, scope, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    log.LogWarning(ex, "Could not read removed edit {Edit}", edit.Key);
                    outcome = new Outcome(null, "unreadable", AssessmentUnavailable: false);
                    lock (gate) { failed++; lastFailure = ex; }
                }

                lock (gate) // index and write together so CandidateScored arrives in index order
                {
                    if (outcome.Incident is not null)
                    {
                        incidents.Add(outcome.Incident);
                        firstIncident ??= time.GetElapsedTime(runStarted);
                    }
                    if (outcome.AssessmentUnavailable) unassessed++;
                    writer.TryWrite(new CandidateScored(++done, edits.Count, edit, outcome.Incident, outcome.Exclusion)
                    {
                        AssessmentUnavailable = outcome.AssessmentUnavailable,
                    });
                }
            });

        if (edits.Count > 0 && failed == edits.Count)
        {
            writer.TryWrite(new StageFailed(InvestigationStage.ReadEdits, Classify(lastFailure!), lastFailure!.Message));
            return null;
        }
        return new Scored(incidents, failed + unassessed, firstIncident);
    }

    private sealed record Outcome(IncidentRecord? Incident, string? Exclusion, bool AssessmentUnavailable);

    private async Task<Outcome> ScoreAsync(RemovedEdit edit, string scope, CancellationToken ct)
    {
        var reading = await reader.ReadAsync(edit, ct);

        // Exclusions that need no assessment can't be lifted by one, so don't spend an LLM call on them.
        var unassessed = IncidentAssembler.Assemble(reading, null);
        if (unassessed.Incident is null)
            return new Outcome(null, ExclusionId(unassessed.Assessment), false);

        var classification = await classifier.ClassifyAsync(reading, scope, ct);
        if (classification is null)
            return new Outcome(unassessed.Incident, null, AssessmentUnavailable: true);

        var assessed = IncidentAssembler.Assemble(reading, classification);
        return new Outcome(assessed.Incident, ExclusionId(assessed.Assessment), false);
    }

    private static string? ExclusionId(Evidence.TierAssessment a) => a.Exclusion is { } x ? IncidentJson.Id(x) : null;

    private static StageFailureKind Classify(Exception ex) => ex switch
    {
        EmbeddingUnavailableException => StageFailureKind.EmbeddingUnavailable,
        CorpusUnavailableException => StageFailureKind.CorpusUnavailable,
        _ => StageFailureKind.Error,
    };
}
