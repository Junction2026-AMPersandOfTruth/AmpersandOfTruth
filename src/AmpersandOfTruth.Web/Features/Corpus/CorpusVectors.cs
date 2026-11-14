using System.Buffers.Binary;

namespace AmpersandOfTruth.Web.Features.Corpus;

/// <summary>
/// Vector parameters for MariaDB <c>VECTOR(n)</c> columns. Pass the raw bytes as a plain parameter:
/// <code>cmd.Parameters.AddWithValue("@q", CorpusVectors.ToBytes(embedding));
/// ... ORDER BY VEC_DISTANCE_COSINE(embedding, @q) LIMIT 20</code>
/// (MariaDB stores vectors as little-endian float32; this avoids the JSON round-trip of <c>VEC_FromText</c>.)
/// </summary>
public static class CorpusVectors
{
    public static byte[] ToBytes(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), vector[i]);
        return bytes;
    }

    public static float[] FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
            throw new ArgumentException($"Vector byte length {bytes.Length} is not a multiple of 4.", nameof(bytes));
        var vector = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * sizeof(float))..]);
        return vector;
    }
}
