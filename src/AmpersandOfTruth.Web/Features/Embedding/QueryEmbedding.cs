namespace AmpersandOfTruth.Web.Features.Embedding;

/// <summary>Persistent cache row: <see cref="Vector"/> is little-endian float32 (same encoding as the corpus).</summary>
public sealed record QueryEmbedding(string Model, string Text, byte[] Vector, DateTimeOffset CreatedAt);
