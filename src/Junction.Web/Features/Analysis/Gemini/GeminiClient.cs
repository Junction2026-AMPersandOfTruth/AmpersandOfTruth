using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Junction.Web.Features.Analysis.Gemini;

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
/// <remarks>TASK gemini-transport implements this.</remarks>
public sealed class GeminiClient(
    HttpClient http,
    IOptionsMonitor<GeminiOptions> options,
    TimeProvider time,
    ILogger<GeminiClient> log)
{
    public Task<GeminiReply> SendAsync(GeminiCall call, CancellationToken ct = default) =>
        throw new NotImplementedException("gemini-transport");
}
