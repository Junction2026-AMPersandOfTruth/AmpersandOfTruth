using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Investigations;

/// <summary>
/// One incident as the pipeline (#39) emits it: the C# twin of <c>research/eval/schemas/incident.schema.json</c>
/// (the #37 output contract, still a draft apart from the five required fields). Serialises to exactly that JSON
/// through <see cref="IncidentJson.Options"/>, so the same record feeds the eval runner and the UI.
/// The UI maps it to its own view model (#98 <c>IncidentMapper</c>); diff passages are derived there, not here.
/// </summary>
public sealed record IncidentRecord
{
    // ── Frozen for the eval (CONTRACT.md) ──
    public required long PageId { get; init; }
    public required IReadOnlyList<long> InsertingRevIds { get; init; }
    public required IReadOnlyList<long> RemovingRevIds { get; init; }
    public required EvidenceTier Tier { get; init; }
    public required double Score { get; init; }

    // ── #37 draft ──
    public string? Id { get; init; }
    public IReadOnlyList<long> OtherPageIds { get; init; } = [];
    public string? ClaimEn { get; init; }
    public string? ClaimFi { get; init; }
    public string? SummaryEn { get; init; }
    public string? Narrative { get; init; }
    public ContentCategory? Category { get; init; }
    public IReadOnlyList<IncidentArticle> Articles { get; init; } = [];
    public DateTimeOffset? FirstInsertedAt { get; init; }
    public DateTimeOffset? LastRemovedAt { get; init; }
    public long? TotalExposureSeconds { get; init; }
    public int? ReinsertionCount { get; init; }
    public IReadOnlyList<IncidentEdit> Edits { get; init; } = [];
    public IReadOnlyList<IncidentSignal> Signals { get; init; } = [];
    public string? ConfidenceNote { get; init; }

    // ── Proposed additions (#98 §5 gap 2; additionalProperties allows them until #37 freezes) ──

    /// <summary>The "because …" sentence from <see cref="TierRules"/> (<see cref="TierAssessment.BecauseSentence"/>).</summary>
    public string? TierReason { get; init; }

    /// <summary>The automated content assessment (P1.S9). null = not run, failed or over budget → tier stays at most "reverted".</summary>
    public IncidentAssessment? Assessment { get; init; }
}

public sealed record IncidentArticle(long PageId, string? TitleFi, string? TitleEn = null);

public enum IncidentEditRole { Inserted, Removed, Reinserted }

/// <param name="UserDisplay"><see cref="EditorDisplay"/> plain text — IPs masked, never the raw user string.</param>
/// <param name="Summary"><see cref="EditorDisplay.MaskComment"/> of the edit summary — never the raw comment.</param>
public sealed record IncidentEdit(
    long RevId,
    long? ParentRevId,
    long PageId,
    string? UserDisplay,
    bool? IsAnonymous,
    DateTimeOffset? Timestamp,
    IncidentEditRole Role,
    string? Summary,
    string? DiffRef,
    IReadOnlyList<string> Flags);

/// <param name="Kind">Machine id (snake_case). Today: the <see cref="TierReasonKind"/> names; see #98 §5 gap 1.</param>
public sealed record IncidentSignal(string Kind, string? Label, string? Evidence = null);

/// <param name="Confidence">1 (guess) – 5 (certain), about the category.</param>
/// <param name="Quotes">Spans the model quoted. <see cref="HasVerifiedQuote"/> = at least one is verbatim in the edit.</param>
public sealed record IncidentAssessment(
    ContentCategory Category,
    int Confidence,
    string? WhatChangedEn,
    IReadOnlyList<string> Quotes,
    bool HasVerifiedQuote,
    string? UncertaintyNote,
    string? HistoricalNoteEn);

/// <summary>Serializer settings for the incident contract: snake_case names and enum values, UTC timestamps with <c>Z</c>.</summary>
public static class IncidentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new UtcZConverter(),
        },
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>Snake-case machine id of an enum value, e.g. <c>TierReasonKind.RemovedByOthers</c> → <c>removed_by_others</c>.</summary>
    public static string Id<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private sealed class UtcZConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
