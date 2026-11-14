using System.Runtime.CompilerServices;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

public sealed class HybridFixtureOptions
{
    public const string Section = "Investigations:Fixtures";

    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(350);
}

/// <summary>
/// P2.0 fixture runner (#98 §1): runs stages 1–2 (<see cref="ITopicSearch"/>, <see cref="ICandidateSource"/>) against the
/// real corpus and replays <see cref="SampleIncidents"/> for stages 3–4 through the same <see cref="InvestigationEvent"/> stream.
/// </summary>
public sealed class HybridFixtureRunner(
    ITopicSearch search,
    ICandidateSource candidates,
    SampleIncidents samples,
    IOptions<HybridFixtureOptions> options,
    TimeProvider time,
    ILogger<HybridFixtureRunner> log) : IInvestigationRunner
{
    public async IAsyncEnumerable<InvestigationEvent> RunAsync(
        string topic, bool refresh = false, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        var trimmed = topic.Trim();
        var started = time.GetTimestamp();

        IReadOnlyList<TopicArticle>? articles = null;
        StageFailed? stageOneFailure = null;
        try
        {
            articles = await search.FindArticlesAsync(trimmed, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stageOneFailure = Fail(InvestigationStage.FindArticles, trimmed, ex);
        }

        if (stageOneFailure is not null)
        {
            yield return stageOneFailure;
            yield break;
        }

        yield return new ArticlesFound(articles!.Count, articles);

        CandidateScan? scan = null;
        StageFailed? stageTwoFailure = null;
        try
        {
            scan = await candidates.ScanAsync(articles, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stageTwoFailure = Fail(InvestigationStage.ScanHistory, trimmed, ex);
        }

        if (stageTwoFailure is not null)
        {
            yield return stageTwoFailure;
            yield break;
        }

        yield return new RevisionsScanned(scan!.RevisionsScanned, scan.Candidates);

        var sample = samples.Match(trimmed);
        var ranked = sample is null
            ? []
            : sample.Incidents.OrderByDescending(i => i.Score).ToList();

        var delay = options.Value.Delay;
        for (var i = 0; i < ranked.Count; i++)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, time, ct);
            }
            else
            {
                ct.ThrowIfCancellationRequested();
            }

            var incident = ranked[i];
            var candidate = FindOrSynthesiseCandidate(incident, articles, scan.Candidates);
            yield return new CandidateScored(i + 1, ranked.Count, candidate, incident);
        }

        ct.ThrowIfCancellationRequested();
        yield return new Completed(FromCache: false, ranked, time.GetElapsedTime(started));
    }

    private static RemovedEdit FindOrSynthesiseCandidate(
        IncidentRecord incident,
        IReadOnlyList<TopicArticle> articles,
        IReadOnlyList<RemovedEdit> candidates)
    {
        var match = candidates.FirstOrDefault(c =>
            c.Article.PageId == incident.PageId && incident.InsertingRevIds.Contains(c.Revert.RevertedRevId));
        if (match is not null)
        {
            return match;
        }

        var title = articles.FirstOrDefault(a => a.Lang == "fi" && a.PageId == incident.PageId)?.Title
            ?? incident.Articles.FirstOrDefault(a => a.PageId == incident.PageId)?.TitleFi
            ?? $"Page {incident.PageId}";

        var article = new TopicArticle("fi", incident.PageId, title, 1.0);
        var removingRevId = incident.RemovingRevIds.Count > 0 ? incident.RemovingRevIds[0] : 0L;
        var insertingRevId = incident.InsertingRevIds.Count > 0 ? incident.InsertingRevIds[0] : 0L;

        var remEdit = incident.Edits.FirstOrDefault(e => e.RevId == removingRevId)
            ?? incident.Edits.FirstOrDefault(e => e.Role == IncidentEditRole.Removed);
        var insEdit = incident.Edits.FirstOrDefault(e => e.RevId == insertingRevId)
            ?? incident.Edits.FirstOrDefault(e => e.Role == IncidentEditRole.Inserted);

        var ts = (remEdit?.Timestamp ?? incident.LastRemovedAt ?? incident.FirstInsertedAt ?? DateTimeOffset.UnixEpoch).UtcDateTime;
        var restoredRevId = insEdit?.ParentRevId ?? Math.Max(0L, insertingRevId - 1);

        var revert = new RevertEvent(
            PageId: incident.PageId,
            RevId: removingRevId,
            Timestamp: DateTime.SpecifyKind(ts, DateTimeKind.Utc),
            User: remEdit?.UserDisplay,
            Comment: remEdit?.Summary,
            RestoredRevId: restoredRevId,
            RevertedCount: Math.Max(1, incident.InsertingRevIds.Count),
            RevertedRevId: insertingRevId,
            RevertedUser: insEdit?.UserDisplay);

        return new RemovedEdit(article, revert);
    }

    private StageFailed Fail(InvestigationStage stage, string topic, Exception ex)
    {
        var kind = Classify(ex);
        if (kind == StageFailureKind.Error)
        {
            log.LogError(ex, "Investigation of {Topic} failed in {Stage}", topic, stage);
        }

        return new StageFailed(stage, kind, ex.Message);
    }

    private static StageFailureKind Classify(Exception ex) => ex switch
    {
        EmbeddingUnavailableException => StageFailureKind.EmbeddingUnavailable,
        CorpusUnavailableException => StageFailureKind.CorpusUnavailable,
        _ => StageFailureKind.Error,
    };
}
