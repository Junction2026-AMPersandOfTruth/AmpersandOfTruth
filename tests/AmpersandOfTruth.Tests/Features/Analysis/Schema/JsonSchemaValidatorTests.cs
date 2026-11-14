using System.Text.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Analysis.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Analysis.Schema;

public sealed class JsonSchemaValidatorTests
{
    private static JsonNode ClassifyEditSchema =>
        new PromptLibrary(Options.Create(new GeminiOptions()), NullLogger<PromptLibrary>.Instance)
            .Get(ClassifyEditPrompt.Id)
            .Schema;

    [Fact]
    public void Valid_classify_edit_sample_produces_zero_errors()
    {
        var sample = JsonNode.Parse("""
            {
              "what_changed_en": "Changed the casualty count from 8,400 to 840.",
              "evidence": ["8 400", "840"],
              "claim_en": "840 Reds were executed.",
              "claim_fi": "840 punaista teloitettiin.",
              "narrative_en": null,
              "category": "false_factual_claim",
              "confidence": 5,
              "historical_note_en": "Historical consensus places the figure around 8,400.",
              "uncertainty_note": null
            }
            """);

        var errors = JsonSchemaValidator.Validate(ClassifyEditSchema, sample);

        Assert.Empty(errors);
    }

    [Fact]
    public void EditClassification_record_and_classify_edit_schema_stay_in_sync()
    {
        var json = """
            {
              "what_changed_en": "Altered date of treaty signing from 1940 to 1942.",
              "evidence": ["12. maaliskuuta 1942"],
              "claim_en": "The Moscow Peace Treaty was signed in 1942.",
              "claim_fi": "Moskovan rauhansopimus allekirjoitettiin vuonna 1942.",
              "narrative_en": "Timeline distortion",
              "category": "false_factual_claim",
              "confidence": 4,
              "historical_note_en": "The Moscow Peace Treaty was signed on 12 March 1940.",
              "uncertainty_note": null
            }
            """;

        var parsedNode = JsonNode.Parse(json);
        Assert.Empty(JsonSchemaValidator.Validate(ClassifyEditSchema, parsedNode));

        var record = JsonSerializer.Deserialize<EditClassification>(json, AnalysisJson.Options);
        Assert.NotNull(record);
        Assert.Equal(EditCategory.FalseFactualClaim, record.Category);
        Assert.Equal(4, record.Confidence);
        Assert.Single(record.Evidence);
        Assert.Null(record.UncertaintyNote);

        var reserialized = JsonSerializer.SerializeToNode(record, AnalysisJson.Options);
        Assert.Empty(JsonSchemaValidator.Validate(ClassifyEditSchema, reserialized));

        // Verify every EditCategory enum value serializes to a schema-valid category string.
        foreach (var category in Enum.GetValues<EditCategory>())
        {
            var variant = record with { Category = category, ClaimEn = null, ClaimFi = null, NarrativeEn = null, HistoricalNoteEn = null };
            var node = JsonSerializer.SerializeToNode(variant, AnalysisJson.Options);
            Assert.Empty(JsonSchemaValidator.Validate(ClassifyEditSchema, node));
        }
    }

