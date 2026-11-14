using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AmpersandOfTruth.Tests.Features.WikiDiff;
using AmpersandOfTruth.Web.Features.Annotations;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Annotations;

public sealed class AnnotationExportTests
{
    private const char Lf = (char)10;

    [Fact]
    public void Maps_verdicts_to_golden_labels_skips_NeedsMoreEvidence_and_preserves_snapshot_revs()
    {
        var invId = Guid.NewGuid();
        var updatedAt = new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

        var incidents = new List<IncidentRecord>
        {
            new()
            {
                Id = "ww-01",
                PageId = 1150,
                OtherPageIds = [2048],
                InsertingRevIds = [1001, 1005],
                RemovingRevIds = [1002, 1006],
                Tier = EvidenceTier.Campaign,
                Score = 95.0,
                Category = ContentCategory.FalseFactualClaim,
                ClaimEn = "Finland shelled Mainila.",
                ClaimFi = "Suomi ampui Mainilaan.",
            },
            new()
            {
                Id = "ww-02",
                PageId = 1200,
                InsertingRevIds = [2001],
                RemovingRevIds = [2002],
                Tier = EvidenceTier.Contested,
                Score = 62.0,
                ClaimEn = "Disputed casualty figure.",
            },
            new()
            {
                Id = "ww-03",
                PageId = 1300,
                InsertingRevIds = [3001],
                RemovingRevIds = [3002],
                Tier = EvidenceTier.Reverted,
                Score = 25.0,
                ClaimEn = "Added unsourced line.",
            },
            new()
            {
                Id = "ww-04",
                PageId = 1400,
                InsertingRevIds = [4001],
                RemovingRevIds = [4002],
                Tier = EvidenceTier.Contested,
                Score = 50.0,
                ClaimEn = "Random graffiti edit.",
            },
            new()
            {
                Id = "ww-05",
                PageId = 1500,
                InsertingRevIds = [5001],
                RemovingRevIds = [5002],
                Tier = EvidenceTier.Campaign,
                Score = 88.0,
                ClaimEn = "Ambiguous claim needing archive check.",
            },
            new()
            {
                Id = "ww-06",
                PageId = 1600,
                InsertingRevIds = [6001],
                RemovingRevIds = [6002],
                Tier = EvidenceTier.Reverted,
                Score = 20.0,
                ClaimEn = "Note-only feedback without verdict.",
            },
        };

        var annotations = new Dictionary<string, IncidentAnnotation>(StringComparer.Ordinal)
        {
            ["ww-01"] = new()
            {
                InvestigationId = invId,
                IncidentId = "ww-01",
                Verdict = AnalystVerdict.Confirmed,
                Note = "Checked both revisions.",
                Source = "analyst",
                UpdatedAt = updatedAt,
            },
            ["ww-02"] = new()
            {
                InvestigationId = invId,
                IncidentId = "ww-02",
                Verdict = AnalystVerdict.Plausible,
                Note = "Looks consistent with dispute.",
                Source = "analyst",
                UpdatedAt = updatedAt,
            },
            ["ww-03"] = new()
            {
                InvestigationId = invId,
                IncidentId = "ww-03",
                Verdict = AnalystVerdict.Confirmed,
                UpdatedAt = updatedAt,
            },
            ["ww-04"] = new()
            {
                InvestigationId = invId,
                IncidentId = "ww-04",
                Verdict = AnalystVerdict.NotAnIncident,
                DismissReason = DismissReason.OrdinaryVandalism,
                Note = "Plain vandalism.",
                Source = "analyst",
                UpdatedAt = updatedAt,
            },
            ["ww-05"] = new()
            {
                InvestigationId = invId,
                IncidentId = "ww-05",
                Verdict = AnalystVerdict.NeedsMoreEvidence,
                Note = "Need talk page context.",
                Source = "analyst",
                UpdatedAt = updatedAt,
            },
            ["ww-06"] = new()
            {
                InvestigationId = invId,
                IncidentId = "ww-06",
                Verdict = null,
                Note = "Translation looks off.",
                Source = "looks_wrong",
                UpdatedAt = updatedAt,
            },
        };

        var jsonl = AnnotationExport.ToGoldenJsonl("Winter War", incidents, annotations);
        var lines = jsonl.Split(Lf, StringSplitOptions.RemoveEmptyEntries);

        // ww-05 (NeedsMoreEvidence) and ww-06 (note-only, no verdict) are skipped -> 4 lines
        Assert.Equal(4, lines.Length);

        var schemaPath = Path.Combine(WikitextDiffTests.RepoRoot(), "research", "eval", "schemas", "golden.schema.json");
        var goldenSchema = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var itemSchema = goldenSchema["$defs"]!["item"]!.AsObject();
        var allowedProps = itemSchema["properties"]!.AsObject().Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var requiredProps = itemSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        var idPattern = itemSchema["properties"]!["id"]!["pattern"]!.GetValue<string>();
        var allowedLabels = itemSchema["properties"]!["label"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var obj = JsonNode.Parse(line)!.AsObject();
            foreach (var req in requiredProps)
            {
                Assert.True(obj.ContainsKey(req), $"missing required field '{req}'");
            }

            foreach (var (propName, _) in obj)
            {
                Assert.Contains(propName, allowedProps);
            }

            Assert.Matches(new Regex(idPattern), obj["id"]!.GetValue<string>());
            Assert.Contains(obj["label"]!.GetValue<string>(), allowedLabels);
        }

        using var doc0 = JsonDocument.Parse(lines[0]);
        var item0 = doc0.RootElement;
        Assert.Equal("winter-war/ww-01", item0.GetProperty("id").GetString());
        Assert.Equal("campaign", item0.GetProperty("label").GetString());
        Assert.False(item0.TryGetProperty("exclusion", out _));
        Assert.Equal("false_factual_claim", item0.GetProperty("category").GetString());
        Assert.Equal(1150L, item0.GetProperty("page_id").GetInt64());
        Assert.Equal([2048L], item0.GetProperty("other_page_ids").EnumerateArray().Select(x => x.GetInt64()).ToArray());
        Assert.Equal([1001L, 1005L], item0.GetProperty("inserting_rev_ids").EnumerateArray().Select(x => x.GetInt64()).ToArray());
        Assert.Equal([1002L, 1006L], item0.GetProperty("removing_rev_ids").EnumerateArray().Select(x => x.GetInt64()).ToArray());
        Assert.True(item0.GetProperty("human_verified").GetBoolean());
        Assert.Equal("Finland shelled Mainila.", item0.GetProperty("description").GetString());
        Assert.Equal("Checked both revisions.", item0.GetProperty("notes").GetString());

        using var doc1 = JsonDocument.Parse(lines[1]);
        var item1 = doc1.RootElement;
        Assert.Equal("winter-war/ww-02", item1.GetProperty("id").GetString());
        Assert.Equal("contested", item1.GetProperty("label").GetString());
        Assert.False(item1.TryGetProperty("exclusion", out _));
        Assert.Equal(1200L, item1.GetProperty("page_id").GetInt64());
        Assert.Equal([2001L], item1.GetProperty("inserting_rev_ids").EnumerateArray().Select(x => x.GetInt64()).ToArray());
        Assert.False(item1.GetProperty("human_verified").GetBoolean());

        using var doc2 = JsonDocument.Parse(lines[2]);
        var item2 = doc2.RootElement;
        Assert.Equal("winter-war/ww-03", item2.GetProperty("id").GetString());
        Assert.Equal("reverted", item2.GetProperty("label").GetString());
        Assert.True(item2.GetProperty("human_verified").GetBoolean());

        using var doc3 = JsonDocument.Parse(lines[3]);
        var item3 = doc3.RootElement;
        Assert.Equal("winter-war/ww-04", item3.GetProperty("id").GetString());
        Assert.Equal("not-incident", item3.GetProperty("label").GetString());
        Assert.Equal("graffiti", item3.GetProperty("exclusion").GetString());
        Assert.Equal(1400L, item3.GetProperty("page_id").GetInt64());
        Assert.Equal([4001L], item3.GetProperty("inserting_rev_ids").EnumerateArray().Select(x => x.GetInt64()).ToArray());
        Assert.True(item3.GetProperty("human_verified").GetBoolean());
    }

