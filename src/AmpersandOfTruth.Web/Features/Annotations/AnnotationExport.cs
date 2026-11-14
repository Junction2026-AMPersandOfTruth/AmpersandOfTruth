using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Web.Features.Annotations;

/// <summary>
/// Exports analyst-annotated incidents as JSON Lines where each line matches
/// <c>research/eval/schemas/golden.schema.json</c> (<c>$defs/item</c>).
/// </summary>
public static class AnnotationExport
{
    private const char Cr = (char)13;
    private const char Lf = (char)10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToGoldenJsonl(
        string topicSlug,
        IReadOnlyList<IncidentRecord> incidents,
        IReadOnlyDictionary<string, IncidentAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(annotations);

        var items = new List<AnnotatedIncident>();
        foreach (var incident in incidents)
        {
            var incidentId = ResolveIncidentId(incident);
            if (annotations.TryGetValue(incidentId, out var annotation))
            {
                items.Add(new AnnotatedIncident(
                    annotation.InvestigationId,
                    topicSlug ?? "",
                    topicSlug ?? "",
                    incidentId,
                    incident,
                    annotation));
            }
        }

        return ToGoldenJsonl(items);
    }

    public static string ToGoldenJsonl(IEnumerable<AnnotatedIncident> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var sb = new StringBuilder();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in items)
        {
            var mapped = TryMapGoldenItem(entry, usedIds);
            if (mapped is null)
            {
                continue;
            }

            sb.Append(JsonSerializer.Serialize(mapped, JsonOptions));
            sb.Append(Lf);
        }

        return sb.ToString();
    }

    public static string ResolveIncidentId(IncidentRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (!string.IsNullOrWhiteSpace(r.Id))
        {
            return r.Id;
        }

        var firstRevId = r.InsertingRevIds.Count > 0 ? r.InsertingRevIds[0] : 0L;
        return string.Create(CultureInfo.InvariantCulture, $"{r.PageId}-{firstRevId}");
    }

    private static GoldenItemDto? TryMapGoldenItem(AnnotatedIncident entry, HashSet<string> usedIds)
    {
        var annotation = entry.Annotation;
        var incident = entry.Incident;

        string label;
        string? exclusion;
        bool humanVerified;

        if (annotation.Verdict == AnalystVerdict.NeedsMoreEvidence)
        {
            return null;
        }

        if (annotation.Verdict == AnalystVerdict.NotAnIncident
            || (annotation.Verdict is null && annotation.DismissReason is not null))
        {
            label = "not-incident";
            exclusion = MapExclusion(annotation.DismissReason);
            humanVerified = true;
        }
        else if (annotation.Verdict is AnalystVerdict.Confirmed or AnalystVerdict.Plausible)
        {
            label = EvidenceTiers.Get(incident.Tier).Id;
            exclusion = null;
            humanVerified = annotation.Verdict == AnalystVerdict.Confirmed;
        }
        else
        {
            // Note-only annotation (e.g. "looks_wrong" without an analyst verdict yet).
            return null;
        }

        var insertingRevIds = incident.InsertingRevIds
            .Where(static r => r >= 1)
            .Distinct()
            .ToList();
        if (insertingRevIds.Count == 0)
        {
            return null;
        }

        var removingRevIds = incident.RemovingRevIds
            .Where(static r => r >= 1)
            .Distinct()
            .ToList();

        var topicId = ToKebabId(
            string.IsNullOrWhiteSpace(entry.TopicSlug) ? entry.Topic : entry.TopicSlug,
            fallback: "topic");
        var baseItemSuffix = ToKebabId(entry.IncidentId, fallback: $"p{incident.PageId}-{insertingRevIds[0]}");
        var itemId = EnsureUniqueId($"{topicId}/{baseItemSuffix}", usedIds);

        var otherPageIds = incident.OtherPageIds
            .Where(p => p >= 1 && p != incident.PageId)
            .Distinct()
            .ToList();

        var category = (incident.Category ?? incident.Assessment?.Category) is { } cat
            ? IncidentJson.Id(cat)
            : null;

        var description = BuildDescription(incident, entry.IncidentId);
        var why = BuildWhy(annotation, incident);
        var notes = string.IsNullOrWhiteSpace(annotation.Note)
            ? null
            : EditorDisplay.MaskComment(annotation.Note.Trim());

        return new GoldenItemDto
        {
            Id = itemId,
            Label = label,
            Exclusion = exclusion,
            Category = category,
            PageId = incident.PageId,
            OtherPageIds = otherPageIds.Count > 0 ? otherPageIds : null,
            InsertingRevIds = insertingRevIds,
            RemovingRevIds = removingRevIds,
            Description = description,
            Why = why,
            HumanVerified = humanVerified,
            LabelledBy = ["human:analyst"],
            Notes = notes,
        };
    }

    private static string MapExclusion(DismissReason? reason) =>
        reason switch
        {
            DismissReason.OrdinaryVandalism => "graffiti",
            _ => "not-substantive",
        };

    private static string BuildDescription(IncidentRecord incident, string incidentId)
    {
        var raw = FirstNonBlank(incident.ClaimEn, incident.SummaryEn, incident.ClaimFi)
            ?? string.Create(CultureInfo.InvariantCulture, $"Incident {incidentId} on page {incident.PageId}");

        var sb = new StringBuilder(raw.Length);
        var prevSpace = false;
        foreach (var ch in raw.AsSpan().Trim())
        {
            if (ch == Cr || ch == Lf)
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevSpace = ch == ' ';
            }
        }

        return EditorDisplay.MaskComment(sb.ToString());
    }

    private static string BuildWhy(IncidentAnnotation annotation, IncidentRecord incident)
    {
        var parts = new List<string>(3);

        if (annotation.Verdict is { } verdict)
        {
            parts.Add(AnnotationLabels.ForVerdict(verdict));
        }

        if (annotation.DismissReason is { } dismiss)
        {
            parts.Add($"Dismiss reason: {AnnotationLabels.ForDismissReason(dismiss)}");
        }

        if (!string.IsNullOrWhiteSpace(annotation.Note))
        {
            parts.Add(EditorDisplay.MaskComment(annotation.Note.Trim()));
        }
        else if (!string.IsNullOrWhiteSpace(incident.TierReason))
        {
            parts.Add(EditorDisplay.MaskComment(incident.TierReason.Trim()));
        }

        return parts.Count > 0 ? string.Join(" — ", parts) : "Analyst export.";
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string EnsureUniqueId(string candidate, HashSet<string> usedIds)
    {
        if (usedIds.Add(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (true)
        {
            var next = string.Create(CultureInfo.InvariantCulture, $"{candidate}-{suffix}");
            if (usedIds.Add(next))
            {
                return next;
            }

            suffix++;
        }
    }

    private static string ToKebabId(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var dash = false;

        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                if (dash && sb.Length > 0)
                {
                    sb.Append('-');
                }

                sb.Append(lower);
                dash = false;
            }
            else
            {
                dash = true;
            }
        }

        return sb.Length > 0 ? sb.ToString() : fallback;
    }

    private sealed class GoldenItemDto
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public string? Exclusion { get; init; }
        public string? Category { get; init; }
        public required long PageId { get; init; }
        public IReadOnlyList<long>? OtherPageIds { get; init; }
        public required IReadOnlyList<long> InsertingRevIds { get; init; }
        public required IReadOnlyList<long> RemovingRevIds { get; init; }
        public required string Description { get; init; }
        public required string Why { get; init; }
        public required bool HumanVerified { get; init; }
        public IReadOnlyList<string>? LabelledBy { get; init; }
        public string? Notes { get; init; }
    }
}
