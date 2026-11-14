using Junction.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Junction.Web.Features.Analysis.Store;

/// <summary>SQLite response cache. Singleton; a DbContext per operation.</summary>
/// <remarks>TASK analysis-store implements this.</remarks>
public sealed class AnalysisCache(IDbContextFactory<AppDbContext> dbFactory)
{
    public Task<AnalysisCacheEntry?> GetAsync(string key, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    /// <summary>Upsert by <see cref="AnalysisCacheEntry.Key"/> (replace). Must not throw when the key already exists.</summary>
    public Task PutAsync(AnalysisCacheEntry entry, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    public Task<int> CountAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    /// <summary>Deletes all entries, or only one prompt's. Returns rows deleted.</summary>
    public Task<int> ClearAsync(string? promptId = null, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");
}

/// <summary>Usage ledger. Singleton; a DbContext per operation. <see cref="Changed"/> fires after every write (dashboard live refresh).</summary>
/// <remarks>TASK analysis-store implements this.</remarks>
public sealed class AnalysisLedger(IDbContextFactory<AppDbContext> dbFactory)
{
    public event Action? Changed;

    public Task RecordAsync(AnalysisCallRecord record, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    /// <summary>Aggregate over records with <c>At &gt;= since</c>, optionally only one scope.</summary>
    public Task<UsageSummary> SummarizeAsync(DateTimeOffset since, string? scope = null, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    /// <summary>Live calls only, grouped by model, ordered by estimated cost descending.</summary>
    public Task<IReadOnlyList<ModelUsage>> ByModelAsync(DateTimeOffset since, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    /// <summary>Newest first.</summary>
    public Task<IReadOnlyList<AnalysisCallRecord>> RecentAsync(int take = 50, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");
}

/// <summary>USD estimate from <see cref="GeminiOptions.Pricing"/>. Unknown model → 0. Pure apart from reading options.</summary>
/// <remarks>TASK analysis-store implements this.</remarks>
public sealed class ModelPricing(IOptionsMonitor<GeminiOptions> options)
{
    /// <summary>(Input − Cached)·InputPerMTok + Cached·CachedInputPerMTok + (Output + Thought)·OutputPerMTok, all ÷ 1e6.</summary>
    public double EstimateUsd(string model, AnalysisUsage usage) =>
        throw new NotImplementedException("analysis-store");
}

/// <summary>
/// Spend guard (the bill is someone else's and the key is powerful). Counts <b>live</b> calls per UTC day (app-wide) and per
/// scope, plus estimated USD per UTC day. In-memory, thread-safe; seeded from the ledger the first time a given UTC day /
/// scope is seen so a restart doesn't reset the caps. Singleton.
/// </summary>
/// <remarks>TASK analysis-store implements this.</remarks>
public sealed class AnalysisBudget(AnalysisLedger ledger, IOptionsMonitor<GeminiOptions> options, TimeProvider time)
{
    /// <summary>Atomically checks the caps and, if allowed, counts one live call against today and the scope.
    /// Deny reasons name the cap, e.g. <c>"daily call cap 20000 reached"</c>, <c>"scope 'inv-42' call cap 1500 reached"</c>,
    /// <c>"daily cost cap $150.00 reached ($150.12 spent)"</c>.</summary>
    public Task<BudgetDecision> TryReserveAsync(string? scope, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis-store");

    /// <summary>Adds a finished live call's estimated cost to today's total.</summary>
    public void AddCost(double usd) => throw new NotImplementedException("analysis-store");

    /// <summary>Snapshot for the dashboard: live calls and USD so far today (UTC).</summary>
    public (int Calls, double CostUsd) Today => throw new NotImplementedException("analysis-store");
}
