using AmpersandOfTruth.Web.Features.Embedding;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// P1.S10 (#37) mode B+rel: how close an incident's claim is to the topic searched for, as
/// max(0, cosine(bge-m3(topic), bge-m3(claim))). It only reorders incidents within a tier (<see cref="IncidentAssembler.Rank"/>).
/// Both texts go through the cached <see cref="IQueryEmbedder"/>, so a topic is embedded once (stage 1 already did) and a
/// re-run costs nothing. Thread-safe.
/// </summary>
public sealed class TopicRelevance(IQueryEmbedder embedder, ILogger<TopicRelevance> log)
{
    /// <summary>Relevance in [0, 1]; 1 when there is no text to compare (as the research variant). Null when the embedder
    /// is unavailable: the caller ranks with 1 and must not cache the run.</summary>
    public async Task<double?> OfAsync(string topic, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(text)) return 1.0;
        try
        {
            var q = await embedder.EmbedAsync(topic.Trim(), ct);
            var c = await embedder.EmbedAsync(text.Trim(), ct);
            return Math.Max(0, Cosine(q, c));
        }
        catch (EmbeddingUnavailableException ex)
        {
            log.LogWarning("Topic relevance unavailable ({Error}); ranking within the tier without it", ex.Message);
            return null;
        }
    }

    internal static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
