namespace AmpersandOfTruth.Web.Features.Analysis.Store;

/// <summary>
/// Response cache row (table <c>AnalysisCache</c>). Makes reruns free and the demo deterministic.
/// Only <see cref="AnalysisOutcome.Ok"/> (validated JSON) and <see cref="AnalysisOutcome.Blocked"/> (deterministic refusal) are cached.
/// </summary>
public sealed class AnalysisCacheEntry
{
    /// <summary>PK. 64 lowercase hex chars: SHA-256 of prompt id + full version + model + schema + rendered system + rendered user + settings.</summary>
    public required string Key { get; init; }
    public required string PromptId { get; init; }
    public required string PromptVersion { get; init; }
    public required string Model { get; init; }
    /// <summary>Ok or Blocked. Stored as string.</summary>
    public required AnalysisOutcome Outcome { get; init; }
    /// <summary>The validated JSON text for Ok; null for Blocked.</summary>
    public string? ResponseJson { get; init; }
    /// <summary>Blocked reason (Gemini error code + message); null for Ok.</summary>
    public string? Error { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ThoughtTokens { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Usage ledger row (table <c>AnalysisCalls</c>): one per <see cref="IAnalysisModel.RunAsync{T}"/> call, cached or live.
/// Source of the daily/per-scope caps (after a restart), the token log per investigation and the /analysis dashboard.
/// </summary>
public sealed class AnalysisCallRecord
{
    /// <summary>Autoincrement PK.</summary>
    public long Id { get; set; }
    public required DateTimeOffset At { get; init; }
    /// <summary>Investigation / experiment run id; null = unscoped.</summary>
    public string? Scope { get; init; }
    public required string PromptId { get; init; }
    public required string PromptVersion { get; init; }
    public required string Model { get; init; }
    /// <summary>Stored as string.</summary>
    public required AnalysisJob Job { get; init; }
    /// <summary>Stored as string.</summary>
    public required AnalysisOutcome Outcome { get; init; }
    public bool FromCache { get; init; }
    public int Attempts { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ThoughtTokens { get; init; }
    public int CachedTokens { get; init; }
    public long LatencyMs { get; init; }
    public double EstimatedCostUsd { get; init; }
    /// <summary>Short error text for non-Ok outcomes (truncate to 1000 chars before storing).</summary>
    public string? Error { get; init; }
}

/// <param name="Calls">All recorded calls (cached + live).</param>
/// <param name="LiveCalls">Calls that reached Gemini (FromCache = false and Outcome != BudgetExceeded).</param>
public sealed record UsageSummary(
    int Calls,
    int LiveCalls,
    int CacheHits,
    int Ok,
    int Blocked,
    int Invalid,
    int Truncated,
    int Failed,
    int BudgetExceeded,
    long InputTokens,
    long OutputTokens,
    long ThoughtTokens,
    double EstimatedCostUsd);

/// <summary>Per-model aggregate over live calls only.</summary>
public sealed record ModelUsage(
    string Model,
    int LiveCalls,
    long InputTokens,
    long OutputTokens,
    long ThoughtTokens,
    double EstimatedCostUsd,
    double AvgLatencyMs);

public sealed record BudgetDecision(bool Allowed, string? Reason)
{
    public static readonly BudgetDecision Allow = new(true, null);
    public static BudgetDecision Deny(string reason) => new(false, reason);
}
