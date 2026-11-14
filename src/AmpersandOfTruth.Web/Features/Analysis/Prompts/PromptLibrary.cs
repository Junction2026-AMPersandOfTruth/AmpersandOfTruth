using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Analysis.Prompts;

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
/// <c>EmbeddedResource</c> item in AmpersandOfTruth.Web.csproj), then <see cref="GeminiOptions.PromptDirectory"/> if set
/// (same id there replaces the embedded one). Invalid files throw <see cref="InvalidOperationException"/>
/// naming the file — a broken prompt must fail tests/startup, not a demo. Singleton; immutable after construction.
/// </summary>
public sealed class PromptLibrary
{
    private const string EmbeddedPrefix = "prompts/";
    private const string MdExtension = ".md";
    private const string SchemaExtension = ".schema.json";

    private static readonly Regex PlaceholderRegex = new(
        @"\{\{\s*(data:)?([a-z0-9_]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, PromptDefinition> _byId;
    private readonly PromptDefinition[] _all;

    public PromptLibrary(IOptions<GeminiOptions> options, ILogger<PromptLibrary> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var prompts = new Dictionary<string, PromptDefinition>(StringComparer.Ordinal);
        LoadEmbedded(prompts);
        LoadDirectoryOverrides(options.Value.PromptDirectory, log, prompts);

        _byId = prompts;
        _all = prompts.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Sorted by id.</summary>
    public IReadOnlyList<PromptDefinition> All => _all;

    /// <exception cref="KeyNotFoundException">Unknown id (message lists the known ids).</exception>
    public PromptDefinition Get(string id)
    {
        if (id is not null && _byId.TryGetValue(id, out var definition))
        {
            return definition;
        }

        var known = _all.Length == 0 ? "(none)" : string.Join(", ", _all.Select(p => p.Id));
        throw new KeyNotFoundException($"Unknown prompt '{id}'. Known prompts: {known}.");
    }

    /// <summary>Substitutes <c>{{name}}</c> verbatim and <c>{{data:name}}</c> as an untrusted-data block.</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    /// <exception cref="ArgumentException">A variable used by the templates is missing (message lists all missing names).</exception>
    public RenderedPrompt Render(string id, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var def = Get(id);

        List<string>? missing = null;
        foreach (var variable in def.Variables)
        {
            if (!variables.ContainsKey(variable))
            {
                missing ??= [];
                missing.Add(variable);
            }
        }

        if (missing is { Count: > 0 })
        {
            throw new ArgumentException(
                $"Prompt '{id}' is missing required variable(s): {string.Join(", ", missing)}.",
                nameof(variables));
        }

        return new RenderedPrompt(
            def,
            RenderTemplate(def.SystemTemplate, variables),
            RenderTemplate(def.UserTemplate, variables));
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> variables) =>
        PlaceholderRegex.Replace(template, match =>
        {
            var isData = match.Groups[1].Success;
            var name = match.Groups[2].Value;
            var value = variables[name] ?? string.Empty;

            if (!isData)
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return $"""<data name="{name}" empty="true" />""";
            }

            var escaped = value.Replace("</data", @"<\/data", StringComparison.OrdinalIgnoreCase);
            return $"<data name=\u0022{name}\u0022>\u000A{escaped}\u000A</data>";
        });

    private static void LoadEmbedded(Dictionary<string, PromptDefinition> prompts)
    {
        var assembly = typeof(PromptLibrary).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var mdResource in resources
            .Where(n => n.EndsWith(MdExtension, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            var fileName = mdResource[EmbeddedPrefix.Length..];
            var expectedId = fileName[..^MdExtension.Length];
            var schemaResource = $"{EmbeddedPrefix}{expectedId}{SchemaExtension}";

            if (!resources.Contains(schemaResource))
            {
                throw new InvalidOperationException($"Prompt '{mdResource}' is missing schema '{schemaResource}'.");
            }

            var mdBytes = ReadResourceBytes(assembly, mdResource);
            var schemaBytes = ReadResourceBytes(assembly, schemaResource);
            var def = ParsePrompt(expectedId, mdResource, schemaResource, mdBytes, schemaBytes, "embedded");
            prompts[def.Id] = def;
        }
    }

    private static void LoadDirectoryOverrides(
        string? promptDirectory,
        ILogger<PromptLibrary> log,
        Dictionary<string, PromptDefinition> prompts)
    {
        if (string.IsNullOrWhiteSpace(promptDirectory))
        {
            return;
        }

        if (!Directory.Exists(promptDirectory))
        {
            log.LogWarning("Prompt override directory '{PromptDirectory}' does not exist; ignoring.", promptDirectory);
            return;
        }

        foreach (var mdPath in Directory.GetFiles(promptDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            var expectedId = Path.GetFileNameWithoutExtension(mdPath);
            if (string.IsNullOrEmpty(expectedId))
            {
                throw new InvalidOperationException($"Invalid prompt file name '{mdPath}'.");
            }

            var schemaPath = Path.Combine(promptDirectory, $"{expectedId}{SchemaExtension}");
            if (!File.Exists(schemaPath))
            {
                throw new InvalidOperationException($"Prompt '{mdPath}' is missing schema '{schemaPath}'.");
            }

            var mdBytes = File.ReadAllBytes(mdPath);
            var schemaBytes = File.ReadAllBytes(schemaPath);
            var def = ParsePrompt(expectedId, mdPath, schemaPath, mdBytes, schemaBytes, mdPath);
            var overridesEmbedded = prompts.ContainsKey(def.Id);
            prompts[def.Id] = def;

            log.LogInformation(
                overridesEmbedded
                    ? "Overrode prompt '{PromptId}' ({FullVersion}) from '{PromptPath}'."
                    : "Loaded prompt '{PromptId}' ({FullVersion}) from '{PromptPath}'.",
                def.Id,
                def.FullVersion,
                mdPath);
        }
    }

    private static byte[] ReadResourceBytes(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be opened.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static PromptDefinition ParsePrompt(
        string expectedId,
        string mdLabel,
        string schemaLabel,
        byte[] mdBytes,
        byte[] schemaBytes,
        string source)
    {
        var contentHash = ComputeContentHash(mdBytes, schemaBytes);
        var schema = ParseSchema(schemaLabel, mdLabel, schemaBytes);

        var rawText = Encoding.UTF8.GetString(mdBytes);
        if (rawText.StartsWith('\uFEFF'))
        {
            rawText = rawText[1..];
        }

        var normalized = rawText.Replace("\u000D\u000A", "\u000A", StringComparison.Ordinal);
        var lines = normalized.Split('\u000A');

        if (lines.Length == 0 || lines[0].TrimEnd(' ') != "---")
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' must begin with front matter '---'.");
        }

        var closingDashIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd(' ') == "---")
            {
                closingDashIndex = i;
                break;
            }
        }

        if (closingDashIndex < 0)
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' is missing closing front matter '---'.");
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        string? id = null;
        int? version = null;
        AnalysisJob? job = null;
        string? description = null;
        double? temperature = null;
        int? maxOutputTokens = null;
        string? thinkingLevel = null;

        for (var i = 1; i < closingDashIndex; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new InvalidOperationException($"Prompt '{mdLabel}' has invalid front matter line '{line}'.");
            }

            var key = line[..colon].Trim();
            var val = line[(colon + 1)..].Trim();

            if (!seenKeys.Add(key))
            {
                throw new InvalidOperationException($"Prompt '{mdLabel}' has duplicate front matter key '{key}'.");
            }

            switch (key)
            {
                case "id":
                    if (string.IsNullOrEmpty(val))
                    {
                        throw new InvalidOperationException($"Prompt '{mdLabel}' has empty 'id'.");
                    }
                    if (!string.Equals(val, expectedId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Prompt '{mdLabel}' front matter id '{val}' does not match file name '{expectedId}'.");
                    }
                    id = val;
                    break;

                case "version":
                    if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVersion) || parsedVersion < 1)
                    {
                        throw new InvalidOperationException(
                            $"Prompt '{mdLabel}' has invalid version '{val}' (expected int >= 1).");
                    }
                    version = parsedVersion;
                    break;

                case "job":
                    if (!TryParseJob(val, out var parsedJob))
                    {
                        throw new InvalidOperationException($"Prompt '{mdLabel}' has invalid job '{val}'.");
                    }
                    job = parsedJob;
                    break;

                case "description":
                    if (string.IsNullOrEmpty(val))
                    {
                        throw new InvalidOperationException($"Prompt '{mdLabel}' has empty 'description'.");
                    }
                    description = val;
                    break;

                case "temperature":
                    if (!double.TryParse(val, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedTemp)
                        || !double.IsFinite(parsedTemp))
                    {
                        throw new InvalidOperationException($"Prompt '{mdLabel}' has invalid temperature '{val}'.");
                    }
                    temperature = parsedTemp;
                    break;

                case "max_output_tokens":
                    if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxTokens) || parsedMaxTokens < 1)
                    {
                        throw new InvalidOperationException($"Prompt '{mdLabel}' has invalid max_output_tokens '{val}'.");
                    }
                    maxOutputTokens = parsedMaxTokens;
                    break;

