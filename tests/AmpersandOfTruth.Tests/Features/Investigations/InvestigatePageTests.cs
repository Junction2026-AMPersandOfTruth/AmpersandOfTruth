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
            Assert.NotNull(rows[0].QuerySelector("[data-testid=revert-when] a[href='https://fi.wikipedia.org/w/index.php?diff=prev&oldid=2000']"));
            Assert.NotNull(rows[0].QuerySelector("[data-testid=revert-undid] a[href='https://fi.wikipedia.org/w/index.php?diff=prev&oldid=1999']"));
        });
        Assert.Contains("only loaded for Finnish", cut.Markup);
        Assert.Contains("No reverts found", cut.Markup);
    }

    [Fact]
    public void Revert_with_ip_addresses_is_masked_and_cells_have_no_links()
    {
        _corpus.Reverts.Clear();
        _corpus.Reverts.Add(FakeInvestigationCorpus.Revert(
            pageId: 1256,
            revId: 3000,
            user: "85.76.123.45",
            comment: "Kumottu muokkaus [[Special:Diff/23181977|23181977]], jonka teki [[Special:Contributions/2001:14BB:51:D813:A1B9:8F12:23B6:8D13|2001:14BB:51:D813:A1B9:8F12:23B6:8D13]] ([[User talk:2001:14BB:51:D813:A1B9:8F12:23B6:8D13|keskustelu]])",
            revertedUser: "2001:14BB:51:D813:A1B9:8F12:23B6:8D13"));

        var cut = Search("Talvisota");

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid=revert-row]"));

            // Neither full address appears anywhere in the markup
            Assert.DoesNotContain("85.76.123.45", cut.Markup);
            Assert.DoesNotContain("2001:14BB:51:D813:A1B9:8F12:23B6:8D13", cut.Markup);

            // No Special:Contributions/ with an IP
            Assert.DoesNotContain("Special:Contributions/85.76", cut.Markup);
            Assert.DoesNotContain("Special:Contributions/2001:", cut.Markup);

            // Contains masked labels
            Assert.Contains("85.76.x.x (anonymous)", cut.Markup);
            Assert.Contains("2001:14BB:… (anonymous)", cut.Markup);

            // The user and reverted-user cells contain no <a> tags
            var userCell = row.QuerySelector("[data-testid=revert-user]");
            Assert.NotNull(userCell);
            Assert.Null(userCell.QuerySelector("a"));
            Assert.Equal("85.76.x.x (anonymous)", userCell.TextContent.Trim());

            var revertedUserCell = row.QuerySelector("[data-testid=revert-reverted-user]");
            Assert.NotNull(revertedUserCell);
            Assert.Null(revertedUserCell.QuerySelector("a"));
            Assert.Equal("2001:14BB:… (anonymous)", revertedUserCell.TextContent.Trim());

            // Comment is masked as plain text
            var commentCell = row.QuerySelector("[data-testid=revert-comment]");
            Assert.NotNull(commentCell);
            Assert.Equal("Kumottu muokkaus 23181977, jonka teki 2001:14BB:… (keskustelu)", commentCell.TextContent.Trim());
        });
    }

    [Fact]
    public void Named_user_links_to_contributions_and_null_user_shows_hidden_editor_unlinked()
    {
        _corpus.Reverts.Clear();
        _corpus.Reverts.Add(FakeInvestigationCorpus.Revert(
            pageId: 1256,
            revId: 3000,
            user: "Matti Meikäläinen",
            comment: "Asiallinen muutos",
            revertedUser: null));

        var cut = Search("Talvisota");

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid=revert-row]"));

            var userCell = row.QuerySelector("[data-testid=revert-user]");
            Assert.NotNull(userCell);
            var link = userCell.QuerySelector("a");
            Assert.NotNull(link);
            Assert.Equal("https://fi.wikipedia.org/wiki/Special:Contributions/Matti_Meik%C3%A4l%C3%A4inen", link.GetAttribute("href"));
            Assert.Equal("Matti Meikäläinen", link.TextContent.Trim());

            var revertedUserCell = row.QuerySelector("[data-testid=revert-reverted-user]");
            Assert.NotNull(revertedUserCell);
            Assert.Null(revertedUserCell.QuerySelector("a"));
            Assert.Equal("Hidden editor", revertedUserCell.TextContent.Trim());
        });
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
