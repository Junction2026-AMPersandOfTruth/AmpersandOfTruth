using System.Collections.Concurrent;
using System.Text.Json;
using AmpersandOfTruth.Web.Features.Analysis;

namespace AmpersandOfTruth.Tests.Infrastructure;

/// <summary>
/// Scriptable <see cref="IAnalysisModel"/> for tests of anything that consumes LLM output — no HTTP, no DB.
/// <code>
/// var llm = new FakeAnalysisModel().Returns("classify-edit", """{"category":"vandalism", …}""");
/// Services.AddSingleton&lt;IAnalysisModel&gt;(llm);      // bUnit / DI
/// … assert on llm.Requests
/// </code>
/// JSON replies are deserialized with <see cref="AnalysisJson.Options"/> exactly like the real model (no schema check).
/// Unscripted prompts return <see cref="AnalysisOutcome.Failed"/> with error "fake: no reply scripted".
/// </summary>
public sealed class FakeAnalysisModel : IAnalysisModel
{
    private readonly ConcurrentDictionary<string, Func<AnalysisRequest, FakeReply>> _byPrompt = new();
    private readonly ConcurrentQueue<AnalysisRequest> _requests = new();

    public IReadOnlyList<AnalysisRequest> Requests => [.. _requests];

    /// <summary>Optional artificial latency per call (e.g. to test loading states).</summary>
    public TimeSpan Delay { get; set; }

    public FakeAnalysisModel Returns(string promptId, string json) => Handle(promptId, _ => FakeReply.Ok(json));

    public FakeAnalysisModel Returns(string promptId, AnalysisOutcome outcome, string? error = null) =>
        Handle(promptId, _ => new FakeReply(outcome, null, error ?? $"fake: {outcome}"));

    public FakeAnalysisModel Handle(string promptId, Func<AnalysisRequest, FakeReply> handler)
    {
        _byPrompt[promptId] = handler;
        return this;
    }

    public async Task<AnalysisResult<T>> RunAsync<T>(AnalysisRequest request, CancellationToken ct = default)
    {
        _requests.Enqueue(request);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);

        var reply = _byPrompt.TryGetValue(request.PromptId, out var handler)
            ? handler(request)
            : new FakeReply(AnalysisOutcome.Failed, null, "fake: no reply scripted");
        var info = new AnalysisCallInfo(request.PromptId, "0.fake", request.Job ?? AnalysisJob.Classify,
            request.Model ?? "fake-model", FromCache: false, Attempts: 1, new AnalysisUsage(100, 20, 0, 0),
            TimeSpan.FromMilliseconds(5), EstimatedCostUsd: 0, reply.Json);

        return reply.Outcome == AnalysisOutcome.Ok && reply.Json is not null
            ? new AnalysisResult<T>(AnalysisOutcome.Ok, JsonSerializer.Deserialize<T>(reply.Json, AnalysisJson.Options), info)
            : new AnalysisResult<T>(reply.Outcome, default, info, reply.Error);
    }
}

public sealed record FakeReply(AnalysisOutcome Outcome, string? Json, string? Error = null)
{
    public static FakeReply Ok(string json) => new(AnalysisOutcome.Ok, json);
}
