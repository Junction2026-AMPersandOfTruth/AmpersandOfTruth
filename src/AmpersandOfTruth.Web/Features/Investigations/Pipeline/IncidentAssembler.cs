using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Stage 4 placeholder: one removed edit (+ its LLM reading, if any) → <see cref="IncidentEvidence"/> →
/// <see cref="TierRules.Assess"/> → <see cref="IncidentRecord"/>, or null when the evidence standard excludes it.
/// One edit = one incident here: its re-insertions are exact restores of its own content, so everything counted is on the
/// claim's side (the frozen contract's claim-side counting, CONTRACT.md §3) without S7 clusters. A re-insertion chain
/// reverted several times gives one incident per revert; <see cref="IncidentMerger"/> folds them into one after stage 4.
/// Wired: removal reason (S5, <see cref="RevertReasons"/>; a block-evasion revert is procedural, so its put-backs are no
/// dispute), re-insertion (S1 identity restores), distinct editors, passage-level edit war (S10, <see cref="EditWars.Passage"/>),
/// source change (S8 citations removed/swapped, or an assessment of source manipulation, as S10), kept in the latest revision
/// (S10, <see cref="KeptInLatest"/>, passed in by the pipeline). Not yet (left at their "no evidence" default): dispute
/// template, multiple articles.
/// Pure and thread-safe.
/// </summary>
public static partial class IncidentAssembler
{
/// <param name="WithinTier">P1.S10's additive score <see cref="ScoreA"/>; the pipeline multiplies it by topic relevance.</param>
    public sealed record Result(IncidentRecord? Incident, TierAssessment Assessment, double WithinTier = 0);

    /// <param name="keptInLatest">The added content is still in the page's latest revision (<see cref="KeptInLatest"/>):
    /// the evidence standard's <c>kept</c> exclusion. The pipeline checks it once per edit, before the LLM call.</param>
    public static Result Assemble(EditReading reading, EditClassification? classification, bool keptInLatest = false)
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
        var reRemovals = reading.ReRemovals;

        var evidence = new IncidentEvidence
        {
            IsSubstantive = diff.Stats.IsSubstantive,
            RemovedByOtherAccount = revert.User is null || !span.All(r => EditorDisplay.For(r.User).GroupingKey == reverterKey),
            InsertedByBot = span.All(r => IsBot(r.User)),
            IsBlanking = diff.Stats.IsBlanking,
            KeptInLatestRevision = keptInLatest,
            Assessment = assessment is null
                ? null
                : new ContentAssessment(assessment.Category, assessment.Confidence, assessment.HasVerifiedQuote),
            RemovalReason = removalReason,
            ProceduralRemoval = RevertReasons.IsProcedural(revert.Comment),
            ReinsertionCount = reinsertions.Count,
            InsertionSpan = reinsertions.Count == 0 ? TimeSpan.Zero : reinsertions[^1].Timestamp - span[0].Timestamp,
            DistinctInsertingEditors = Math.Max(1, inserters.Select(r => EditorDisplay.For(r.User).GroupingKey).Distinct().Count()),
            InEditWar = reading.InPassageEditWar,
            SourceManipulation = reading.SourceChanges.Count > 0 || assessment?.Category == ContentCategory.SourceManipulation,
        };

        var tier = TierRules.Assess(evidence);
        if (tier.Tier is not { } level) return new Result(null, tier);


        var first = span[0];
        var firstInserted = Utc(first.Timestamp);
        var removed = Utc(revert.Timestamp);   // the first removal: the exposure window is the first live window
        var exposure = (long)Math.Max(0, (removed - firstInserted).TotalSeconds);
        var scoreA = ScoreA(evidence, diff.Stats.AddedProseChars, exposure);

