using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AmpersandOfTruth.Web.Features.Embedding;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// P1.P1 (#39): topic → articles → removed edits → (read → classify → assemble) per edit with bounded parallelism,
/// streamed as <see cref="InvestigationEvent"/>s. Stateless singleton; every run is independent.
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
    public async IAsyncEnumerable<InvestigationEvent> RunAsync(string topic, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var channel = Channel.CreateUnbounded<InvestigationEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = Task.Run(() => ProduceAsync(topic.Trim(), channel.Writer, run.Token), CancellationToken.None);
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

    private async Task ProduceAsync(string topic, ChannelWriter<InvestigationEvent> writer, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var stage = InvestigationStage.FindArticles;
        try
        {
            var articles = await search.FindArticlesAsync(topic, ct);
            writer.TryWrite(new ArticlesFound(articles.Count, articles));

            stage = InvestigationStage.ScanHistory;
            var scan = await candidates.ScanAsync(articles, ct);
            writer.TryWrite(new RevisionsScanned(scan.RevisionsScanned, scan.Candidates));

            stage = InvestigationStage.ReadEdits;
            var incidents = await ScoreAllAsync(scan.Candidates, $"investigation:{Guid.NewGuid():N}", writer, ct);
            if (incidents is null) return; // StageFailed already written

            stage = InvestigationStage.AssembleIncidents;
            var ranked = incidents.OrderByDescending(i => i.Score).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
            writer.TryWrite(new Completed(FromCache: false, ranked, time.GetElapsedTime(started)));
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
    }

    /// <returns>The incidents, or null when every candidate failed (a <see cref="StageFailed"/> has been written).</returns>
    private async Task<List<IncidentRecord>?> ScoreAllAsync(
        IReadOnlyList<RemovedEdit> edits, string scope, ChannelWriter<InvestigationEvent> writer, CancellationToken ct)
    {
        var incidents = new List<IncidentRecord>();
        var gate = new Lock();
        int done = 0, failed = 0;
        Exception? lastFailure = null;

        await Parallel.ForEachAsync(edits,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.Parallelism), CancellationToken = ct },
            async (edit, token) =>
            {
                IncidentRecord? incident = null;
                string? exclusion;
                try
                {
                    (incident, exclusion) = await ScoreAsync(edit, scope, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    log.LogWarning(ex, "Could not read removed edit {Edit}", edit.Key);
                    exclusion = "unreadable";
                    lock (gate) { failed++; lastFailure = ex; }
                }

                lock (gate) // index and write together so CandidateScored arrives in index order
                {
                    if (incident is not null) incidents.Add(incident);
                    writer.TryWrite(new CandidateScored(++done, edits.Count, edit, incident, exclusion));
                }
            });

        if (edits.Count > 0 && failed == edits.Count)
        {
            writer.TryWrite(new StageFailed(InvestigationStage.ReadEdits, Classify(lastFailure!), lastFailure!.Message));
            return null;
        }
        return incidents;
    }

    private async Task<(IncidentRecord? Incident, string? Exclusion)> ScoreAsync(RemovedEdit edit, string scope, CancellationToken ct)
    {
        var reading = await reader.ReadAsync(edit, ct);

        // Exclusions that need no assessment can't be lifted by one, so don't spend an LLM call on them.
        var unassessed = IncidentAssembler.Assemble(reading, null);
        if (unassessed.Incident is null)
            return (null, ExclusionId(unassessed.Assessment));

        var classification = await classifier.ClassifyAsync(reading, scope, ct);
        if (classification is null)
            return (unassessed.Incident, null);

        var assessed = IncidentAssembler.Assemble(reading, classification);
        return (assessed.Incident, ExclusionId(assessed.Assessment));
    }

    private static string? ExclusionId(Evidence.TierAssessment a) => a.Exclusion is { } x ? IncidentJson.Id(x) : null;

    private static StageFailureKind Classify(Exception ex) => ex switch
    {
        EmbeddingUnavailableException => StageFailureKind.EmbeddingUnavailable,
        CorpusUnavailableException => StageFailureKind.CorpusUnavailable,
        _ => StageFailureKind.Error,
    };
}
