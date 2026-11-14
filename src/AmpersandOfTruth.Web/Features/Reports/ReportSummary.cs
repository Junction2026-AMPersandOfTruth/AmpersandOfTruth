using System.Text.Encodings.Web;
using System.Text.Json;
using AmpersandOfTruth.Web.Features.Analysis;

namespace AmpersandOfTruth.Web.Features.Reports;

/// <summary>Typed output of the <c>summarize-investigation</c> prompt (mirrors <c>summarize-investigation.schema.json</c>).</summary>
public sealed record ReportSummary(string Summary);

public sealed record SummarizeIncidentItem(
    string Tier,
    string ClaimEn,
    string Article,
    string FirstSeen,
    string? LastRemoved,
    string LiveDuration);

public static class SummarizeInvestigationPrompt
{
    public const string Id = "summarize-investigation";

    private static readonly JsonSerializerOptions IncidentsJsonOptions = new(AnalysisJson.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyDictionary<string, string> Variables(
        string topic,
        IReadOnlyList<SummarizeIncidentItem> incidents) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["topic"] = topic,
            ["incidents"] = JsonSerializer.Serialize(incidents, IncidentsJsonOptions),
        };
}
