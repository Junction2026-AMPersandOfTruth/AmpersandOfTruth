using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Stage 4 placeholder: one removed edit (+ its LLM reading, if any) → <see cref="IncidentEvidence"/> →
/// <see cref="TierRules.Assess"/> → <see cref="IncidentRecord"/>, or null when the evidence standard excludes it.
/// One edit = one incident until P1.S10 (#37) groups edits by claim cluster / conflict episode.
/// Wired: removal reason (S5, <see cref="RevertReasons"/>), re-insertion (S1 identity restores), distinct editors.
/// Not yet (left at their "no evidence" default): edit war (S4), dispute template, multiple articles, source change (S8),
/// kept-in-latest. Pure and thread-safe.
/// </summary>
public static partial class IncidentAssembler
{
    public sealed record Result(IncidentRecord? Incident, TierAssessment Assessment);

    public static Result Assemble(EditReading reading, EditClassification? classification)
    {
        var edit = reading.Edit;
        var revert = edit.Revert;
        var span = reading.Span;
        var diff = reading.Diff;

        var reverterKey = EditorDisplay.For(revert.User).GroupingKey;
        var assessment = classification is null ? null : Assessment(classification, reading.InsertedText);

        var reinsertions = reading.Reinsertions;
        var inserters = span.Concat(reinsertions).ToList();
        var removalReason = RevertReasons.Of(revert.Comment);

        var evidence = new IncidentEvidence
        {
            IsSubstantive = diff.Stats.IsSubstantive,
            RemovedByOtherAccount = revert.User is null || !span.All(r => EditorDisplay.For(r.User).GroupingKey == reverterKey),
            InsertedByBot = span.All(r => IsBot(r.User)),
            IsBlanking = diff.Stats.IsBlanking,
            Assessment = assessment is null
                ? null
                : new ContentAssessment(assessment.Category, assessment.Confidence, assessment.HasVerifiedQuote),
            RemovalReason = removalReason,
            ReinsertionCount = reinsertions.Count,
            InsertionSpan = reinsertions.Count == 0 ? TimeSpan.Zero : reinsertions[^1].Timestamp - span[0].Timestamp,
            DistinctInsertingEditors = Math.Max(1, inserters.Select(r => EditorDisplay.For(r.User).GroupingKey).Distinct().Count()),
        };

        var tier = TierRules.Assess(evidence);
        if (tier.Tier is not { } level) return new Result(null, tier);

        var first = span[0];
        var firstInserted = Utc(first.Timestamp);
        var removed = Utc(revert.Timestamp);
        var exposure = (long)Math.Max(0, (removed - firstInserted).TotalSeconds);

        var incident = new IncidentRecord
        {
            Id = edit.Key,
            PageId = edit.Article.PageId,
            InsertingRevIds = [.. inserters.Select(r => r.RevId)],
            RemovingRevIds = [revert.RevId],
            Tier = level,
            Score = Score(level, assessment, exposure, diff.Stats),
            TierReason = tier.BecauseSentence,
            ClaimEn = classification?.ClaimEn,
            ClaimFi = classification?.ClaimFi,
            SummaryEn = classification?.WhatChangedEn,
            Narrative = classification?.NarrativeEn,
            Category = assessment?.Category,
            Assessment = assessment,
            ConfidenceNote = classification?.UncertaintyNote,
            Articles = [new IncidentArticle(edit.Article.PageId, edit.Article.Title)],
            FirstInsertedAt = firstInserted,
            LastRemovedAt = removed,
            TotalExposureSeconds = exposure,
            ReinsertionCount = reinsertions.Count,
            Edits =
            [
                .. span.Select(r => Edit(r.RevId, r.ParentRevId, edit.Article.PageId, r.User, r.Timestamp, IncidentEditRole.Inserted, r.Comment)),
                Edit(revert.RevId, revert.RevertedRevId, edit.Article.PageId, revert.User, revert.Timestamp, IncidentEditRole.Removed, revert.Comment),
                .. reinsertions.Select(r => Edit(r.RevId, r.ParentRevId, edit.Article.PageId, r.User, r.Timestamp, IncidentEditRole.Reinserted, r.Comment)),
            ],
            Signals = [.. tier.Reasons.Select(r => new IncidentSignal(
                IncidentJson.Id(r.Kind), r.Text,
                r.Kind switch
                {
                    TierReasonKind.ClaimContent => assessment?.Quotes.FirstOrDefault(),
                    TierReasonKind.RemovalReason => EditorDisplay.MaskComment(RevertReasons.ReasonText(revert.Comment)),
                    _ => null,
                }))],
        };
        return new Result(incident, tier);
    }

    /// <summary>
    /// Placeholder ranking until P1.S10 (#37): tier first, then model confidence, then how long it stayed live and how much
    /// prose it added. Informational only — the order of the list is the ranking.
    /// </summary>
    internal static double Score(EvidenceTier tier, IncidentAssessment? assessment, long exposureSeconds, DiffStats stats) =>
        Math.Round(
            (int)tier * 10
            + (assessment?.Confidence ?? 0)
            + Math.Min(Math.Log10(exposureSeconds + 1), 8) * 0.5
            + Math.Min(stats.AddedProseChars / 500.0, 2),
            3);

    internal static IncidentAssessment Assessment(EditClassification c, string insertedText)
    {
        var quotes = c.Evidence.Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        return new IncidentAssessment(
            Category: Enum.Parse<ContentCategory>(c.Category.ToString()),
            Confidence: Math.Clamp(c.Confidence, 1, 5),
            WhatChangedEn: c.WhatChangedEn,
            Quotes: quotes,
            HasVerifiedQuote: quotes.Any(q => q.Trim().Length >= 3 && insertedText.Contains(q.Trim(), StringComparison.Ordinal)),
            UncertaintyNote: c.UncertaintyNote,
            HistoricalNoteEn: c.HistoricalNoteEn);
    }

    private static IncidentEdit Edit(long revId, long? parent, long pageId, string? user, DateTime ts, IncidentEditRole role, string? comment)
    {
        var label = EditorDisplay.For(user);
        return new IncidentEdit(
            RevId: revId,
            ParentRevId: parent,
            PageId: pageId,
            UserDisplay: label.PlainText,
            IsAnonymous: label.IsAnonymous,
            Timestamp: Utc(ts),
            Role: role,
            Summary: string.IsNullOrWhiteSpace(comment) ? null : EditorDisplay.MaskComment(comment),
            DiffRef: WikipediaLinks.Diff(revId).AbsoluteUri,
            Flags: role != IncidentEditRole.Removed && IsBot(user) ? ["bot"] : []);
    }

    private static DateTimeOffset Utc(DateTime ts) => new(DateTime.SpecifyKind(ts, DateTimeKind.Utc));

    /// <summary>P1.S1's bot rule: a named account ending in bot/botti (optionally + digits or punctuation), or a known bot.</summary>
    internal static bool IsBot(string? user) =>
        user is not null && EditorDisplay.For(user).Kind == EditorKind.Named && (BotSuffix().IsMatch(user) || KnownBots.Contains(user));

    private static readonly HashSet<string> KnownBots = new(StringComparer.Ordinal)
    {
        "Lab-ot", "CommonsDelinker", "ʬʭ", "Overlord", "Loveless", "Ptbotgourou", "RobotJcb",
        "BOTarate", "Bot-Schafter", "BotMultichill", "BotSottile", "BotdeSki",
    };

    [GeneratedRegex(@"bot(ti)?($|[0-9~_)\-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BotSuffix();
}
