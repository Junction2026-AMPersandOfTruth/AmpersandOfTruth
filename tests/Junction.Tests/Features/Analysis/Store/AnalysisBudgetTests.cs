using Junction.Tests.Infrastructure;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Store;
using Microsoft.Extensions.Time.Testing;

namespace Junction.Tests.Features.Analysis.Store;

public sealed class AnalysisBudgetTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly AnalysisLedger _ledger;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 14, 0, 0, TimeSpan.Zero));
    private readonly TestOptionsMonitor<GeminiOptions> _options = new(new GeminiOptions
    {
        DailyCallCap = 20_000,
        DailyCostCapUsd = 150,
        PerScopeCallCap = 1_500,
    });

    public AnalysisBudgetTests() => _ledger = new AnalysisLedger(_db.Factory);

    [Fact]
    public async Task Daily_call_cap_denies_with_documented_reason()
    {
        _options.CurrentValue = new GeminiOptions { DailyCallCap = 3, DailyCostCapUsd = 150, PerScopeCallCap = 100 };
        var budget = CreateBudget();

        Assert.True((await budget.TryReserveAsync("inv-1")).Allowed);
        Assert.True((await budget.TryReserveAsync("inv-1")).Allowed);
        Assert.True((await budget.TryReserveAsync(null)).Allowed);

        var fourth = await budget.TryReserveAsync("inv-1");
        Assert.False(fourth.Allowed);
        Assert.Equal("daily call cap 3 reached", fourth.Reason);
        Assert.Equal((3, 0.0), budget.Today);

        // Raising DailyCallCap at runtime takes effect immediately.
        _options.CurrentValue = new GeminiOptions { DailyCallCap = 4, DailyCostCapUsd = 150, PerScopeCallCap = 100 };
        Assert.True((await budget.TryReserveAsync("inv-1")).Allowed);
        Assert.Equal((4, 0.0), budget.Today);
    }

    [Fact]
    public async Task Scope_cap_is_independent_of_daily_cap_and_persists_across_days()
    {
        _options.CurrentValue = new GeminiOptions { DailyCallCap = 100, DailyCostCapUsd = 150, PerScopeCallCap = 2 };
        var budget = CreateBudget();

        Assert.True((await budget.TryReserveAsync("inv-42")).Allowed);
        Assert.True((await budget.TryReserveAsync("inv-42")).Allowed);

        var denied = await budget.TryReserveAsync("inv-42");
        Assert.False(denied.Allowed);
        Assert.Equal("scope 'inv-42' call cap 2 reached", denied.Reason);

        // Other scopes and unscoped calls remain allowed, and denied scope calls did not increment daily calls.
        Assert.Equal((2, 0.0), budget.Today);
        Assert.True((await budget.TryReserveAsync("inv-99")).Allowed);
        Assert.True((await budget.TryReserveAsync(null)).Allowed);
        Assert.Equal((4, 0.0), budget.Today);

        // Advancing past UTC midnight resets daily calls, while lifetime scope cap still holds.
        _time.Advance(TimeSpan.FromDays(1));
        Assert.Equal((0, 0.0), budget.Today);
        var stillDenied = await budget.TryReserveAsync("inv-42");
        Assert.False(stillDenied.Allowed);
        Assert.Equal("scope 'inv-42' call cap 2 reached", stillDenied.Reason);
    }

    [Fact]
    public async Task Cost_cap_denies_after_AddCost_crosses_limit_and_resets_after_utc_midnight()
    {
        _options.CurrentValue = new GeminiOptions { DailyCallCap = 20_000, DailyCostCapUsd = 150, PerScopeCallCap = 1_500 };
        var budget = CreateBudget();

        Assert.True((await budget.TryReserveAsync("inv-1")).Allowed);
        budget.AddCost(100.00);
        Assert.True((await budget.TryReserveAsync("inv-1")).Allowed);
        budget.AddCost(50.12);

        Assert.Equal(2, budget.Today.Calls);
        Assert.Equal(150.12, budget.Today.CostUsd, precision: 9);

        var denied = await budget.TryReserveAsync("inv-1");
        Assert.False(denied.Allowed);
        Assert.Equal("daily cost cap $150.00 reached ($150.12 spent)", denied.Reason);

        // Advancing past UTC midnight resets daily call and cost counters.
        _time.Advance(TimeSpan.FromHours(11));
        Assert.Equal((0, 0.0), budget.Today);

        var nextDay = await budget.TryReserveAsync("inv-1");
        Assert.True(nextDay.Allowed);
        Assert.Equal((1, 0.0), budget.Today);
    }

    [Fact]
    public async Task New_budget_seeds_today_and_lifetime_scope_counts_from_ledger_after_restart()
    {
        var now = _time.GetUtcNow();
        var yesterday = now.AddDays(-1);

        // Yesterday's live call in inv-restart: counts toward lifetime scope cap, NOT today's daily cap/cost.
        await _ledger.RecordAsync(MakeLedgerRow(yesterday, "inv-restart", AnalysisOutcome.Ok, fromCache: false, cost: 80.0));

        // Today's live calls: 2 calls, $45.50 total cost (one in inv-restart, one unscoped).
        await _ledger.RecordAsync(MakeLedgerRow(now.AddHours(-2), "inv-restart", AnalysisOutcome.Ok, fromCache: false, cost: 25.25));
        await _ledger.RecordAsync(MakeLedgerRow(now.AddHours(-1), null, AnalysisOutcome.Failed, fromCache: false, cost: 20.25));

        // Today's cached and BudgetExceeded rows: must NOT count toward live calls or cost.
        await _ledger.RecordAsync(MakeLedgerRow(now.AddMinutes(-30), "inv-restart", AnalysisOutcome.Ok, fromCache: true, cost: 99.0));
        await _ledger.RecordAsync(MakeLedgerRow(now.AddMinutes(-15), "inv-restart", AnalysisOutcome.BudgetExceeded, fromCache: false, cost: 0.0));

        _options.CurrentValue = new GeminiOptions
        {
            DailyCallCap = 3,
            DailyCostCapUsd = 100,
            PerScopeCallCap = 2,
        };

        // Simulate app restart with a fresh AnalysisBudget over the persisted ledger.
        var restartedBudget = CreateBudget();

        // inv-restart already has 2 lifetime live calls (1 yesterday + 1 today) → denied by PerScopeCallCap = 2.
        var scopeDecision = await restartedBudget.TryReserveAsync("inv-restart");
        Assert.False(scopeDecision.Allowed);
        Assert.Equal("scope 'inv-restart' call cap 2 reached", scopeDecision.Reason);

        // Today was seeded with 2 live calls and $45.50 cost → one more live call in a fresh scope is allowed (reaches 3).
        Assert.Equal(2, restartedBudget.Today.Calls);
        Assert.Equal(45.50, restartedBudget.Today.CostUsd, precision: 9);

        Assert.True((await restartedBudget.TryReserveAsync("inv-new")).Allowed);
        Assert.Equal(3, restartedBudget.Today.Calls);

        // 4th daily call is now denied by DailyCallCap = 3.
        var dailyDecision = await restartedBudget.TryReserveAsync("inv-new");
        Assert.False(dailyDecision.Allowed);
        Assert.Equal("daily call cap 3 reached", dailyDecision.Reason);
    }

    [Fact]
    public async Task Concurrent_TryReserveAsync_allows_exact_cap_count()
    {
        _options.CurrentValue = new GeminiOptions
        {
            DailyCallCap = 20,
            DailyCostCapUsd = 0,
            PerScopeCallCap = 100,
        };
        var budget = CreateBudget();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 50)
            .Select(async _ =>
            {
                await startGate.Task;
                return await budget.TryReserveAsync("inv-concurrent");
            })
            .ToArray();

        startGate.SetResult();
        var decisions = await Task.WhenAll(tasks);

        Assert.Equal(20, decisions.Count(d => d.Allowed));
        Assert.Equal(30, decisions.Count(d => !d.Allowed));
        Assert.Equal((20, 0.0), budget.Today);
    }

    private AnalysisBudget CreateBudget() => new(_ledger, _options, _time);

    private static AnalysisCallRecord MakeLedgerRow(
        DateTimeOffset at,
        string? scope,
        AnalysisOutcome outcome,
        bool fromCache,
        double cost) => new()
    {
        At = at,
        Scope = scope,
        PromptId = "classify-edit",
        PromptVersion = "1.3fa2c9e0",
        Model = "gemini-4-hydrogen",
        Job = AnalysisJob.Classify,
        Outcome = outcome,
        FromCache = fromCache,
        Attempts = fromCache || outcome == AnalysisOutcome.BudgetExceeded ? 0 : 1,
        InputTokens = 100,
        OutputTokens = 20,
        ThoughtTokens = 10,
        LatencyMs = 50,
        EstimatedCostUsd = cost,
    };

    public void Dispose() => _db.Dispose();
}
