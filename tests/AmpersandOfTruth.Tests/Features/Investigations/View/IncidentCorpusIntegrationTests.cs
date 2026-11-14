using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

[Trait("Category", "Corpus")]
public class IncidentCorpusIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    private const long SuomenSisallissota = 1150;
    private const long Talvisota = 1256;

    private IIncidentCorpus Corpus => app.Services.GetRequiredService<IIncidentCorpus>();

    [CorpusFact]
    public async Task ArticlesAsync_returns_Suomen_sisallissota_with_English_title_for_page_1150()
    {
        var articles = await Corpus.ArticlesAsync([SuomenSisallissota]);

        Assert.True(articles.TryGetValue(SuomenSisallissota, out var article));
        Assert.Equal(SuomenSisallissota, article.PageId);
        Assert.Equal("Suomen sisällissota", article.TitleFi);
        Assert.False(string.IsNullOrWhiteSpace(article.TitleEn));
    }

    [CorpusFact]
    public async Task RevisionsAsync_fetches_revision_metadata_with_parent_id()
    {
        var rows = await Corpus.RevisionsAsync(Talvisota, [23400357, 23400367]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(Talvisota, r.PageId);
            Assert.NotNull(r.ParentRevId);
        });
    }
}
