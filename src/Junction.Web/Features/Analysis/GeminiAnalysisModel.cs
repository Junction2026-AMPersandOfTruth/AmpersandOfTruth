using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Junction.Web.Features.Analysis.Gemini;
using Junction.Web.Features.Analysis.Prompts;
using Junction.Web.Features.Analysis.Schema;
using Junction.Web.Features.Analysis.Store;
using Microsoft.Extensions.Options;

namespace Junction.Web.Features.Analysis;

/// <summary>
/// <see cref="IAnalysisModel"/> over Gemini. Per call:
/// render prompt → cache lookup → budget reservation → global concurrency limit → <see cref="GeminiClient"/>
/// (transport retries) → one retry with a larger token limit if truncated → schema validation + typed deserialization
/// → up to <see cref="GeminiOptions.RepairAttempts"/> repair calls → cache (Ok/Blocked) → ledger row + running-total log line.
/// Singleton; thread-safe. Cache and ledger failures are logged and never fail the call.
/// </summary>
public sealed class GeminiAnalysisModel : IAnalysisModel
{
    private const int MaxRetryOutputTokens = 32_768; // Gemini 4 Carbon/Argon output limit; Hydrogen allows 65,536.
    private const int RawTextLimitForRepair = 8_000;

    private readonly PromptLibrary _prompts;
    private readonly GeminiClient _gemini;
    private readonly AnalysisCache _cache;
    private readonly AnalysisLedger _ledger;
    private readonly AnalysisBudget _budget;
    private readonly ModelPricing _pricing;
    private readonly IOptionsMonitor<GeminiOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GeminiAnalysisModel> _log;
    private readonly SemaphoreSlim _limiter;
    private long _liveTokens;

    public GeminiAnalysisModel(
        PromptLibrary prompts, GeminiClient gemini, AnalysisCache cache, AnalysisLedger ledger, AnalysisBudget budget,
        ModelPricing pricing, IOptionsMonitor<GeminiOptions> options, TimeProvider time, ILogger<GeminiAnalysisModel> log)
    {
        (_prompts, _gemini, _cache, _ledger, _budget, _pricing, _options, _time, _log) =
            (prompts, gemini, cache, ledger, budget, pricing, options, time, log);
        _limiter = new SemaphoreSlim(Math.Max(1, options.CurrentValue.MaxConcurrency));
    }

