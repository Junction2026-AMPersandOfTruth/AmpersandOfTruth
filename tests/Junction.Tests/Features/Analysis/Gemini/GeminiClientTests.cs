using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Gemini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Junction.Tests.Features.Analysis.Gemini;

public sealed class GeminiClientTests
{
    private const string RealCompletedJson =
        """{"status":"completed","usage":{"total_tokens":681,"total_input_tokens":369,"total_cached_tokens":0,"total_output_tokens":61,"total_tool_use_tokens":0,"total_thought_tokens":251,"initial_input_tokens":369,"input_tokens_by_modality":[{"modality":"TEXT","tokens":369}]},"created":"2026-11-14T09:30:39Z","service_tier":"standard","object":"interaction","model":"gemini-4-hydrogen","steps":[{"type":"thought","signature":"ErMI...","content":[{"type":"text","text":"internal reasoning"}]},{"type":"model_output","content":[{"type":"text","text":"{\n \"category\": \"vandalism\"\n}"}]}]}""";

    private const string RealInvalidRequest400Json = """
        {"error":{"message":"Thinking level THINKING_LEVEL_MINIMAL is not supported for this model. Please retry with other thinking level.","code":"invalid_request"}}
        """;

    private static GeminiOptions DefaultOptions(Action<GeminiOptions>? configure = null)
    {
        var opts = new GeminiOptions
        {
            ApiKey = "test-secret-api-key-999",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
            MaxAttempts = 4,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(120),
        };
        configure?.Invoke(opts);
        return opts;
    }

