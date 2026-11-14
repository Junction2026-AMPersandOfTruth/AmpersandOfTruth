using Junction.Tests.Infrastructure;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Store;

namespace Junction.Tests.Features.Analysis.Store;

public sealed class AnalysisCacheTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly AnalysisCache _cache;

    public AnalysisCacheTests() => _cache = new AnalysisCache(_db.Factory);

    [Fact]
    public async Task Put_and_Get_round_trips_all_fields()
    {
        var createdAt = new DateTimeOffset(2026, 11, 14, 9, 30, 15, TimeSpan.FromHours(2));
        var okEntry = new AnalysisCacheEntry
        {
            Key = new string('a', 64),
            PromptId = "classify-edit",
            PromptVersion = "1.3fa2c9e0",
            Model = "gemini-4-hydrogen",
            Outcome = AnalysisOutcome.Ok,
            ResponseJson = """{"label":"vandalism"}""",
            Error = null,
            InputTokens = 1200,
            OutputTokens = 85,
            ThoughtTokens = 340,
            CreatedAt = createdAt,
        };
        var blockedEntry = new AnalysisCacheEntry
        {
            Key = new string('b', 64),
            PromptId = "classify-edit",
            PromptVersion = "1.3fa2c9e0",
            Model = "gemini-4-hydrogen",
            Outcome = AnalysisOutcome.Blocked,
            ResponseJson = null,
            Error = "PROHIBITED_CONTENT: blocked",
            InputTokens = 900,
            OutputTokens = 0,
            ThoughtTokens = 0,
            CreatedAt = createdAt.AddMinutes(1),
        };

        await _cache.PutAsync(okEntry);
        await _cache.PutAsync(blockedEntry);

        var loadedOk = await _cache.GetAsync(okEntry.Key);
        Assert.NotNull(loadedOk);
        Assert.Equal(okEntry.Key, loadedOk.Key);
        Assert.Equal(okEntry.PromptId, loadedOk.PromptId);
        Assert.Equal(okEntry.PromptVersion, loadedOk.PromptVersion);
        Assert.Equal(okEntry.Model, loadedOk.Model);
        Assert.Equal(AnalysisOutcome.Ok, loadedOk.Outcome);
        Assert.Equal(okEntry.ResponseJson, loadedOk.ResponseJson);
        Assert.Null(loadedOk.Error);
        Assert.Equal(1200, loadedOk.InputTokens);
        Assert.Equal(85, loadedOk.OutputTokens);
        Assert.Equal(340, loadedOk.ThoughtTokens);
        Assert.Equal(createdAt.ToUniversalTime(), loadedOk.CreatedAt);
        Assert.Equal(TimeSpan.Zero, loadedOk.CreatedAt.Offset);

        var loadedBlocked = await _cache.GetAsync(blockedEntry.Key);
        Assert.NotNull(loadedBlocked);
        Assert.Equal(AnalysisOutcome.Blocked, loadedBlocked.Outcome);
        Assert.Null(loadedBlocked.ResponseJson);
        Assert.Equal("PROHIBITED_CONTENT: blocked", loadedBlocked.Error);

        Assert.Null(await _cache.GetAsync(new string('c', 64)));
    }

    [Fact]
    public async Task Put_twice_with_same_key_replaces_existing_entry()
    {
        var key = new string('d', 64);
        var first = new AnalysisCacheEntry
        {
            Key = key,
            PromptId = "p1",
            PromptVersion = "1.00000001",
            Model = "gemini-4-hydrogen",
            Outcome = AnalysisOutcome.Ok,
            ResponseJson = """{"v":1}""",
            InputTokens = 10,
            OutputTokens = 5,
            ThoughtTokens = 2,
            CreatedAt = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
        };
        var second = new AnalysisCacheEntry
        {
            Key = key,
            PromptId = "p1",
            PromptVersion = "1.00000002",
            Model = "gemini-4-carbon",
            Outcome = AnalysisOutcome.Blocked,
            ResponseJson = null,
            Error = "SAFETY",
            InputTokens = 20,
            OutputTokens = 0,
            ThoughtTokens = 0,
            CreatedAt = new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.Zero),
        };

        await _cache.PutAsync(first);
        await _cache.PutAsync(second);

        Assert.Equal(1, await _cache.CountAsync());
        var loaded = await _cache.GetAsync(key);
        Assert.NotNull(loaded);
        Assert.Equal("1.00000002", loaded.PromptVersion);
        Assert.Equal("gemini-4-carbon", loaded.Model);
        Assert.Equal(AnalysisOutcome.Blocked, loaded.Outcome);
        Assert.Null(loaded.ResponseJson);
        Assert.Equal("SAFETY", loaded.Error);
        Assert.Equal(20, loaded.InputTokens);
        Assert.Equal(second.CreatedAt, loaded.CreatedAt);
    }

    [Fact]
    public async Task ClearAsync_filters_by_prompt_or_clears_all()
    {
        await _cache.PutAsync(MakeEntry("k1", "p"));
        await _cache.PutAsync(MakeEntry("k2", "p"));
        await _cache.PutAsync(MakeEntry("k3", "other"));

        Assert.Equal(3, await _cache.CountAsync());

        var deletedPrompt = await _cache.ClearAsync("p");
        Assert.Equal(2, deletedPrompt);
        Assert.Equal(1, await _cache.CountAsync());
        Assert.Null(await _cache.GetAsync("k1"));
        Assert.NotNull(await _cache.GetAsync("k3"));

        var deletedAll = await _cache.ClearAsync();
        Assert.Equal(1, deletedAll);
        Assert.Equal(0, await _cache.CountAsync());
    }

    private static AnalysisCacheEntry MakeEntry(string key, string promptId) => new()
    {
        Key = key,
        PromptId = promptId,
        PromptVersion = "1.abcd1234",
        Model = "gemini-4-hydrogen",
        Outcome = AnalysisOutcome.Ok,
        ResponseJson = "{}",
        CreatedAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
    };

    public void Dispose() => _db.Dispose();
}
