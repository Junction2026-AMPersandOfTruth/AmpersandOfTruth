namespace AmpersandOfTruth.Web.Features.Evidence;

/// <param name="Id">Canonical machine id (snake_case).</param>
/// <param name="Chip">Short plain-language text for chips and filters.</param>
/// <param name="Why">One generic sentence for the Why-flagged panel; the incident's own evidence text goes under it.</param>
/// <param name="IsKnown">False = an id the catalog doesn't know; Chip/Why are then derived from the label or id.</param>
public sealed record SignalPhrase(string Id, string Chip, string Why, bool IsKnown = true);

/// <summary>
/// The one signal id → wording mapping (#98 §5 gap 1, phrasebook in <c>docs/design/plan/3-states-and-trust.md</c>).
/// Canonical ids = the <see cref="TierReasonKind"/> names in snake_case (what the pipeline emits, see
/// <c>IncidentJson.Id</c>) plus a few signals that are not tier reasons. Older ids from the design phrasebook and
/// the schema examples are accepted as aliases. Wording follows the copy rules: no "revert", no "tier", no intent.
/// </summary>
public static class SignalCatalog
{
    public static IReadOnlyList<SignalPhrase> All { get; } =
    [
        new("removed_by_others", "Removed by another editor", "Another editor took this text out."),
        new("removal_reason", "Removal reason given",
            "The editor who removed it gave a reason about accuracy, neutrality or sources."),
        new("reinserted", "Put back after removal", "The same text was put back after editors removed it."),
        new("edit_war", "Back-and-forth edits", "Editors removed and restored this passage several times in a short period."),
        new("dispute_template", "Marked as disputed", "An editor marked the article as disputed around this change."),
        new("claim_content", "Factual claim",
            "The automated assessment reads the added text as a factual or narrative claim; the quoted words are shown beside it."),
        new("multiple_editors", "Several accounts", "More than one account added this text."),
        new("multiple_articles", "In several articles", "The same text was added to more than one article."),
        new("persistence", "Put back over time", "The text was put back several times over a week or longer."),
        new("source_change", "Changed sources", "The edit also changed the sources cited for this passage."),
        new("citation_removed", "Removed a citation", "The edit deleted a source that supported the previous wording."),
        new("number_changed", "Changed a number", "A figure was changed without a new source."),
        new("new_accounts", "New accounts", "Accounts that added it were created shortly before their first edit to it."),
        new("anonymous", "Anonymous edits", "It was added from IP addresses, without an account."),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["reinserted_after_revert"] = "reinserted",
        ["cross_article"] = "multiple_articles",
        ["removal_reason_accuracy"] = "removal_reason",
        ["removal_reason_neutrality"] = "removal_reason",
        ["removal_reason_sourcing"] = "removal_reason",
        ["multiple_accounts"] = "multiple_editors",
    };

    private static readonly Dictionary<string, SignalPhrase> ById = All.ToDictionary(p => p.Id, StringComparer.Ordinal);

    /// <summary>Canonical id for <paramref name="id"/> (trimmed, lower-cased, aliases resolved).</summary>
    public static string Normalize(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var key = id.Trim().ToLowerInvariant().Replace('-', '_');
        return Aliases.GetValueOrDefault(key, key);
    }

    /// <summary>
    /// Wording for a signal. Unknown ids never throw: they get <paramref name="label"/> (or the id with spaces) as the chip
    /// and <paramref name="label"/> or an empty string as the sentence, with <see cref="SignalPhrase.IsKnown"/> false.
    /// </summary>
    public static SignalPhrase For(string id, string? label = null)
    {
        var key = Normalize(id);
        if (ById.TryGetValue(key, out var phrase)) return phrase;
        var chip = string.IsNullOrWhiteSpace(label) ? char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ') : label.Trim();
        return new SignalPhrase(key, chip, label?.Trim() ?? "", IsKnown: false);
    }

    /// <summary>The canonical id of a tier reason, e.g. <see cref="TierReasonKind.MultipleEditors"/> → <c>multiple_editors</c>.</summary>
    public static string IdOf(TierReasonKind kind) =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString());
}
