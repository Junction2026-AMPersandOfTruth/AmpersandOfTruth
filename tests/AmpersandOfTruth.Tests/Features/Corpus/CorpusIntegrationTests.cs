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

    [CorpusFact]
    public async Task Dataset_S_has_expected_counts_and_vector_neighbours()
    {
        await using var c = await Corpus.OpenAsync();

        Assert.Equal(19_285L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM article WHERE lang = 'fi'")));
        Assert.Equal(4_921L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM article WHERE lang = 'sv'")));
        Assert.Equal(5_254L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM article WHERE lang = 'en'")));
        Assert.Equal(29_460L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM article_vector")));
        Assert.Equal(1_760L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM category_link")));
        Assert.True(Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM article_category")) > 35_000L);

        var rawBytes = (byte[])(await Scalar(c, """
            SELECT v.embedding FROM article_vector v
            JOIN article a USING (lang, page_id)
            WHERE a.lang = 'fi' AND a.title = 'Talvisota'
            """))!;
        var vec = CorpusVectors.FromBytes(rawBytes);
        Assert.Equal(1024, vec.Length);

        await using var q = new MySqlCommand("""
            SELECT a.title, v.distance
            FROM (
                SELECT lang, page_id, VEC_DISTANCE_COSINE(embedding, @q) AS distance
                FROM article_vector
                ORDER BY distance
                LIMIT 5
            ) AS v
            JOIN article a USING (lang, page_id)
            ORDER BY v.distance
            """, c);
        q.Parameters.AddWithValue("@q", CorpusVectors.ToBytes(vec));

        var titles = new List<string>();
        await using (var r = await q.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                titles.Add(r.GetString(0));
        }

        Assert.Equal("Talvisota", titles[0]);
        Assert.Contains(titles, t => t is "Jatkosota" or "Talvisodan tausta");
    }

    [CorpusFact]
    public async Task Dataset_M_has_expected_counts_and_indexed_revision_queries()
    {
        await using var c = await Corpus.OpenAsync();

        Assert.Equal(862_076L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM revision")));
        Assert.Equal(19_742L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(DISTINCT page_id) FROM revision")));
        Assert.Equal(862_076L, Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM revision_text")));

        // EXPLAIN 1: all revisions of page X ordered by time uses idx_page_ts without filesort
        await using (var expTime = new MySqlCommand(
            "EXPLAIN SELECT rev_id, ts, user, comment FROM revision WHERE page_id = 1256 ORDER BY ts, rev_id", c))
        await using (var r = await expTime.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            var key = r.IsDBNull(r.GetOrdinal("key")) ? null : r.GetString("key");
            var extra = r.IsDBNull(r.GetOrdinal("Extra")) ? "" : r.GetString("Extra");
            Assert.Equal("idx_page_ts", key);
            Assert.DoesNotContain("filesort", extra, StringComparison.OrdinalIgnoreCase);
        }

        // EXPLAIN 2: revisions sharing sha1 within page X uses idx_page_sha1
        await using (var expSha = new MySqlCommand(
            "EXPLAIN SELECT rev_id, ts, user FROM revision WHERE page_id = 1256 AND sha1 = 'r7yog8yfspwb3ftypeoukknggosrv32'", c))
        await using (var r = await expSha.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            var key = r.IsDBNull(r.GetOrdinal("key")) ? "" : r.GetString("key");
            Assert.Contains("idx_page_sha1", key);
        }

        var sampleLen = Convert.ToInt64(await Scalar(c, """
            SELECT CHAR_LENGTH(rt.text)
            FROM revision r
            JOIN revision_text rt USING (page_id, rev_id)
            WHERE r.page_id = 1256
            ORDER BY r.ts DESC, r.rev_id DESC
            LIMIT 1
            """));
        Assert.True(sampleLen > 1_000L);
    }
}
