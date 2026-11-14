using System.Text.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Tests.Features.WikiDiff;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>Keeps <see cref="IncidentRecord"/> in sync with <c>research/eval/schemas/incident.schema.json</c> (#37 contract).</summary>
public class IncidentRecordTests
{
    private static readonly JsonObject Schema = JsonNode.Parse(File.ReadAllText(
        Path.Combine(WikitextDiffTests.RepoRoot(), "research", "eval", "schemas", "incident.schema.json")))!.AsObject();

    private static async Task<IncidentRecord> PipelineIncident() =>
        Assert.Single(((Completed)(await new PipelineKit().RunAsync())[^1]).Incidents);

    private static JsonObject Json(IncidentRecord r) => JsonSerializer.SerializeToNode(r, IncidentJson.Options)!.AsObject();

    [Fact]
    public async Task Serialises_every_schema_property_with_the_schema_names()
    {
        var json = Json(await PipelineIncident());
        var properties = Schema["properties"]!.AsObject();

        foreach (var required in Schema["required"]!.AsArray().Select(n => n!.GetValue<string>()))
            Assert.True(json.ContainsKey(required), $"missing required '{required}'");
        foreach (var (name, _) in properties)
            Assert.True(json.ContainsKey(name), $"schema property '{name}' is not serialised");

        var edit = json["edits"]!.AsArray()[0]!.AsObject();
        foreach (var (name, _) in properties["edits"]!["items"]!["properties"]!.AsObject())
            Assert.True(edit.ContainsKey(name), $"edits[] property '{name}' is not serialised");
        var article = json["articles"]!.AsArray()[0]!.AsObject();
        foreach (var (name, _) in properties["articles"]!["items"]!["properties"]!.AsObject())
            Assert.True(article.ContainsKey(name), $"articles[] property '{name}' is not serialised");
        var signal = json["signals"]!.AsArray()[0]!.AsObject();
        foreach (var (name, _) in properties["signals"]!["items"]!["properties"]!.AsObject())
            Assert.True(signal.ContainsKey(name), $"signals[] property '{name}' is not serialised");
        var assessment = json["assessment"]!.AsObject();
        foreach (var (name, _) in properties["assessment"]!["properties"]!.AsObject())
            Assert.True(assessment.ContainsKey(name), $"assessment property '{name}' is not serialised");
    }

    [Fact]
    public async Task Signal_ids_are_the_known_ids_of_the_schema()
    {
        var known = Schema["properties"]!["signals"]!["items"]!["properties"]!["kind"]!["examples"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();

        Assert.Equal(known.Count, known.Distinct().Count());
        Assert.Equal(Enum.GetValues<TierReasonKind>().Select(IncidentJson.Id), known.Take(Enum.GetValues<TierReasonKind>().Length));
        Assert.All((await PipelineIncident()).Signals, s => Assert.Contains(s.Kind, known));
    }

    [Fact]
    public async Task Enum_values_and_timestamps_match_the_schema()
    {
        var json = Json(await PipelineIncident());
        var properties = Schema["properties"]!.AsObject();

        Assert.Equal("reverted", json["tier"]!.GetValue<string>());
        Assert.Equal("false_factual_claim", json["category"]!.GetValue<string>());
        Assert.Equal("2024-01-02T21:19:00Z", json["first_inserted_at"]!.GetValue<string>());
        Assert.Equal("inserted", json["edits"]![0]!["role"]!.GetValue<string>());

        static IEnumerable<string?> Values(JsonNode n) => n["enum"]!.AsArray().Select(v => v?.GetValue<string>());
        Assert.Equal(EvidenceTiers.All.Select(t => t.Id), Values(properties["tier"]!));
        Assert.Equal(
            Enum.GetValues<ContentCategory>().Select(IncidentJson.Id).Append(null),
            Values(properties["category"]!));
        Assert.Equal(
            Enum.GetValues<IncidentEditRole>().Select(IncidentJson.Id),
            Values(properties["edits"]!["items"]!["properties"]!["role"]!));
        Assert.Equal(
            Enum.GetValues<ContentCategory>().Select(IncidentJson.Id),
            Values(properties["assessment"]!["properties"]!["category"]!));
        Assert.Equal("false_factual_claim", json["assessment"]!["category"]!.GetValue<string>());
    }

    [Fact]
    public async Task Round_trips_through_the_contract_json()
    {
        var incident = await PipelineIncident();

        var back = JsonSerializer.Deserialize<IncidentRecord>(JsonSerializer.Serialize(incident, IncidentJson.Options), IncidentJson.Options)!;

        Assert.Equal(JsonSerializer.Serialize(incident, IncidentJson.Options), JsonSerializer.Serialize(back, IncidentJson.Options));
        Assert.Equal(incident.FirstInsertedAt, back.FirstInsertedAt);
    }

    [Fact]
    public void Links_point_at_fi_wikipedia()
    {
        var r = FakeInvestigationCorpus.Revert(1256, 2000);

        Assert.Equal("https://fi.wikipedia.org/w/index.php?diff=prev&oldid=2000", r.DiffUrl.AbsoluteUri);
        Assert.Equal("https://fi.wikipedia.org/w/index.php?diff=prev&oldid=1999", r.RevertedDiffUrl.AbsoluteUri);
        Assert.Equal("https://fi.wikipedia.org/?curid=1256", new TopicArticle("fi", 1256, "Talvisota", 0).Url);
    }
}