    [Fact]
    public void Synthesizes_fallback_incident_id_and_ascii_kebab_topic_slug()
    {
        var invId = Guid.NewGuid();
        var incident = new IncidentRecord
        {
            Id = null,
            PageId = 9876,
            InsertingRevIds = [54321],
            RemovingRevIds = [54322],
            Tier = EvidenceTier.Contested,
            Score = 70.0,
            ClaimFi = "Suomen sisällissota.",
        };

        var resolvedId = AnnotationExport.ResolveIncidentId(incident);
        Assert.Equal("9876-54321", resolvedId);

        var annotations = new Dictionary<string, IncidentAnnotation>(StringComparer.Ordinal)
        {
            [resolvedId] = new()
            {
                InvestigationId = invId,
                IncidentId = resolvedId,
                Verdict = AnalystVerdict.NotAnIncident,
                DismissReason = DismissReason.WrongGrouping,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };

        var jsonl = AnnotationExport.ToGoldenJsonl("Suomen sisällissota 1918", [incident], annotations);
        var line = Assert.Single(jsonl.Split(Lf, StringSplitOptions.RemoveEmptyEntries));

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("suomen-sisallissota-1918/9876-54321", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("not-incident", doc.RootElement.GetProperty("label").GetString());
        Assert.Equal("not-substantive", doc.RootElement.GetProperty("exclusion").GetString());
    }
}
