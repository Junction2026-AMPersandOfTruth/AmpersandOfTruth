using Junction.Tests.Infrastructure;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Batch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Junction.Tests.Features.Analysis.Batch;

public sealed class AnalysisBatchRunnerTests
{
    private static AnalysisBatchRunner CreateRunner(IAnalysisModel model) =>
        new(model, NullLogger<AnalysisBatchRunner>.Instance);

    [Fact]
    public void ParseJsonl_extracts_ids_converts_variables_and_skips_blank_lines()
    {
        const string jsonl = """

            {"_id":"explicit-underscore","id":"ignored","text":"hei","count":42,"flag":true,"empty":null,"nested":{"a":1},"arr":[1,2]}
               
            {"_id":99,"name":"number-underscore"}
            {"id":"plain-id","flag":false}
            {"text":"no-id-row"}
            """;

        var items = AnalysisBatchRunner.ParseJsonl(new StringReader(jsonl));

        Assert.Equal(["explicit-underscore", "99", "plain-id", "6"], items.Select(static i => i.Id));

        var first = items[0].Variables;
        Assert.Equal("explicit-underscore", first["_id"]);
        Assert.Equal("ignored", first["id"]);
        Assert.Equal("hei", first["text"]);
        Assert.Equal("42", first["count"]);
        Assert.Equal("true", first["flag"]);
        Assert.Equal("", first["empty"]);
        Assert.Equal("""{"a":1}""", first["nested"]);
        Assert.Equal("[1,2]", first["arr"]);

        Assert.Equal("number-underscore", items[1].Variables["name"]);
        Assert.Equal("false", items[2].Variables["flag"]);
        Assert.Equal("no-id-row", items[3].Variables["text"]);
    }

    [Fact]
    public void ParseJsonl_throws_FormatException_with_line_number_on_bad_line()
    {
        const string malformed = """
            {"id":"1"}

            {bad json}
            """;

        var ex = Assert.Throws<FormatException>(() => AnalysisBatchRunner.ParseJsonl(new StringReader(malformed)));
        Assert.Contains("3", ex.Message);

        const string nonObject = """
            {"id":"1"}
            [1, 2, 3]
            """;

        var ex2 = Assert.Throws<FormatException>(() => AnalysisBatchRunner.ParseJsonl(new StringReader(nonObject)));
        Assert.Contains("2", ex2.Message);
    }

    [Fact]
    public async Task RunAsync_executes_items_across_models_and_computes_per_model_stats()
    {
        var fake = new FakeAnalysisModel().Handle("classify-edit", static req =>
            req.Model == "model-a" && req.Variables["text"] == "blocked-row"
                ? new FakeReply(AnalysisOutcome.Blocked, null, "safety_block")
                : FakeReply.Ok("""{"category":"vandalism"}"""));

        var runner = CreateRunner(fake);
        IReadOnlyList<BatchItem> items =
        [
            new("1", new Dictionary<string, string> { ["text"] = "first" }),
            new("2", new Dictionary<string, string> { ["text"] = "blocked-row" }),
            new("3", new Dictionary<string, string> { ["text"] = "third" }),
        ];

        var results = new List<BatchResultLine>();
        var stats = await runner.RunAsync(
            items,
            new BatchOptions("classify-edit", ["model-a", "model-b"]) { Scope = "exp-42", Concurrency = 2 },
            line =>
            {
                results.Add(line);
                return Task.CompletedTask;
            });

        Assert.Equal(6, results.Count);
        Assert.Equal(6, fake.Requests.Count);
        Assert.Equal(3, fake.Requests.Count(static r => r.Model == "model-a"));
        Assert.Equal(3, fake.Requests.Count(static r => r.Model == "model-b"));
        Assert.All(fake.Requests, static r => Assert.Equal("exp-42", r.Scope));

        Assert.Equal(2, stats.Count);

        var statsA = stats[0];
        Assert.Equal("model-a", statsA.Model);
        Assert.Equal(3, statsA.Items);
        Assert.Equal(2, statsA.Ok);
        Assert.Equal(1, statsA.Blocked);
        Assert.Equal(0, statsA.Invalid);
        Assert.Equal(0, statsA.Failed);
        Assert.Equal(0, statsA.BudgetExceeded);

        var statsB = stats[1];
        Assert.Equal("model-b", statsB.Model);
        Assert.Equal(3, statsB.Items);
        Assert.Equal(3, statsB.Ok);
        Assert.Equal(0, statsB.Blocked);

        var blockedLine = Assert.Single(results, static r => r.Outcome == "blocked");
        Assert.Equal("2", blockedLine.Id);
        Assert.Equal("model-a", blockedLine.Model);
        Assert.Null(blockedLine.Value);
        Assert.Equal("safety_block", blockedLine.Error);

        var okLines = results.Where(static r => r.Outcome == "ok").ToList();
        Assert.Equal(5, okLines.Count);
        Assert.All(okLines, static r =>
        {
            Assert.True(r.Value.HasValue);
            Assert.Equal("vandalism", r.Value.Value.GetProperty("category").GetString());
            Assert.Null(r.RawText);
        });
    }

