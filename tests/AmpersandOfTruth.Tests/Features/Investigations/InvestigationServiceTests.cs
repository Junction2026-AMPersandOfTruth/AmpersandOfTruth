using AmpersandOfTruth.Tests.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Investigations;

public class InvestigationServiceTests
{
    private readonly FakeQueryEmbedder _embedder = new();
    private readonly FakeInvestigationCorpus _corpus = FakeInvestigationCorpus.WithWinterWar();

    private InvestigationService Service => new(_embedder, _corpus);

    [Fact]
    public async Task Returns_articles_closest_first_with_reverts_only_for_fi()
    {
        var result = await Service.InvestigateAsync("  Talvisota  ");

        Assert.Equal(InvestigationStatus.Ok, result.Status);
        Assert.Equal("Talvisota", result.Topic);
        Assert.Equal(["Talvisota", "Winter War", "Jatkosota"], result.Articles.Select(a => a.Article.Title));
        Assert.Equal([2000L, 1000L], result.Articles[0].Reverts.Select(r => r.RevId));
        Assert.Empty(result.Articles[1].Reverts);
        Assert.Empty(result.Articles[2].Reverts);
        Assert.Equal([1256L, 5263L], Assert.Single(_corpus.RevertQueries));
    }

    [Fact]
    public async Task Embedder_down_is_reported_not_thrown()
    {
        _embedder.Unavailable = true;

        var result = await Service.InvestigateAsync("Talvisota");

        Assert.Equal(InvestigationStatus.EmbeddingUnavailable, result.Status);
        Assert.Empty(result.Articles);
        Assert.Contains("down", result.Error);
    }

    [Fact]
    public async Task Corpus_down_is_reported_not_thrown()
    {
        _corpus.Unavailable = true;

        var result = await Service.InvestigateAsync("Talvisota");

        Assert.Equal(InvestigationStatus.CorpusUnavailable, result.Status);
        Assert.Empty(result.Articles);
    }

    [Fact]
    public void Links_point_at_fi_wikipedia_revisions()
    {
        var r = FakeInvestigationCorpus.Revert(1256, 2000);

        Assert.Equal("https://fi.wikipedia.org/w/index.php?diff=prev&oldid=2000", r.DiffUrl.AbsoluteUri);
        Assert.Equal("https://fi.wikipedia.org/w/index.php?diff=prev&oldid=1999", r.RevertedDiffUrl.AbsoluteUri);
        Assert.Equal("https://fi.wikipedia.org/?curid=1256", new TopicArticle("fi", 1256, "Talvisota", 0).Url);
    }
}
