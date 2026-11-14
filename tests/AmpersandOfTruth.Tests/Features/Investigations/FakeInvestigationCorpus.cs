using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>In-memory stand-in for the MariaDB corpus. <see cref="Unavailable"/> simulates the tunnel being down.</summary>
public sealed class FakeInvestigationCorpus : IInvestigationCorpus
{
    public List<TopicArticle> Articles { get; } = [];
    public List<RevertEvent> Reverts { get; } = [];
    public bool Unavailable { get; set; }
    public List<IReadOnlyCollection<long>> RevertQueries { get; } = [];

    public Task<IReadOnlyList<TopicArticle>> NearestArticlesAsync(float[] vector, int limit, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<TopicArticle>>(Articles.OrderBy(a => a.Distance).Take(limit).ToList());
    }

    public Task<IReadOnlyList<RevertEvent>> RevertsAsync(IReadOnlyCollection<long> pageIds, int perPage, CancellationToken ct = default)
    {
        ThrowIfDown();
        RevertQueries.Add(pageIds);
        return Task.FromResult<IReadOnlyList<RevertEvent>>(Reverts
            .Where(r => pageIds.Contains(r.PageId))
            .GroupBy(r => r.PageId)
            .SelectMany(g => g.OrderByDescending(r => r.Timestamp).Take(perPage))
            .ToList());
    }

    private void ThrowIfDown()
    {
        if (Unavailable) throw new CorpusUnavailableException("fake corpus is down");
    }

    public static RevertEvent Revert(long pageId, long revId, string user = "Reverter", string comment = "Hylättiin muutokset") =>
        new(pageId, revId, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(revId), user, comment,
            RestoredRevId: revId - 2, RevertedCount: 1, RevertedRevId: revId - 1, RevertedUser: "192.0.2.1");

    /// <summary>Talvisota (fi, with reverts), Jatkosota (fi, none), Winter War (en, no history).</summary>
    public static FakeInvestigationCorpus WithWinterWar()
    {
        var corpus = new FakeInvestigationCorpus();
        corpus.Articles.AddRange([
            new TopicArticle("fi", 1256, "Talvisota", 0.10),
            new TopicArticle("en", 33526, "Winter War", 0.15),
            new TopicArticle("fi", 5263, "Jatkosota", 0.20),
        ]);
        corpus.Reverts.AddRange([Revert(1256, 1000), Revert(1256, 2000, user: "Rollbacker")]);
        return corpus;
    }
}