    public async Task<AnalysisResult<T>> RunAsync<T>(AnalysisRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var o = _options.CurrentValue;
        var rendered = _prompts.Render(request.PromptId, request.Variables); // programmer errors throw here
        var def = rendered.Definition;
        var job = request.Job ?? def.Job;
        var model = string.IsNullOrWhiteSpace(request.Model) ? o.ModelFor(job) : request.Model;
        var schema = request.Schema ?? def.Schema;
        var key = CacheKey(rendered, model, schema);
        var started = _time.GetTimestamp();
        var ctx = new CallContext(request, def, job, model, started);

        if (o.CacheEnabled && request.Cache == CachePolicy.ReadWrite
            && await TryCacheGetAsync(key, ct) is { } hit
            && FromCache<T>(hit, ctx) is { } cached)
        {
            await RecordAsync(ctx, cached, ct);
            return cached;
        }

        var decision = await _budget.TryReserveAsync(request.Scope, ct);
        if (!decision.Allowed)
        {
            _log.LogWarning("Gemini {PromptId} not called: {Reason}", def.Id, decision.Reason);
            var denied = Result<T>(ctx, AnalysisOutcome.BudgetExceeded, default, AnalysisUsage.None, 0, null, decision.Reason);
            await RecordAsync(ctx, denied, ct);
            return denied;
        }

        AnalysisResult<T> result;
        await _limiter.WaitAsync(ct);
        try
        {
            result = await CallAsync<T>(ctx, rendered, schema, o, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Gemini {PromptId} on {Model} failed unexpectedly", def.Id, model);
            result = Result<T>(ctx, AnalysisOutcome.Failed, default, AnalysisUsage.None, 0, null, $"unexpected: {ex.Message}");
        }
        finally
        {
            _limiter.Release();
        }

        _budget.AddCost(result.Info.EstimatedCostUsd);
        if (o.CacheEnabled && request.Cache != CachePolicy.Bypass && result.Outcome is AnalysisOutcome.Ok or AnalysisOutcome.Blocked)
            await TryCachePutAsync(key, ctx, result, ct);
        await RecordAsync(ctx, result, ct);
        LogLive(ctx, result);
        return result;
    }

    private async Task<AnalysisResult<T>> CallAsync<T>(
        CallContext ctx, RenderedPrompt rendered, JsonNode schema, GeminiOptions o, CancellationToken ct)
    {
        var s = ctx.Definition.Settings;
        var call = new GeminiCall(ctx.Model, rendered.User)
        {
            SystemInstruction = string.IsNullOrWhiteSpace(rendered.System) ? null : rendered.System,
            ResponseSchema = schema,
            Temperature = s.Temperature,
            MaxOutputTokens = s.MaxOutputTokens,
            ThinkingLevel = s.ThinkingLevel,
        };

        var usage = AnalysisUsage.None;
        var attempts = 0;
        async Task<GeminiReply> Send(GeminiCall c)
        {
            var r = await _gemini.SendAsync(c, ct);
            usage += r.Usage;
            attempts += r.Attempts;
            return r;
        }

        var reply = await Send(call);
        if (reply.Status == GeminiStatus.Incomplete)
        {
            var bigger = Math.Min(MaxRetryOutputTokens, Math.Max(8_192, (call.MaxOutputTokens ?? 8_192) * 2));
            _log.LogInformation("Gemini {PromptId} on {Model} truncated; retrying with max_output_tokens={Max}",
                ctx.Definition.Id, ctx.Model, bigger);
            call = call with { MaxOutputTokens = bigger };
            reply = await Send(call);
        }

        switch (reply.Status)
        {
            case GeminiStatus.Blocked:
                return Result<T>(ctx, AnalysisOutcome.Blocked, default, usage, attempts, null, Describe(reply));
            case GeminiStatus.Failed:
                return Result<T>(ctx, AnalysisOutcome.Failed, default, usage, attempts, reply.Text, Describe(reply));
            case GeminiStatus.Incomplete:
                return Result<T>(ctx, AnalysisOutcome.Truncated, default, usage, attempts, reply.Text,
                    $"output truncated at max_output_tokens={call.MaxOutputTokens}");
        }

        var (value, errors) = Validate<T>(schema, reply.Text);
        for (var repair = 0; errors.Count > 0 && repair < o.RepairAttempts; repair++)
        {
            _log.LogInformation("Gemini {PromptId} on {Model} returned invalid output ({Errors}); repair attempt {N}",
                ctx.Definition.Id, ctx.Model, string.Join("; ", errors.Take(3)), repair + 1);
            var repaired = await Send(call with { Input = RepairInput(rendered.User, reply.Text, errors) });
            if (repaired.Status != GeminiStatus.Completed)
                return Result<T>(ctx, repaired.Status == GeminiStatus.Blocked ? AnalysisOutcome.Blocked : AnalysisOutcome.Invalid,
                    default, usage, attempts, reply.Text,
                    $"repair call ended {repaired.Status}: {Describe(repaired)}; original problems: {string.Join("; ", errors.Take(5))}");
            reply = repaired;
            (value, errors) = Validate<T>(schema, reply.Text);
        }

        return errors.Count > 0
            ? Result<T>(ctx, AnalysisOutcome.Invalid, default, usage, attempts, reply.Text, string.Join("; ", errors.Take(5)))
            : Result<T>(ctx, AnalysisOutcome.Ok, value, usage, attempts, reply.Text);
    }

    /// <summary>Parse → schema-validate → deserialize. Any failure becomes an error string for the repair prompt.</summary>
    internal static (T? Value, IReadOnlyList<string> Errors) Validate<T>(JsonNode schema, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (default, ["output is empty"]);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(StripCodeFence(text));
        }
        catch (JsonException ex)
        {
            return (default, [$"output is not valid JSON: {ex.Message}"]);
        }

        var errors = JsonSchemaValidator.Validate(schema, node);
        if (errors.Count > 0)
            return (default, errors);

        try
        {
            var value = node.Deserialize<T>(AnalysisJson.Options);
            return value is null ? (default, ["output deserialized to null"]) : (value, []);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return (default, [$"output does not fit {typeof(T).Name}: {ex.Message}"]);
        }
    }

