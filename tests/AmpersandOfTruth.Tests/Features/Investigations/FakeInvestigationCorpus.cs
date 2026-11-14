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

    /// <summary>fi articles that exist in the corpus but aren't nearest-neighbour hits (fold targets, signal-variant lookups).</summary>
    public List<TopicArticle> OtherFiArticles { get; } = [];

    private IEnumerable<TopicArticle> FiCatalog => Articles.Where(a => a.Lang == "fi").Concat(OtherFiArticles);

    public Task<IReadOnlyList<TopicArticle>> FiArticlesAsync(IReadOnlyCollection<long> pageIds, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<TopicArticle>>(FiCatalog.Where(a => pageIds.Contains(a.PageId))
            .Select(a => a with { Distance = 0 }).ToList());
    }

    public Task<IReadOnlyDictionary<string, TopicArticle>> FiSiblingsAsync(IReadOnlyCollection<string> wikidataIds, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyDictionary<string, TopicArticle>>(FiCatalog
            .Where(a => a.WikidataId is not null && wikidataIds.Contains(a.WikidataId))
            .DistinctBy(a => a.WikidataId)
            .ToDictionary(a => a.WikidataId!, a => a with { Distance = 0 }));
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

    public static RevertEvent Revert(
        long pageId,
        long revId,
        string? user = "Reverter",
        string? comment = "Hylättiin muutokset",
        string? revertedUser = "192.0.2.1") =>
        new(pageId, revId, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(revId), user, comment,
            RestoredRevId: revId - 2, RevertedCount: 1, RevertedRevId: revId - 1, RevertedUser: revertedUser);

    /// <summary>
    /// Nearest hits: Talvisota (fi, with reverts), Winter War (en sibling of Talvisota — folds away), Jatkosota (fi, none),
    /// Lapland War (en, whose fi sibling Lapin sota is no hit itself — folds onto it), Soviet propaganda (en, no fi sibling — dropped).
    /// </summary>
    public static FakeInvestigationCorpus WithWinterWar()
    {
        var corpus = new FakeInvestigationCorpus();
        corpus.Articles.AddRange([
            new TopicArticle("fi", 1256, "Talvisota", 0.10, "Q191721"),
            new TopicArticle("en", 33526, "Winter War", 0.15, "Q191721"),
            new TopicArticle("fi", 5263, "Jatkosota", 0.20, "Q649022"),
            new TopicArticle("en", 4401, "Lapland War", 0.25, "Q207818"),
            new TopicArticle("en", 9999, "Soviet propaganda", 0.30, "Q1"),
        ]);
        corpus.OtherFiArticles.Add(new TopicArticle("fi", 9012, "Lapin sota", 0, "Q207818"));
        corpus.Reverts.AddRange([Revert(1256, 1000), Revert(1256, 2000, user: "Rollbacker")]);
        return corpus;
    }
}