    [Fact]
    public async Task ReadCompleted_ignores_retryable_failures_and_skip_prevents_calls()
    {
        const string existingOutput = """
            {"id":"1","model":"model-a","outcome":"ok"}
            {"id":"2","model":"model-a","outcome":"invalid"}
            {"id":"3","model":"model-a","outcome":"blocked"}
            {"id":"4","model":"model-a","outcome":"truncated"}
            {"id":"5","model":"model-a","outcome":"failed"}
            {"id":"6","model":"model-a","outcome":"budget_exceeded"}
            """;

        var completed = AnalysisBatchRunner.ReadCompleted(new StringReader(existingOutput));

        Assert.Equal(4, completed.Count);
        Assert.Contains(("1", "model-a"), completed);
        Assert.Contains(("2", "model-a"), completed);
        Assert.Contains(("3", "model-a"), completed);
        Assert.Contains(("4", "model-a"), completed);
        Assert.DoesNotContain(("5", "model-a"), completed);
        Assert.DoesNotContain(("6", "model-a"), completed);

        var fake = new FakeAnalysisModel().Returns("classify-edit", """{"ok":true}""");
        var runner = CreateRunner(fake);
        IReadOnlyList<BatchItem> items =
        [
            new("1", new Dictionary<string, string> { ["x"] = "1" }),
            new("3", new Dictionary<string, string> { ["x"] = "3" }),
            new("5", new Dictionary<string, string> { ["x"] = "5" }),
            new("6", new Dictionary<string, string> { ["x"] = "6" }),
        ];

        var produced = new List<BatchResultLine>();
        var stats = await runner.RunAsync(
            items,
            new BatchOptions("classify-edit", ["model-a"]),
            line =>
            {
                produced.Add(line);
                return Task.CompletedTask;
            },
            skip: completed);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(["5", "6"], fake.Requests.Select(static r => r.Variables["x"]).OrderBy(static x => x));
        Assert.Equal(["5", "6"], produced.Select(static r => r.Id).OrderBy(static x => x));
        Assert.Equal(2, Assert.Single(stats).Items);
    }

    [Fact]
    public async Task RunAsync_respects_concurrency_limit_and_serializes_onResult()
    {
        var inFlight = 0;
        var maxInFlight = 0;
        var fake = new FakeAnalysisModel
        {
            Delay = TimeSpan.FromMilliseconds(15),
        };
        fake.Handle("classify-edit", _ =>
        {
            var current = Interlocked.Increment(ref inFlight);
            try
            {
                UpdateMax(ref maxInFlight, current);
                Thread.Sleep(25);
                return FakeReply.Ok("""{"ok":true}""");
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        });

        var activeOnResult = 0;
        var maxActiveOnResult = 0;
        var count = 0;

        var items = Enumerable.Range(1, 8)
            .Select(static i => new BatchItem(i.ToString(), new Dictionary<string, string> { ["n"] = i.ToString() }))
            .ToList();

        var runner = CreateRunner(fake);
        await runner.RunAsync(
            items,
            new BatchOptions("classify-edit", ["model-a"]) { Concurrency = 2 },
            async _ =>
            {
                var current = Interlocked.Increment(ref activeOnResult);
                try
                {
                    UpdateMax(ref maxActiveOnResult, current);
                    await Task.Delay(10);
                    Interlocked.Increment(ref count);
                }
                finally
                {
                    Interlocked.Decrement(ref activeOnResult);
                }
            });

        Assert.Equal(8, count);
        Assert.InRange(maxInFlight, 1, 2);
        Assert.Equal(1, maxActiveOnResult);
    }

    [Fact]
    public async Task RunAsync_aborts_when_handler_throws_KeyNotFoundException()
    {
        var fake = new FakeAnalysisModel().Handle(
            "unknown-prompt",
            static _ => throw new KeyNotFoundException("Unknown prompt 'unknown-prompt'."));

        var runner = CreateRunner(fake);
        IReadOnlyList<BatchItem> items =
        [
            new("1", new Dictionary<string, string>()),
            new("2", new Dictionary<string, string>()),
        ];

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            runner.RunAsync(
                items,
                new BatchOptions("unknown-prompt", ["model-a"]) { Concurrency = 2 },
                static _ => Task.CompletedTask));
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }
}