    private static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;
        var firstNewline = t.IndexOf('\n');
        var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? t[(firstNewline + 1)..lastFence].Trim() : t;
    }

    internal static string RepairInput(string originalUser, string? previous, IReadOnlyList<string> errors)
    {
        var prev = previous ?? "";
        if (prev.Length > RawTextLimitForRepair) prev = prev[..RawTextLimitForRepair] + " …[cut]";
        var sb = new StringBuilder(originalUser.Length + prev.Length + 512)
            .AppendLine(originalUser)
            .AppendLine()
            .AppendLine("---")
            .AppendLine("Your previous reply did not satisfy the required JSON schema.")
            .AppendLine("<previous_reply>")
            .AppendLine(prev)
            .AppendLine("</previous_reply>")
            .AppendLine("Problems:");
        foreach (var e in errors.Take(10)) sb.Append("- ").AppendLine(e);
        sb.Append("Reply again with only the corrected JSON object, following the same instructions.");
        return sb.ToString();
    }

    /// <summary>SHA-256 over everything that can change the output: prompt id + full version (file content hash),
    /// model, effective schema, generation settings and the fully rendered system + user text.</summary>
    internal static string CacheKey(RenderedPrompt rendered, string model, JsonNode schema)
    {
        var d = rendered.Definition;
        var s = d.Settings;
        var canonical = string.Join('\n',
            "analysis-cache-v1", d.Id, d.FullVersion, model, schema.ToJsonString(),
            string.Create(CultureInfo.InvariantCulture, $"{s.Temperature}|{s.MaxOutputTokens}|{s.ThinkingLevel}"),
            rendered.System, "␞", rendered.User);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private AnalysisResult<T>? FromCache<T>(AnalysisCacheEntry hit, CallContext ctx)
    {
        var usage = new AnalysisUsage(hit.InputTokens, hit.OutputTokens, hit.ThoughtTokens, 0);
        if (hit.Outcome == AnalysisOutcome.Blocked)
            return Result<T>(ctx, AnalysisOutcome.Blocked, default, usage, 0, null, hit.Error, fromCache: true);
        if (hit.Outcome != AnalysisOutcome.Ok || hit.ResponseJson is null)
            return null;
        try
        {
            var value = JsonSerializer.Deserialize<T>(hit.ResponseJson, AnalysisJson.Options);
            return value is null ? null
                : Result<T>(ctx, AnalysisOutcome.Ok, value, usage, 0, hit.ResponseJson, fromCache: true);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            _log.LogInformation("Cached {PromptId} entry no longer fits {Type}; calling the model", ctx.Definition.Id, typeof(T).Name);
            return null; // the caller's type changed since the entry was written → treat as a miss
        }
    }

    private AnalysisResult<T> Result<T>(CallContext ctx, AnalysisOutcome outcome, T? value, AnalysisUsage usage,
        int attempts, string? raw, string? error = null, bool fromCache = false)
    {
        var cost = fromCache ? 0 : _pricing.EstimateUsd(ctx.Model, usage);
        var info = new AnalysisCallInfo(ctx.Definition.Id, ctx.Definition.FullVersion, ctx.Job, ctx.Model, fromCache,
            attempts, usage, _time.GetElapsedTime(ctx.Started), cost, raw);
        return new AnalysisResult<T>(outcome, value, info, error);
    }

    private static string Describe(GeminiReply r) =>
        $"{r.ErrorCode ?? r.Status.ToString().ToLowerInvariant()}{(r.HttpStatus is { } h ? $" (HTTP {h})" : "")}" +
        (string.IsNullOrWhiteSpace(r.ErrorMessage) ? "" : $": {r.ErrorMessage}");

    private async Task<AnalysisCacheEntry?> TryCacheGetAsync(string key, CancellationToken ct)
    {
        try { return await _cache.GetAsync(key, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Analysis cache read failed; calling the model");
            return null;
        }
    }

    private async Task TryCachePutAsync<T>(string key, CallContext ctx, AnalysisResult<T> result, CancellationToken ct)
    {
        try
        {
            await _cache.PutAsync(new AnalysisCacheEntry
            {
                Key = key,
                PromptId = ctx.Definition.Id,
                PromptVersion = ctx.Definition.FullVersion,
                Model = ctx.Model,
                Outcome = result.Outcome,
                ResponseJson = result.IsOk ? JsonSerializer.Serialize(result.Value, AnalysisJson.Options) : null,
                Error = result.Error,
                InputTokens = result.Info.Usage.InputTokens,
                OutputTokens = result.Info.Usage.OutputTokens,
                ThoughtTokens = result.Info.Usage.ThoughtTokens,
                CreatedAt = _time.GetUtcNow(),
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Analysis cache write failed for {PromptId}", ctx.Definition.Id);
        }
    }

    private async Task RecordAsync<T>(CallContext ctx, AnalysisResult<T> result, CancellationToken ct)
    {
        var i = result.Info;
        try
        {
            await _ledger.RecordAsync(new AnalysisCallRecord
            {
                At = _time.GetUtcNow(),
                Scope = ctx.Request.Scope,
                PromptId = i.PromptId,
                PromptVersion = i.PromptVersion,
                Model = i.Model,
                Job = i.Job,
                Outcome = result.Outcome,
                FromCache = i.FromCache,
                Attempts = i.Attempts,
                InputTokens = i.Usage.InputTokens,
                OutputTokens = i.Usage.OutputTokens,
                ThoughtTokens = i.Usage.ThoughtTokens,
                CachedTokens = i.Usage.CachedTokens,
                LatencyMs = (long)i.Latency.TotalMilliseconds,
                EstimatedCostUsd = i.EstimatedCostUsd,
                Error = result.Error,
            }, CancellationToken.None); // record even if the caller cancels right after the call
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Analysis ledger write failed for {PromptId}", i.PromptId);
        }
    }

    private void LogLive<T>(CallContext ctx, AnalysisResult<T> r)
    {
        var u = r.Info.Usage;
        var total = Interlocked.Add(ref _liveTokens, u.Total);
        var (callsToday, costToday) = _budget.Today;
        _log.LogInformation(
            "Gemini {PromptId}@{Version} {Model} → {Outcome} in {LatencyMs:F0} ms, {Attempts} attempt(s), tokens in {In} / out {Out} / thought {Thought}, ~${Cost:F4}, scope {Scope} · running: {Total} tokens since start, {CallsToday} live calls ~${CostToday:F2} today",
            ctx.Definition.Id, ctx.Definition.FullVersion, ctx.Model, r.Outcome, r.Info.Latency.TotalMilliseconds, r.Info.Attempts,
            u.InputTokens, u.OutputTokens, u.ThoughtTokens, r.Info.EstimatedCostUsd, ctx.Request.Scope ?? "-",
            total, callsToday, costToday);
    }

    private sealed record CallContext(AnalysisRequest Request, PromptDefinition Definition, AnalysisJob Job, string Model, long Started);
}
