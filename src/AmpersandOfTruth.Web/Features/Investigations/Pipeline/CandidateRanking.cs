using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Stage 2's pre-ranking of identity reverts before anything is read or classified: P1.S3's selected formula
/// (<c>f3_dispute_gated</c>, <c>research/30-s3-extract/README.md</c>; judged p@10 0.62 on test with no LLM) on the inputs a
/// revert row carries. Where S3 used the diff, this uses a stand-in, as the #120 review proposed:
/// <list type="bullet">
/// <item>prose chars → <see cref="RevertEvent.UndoneBytes"/>; "substantive claim" → ≥ 50 bytes added; refs added → not used;</item>
/// <item>re-insertions → <see cref="RevertEvent.Reappearances"/> (the whole span put back);</item>
/// <item>S1 classes from the last undone editor: <c>dispute</c> = a named account or put back; <c>graffiti_cleanup</c> =
/// anonymous and undone within 24 h; <c>edit_war</c> (needs other reverts) → not used;</item>
/// <item>distinct editors in the span → not used (only the last undone editor is on the row).</item>
/// </list>
/// Removal reasons are the S5 lexicon labels of the revert's summary (<see cref="RevertReasons"/>). Pure.
/// </summary>
public static class CandidateRanking
{
    private static readonly string[] AccuracyOrPov = [RevertReasons.FactualError, RevertReasons.NeutralityPov, RevertReasons.OriginalResearch];
    private static readonly string[] SourcingOrConsensus = [RevertReasons.Sourcing, RevertReasons.Consensus];
    private static readonly string[] Noise = [RevertReasons.VandalismTest, RevertReasons.Maintenance, RevertReasons.Copyright];

    public static double Score(RevertEvent r)
    {
        var labels = RevertReasons.Categories(RevertReasons.ReasonText(r.Comment));
        var accPov = AccuracyOrPov.Any(labels.Contains);
        var srcCon = SourcingOrConsensus.Any(labels.Contains);
        var disputeReason = accPov || srcCon;
        var noise = Noise.Any(labels.Contains);

        var prose = Math.Max(0, r.UndoneBytes);
        var substantive = prose >= 50;
        var exposure = r.FirstUndoneAt is { } start ? Math.Max(0, (r.Timestamp - start).TotalHours) : 0;

        var inserter = EditorDisplay.For(r.RevertedUser).Kind;
        var named = inserter == EditorKind.Named;
        var anonymous = inserter is EditorKind.IPv4 or EditorKind.IPv6 or EditorKind.Temporary;
        var reinserted = r.Reappearances;
        var disputeClass = named || reinserted > 0;
        var graffitiCleanup = !disputeClass && anonymous && exposure < 24;
        var communityDispute = reinserted >= 1 || disputeReason;

        return 0.55 * Math.Log(1 + Math.Min(prose, 1800))
            + (r.UndoneBytes <= 0 ? -1.80 : substantive ? 0.50 : 0)
            + 0.22 * Math.Log(1 + exposure)
            + (accPov ? 2.30 : srcCon ? 1.70 : 0)
            + 2.10 * Math.Log(1 + reinserted)
            + (disputeClass ? 0.90 : graffitiCleanup ? -1.10 : 0)
            + (substantive && communityDispute ? 2.50 : 0)
            + (named ? 0.90 : 0)
            - (noise && !disputeReason ? 2.20 : 0);
    }

    /// <summary>S1's exclusions that the row can decide without reading: a bot revert, a lone bot edit, a self-revert of
    /// one edit. (The tier rules exclude the rest after reading.)</summary>
    public static bool Excluded(RevertEvent r) =>
        IncidentAssembler.IsBot(r.User)
        || r.RevertedCount == 1 && (IncidentAssembler.IsBot(r.RevertedUser)
            || r.User is not null && string.Equals(r.User, r.RevertedUser, StringComparison.Ordinal));
}
