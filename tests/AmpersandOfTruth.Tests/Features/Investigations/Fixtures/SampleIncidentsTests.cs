using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.Analysis.Schema;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using Microsoft.Extensions.Logging;

namespace AmpersandOfTruth.Tests.Features.Investigations.Fixtures;

public class SampleIncidentsTests
{
    private static readonly JsonObject IncidentSchema = LoadIncidentSchema();

    private const string ValidTopicJson = """
        {
          "slug": "winter-war",
          "title": "Winter War",
          "aliases": ["Talvisota", "Suomen talvisota 1939"],
          "incidents": [
            {
              "id": "1256-1999",
              "page_id": 1256,
              "other_page_ids": [],
              "inserting_rev_ids": [1999],
              "removing_rev_ids": [2000],
              "tier": "contested",
              "score": 18.5,
              "claim_en": "Finland attacked the Soviet Union first.",
              "claim_fi": "Suomi hyökkäsi ensin.",
              "articles": [
                { "page_id": 1256, "title_fi": "Talvisota", "title_en": "Winter War" }
              ],
              "first_inserted_at": "2024-01-02T21:19:00Z",
              "last_removed_at": "2024-01-02T21:20:00Z",
              "total_exposure_seconds": 60,
              "edits": [
                {
                  "rev_id": 1999,
                  "parent_rev_id": 1998,
                  "page_id": 1256,
                  "user_display": "192.0.x.x (anonymous)",
                  "is_anonymous": true,
                  "timestamp": "2024-01-02T21:19:00Z",
                  "role": "inserted",
                  "summary": "korjaus"
                },
                {
                  "rev_id": 2000,
                  "parent_rev_id": 1999,
                  "page_id": 1256,
                  "user_display": "Rollbacker",
                  "is_anonymous": false,
                  "timestamp": "2024-01-02T21:20:00Z",
                  "role": "removed",
                  "summary": "Hylättiin muutokset"
                }
              ],
              "signals": [
                { "kind": "removal_reason", "label": "removed as inaccurate", "evidence": null }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Embedded_fixtures_deserialise_and_validate_against_incident_schema()
    {
        var assembly = typeof(SampleIncidents).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(SampleIncidents.EmbeddedPrefix, StringComparison.Ordinal)
                        && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var embedded = SampleIncidents.ReadEmbeddedFiles(assembly);
        Assert.Equal(resourceNames.Count, embedded.Count);

        foreach (var (fileName, json) in embedded)
        {
            AssertValidFixture(fileName, json);
        }
    }

    [Fact]
    public void Valid_fixture_json_validates_against_incident_schema_and_matches_slug_title_and_aliases()
    {
        var topic = AssertValidFixture("winter-war.json", ValidTopicJson);

        Assert.Equal("winter-war", topic.Slug);
        Assert.Equal("Winter War", topic.Title);
        Assert.Equal(["Talvisota", "Suomen talvisota 1939"], topic.Aliases);
        Assert.Single(topic.Incidents);

        var samples = SampleIncidents.From(topic);
        Assert.Same(topic, samples.Match("winter-war"));
        Assert.Same(topic, samples.Match("  Winter War  "));
        Assert.Same(topic, samples.Match("TALVISOTA"));
        Assert.Same(topic, samples.Match("suomen-talvisota-1939"));
        Assert.Null(samples.Match("continuation-war"));
        Assert.Null(samples.Match("!!!"));
    }

    [Theory]
    [InlineData("broken.json", "{not valid json")]
    [InlineData("array-root.json", "[]")]
    [InlineData("missing-slug.json", """{"title":"T","incidents":[]}""")]
    [InlineData("missing-title.json", """{"slug":"s","incidents":[]}""")]
    [InlineData("missing-incidents.json", """{"slug":"s","title":"T"}""")]
    [InlineData("dup-inserts.json", """{"slug":"s","title":"T","incidents":[{"page_id":1,"inserting_rev_ids":[10,10],"removing_rev_ids":[11],"tier":"reverted","score":1}]}""")]
    [InlineData("empty-removes.json", """{"slug":"s","title":"T","incidents":[{"page_id":1,"inserting_rev_ids":[10],"removing_rev_ids":[],"tier":"reverted","score":1}]}""")]
    [InlineData("bad-tier.json", """{"slug":"s","title":"T","incidents":[{"page_id":1,"inserting_rev_ids":[10],"removing_rev_ids":[11],"tier":"bogus","score":1}]}""")]
    public void Malformed_fixture_throws_with_file_name_on_parse_and_logs_without_crashing_startup(
        string fileName, string badJson)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SampleIncidents.Parse(fileName, badJson));
        Assert.Contains(fileName, ex.Message);

        var log = new CapturingLogger<SampleIncidents>();
        var loaded = new SampleIncidents([(fileName, badJson), ("winter-war.json", ValidTopicJson)], log);

        Assert.Single(loaded.Topics);
        Assert.Equal("winter-war", loaded.Topics[0].Slug);
        Assert.Contains(log.Messages, m => m.Level == LogLevel.Error && m.Text.Contains(fileName, StringComparison.Ordinal));
    }

    private static SampleTopic AssertValidFixture(string fileName, string json)
    {
        var topic = SampleIncidents.Parse(fileName, json);
        var root = JsonNode.Parse(json)!.AsObject();
        var rawIncidents = root["incidents"]!.AsArray();

        Assert.Equal(rawIncidents.Count, topic.Incidents.Count);

        for (var i = 0; i < rawIncidents.Count; i++)
        {
            var errors = JsonSchemaValidator.Validate(IncidentSchema, rawIncidents[i]);
            Assert.True(errors.Count == 0, $"{fileName} incidents[{i}] failed schema validation: {string.Join("; ", errors)}");

            var inc = topic.Incidents[i];
            Assert.NotEmpty(inc.InsertingRevIds);
            Assert.Equal(inc.InsertingRevIds.Count, inc.InsertingRevIds.Distinct().Count());
            Assert.True(Enum.IsDefined(inc.Tier), $"{fileName} incidents[{i}] has undefined tier {inc.Tier}");
            Assert.NotNull(EvidenceTiers.Get(inc.Tier));
        }

        return topic;
    }

    private static JsonObject LoadIncidentSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? throw new InvalidOperationException("Repo root (AmpersandOfTruth.sln) not found.");
        var schemaPath = Path.Combine(root, "research", "eval", "schemas", "incident.schema.json");
        return JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add((logLevel, formatter(state, exception)));
    }
}
