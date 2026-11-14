using System.Security.Cryptography;
using System.Text;
using AmpersandOfTruth.Web.Features.Embedding;

namespace AmpersandOfTruth.Tests.Features.Embedding;

/// <summary>
/// Deterministic stand-in for bge-m3: same text → same unit vector of 1024 floats, different text → (almost
/// surely) a different one. Register with <c>Services.AddSingleton&lt;IQueryEmbedder&gt;(new FakeQueryEmbedder())</c>.
/// <see cref="Unavailable"/> simulates Ollama being down.
/// </summary>
public sealed class FakeQueryEmbedder : IQueryEmbedder
{
    private int _calls;

    public int Calls => _calls;
    public bool Unavailable { get; set; }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        if (Unavailable) throw new EmbeddingUnavailableException("fake embedder is down");
        return Task.FromResult(VectorFor(text));
    }

    public static float[] VectorFor(string text)
    {
        var seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var rng = new Random(seed);
        var v = new float[EmbeddingOptions.Dimensions];
        for (var i = 0; i < v.Length; i++) v[i] = (float)(rng.NextDouble() * 2 - 1);
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }
}
