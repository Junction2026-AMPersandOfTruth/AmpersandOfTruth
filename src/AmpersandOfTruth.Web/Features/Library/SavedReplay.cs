using System.Runtime.CompilerServices;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Web.Features.Library;

public sealed class SavedReplay(LibraryStore store)
{
    public async IAsyncEnumerable<InvestigationEvent> EventsAsync(
        Guid id,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var found = await store.GetAsync(id, ct);
        if (found is not { } loaded)
        {
            yield return new StageFailed(
                InvestigationStage.FindArticles,
                StageFailureKind.Error,
                "Saved investigation not found");
            yield break;
        }

        var snapshot = loaded.Snapshot;
        var articles = snapshot.Articles ?? [];
        var incidents = snapshot.Incidents ?? [];

        var articleByPageId = new Dictionary<long, TopicArticle>();
        foreach (var article in articles)
        {
            articleByPageId.TryAdd(article.PageId, article);
        }

        yield return new ArticlesFound(articles.Count, articles);

        ct.ThrowIfCancellationRequested();
        yield return new RevisionsScanned(0, []);

        for (var i = 0; i < incidents.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var incident = incidents[i];
            var candidate = BuildMinimalCandidate(incident, articleByPageId, snapshot.AnalysedAt);
            yield return new CandidateScored(i + 1, incidents.Count, candidate, incident);
        }

        ct.ThrowIfCancellationRequested();
        yield return new Completed(FromCache: true, incidents, TimeSpan.Zero)
        {
            AnalysedAt = snapshot.AnalysedAt,
        };
    }

    private static RemovedEdit BuildMinimalCandidate(
        IncidentRecord incident,
        IReadOnlyDictionary<long, TopicArticle> articleByPageId,
        DateTimeOffset fallbackTime)
    {
        if (!articleByPageId.TryGetValue(incident.PageId, out var article))
        {
            var title = incident.Articles.FirstOrDefault(a => a.PageId == incident.PageId)?.TitleFi
                ?? $"Page {incident.PageId}";
            article = new TopicArticle("fi", incident.PageId, title, Distance: 0);
        }

        var removeRevId = incident.RemovingRevIds.Count > 0 ? incident.RemovingRevIds[0] : 0L;
        var insertRevId = incident.InsertingRevIds.Count > 0 ? incident.InsertingRevIds[^1] : 0L;
        var timestamp = (incident.LastRemovedAt ?? incident.FirstInsertedAt ?? fallbackTime).UtcDateTime;

        var revert = new RevertEvent(
            PageId: incident.PageId,
            RevId: removeRevId,
            Timestamp: timestamp,
            User: null,
            Comment: null,
            RestoredRevId: 0L,
            RevertedCount: Math.Max(1, incident.InsertingRevIds.Count),
            RevertedRevId: insertRevId,
            RevertedUser: null);

        return new RemovedEdit(article, revert);
    }
}