                case "thinking_level":
                    if (string.IsNullOrEmpty(val))
                    {
                        throw new InvalidOperationException($"Prompt '{mdLabel}' has empty 'thinking_level'.");
                    }
                    thinkingLevel = val;
                    break;

                default:
                    throw new InvalidOperationException($"Prompt '{mdLabel}' has unknown front matter key '{key}'.");
            }
        }

        if (id is null)
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' is missing required front matter key 'id'.");
        }
        if (version is null)
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' is missing required front matter key 'version'.");
        }
        if (job is null)
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' is missing required front matter key 'job'.");
        }

        var systemIndices = new List<int>();
        var userIndices = new List<int>();
        for (var i = closingDashIndex + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed == "<<<system>>>")
            {
                systemIndices.Add(i);
            }
            else if (trimmed == "<<<user>>>")
            {
                userIndices.Add(i);
            }
        }

        if (systemIndices.Count != 1)
        {
            throw new InvalidOperationException(
                $"Prompt '{mdLabel}' must have exactly one '<<<system>>>' line (found {systemIndices.Count}).");
        }
        if (userIndices.Count != 1)
        {
            throw new InvalidOperationException(
                $"Prompt '{mdLabel}' must have exactly one '<<<user>>>' line (found {userIndices.Count}).");
        }

        var sysIdx = systemIndices[0];
        var usrIdx = userIndices[0];
        if (usrIdx <= sysIdx)
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' must have '<<<system>>>' before '<<<user>>>'.");
        }

        for (var i = closingDashIndex + 1; i < sysIdx; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                throw new InvalidOperationException($"Prompt '{mdLabel}' has unexpected content before '<<<system>>>'.");
            }
        }

        var systemTemplate = string.Join('\u000A', lines[(sysIdx + 1)..usrIdx]).Trim();
        var userTemplate = string.Join('\u000A', lines[(usrIdx + 1)..]).Trim();
        if (userTemplate.Length == 0)
        {
            throw new InvalidOperationException($"Prompt '{mdLabel}' has an empty user template.");
        }

        var variables = new List<string>();
        var seenVariables = new HashSet<string>(StringComparer.Ordinal);
        ExtractVariables(systemTemplate, seenVariables, variables);
        ExtractVariables(userTemplate, seenVariables, variables);

        return new PromptDefinition(
            Id: id,
            Version: version.Value,
            ContentHash: contentHash,
            Job: job.Value,
            Description: description,
            Settings: new PromptSettings(temperature, maxOutputTokens, thinkingLevel),
            SystemTemplate: systemTemplate,
            UserTemplate: userTemplate,
            Schema: schema,
            Variables: variables,
            Source: source);
    }

    private static string ComputeContentHash(byte[] mdBytes, byte[] schemaBytes)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(mdBytes);
        sha.AppendData(schemaBytes);
        var hash = sha.GetHashAndReset();
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    private static JsonObject ParseSchema(string schemaLabel, string mdLabel, byte[] schemaBytes)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(schemaBytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Schema '{schemaLabel}' (for '{mdLabel}') contains invalid JSON: {ex.Message}",
                ex);
        }

        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException(
                $"Schema '{schemaLabel}' (for '{mdLabel}') must be a JSON object.");
        }

        Materialize(obj);
        return obj;
    }

    /// <summary>JsonNode.Parse builds child collections lazily on first access, which is not thread-safe. The schema is
    /// shared by every circuit and read concurrently (validator, cache key, request body) → build the whole tree now.</summary>
    private static void Materialize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (_, child) in o) Materialize(child);
                break;
            case JsonArray a:
                foreach (var child in a) Materialize(child);
                break;
        }
    }

    private static bool TryParseJob(string value, out AnalysisJob job)
    {
        foreach (var candidate in Enum.GetValues<AnalysisJob>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                job = candidate;
                return true;
            }
        }

        job = default;
        return false;
    }

    private static void ExtractVariables(string template, HashSet<string> seen, List<string> ordered)
    {
        foreach (Match match in PlaceholderRegex.Matches(template))
        {
            var name = match.Groups[2].Value;
            if (seen.Add(name))
            {
                ordered.Add(name);
            }
        }
    }
}
