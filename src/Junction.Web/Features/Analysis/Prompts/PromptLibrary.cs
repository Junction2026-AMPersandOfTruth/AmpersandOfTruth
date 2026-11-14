using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Junction.Web.Features.Analysis.Prompts;

/// <summary>Generation settings declared in a prompt's front matter. Null = not sent (API default).</summary>
public sealed record PromptSettings(double? Temperature, int? MaxOutputTokens, string? ThinkingLevel);

/// <summary>A parsed prompt file pair: <c>&lt;id&gt;.md</c> (front matter + system/user templates) and <c>&lt;id&gt;.schema.json</c>.</summary>
/// <param name="Version">Front-matter <c>version</c> (int, bumped by hand when the prompt's meaning changes).</param>
/// <param name="ContentHash">First 8 lowercase hex chars of SHA-256 over the raw .md bytes + the raw .schema.json bytes.</param>
/// <param name="Variables">Distinct variable names used by either template (plain or <c>data:</c>), in first-use order.</param>
/// <param name="Source">Where it was loaded from: <c>embedded</c> or the override file path.</param>
public sealed record PromptDefinition(
    string Id,
    int Version,
    string ContentHash,
    AnalysisJob Job,
    string? Description,
    PromptSettings Settings,
    string SystemTemplate,
    string UserTemplate,
    JsonNode Schema,
    IReadOnlyList<string> Variables,
    string Source)
{
    /// <summary><c>"{Version}.{ContentHash}"</c> — what the cache key and the usage ledger record.</summary>
    public string FullVersion => $"{Version}.{ContentHash}";
}

public sealed record RenderedPrompt(PromptDefinition Definition, string System, string User);

/// <summary>
/// Loads every prompt once at construction: embedded resources named <c>prompts/&lt;file&gt;</c> (see the
/// <c>EmbeddedResource</c> item in Junction.Web.csproj), then <see cref="GeminiOptions.PromptDirectory"/> if set
/// (same id there replaces the embedded one). Invalid files throw <see cref="InvalidOperationException"/>
/// naming the file — a broken prompt must fail tests/startup, not a demo. Singleton; immutable after construction.
/// </summary>
/// <remarks>TASK analysis-prompts implements this.</remarks>
public sealed class PromptLibrary
{
    public PromptLibrary(IOptions<GeminiOptions> options, ILogger<PromptLibrary> log) =>
        throw new NotImplementedException("analysis-prompts");

    /// <summary>Sorted by id.</summary>
    public IReadOnlyList<PromptDefinition> All => throw new NotImplementedException("analysis-prompts");

    /// <exception cref="KeyNotFoundException">Unknown id (message lists the known ids).</exception>
    public PromptDefinition Get(string id) => throw new NotImplementedException("analysis-prompts");

    /// <summary>Substitutes <c>{{name}}</c> verbatim and <c>{{data:name}}</c> as an untrusted-data block.</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    /// <exception cref="ArgumentException">A variable used by the templates is missing (message lists all missing names).</exception>
    public RenderedPrompt Render(string id, IReadOnlyDictionary<string, string> variables) =>
        throw new NotImplementedException("analysis-prompts");
}
