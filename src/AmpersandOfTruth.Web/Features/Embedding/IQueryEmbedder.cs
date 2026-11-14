namespace AmpersandOfTruth.Web.Features.Embedding;

/// <summary>
/// Turns a topic/query into a <c>BAAI/bge-m3</c> vector (1024 floats) comparable with the corpus's stored
/// article vectors. Only bge-m3 works here: vectors from any other model (e.g. Gemini's) are not comparable.
/// Inject <see cref="IQueryEmbedder"/> (cached); throws <see cref="EmbeddingUnavailableException"/> when no
/// Ollama endpoint can serve the model — show a "search unavailable" state rather than crashing.
/// </summary>
public interface IQueryEmbedder
{
    /// <summary>Returns a fresh array of <see cref="EmbeddingOptions.Dimensions"/> floats (safe to mutate).</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

public sealed class EmbeddingUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
