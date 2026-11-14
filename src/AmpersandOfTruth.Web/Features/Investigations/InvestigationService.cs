using AmpersandOfTruth.Web.Features.Embedding;

namespace AmpersandOfTruth.Web.Features.Investigations;

/// <summary>
/// Walking skeleton (issue #19): topic → bge-m3 vector → nearest articles → identity reverts on the fi ones.
/// No diffing, no LLM. Never throws for a missing corpus/Ollama — returns an "unavailable" result instead.
/// Singleton, stateless.
/// </summary>
public sealed class InvestigationService(IQueryEmbedder embedder, IInvestigationCorpus corpus)
{
    public const int ArticleLimit = 10;
    public const int RevertsPerArticle = 50;

    public async Task<InvestigationResult> InvestigateAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        topic = topic.Trim();

        float[] vector;
        try
        {
            vector = await embedder.EmbedAsync(topic, ct);
        }
        catch (EmbeddingUnavailableException ex)
        {
            return InvestigationResult.Unavailable(InvestigationStatus.EmbeddingUnavailable, topic, ex.Message);
        }

        try
        {
            var articles = await corpus.NearestArticlesAsync(vector, ArticleLimit, ct);
            var fiPages = articles.Where(a => a.HasHistory).Select(a => a.PageId).Distinct().ToList();
            var reverts = (await corpus.RevertsAsync(fiPages, RevertsPerArticle, ct)).ToLookup(r => r.PageId);

            return new InvestigationResult(InvestigationStatus.Ok, topic,
                articles.Select(a => new ArticleReverts(a, a.HasHistory ? [.. reverts[a.PageId]] : [])).ToList());
        }
        catch (CorpusUnavailableException ex)
        {
            return InvestigationResult.Unavailable(InvestigationStatus.CorpusUnavailable, topic, ex.Message);
        }
    }
}
