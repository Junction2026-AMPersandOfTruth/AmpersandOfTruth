using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Junction.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Junction.Web.Features.Analysis.Store;

/// <summary>SQLite response cache. Singleton; a DbContext per operation.</summary>
public sealed class AnalysisCache(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<AnalysisCacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<AnalysisCacheEntry>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Upsert by <see cref="AnalysisCacheEntry.Key"/> (replace). Must not throw when the key already exists.</summary>
    public async Task PutAsync(AnalysisCacheEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.Set<AnalysisCacheEntry>().Where(e => e.Key == entry.Key).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        db.Set<AnalysisCacheEntry>().Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<AnalysisCacheEntry>().CountAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Deletes all entries, or only one prompt's. Returns rows deleted.</summary>
    public async Task<int> ClearAsync(string? promptId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<AnalysisCacheEntry> query = db.Set<AnalysisCacheEntry>();
        if (promptId is not null)
        {
            query = query.Where(e => e.PromptId == promptId);
        }

        return await query.ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Usage ledger. Singleton; a DbContext per operation. <see cref="Changed"/> fires after every write (dashboard live refresh).</summary>
public sealed class AnalysisLedger(IDbContextFactory<AppDbContext> dbFactory)
{
    public event Action? Changed;

    public async Task RecordAsync(AnalysisCallRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var row = new AnalysisCallRecord
        {
            At = record.At,
            Scope = record.Scope,
            PromptId = record.PromptId,
            PromptVersion = record.PromptVersion,
            Model = record.Model,
            Job = record.Job,
            Outcome = record.Outcome,
            FromCache = record.FromCache,
            Attempts = record.Attempts,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            ThoughtTokens = record.ThoughtTokens,
            CachedTokens = record.CachedTokens,
            LatencyMs = record.LatencyMs,
            EstimatedCostUsd = record.EstimatedCostUsd,
            Error = record.Error is { Length: > 1000 } ? record.Error[..1000] : record.Error,
        };

        await using (var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            db.Set<AnalysisCallRecord>().Add(row);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        record.Id = row.Id;
        Changed?.Invoke();
    }

    /// <summary>Aggregate over records with <c>At &gt;= since</c>, optionally only one scope.</summary>
    public async Task<UsageSummary> SummarizeAsync(DateTimeOffset since, string? scope = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<AnalysisCallRecord> query = db.Set<AnalysisCallRecord>()
            .AsNoTracking()
            .Where(r => r.At >= since);

        if (scope is not null)
        {
            query = query.Where(r => r.Scope == scope);
        }

        // GroupBy constant = one aggregate row; ToList (not FirstOrDefault) avoids EF's "First without OrderBy" warning.
        var rows = await query
            .GroupBy(_ => 1)
            .Select(g => new UsageSummary(
                g.Count(),
                g.Count(r => !r.FromCache && r.Outcome != AnalysisOutcome.BudgetExceeded),
                g.Count(r => r.FromCache),
                g.Count(r => r.Outcome == AnalysisOutcome.Ok),
                g.Count(r => r.Outcome == AnalysisOutcome.Blocked),
                g.Count(r => r.Outcome == AnalysisOutcome.Invalid),
                g.Count(r => r.Outcome == AnalysisOutcome.Truncated),
                g.Count(r => r.Outcome == AnalysisOutcome.Failed),
                g.Count(r => r.Outcome == AnalysisOutcome.BudgetExceeded),
                g.Sum(r => !r.FromCache && r.Outcome != AnalysisOutcome.BudgetExceeded ? (long)r.InputTokens : 0L),
                g.Sum(r => !r.FromCache && r.Outcome != AnalysisOutcome.BudgetExceeded ? (long)r.OutputTokens : 0L),
                g.Sum(r => !r.FromCache && r.Outcome != AnalysisOutcome.BudgetExceeded ? (long)r.ThoughtTokens : 0L),
                g.Sum(r => !r.FromCache && r.Outcome != AnalysisOutcome.BudgetExceeded ? r.EstimatedCostUsd : 0.0)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : new UsageSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>Live calls only, grouped by model, ordered by estimated cost descending.</summary>
    public async Task<IReadOnlyList<ModelUsage>> ByModelAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<AnalysisCallRecord>()
            .AsNoTracking()
            .Where(r => r.At >= since && !r.FromCache && r.Outcome != AnalysisOutcome.BudgetExceeded)
            .GroupBy(r => r.Model)
            .OrderByDescending(g => g.Sum(r => r.EstimatedCostUsd))
            .ThenBy(g => g.Key)
            .Select(g => new ModelUsage(
                g.Key,
                g.Count(),
                g.Sum(r => (long)r.InputTokens),
                g.Sum(r => (long)r.OutputTokens),
                g.Sum(r => (long)r.ThoughtTokens),
                g.Sum(r => r.EstimatedCostUsd),
                g.Average(r => (double)r.LatencyMs)))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<AnalysisCallRecord>> RecentAsync(int take = 50, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<AnalysisCallRecord>()
            .AsNoTracking()
            .OrderByDescending(r => r.At)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}

/// <summary>USD estimate from <see cref="GeminiOptions.Pricing"/>. Unknown model → 0. Pure apart from reading options.</summary>
public sealed class ModelPricing(IOptionsMonitor<GeminiOptions> options)
{
    /// <summary>(Input − Cached)·InputPerMTok + Cached·CachedInputPerMTok + (Output + Thought)·OutputPerMTok, all ÷ 1e6.</summary>
    public double EstimateUsd(string model, AnalysisUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (string.IsNullOrWhiteSpace(model))
        {
            return 0;
        }

        if (!TryFindPrice(options.CurrentValue.Pricing, model, out var price))
        {
            return 0;
        }

        var uncachedInput = Math.Max(0L, (long)usage.InputTokens - usage.CachedTokens);
        var cachedInput = Math.Max(0L, (long)usage.CachedTokens);
        var totalOutput = Math.Max(0L, (long)usage.OutputTokens + usage.ThoughtTokens);

        return ((uncachedInput * price.InputPerMTok)
            + (cachedInput * price.CachedInputPerMTok)
            + (totalOutput * price.OutputPerMTok)) / 1_000_000.0;
    }

    private static bool TryFindPrice(Dictionary<string, ModelPrice>? pricing, string model, [NotNullWhen(true)] out ModelPrice? price)
    {
        if (pricing is null)
        {
            price = null;
            return false;
        }

        if (pricing.TryGetValue(model, out price))
        {
            return price is not null;
        }

        foreach (var kvp in pricing)
        {
            if (string.Equals(kvp.Key, model, StringComparison.OrdinalIgnoreCase) && kvp.Value is not null)
            {
                price = kvp.Value;
                return true;
            }
        }

        price = null;
        return false;
    }
}

/// <summary>
/// Spend guard (the bill is someone else's and the key is powerful). Counts <b>live</b> calls per UTC day (app-wide) and per
/// scope, plus estimated USD per UTC day. In-memory, thread-safe; seeded from the ledger the first time a given UTC day /
/// scope is seen so a restart doesn't reset the caps. Singleton.
/// </summary>
public sealed class AnalysisBudget(AnalysisLedger ledger, IOptionsMonitor<GeminiOptions> options, TimeProvider time)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _scopeCalls = new(StringComparer.Ordinal);
    private DateOnly _day;
    private int _todayCalls;
    private double _todayCost;

    /// <summary>Atomically checks the caps and, if allowed, counts one live call against today and the scope.
    /// Deny reasons name the cap, e.g. <c>"daily call cap 20000 reached"</c>, <c>"scope 'inv-42' call cap 1500 reached"</c>,
    /// <c>"daily cost cap $150.00 reached ($150.12 spent)"</c>.</summary>
    public async Task<BudgetDecision> TryReserveAsync(string? scope, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var today = CurrentUtcDay();
            await EnsureDaySeededAsync(today, ct).ConfigureAwait(false);

            var opts = options.CurrentValue;
            if (_todayCalls >= opts.DailyCallCap)
            {
                return BudgetDecision.Deny($"daily call cap {opts.DailyCallCap} reached");
            }

            if (opts.DailyCostCapUsd > 0 && _todayCost >= opts.DailyCostCapUsd)
            {
                return BudgetDecision.Deny(string.Create(
                    CultureInfo.InvariantCulture,
                    $"daily cost cap ${opts.DailyCostCapUsd:0.00} reached (${_todayCost:0.00} spent)"));
            }

            var scopeCalls = 0;
            if (scope is not null)
            {
                if (!_scopeCalls.TryGetValue(scope, out scopeCalls))
                {
                    var scopeSummary = await ledger.SummarizeAsync(DateTimeOffset.MinValue, scope, ct).ConfigureAwait(false);
                    scopeCalls = scopeSummary.LiveCalls;
                    _scopeCalls[scope] = scopeCalls;
                }

                if (scopeCalls >= opts.PerScopeCallCap)
                {
                    return BudgetDecision.Deny($"scope '{scope}' call cap {opts.PerScopeCallCap} reached");
                }
            }

            _todayCalls++;
            if (scope is not null)
            {
                _scopeCalls[scope] = scopeCalls + 1;
            }

            return BudgetDecision.Allow;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Adds a finished live call's estimated cost to today's total.</summary>
    /// <remarks>Never touches the DB (it is called on Blazor circuit threads): if the UTC day rolled over since the
    /// reservation, the cost is dropped here — the ledger row still has it, and the next reservation re-seeds from the ledger.</remarks>
    public void AddCost(double usd)
    {
        if (usd <= 0) return;
        _gate.Wait();
        try
        {
            if (_day == CurrentUtcDay())
                _todayCost += usd;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Snapshot for the dashboard: live calls and USD so far today (UTC).</summary>
    public (int Calls, double CostUsd) Today
    {
        get
        {
            _gate.Wait();
            try
            {
                var today = CurrentUtcDay();
                return _day == today ? (_todayCalls, _todayCost) : (0, 0.0);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private DateOnly CurrentUtcDay() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private async Task EnsureDaySeededAsync(DateOnly today, CancellationToken ct)
    {
        if (_day == today)
        {
            return;
        }

        var utcMidnight = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var summary = await ledger.SummarizeAsync(utcMidnight, scope: null, ct).ConfigureAwait(false);
        _day = today;
        _todayCalls = summary.LiveCalls;
        _todayCost = summary.EstimatedCostUsd;
    }
}
