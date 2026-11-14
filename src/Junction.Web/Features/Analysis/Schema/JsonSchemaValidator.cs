using System.Text.Json.Nodes;

namespace Junction.Web.Features.Analysis.Schema;

/// <summary>
/// Validates model output against the JSON Schema subset our prompts use — Gemini's structured output is
/// constrained decoding, but it ignores some keywords (minLength, maxLength, uniqueItems, …) and a model can
/// still emit garbage on truncation, so every response is re-checked in code before it is trusted or cached.
/// Pure and thread-safe.
/// </summary>
/// <remarks>TASK analysis-prompts implements this.</remarks>
public static class JsonSchemaValidator
{
    /// <summary>Returns human-readable errors with JSON paths (e.g. <c>$.evidence[2]: expected string, got number</c>);
    /// empty list = valid. Fed back to the model verbatim in the repair prompt, so keep them short and specific.</summary>
    public static IReadOnlyList<string> Validate(JsonNode schema, JsonNode? instance) =>
        throw new NotImplementedException("analysis-prompts");
}
