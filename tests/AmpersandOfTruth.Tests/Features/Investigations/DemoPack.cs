using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>
/// Recorded demo-topic regression fixture (<c>Fixtures/demo-pack.json</c>, P1.P3 #41), written by
/// <c>research/41-regression/pack.py record</c> and replayed against the real corpus without LLM calls.
/// </summary>
public sealed record DemoPack(
    int Version,
    DateTimeOffset RecordedAt,
    string? GitCommit,
    int PipelineVersion,
    int TopN,
    IReadOnlyList<DemoPackTopic> Topics)
{
    public static string DefaultPath => FixturePath("demo-pack.json");
    public static string SamplePath => FixturePath("demo-pack.sample.json");

    public static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Features", "Investigations", "Fixtures", fileName);

    public static DemoPack Load(string path) =>
        Parse(File.ReadAllText(path, Encoding.UTF8));

    public static DemoPack Parse(string json) =>
        JsonSerializer.Deserialize<DemoPack>(json, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize demo-pack fixture.");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new EditClassificationJsonConverter(),
        },
    };

    private sealed record RawClassification(
        EditCategory Category,
        int Confidence,
        string? WhatChangedEn = null,
        IReadOnlyList<string>? Evidence = null,
        string? ClaimEn = null,
        string? ClaimFi = null,
        string? NarrativeEn = null,
        string? HistoricalNoteEn = null,
        string? UncertaintyNote = null);

    private sealed class EditClassificationJsonConverter : JsonConverter<EditClassification>
    {
        public override EditClassification? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var raw = JsonSerializer.Deserialize<RawClassification>(ref reader, options)
                ?? throw new JsonException("Invalid classification entry in demo-pack fixture.");
            return new EditClassification(
                WhatChangedEn: raw.WhatChangedEn ?? "",
                Evidence: raw.Evidence ?? [],
                ClaimEn: raw.ClaimEn,
                ClaimFi: raw.ClaimFi,
                NarrativeEn: raw.NarrativeEn,
                Category: raw.Category,
                Confidence: raw.Confidence,
                HistoricalNoteEn: raw.HistoricalNoteEn,
                UncertaintyNote: raw.UncertaintyNote);
        }

        public override void Write(Utf8JsonWriter writer, EditClassification value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new RawClassification(
                value.Category,
                value.Confidence,
                value.WhatChangedEn,
                value.Evidence,
                value.ClaimEn,
                value.ClaimFi,
                value.NarrativeEn,
                value.HistoricalNoteEn,
                value.UncertaintyNote), options);
    }
}

public sealed record DemoPackTopic(
    string Topic,
    string Kind,
    TopicScope? Scope,
    IReadOnlyList<TopicArticle> Articles,
    int Candidates,
    IReadOnlyDictionary<string, EditClassification> Classifications,
    IReadOnlyList<DemoPackIncident> Incidents,
    DemoPackExpect Expect);

public sealed record DemoPackIncident(
    int Rank,
    string Id,
    string Key,
    long PageId,
    EvidenceTier Tier,
    double Score,
    IReadOnlyList<long> InsertingRevIds,
    IReadOnlyList<long> RemovingRevIds,
    string? ClaimEn = null);

public sealed record DemoPackExpect(
    bool? NoNewCampaign = null,
    int? MaxContestedOrAbove = null,
    bool? OutOfScope = null);

/// <summary>Stage 1 replay: returns a topic's recorded <see cref="TopicArticle"/> list without calling Gemini or Ollama.</summary>
public sealed class FixtureTopicSearch(IReadOnlyList<TopicArticle> articles, TopicScope? scope = null) : ITopicSearch
{
    public string Fingerprint => "fixture";

    public Task<TopicSearchResult> SearchAsync(string topic, CancellationToken ct = default) =>
        Task.FromResult(new TopicSearchResult(articles) { Scope = scope });
}

/// <summary>Stage 3b replay: returns the recorded <see cref="EditClassification"/> for <see cref="RemovedEdit.Key"/>, else null.</summary>
public sealed class ReplayingEditClassifier(IReadOnlyDictionary<string, EditClassification> classifications) : IEditClassifier
{
    public Task<EditClassification?> ClassifyAsync(EditReading reading, string scope, CancellationToken ct = default) =>
        Task.FromResult<EditClassification?>(classifications.TryGetValue(reading.Edit.Key, out var classification) ? classification : null);
}
