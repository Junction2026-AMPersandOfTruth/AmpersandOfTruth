using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Corpus;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace AmpersandOfTruth.Tests.Features.Corpus;

/// <summary>Against the real VPS corpus through the tunnel, wired exactly as the app wires it. Opt-in: see <see cref="CorpusFactAttribute"/>.</summary>
[Trait("Category", "Corpus")]
public class CorpusIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    private CorpusConnectionFactory Corpus => app.Services.GetRequiredService<CorpusConnectionFactory>();

    private static async Task<object?> Scalar(MySqlConnection c, string sql)
    {
        await using var cmd = new MySqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }

    [CorpusFact]
    public async Task Connects_with_the_tuned_server_settings()
    {
        await using var c = await Corpus.OpenAsync();

        Assert.StartsWith("12.", (string)(await Scalar(c, "SELECT VERSION()"))!);
        Assert.Equal("corpus", await Scalar(c, "SELECT DATABASE()"));
        Assert.Equal(80L, Convert.ToInt64(await Scalar(c, "SELECT @@mhnsw_ef_search")));
        Assert.True(Convert.ToInt64(await Scalar(c, "SELECT @@mhnsw_max_cache_size")) >= 256L << 20);
        Assert.True(Convert.ToInt64(await Scalar(c, "SELECT @@innodb_buffer_pool_size")) >= 2L << 30);
    }

    [CorpusFact]
    public async Task App_user_cannot_write_the_shared_corpus()
    {
        await using var c = await Corpus.OpenAsync();

        var ex = await Assert.ThrowsAsync<MySqlException>(() =>
            Scalar(c, "CREATE TABLE corpus.should_not_exist (id INT)"));
        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [CorpusFact]
    public async Task Vector_parameters_work_as_raw_bytes_and_as_VEC_FromText()
    {
        await using var c = await Corpus.OpenAsync();
        var table = $"research.vec_probe_{Guid.NewGuid():N}";
        await Scalar(c, $"""
            CREATE TABLE {table} (id INT PRIMARY KEY, v VECTOR(3) NOT NULL,
                                  VECTOR INDEX (v) M=6 DISTANCE=cosine)
            """);
        try
        {
            await using (var ins = new MySqlCommand($"INSERT INTO {table} VALUES (1, @bytes), (2, VEC_FromText(@json))", c))
            {
                ins.Parameters.AddWithValue("@bytes", CorpusVectors.ToBytes([1f, 0f, 0f]));
                ins.Parameters.AddWithValue("@json", "[0,1,0]");
                await ins.ExecuteNonQueryAsync();
            }

            await using var q = new MySqlCommand(
                $"SELECT id, v FROM {table} ORDER BY VEC_DISTANCE_COSINE(v, @q) LIMIT 1", c);
            q.Parameters.AddWithValue("@q", CorpusVectors.ToBytes([0.1f, 0.9f, 0f]));
            await using var r = await q.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(2, r.GetInt32(0));
            Assert.Equal([0f, 1f, 0f], CorpusVectors.FromBytes((byte[])r.GetValue(1)));
        }
        finally
        {
            await using var c2 = await Corpus.OpenAsync();
            await Scalar(c2, $"DROP TABLE IF EXISTS {table}");
        }
    }
}
