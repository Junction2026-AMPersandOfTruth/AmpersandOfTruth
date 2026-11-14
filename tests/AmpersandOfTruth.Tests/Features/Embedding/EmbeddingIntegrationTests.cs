using System.Diagnostics;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Embedding;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit.Abstractions;

namespace AmpersandOfTruth.Tests.Features.Embedding;

/// <summary>
/// Against real Ollama (and the corpus), wired exactly as the app wires it. Opt-in, see <see cref="EmbeddingFactAttribute"/>:
/// <c>scripts/corpus-tunnel.sh &amp;&amp; EMBEDDING_TESTS=1 CORPUS_TESTS=1 dotnet test --filter Category=Embedding --logger "console;verbosity=detailed"</c>
/// (detailed logger prints the measured latency and similarities).
/// </summary>
[Trait("Category", "Embedding")]
public class EmbeddingIntegrationTests(AppFactory app, ITestOutputHelper output) : IClassFixture<AppFactory>
{
    // Uncached: these tests are about what Ollama returns, not what's in the SQLite cache.
    private OllamaQueryEmbedder Ollama => app.Services.GetRequiredService<OllamaQueryEmbedder>();

    [EmbeddingFact]
    public async Task Short_query_returns_a_1024_float_unit_vector_and_reports_latency()
    {
        await Ollama.EmbedAsync("warm-up"); // first call may load the model; keep_alive keeps it loaded after

        var timings = new List<double>();
        float[] v = [];
        foreach (var topic in new[] { "Talvisodan syyt", "Suomen sisällissota 1918", "Karjalan kysymys ja Neuvostoliitto",
                                      "Finland Russia relations", "language politics in Finland" })
        {
            var sw = Stopwatch.StartNew();
            v = await Ollama.EmbedAsync(topic);
            timings.Add(sw.Elapsed.TotalMilliseconds);
        }

        Assert.Equal(1024, v.Length);
        Assert.InRange(MathF.Sqrt(v.Sum(x => x * x)), 0.99f, 1.01f);
        timings.Sort();
        output.WriteLine($"warm short-query latency via {await Ollama.FindServingEndpointAsync()}: " +
                         $"median {timings[timings.Count / 2]:F0} ms, max {timings[^1]:F0} ms ({string.Join(", ", timings.Select(t => $"{t:F0}"))})");
    }

    /// <summary>
    /// The issue's acceptance check: embedding an article exactly as the dataset did (<c>title + "\n\n" + text</c>)
    /// must reproduce its stored vector. If not, query vectors aren't comparable and search quality silently suffers.
    /// Uses non-truncated articles so Ollama sees the same tokens the dataset builder did: one ~1k-token article per
    /// language, plus a ~3–4k-token one (beyond Ollama's default 2,048-token batch and the 512-token ubatch that
    /// crashed Ollama ≤0.12) that still fits the per-endpoint timeout on the 2-vCPU VPS.
    /// </summary>
    [CorpusEmbeddingFact]
    public async Task Embedding_an_article_reproduces_its_stored_dataset_vector()
    {
        var corpus = app.Services.GetRequiredService<CorpusConnectionFactory>();
        var samples = new List<(string Lang, string Title, string Text, float[] Stored)>();
        await using (var c = await corpus.OpenAsync())
        {
            foreach (var (lang, minChars, maxChars) in new[] { ("fi", 1000, 8000), ("sv", 1000, 8000), ("en", 1000, 8000), ("fi", 12000, 16000) })
            {
                await using var cmd = new MySqlCommand("""
                    SELECT a.title, a.text, v.embedding
                    FROM article a JOIN article_vector v ON v.lang = a.lang AND v.page_id = a.page_id
                    WHERE a.lang = @lang AND NOT v.truncated AND CHAR_LENGTH(a.text) BETWEEN @min AND @max
                    ORDER BY a.page_id LIMIT 1
                    """, c);
                cmd.Parameters.AddWithValue("@lang", lang);
                cmd.Parameters.AddWithValue("@min", minChars);
                cmd.Parameters.AddWithValue("@max", maxChars);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                    samples.Add((lang, r.GetString(0), r.GetString(1), CorpusVectors.FromBytes((byte[])r.GetValue(2))));
            }
        }
        Assert.NotEmpty(samples); // no rows → dataset S (P0.2) isn't loaded yet

        foreach (var (lang, title, text, stored) in samples)
        {
            var sw = Stopwatch.StartNew();
            var mine = await Ollama.EmbedAsync(title + "\n\n" + text);
            var cos = Cosine(mine, stored);
            output.WriteLine($"{lang} '{title}' ({text.Length} chars): cosine {cos:F5}, {sw.ElapsedMilliseconds} ms");
            Assert.True(cos > 0.99, $"{lang} '{title}': cosine {cos:F5} — our bge-m3 setup differs from the dataset's");
        }
    }

    private static double Cosine(float[] a, float[] b)
    {
        Assert.Equal(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / Math.Sqrt(na * nb);
    }
}
