using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Embedding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Embedding;

public class CachedQueryEmbedderTests
{
    private static CachedQueryEmbedder Create(TestDb db, IQueryEmbedder inner, string model = "bge-m3") =>
        new(inner, Options.Create(new EmbeddingOptions { Model = model }), db.Factory, TimeProvider.System,
            NullLogger<CachedQueryEmbedder>.Instance);

    [Fact]
    public async Task Second_call_is_served_from_memory()
    {
        using var db = new TestDb();
        var fake = new FakeQueryEmbedder();
        var cache = Create(db, fake);

        var first = await cache.EmbedAsync("Talvisota");
        var second = await cache.EmbedAsync("Talvisota");

        Assert.Equal(1, fake.Calls);
        Assert.Equal(first, second);
        Assert.Equal(FakeQueryEmbedder.VectorFor("Talvisota"), first);
    }

    [Fact]
    public async Task Survives_a_restart_via_SQLite_so_demo_topics_work_with_Ollama_down()
    {
        using var db = new TestDb();
        await Create(db, new FakeQueryEmbedder()).EmbedAsync("Suomen sisällissota");

        var down = new FakeQueryEmbedder { Unavailable = true };
        var afterRestart = await Create(db, down).EmbedAsync("Suomen sisällissota");

        Assert.Equal(FakeQueryEmbedder.VectorFor("Suomen sisällissota"), afterRestart);
        Assert.Equal(0, down.Calls);
    }

    [Fact]
    public async Task Whitespace_variants_share_one_entry_and_the_normalised_text_is_what_gets_embedded()
    {
        using var db = new TestDb();
        var fake = new FakeQueryEmbedder();
        var cache = Create(db, fake);

        var a = await cache.EmbedAsync("  Karjalan \t kysymys\n");
        var b = await cache.EmbedAsync("Karjalan kysymys");

        Assert.Equal(1, fake.Calls);
        Assert.Equal(FakeQueryEmbedder.VectorFor("Karjalan kysymys"), a);
        Assert.Equal(a, b);
        Assert.Equal("Karjalan kysymys", CachedQueryEmbedder.Normalize(" Karjalan  kysymys "));
    }

    [Fact]
    public async Task Case_is_kept_because_bge_m3_is_case_sensitive()
    {
        using var db = new TestDb();
        var fake = new FakeQueryEmbedder();
        var cache = Create(db, fake);

        await cache.EmbedAsync("talvisota");
        await cache.EmbedAsync("Talvisota");

        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task Returned_arrays_can_be_mutated_without_corrupting_the_cache()
    {
        using var db = new TestDb();
        var cache = Create(db, new FakeQueryEmbedder());

        var first = await cache.EmbedAsync("topic");
        Array.Clear(first);

        Assert.Equal(FakeQueryEmbedder.VectorFor("topic"), await cache.EmbedAsync("topic"));
    }

    [Fact]
    public async Task A_different_model_does_not_reuse_cached_vectors()
    {
        using var db = new TestDb();
        await Create(db, new FakeQueryEmbedder(), model: "bge-m3").EmbedAsync("topic");

        var other = new FakeQueryEmbedder();
        await Create(db, other, model: "bge-m3:567m-fp16").EmbedAsync("topic");

        Assert.Equal(1, other.Calls);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        using var db = new TestDb();
        var fake = new FakeQueryEmbedder { Unavailable = true };
        var cache = Create(db, fake);

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => cache.EmbedAsync("topic"));
        fake.Unavailable = false;

        Assert.Equal(1024, (await cache.EmbedAsync("topic")).Length);
        Assert.Equal(2, fake.Calls);
    }
}
