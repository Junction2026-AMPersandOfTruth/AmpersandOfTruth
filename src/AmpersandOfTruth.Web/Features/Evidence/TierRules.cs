namespace AmpersandOfTruth.Web.Features.Evidence;

public static class TierRules
{
    public static TierAssessment Assess(IncidentEvidence evidence, TierThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.ReinsertionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), evidence.ReinsertionCount, "ReinsertionCount must be non-negative.");
        }

        if (evidence.DistinctInsertingEditors < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), evidence.DistinctInsertingEditors, "DistinctInsertingEditors must be at least 1.");
        }

        if (evidence.DistinctArticles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), evidence.DistinctArticles, "DistinctArticles must be at least 1.");
        }

        if (evidence.Assessment is { Confidence: < 1 or > 5 })
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), evidence.Assessment.Confidence, "Assessment confidence must be between 1 and 5.");
        }

        var t = thresholds ?? TierThresholds.Default;

        bool isConfident = evidence.Assessment is { Confidence: var conf } && conf >= t.MinModelConfidence;

        // 2. Exclusions, first match wins → new(null, reason, []):
        //    !IsSubstantive→NotSubstantive; !RemovedByOtherAccount→SelfRemoval; InsertedByBot→Bot;
        //    IsBlanking→Blanking; KeptInLatestRevision→Kept; confident && Category ∈ {Vandalism, TestOrNonsense}→Graffiti.
        if (!evidence.IsSubstantive)
        {
            return new TierAssessment(null, ExclusionReason.NotSubstantive, []);
        }

        if (!evidence.RemovedByOtherAccount)
        {
            return new TierAssessment(null, ExclusionReason.SelfRemoval, []);
        }

        if (evidence.InsertedByBot)
        {
            return new TierAssessment(null, ExclusionReason.Bot, []);
        }

        if (evidence.IsBlanking)
        {
            return new TierAssessment(null, ExclusionReason.Blanking, []);
        }

        if (evidence.KeptInLatestRevision)
        {
            return new TierAssessment(null, ExclusionReason.Kept, []);
        }

        if (isConfident && evidence.Assessment!.Category is ContentCategory.Vandalism or ContentCategory.TestOrNonsense)
        {
            return new TierAssessment(null, ExclusionReason.Graffiti, []);
        }

        // 3. isClaim = confident && HasVerifiedQuote && Category ∈ {ContentDispute, PovPush, FalseFactualClaim, SourceManipulation}.
        bool isClaim = isConfident
            && evidence.Assessment!.HasVerifiedQuote
            && evidence.Assessment.Category is ContentCategory.ContentDispute
                or ContentCategory.PovPush
                or ContentCategory.FalseFactualClaim
                or ContentCategory.SourceManipulation;

        // 4. dispute reasons, in this order:
        //    RemovalReason ∈ {Accuracy, Neutrality, Sourcing}; ReinsertionCount >= 1 && !ProceduralRemoval; InEditWar;
        //    DisputeTemplateAdded.
        var disputeReasons = new List<TierReason>();

        switch (evidence.RemovalReason)
        {
            case RemovalReason.Accuracy:
                disputeReasons.Add(new TierReason(TierReasonKind.RemovalReason, "removed as inaccurate"));
                break;
            case RemovalReason.Neutrality:
                disputeReasons.Add(new TierReason(TierReasonKind.RemovalReason, "removed as not neutral"));
                break;
            case RemovalReason.Sourcing:
                disputeReasons.Add(new TierReason(TierReasonKind.RemovalReason, "removed for lack of a reliable source"));
                break;
        }

        // A put-back after a procedural removal (block evasion) restores an edit undone for who made it, not for what it
        // says: no disagreement about the content (#38 red team). A later back-and-forth still counts as an edit war.
        if (evidence.ReinsertionCount >= 1 && !evidence.ProceduralRemoval)
        {
            string reinsertedText = evidence.ReinsertionCount == 1
                ? "put back once after removal"
                : $"put back {evidence.ReinsertionCount} times after removal";
            disputeReasons.Add(new TierReason(TierReasonKind.Reinserted, reinsertedText));
        }

        if (evidence.InEditWar)
        {
            disputeReasons.Add(new TierReason(TierReasonKind.EditWar, "part of a back-and-forth over this passage"));
        }

        if (evidence.DisputeTemplateAdded)
        {
            disputeReasons.Add(new TierReason(TierReasonKind.DisputeTemplate, "the article was marked as disputed"));
        }

        // 5. If !isClaim or no dispute reason → Reverted, reasons = [RemovedByOthers].
        if (!isClaim || disputeReasons.Count == 0)
        {
            return new TierAssessment(
                EvidenceTier.Reverted,
                null,
                [new TierReason(TierReasonKind.RemovedByOthers, "taken out by another editor")]);
        }

        // 6. coordination reasons, in this order:
        //    DistinctInsertingEditors >= t.MinDistinctEditors (MultipleEditors);
        //    DistinctArticles >= t.MinDistinctArticles (MultipleArticles);
        //    ReinsertionCount >= t.MinPersistenceReinsertions && InsertionSpan >= t.MinPersistenceSpan (Persistence);
        //    SourceManipulation (SourceChange).
        var coordinationReasons = new List<TierReason>();
        bool multipleEditorsFired = false;
        bool multipleArticlesFired = false;
        bool persistenceFired = false;

        if (evidence.DistinctInsertingEditors >= t.MinDistinctEditors)
        {
            multipleEditorsFired = true;
            coordinationReasons.Add(new TierReason(
                TierReasonKind.MultipleEditors,
                $"added by {evidence.DistinctInsertingEditors} different editors"));
        }

        if (evidence.DistinctArticles >= t.MinDistinctArticles)
        {
            multipleArticlesFired = true;
            coordinationReasons.Add(new TierReason(
                TierReasonKind.MultipleArticles,
                $"added to {evidence.DistinctArticles} articles"));
        }

        if (evidence.ReinsertionCount >= t.MinPersistenceReinsertions && evidence.InsertionSpan >= t.MinPersistenceSpan)
        {
            persistenceFired = true;
            int days = (int)Math.Floor(evidence.InsertionSpan.TotalDays);
            coordinationReasons.Add(new TierReason(
                TierReasonKind.Persistence,
                $"put back {evidence.ReinsertionCount} times over {days} days"));
        }

        if (evidence.SourceManipulation)
        {
            coordinationReasons.Add(new TierReason(
                TierReasonKind.SourceChange,
                "came with changes to the cited sources"));
        }

        var claimReason = new TierReason(
            TierReasonKind.ClaimContent,
            "automated assessment: a factual or narrative claim");

        // 7. If coordination count >= t.MinCoordinationSignals AND (MultipleEditors or MultipleArticles fired) → Campaign,
        //    reasons = coordination reasons + dispute reasons (drop the Reinserted reason when Persistence fired) + ClaimContent.
        if (coordinationReasons.Count >= t.MinCoordinationSignals && (multipleEditorsFired || multipleArticlesFired))
        {
            var campaignReasons = new List<TierReason>(coordinationReasons);

            foreach (var reason in disputeReasons)
            {
                if (persistenceFired && reason.Kind == TierReasonKind.Reinserted)
                {
                    continue;
                }

                campaignReasons.Add(reason);
            }

            campaignReasons.Add(claimReason);

            return new TierAssessment(EvidenceTier.Campaign, null, campaignReasons);
        }

        // 8. Else → Contested, reasons = dispute reasons + ClaimContent.
        var contestedReasons = new List<TierReason>(disputeReasons)
        {
            claimReason,
        };

        return new TierAssessment(EvidenceTier.Contested, null, contestedReasons);
    }
}
