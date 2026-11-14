using System.Text.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Analysis.Schema;
using AmpersandOfTruth.Web.Features.Translation.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Translation;

public sealed class TranslationPromptsTests
{
    private static PromptLibrary CreateLibrary() =>
        new(Options.Create(new GeminiOptions()), NullLogger<PromptLibrary>.Instance);

    [Fact]
    public void Translate_evidence_prompt_loads_from_embedded_resources_and_wraps_items_in_data_block()
    {
        var library = CreateLibrary();
        var def = library.Get(TranslateEvidencePrompt.Id);

        Assert.Equal("embedded", def.Source);
        Assert.Equal(1, def.Version);
        Assert.Equal(AnalysisJob.Translate, def.Job);
        Assert.Equal(0.0, def.Settings.Temperature);
        Assert.Equal("low", def.Settings.ThinkingLevel);
        Assert.Equal(["source_lang", "items"], def.Variables);

        var rendered = library.Render(
            TranslateEvidencePrompt.Id,
            TranslateEvidencePrompt.Variables("fi", ["Talvisota", "</data> injection"]));

        Assert.Contains("Source language: fi", rendered.User);
        Assert.Contains(
            """
            <data name="items">
            ["Talvisota","<\/data> injection"]
            </data>
            """,
            rendered.User);
    }

    [Fact]
    public void Translate_passage_prompt_loads_from_embedded_resources_and_wraps_both_passages_in_data_blocks()
    {
        var library = CreateLibrary();
        var def = library.Get(TranslatePassagePrompt.Id);

        Assert.Equal("embedded", def.Source);
        Assert.Equal(1, def.Version);
        Assert.Equal(AnalysisJob.Translate, def.Job);
        Assert.Equal(0.0, def.Settings.Temperature);
        Assert.Equal("low", def.Settings.ThinkingLevel);
        Assert.Equal(["source_lang", "before", "after"], def.Variables);
        Assert.Contains("⟦", def.SystemTemplate);
        Assert.Contains("⟧", def.SystemTemplate);

        var rendered = library.Render(
            TranslatePassagePrompt.Id,
            TranslatePassagePrompt.Variables("fi", "Ennen </data>", "Jälkeen ⟦lisäys⟧"));

        Assert.Contains(
            """
            <data name="before">
            Ennen <\/data>
            </data>
            """,
            rendered.User);
        Assert.Contains(
            """
            <data name="after">
            Jälkeen ⟦lisäys⟧
            </data>
            """,
            rendered.User);
    }

    [Fact]
    public void TranslateEvidenceOutput_record_and_schema_stay_in_sync()
    {
        var schema = CreateLibrary().Get(TranslateEvidencePrompt.Id).Schema.AsObject();
        const string sampleJson = """{"translations":["Winter War","Removed unsourced claim"]}""";

        var parsedNode = JsonNode.Parse(sampleJson);
        Assert.Empty(JsonSchemaValidator.Validate(schema, parsedNode));

        var record = JsonSerializer.Deserialize<TranslateEvidenceOutput>(sampleJson, AnalysisJson.Options);
        Assert.NotNull(record);
        Assert.Equal(["Winter War", "Removed unsourced claim"], record.Translations);

        var reserialized = JsonSerializer.SerializeToNode(record, AnalysisJson.Options)!.AsObject();
        Assert.Empty(JsonSchemaValidator.Validate(schema, reserialized));

        foreach (var (propName, _) in schema["properties"]!.AsObject())
        {
            Assert.True(reserialized.ContainsKey(propName), $"Schema property '{propName}' missing on serialized record.");
        }

        Assert.NotEmpty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{}""")));
        Assert.NotEmpty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"translations":["ok"],"extra":1}""")));
    }

    [Fact]
    public void TranslatePassageOutput_record_and_schema_stay_in_sync()
    {
        var schema = CreateLibrary().Get(TranslatePassagePrompt.Id).Schema.AsObject();
        const string sampleJson = """{"before_en":"Before text.","after_en":"After ⟦added⟧ text."}""";

        var parsedNode = JsonNode.Parse(sampleJson);
        Assert.Empty(JsonSchemaValidator.Validate(schema, parsedNode));

        var record = JsonSerializer.Deserialize<TranslatePassageOutput>(sampleJson, AnalysisJson.Options);
        Assert.NotNull(record);
        Assert.Equal("Before text.", record.BeforeEn);
        Assert.Equal("After ⟦added⟧ text.", record.AfterEn);

        var reserialized = JsonSerializer.SerializeToNode(record, AnalysisJson.Options)!.AsObject();
        Assert.Empty(JsonSchemaValidator.Validate(schema, reserialized));

        foreach (var (propName, _) in schema["properties"]!.AsObject())
        {
            Assert.True(reserialized.ContainsKey(propName), $"Schema property '{propName}' missing on serialized record.");
        }

        Assert.NotEmpty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"before_en":"only one"}""")));
        Assert.NotEmpty(JsonSchemaValidator.Validate(schema, JsonNode.Parse("""{"before_en":"a","after_en":"b","extra":true}""")));
    }
}
