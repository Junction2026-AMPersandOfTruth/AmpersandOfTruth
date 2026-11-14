"""P1.S10 (#37): 1:1 Python port of C# `TierRules.cs`, `IncidentEvidence.cs`, and `EvidenceTier.cs`.

Python 3.10+, standard library only. Same check order, thresholds, validation rules, and exact reason strings.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from enum import Enum
import math


class EvidenceTier(str, Enum):
    REVERTED = "reverted"
    CONTESTED = "contested"
    CAMPAIGN = "campaign"


TIER_LEVELS: dict[EvidenceTier, int] = {
    EvidenceTier.REVERTED: 1,
    EvidenceTier.CONTESTED: 2,
    EvidenceTier.CAMPAIGN: 3,
}

TIER_LABELS: dict[EvidenceTier, str] = {
    EvidenceTier.REVERTED: "Removed by Wikipedia editors",
    EvidenceTier.CONTESTED: "Disputed and removed",
    EvidenceTier.CAMPAIGN: "Pattern consistent with a coordinated attempt",
}


class ContentCategory(str, Enum):
    VANDALISM = "vandalism"
    TEST_OR_NONSENSE = "test_or_nonsense"
    GOOD_FAITH_ERROR = "good_faith_error"
    CONTENT_DISPUTE = "content_dispute"
    POV_PUSH = "pov_push"
    FALSE_FACTUAL_CLAIM = "false_factual_claim"
    SOURCE_MANIPULATION = "source_manipulation"
    OTHER = "other"


class RemovalReason(str, Enum):
    NONE = "none"
    ACCURACY = "accuracy"
    NEUTRALITY = "neutrality"
    SOURCING = "sourcing"
    VANDALISM = "vandalism"
    OTHER = "other"


class ExclusionReason(str, Enum):
    NOT_SUBSTANTIVE = "not-substantive"
    SELF_REMOVAL = "self-removal"
    BOT = "bot"
    BLANKING = "blanking"
    KEPT = "kept"
    GRAFFITI = "graffiti"


class TierReasonKind(str, Enum):
    REMOVED_BY_OTHERS = "removed_by_others"
    REMOVAL_REASON = "removal_reason"
    REINSERTED = "reinserted"
    EDIT_WAR = "edit_war"
    DISPUTE_TEMPLATE = "dispute_template"
    CLAIM_CONTENT = "claim_content"
    MULTIPLE_EDITORS = "multiple_editors"
    MULTIPLE_ARTICLES = "multiple_articles"
    PERSISTENCE = "persistence"
    SOURCE_CHANGE = "source_change"


CLAIM_CATEGORIES: frozenset[ContentCategory] = frozenset(
    {
        ContentCategory.CONTENT_DISPUTE,
        ContentCategory.POV_PUSH,
        ContentCategory.FALSE_FACTUAL_CLAIM,
        ContentCategory.SOURCE_MANIPULATION,
    }
)

GRAFFITI_CATEGORIES: frozenset[ContentCategory] = frozenset(
    {
        ContentCategory.VANDALISM,
        ContentCategory.TEST_OR_NONSENSE,
    }
)


@dataclass(frozen=True)
class ContentAssessment:
    """Automated assessment (P1.S9). Confidence 1-5; has_verified_quote = >=1 quoted span verbatim in inserted text."""

    category: ContentCategory
    confidence: int
    has_verified_quote: bool


@dataclass(frozen=True)
class IncidentEvidence:
    is_substantive: bool
    removed_by_other_account: bool
    inserted_by_bot: bool = False
    is_blanking: bool = False
    kept_in_latest_revision: bool = False
    assessment: ContentAssessment | None = None
    removal_reason: RemovalReason = RemovalReason.NONE
    reinsertion_count: int = 0
    in_edit_war: bool = False
    dispute_template_added: bool = False
    distinct_inserting_editors: int = 1
    distinct_articles: int = 1
    insertion_span: timedelta = timedelta(0)
    source_manipulation: bool = False


@dataclass(frozen=True)
class TierThresholds:
    min_model_confidence: int = 3
    min_distinct_editors: int = 2
    min_distinct_articles: int = 2
    min_persistence_reinsertions: int = 2
    min_persistence_span: timedelta = timedelta(days=7)
    min_coordination_signals: int = 2


DEFAULT_THRESHOLDS = TierThresholds()


@dataclass(frozen=True)
class TierReason:
    kind: TierReasonKind
    text: str


@dataclass(frozen=True)
class TierAssessment:
    tier: EvidenceTier | None
    exclusion: ExclusionReason | None
    reasons: tuple[TierReason, ...]

    @property
    def is_incident(self) -> bool:
        return self.tier is not None

    @property
    def because_sentence(self) -> str | None:
        if self.tier is None:
            return None
        joined = ", ".join(r.text for r in self.reasons)
        return f"{TIER_LABELS[self.tier]} \u2014 because: {joined}."


def assess(evidence: IncidentEvidence, thresholds: TierThresholds | None = None) -> TierAssessment:
    """1:1 port of `TierRules.Assess(IncidentEvidence, TierThresholds?)`."""
    if evidence is None:
        raise TypeError("evidence must not be None.")

    if evidence.reinsertion_count < 0:
        raise ValueError(f"ReinsertionCount must be non-negative, got {evidence.reinsertion_count}.")

    if evidence.distinct_inserting_editors < 1:
        raise ValueError(f"DistinctInsertingEditors must be at least 1, got {evidence.distinct_inserting_editors}.")

    if evidence.distinct_articles < 1:
        raise ValueError(f"DistinctArticles must be at least 1, got {evidence.distinct_articles}.")

    if evidence.assessment is not None and (evidence.assessment.confidence < 1 or evidence.assessment.confidence > 5):
        raise ValueError(f"Assessment confidence must be between 1 and 5, got {evidence.assessment.confidence}.")

    t = thresholds if thresholds is not None else DEFAULT_THRESHOLDS

    is_confident = evidence.assessment is not None and evidence.assessment.confidence >= t.min_model_confidence

    # 2. Exclusions, first match wins -> TierAssessment(None, reason, ())
    if not evidence.is_substantive:
        return TierAssessment(None, ExclusionReason.NOT_SUBSTANTIVE, ())

    if not evidence.removed_by_other_account:
        return TierAssessment(None, ExclusionReason.SELF_REMOVAL, ())

    if evidence.inserted_by_bot:
        return TierAssessment(None, ExclusionReason.BOT, ())

    if evidence.is_blanking:
        return TierAssessment(None, ExclusionReason.BLANKING, ())

    if evidence.kept_in_latest_revision:
        return TierAssessment(None, ExclusionReason.KEPT, ())

    if is_confident and evidence.assessment is not None and evidence.assessment.category in GRAFFITI_CATEGORIES:
        return TierAssessment(None, ExclusionReason.GRAFFITI, ())

    # 3. isClaim = confident && HasVerifiedQuote && Category in {ContentDispute, PovPush, FalseFactualClaim, SourceManipulation}
    is_claim = (
        is_confident
        and evidence.assessment is not None
        and evidence.assessment.has_verified_quote
        and evidence.assessment.category in CLAIM_CATEGORIES
    )

    # 4. Dispute reasons, in exact order
    dispute_reasons: list[TierReason] = []

    if evidence.removal_reason == RemovalReason.ACCURACY:
        dispute_reasons.append(TierReason(TierReasonKind.REMOVAL_REASON, "removed as inaccurate"))
    elif evidence.removal_reason == RemovalReason.NEUTRALITY:
        dispute_reasons.append(TierReason(TierReasonKind.REMOVAL_REASON, "removed as not neutral"))
    elif evidence.removal_reason == RemovalReason.SOURCING:
        dispute_reasons.append(TierReason(TierReasonKind.REMOVAL_REASON, "removed for lack of a reliable source"))

    if evidence.reinsertion_count >= 1:
        reinserted_text = (
            "put back once after removal"
            if evidence.reinsertion_count == 1
            else f"put back {evidence.reinsertion_count} times after removal"
        )
        dispute_reasons.append(TierReason(TierReasonKind.REINSERTED, reinserted_text))

    if evidence.in_edit_war:
        dispute_reasons.append(TierReason(TierReasonKind.EDIT_WAR, "part of a back-and-forth over this passage"))

    if evidence.dispute_template_added:
        dispute_reasons.append(TierReason(TierReasonKind.DISPUTE_TEMPLATE, "the article was marked as disputed"))

    # 5. If !isClaim or no dispute reason -> Reverted, reasons = [RemovedByOthers]
    if not is_claim or len(dispute_reasons) == 0:
        return TierAssessment(
            EvidenceTier.REVERTED,
            None,
            (TierReason(TierReasonKind.REMOVED_BY_OTHERS, "taken out by another editor"),),
        )

    # 6. Coordination reasons, in exact order
    coordination_reasons: list[TierReason] = []
    multiple_editors_fired = False
    multiple_articles_fired = False
    persistence_fired = False

    if evidence.distinct_inserting_editors >= t.min_distinct_editors:
        multiple_editors_fired = True
        coordination_reasons.append(
            TierReason(
                TierReasonKind.MULTIPLE_EDITORS,
                f"added by {evidence.distinct_inserting_editors} different editors",
            )
        )

    if evidence.distinct_articles >= t.min_distinct_articles:
        multiple_articles_fired = True
        coordination_reasons.append(
            TierReason(
                TierReasonKind.MULTIPLE_ARTICLES,
                f"added to {evidence.distinct_articles} articles",
            )
        )

    if (
        evidence.reinsertion_count >= t.min_persistence_reinsertions
        and evidence.insertion_span >= t.min_persistence_span
    ):
        persistence_fired = True
        days = int(math.floor(evidence.insertion_span.total_seconds() / 86400.0))
        coordination_reasons.append(
            TierReason(
                TierReasonKind.PERSISTENCE,
                f"put back {evidence.reinsertion_count} times over {days} days",
            )
        )

    if evidence.source_manipulation:
        coordination_reasons.append(
            TierReason(
                TierReasonKind.SOURCE_CHANGE,
                "came with changes to the cited sources",
            )
        )

    claim_reason = TierReason(
        TierReasonKind.CLAIM_CONTENT,
        "automated assessment: a factual or narrative claim",
    )

    # 7. Campaign check
    if len(coordination_reasons) >= t.min_coordination_signals and (multiple_editors_fired or multiple_articles_fired):
        campaign_reasons: list[TierReason] = list(coordination_reasons)
        for reason in dispute_reasons:
            if persistence_fired and reason.kind == TierReasonKind.REINSERTED:
                continue
            campaign_reasons.append(reason)
        campaign_reasons.append(claim_reason)
        return TierAssessment(EvidenceTier.CAMPAIGN, None, tuple(campaign_reasons))

    # 8. Contested
    return TierAssessment(EvidenceTier.CONTESTED, None, tuple([*dispute_reasons, claim_reason]))


class TierRules:
    """Namespace alias mirroring C# `TierRules.Assess`."""

    Assess = staticmethod(assess)
    assess = staticmethod(assess)
