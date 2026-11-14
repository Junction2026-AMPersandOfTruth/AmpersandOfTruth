namespace AmpersandOfTruth.Web.Features.Embedding;

/// <summary>Bound from the <c>Embedding</c> config section. No secrets here.</summary>
public sealed class EmbeddingOptions
{
    public const int Dimensions = 1024;

    /// <summary>Tried first: a local Ollama on a laptop, or the VPS's own Ollama when the app runs on the VPS.</summary>
    public string OllamaUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Tried when <see cref="OllamaUrl"/> is down or lacks the model: the VPS Ollama through
    /// <c>scripts/corpus-tunnel.sh</c> (local 11435 → VPS 11434). Empty = no fallback.</summary>
    public string? FallbackOllamaUrl { get; set; } = "http://127.0.0.1:11435";

    /// <summary>Must be the model that produced the dataset vectors.</summary>
    public string Model { get; set; } = "bge-m3";

    /// <summary>Ollama <c>keep_alive</c> per request (e.g. "24h"). Null = the server's default: the VPS Ollama runs with
    /// <c>OLLAMA_KEEP_ALIVE=-1</c> (model never unloads); a laptop Ollama unloads after 5 idle minutes.</summary>
    public string? KeepAlive { get; set; }

    /// <summary>Per endpoint. A cold model load on the 2-vCPU VPS takes several seconds; warm queries are fast.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    public IEnumerable<Uri> Endpoints()
    {
        foreach (var url in new[] { OllamaUrl, FallbackOllamaUrl })
            if (!string.IsNullOrWhiteSpace(url))
                yield return new Uri(url.TrimEnd('/') + "/");
    }
}
