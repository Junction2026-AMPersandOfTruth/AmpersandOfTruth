namespace AmpersandOfTruth.Tests.Features.Embedding;

/// <summary>
/// Integration test against a real Ollama with <c>bge-m3</c>. Skipped unless <c>EMBEDDING_TESTS=1</c>, so
/// <c>verify.sh</c> stays green without Ollama. Local: <c>ollama pull bge-m3</c>; VPS: <c>scripts/corpus-tunnel.sh</c>.
/// Set <c>CORPUS_TESTS=1</c> too for the check against stored corpus vectors (<see cref="CorpusEmbeddingFactAttribute"/>).
/// </summary>
public class EmbeddingFactAttribute : FactAttribute
{
    public EmbeddingFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EMBEDDING_TESTS") != "1")
            Skip = "Embedding integration test — set EMBEDDING_TESTS=1 (and run Ollama with bge-m3 or scripts/corpus-tunnel.sh).";
    }
}

/// <summary>Needs both Ollama (<c>EMBEDDING_TESTS=1</c>) and the corpus (<c>CORPUS_TESTS=1</c>, tunnel + password).</summary>
public sealed class CorpusEmbeddingFactAttribute : EmbeddingFactAttribute
{
    public CorpusEmbeddingFactAttribute()
    {
        if (Skip is null && Environment.GetEnvironmentVariable("CORPUS_TESTS") != "1")
            Skip = "Needs the corpus too — set CORPUS_TESTS=1 (and run scripts/corpus-tunnel.sh).";
    }
}
