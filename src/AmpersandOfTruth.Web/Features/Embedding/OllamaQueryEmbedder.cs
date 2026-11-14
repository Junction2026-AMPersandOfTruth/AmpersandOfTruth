using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Embedding;

/// <summary>
/// Calls <c>POST {url}/api/embed</c> on Ollama, trying <see cref="EmbeddingOptions.OllamaUrl"/> then
/// <see cref="EmbeddingOptions.FallbackOllamaUrl"/>. Uncached — use <see cref="IQueryEmbedder"/> in features;
/// inject this directly only to bypass the cache (e.g. embedding exact article text for verification).
/// Singleton; thread-safe (IHttpClientFactory per call).
/// </summary>
public sealed class OllamaQueryEmbedder(
    IHttpClientFactory http, IOptions<EmbeddingOptions> options, ILogger<OllamaQueryEmbedder> log) : IQueryEmbedder
{
    public const string HttpClientName = "ollama";

    // num_ctx/num_batch as upstream used to build the dataset vectors (default batch of 2,048 tokens is too small).
    private static readonly OllamaRunOptions RunOptions = new(NumCtx: 8192, NumBatch: 8192);

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var o = options.Value;
        var request = new EmbedRequest(o.Model, text, Truncate: true, KeepAlive: o.KeepAlive, RunOptions);
        var failures = new List<string>();
        Exception? last = null;

        foreach (var endpoint in o.Endpoints())
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await http.CreateClient(HttpClientName)
                    .PostAsync(new Uri(endpoint, "api/embed"), Json(request), timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(timeout.Token);
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}", null, response.StatusCode);
                }
                var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(timeout.Token);
                var vector = result?.Embeddings is [{ } first] ? first : null;
                if (vector is not { Length: EmbeddingOptions.Dimensions })
                    throw new InvalidDataException(
                        $"expected 1 vector of {EmbeddingOptions.Dimensions} floats from '{o.Model}', got {vector?.Length.ToString() ?? "none"}");

                log.LogDebug("Embedded {Chars} chars via {Endpoint} in {Ms} ms (load {LoadMs} ms)",
                    text.Length, endpoint, sw.ElapsedMilliseconds, (result!.LoadDuration ?? 0) / 1_000_000);
                return vector;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                var reason = ex is OperationCanceledException ? $"timed out after {o.TimeoutSeconds}s" : ex.Message;
                log.LogWarning("Ollama embed via {Endpoint} failed after {Ms} ms: {Reason}", endpoint, sw.ElapsedMilliseconds, reason);
                failures.Add($"{endpoint}: {reason}");
                last = ex;
            }
        }

        throw new EmbeddingUnavailableException(
            $"No Ollama endpoint could embed with '{o.Model}' (run `ollama pull {o.Model}` locally or scripts/corpus-tunnel.sh). "
            + string.Join(" | ", failures), last);
    }

    /// <summary>First endpoint that has the model pulled, or null. Cheap (<c>/api/show</c>, no inference).</summary>
    public async Task<Uri?> FindServingEndpointAsync(CancellationToken ct = default)
    {
        foreach (var endpoint in options.Value.Endpoints())
        {
            try
            {
                using var response = await http.CreateClient(HttpClientName)
                    .PostAsync(new Uri(endpoint, "api/show"), Json(new { model = options.Value.Model }), ct);
                if (response.IsSuccessStatusCode) return endpoint;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested)) { }
        }
        return null;
    }

    // Buffered (Content-Length) rather than PostAsJsonAsync's chunked stream: works with any server/proxy.
    private static StringContent Json<T>(T body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("truncate")] bool Truncate,
        [property: JsonPropertyName("keep_alive"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KeepAlive,
        [property: JsonPropertyName("options")] OllamaRunOptions Options);

    private sealed record OllamaRunOptions(
        [property: JsonPropertyName("num_ctx")] int NumCtx,
        [property: JsonPropertyName("num_batch")] int NumBatch);

    private sealed record EmbedResponse(
        [property: JsonPropertyName("embeddings")] float[][]? Embeddings,
        [property: JsonPropertyName("load_duration")] long? LoadDuration);
}