        var incident = new IncidentRecord
        {
            Id = edit.Key,
            PageId = edit.Article.PageId,
            InsertingRevIds = [.. inserters.Select(r => r.RevId)],
            RemovingRevIds = [revert.RevId, .. reRemovals.Select(r => r.RevId)],
            Tier = level,
            Score = Rank(level, scoreA, relevance: 1),
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
            LastRemovedAt = reRemovals.Count > 0 ? Utc(reRemovals[^1].Timestamp) : removed,
            TotalExposureSeconds = exposure,
            ReinsertionCount = reinsertions.Count,
            Edits = ((IEnumerable<IncidentEdit>)
            [
                .. span.Select(r => Edit(r.RevId, r.ParentRevId, edit.Article.PageId, r.User, r.Timestamp, IncidentEditRole.Inserted, r.Comment)),
                Edit(revert.RevId, revert.RevertedRevId, edit.Article.PageId, revert.User, revert.Timestamp, IncidentEditRole.Removed, revert.Comment),
                .. reinsertions.Select(r => Edit(r.RevId, r.ParentRevId, edit.Article.PageId, r.User, r.Timestamp, IncidentEditRole.Reinserted, r.Comment)),
                .. reRemovals.Select(r => Edit(r.RevId, r.ParentRevId, edit.Article.PageId, r.User, r.Timestamp, IncidentEditRole.Removed, r.Comment)),
            ])
            .OrderBy(e => e.Timestamp).ThenBy(e => e.RevId).ToList(),
            Signals = [.. tier.Reasons.Select(r => new IncidentSignal(
                IncidentJson.Id(r.Kind), r.Text,
                r.Kind switch
                {
                    TierReasonKind.ClaimContent => assessment?.Quotes.FirstOrDefault(),
                    TierReasonKind.RemovalReason => EditorDisplay.MaskComment(RevertReasons.ReasonText(revert.Comment)),
                    _ => null,
                }))],
        };
        return new Result(incident, tier, scoreA);
    }

    /// <summary>
    /// P1.S10 (#37) mode B+rel: tier first, then the additive score scaled by topic relevance (0–1). The list order is the
    /// ranking; <c>score</c> keeps it sortable on its own (contract: higher = more relevant).
    /// </summary>
    public static double Rank(EvidenceTier tier, double scoreA, double relevance) =>
        Math.Round((int)tier * 100.0 + scoreA * Math.Clamp(relevance, 0, 1), 4);

    /// <summary>
    /// P1.S10's hand-weighted additive score (<c>research/37-s10-assembly/export_classify.py score_variant_a</c>), ≥ 0.1.
    /// </summary>
    public static double ScoreA(IncidentEvidence ev, int proseChars, long exposureSeconds)
    {
        var score = 2.0;
        score += 0.6 * Math.Log(1 + Math.Min(Math.Max(0, proseChars), 4000));
        score += 0.25 * Math.Log(1 + Math.Min(Math.Max(0, exposureSeconds), 86400L * 365) / 60.0);

        score += ev.RemovalReason switch
        {
            RemovalReason.Accuracy or RemovalReason.Neutrality or RemovalReason.Sourcing => 2.5,
            RemovalReason.Other => 0.5,
            RemovalReason.Vandalism => -1.5,
            _ => 0,
        };

        if (ev.ReinsertionCount > 0) score += 2.2 * Math.Log(1 + ev.ReinsertionCount);
        if (ev.InEditWar) score += 1.8;
        if (ev.DisputeTemplateAdded) score += 1.5;
        if (ev.DistinctInsertingEditors > 1) score += 1.8 * Math.Log2(ev.DistinctInsertingEditors);
        if (ev.DistinctArticles > 1) score += 2.0 * Math.Log2(ev.DistinctArticles);

        var spanDays = Math.Max(0, ev.InsertionSpan.TotalDays);
        if (spanDays > 0) score += 0.5 * Math.Log(1 + spanDays);
        if (ev.ReinsertionCount >= 2 && spanDays >= 7) score += 1.0;
        if (ev.SourceManipulation) score += 1.5;

        if (ev.Assessment is { } a)
        {
            var scale = Math.Clamp(a.Confidence, 1, 5) / 5.0;
            score += a.Category switch
            {
                ContentCategory.ContentDispute or ContentCategory.PovPush or ContentCategory.FalseFactualClaim
                    or ContentCategory.SourceManipulation => 3.0 * scale + (a.HasVerifiedQuote ? 1.0 : 0),
                ContentCategory.GoodFaithError or ContentCategory.Other => -0.5 * scale,
                ContentCategory.Vandalism or ContentCategory.TestOrNonsense => -2.5 * scale,
                _ => 0,
            };
        }
        return Math.Max(0.1, score);
    }

    /// <summary>What S10 compares with the topic: the claim if the assessment named one, else the added prose, else the
    /// added wikitext — the first 500 characters.</summary>
    public static string RelevanceText(EditReading reading, EditClassification? classification)
    {
        static string Cut(string s) => s.Length <= 500 ? s : s[..500];
        if (classification?.ClaimEn is { } claim && !string.IsNullOrWhiteSpace(claim)) return Cut(claim.Trim());
        var prose = WikitextProse.Of(reading.InsertedText).Prose.Trim();
        return Cut(prose.Length > 0 ? prose : reading.InsertedText.Trim());
    }

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
