using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Evidence;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Stage 3b placeholder: the P0.5 <c>classify-edit</c> prompt (v1) on one removed edit. P1.S9 (#36) iterates on the prompt;
/// this class only maps an <see cref="EditReading"/> to its variables. Model-side failures → null (logged), never thrown.
/// </summary>
public sealed class GeminiEditClassifier(IAnalysisModel model, IOptions<InvestigationPipelineOptions> options, ILogger<GeminiEditClassifier> log)
    : IEditClassifier
{
    public async Task<EditClassification?> ClassifyAsync(EditReading reading, string scope, CancellationToken ct = default)
    {
        var request = new AnalysisRequest(ClassifyEditPrompt.Id, Variables(reading, options.Value.MaxPromptChars)) { Scope = scope };
        var result = await model.RunAsync<EditClassification>(request, ct);
        if (result.IsOk) return result.Value;

        log.LogInformation("classify-edit for {Edit} returned {Outcome}: {Error}", reading.Edit.Key, result.Outcome, result.Error);
        return null;
    }

    internal static Dictionary<string, string> Variables(EditReading reading, int maxChars)
    {
        var first = reading.FirstInsert;
        var revert = reading.Edit.Revert;
        var editors = PromptEditors.For(reading);   // no account names in the prompt (evidence standard §3)
        var summaries = reading.Span
            .Select(r => editors.Mask(r.Comment))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        return ClassifyEditPrompt.Variables(
            articleTitle: reading.Edit.Article.Title,
            editTimestamp: new DateTimeOffset(first.Timestamp, TimeSpan.Zero),
            editorKind: EditorKind(first.User),
            exposure: Exposure(revert.Timestamp - first.Timestamp),
            editSummary: summaries.Count == 0 ? "(none)" : string.Join(" | ", summaries),
            revertSummary: editors.Mask(revert.Comment) is { Length: > 0 } masked ? masked : "(none)",
            removedText: Text(reading.RemovedText, maxChars),
            insertedText: Text(reading.InsertedText, maxChars),
            context: reading.Context.Length == 0 ? "(none)" : reading.Context);
    }

    internal static string EditorKind(string? user) => EditorDisplay.For(user).Kind switch
    {
        Evidence.EditorKind.Named => "registered",
        Evidence.EditorKind.Temporary => "temporary-account",
        Evidence.EditorKind.IPv4 or Evidence.EditorKind.IPv6 => "anonymous (IP address)",
        _ => "hidden",
    };

    internal static string Exposure(TimeSpan live) => live switch
    {
        { TotalMinutes: < 1 } => "less than a minute",
        { TotalHours: < 1 } => Plural((int)live.TotalMinutes, "minute"),
        { TotalDays: < 1 } => Plural((int)live.TotalHours, "hour"),
        { TotalDays: < 365 } => Plural((int)live.TotalDays, "day"),
        _ => Plural((int)(live.TotalDays / 365.25), "year"),
    };

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

    private static string Text(string text, int maxChars) =>
        text.Length == 0 ? "(nothing)" : CorpusEditReader.Truncate(text, maxChars);
}