    private static (GeminiClient Client, StubHttpHandler Handler, FakeTimeProvider Time, CapturingLogger Logger) CreateSut(
        Action<GeminiOptions>? configure = null)
    {
        var handler = new StubHttpHandler();
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var opts = new StaticOptionsMonitor(DefaultOptions(configure));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero));
        var logger = new CapturingLogger();
        var client = new GeminiClient(http, opts, time, logger);
        return (client, handler, time, logger);
    }

    [Fact]
    public async Task Request_serializes_full_wire_format_and_headers()
    {
        var (client, handler, _, _) = CreateSut(o =>
        {
            o.ApiRevision = "2026-05-20";
            o.SafetySettings = new Dictionary<string, string>
            {
                ["hate_speech"] = "off",
                ["harassment"] = "block_only_high",
            };
        });
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        // Attach schema inside a parent object first to verify DeepClone() prevents parent collision.
        var parentWrapper = JsonNode.Parse("""{"nested":{"type":"object","properties":{"category":{"type":"string"}}}}""")!;
        var schemaNode = parentWrapper["nested"]!;

        var call = new GeminiCall("gemini-4-hydrogen", "Classify this diff")
        {
            SystemInstruction = "You are an editor assistant.",
            Temperature = 0,
            MaxOutputTokens = 4096,
            ThinkingLevel = "low",
            ResponseSchema = schemaNode,
        };

        var reply = await client.SendAsync(call);

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", req.Url);
        Assert.Equal("application/json", req.ContentType);
        Assert.Equal("test-secret-api-key-999", req.GetHeader("x-goog-api-key"));
        Assert.Equal("2026-05-20", req.GetHeader("Api-Revision"));

        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("gemini-4-hydrogen", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("Classify this diff", root.GetProperty("input").GetString());
        Assert.Equal("You are an editor assistant.", root.GetProperty("system_instruction").GetString());

        var gen = root.GetProperty("generation_config");
        Assert.Equal(0, gen.GetProperty("temperature").GetDouble());
        Assert.Equal(4096, gen.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("low", gen.GetProperty("thinking_level").GetString());

        var fmt = root.GetProperty("response_format");
        Assert.Equal("text", fmt.GetProperty("type").GetString());
        Assert.Equal("application/json", fmt.GetProperty("mime_type").GetString());
        Assert.Equal("object", fmt.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("string", fmt.GetProperty("schema").GetProperty("properties").GetProperty("category").GetProperty("type").GetString());

        var safety = root.GetProperty("safety_settings").EnumerateArray().ToList();
        Assert.Equal(2, safety.Count);
        Assert.Contains(safety, s => s.GetProperty("type").GetString() == "hate_speech" && s.GetProperty("threshold").GetString() == "off");
        Assert.Contains(safety, s => s.GetProperty("type").GetString() == "harassment" && s.GetProperty("threshold").GetString() == "block_only_high");
    }

    [Fact]
    public async Task Request_omits_null_fields_empty_safety_settings_and_unconfigured_api_revision()
    {
        var (client, handler, _, _) = CreateSut(o =>
        {
            o.ApiRevision = null;
            o.SafetySettings.Clear();
        });
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "Hello"));

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        var req = Assert.Single(handler.Requests);
        Assert.Null(req.GetHeader("Api-Revision"));

        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("gemini-4-hydrogen", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("Hello", root.GetProperty("input").GetString());
        Assert.False(root.TryGetProperty("system_instruction", out _));
        Assert.False(root.TryGetProperty("generation_config", out _));
        Assert.False(root.TryGetProperty("response_format", out _));
        Assert.False(root.TryGetProperty("safety_settings", out _));
    }

    [Fact]
    public async Task Request_generation_config_omits_individual_null_properties()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "Hi") { ThinkingLevel = "high" });

        using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var gen = doc.RootElement.GetProperty("generation_config");
        Assert.Equal("high", gen.GetProperty("thinking_level").GetString());
        Assert.False(gen.TryGetProperty("temperature", out _));
        Assert.False(gen.TryGetProperty("max_output_tokens", out _));
    }

    [Fact]
    public async Task Completed_response_maps_model_output_only_and_usage_including_thought_tokens()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal("{\n \"category\": \"vandalism\"\n}", reply.Text);
        Assert.Equal(new AnalysisUsage(InputTokens: 369, OutputTokens: 61, ThoughtTokens: 251, CachedTokens: 0), reply.Usage);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal(200, reply.HttpStatus);
        Assert.Null(reply.ErrorCode);
    }

    [Fact]
    public async Task Completed_response_concatenates_multiple_text_blocks_and_steps_in_order()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "status": "completed",
              "usage": { "total_input_tokens": 10, "total_output_tokens": 20, "total_thought_tokens": 5, "total_cached_tokens": 2 },
              "steps": [
                { "type": "thought", "signature": "sig1" },
                { "type": "model_output", "content": [
                    { "type": "text", "text": "alpha-" },
                    { "type": "image", "data": "ignored" },
                    { "type": "text", "text": "beta" }
                ]},
                { "type": "future_step_type", "payload": {} },
                { "type": "model_output", "content": [
                    { "type": "text", "text": "-gamma" }
                ]}
              ]
            }
            """);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal("alpha-beta-gamma", reply.Text);
        Assert.Equal(new AnalysisUsage(10, 20, 5, 2), reply.Usage);
    }

    [Fact]
    public async Task Completed_with_no_text_returns_failed_empty_output()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {"status":"completed","usage":{"total_input_tokens":15},"steps":[{"type":"thought","signature":"abc"}]}
            """);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Null(reply.Text);
        Assert.Equal("empty_output", reply.ErrorCode);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal(new AnalysisUsage(15, 0, 0, 0), reply.Usage);
    }

    [Fact]
    public async Task Incomplete_without_steps_returns_incomplete_with_null_text_and_mapped_usage()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {"status":"incomplete","usage":{"total_input_tokens":200,"total_output_tokens":4096,"total_thought_tokens":300,"total_cached_tokens":50}}
            """);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Incomplete, reply.Status);
        Assert.Null(reply.Text);
        Assert.Equal(new AnalysisUsage(200, 4096, 300, 50), reply.Usage);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal(200, reply.HttpStatus);
    }

    [Theory]
    [InlineData("""{"error":{"code":"safety","message":"Blocked by safety filter"}}""", "safety")]
    [InlineData("""{"error":{"code":"JAILBREAK","message":"Jailbreak detected"}}""", "JAILBREAK")]
    [InlineData("""{"error":{"code":"blocklist","message":"Term blocked"}}""", "blocklist")]
    [InlineData("""{"error":{"code":"invalid_request","message":"Input blocked by policy"}}""", "invalid_request")]
    public async Task Http_400_block_codes_and_input_blocked_message_return_blocked_without_retry(string json, string expectedCode)
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.BadRequest, json);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Blocked, reply.Status);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal(400, reply.HttpStatus);
        Assert.Equal(expectedCode, reply.ErrorCode);
        Assert.NotNull(reply.ErrorMessage);
    }

    [Fact]
    public async Task Status_failed_with_prohibited_content_returns_blocked()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {"status":"failed","usage":{"total_input_tokens":42},"errors":[{"code":"prohibited_content","message":"Content not permitted"}]}
            """);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Blocked, reply.Status);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal("prohibited_content", reply.ErrorCode);
        Assert.Equal("Content not permitted", reply.ErrorMessage);
        Assert.Equal(new AnalysisUsage(42, 0, 0, 0), reply.Usage);
    }

    [Fact]
    public async Task Status_failed_with_non_block_code_returns_failed()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {"status":"failed","errors":[{"code":"model_error","message":"Backend failure"}]}
            """);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal("model_error", reply.ErrorCode);
        Assert.Equal("Backend failure", reply.ErrorMessage);
    }

    [Fact]
    public async Task Http_400_invalid_request_fails_immediately_with_preserved_code_and_message()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.BadRequest, RealInvalidRequest400Json);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input") { ThinkingLevel = "minimal" });

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal(400, reply.HttpStatus);
        Assert.Equal("invalid_request", reply.ErrorCode);
        Assert.Equal(
            "Thinking level THINKING_LEVEL_MINIMAL is not supported for this model. Please retry with other thinking level.",
            reply.ErrorMessage);
    }

    [Fact]
    public async Task Retryable_503_then_200_succeeds_on_second_attempt_and_sums_usage()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.ServiceUnavailable, """
            {"usage":{"total_input_tokens":31},"error":{"code":"unavailable","message":"Temporarily overloaded"}}
            """);
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal(2, reply.Attempts);
        Assert.Equal(200, reply.HttpStatus);
        Assert.Equal(new AnalysisUsage(400, 61, 251, 0), reply.Usage);
    }

    [Fact]
    public async Task Four_429s_with_max_attempts_4_returns_failed_after_4_attempts()
    {
        var (client, handler, _, _) = CreateSut(o => o.MaxAttempts = 4);
        for (var i = 0; i < 4; i++)
        {
            handler.EnqueueJson(HttpStatusCode.TooManyRequests, """
                {"error":{"code":"rate_limit_exceeded","message":"Quota exceeded"}}
                """);
        }

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal(4, reply.Attempts);
        Assert.Equal(429, reply.HttpStatus);
        Assert.Equal("rate_limit_exceeded", reply.ErrorCode);
    }

    [Fact]
    public async Task Retry_After_7_waits_7_seconds_on_FakeTimeProvider()
    {
        var (client, handler, time, _) = CreateSut(o =>
        {
            o.RetryBaseDelay = TimeSpan.FromSeconds(1);
            o.MaxRetryDelay = TimeSpan.FromSeconds(30);
        });

        handler.Enqueue(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":{"code":"rate_limit","message":"slow down"}}""", Encoding.UTF8, "application/json"),
            };
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(resp);
        });
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var callTask = client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        time.Advance(TimeSpan.FromSeconds(6));
        await Task.Yield();
        Assert.False(callTask.IsCompleted);
        Assert.Single(handler.Requests);

        time.Advance(TimeSpan.FromSeconds(2));
        var reply = await callTask;

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal(2, reply.Attempts);
        Assert.True(reply.Latency >= TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task Retry_After_http_date_is_capped_at_MaxRetryDelay()
    {
        var (client, handler, time, _) = CreateSut(o =>
        {
            o.MaxRetryDelay = TimeSpan.FromSeconds(10);
        });

        var serverDate = time.GetUtcNow().AddSeconds(60);
        handler.Enqueue(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(serverDate);
            return Task.FromResult(resp);
        });
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var callTask = client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        time.Advance(TimeSpan.FromSeconds(9));
        await Task.Yield();
        Assert.False(callTask.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(2));
        var reply = await callTask;

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal(2, reply.Attempts);
    }

    [Fact]
    public async Task Handler_throwing_HttpRequestException_once_is_retried_and_succeeds()
    {
        var (client, handler, _, _) = CreateSut();
        handler.Enqueue(_ => throw new HttpRequestException("Connection reset by peer"));
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal(2, reply.Attempts);
        Assert.Equal(200, reply.HttpStatus);
    }

    [Fact]
    public async Task Persistent_HttpRequestException_returns_failed_network()
    {
        var (client, handler, _, _) = CreateSut(o => o.MaxAttempts = 2);
        handler.Enqueue(_ => throw new HttpRequestException("DNS failure"));
        handler.Enqueue(_ => throw new HttpRequestException("DNS failure"));

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal(2, reply.Attempts);
        Assert.Null(reply.HttpStatus);
        Assert.Equal("network", reply.ErrorCode);
    }

    [Fact]
    public async Task Per_attempt_timeout_is_retried_and_succeeds()
    {
        var (client, handler, time, _) = CreateSut(o =>
        {
            o.RequestTimeout = TimeSpan.FromSeconds(5);
            o.RetryBaseDelay = TimeSpan.Zero;
        });

        handler.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var callTask = client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));
        time.Advance(TimeSpan.FromSeconds(6));
        var reply = await callTask;

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Equal(2, reply.Attempts);
    }

    [Fact]
    public async Task Exhausted_per_attempt_timeouts_return_failed_timeout()
    {
        var (client, handler, time, _) = CreateSut(o =>
        {
            o.MaxAttempts = 2;
            o.RequestTimeout = TimeSpan.FromSeconds(5);
            o.RetryBaseDelay = TimeSpan.Zero;
        });

        var attempt2Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        handler.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        handler.Enqueue(async ct =>
        {
            attempt2Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });

        var callTask = client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));
        time.Advance(TimeSpan.FromSeconds(6));
        await attempt2Started.Task;
        time.Advance(TimeSpan.FromSeconds(6));
        var reply = await callTask;

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal("timeout", reply.ErrorCode);
        Assert.Equal(2, reply.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_or_blank_api_key_returns_no_api_key_without_http_call(string? key)
    {
        var (client, handler, _, _) = CreateSut(o => o.ApiKey = key);

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal("no_api_key", reply.ErrorCode);
        Assert.Equal(0, reply.Attempts);
        Assert.Null(reply.HttpStatus);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_OperationCanceledException()
    {
        var (client, handler, _, _) = CreateSut();
        using var cts = new CancellationTokenSource();

        handler.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });

        var callTask = client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callTask);
    }

    [Fact]
    public async Task Garbage_html_body_with_200_returns_failed_bad_response_without_throwing()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueRaw(HttpStatusCode.OK, "<html><body>Gateway glitch</body></html>", "text/html");

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal("bad_response", reply.ErrorCode);
        Assert.Equal(1, reply.Attempts);
        Assert.Equal(200, reply.HttpStatus);
    }

    [Fact]
    public async Task Unexpected_interaction_status_returns_bad_response()
    {
        var (client, handler, _, _) = CreateSut();
        handler.EnqueueJson(HttpStatusCode.OK, """{"status":"requires_action","usage":{"total_input_tokens":5}}""");

        var reply = await client.SendAsync(new GeminiCall("gemini-4-hydrogen", "input"));

        Assert.Equal(GeminiStatus.Failed, reply.Status);
        Assert.Equal("bad_response", reply.ErrorCode);
        Assert.Equal(1, reply.Attempts);
    }

    [Fact]
    public async Task Logs_never_contain_api_key_input_or_system_instruction()
    {
        var (client, handler, _, logger) = CreateSut(o => o.ApiKey = "ULTRA_SECRET_KEY_VALUE");
        handler.EnqueueJson(HttpStatusCode.ServiceUnavailable, """{"error":{"code":"unavailable","message":"busy"}}""");
        handler.EnqueueJson(HttpStatusCode.OK, RealCompletedJson);

        var call = new GeminiCall("gemini-4-hydrogen", "SENSITIVE_USER_INPUT_TEXT")
        {
            SystemInstruction = "CONFIDENTIAL_SYSTEM_PROMPT_TEXT",
        };

        var reply = await client.SendAsync(call);

        Assert.Equal(GeminiStatus.Completed, reply.Status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug);
        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain("ULTRA_SECRET_KEY_VALUE", entry.Message);
            Assert.DoesNotContain("SENSITIVE_USER_INPUT_TEXT", entry.Message);
            Assert.DoesNotContain("CONFIDENTIAL_SYSTEM_PROMPT_TEXT", entry.Message);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Url,
        string? ContentType,
        Dictionary<string, string> Headers,
        string Body)
    {
        public string? GetHeader(string name) =>
            Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(Func<CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responses.Enqueue(responder);

        public void EnqueueJson(HttpStatusCode status, string json) =>
            EnqueueRaw(status, json, "application/json");

        public void EnqueueRaw(HttpStatusCode status, string body, string mediaType) =>
            _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
            {
                headers[h.Key] = string.Join(",", h.Value);
            }

            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(CancellationToken.None)
                : string.Empty;

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Content?.Headers.ContentType?.MediaType,
                headers,
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("StubHttpHandler queue exhausted.");
            }

            var next = _responses.Dequeue();
            return await next(cancellationToken);
        }
    }

    private sealed class StaticOptionsMonitor(GeminiOptions currentValue) : IOptionsMonitor<GeminiOptions>
    {
        public GeminiOptions CurrentValue => currentValue;
        public GeminiOptions Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<GeminiOptions, string?> listener) => null;
    }

    private sealed class CapturingLogger : ILogger<GeminiClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
