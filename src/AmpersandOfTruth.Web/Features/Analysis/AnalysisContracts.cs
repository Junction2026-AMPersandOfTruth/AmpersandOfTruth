using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AmpersandOfTruth.Web.Features.Analysis;

/// <summary>
/// The one entry point for product LLM calls (issue #16). Runs a versioned prompt
/// (<c>Features/Analysis/Prompts/&lt;id&gt;.md</c> + <c>&lt;id&gt;.schema.json</c>) with variables and returns
/// schema-validated, typed output. Never throws for model-side problems (blocked, malformed, rate-limited,
/// over budget) — those come back as an <see cref="AnalysisOutcome"/> so a pipeline can degrade one item
/// instead of failing the run. Throws only for programmer errors (unknown prompt, missing variable) and
/// <see cref="OperationCanceledException"/>.
/// </summary>
/// <remarks>
/// <typeparamref name="T"/> is deserialized from the validated JSON with <see cref="AnalysisJson.Options"/>
/// (snake_case properties, snake_case enum strings). Use <see cref="JsonElement"/> for untyped output.
/// </remarks>
public interface IAnalysisModel
{
    Task<AnalysisResult<T>> RunAsync<T>(AnalysisRequest request, CancellationToken ct = default);
}

/// <summary>What the call is for. Each job maps to a model id in config (<c>Gemini:Models:&lt;Job&gt;</c>),
/// so swapping tiers is a config change. A prompt declares its job in its front matter.</summary>
public enum AnalysisJob
{
    /// <summary>High-volume, closed-set labels (cheap tier).</summary>
    Triage,
    /// <summary>Per-edit classification with claim extraction.</summary>
    Classify,
    /// <summary>Finnish evidence → English.</summary>
    Translate,
    /// <summary>Topic understanding / query expansion before retrieval.</summary>
    QueryExpand,
    /// <summary>Incident summaries (quality over cost, low volume).</summary>
    Summarize,
    /// <summary>Eval judge — deliberately a different model from Classify.</summary>
    Judge,
}

public enum CachePolicy
{
    /// <summary>Serve from cache when present, store new results. Default.</summary>
    ReadWrite,
    /// <summary>Always call the model, then overwrite the cache entry (refresh).</summary>
    Refresh,
    /// <summary>Neither read nor write the cache.</summary>
    Bypass,
}

/// <param name="PromptId">File name of the prompt without extension, e.g. <c>classify-edit</c>.</param>
/// <param name="Variables">Template variables; every <c>{{name}}</c> / <c>{{data:name}}</c> in the prompt must be
/// present. Extra keys are ignored (so experiment rows can carry ids and metadata).</param>
public sealed record AnalysisRequest(string PromptId, IReadOnlyDictionary<string, string> Variables)
{
    /// <summary>Investigation id (or experiment run id). Drives the per-scope call cap and usage attribution.</summary>
    public string? Scope { get; init; }

    /// <summary>Force a model id (experiments / bake-offs). Default: the model configured for the job.</summary>
    public string? Model { get; init; }

    /// <summary>Override the prompt's declared job (changes which model is picked, nothing else).</summary>
    public AnalysisJob? Job { get; init; }

    /// <summary>Override the prompt's JSON schema (experiments). Part of the cache key.</summary>
    public JsonNode? Schema { get; init; }

    public CachePolicy Cache { get; init; } = CachePolicy.ReadWrite;
}

public enum AnalysisOutcome
{
    /// <summary>Schema-valid output in <see cref="AnalysisResult{T}.Value"/>.</summary>
    Ok,
    /// <summary>Gemini refused (safety / prohibited content / blocklist). Show as "unclassified — blocked";
    /// a block on a reverted edit is itself weak evidence of vandalism.</summary>
    Blocked,
    /// <summary>Output still failed schema validation (or deserialization) after the repair retry.</summary>
    Invalid,
    /// <summary>Hit max output tokens even after one retry with a larger limit.</summary>
    Truncated,
    /// <summary>Transport/API error after retries (rate limit, 5xx, timeout, bad request, no API key).</summary>
    Failed,
    /// <summary>Per-scope or daily cap reached; no call was made.</summary>
    BudgetExceeded,
}

public sealed record AnalysisResult<T>(AnalysisOutcome Outcome, T? Value, AnalysisCallInfo Info, string? Error = null)
{
    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsOk => Outcome == AnalysisOutcome.Ok;
}

/// <param name="PromptVersion"><c>&lt;front-matter version&gt;.&lt;8 hex of content hash&gt;</c>, e.g. <c>1.3fa2c9e0</c> —
/// editing a prompt without bumping its version still invalidates the cache.</param>
/// <param name="Attempts">HTTP attempts across transport retries, truncation retry and repair retry (0 when cached / over budget).</param>
/// <param name="RawText">The model's final raw output text (also for Invalid outcomes, for debugging). Null when blocked.</param>
public sealed record AnalysisCallInfo(
    string PromptId,
    string PromptVersion,
    AnalysisJob Job,
    string Model,
    bool FromCache,
    int Attempts,
    AnalysisUsage Usage,
    TimeSpan Latency,
    double EstimatedCostUsd,
    string? RawText);

/// <summary>Token counts as reported by the Interactions API (<c>usage.total_*_tokens</c>).
/// <see cref="OutputTokens"/> excludes thinking; thinking is billed at the output rate.</summary>
public sealed record AnalysisUsage(int InputTokens, int OutputTokens, int ThoughtTokens, int CachedTokens)
{
    public static readonly AnalysisUsage None = new(0, 0, 0, 0);

    public int Total => InputTokens + OutputTokens + ThoughtTokens;

    public static AnalysisUsage operator +(AnalysisUsage a, AnalysisUsage b) => new(
        a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens,
        a.ThoughtTokens + b.ThoughtTokens, a.CachedTokens + b.CachedTokens);
}

/// <summary>Serializer settings for model output: schema property names are snake_case, C# records are PascalCase.</summary>
public static class AnalysisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };
}
