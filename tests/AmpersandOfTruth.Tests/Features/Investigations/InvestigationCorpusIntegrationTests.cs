using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Investigations;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>The real revert/top-k SQL against the VPS corpus. Opt-in: see <see cref="CorpusFactAttribute"/>.</summary>
[Trait("Category", "Corpus")]
public class InvestigationCorpusIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    private const long Talvisota = 1256;

    private IInvestigationCorpus Corpus => app.Services.GetRequiredService<IInvestigationCorpus>();

    [CorpusFact]
    public async Task Talvisota_has_identity_reverts_newest_first()
    {
        var reverts = await Corpus.RevertsAsync([Talvisota], perPage: 20);

        Assert.Equal(20, reverts.Count);
        Assert.All(reverts, r =>
        {
            Assert.Equal(Talvisota, r.PageId);
            Assert.True(r.RevertedCount >= 1);
            Assert.True(r.RestoredRevId < r.RevertedRevId && r.RevertedRevId < r.RevId);
        });
        Assert.Equal(reverts.OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.RevId).Select(r => r.RevId),
                     reverts.Select(r => r.RevId));
    }

    [CorpusFact]
    public async Task Talvisota_vector_finds_itself_first()
    {
        await using var c = await app.Services.GetRequiredService<CorpusConnectionFactory>().OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT embedding FROM article_vector WHERE lang = 'fi' AND page_id = @p", c);
        cmd.Parameters.AddWithValue("@p", Talvisota);
        var vector = CorpusVectors.FromBytes((byte[])(await cmd.ExecuteScalarAsync())!);

        var articles = await Corpus.NearestArticlesAsync(vector, 10);

        Assert.Equal(10, articles.Count);
        Assert.Equal(("fi", Talvisota), (articles[0].Lang, articles[0].PageId));
        Assert.Equal(articles.OrderBy(a => a.Distance).Select(a => a.PageId), articles.Select(a => a.PageId));
    }

    [CorpusFact]
    public async Task Talvisota_reverts_ignore_null_edits_from_vakauttajabot()
    {
        var reverts = await Corpus.RevertsAsync([Talvisota], perPage: 100_000);
        var byRevId = reverts.ToDictionary(r => r.RevId);

        var r1 = byRevId[23400367];
        Assert.Equal(1, r1.RevertedCount);
        Assert.Equal(23400357, r1.RevertedRevId);
        Assert.NotEqual("VakauttajaBot", r1.RevertedUser);

        var r2 = byRevId[22615584];
        Assert.Equal(1, r2.RevertedCount);
        Assert.Equal(22615576, r2.RevertedRevId);
        Assert.NotEqual("VakauttajaBot", r2.RevertedUser);

        Assert.DoesNotContain(reverts, r => r.RevertedUser == "VakauttajaBot");
    }

    // R3-D title lookup (#27): the SQL's collation does the matching, so only the real corpus proves it.
    [CorpusFact]
    public async Task Title_lookup_ignores_case_and_dash_kind_but_not_accents()
    {
        // "Suomi–Finnland" has an en dash in the corpus; "Suomen sisällissota" (1150) must not match without its ä.
        var articles = await Corpus.FiArticlesByTitleAsync(["talvisota", "Suomi-Finnland", "Suomen sisallissota"]);

        Assert.Equal([1256L, 1391324], articles.Select(a => a.PageId).Order());
        Assert.All(articles, a => Assert.Equal(("fi", 0.0), (a.Lang, a.Distance)));
    }

    [CorpusFact]
    public async Task Title_prefix_lookup_returns_the_shortest_titles_first()
    {
        var articles = await Corpus.FiArticlesByTitlePrefixAsync(["talvisodan"], limit: 40);

        Assert.Equal([18349L, 727489], articles.Take(2).Select(a => a.PageId));   // Talvisodan henki, Talvisodan tausta
        Assert.All(articles, a => Assert.StartsWith("Talvisodan", a.Title));
        Assert.Equal(articles.OrderBy(a => a.Title.Length).Select(a => a.PageId), articles.Select(a => a.PageId));
    }

    [CorpusFact]
    public async Task Title_prefix_lookup_treats_like_wildcards_literally()
    {
        // Unescaped, "_" would match the "n" of "Talvisodan …".
        Assert.Empty(await Corpus.FiArticlesByTitlePrefixAsync(["Talvisoda_"], limit: 40));
    }
}
