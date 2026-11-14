namespace Junction.Web.Features.Analysis;

/// <summary>
/// Config section <c>Gemini</c>. Non-secret defaults live in <c>appsettings.json</c>.
/// The key is <b>only</b> ever in user-secrets (<c>Gemini:ApiKey</c>) or the environment (<c>Gemini__ApiKey</c>) —
/// never in appsettings, the repo, logs or screenshots. The app never reads <c>~/.gemini/.env</c> (that key is the coding workers').
/// </summary>
public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    /// <summary>Secret. Missing key → every call returns <see cref="AnalysisOutcome.Failed"/> ("no_api_key"); the app still starts.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Interactions API base; the client POSTs to <c>{BaseUrl}interactions</c>.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>Optional <c>Api-Revision</c> header value (e.g. <c>2026-05-20</c>). Null = not sent (server default).</summary>
    public string? ApiRevision { get; set; }

    /// <summary>Model id per <see cref="AnalysisJob"/> name, e.g. <c>"Classify": "gemini-4-hydrogen"</c>.</summary>
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Used when a job has no entry in <see cref="Models"/>.</summary>
    public string DefaultModel { get; set; } = "gemini-4-hydrogen";

    /// <summary>Interactions API <c>safety_settings</c>: category → threshold
    /// (<c>hate_speech|dangerous_content|harassment|sexually_explicit|jailbreak</c> →
    /// <c>off|block_only_high|block_medium_and_above|block_low_and_above</c>). We analyse hostile content, we don't generate it.
    /// Empty = field omitted.</summary>
    public Dictionary<string, string> SafetySettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>USD per 1M tokens, by model id. Used for the estimated-cost column and the daily cost cap.</summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Max in-flight HTTP calls across the whole app (all circuits, all investigations).</summary>
    public int MaxConcurrency { get; set; } = 16;

    /// <summary>Total HTTP attempts per call for retryable failures (429, 500, 502, 503, 504, timeouts, network errors).</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Exponential backoff base (full jitter): delay_n = rand(0, min(Max, Base·2^(n-1))). Retry-After wins when present (capped at Max).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per HTTP attempt. Gemini 4 Argon took 35 s for a tiny prompt in testing — keep this generous.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Extra calls allowed to fix schema-invalid output (the issue asks for exactly one).</summary>
    public int RepairAttempts { get; set; } = 1;

    /// <summary>Live (non-cached) calls per UTC day, whole app. Tier 3 has no practical rate limit — this is the spend guard.</summary>
    public int DailyCallCap { get; set; } = 20_000;

    /// <summary>Estimated USD per UTC day, whole app (0 = no cost cap).</summary>
    public double DailyCostCapUsd { get; set; } = 150;

    /// <summary>Live calls per <see cref="AnalysisRequest.Scope"/> (one investigation / experiment run).</summary>
    public int PerScopeCallCap { get; set; } = 1_500;

    /// <summary>Optional directory with extra/overriding <c>*.md</c> + <c>*.schema.json</c> prompt files (experiments iterate without a rebuild).</summary>
    public string? PromptDirectory { get; set; }

    public bool CacheEnabled { get; set; } = true;

    public string ModelFor(AnalysisJob job) =>
        Models.TryGetValue(job.ToString(), out var model) && !string.IsNullOrWhiteSpace(model) ? model : DefaultModel;
}

/// <summary>USD per 1M tokens. Thinking tokens are billed at <see cref="OutputPerMTok"/>.</summary>
public sealed class ModelPrice
{
    public double InputPerMTok { get; set; }
    public double OutputPerMTok { get; set; }
    public double CachedInputPerMTok { get; set; }
}
