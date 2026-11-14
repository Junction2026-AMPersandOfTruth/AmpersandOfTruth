using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.Embedding;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Embedding;

public class OllamaQueryEmbedderTests
{
    private const string Primary = "http://primary.test:11434";
    private const string Fallback = "http://fallback.test:11435";

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, JsonNode Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!, JsonNode.Parse(body)!));
            return respond(request, body);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static OllamaQueryEmbedder Create(StubHandler handler, string? fallback = Fallback) =>
        new(new StubFactory(handler), Options.Create(new EmbeddingOptions { OllamaUrl = Primary, FallbackOllamaUrl = fallback }),
            NullLogger<OllamaQueryEmbedder>.Instance);

    private static HttpResponseMessage Embeddings(int dims) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { model = "bge-m3", embeddings = new[] { Enumerable.Range(0, dims).Select(i => i / 1000f) } }),
    };

    [Fact]
    public async Task Posts_bge_m3_with_the_dataset_context_options_and_returns_1024_floats()
    {
        var handler = new StubHandler((_, _) => Embeddings(1024));

        var v = await Create(handler).EmbedAsync("Talvisodan syyt");

        Assert.Equal(1024, v.Length);
        Assert.Equal(0.5f, v[500]);
        var (uri, body) = Assert.Single(handler.Requests);
        Assert.Equal($"{Primary}/api/embed", uri.ToString());
        Assert.Equal("bge-m3", (string?)body["model"]);
        Assert.Equal("Talvisodan syyt", (string?)body["input"]);
        Assert.Equal(8192, (int?)body["options"]!["num_ctx"]);
        Assert.Equal(8192, (int?)body["options"]!["num_batch"]);
        Assert.Null(body["keep_alive"]); // server default: the VPS keeps the model loaded forever
    }

    [Fact]
    public async Task Falls_back_when_the_primary_is_down_or_lacks_the_model()
    {
        var handler = new StubHandler((req, _) => req.RequestUri!.Host == "primary.test"
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"model \"bge-m3\" not found"}""") }
            : Embeddings(1024));

        var v = await Create(handler).EmbedAsync("topic");

        Assert.Equal(1024, v.Length);
        Assert.Equal(["primary.test", "fallback.test"], handler.Requests.Select(r => r.Uri.Host));
    }

    [Fact]
    public async Task Connection_refused_on_both_endpoints_is_EmbeddingUnavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => Create(handler).EmbedAsync("topic"));

        Assert.Contains("primary.test", ex.Message);
        Assert.Contains("fallback.test", ex.Message);
    }

    [Fact]
    public async Task Wrong_dimensions_are_rejected_not_returned()
    {
        // e.g. someone pulled a different embedding model under the configured name
        var handler = new StubHandler((_, _) => Embeddings(768));

        var ex = await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => Create(handler, fallback: null).EmbedAsync("topic"));

        Assert.Contains("768", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_falling_back()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).EmbedAsync("topic", cts.Token));
    }

    [Fact]
    public async Task Health_check_is_degraded_and_fast_when_no_Ollama_is_reachable()
    {
        // Port 1 on loopback: nothing listens → connection refused.
        var options = Options.Create(new EmbeddingOptions { OllamaUrl = "http://127.0.0.1:1", FallbackOllamaUrl = null });
        var ollama = new OllamaQueryEmbedder(new StubFactory(new SocketsHttpHandler()), options, NullLogger<OllamaQueryEmbedder>.Instance);
        var check = new EmbeddingHealthCheck(ollama, options);
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration("embedding", check, HealthStatus.Degraded, null) };

        var sw = Stopwatch.StartNew();
        var result = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }
}