    [Fact]
    public void Nullable_type_array_accepts_null_and_string_and_rejects_other_types()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "note": { "type": ["string", "null"] }
              }
            }
            """)!;

        Assert.Empty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"note": null}""")));
        Assert.Empty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"note": "ok"}""")));

        var errors = JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"note": 42}"""));
        var err = Assert.Single(errors);
        Assert.StartsWith("$.note:", err);
        Assert.Contains("expected string or null, got number", err);

        // Root null instance is treated as JSON null.
        Assert.Empty(JsonSchemaValidator.Validate(JsonNode.Parse("""{"type": "null"}""")!, null));
        var rootErr = Assert.Single(JsonSchemaValidator.Validate(JsonNode.Parse("""{"type": "object"}""")!, null));
        Assert.Equal("$: expected object, got null", rootErr);
    }

    [Fact]
    public void Integer_rejects_fractional_numbers_and_non_numbers()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "confidence": { "type": "integer" }
              }
            }
            """)!;

        Assert.Empty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"confidence": 3}""")));
        Assert.Empty(JsonSchemaValidator.Validate(schema, new JsonObject { ["confidence"] = 3 }));

        var fracErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"confidence": 2.5}""")));
        Assert.Equal("$.confidence: expected integer, got number", fracErr);

        var strErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"confidence": "high"}""")));
        Assert.Equal("$.confidence: expected integer, got string", strErr);
    }

    [Fact]
    public void Required_properties_and_additionalProperties_report_expected_paths()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "category": { "type": "string" },
                "nested": {
                  "type": "object",
                  "properties": { "ok": { "type": "boolean" } },
                  "additionalProperties": { "type": "integer" }
                }
              },
              "required": ["category"],
              "additionalProperties": false
            }
            """)!;

        var missingErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{}""")));
        Assert.Equal("$: missing required property 'category'", missingErr);

        var extraErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"category": "other", "extra": 1}""")));
        Assert.Equal("$.extra: property not allowed", extraErr);

        var addSchemaValid = JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"category": "other", "nested": {"ok": true, "dyn": 7}}"""));
        Assert.Empty(addSchemaValid);

        var addSchemaErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"category": "other", "nested": {"ok": true, "dyn": "nope"}}""")));
        Assert.Equal("$.nested.dyn: expected integer, got string", addSchemaErr);
    }

    [Fact]
    public void Enum_and_const_keywords_validate_json_equality()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "category": { "enum": ["vandalism", "other"] },
                "version": { "const": 1 },
                "empty_marker": { "const": null }
              }
            }
            """)!;

        Assert.Empty(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"category": "vandalism", "version": 1, "empty_marker": null}""")));

        var enumErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"category": "spam"}""")));
        Assert.Equal("""$.category: value "spam" not in enum""", enumErr);

        var constErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"version": 2}""")));
        Assert.StartsWith("$.version:", constErr);

        var nullConstErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"empty_marker": "not-null"}""")));
        Assert.StartsWith("$.empty_marker:", nullConstErr);
    }

    [Fact]
    public void Array_keywords_items_minItems_and_maxItems_report_expected_paths()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "evidence": {
                  "type": "array",
                  "items": { "type": "string" },
                  "minItems": 1,
                  "maxItems": 4
                }
              }
            }
            """)!;

        var minErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"evidence": []}""")));
        Assert.Equal("$.evidence: expected at least 1 items, got 0", minErr);

        var maxErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"evidence": ["a", "b", "c", "d", "e"]}""")));
        Assert.Equal("$.evidence: expected at most 4 items, got 5", maxErr);

        var itemErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"evidence": ["ok", "ok2", 99]}""")));
        Assert.Equal("$.evidence[2]: expected string, got number", itemErr);
    }

    [Fact]
    public void Numeric_minimum_maximum_and_string_minLength_maxLength_report_expected_paths()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "title": "IgnoredTitle",
              "format": "ignored-format",
              "properties": {
                "score": { "type": "number", "minimum": 1.5, "maximum": 5 },
                "code": { "type": "string", "minLength": 2, "maxLength": 4 }
              }
            }
            """)!;

        Assert.Empty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"score": 1.5, "code": "fi"}""")));

        var minNumErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"score": 1.0}""")));
        Assert.StartsWith("$.score:", minNumErr);

        var maxNumErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"score": 6}""")));
        Assert.StartsWith("$.score:", maxNumErr);

        var minLenErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"code": "a"}""")));
        Assert.StartsWith("$.code:", minLenErr);

        var maxLenErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"code": "abcde"}""")));
        Assert.StartsWith("$.code:", maxLenErr);
    }

    [Fact]
    public void AnyOf_and_ref_keywords_support_defs_and_root_references()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "$defs": {
                "tag": { "type": "string", "minLength": 2 }
              },
              "properties": {
                "label": { "$ref": "#/$defs/tag" },
                "choice": {
                  "anyOf": [
                    { "type": "integer", "minimum": 10 },
                    { "const": "auto" }
                  ]
                },
                "child": { "$ref": "#" }
              }
            }
            """)!;

        Assert.Empty(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"label": "ok", "choice": "auto", "child": {"label": "nested", "choice": 12}}""")));

        var refErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"label": "x"}""")));
        Assert.StartsWith("$.label:", refErr);

        var rootRefErr = Assert.Single(JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"child": {"label": "z"}}""")));
        Assert.StartsWith("$.child.label:", rootRefErr);

        var anyOfErr = Assert.Single(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"choice": 3}""")));
        Assert.StartsWith("$.choice:", anyOfErr);
    }

    [Fact]
    public void Collects_multiple_errors_and_caps_at_twenty()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "items": {
                  "type": "array",
                  "items": { "type": "integer" }
                },
                "name": { "type": "string" }
              },
              "required": ["name", "items"]
            }
            """)!;

        var multiErrors = JsonSchemaValidator.Validate(
            schema,
            JsonNode.Parse("""{"name": 123, "items": ["bad1", "bad2"]}"""));
        Assert.Equal(3, multiErrors.Count);
        Assert.Contains("$.name: expected string, got number", multiErrors);
        Assert.Contains("$.items[0]: expected integer, got string", multiErrors);
        Assert.Contains("$.items[1]: expected integer, got string", multiErrors);

        var badItems = new JsonArray();
        for (var i = 0; i < 30; i++)
        {
            badItems.Add("wrong");
        }
        var capped = JsonSchemaValidator.Validate(schema, new JsonObject { ["name"] = "ok", ["items"] = badItems });
        Assert.Equal(20, capped.Count);
    }
}
