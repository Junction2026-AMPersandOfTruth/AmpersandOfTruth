using System.Diagnostics;
using AmpersandOfTruth.Web.Features.Corpus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace AmpersandOfTruth.Tests.Features.Corpus;

public class CorpusUnitTests
{
    private static CorpusConnectionFactory Factory(string cs, string? password = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Corpus"] = cs,
            ["Corpus:Password"] = password,
        }).Build());

    [Fact]
    public async Task Password_comes_from_separate_setting_and_is_not_in_Server()
    {
        await using var f = Factory("Server=127.0.0.1;Port=3307;Database=corpus;User ID=corpus_ro", "s3cret");

        Assert.Equal("s3cret", new MySqlConnectionStringBuilder(f.ConnectionString).Password);
        Assert.Equal("127.0.0.1:3307/corpus", f.Server);
        Assert.DoesNotContain("s3cret", f.Server);
    }

    [Fact]
    public void Vector_bytes_are_little_endian_float32_and_round_trip()
    {
        float[] v = [1f, -2.5f, 0f];

        var bytes = CorpusVectors.ToBytes(v);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x80, 0x3F }, bytes[..4]); // 1.0f
        Assert.Equal(v, CorpusVectors.FromBytes(bytes));
        Assert.Throws<ArgumentException>(() => CorpusVectors.FromBytes(new byte[5]));
    }

    [Fact]
    public async Task Health_check_is_degraded_and_fast_when_corpus_is_unreachable()
    {
        // Port 1 on loopback: nothing listens → connection refused.
        await using var f = Factory("Server=127.0.0.1;Port=1;Database=corpus;User ID=corpus_ro;Connection Timeout=2");
        var check = new CorpusHealthCheck(f);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("corpus", check, HealthStatus.Degraded, null),
        };

        var sw = Stopwatch.StartNew();
        var result = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }
}
