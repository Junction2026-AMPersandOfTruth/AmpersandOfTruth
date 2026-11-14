using Junction.Tests.Infrastructure;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Store;

namespace Junction.Tests.Features.Analysis.Store;

public sealed class AnalysisLedgerTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly AnalysisLedger _ledger;

    public AnalysisLedgerTests() => _ledger = new AnalysisLedger(_db.Factory);

    [Fact]
    public async Task Summarize_counts_outcomes_and_sums_live_calls_only_with_scope_and_since_filters()
    {
        var t0 = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var changedCount = 0;
        _ledger.Changed += () => changedCount++;

        // Older row before `since` — must be excluded when querying from t0.
        await _ledger.RecordAsync(MakeRecord(
            at: t0.AddHours(-2),
            scope: "inv-1",
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Invalid,
            fromCache: false,
            input: 999,
            output: 999,
            thought: 999,
            cost: 9.99));

        // 6 mixed rows at or after t0:
        // 1. Cached Ok (inv-1) — counts in Calls, CacheHits, Ok, NOT LiveCalls, NOT token/cost sums
        await _ledger.RecordAsync(MakeRecord(
            at: t0,
            scope: "inv-1",
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Ok,
            fromCache: true,
            input: 5000,
            output: 500,
            thought: 200,
            cost: 5.00));

        // 2. Live Ok (inv-1)
        await _ledger.RecordAsync(MakeRecord(
            at: t0.AddMinutes(1),
            scope: "inv-1",
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Ok,
            fromCache: false,
            input: 1000,
            output: 100,
            thought: 50,
            cost: 0.25));

        // 3. Live Blocked (inv-1)
        await _ledger.RecordAsync(MakeRecord(
            at: t0.AddMinutes(2),
            scope: "inv-1",
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Blocked,
            fromCache: false,
            input: 800,
            output: 0,
            thought: 0,
            cost: 0.10));

        // 4. Live Failed (inv-1)
        await _ledger.RecordAsync(MakeRecord(
            at: t0.AddMinutes(3),
            scope: "inv-1",
            model: "gemini-4-carbon",
            outcome: AnalysisOutcome.Failed,
            fromCache: false,
            input: 400,
            output: 20,
            thought: 10,
            cost: 0.05));

        // 5. BudgetExceeded (inv-1) — NOT a live call, NOT included in token/cost sums
        await _ledger.RecordAsync(MakeRecord(
            at: t0.AddMinutes(4),
            scope: "inv-1",
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.BudgetExceeded,
            fromCache: false,
            input: 777,
            output: 777,
            thought: 777,
            cost: 7.77));

        // 6. Live Ok in other scope (inv-2)
        await _ledger.RecordAsync(MakeRecord(
            at: t0.AddMinutes(5),
            scope: "inv-2",
            model: "gemini-4-carbon",
            outcome: AnalysisOutcome.Ok,
            fromCache: false,
            input: 2000,
            output: 300,
            thought: 150,
            cost: 0.60));

        Assert.Equal(7, changedCount);

        var allSinceT0 = await _ledger.SummarizeAsync(t0);
        Assert.Equal(6, allSinceT0.Calls);
        Assert.Equal(4, allSinceT0.LiveCalls);
        Assert.Equal(1, allSinceT0.CacheHits);
        Assert.Equal(3, allSinceT0.Ok);
        Assert.Equal(1, allSinceT0.Blocked);
        Assert.Equal(0, allSinceT0.Invalid);
        Assert.Equal(0, allSinceT0.Truncated);
        Assert.Equal(1, allSinceT0.Failed);
        Assert.Equal(1, allSinceT0.BudgetExceeded);
        Assert.Equal(1000L + 800 + 400 + 2000, allSinceT0.InputTokens);
        Assert.Equal(100L + 0 + 20 + 300, allSinceT0.OutputTokens);
        Assert.Equal(50L + 0 + 10 + 150, allSinceT0.ThoughtTokens);
        Assert.Equal(1.00, allSinceT0.EstimatedCostUsd, precision: 9);

        var inv1Only = await _ledger.SummarizeAsync(t0, scope: "inv-1");
        Assert.Equal(5, inv1Only.Calls);
        Assert.Equal(3, inv1Only.LiveCalls);
        Assert.Equal(1, inv1Only.CacheHits);
        Assert.Equal(2, inv1Only.Ok);
        Assert.Equal(1, inv1Only.Blocked);
        Assert.Equal(1, inv1Only.Failed);
        Assert.Equal(1, inv1Only.BudgetExceeded);
        Assert.Equal(2200L, inv1Only.InputTokens);
        Assert.Equal(120L, inv1Only.OutputTokens);
        Assert.Equal(60L, inv1Only.ThoughtTokens);
        Assert.Equal(0.40, inv1Only.EstimatedCostUsd, precision: 9);

        var empty = await _ledger.SummarizeAsync(t0.AddHours(1));
        Assert.Equal(new UsageSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), empty);

        var includingOlder = await _ledger.SummarizeAsync(t0.AddHours(-3));
        Assert.Equal(7, includingOlder.Calls);
        Assert.Equal(1, includingOlder.Invalid);
    }

    [Fact]
    public async Task ByModel_groups_live_calls_and_orders_by_cost_descending()
    {
        var since = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Excluded rows: before since, cached, BudgetExceeded
        await _ledger.RecordAsync(MakeRecord(since.AddMinutes(-5), "s", "gemini-4-hydrogen", AnalysisOutcome.Ok, false, 100, 10, 0, 9.00, latencyMs: 500));
        await _ledger.RecordAsync(MakeRecord(since, "s", "gemini-4-hydrogen", AnalysisOutcome.Ok, true, 500, 50, 0, 5.00, latencyMs: 1));
        await _ledger.RecordAsync(MakeRecord(since, "s", "gemini-4-hydrogen", AnalysisOutcome.BudgetExceeded, false, 0, 0, 0, 0.0, latencyMs: 0));

        // Included live rows
        await _ledger.RecordAsync(MakeRecord(since.AddMinutes(1), "s", "gemini-4-hydrogen", AnalysisOutcome.Ok, false, 1000, 100, 20, 0.20, latencyMs: 200));
        await _ledger.RecordAsync(MakeRecord(since.AddMinutes(2), "s", "gemini-4-hydrogen", AnalysisOutcome.Truncated, false, 500, 200, 30, 0.30, latencyMs: 400));
        await _ledger.RecordAsync(MakeRecord(since.AddMinutes(3), "s", "gemini-4-carbon", AnalysisOutcome.Ok, false, 2000, 400, 100, 1.25, latencyMs: 900));

        var byModel = await _ledger.ByModelAsync(since);

        Assert.Equal(2, byModel.Count);

        Assert.Equal("gemini-4-carbon", byModel[0].Model);
        Assert.Equal(1, byModel[0].LiveCalls);
        Assert.Equal(2000L, byModel[0].InputTokens);
        Assert.Equal(400L, byModel[0].OutputTokens);
        Assert.Equal(100L, byModel[0].ThoughtTokens);
        Assert.Equal(1.25, byModel[0].EstimatedCostUsd, precision: 9);
        Assert.Equal(900.0, byModel[0].AvgLatencyMs, precision: 9);

        Assert.Equal("gemini-4-hydrogen", byModel[1].Model);
        Assert.Equal(2, byModel[1].LiveCalls);
        Assert.Equal(1500L, byModel[1].InputTokens);
        Assert.Equal(300L, byModel[1].OutputTokens);
        Assert.Equal(50L, byModel[1].ThoughtTokens);
        Assert.Equal(0.50, byModel[1].EstimatedCostUsd, precision: 9);
        Assert.Equal(300.0, byModel[1].AvgLatencyMs, precision: 9);
    }

    [Fact]
    public async Task RecentAsync_orders_by_At_desc_then_Id_desc_and_RecordAsync_truncates_error()
    {
        var t1 = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(1);
        var longError = new string('x', 1500);

        var firstSameTime = MakeRecord(t1, "s1", "m", AnalysisOutcome.Failed, false, 1, 0, 0, 0, error: longError);
        var secondSameTime = MakeRecord(t1, "s2", "m", AnalysisOutcome.Ok, false, 2, 0, 0, 0);
        var newest = MakeRecord(t2, "s3", "m", AnalysisOutcome.Ok, false, 3, 0, 0, 0);

        await _ledger.RecordAsync(firstSameTime);
        await _ledger.RecordAsync(secondSameTime);
        await _ledger.RecordAsync(newest);

        var recentTwo = await _ledger.RecentAsync(take: 2);
        Assert.Equal(["s3", "s2"], recentTwo.Select(r => r.Scope));

        var recentAll = await _ledger.RecentAsync();
        Assert.Equal(["s3", "s2", "s1"], recentAll.Select(r => r.Scope));
        Assert.NotNull(recentAll[2].Error);
        Assert.Equal(1000, recentAll[2].Error!.Length);
    }

    private static AnalysisCallRecord MakeRecord(
        DateTimeOffset at,
        string? scope,
        string model,
        AnalysisOutcome outcome,
        bool fromCache,
        int input,
        int output,
        int thought,
        double cost,
        long latencyMs = 100,
        string? error = null) => new()
    {
        At = at,
        Scope = scope,
        PromptId = "classify-edit",
        PromptVersion = "1.3fa2c9e0",
        Model = model,
        Job = AnalysisJob.Classify,
        Outcome = outcome,
        FromCache = fromCache,
        Attempts = fromCache || outcome == AnalysisOutcome.BudgetExceeded ? 0 : 1,
        InputTokens = input,
        OutputTokens = output,
        ThoughtTokens = thought,
        CachedTokens = 0,
        LatencyMs = latencyMs,
        EstimatedCostUsd = cost,
        Error = error,
    };

    public void Dispose() => _db.Dispose();
}
