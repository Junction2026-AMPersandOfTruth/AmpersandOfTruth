using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Analysis.Gemini;

/// <summary>One Interactions API call. Null fields are omitted from the request.</summary>
/// <param name="Input">The user turn as plain text (sent as the string form of <c>input</c>).</param>
public sealed record GeminiCall(string Model, string Input)
{
    public string? SystemInstruction { get; init; }

    /// <summary>JSON Schema for structured output. Null = free text (no <c>response_format</c>).</summary>
    public JsonNode? ResponseSchema { get; init; }

    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }

    /// <summary><c>low|medium|high|xhigh</c>. Gemini 4 Hydrogen/Carbon reject <c>minimal</c> with HTTP 400.</summary>
    public string? ThinkingLevel { get; init; }
}

public enum GeminiStatus
{
    /// <summary><c>status: completed</c> and at least one <c>model_output</c> text block.</summary>
    Completed,
    /// <summary><c>status: incomplete</c> — hit <c>max_output_tokens</c>. <see cref="GeminiReply.Text"/> may be partial or null.</summary>
    Incomplete,
    /// <summary>Safety / jailbreak / prohibited_content / blocklist refusal (HTTP 400 or <c>status: failed</c>).</summary>
    Blocked,
    /// <summary>Anything else, after retries: HTTP errors, timeouts, empty output, unparseable body, missing API key.</summary>
    Failed,
}

/// <param name="Usage">Summed over all HTTP attempts that returned a usage block.</param>
public sealed record GeminiReply(GeminiStatus Status, string? Text, AnalysisUsage Usage)
{
    /// <summary>HTTP attempts made (0 when no request was sent, e.g. missing API key).</summary>
    public int Attempts { get; init; }

    /// <summary>Status code of the last HTTP response, if any.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Gemini <c>error.code</c> (e.g. <c>safety</c>, <c>invalid_request</c>) or a client code:
    /// <c>no_api_key</c>, <c>timeout</c>, <c>network</c>, <c>empty_output</c>, <c>bad_response</c>.</summary>
    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Wall-clock time including retries and backoff.</summary>
    public TimeSpan Latency { get; init; }
}

