using AmpersandOfTruth.Tests.Features.Embedding;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Investigations;

public class InvestigatePageTests : BunitContext
{
    private readonly FakeQueryEmbedder _embedder = new();
    private readonly FakeInvestigationCorpus _corpus = FakeInvestigationCorpus.WithWinterWar();

    public InvestigatePageTests()
    {
        Services.AddSingleton<IQueryEmbedder>(_embedder);
        Services.AddSingleton<IInvestigationCorpus>(_corpus);
        Services.AddSingleton<InvestigationService>();
    }

    private IRenderedComponent<InvestigatePage> Search(string topic)
    {
        var cut = Render<InvestigatePage>();
        cut.Find("[data-testid=investigate-topic]").Change(topic);
        cut.Find("form").Submit();
        return cut;
    }

    [Fact]
    public void Shows_empty_state_before_searching()
    {
        var cut = Render<InvestigatePage>();

        cut.Find("[data-testid=empty-state]");
        cut.Find("[data-testid=investigate-submit]");
    }

    [Fact]
    public void Submitting_a_topic_lists_articles_and_revert_rows()
    {
        var cut = Search("Talvisota");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll("[data-testid=investigate-article]").Count);
            var rows = cut.FindAll("[data-testid=revert-row]");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Rollbacker", rows[0].TextContent);
            Assert.Contains("Hylättiin muutokset", rows[0].TextContent);
            Assert.NotNull(rows[0].QuerySelector("a[href='https://fi.wikipedia.org/w/index.php?oldid=2000']"));
        });
        Assert.Contains("only loaded for Finnish", cut.Markup);
        Assert.Contains("No reverts found", cut.Markup);
    }

    [Fact]
    public void Corpus_down_shows_corpus_not_available()
    {
        _corpus.Unavailable = true;

        var cut = Search("Talvisota");

        cut.WaitForAssertion(() =>
            Assert.Contains("Corpus not available", cut.Find("[data-testid=investigate-unavailable]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=revert-row]"));
    }

    [Fact]
    public void Embedder_down_shows_search_not_available()
    {
        _embedder.Unavailable = true;

        var cut = Search("Talvisota");

        cut.WaitForAssertion(() =>
            Assert.Contains("Topic search not available", cut.Find("[data-testid=investigate-unavailable]").TextContent));
    }
}
