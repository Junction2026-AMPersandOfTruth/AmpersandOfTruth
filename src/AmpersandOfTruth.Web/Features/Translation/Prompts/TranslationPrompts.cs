using System.Text.Encodings.Web;
using System.Text.Json;

namespace AmpersandOfTruth.Web.Features.Translation.Prompts;

/// <summary>Typed output of the <c>translate-evidence</c> prompt (mirrors <c>translate-evidence.schema.json</c>).</summary>
public sealed record TranslateEvidenceOutput(IReadOnlyList<string> Translations);

public static class TranslateEvidencePrompt
{
    public const string Id = "translate-evidence";

    private static readonly JsonSerializerOptions ItemsJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyDictionary<string, string> Variables(string sourceLang, IReadOnlyList<string> items) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_lang"] = sourceLang,
            ["items"] = JsonSerializer.Serialize(items, ItemsJsonOptions),
        };
}

/// <summary>Typed output of the <c>translate-passage</c> prompt (mirrors <c>translate-passage.schema.json</c>).</summary>
public sealed record TranslatePassageOutput(string BeforeEn, string AfterEn);

public static class TranslatePassagePrompt
{
    public const string Id = "translate-passage";

    public static IReadOnlyDictionary<string, string> Variables(string sourceLang, string before, string after) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_lang"] = sourceLang,
            ["before"] = before,
            ["after"] = after,
        };
}