/// <summary>
/// Thin REST client for <c>POST {BaseUrl}interactions</c> (Gemini Developer API, Interactions API, stateless:
/// <c>store=false</c>). Owns the wire format, error classification and transport retries. Knows nothing about
/// prompts, caching or budgets. Singleton; thread-safe.
/// </summary>
public sealed class GeminiClient(
    HttpClient http,
    IOptionsMonitor<GeminiOptions> options,
    TimeProvider time,
    ILogger<GeminiClient> log)
{
    private static readonly HashSet<string> BlockCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "safety",
        "jailbreak",
        "prohibited_content",
        "blocklist",
    };

    public async Task<GeminiReply> SendAsync(GeminiCall call, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ct.ThrowIfCancellationRequested();

        var startTimestamp = time.GetTimestamp();
        var opt = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(opt.ApiKey))
        {
            return new GeminiReply(GeminiStatus.Failed, null, AnalysisUsage.None)
            {
                Attempts = 0,
                ErrorCode = "no_api_key",
                ErrorMessage = "Gemini API key is not configured.",
                Latency = time.GetElapsedTime(startTimestamp),
            };
        }

        var url = BuildInteractionsUrl(opt.BaseUrl);
        var requestBytes = SerializeRequest(call, opt);
        var maxAttempts = Math.Max(1, opt.MaxAttempts);

        var totalUsage = AnalysisUsage.None;
        int? lastHttpStatus = null;
        string? lastErrorCode = null;
        string? lastErrorMessage = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var attemptStart = time.GetTimestamp();
            TimeSpan? retryAfter = null;
            bool shouldRetry;

            try
            {
                using var request = CreateHttpRequest(url, opt, requestBytes);
                using var timeoutCts = CreateTimeoutCts(opt.RequestTimeout, time);
                using var linkedCts = timeoutCts is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
                    : null;
                var attemptCt = linkedCts?.Token ?? ct;

                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    attemptCt);

                var httpStatus = (int)response.StatusCode;
                lastHttpStatus = httpStatus;
                retryAfter = ParseRetryAfter(response.Headers, time);

                var rawBody = await response.Content.ReadAsStringAsync(attemptCt);
                var parsed = InterpretResponse(httpStatus, response.IsSuccessStatusCode, rawBody);

                totalUsage += parsed.Usage;

                var attemptElapsed = time.GetElapsedTime(attemptStart);
                log.LogDebug(
                    "Gemini attempt {Attempt}/{MaxAttempts} model={Model} http={HttpStatus} status={Status} latencyMs={LatencyMs:F0} tokens={Tokens}",
                    attempt,
                    maxAttempts,
                    call.Model,
                    httpStatus,
                    parsed.Status,
                    attemptElapsed.TotalMilliseconds,
                    parsed.Usage.Total);

                if (!parsed.IsRetryable || attempt == maxAttempts)
                {
                    return new GeminiReply(parsed.Status, parsed.Text, totalUsage)
                    {
                        Attempts = attempt,
                        HttpStatus = lastHttpStatus,
                        ErrorCode = parsed.ErrorCode,
                        ErrorMessage = parsed.ErrorMessage,
                        Latency = time.GetElapsedTime(startTimestamp),
                    };
                }

                lastErrorCode = parsed.ErrorCode;
                lastErrorMessage = parsed.ErrorMessage;
                shouldRetry = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                var attemptElapsed = time.GetElapsedTime(attemptStart);
                lastErrorCode = "timeout";
                lastErrorMessage = $"Request timed out after {opt.RequestTimeout}.";
                shouldRetry = attempt < maxAttempts;

                log.LogDebug(
                    "Gemini attempt {Attempt}/{MaxAttempts} model={Model} http={HttpStatus} status={Status} latencyMs={LatencyMs:F0} tokens=0",
                    attempt,
                    maxAttempts,
                    call.Model,
                    (int?)null,
                    "timeout",
                    attemptElapsed.TotalMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                ct.ThrowIfCancellationRequested();

                var attemptElapsed = time.GetElapsedTime(attemptStart);
                lastErrorCode = "network";
                lastErrorMessage = ex.Message;
                shouldRetry = attempt < maxAttempts;

                log.LogDebug(
                    "Gemini attempt {Attempt}/{MaxAttempts} model={Model} http={HttpStatus} status={Status} latencyMs={LatencyMs:F0} tokens=0",
                    attempt,
                    maxAttempts,
                    call.Model,
                    (int?)null,
                    "network",
                    attemptElapsed.TotalMilliseconds);
            }

            if (!shouldRetry)
            {
                break;
            }

            var delay = ComputeRetryDelay(attempt, retryAfter, opt);
            log.LogWarning(
                "Gemini retry {Retry} model={Model} reason={Reason} delayMs={DelayMs:F0}",
                attempt,
                call.Model,
                lastErrorCode,
                delay.TotalMilliseconds);

            await Task.Delay(delay, time, ct);
        }

        return new GeminiReply(GeminiStatus.Failed, null, totalUsage)
        {
            Attempts = maxAttempts,
            HttpStatus = lastHttpStatus,
            ErrorCode = lastErrorCode,
            ErrorMessage = lastErrorMessage,
            Latency = time.GetElapsedTime(startTimestamp),
        };
    }

    private static string BuildInteractionsUrl(string baseUrl) =>
        baseUrl.EndsWith('/') ? $"{baseUrl}interactions" : $"{baseUrl}/interactions";

    private static HttpRequestMessage CreateHttpRequest(string url, GeminiOptions opt, byte[] bodyBytes)
    {
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("x-goog-api-key", opt.ApiKey);
        if (!string.IsNullOrWhiteSpace(opt.ApiRevision))
        {
            request.Headers.TryAddWithoutValidation("Api-Revision", opt.ApiRevision);
        }

        return request;
    }

    private static byte[] SerializeRequest(GeminiCall call, GeminiOptions opt)
    {
        var body = new JsonObject();
        if (call.Model is not null)
        {
            body["model"] = call.Model;
        }
        body["store"] = false;
        if (call.Input is not null)
        {
            body["input"] = call.Input;
        }
        if (call.SystemInstruction is not null)
        {
            body["system_instruction"] = call.SystemInstruction;
        }

        var genConfig = new JsonObject();
        if (call.Temperature is { } temperature)
        {
            genConfig["temperature"] = temperature;
        }
        if (call.MaxOutputTokens is { } maxOutputTokens)
        {
            genConfig["max_output_tokens"] = maxOutputTokens;
        }
        if (call.ThinkingLevel is not null)
        {
            genConfig["thinking_level"] = call.ThinkingLevel;
        }
        if (genConfig.Count > 0)
        {
            body["generation_config"] = genConfig;
        }

        if (call.ResponseSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "text",
                ["mime_type"] = "application/json",
                ["schema"] = call.ResponseSchema.DeepClone(),
            };
        }

        if (opt.SafetySettings is { Count: > 0 })
        {
            var safety = new JsonArray();
            foreach (var (type, threshold) in opt.SafetySettings)
            {
                if (threshold is not null)
                {
                    safety.Add(new JsonObject
                    {
                        ["type"] = type,
                        ["threshold"] = threshold,
                    });
                }
            }
            if (safety.Count > 0)
            {
                body["safety_settings"] = safety;
            }
        }

        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    private static CancellationTokenSource? CreateTimeoutCts(TimeSpan requestTimeout, TimeProvider time)
    {
        if (requestTimeout == Timeout.InfiniteTimeSpan || requestTimeout.TotalMilliseconds > (uint.MaxValue - 2))
        {
            return null;
        }

        if (requestTimeout <= TimeSpan.Zero)
        {
            var expired = new CancellationTokenSource();
            expired.Cancel();
            return expired;
        }

        return new CancellationTokenSource(requestTimeout, time);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseHeaders headers, TimeProvider time)
    {
        if (headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta;
            }
            if (retryAfter.Date is { } date)
            {
                return date - time.GetUtcNow();
            }
        }

        if (headers.TryGetValues("Retry-After", out var values) && values.FirstOrDefault() is { } raw)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                return parsedDate - time.GetUtcNow();
            }
        }

        return null;
    }

    private static TimeSpan ComputeRetryDelay(int retryNumber, TimeSpan? retryAfter, GeminiOptions opt)
    {
        var maxDelay = opt.MaxRetryDelay > TimeSpan.Zero ? opt.MaxRetryDelay : TimeSpan.Zero;

        if (retryAfter is { } serverDelay)
        {
            if (serverDelay <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }
            return serverDelay <= maxDelay ? serverDelay : maxDelay;
        }

        if (opt.RetryBaseDelay <= TimeSpan.Zero || maxDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var capMs = Math.Min(
            maxDelay.TotalMilliseconds,
            opt.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(retryNumber - 1, 30)));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capMs);
    }

    private static bool IsRetryableHttpStatus(int httpStatus) =>
        httpStatus is 429 or 500 or 502 or 503 or 504;

    private static bool IsBlock(string? code, string? message) =>
        (code is not null && BlockCodes.Contains(code))
        || (message is not null && message.StartsWith("Input blocked", StringComparison.OrdinalIgnoreCase));

    private static ParsedResponse InterpretResponse(int httpStatus, bool isSuccessStatusCode, string rawBody)
    {
        if (!isSuccessStatusCode)
        {
            return InterpretErrorResponse(httpStatus, rawBody);
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new ParsedResponse(GeminiStatus.Failed, null, AnalysisUsage.None, "bad_response", "Empty response body.", false);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return new ParsedResponse(GeminiStatus.Failed, null, AnalysisUsage.None, "bad_response", "Response body was not valid JSON.", false);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ParsedResponse(GeminiStatus.Failed, null, AnalysisUsage.None, "bad_response", "Response body was not a JSON object.", false);
            }

            var root = doc.RootElement;
            var usage = ParseUsage(root);
            var status = GetStringOrNull(root, "status");
            var text = ExtractModelOutputText(root);

            return status switch
            {
                "completed" when !string.IsNullOrEmpty(text) => new ParsedResponse(
                    GeminiStatus.Completed,
                    text,
                    usage,
                    null,
                    null,
                    false),

                "completed" => new ParsedResponse(
                    GeminiStatus.Failed,
                    null,
                    usage,
                    "empty_output",
                    "Interaction completed without any model_output text.",
                    false),

                "incomplete" => new ParsedResponse(
                    GeminiStatus.Incomplete,
                    string.IsNullOrEmpty(text) ? null : text,
                    usage,
                    null,
                    null,
                    false),

                "failed" => InterpretFailedInteraction(root, usage),

                _ => new ParsedResponse(
                    GeminiStatus.Failed,
                    null,
                    usage,
                    "bad_response",
                    status is null ? "Interaction response missing status." : $"Unexpected interaction status '{status}'.",
                    false),
            };
        }
    }

    private static ParsedResponse InterpretErrorResponse(int httpStatus, string rawBody)
    {
        var retryableHttp = IsRetryableHttpStatus(httpStatus);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new ParsedResponse(GeminiStatus.Failed, null, AnalysisUsage.None, $"http_{httpStatus}", null, retryableHttp);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return new ParsedResponse(GeminiStatus.Failed, null, AnalysisUsage.None, "bad_response", "Error response body was not valid JSON.", retryableHttp);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ParsedResponse(GeminiStatus.Failed, null, AnalysisUsage.None, "bad_response", "Error response body was not a JSON object.", retryableHttp);
            }

            var root = doc.RootElement;
            var usage = ParseUsage(root);

            if (TryReadErrorObject(root, out var code, out var message))
            {
                if (IsBlock(code, message))
                {
                    return new ParsedResponse(
                        GeminiStatus.Blocked,
                        null,
                        usage,
                        !string.IsNullOrWhiteSpace(code) ? code : "safety",
                        message,
                        false);
                }

                return new ParsedResponse(
                    GeminiStatus.Failed,
                    null,
                    usage,
                    !string.IsNullOrWhiteSpace(code) ? code : $"http_{httpStatus}",
                    message,
                    retryableHttp);
            }

            return new ParsedResponse(GeminiStatus.Failed, null, usage, $"http_{httpStatus}", null, retryableHttp);
        }
    }

    private static ParsedResponse InterpretFailedInteraction(JsonElement root, AnalysisUsage usage)
    {
        string? firstCode = null;
        string? firstMessage = null;
        var hasAnyError = false;

        if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var err in errorsEl.EnumerateArray())
            {
                if (err.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var code = GetStringOrNull(err, "code");
                var message = GetStringOrNull(err, "message");

                if (!hasAnyError)
                {
                    firstCode = code;
                    firstMessage = message;
                    hasAnyError = true;
                }

                if (IsBlock(code, message))
                {
                    return new ParsedResponse(
                        GeminiStatus.Blocked,
                        null,
                        usage,
                        !string.IsNullOrWhiteSpace(code) ? code : "safety",
                        message,
                        false);
                }
            }
        }
        else if (TryReadErrorObject(root, out var singleCode, out var singleMessage))
        {
            if (IsBlock(singleCode, singleMessage))
            {
                return new ParsedResponse(
                    GeminiStatus.Blocked,
                    null,
                    usage,
                    !string.IsNullOrWhiteSpace(singleCode) ? singleCode : "safety",
                    singleMessage,
                    false);
            }

            firstCode = singleCode;
            firstMessage = singleMessage;
            hasAnyError = true;
        }

        return new ParsedResponse(
            GeminiStatus.Failed,
            null,
            usage,
            hasAnyError && !string.IsNullOrWhiteSpace(firstCode) ? firstCode : "bad_response",
            firstMessage ?? "Interaction returned status 'failed'.",
            false);
    }

    private static bool TryReadErrorObject(JsonElement root, out string? code, out string? message)
    {
        if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
        {
            code = GetStringOrNull(errorEl, "code");
            message = GetStringOrNull(errorEl, "message");
            return true;
        }

        if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var err in errorsEl.EnumerateArray())
            {
                if (err.ValueKind == JsonValueKind.Object)
                {
                    code = GetStringOrNull(err, "code");
                    message = GetStringOrNull(err, "message");
                    return true;
                }
            }
        }

        code = null;
        message = null;
        return false;
    }

    private static AnalysisUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
        {
            return AnalysisUsage.None;
        }

        return new AnalysisUsage(
            InputTokens: GetIntOrZero(u, "total_input_tokens"),
            OutputTokens: GetIntOrZero(u, "total_output_tokens"),
            ThoughtTokens: GetIntOrZero(u, "total_thought_tokens"),
            CachedTokens: GetIntOrZero(u, "total_cached_tokens"));
    }

    private static string? ExtractModelOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        StringBuilder? sb = null;
        foreach (var step in steps.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object
                || GetStringOrNull(step, "type") != "model_output"
                || !step.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || GetStringOrNull(item, "type") != "text")
                {
                    continue;
                }

                var piece = GetStringOrNull(item, "text");
                if (!string.IsNullOrEmpty(piece))
                {
                    sb ??= new StringBuilder();
                    sb.Append(piece);
                }
            }
        }

        return sb?.ToString();
    }

    private static string? GetStringOrNull(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int GetIntOrZero(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : 0;

    private readonly record struct ParsedResponse(
        GeminiStatus Status,
        string? Text,
        AnalysisUsage Usage,
        string? ErrorCode,
        string? ErrorMessage,
        bool IsRetryable);
}
