using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Web.Features.Investigations.Fixtures;

public sealed record SampleTopic(
    string Slug,
    string Title,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<IncidentRecord> Incidents);

/// <summary>
/// Loads embedded sample-incident fixture files (<c>Features/Investigations/Fixtures/*.json</c>) once at startup.
/// <see cref="Parse"/> throws <see cref="InvalidOperationException"/> naming the file so tests fail on broken JSON;
/// the DI constructor logs and skips malformed files so app startup never crashes.
/// </summary>
public sealed class SampleIncidents
{
    internal const string EmbeddedPrefix = "AmpersandOfTruth.Web.Features.Investigations.Fixtures.";

    private readonly Dictionary<string, SampleTopic> _bySlug;

    public IReadOnlyList<SampleTopic> Topics { get; }

    public SampleIncidents(ILogger<SampleIncidents> log)
        : this(ReadEmbeddedFiles(), log)
    {
    }

    internal SampleIncidents(IEnumerable<(string FileName, string Json)> files, ILogger<SampleIncidents> log)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(log);

        var topics = new List<SampleTopic>();
        foreach (var (fileName, json) in files)
        {
            try
            {
                topics.Add(Parse(fileName, json));
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                log.LogError(ex, "Skipping malformed sample fixture '{FileName}'", fileName);
            }
        }

        Topics = topics;
        _bySlug = IndexBySlug(topics);
    }

    private SampleIncidents(IEnumerable<SampleTopic> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        var list = topics.ToList();
        Topics = list;
        _bySlug = IndexBySlug(list);
    }

    public static SampleIncidents From(params IEnumerable<SampleTopic> topics) => new(topics);

    /// <summary>Matches a topic query or slug against each topic's slug, title, or aliases via <see cref="TopicSlug.From"/>.</summary>
    public SampleTopic? Match(string topicOrSlug)
    {
        ArgumentNullException.ThrowIfNull(topicOrSlug);
        var key = TopicSlug.From(topicOrSlug);
        return key.Length == 0 ? null : _bySlug.GetValueOrDefault(key);
    }

    internal static IReadOnlyList<(string FileName, string Json)> ReadEmbeddedFiles(Assembly? assembly = null)
    {
        var asm = assembly ?? typeof(SampleIncidents).Assembly;
        var resources = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var result = new List<(string FileName, string Json)>(resources.Count);
        foreach (var resourceName in resources)
        {
            var fileName = resourceName[EmbeddedPrefix.Length..];
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded fixture '{fileName}' could not be opened.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            result.Add((fileName, reader.ReadToEnd()));
        }

        return result;
    }

    /// <summary>Parses and validates one fixture JSON document. Throws <see cref="InvalidOperationException"/> naming <paramref name="fileName"/> on error.</summary>
    public static SampleTopic Parse(string fileName, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (json is null)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' is null.");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' contains invalid JSON: {ex.Message}", ex);
        }

        if (rootNode is not JsonObject obj)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' root must be a JSON object.");
        }

        var slug = ReadRequiredString(obj, "slug", fileName);
        if (TopicSlug.From(slug).Length == 0)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' has an empty normalised slug '{slug}'.");
        }

        var title = ReadRequiredString(obj, "title", fileName);
        var aliases = ReadAliases(obj, fileName);

        if (!obj.TryGetPropertyValue("incidents", out var incidentsNode) || incidentsNode is not JsonArray incidentsArray)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' must contain an 'incidents' JSON array.");
        }

        var incidents = new List<IncidentRecord>(incidentsArray.Count);
        for (var i = 0; i < incidentsArray.Count; i++)
        {
            var item = incidentsArray[i];
            if (item is not JsonObject)
            {
                throw new InvalidOperationException($"Fixture '{fileName}' incidents[{i}] must be a JSON object.");
            }

            IncidentRecord? record;
            try
            {
                record = item.Deserialize<IncidentRecord>(IncidentJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException)
            {
                throw new InvalidOperationException($"Fixture '{fileName}' incidents[{i}] is invalid: {ex.Message}", ex);
            }

            if (record is null)
            {
                throw new InvalidOperationException($"Fixture '{fileName}' incidents[{i}] deserialised to null.");
            }

            ValidateIncident(record, fileName, i);
            incidents.Add(record);
        }

        return new SampleTopic(slug, title, aliases, incidents);
    }

    private static string ReadRequiredString(JsonObject obj, string property, string fileName)
    {
        if (!obj.TryGetPropertyValue(property, out var node)
            || node is not JsonValue val
            || val.GetValueKind() != JsonValueKind.String
            || !val.TryGetValue<string>(out var str)
            || string.IsNullOrWhiteSpace(str))
        {
            throw new InvalidOperationException($"Fixture '{fileName}' is missing non-empty string property '{property}'.");
        }

        return str.Trim();
    }

    private static IReadOnlyList<string> ReadAliases(JsonObject obj, string fileName)
    {
        if (!obj.TryGetPropertyValue("aliases", out var node) || node is null)
        {
            return [];
        }

        if (node is not JsonArray arr)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' property 'aliases' must be a JSON array.");
        }

        var aliases = new List<string>(arr.Count);
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JsonValue val
                || val.GetValueKind() != JsonValueKind.String
                || !val.TryGetValue<string>(out var alias)
                || string.IsNullOrWhiteSpace(alias))
            {
                throw new InvalidOperationException($"Fixture '{fileName}' aliases[{i}] must be a non-empty string.");
            }

            aliases.Add(alias.Trim());
        }

        return aliases;
    }

    private static void ValidateIncident(IncidentRecord record, string fileName, int index)
    {
        if (record.PageId < 1)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' incidents[{index}] must have page_id >= 1.");
        }

        if (record.InsertingRevIds is null || record.InsertingRevIds.Count == 0 || record.InsertingRevIds.Any(id => id < 1))
        {
            throw new InvalidOperationException($"Fixture '{fileName}' incidents[{index}] must have at least one positive inserting_rev_id.");
        }

        if (record.InsertingRevIds.Distinct().Count() != record.InsertingRevIds.Count)
        {
            throw new InvalidOperationException($"Fixture '{fileName}' incidents[{index}] has duplicate inserting_rev_ids.");
        }

        if (record.RemovingRevIds is null || record.RemovingRevIds.Count == 0 || record.RemovingRevIds.Any(id => id < 1))
        {
            throw new InvalidOperationException($"Fixture '{fileName}' incidents[{index}] must have at least one positive removing_rev_id.");
        }

        if (!Enum.IsDefined(record.Tier))
        {
            throw new InvalidOperationException($"Fixture '{fileName}' incidents[{index}] has invalid tier '{record.Tier}'.");
        }
    }

    private static Dictionary<string, SampleTopic> IndexBySlug(IReadOnlyList<SampleTopic> topics)
    {
        var map = new Dictionary<string, SampleTopic>(StringComparer.Ordinal);
        foreach (var topic in topics)
        {
            AddKey(map, topic.Slug, topic);
            AddKey(map, topic.Title, topic);
            foreach (var alias in topic.Aliases)
            {
                AddKey(map, alias, topic);
            }
        }

        return map;
    }

    private static void AddKey(Dictionary<string, SampleTopic> map, string raw, SampleTopic topic)
    {
        var slug = TopicSlug.From(raw);
        if (slug.Length > 0)
        {
            map.TryAdd(slug, topic);
        }
    }
}
