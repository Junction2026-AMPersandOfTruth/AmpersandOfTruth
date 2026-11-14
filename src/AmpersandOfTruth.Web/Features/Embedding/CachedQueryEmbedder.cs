using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Corpus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Embedding;

/// <summary>
/// The app's <see cref="IQueryEmbedder"/>: memory → SQLite (<c>QueryEmbeddings</c>) → Ollama. Once a demo topic
/// has been embedded it never waits on (or needs) Ollama again, even across restarts.
/// Key = model + normalised text (NFC, trimmed, whitespace collapsed; case kept — bge-m3 is case-sensitive).
/// The normalised text is what gets embedded, so a cached vector is exactly what Ollama would return.
/// Singleton, thread-safe. Failures are not cached. <paramref name="inner"/> is the uncached embedder (Ollama in the app).
/// </summary>
public sealed partial class CachedQueryEmbedder(
    IQueryEmbedder inner, IOptions<EmbeddingOptions> options, IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider clock, ILogger<CachedQueryEmbedder> log) : IQueryEmbedder
{
    private const int MaxMemoryEntries = 10_000;
    private readonly ConcurrentDictionary<(string Model, string Text), float[]> _memory = new();

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var key = (options.Value.Model, Normalize(text));

        if (_memory.TryGetValue(key, out var hit)) return (float[])hit.Clone();

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var row = await db.Set<QueryEmbedding>().AsNoTracking()
                .Where(e => e.Model == key.Model && e.Text == key.Item2)
                .Select(e => e.Vector).FirstOrDefaultAsync(ct);
            if (row is not null)
                return (float[])Remember(key, CorpusVectors.FromBytes(row)).Clone();
        }

        var vector = await inner.EmbedAsync(key.Item2, ct);
        Remember(key, (float[])vector.Clone());

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.Add(new QueryEmbedding(key.Model, key.Item2, CorpusVectors.ToBytes(vector), clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Concurrent miss for the same text already stored it (PK clash) — the vector is identical.
            log.LogDebug(ex, "Query embedding for {Text} already cached", key.Item2);
        }
        return vector;
    }

    /// <summary>NFC, trim, collapse whitespace runs to one space. Case is preserved.</summary>
    public static string Normalize(string text) =>
        Whitespace().Replace(text.Normalize(NormalizationForm.FormC).Trim(), " ");

    private float[] Remember((string, string) key, float[] vector)
    {
        if (_memory.Count >= MaxMemoryEntries) _memory.Clear(); // crude bound; SQLite still has everything
        return _memory.GetOrAdd(key, vector);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
