using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Junction.Tests.Infrastructure;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Gemini;
using Junction.Web.Features.Analysis.Prompts;
using Junction.Web.Features.Analysis.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Junction.Tests.Features.Analysis;

/// <summary>The whole pipeline with real prompts, validator, SQLite cache/ledger/budget — only HTTP is faked.</summary>
public sealed class GeminiAnalysisModelTests : IDisposable
{
    private const string ValidJson = """
        {"what_changed_en":"Changed who started the Winter War.","evidence":["Suomi aloitti talvisodan"],
         "claim_en":"Finland started the Winter War.","claim_fi":"Suomi aloitti talvisodan.",
         "narrative_en":"Soviet justification of the Winter War","category":"false_factual_claim","confidence":5,
         "historical_note_en":"The Soviet Union attacked Finland on 30 November 1939.","uncertainty_note":null}
        """;

    private readonly TestDb _db = new();
    private readonly StubHandler _http = new();
    private readonly GeminiOptions _options = new()
    {
        ApiKey = "test-key",
        RetryBaseDelay = TimeSpan.Zero,
        MaxAttempts = 2,
        Models = { ["Classify"] = "gemini-4-hydrogen", ["Judge"] = "gemini-4-carbon" },
        Pricing = { ["gemini-4-hydrogen"] = new ModelPrice { InputPerMTok = 1.5, OutputPerMTok = 7.5 } },
    };

    private readonly AnalysisLedger _ledger;
    private readonly AnalysisCache _cache;
    private readonly GeminiAnalysisModel _model;

    public GeminiAnalysisModelTests()
    {
        var monitor = new FixedOptions(_options);
        _ledger = new AnalysisLedger(_db.Factory);
        _cache = new AnalysisCache(_db.Factory);
        _model = new GeminiAnalysisModel(
            new PromptLibrary(Options.Create(_options), NullLogger<PromptLibrary>.Instance),
            new GeminiClient(new HttpClient(_http), monitor, TimeProvider.System, NullLogger<GeminiClient>.Instance),
            _cache, _ledger,
            new AnalysisBudget(_ledger, monitor, TimeProvider.System),
            new ModelPricing(monitor), monitor, TimeProvider.System, NullLogger<GeminiAnalysisModel>.Instance);
    }

    [Fact]
    public async Task Valid_output_is_typed_cached_and_recorded()
    {
        _http.Enqueue(Completed(ValidJson));

        var r = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Ok, r.Outcome);
        Assert.Equal(EditCategory.FalseFactualClaim, r.Value!.Category);
        Assert.Equal("Finland started the Winter War.", r.Value.ClaimEn);
        Assert.False(r.Info.FromCache);
        Assert.Equal("gemini-4-hydrogen", r.Info.Model);
        Assert.Equal(AnalysisJob.Classify, r.Info.Job);
        Assert.Matches(@"^1\.[0-9a-f]{8}$", r.Info.PromptVersion);
        Assert.True(r.Info.EstimatedCostUsd > 0);
        Assert.Equal(1, await _cache.CountAsync());

        var body = Assert.Single(_http.Bodies);
        Assert.Equal("gemini-4-hydrogen", (string?)body["model"]);
        Assert.Contains("<data name=\"inserted_text\">", (string?)body["input"]);
        Assert.Equal("object", (string?)body["response_format"]?["schema"]?["type"]);

