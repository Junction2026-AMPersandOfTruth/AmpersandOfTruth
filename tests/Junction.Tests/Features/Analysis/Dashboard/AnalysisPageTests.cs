using Bunit;
using Junction.Tests.Infrastructure;
using Junction.Web.Components.Layout;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Junction.Tests.Features.Analysis.Dashboard;

public sealed class AnalysisPageTests : BunitContext
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 14, 30, 0, TimeSpan.Zero));
    private readonly FakeAnalysisModel _fakeModel = new();

    public AnalysisPageTests()
    {
        _db.AddTo(Services);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<TimeProvider>(_time).AddAnalysisFeature();
        Services.AddSingleton<IAnalysisModel>(_fakeModel);
    }

    [Fact]
    public void No_api_key_shows_warning_with_user_secrets_command()
    {
        Services.Configure<GeminiOptions>(o => o.ApiKey = null);

        var cut = Render<AnalysisPage>();

        var warning = cut.WaitForElement("[data-testid=gemini-no-key]");
        Assert.Contains("user-secrets", warning.TextContent);
        Assert.Contains("""dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/Junction.Web""", warning.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=gemini-key-ok]"));
    }

    [Fact]
    public void Configured_api_key_shows_ok_badge_without_exposing_key()
    {
        const string secret = "AIza-never-render-this-secret-value";
        Services.Configure<GeminiOptions>(o => o.ApiKey = secret);

        var cut = Render<AnalysisPage>();

        var badge = cut.WaitForElement("[data-testid=gemini-key-ok]");
        Assert.Equal("Gemini key configured", badge.TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=gemini-no-key]"));
        Assert.DoesNotContain(secret, cut.Markup);
    }

    [Fact]
    public void Jobs_table_renders_one_row_per_job_with_configured_model_and_pricing()
    {
        Services.Configure<GeminiOptions>(o =>
        {
            o.DefaultModel = "gemini-default";
            o.Models["Classify"] = "gemini-4-hydrogen";
            o.Models["Judge"] = "gemini-4-carbon";
            o.Pricing["gemini-4-hydrogen"] = new ModelPrice { InputPerMTok = 1.5, OutputPerMTok = 7.5 };
        });

        var cut = Render<AnalysisPage>();

        var rows = cut.FindAll("[data-testid=jobs-table] tbody tr");
        Assert.Equal(Enum.GetValues<AnalysisJob>().Length, rows.Count);

        var classifyRow = Assert.Single(rows, r => r.TextContent.Contains("Classify"));
        Assert.Contains("gemini-4-hydrogen", classifyRow.TextContent);
        Assert.Contains("$1.50 / $7.50", classifyRow.TextContent);

        var judgeRow = Assert.Single(rows, r => r.TextContent.Contains("Judge"));
        Assert.Contains("gemini-4-carbon", judgeRow.TextContent);
        Assert.Contains("—", judgeRow.TextContent);
    }

    [Fact]
    public void Empty_ledger_shows_zero_totals_and_empty_state()
    {
        var cut = Render<AnalysisPage>();

        var empty = cut.WaitForElement("[data-testid=empty-state]");
        Assert.Equal("No LLM calls yet.", empty.TextContent.Trim());
        Assert.Equal("0 / 20,000", cut.Find("[data-testid=today-calls]").TextContent.Trim());
        Assert.Equal("$0.00 / $150.00", cut.Find("[data-testid=today-cost]").TextContent.Trim());
        Assert.Equal("0", cut.Find("[data-testid=today-cache-hits]").TextContent.Trim());
        Assert.Equal(
            "ok 0 · blocked 0 · invalid 0 · truncated 0 · failed 0 · budget 0",
            cut.Find("[data-testid=today-outcomes]").TextContent.Trim());
    }

    [Fact]
    public async Task Seeded_ledger_displays_today_metrics_by_model_and_recent_calls()
    {
        var ledger = Services.GetRequiredService<AnalysisLedger>();
        var now = _time.GetUtcNow();
        var longError = new string('e', 140);

        // Yesterday row: appears in recent-calls, excluded from today's summary and by-model.
        await ledger.RecordAsync(MakeRecord(
            at: now.AddDays(-1),
            model: "gemini-old",
            outcome: AnalysisOutcome.Ok,
            fromCache: false,
            input: 5000,
            output: 500,
            thought: 100,
            cost: 9.99,
            latencyMs: 800,
            scope: "inv-yesterday"));

        // Today: 2 live calls (Ok + Blocked) + 1 cached Ok + 1 live Failed (total 3 live calls, $0.42 cost)
        await ledger.RecordAsync(MakeRecord(
            at: now.AddMinutes(-20),
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Ok,
            fromCache: false,
            input: 1200,
            output: 300,
            thought: 50,
            cost: 0.30,
            latencyMs: 250,
            scope: "inv-1"));

        await ledger.RecordAsync(MakeRecord(
            at: now.AddMinutes(-15),
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Blocked,
            fromCache: false,
            input: 800,
            output: 0,
            thought: 0,
            cost: 0.05,
            latencyMs: 150,
            scope: "inv-1",
            error: "blocked: safety"));

        await ledger.RecordAsync(MakeRecord(
            at: now.AddMinutes(-10),
            model: "gemini-4-carbon",
            outcome: AnalysisOutcome.Failed,
            fromCache: false,
            input: 400,
            output: 20,
            thought: 10,
            cost: 0.07,
            latencyMs: 600,
            scope: "inv-2",
            error: longError));

        await ledger.RecordAsync(MakeRecord(
            at: now.AddMinutes(-5),
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Ok,
            fromCache: true,
            input: 1200,
            output: 300,
            thought: 50,
            cost: 0.0,
            latencyMs: 2,
            scope: "inv-1"));

        var cut = Render<AnalysisPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("3 / 20,000", cut.Find("[data-testid=today-calls]").TextContent.Trim());
            Assert.Equal("$0.42 / $150.00", cut.Find("[data-testid=today-cost]").TextContent.Trim());
            Assert.Equal("1", cut.Find("[data-testid=today-cache-hits]").TextContent.Trim());
            Assert.Equal(
                "ok 2 · blocked 1 · invalid 0 · truncated 0 · failed 1 · budget 0",
                cut.Find("[data-testid=today-outcomes]").TextContent.Trim());
        });

        var byModelRows = cut.FindAll("[data-testid=by-model] tbody tr");
        Assert.Equal(2, byModelRows.Count);
        Assert.Contains("gemini-4-hydrogen", byModelRows[0].TextContent);
        Assert.Contains("2,000", byModelRows[0].TextContent);
        Assert.Contains("$0.3500", byModelRows[0].TextContent);
        Assert.Contains("200 ms", byModelRows[0].TextContent);

        Assert.Contains("gemini-4-carbon", byModelRows[1].TextContent);
        Assert.Contains("$0.0700", byModelRows[1].TextContent);
        Assert.Contains("600 ms", byModelRows[1].TextContent);

        var recentRows = cut.FindAll("[data-testid=recent-calls] tbody tr");
        Assert.Equal(5, recentRows.Count);
        Assert.Contains("cache", recentRows[0].TextContent);

        var failedErrorSpan = cut.Find("[data-testid=recent-calls] span[title]");
        Assert.Equal(longError, failedErrorSpan.GetAttribute("title"));
        Assert.Equal(121, failedErrorSpan.TextContent.Length); // 120 chars + '…'
    }

    [Fact]
    public async Task Recording_new_row_after_render_updates_recent_calls_live()
    {
        var ledger = Services.GetRequiredService<AnalysisLedger>();
        var cut = Render<AnalysisPage>();
        cut.WaitForElement("[data-testid=empty-state]");

        await ledger.RecordAsync(MakeRecord(
            at: _time.GetUtcNow(),
            model: "gemini-4-hydrogen",
            outcome: AnalysisOutcome.Ok,
            fromCache: false,
            input: 500,
            output: 100,
            thought: 0,
            cost: 0.12,
            latencyMs: 180,
            scope: "live-scope"));

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid=recent-calls] tbody tr"));
            Assert.Contains("live-scope", row.TextContent);
            Assert.Equal("1 / 20,000", cut.Find("[data-testid=today-calls]").TextContent.Trim());
        });
    }

    [Fact]
    public void Playground_is_not_rendered_when_disabled()
    {
        Services.Configure<GeminiOptions>(o => o.PlaygroundEnabled = false);

        var cut = Render<AnalysisPage>();
        cut.WaitForElement("[data-testid=jobs-table]");

        Assert.Empty(cut.FindAll("[data-testid=playground]"));
    }

    [Fact]
    public void Playground_loads_sample_runs_with_selected_model_and_handles_ok_and_blocked_outcomes()
    {
        Services.Configure<GeminiOptions>(o =>
        {
            o.PlaygroundEnabled = true;
            o.Models["Classify"] = "gemini-4-hydrogen";
            o.Pricing["gemini-4-carbon"] = new ModelPrice { InputPerMTok = 2.0, OutputPerMTok = 10.0 };
        });

        _fakeModel.Returns("classify-edit", """{"category":"false_factual_claim","claim_fi":"Suomi aloitti talvisodan."}""");

        var cut = Render<AnalysisPage>();
        cut.WaitForElement("[data-testid=playground]");

        cut.Find("[data-testid=pg-sample]").Click();

        var insertedTextarea = cut.Find("[data-testid=pg-var-inserted_text]");
        Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", insertedTextarea.GetAttribute("value") ?? insertedTextarea.TextContent);

        cut.Find("[data-testid=pg-model]").Change("gemini-4-carbon");
        cut.Find("[data-testid=pg-bypass]").Change(true);
        cut.Find("[data-testid=pg-run]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("ok", cut.Find("[data-testid=pg-outcome]").TextContent.Trim());
            var json = cut.Find("[data-testid=pg-json]").TextContent;
            Assert.Contains(
                """
                  "category": "false_factual_claim",
                """,
                json);
            Assert.Contains("Suomi aloitti talvisodan.", json);
        });

        var req = Assert.Single(_fakeModel.Requests);
        Assert.Equal("classify-edit", req.PromptId);
        Assert.Equal("playground", req.Scope);
        Assert.Equal("gemini-4-carbon", req.Model);
        Assert.Equal(CachePolicy.Bypass, req.Cache);
        Assert.Equal("Talvisota", req.Variables["article_title"]);
        Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", req.Variables["inserted_text"]);

        // Now script a Blocked outcome and run with default cache policy
        _fakeModel.Returns("classify-edit", AnalysisOutcome.Blocked, "blocked by safety");
        cut.Find("[data-testid=pg-bypass]").Change(false);
        cut.Find("[data-testid=pg-run]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("blocked", cut.Find("[data-testid=pg-outcome]").TextContent.Trim());
            Assert.Contains("blocked by safety", cut.Find("[data-testid=pg-result]").TextContent);
        });

        Assert.Equal(2, _fakeModel.Requests.Count);
        Assert.Equal(CachePolicy.ReadWrite, _fakeModel.Requests[1].Cache);
    }

    [Fact]
    public void Playground_changing_prompt_resets_variables_and_hides_sample_button()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"junction-pg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "other-prompt.md"),
                """
                ---
                id: other-prompt
                version: 1
                job: Summarize
                ---
                <<<system>>>
                Summarise.
                <<<user>>>
                Topic: {{topic}}
                """);
            File.WriteAllText(Path.Combine(tempDir, "other-prompt.schema.json"),
                """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}""");

            Services.Configure<GeminiOptions>(o =>
            {
                o.PlaygroundEnabled = true;
                o.PromptDirectory = tempDir;
            });

            var cut = Render<AnalysisPage>();
            cut.Find("[data-testid=pg-sample]").Click();
            Assert.NotEmpty(cut.FindAll("[data-testid=pg-var-inserted_text]"));

            cut.Find("[data-testid=pg-prompt]").Change("other-prompt");

            Assert.Empty(cut.FindAll("[data-testid=pg-sample]"));
            Assert.Empty(cut.FindAll("[data-testid=pg-var-inserted_text]"));
            var topicVar = cut.Find("[data-testid=pg-var-topic]");
            Assert.Equal(string.Empty, topicVar.GetAttribute("value") ?? topicVar.TextContent);

            // Switching back resets classify-edit boxes to empty
            cut.Find("[data-testid=pg-prompt]").Change("classify-edit");
            var resetInserted = cut.Find("[data-testid=pg-var-inserted_text]");
            Assert.Equal(string.Empty, resetInserted.GetAttribute("value") ?? resetInserted.TextContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void NavMenu_contains_analysis_link()
    {
        var cut = Render<NavMenu>();

        var link = Assert.Single(cut.FindAll("a.nav-link"), a => a.GetAttribute("href") == "analysis");
        Assert.Contains("LLM usage", link.TextContent);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        _db.Dispose();
    }

    private static AnalysisCallRecord MakeRecord(
        DateTimeOffset at,
        string model,
        AnalysisOutcome outcome,
        bool fromCache,
        int input,
        int output,
        int thought,
        double cost,
        long latencyMs,
        string? scope = null,
        string? error = null) => new()
    {
        At = at,
        Scope = scope,
        PromptId = "classify-edit",
        PromptVersion = "1.3fa2c9e0",
        Model = model,
        Job = AnalysisJob.Classify,
        Outcome = outcome,
        FromCache = fromCache,
        Attempts = fromCache || outcome == AnalysisOutcome.BudgetExceeded ? 0 : 1,
        InputTokens = input,
        OutputTokens = output,
        ThoughtTokens = thought,
        CachedTokens = 0,
        LatencyMs = latencyMs,
        EstimatedCostUsd = cost,
        Error = error,
    };
}