        var summary = await _ledger.SummarizeAsync(DateTimeOffset.MinValue);
        Assert.Equal((1, 1, 1), (summary.Calls, summary.LiveCalls, summary.Ok));
    }

    [Fact]
    public async Task Repeated_call_hits_the_cache_without_http()
    {
        _http.Enqueue(Completed(ValidJson));
        await _model.RunAsync<EditClassification>(Request());

        var again = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Ok, again.Outcome);
        Assert.True(again.Info.FromCache);
        Assert.Equal(0, again.Info.EstimatedCostUsd);
        Assert.Single(_http.Bodies);
        var summary = await _ledger.SummarizeAsync(DateTimeOffset.MinValue);
        Assert.Equal((2, 1, 1), (summary.Calls, summary.LiveCalls, summary.CacheHits));
    }

    [Fact]
    public async Task Untyped_JsonElement_output_works()
    {
        _http.Enqueue(Completed(ValidJson));

        var r = await _model.RunAsync<JsonElement>(Request());

        Assert.True(r.IsOk);
        Assert.Equal("false_factual_claim", r.Value.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Schema_invalid_output_is_repaired_once()
    {
        _http.Enqueue(Completed(ValidJson.Replace("\"false_factual_claim\"", "\"propaganda\"")));
        _http.Enqueue(Completed(ValidJson));

        var r = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Ok, r.Outcome);
        Assert.Equal(2, r.Info.Attempts);
        var repairInput = (string?)_http.Bodies[1]["input"];
        Assert.Contains("did not satisfy the required JSON schema", repairInput);
        Assert.Contains("propaganda", repairInput);
        Assert.Contains("$.category", repairInput);
    }

    [Fact]
    public async Task Malformed_output_twice_is_Invalid_not_an_exception_and_not_cached()
    {
        _http.Enqueue(Completed("Sure! Here is the JSON: {category: vandalism"));
        _http.Enqueue(Completed("{\"category\": \"vandalism\"}"));

        var r = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Invalid, r.Outcome);
        Assert.Null(r.Value);
        Assert.Equal("{\"category\": \"vandalism\"}", r.Info.RawText);
        Assert.Contains("missing required property", r.Error);
        Assert.Equal(0, await _cache.CountAsync());
    }

    [Fact]
    public async Task Safety_block_is_an_outcome_and_is_cached()
    {
        _http.Enqueue(() => Json(HttpStatusCode.BadRequest,
            """{"error":{"code":"safety","message":"Input blocked: hate_speech"}}"""));

        var r = await _model.RunAsync<EditClassification>(Request());
        var again = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Blocked, r.Outcome);
        Assert.Contains("safety", r.Error);
        Assert.Equal(AnalysisOutcome.Blocked, again.Outcome);
        Assert.True(again.Info.FromCache);
        Assert.Single(_http.Bodies);
    }

    [Fact]
    public async Task Truncated_output_is_retried_with_a_bigger_limit()
    {
        _http.Enqueue(() => Json(HttpStatusCode.OK,
            """{"status":"incomplete","usage":{"total_input_tokens":369,"total_output_tokens":3,"total_thought_tokens":17}}"""));
        _http.Enqueue(Completed(ValidJson));

        var r = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Ok, r.Outcome);
        Assert.Equal(8192, (int?)_http.Bodies[1]["generation_config"]?["max_output_tokens"]);
        Assert.True(r.Info.Usage.InputTokens >= 369);
    }

    [Fact]
    public async Task Still_truncated_after_retry_is_Truncated()
    {
        const string incomplete = """{"status":"incomplete","usage":{"total_input_tokens":10}}""";
        _http.Enqueue(() => Json(HttpStatusCode.OK, incomplete));
        _http.Enqueue(() => Json(HttpStatusCode.OK, incomplete));

        var r = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Truncated, r.Outcome);
    }

    [Fact]
    public async Task Scope_cap_returns_BudgetExceeded_without_calling()
    {
        _options.PerScopeCallCap = 1;
        _http.Enqueue(Completed(ValidJson));

        var first = await _model.RunAsync<EditClassification>(Request() with { Scope = "inv-1" });
        var second = await _model.RunAsync<EditClassification>(Request("another edit") with { Scope = "inv-1" });

        Assert.Equal(AnalysisOutcome.Ok, first.Outcome);
        Assert.Equal(AnalysisOutcome.BudgetExceeded, second.Outcome);
        Assert.Contains("inv-1", second.Error);
        Assert.Single(_http.Bodies);
    }

    [Fact]
    public async Task Missing_api_key_fails_softly()
    {
        _options.ApiKey = null;

        var r = await _model.RunAsync<EditClassification>(Request());

        Assert.Equal(AnalysisOutcome.Failed, r.Outcome);
        Assert.Contains("no_api_key", r.Error);
        Assert.Empty(_http.Bodies);
        Assert.Equal(0, await _cache.CountAsync());
    }

    [Fact]
    public async Task Bypass_cache_always_calls_and_model_override_is_sent()
    {
        _http.Enqueue(Completed(ValidJson));
        _http.Enqueue(Completed(ValidJson));
        var req = Request() with { Cache = CachePolicy.Bypass, Model = "gemini-4-carbon" };

        await _model.RunAsync<EditClassification>(req);
        var r = await _model.RunAsync<EditClassification>(req);

        Assert.False(r.Info.FromCache);
        Assert.Equal(2, _http.Bodies.Count);
        Assert.All(_http.Bodies, b => Assert.Equal("gemini-4-carbon", (string?)b["model"]));
        Assert.Equal(0, await _cache.CountAsync());
    }

    [Fact]
    public async Task Different_model_is_a_different_cache_entry()
    {
        _http.Enqueue(Completed(ValidJson));
        _http.Enqueue(Completed(ValidJson));

        await _model.RunAsync<EditClassification>(Request());
        var r = await _model.RunAsync<EditClassification>(Request() with { Model = "gemini-4-carbon" });

        Assert.False(r.Info.FromCache);
        Assert.Equal(2, await _cache.CountAsync());
    }

    [Fact]
    public async Task Missing_variable_is_a_programmer_error() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _model.RunAsync<EditClassification>(
            new AnalysisRequest(ClassifyEditPrompt.Id, new Dictionary<string, string> { ["article_title"] = "x" })));

    private static AnalysisRequest Request(string inserted = "Suomi aloitti talvisodan hyökkäämällä Mainilaan.") =>
        new(ClassifyEditPrompt.Id, ClassifyEditPrompt.Variables(
            "Talvisota", new DateTimeOffset(2014, 3, 1, 12, 0, 0, TimeSpan.Zero), "anonymous", "3 min",
            "", "Kumottu muokkaus", "Neuvostoliitto hyökkäsi Suomeen 30. marraskuuta 1939.", inserted, "Talvisota oli …"));

    private static Func<HttpResponseMessage> Completed(string text) => () => Json(HttpStatusCode.OK,
        JsonSerializer.Serialize(new
        {
            status = "completed",
            usage = new { total_input_tokens = 400, total_output_tokens = 80, total_thought_tokens = 20, total_cached_tokens = 0 },
            steps = new object[]
            {
                new { type = "thought", signature = "abc" },
                new { type = "model_output", content = new[] { new { type = "text", text } } },
            },
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public void Dispose() => _db.Dispose();

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public List<JsonNode> Bodies { get; } = [];

        public void Enqueue(Func<HttpResponseMessage> response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            lock (Bodies) Bodies.Add(JsonNode.Parse(body)!);
            lock (_responses)
                return _responses.Count > 0 ? _responses.Dequeue()() : Json(HttpStatusCode.InternalServerError, "{}");
        }
    }

    private sealed class FixedOptions(GeminiOptions value) : IOptionsMonitor<GeminiOptions>
    {
        public GeminiOptions CurrentValue => value;
        public GeminiOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<GeminiOptions, string?> listener) => null;
    }
}
