#!/usr/bin/env python3
"""P1.S10 (#37) offline unit tests:
  1. 1:1 parity table for every test and `[InlineData]` row in C# `TierRulesTests.cs`.
  2. Evidence mapping unit tests on synthetic S1/S3/S4/S5/S6/S7/S9 rows (`evidence.py`).
  3. Variant A additive score, Variant B / B+rel ranking, per-article cap, `--disable` ablations,
     and #109 incident schema validation (`variant.py`).

Run:
    python3 -m unittest discover -s research/37-s10-assembly -p 'test_*.py' -v
"""
from __future__ import annotations

from dataclasses import replace
from datetime import timedelta
import json
import pathlib
import sys
import tempfile
import unittest
from unittest import mock

HERE = pathlib.Path(__file__).resolve().parent
RESEARCH_DIR = HERE.parent
sys.path.insert(0, str(RESEARCH_DIR / "lib"))
sys.path.insert(0, str(RESEARCH_DIR / "eval"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
import contract  # noqa: E402
from evidence import (  # noqa: E402
    apply_disables,
    build_cluster_candidate,
    has_dispute_template_added,
    has_ref_manipulation,
    longest_added_sentence,
    orient_cluster_units,
    parse_assessment_row,
    to_removal_reason,
)
from export_classify import score_variant_a  # noqa: E402
from tiers import (  # noqa: E402
    ContentAssessment,
    ContentCategory,
    EvidenceTier,
    ExclusionReason,
    IncidentEvidence,
    RemovalReason,
    TierReasonKind,
    TierRules,
    TierThresholds,
    parse_threshold_overrides,
)
from variant import (  # noqa: E402
    embed_cache_key,
    encode_vector_f16_b64,
    load_embedding_cache,
    rank_and_assemble_incidents,
)


def create_contested_evidence() -> IncidentEvidence:
    """Exact mirror of `TierRulesTests.CreateContestedEvidence()`."""
    return IncidentEvidence(
        is_substantive=True,
        removed_by_other_account=True,
        inserted_by_bot=False,
        is_blanking=False,
        kept_in_latest_revision=False,
        assessment=ContentAssessment(
            category=ContentCategory.CONTENT_DISPUTE,
            confidence=4,
            has_verified_quote=True,
        ),
        removal_reason=RemovalReason.NEUTRALITY,
        reinsertion_count=0,
        in_edit_war=False,
        dispute_template_added=False,
        distinct_inserting_editors=1,
        distinct_articles=1,
        insertion_span=timedelta(0),
        source_manipulation=False,
    )


class TierRulesParityTests(unittest.TestCase):
    """1:1 port of every `[Fact]` and `[Theory]` case in `TierRulesTests.cs`."""

    def test_assess_detects_each_of_the_six_exclusions(self) -> None:
        base = create_contested_evidence()
        parity_table = [
            (ExclusionReason.NOT_SUBSTANTIVE, replace(base, is_substantive=False)),
            (ExclusionReason.SELF_REMOVAL, replace(base, removed_by_other_account=False)),
            (ExclusionReason.BOT, replace(base, inserted_by_bot=True)),
            (ExclusionReason.BLANKING, replace(base, is_blanking=True)),
            (ExclusionReason.KEPT, replace(base, kept_in_latest_revision=True)),
            (
                ExclusionReason.GRAFFITI,
                replace(base, assessment=ContentAssessment(ContentCategory.VANDALISM, 3, False)),
            ),
        ]
        for expected_exclusion, ev in parity_table:
            with self.subTest(expected_exclusion=expected_exclusion):
                result = TierRules.Assess(ev)
                self.assertFalse(result.is_incident)
                self.assertIsNone(result.tier)
                self.assertEqual(expected_exclusion, result.exclusion)
                self.assertEqual((), result.reasons)
                self.assertIsNone(result.because_sentence)

    def test_assess_excludes_graffiti_for_test_or_nonsense_category(self) -> None:
        ev = replace(
            create_contested_evidence(),
            assessment=ContentAssessment(ContentCategory.TEST_OR_NONSENSE, 4, False),
        )
        result = TierRules.Assess(ev)
        self.assertFalse(result.is_incident)
        self.assertIsNone(result.tier)
        self.assertEqual(ExclusionReason.GRAFFITI, result.exclusion)
        self.assertIsNone(result.because_sentence)

    def test_assess_respects_exclusion_order_first_match_wins(self) -> None:
        base = create_contested_evidence()
        cases = [
            (
                "NotSubstantive beats Bot",
                ExclusionReason.NOT_SUBSTANTIVE,
                replace(base, is_substantive=False, inserted_by_bot=True),
            ),
            (
                "SelfRemoval beats Blanking",
                ExclusionReason.SELF_REMOVAL,
                replace(base, removed_by_other_account=False, is_blanking=True),
            ),
            (
                "Bot beats Blanking",
                ExclusionReason.BOT,
                replace(base, inserted_by_bot=True, is_blanking=True),
            ),
            (
                "Blanking beats Kept",
                ExclusionReason.BLANKING,
                replace(base, is_blanking=True, kept_in_latest_revision=True),
            ),
            (
                "Kept beats Graffiti",
                ExclusionReason.KEPT,
                replace(
                    base,
                    kept_in_latest_revision=True,
                    assessment=ContentAssessment(ContentCategory.VANDALISM, 5, True),
                ),
            ),
        ]
        for label, expected, ev in cases:
            with self.subTest(case=label):
                self.assertEqual(expected, TierRules.Assess(ev).exclusion)

    def test_assess_low_confidence_vandalism_is_not_excluded_and_stays_reverted(self) -> None:
        base = create_contested_evidence()
        for cat in (ContentCategory.VANDALISM, ContentCategory.TEST_OR_NONSENSE):
            with self.subTest(category=cat):
                ev = replace(base, assessment=ContentAssessment(cat, confidence=2, has_verified_quote=True))
                result = TierRules.Assess(ev)
                self.assertTrue(result.is_incident)
                self.assertEqual(EvidenceTier.REVERTED, result.tier)
                self.assertIsNone(result.exclusion)
                self.assertEqual(1, len(result.reasons))
                self.assertEqual(TierReasonKind.REMOVED_BY_OTHERS, result.reasons[0].kind)
                self.assertEqual("taken out by another editor", result.reasons[0].text)

    def test_assess_missing_assessment_never_promotes_reverts_even_with_all_other_signals(self) -> None:
        ev = replace(
            create_contested_evidence(),
            assessment=None,
            removal_reason=RemovalReason.ACCURACY,
            reinsertion_count=5,
            in_edit_war=True,
            dispute_template_added=True,
            distinct_inserting_editors=4,
            distinct_articles=3,
            insertion_span=timedelta(days=20),
            source_manipulation=True,
        )
        result = TierRules.Assess(ev)
        self.assertTrue(result.is_incident)
        self.assertEqual(EvidenceTier.REVERTED, result.tier)
        self.assertIsNone(result.exclusion)
        self.assertEqual(1, len(result.reasons))
        self.assertEqual(TierReasonKind.REMOVED_BY_OTHERS, result.reasons[0].kind)

    def test_assess_non_claim_categories_or_missing_quote_or_low_confidence_stay_reverted(self) -> None:
        base = create_contested_evidence()
        cases = [
            (ContentCategory.GOOD_FAITH_ERROR, 4, True),
            (ContentCategory.OTHER, 4, True),
            (ContentCategory.CONTENT_DISPUTE, 4, False),
            (ContentCategory.CONTENT_DISPUTE, 2, True),
        ]
        for cat, conf, has_quote in cases:
            with self.subTest(category=cat, confidence=conf, has_verified_quote=has_quote):
                ev = replace(
                    base,
                    assessment=ContentAssessment(cat, conf, has_quote),
                    removal_reason=RemovalReason.NEUTRALITY,
                    reinsertion_count=1,
                    in_edit_war=True,
                )
                result = TierRules.Assess(ev)
                self.assertEqual(EvidenceTier.REVERTED, result.tier)
                self.assertEqual(1, len(result.reasons))
                self.assertEqual(TierReasonKind.REMOVED_BY_OTHERS, result.reasons[0].kind)

    def test_assess_claim_without_dispute_signal_is_reverted(self) -> None:
        ev = replace(
            create_contested_evidence(),
            removal_reason=RemovalReason.NONE,
            reinsertion_count=0,
            in_edit_war=False,
            dispute_template_added=False,
        )
        result = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.REVERTED, result.tier)
        self.assertEqual(1, len(result.reasons))
        self.assertEqual(TierReasonKind.REMOVED_BY_OTHERS, result.reasons[0].kind)

    def test_assess_non_dispute_removal_reasons_do_not_count_as_dispute_signals(self) -> None:
        base = create_contested_evidence()
        for reason in (RemovalReason.OTHER, RemovalReason.VANDALISM):
            with self.subTest(reason=reason):
                ev = replace(
                    base,
                    removal_reason=reason,
                    reinsertion_count=0,
                    in_edit_war=False,
                    dispute_template_added=False,
                )
                result = TierRules.Assess(ev)
                self.assertEqual(EvidenceTier.REVERTED, result.tier)
                self.assertEqual(1, len(result.reasons))
                self.assertEqual(TierReasonKind.REMOVED_BY_OTHERS, result.reasons[0].kind)

    def test_assess_each_removal_reason_alone_with_claim_is_contested(self) -> None:
        base = create_contested_evidence()
        cases = [
            (RemovalReason.ACCURACY, "removed as inaccurate"),
            (RemovalReason.NEUTRALITY, "removed as not neutral"),
            (RemovalReason.SOURCING, "removed for lack of a reliable source"),
        ]
        for rr, expected_text in cases:
            with self.subTest(removal_reason=rr):
                ev = replace(
                    base,
                    removal_reason=rr,
                    reinsertion_count=0,
                    in_edit_war=False,
                    dispute_template_added=False,
                )
                result = TierRules.Assess(ev)
                self.assertEqual(EvidenceTier.CONTESTED, result.tier)
                self.assertEqual(2, len(result.reasons))
                self.assertEqual(TierReasonKind.REMOVAL_REASON, result.reasons[0].kind)
                self.assertEqual(expected_text, result.reasons[0].text)
                self.assertEqual(TierReasonKind.CLAIM_CONTENT, result.reasons[1].kind)

    def test_assess_reinsertion_alone_with_claim_is_contested(self) -> None:
        ev = replace(
            create_contested_evidence(),
            removal_reason=RemovalReason.NONE,
            reinsertion_count=1,
            in_edit_war=False,
            dispute_template_added=False,
        )
        result = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.CONTESTED, result.tier)
        self.assertEqual(2, len(result.reasons))
        self.assertEqual(TierReasonKind.REINSERTED, result.reasons[0].kind)
        self.assertEqual("put back once after removal", result.reasons[0].text)
        self.assertEqual(TierReasonKind.CLAIM_CONTENT, result.reasons[1].kind)

    def test_assess_edit_war_alone_with_claim_is_contested(self) -> None:
        ev = replace(
            create_contested_evidence(),
            removal_reason=RemovalReason.NONE,
            reinsertion_count=0,
            in_edit_war=True,
            dispute_template_added=False,
        )
        result = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.CONTESTED, result.tier)
        self.assertEqual(2, len(result.reasons))
        self.assertEqual(TierReasonKind.EDIT_WAR, result.reasons[0].kind)
        self.assertEqual("part of a back-and-forth over this passage", result.reasons[0].text)
        self.assertEqual(TierReasonKind.CLAIM_CONTENT, result.reasons[1].kind)

    def test_assess_dispute_template_alone_with_claim_is_contested(self) -> None:
        ev = replace(
            create_contested_evidence(),
            removal_reason=RemovalReason.NONE,
            reinsertion_count=0,
            in_edit_war=False,
            dispute_template_added=True,
        )
        result = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.CONTESTED, result.tier)
        self.assertEqual(2, len(result.reasons))
        self.assertEqual(TierReasonKind.DISPUTE_TEMPLATE, result.reasons[0].kind)
        self.assertEqual("the article was marked as disputed", result.reasons[0].text)
        self.assertEqual(TierReasonKind.CLAIM_CONTENT, result.reasons[1].kind)

    def test_assess_single_editor_and_single_article_persistent_stays_contested(self) -> None:
        ev = replace(
            create_contested_evidence(),
            distinct_inserting_editors=1,
            distinct_articles=1,
            reinsertion_count=5,
            insertion_span=timedelta(days=30),
            source_manipulation=True,
        )
        self.assertEqual(EvidenceTier.CONTESTED, TierRules.Assess(ev).tier)

    def test_assess_two_editors_plus_two_articles_is_campaign(self) -> None:
        ev = replace(create_contested_evidence(), distinct_inserting_editors=2, distinct_articles=2)
        self.assertEqual(EvidenceTier.CAMPAIGN, TierRules.Assess(ev).tier)

    def test_assess_two_editors_plus_persistence_is_campaign(self) -> None:
        ev = replace(
            create_contested_evidence(),
            distinct_inserting_editors=2,
            distinct_articles=1,
            reinsertion_count=2,
            insertion_span=timedelta(days=7),
        )
        self.assertEqual(EvidenceTier.CAMPAIGN, TierRules.Assess(ev).tier)

    def test_assess_one_editor_plus_three_articles_plus_source_manipulation_is_campaign(self) -> None:
        ev = replace(
            create_contested_evidence(),
            distinct_inserting_editors=1,
            distinct_articles=3,
            source_manipulation=True,
        )
        self.assertEqual(EvidenceTier.CAMPAIGN, TierRules.Assess(ev).tier)

    def test_assess_two_editors_only_is_contested_under_defaults(self) -> None:
        ev = replace(
            create_contested_evidence(),
            distinct_inserting_editors=2,
            distinct_articles=1,
            source_manipulation=False,
            reinsertion_count=0,
        )
        self.assertEqual(EvidenceTier.CONTESTED, TierRules.Assess(ev).tier)

    def test_assess_persistence_requires_both_reinsertion_count_and_span(self) -> None:
        base = create_contested_evidence()
        short_span = replace(
            base,
            distinct_inserting_editors=2,
            distinct_articles=1,
            reinsertion_count=2,
            insertion_span=timedelta(days=6),
        )
        short_res = TierRules.Assess(short_span)
        self.assertEqual(EvidenceTier.CONTESTED, short_res.tier)
        self.assertFalse(any(r.kind == TierReasonKind.PERSISTENCE for r in short_res.reasons))

        low_count = replace(
            base,
            distinct_inserting_editors=2,
            distinct_articles=1,
            reinsertion_count=1,
            insertion_span=timedelta(days=10),
        )
        low_res = TierRules.Assess(low_count)
        self.assertEqual(EvidenceTier.CONTESTED, low_res.tier)
        self.assertFalse(any(r.kind == TierReasonKind.PERSISTENCE for r in low_res.reasons))

    def test_assess_respects_custom_thresholds(self) -> None:
        custom = TierThresholds(min_coordination_signals=1)
        ev = replace(create_contested_evidence(), distinct_inserting_editors=2, distinct_articles=1)
        self.assertEqual(EvidenceTier.CAMPAIGN, TierRules.Assess(ev, custom).tier)

    def test_assess_because_sentence_is_null_for_excluded(self) -> None:
        ev = replace(create_contested_evidence(), is_substantive=False)
        res = TierRules.Assess(ev)
        self.assertIsNone(res.tier)
        self.assertIsNone(res.because_sentence)

    def test_assess_exact_because_sentence_for_reverted(self) -> None:
        ev = replace(create_contested_evidence(), assessment=None)
        res = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.REVERTED, res.tier)
        self.assertEqual(
            "Removed by Wikipedia editors \u2014 because: taken out by another editor.",
            res.because_sentence,
        )

    def test_assess_exact_because_sentence_for_contested(self) -> None:
        ev = replace(
            create_contested_evidence(),
            removal_reason=RemovalReason.NEUTRALITY,
            reinsertion_count=2,
            in_edit_war=False,
            dispute_template_added=False,
        )
        res = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.CONTESTED, res.tier)
        expected = (
            "Disputed and removed \u2014 because: removed as not neutral, "
            "put back 2 times after removal, automated assessment: a factual or narrative claim."
        )
        self.assertEqual(expected, res.because_sentence)

    def test_assess_exact_because_sentence_for_campaign_and_persistence_drops_reinserted(self) -> None:
        ev = replace(
            create_contested_evidence(),
            distinct_inserting_editors=2,
            distinct_articles=2,
            reinsertion_count=3,
            insertion_span=timedelta(days=10.5),
            removal_reason=RemovalReason.ACCURACY,
            in_edit_war=False,
            dispute_template_added=False,
            source_manipulation=False,
        )
        res = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.CAMPAIGN, res.tier)
        assert res.because_sentence is not None
        self.assertIn("put back 3 times over 10 days", res.because_sentence)
        self.assertNotIn("put back 3 times after removal", res.because_sentence)
        expected = (
            "Pattern consistent with a coordinated attempt \u2014 because: "
            "added by 2 different editors, added to 2 articles, put back 3 times over 10 days, "
            "removed as inaccurate, automated assessment: a factual or narrative claim."
        )
        self.assertEqual(expected, res.because_sentence)

    def test_assess_campaign_retains_reinserted_when_persistence_did_not_fire(self) -> None:
        ev = replace(
            create_contested_evidence(),
            distinct_inserting_editors=2,
            distinct_articles=2,
            reinsertion_count=1,
            insertion_span=timedelta(days=2),
            removal_reason=RemovalReason.NONE,
        )
        res = TierRules.Assess(ev)
        self.assertEqual(EvidenceTier.CAMPAIGN, res.tier)
        assert res.because_sentence is not None
        self.assertIn("put back once after removal", res.because_sentence)

    def test_assess_reinserted_reason_text_formats_singular_and_plural(self) -> None:
        base = create_contested_evidence()
        cases = [
            (1, "put back once after removal"),
            (2, "put back 2 times after removal"),
            (5, "put back 5 times after removal"),
        ]
        for count, expected_text in cases:
            with self.subTest(count=count):
                ev = replace(base, removal_reason=RemovalReason.NONE, reinsertion_count=count)
                res = TierRules.Assess(ev)
                self.assertEqual(EvidenceTier.CONTESTED, res.tier)
                reinserted = next(r for r in res.reasons if r.kind == TierReasonKind.REINSERTED)
                self.assertEqual(expected_text, reinserted.text)

    def test_assess_throws_argument_out_of_range_on_invalid_inputs(self) -> None:
        base = create_contested_evidence()
        invalid_cases = [
            (-1, 1, 1, 3),
            (0, 0, 1, 3),
            (0, 1, 0, 3),
            (0, 1, 1, 0),
            (0, 1, 1, 6),
        ]
        for reinsertions, editors, articles, confidence in invalid_cases:
            with self.subTest(reinsertions=reinsertions, editors=editors, articles=articles, confidence=confidence):
                ev = replace(
                    base,
                    reinsertion_count=reinsertions,
                    distinct_inserting_editors=editors,
                    distinct_articles=articles,
                    assessment=ContentAssessment(ContentCategory.CONTENT_DISPUTE, confidence, True),
                )
                with self.assertRaises(ValueError):
                    TierRules.Assess(ev)

    def test_assess_throws_argument_null_when_evidence_is_null(self) -> None:
        with self.assertRaises(TypeError):
            TierRules.Assess(None)  # type: ignore[arg-type]

    def test_assess_each_claim_category_qualifies_as_claim(self) -> None:
        base = create_contested_evidence()
        for cat in (
            ContentCategory.CONTENT_DISPUTE,
            ContentCategory.POV_PUSH,
            ContentCategory.FALSE_FACTUAL_CLAIM,
            ContentCategory.SOURCE_MANIPULATION,
        ):
            with self.subTest(category=cat):
                ev = replace(
                    base,
                    assessment=ContentAssessment(cat, 3, True),
                    removal_reason=RemovalReason.ACCURACY,
                )
                self.assertEqual(EvidenceTier.CONTESTED, TierRules.Assess(ev).tier)


class EvidenceMappingTests(unittest.TestCase):
    """Offline synthetic tests for `evidence.py` signal mappings."""

    def test_removal_reason_priority_matches_revert_reasons_cs(self) -> None:
        self.assertEqual(RemovalReason.ACCURACY, to_removal_reason(["vandalism_test", "neutrality_pov", "factual_error"]))
        self.assertEqual(RemovalReason.NEUTRALITY, to_removal_reason(["sourcing", "neutrality_pov"]))
        self.assertEqual(RemovalReason.SOURCING, to_removal_reason(["original_research", "vandalism_test"]))
        self.assertEqual(RemovalReason.SOURCING, to_removal_reason(["sourcing"]))
        self.assertEqual(RemovalReason.VANDALISM, to_removal_reason(["vandalism_test", "other"]))
        self.assertEqual(RemovalReason.OTHER, to_removal_reason(["style_formatting"]))
        self.assertEqual(RemovalReason.NONE, to_removal_reason([]))

    def test_dispute_template_and_ref_manipulation_and_longest_sentence(self) -> None:
        diff_rows = [
            {
                "is_substantive": "1",
                "is_blanking": "0",
                "added_prose": "Lyhyt. Tämä on selvästi yli kaksikymmentäviisi merkkiä pitkä lisätty virke.",
                "added_prose_chars": "75",
                "segments": json.dumps(
                    [
                        {
                            "op": "added",
                            "kind": "template",
                            "text": "{{Neutraalius}}",
                            "prose": "",
                            "old_paragraph": 1,
                            "new_paragraph": 1,
                            "moved": False,
                        },
                        {
                            "op": "removed",
                            "kind": "reference",
                            "text": "<ref>https://yle.fi/a/123456 Luotettava lähde</ref>",
                            "prose": "yle.fi",
                            "old_paragraph": 1,
                            "new_paragraph": 1,
                            "moved": False,
                        },
                        {
                            "op": "added",
                            "kind": "prose",
                            "text": "Tämä on selvästi yli kaksikymmentäviisi merkkiä pitkä lisätty virke.",
                            "prose": "Tämä on selvästi yli kaksikymmentäviisi merkkiä pitkä lisätty virke.",
                            "old_paragraph": 1,
                            "new_paragraph": 1,
                            "moved": False,
                        },
                    ]
                ),
            }
        ]
        self.assertTrue(has_dispute_template_added(diff_rows))
        self.assertTrue(has_ref_manipulation(diff_rows))
        self.assertEqual(
            "Tämä on selvästi yli kaksikymmentäviisi merkkiä pitkä lisätty virke.",
            longest_added_sentence(diff_rows, min_chars=25),
        )

    def test_build_cluster_candidate_and_ip_prefix_dedup_and_ablations(self) -> None:
        cluster_row = {
            "cluster_id": "42",
            "n_units": "2",
            "n_pages": "2",
            "n_accounts": "2",
            "n_reinsertions": "1",
            "prose_chars": "120",
            "sample_text": "Väite sodan syttymisestä.",
        }
        member_rows = [
            {
                "cluster_id": "42",
                "page_id": "100",
                "reverting_rev_id": "1002",
                "inserting_rev_ids": "1001",
                "inserted_at": "2024-01-01 10:00:00",
                "removed_at": "2024-01-01 11:00:00",
                "account_key": "a1",
            },
            {
                "cluster_id": "42",
                "page_id": "200",
                "reverting_rev_id": "2002",
                "inserting_rev_ids": "2001",
                "inserted_at": "2024-01-12 10:00:00",
                "removed_at": "2024-01-12 12:00:00",
                "account_key": "a1",
            },
        ]
        # Two IPs in the same /16 (85.76.x.x) must count as ONE editor (`DistinctInsertingEditors == 1`)!
        rev_by_id = {
            1001: {"page_id": "100", "rev_id": "1001", "ts": "2024-01-01 10:00:00", "user": "85.76.10.1", "anonymous": "1"},
            1002: {"page_id": "100", "rev_id": "1002", "ts": "2024-01-01 11:00:00", "user": "ReviewerA", "anonymous": "0"},
            2001: {"page_id": "200", "rev_id": "2001", "ts": "2024-01-12 10:00:00", "user": "85.76.99.2", "anonymous": "1"},
            2002: {"page_id": "200", "rev_id": "2002", "ts": "2024-01-12 12:00:00", "user": "ReviewerB", "anonymous": "0"},
        }
        seg_json = json.dumps(
            [
                {
                    "op": "added",
                    "kind": "prose",
                    "text": "Neuvostoliitto ei aloittanut sotaa vaan vastasi hyökkäykseen.",
                    "prose": "Neuvostoliitto ei aloittanut sotaa vaan vastasi hyökkäykseen.",
                    "old_paragraph": 0,
                    "new_paragraph": 0,
                    "moved": False,
                }
            ]
        )
        span_diffs = {
            1002: {"is_substantive": "1", "is_blanking": "0", "added_prose": "Neuvostoliitto ei aloittanut sotaa vaan vastasi hyökkäykseen.", "added_prose_chars": "61", "segments": seg_json},
            2002: {"is_substantive": "1", "is_blanking": "0", "added_prose": "Neuvostoliitto ei aloittanut sotaa vaan vastasi hyökkäykseen.", "added_prose_chars": "61", "segments": seg_json},
        }
        assessment_row = {
            "id": "1001+2001",
            "model": "gemini-4-carbon",
            "outcome": "ok",
            "value": {
                "category": "false_factual_claim",
                "confidence": 5,
                "evidence": ["ei aloittanut sotaa"],
                "claim_en": "The Soviet Union did not start the war.",
                "claim_fi": "Neuvostoliitto ei aloittanut sotaa.",
                "what_changed_en": "Asserted that the Soviet Union only responded to an attack.",
                "narrative_en": "Mainila narrative",
                "uncertainty_note": None,
                "historical_note_en": "Consensus history establishes the Red Army attacked on 30 November 1939.",
            },
        }

        cand = build_cluster_candidate(
            cluster_row=cluster_row,
            member_rows=member_rows,
            rev_by_id=rev_by_id,
            span_diffs_by_rev=span_diffs,
            s1_class_by_rev={1002: "dispute", 2002: "edit_war"},
            s4_war_revs={2001, 2002},
            s5_reasons_by_rev={1002: "factual_error,sourcing", 2002: ""},
            s6_bot_by_rev={},
            kept_in_latest=False,
            assessment_row=assessment_row,
            titles_by_page={100: "Talvisota", 200: "Mainilan laukaukset"},
        )

        self.assertEqual("1001+2001", cand.incident_key)
        self.assertEqual(1, cand.evidence.distinct_inserting_editors)  # same /16 IPv4 prefix!
        self.assertEqual(2, cand.evidence.distinct_articles)
        self.assertEqual(1, cand.evidence.reinsertion_count)
        self.assertEqual(timedelta(days=11), cand.evidence.insertion_span)
        self.assertTrue(cand.evidence.in_edit_war)
        self.assertEqual(RemovalReason.ACCURACY, cand.evidence.removal_reason)
        self.assertEqual(3600 + 7200, cand.total_exposure_seconds)
        self.assertIsNotNone(cand.evidence.assessment)
        assert cand.evidence.assessment is not None
        self.assertTrue(cand.evidence.assessment.has_verified_quote)

        # Verify parse_assessment_row rejects non-verbatim quotes
        ca_unverified, _ = parse_assessment_row(
            {
                "outcome": "ok",
                "value": {"category": "pov_push", "confidence": 4, "evidence": ["hallucinated span not in text"]},
            },
            inserted_raw_text=cand.inserted_raw_text,
        )
        assert ca_unverified is not None
        self.assertFalse(ca_unverified.has_verified_quote)

        # Ablation check
        ev_no_assess, meta_no_assess = apply_disables(cand, frozenset({"assessment", "edit_war"}))
        self.assertIsNone(ev_no_assess.assessment)
        self.assertIsNone(meta_no_assess)
        self.assertFalse(ev_no_assess.in_edit_war)


class VariantScoringAndAssemblyTests(unittest.TestCase):
    """Offline tests for `score_variant_a` and `rank_and_assemble_incidents` (`A`, `B`, `B+rel`)."""

    def _make_synthetic_cand(
        self,
        cid: int,
        page_id: int,
        ins_rev: int,
        rem_rev: int,
        ev: IncidentEvidence,
        claim_en: str | None = None,
        prose_chars: int = 150,
    ):
        crow = {"cluster_id": str(cid), "n_pages": str(ev.distinct_articles), "prose_chars": str(prose_chars)}
        mrows = [
            {
                "cluster_id": str(cid),
                "page_id": str(page_id),
                "reverting_rev_id": str(rem_rev),
                "inserting_rev_ids": str(ins_rev),
                "inserted_at": "2024-05-01 10:00:00",
                "removed_at": "2024-05-01 10:30:00",
                "account_key": "k1",
            }
        ]
        revs = {
            ins_rev: {"page_id": str(page_id), "rev_id": str(ins_rev), "ts": "2024-05-01 10:00:00", "user": "EditorA"},
            rem_rev: {"page_id": str(page_id), "rev_id": str(rem_rev), "ts": "2024-05-01 10:30:00", "user": "EditorB"},
        }
        sdiff = {
            rem_rev: {
                "is_substantive": "1" if ev.is_substantive else "0",
                "is_blanking": "1" if ev.is_blanking else "0",
                "added_prose": "Esimerkkiteksti luotettavuuden testaamiseen.",
                "added_prose_chars": str(prose_chars),
                "segments": "[]",
            }
        }
        arow = None
        if ev.assessment is not None:
            arow = {
                "id": str(ins_rev),
                "model": "gemini-4-carbon",
                "outcome": "ok",
                "value": {
                    "category": ev.assessment.category.value,
                    "confidence": ev.assessment.confidence,
                    "evidence": ["Esimerkkiteksti"] if ev.assessment.has_verified_quote else ["not_present"],
                    "claim_en": claim_en,
                    "claim_fi": "Väite suomeksi." if claim_en else None,
                    "what_changed_en": "Added a test claim.",
                    "narrative_en": None,
                    "uncertainty_note": None,
                    "historical_note_en": None,
                },
            }
        base = build_cluster_candidate(
            cluster_row=crow,
            member_rows=mrows,
            rev_by_id=revs,
            span_diffs_by_rev=sdiff,
            s1_class_by_rev={},
            s4_war_revs=set(),
            s5_reasons_by_rev={},
            s6_bot_by_rev={},
            kept_in_latest=ev.kept_in_latest_revision,
            assessment_row=arow,
            titles_by_page={page_id: f"Article {page_id}"},
        )
        return replace(base, evidence=ev)

    def test_variant_a_score_rewards_dispute_signals_and_demotes_vandalism(self) -> None:
        plain = IncidentEvidence(is_substantive=True, removed_by_other_account=True)
        vandal = replace(plain, removal_reason=RemovalReason.VANDALISM)
        disputed = replace(
            plain,
            removal_reason=RemovalReason.NEUTRALITY,
            reinsertion_count=2,
            in_edit_war=True,
            distinct_inserting_editors=2,
        )
        s_vandal = score_variant_a(vandal, prose_chars=120, total_exposure_seconds=600)
        s_plain = score_variant_a(plain, prose_chars=120, total_exposure_seconds=600)
        s_disputed = score_variant_a(disputed, prose_chars=120, total_exposure_seconds=600)

        self.assertGreater(s_vandal, 0.0)
        self.assertLess(s_vandal, s_plain)
        self.assertGreater(s_disputed, s_plain)

    def test_modes_a_b_brel_cap_and_schema_fields(self) -> None:
        # Candidate 1: high additive score (lots of reinsertions/prose), but NO verified quote -> tier = reverted!
        ev_reverted_high_a = IncidentEvidence(
            is_substantive=True,
            removed_by_other_account=True,
            assessment=ContentAssessment(ContentCategory.GOOD_FAITH_ERROR, 5, True),
            reinsertion_count=4,
            in_edit_war=True,
            distinct_inserting_editors=3,
        )
        # Candidate 2: contested claim (relevant to NATO)
        ev_contested_nato = create_contested_evidence()
        # Candidate 3: contested claim (off-topic football)
        ev_contested_other = create_contested_evidence()

        c1 = self._make_synthetic_cand(101, 500, 11, 12, ev_reverted_high_a, claim_en="Typo fix war", prose_chars=900)
        c2 = self._make_synthetic_cand(102, 500, 21, 22, ev_contested_nato, claim_en="NATO membership debate", prose_chars=120)
        c3 = self._make_synthetic_cand(103, 500, 31, 32, ev_contested_other, claim_en="Football league score", prose_chars=200)

        # Mode A ranks c1 first by raw additive score even though c1 is `reverted`
        inc_a, _ = rank_and_assemble_incidents([c1, c2, c3], "NATO membership", [500], limit=10, mode="A")
        self.assertEqual(["11", "31", "21"], [x["id"] for x in inc_a])

        # Mode B orders tiers first (`contested > reverted`), putting c3 and c2 ahead of c1
        inc_b, _ = rank_and_assemble_incidents([c1, c2, c3], "NATO membership", [500], limit=10, mode="B")
        self.assertEqual(["31", "21", "11"], [x["id"] for x in inc_b])

        # Mode B+rel multiplies within-tier score by cosine relevance -> c2 ("NATO membership debate") beats c3!
        vecs = {
            "NATO membership": [1.0, 0.0],
            "NATO membership debate": [0.98, 0.2],
            "Football league score": [0.05, 0.99],
            "Typo fix war": [0.5, 0.5],
        }
        inc_brel, _ = rank_and_assemble_incidents(
            [c1, c2, c3],
            "NATO membership",
            [500],
            limit=10,
            mode="B+rel",
            embed_fn=lambda t: vecs[t],
        )
        self.assertEqual(["21", "31", "11"], [x["id"] for x in inc_brel])

        # Cap per article = 2 drops the 3rd candidate on page 500
        inc_capped, diag_capped = rank_and_assemble_incidents(
            [c1, c2, c3], "NATO membership", [500], limit=10, mode="B", cap_per_article=2
        )
        self.assertEqual(2, len(inc_capped))
        self.assertEqual(1, diag_capped["capped"])

        # Validate #109 fields and schema compliance on every emitted incident
        for inc in inc_brel:
            rep = contract.validate("incident", inc)
            self.assertTrue(rep.ok, rep.errors)
            self.assertIsInstance(inc["cluster_id"], str)
            self.assertTrue(inc["tier_reason"].endswith("."))
            self.assertIsNotNone(inc["assessment"])
            self.assertIn("has_verified_quote", inc["assessment"])


class S10TuningTests(unittest.TestCase):
    """P1.S10 tuning tests: two-sided cluster orientation, passage edit war, thresholds, and B+rel cache."""

    X_TEXT = "Suomen hallitus sopi salaisesta hyökkäyssopimuksesta jo syksyllä 1940 ennen sotaa."
    Y_TEXT = "Suomen hallitus noudatti puolueettomuuspolitiikkaa välirauhan aikana vuosina 1940–1941."

    def test_synthetic_xy_edit_war_orients_sides_to_single_inserting_editor(self) -> None:
        # Timeline on page 300:
        #   r1 (EditorClaimant) : replaces Y with X
        #   r2 (ReviewerOne)    : reverts r1 back to Y -> U1 (side 1: ins=[1], rev=2, added=X, removed=Y)
        #   r3 (EditorClaimant) : reverts r2 back to X -> U2 (side 2: ins=[2], rev=3, added=Y, removed=X)
        #   r4 (ReviewerTwo)    : reverts r3 back to Y -> U3 (side 1: ins=[3], rev=4, added=X, removed=Y)
        #   r5 (EditorClaimant) : reverts r4 back to X -> U4 (side 2: ins=[4], rev=5, added=Y, removed=X)
        crow = {"cluster_id": "900", "n_units": "4", "n_pages": "1", "prose_chars": "82"}
        members = [
            {"cluster_id": "900", "page_id": "300", "reverting_rev_id": "2", "inserting_rev_ids": "1", "inserted_at": "2024-03-01 10:00:00", "removed_at": "2024-03-01 10:05:00", "account_key": "a"},
            {"cluster_id": "900", "page_id": "300", "reverting_rev_id": "3", "inserting_rev_ids": "2", "inserted_at": "2024-03-01 10:05:00", "removed_at": "2024-03-01 10:10:00", "account_key": "b"},
            {"cluster_id": "900", "page_id": "300", "reverting_rev_id": "4", "inserting_rev_ids": "3", "inserted_at": "2024-03-01 10:10:00", "removed_at": "2024-03-09 10:15:00", "account_key": "a"},
            {"cluster_id": "900", "page_id": "300", "reverting_rev_id": "5", "inserting_rev_ids": "4", "inserted_at": "2024-03-09 10:15:00", "removed_at": "2024-03-09 10:20:00", "account_key": "c"},
        ]
        revs = {
            1: {"page_id": "300", "rev_id": "1", "ts": "2024-03-01 10:00:00", "user": "EditorClaimant", "anonymous": "0"},
            2: {"page_id": "300", "rev_id": "2", "ts": "2024-03-01 10:05:00", "user": "ReviewerOne", "anonymous": "0"},
            3: {"page_id": "300", "rev_id": "3", "ts": "2024-03-01 10:10:00", "user": "EditorClaimant", "anonymous": "0"},
            4: {"page_id": "300", "rev_id": "4", "ts": "2024-03-09 10:15:00", "user": "ReviewerTwo", "anonymous": "0"},
            5: {"page_id": "300", "rev_id": "5", "ts": "2024-03-09 10:20:00", "user": "EditorClaimant", "anonymous": "0"},
        }
        sdiffs = {
            2: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.X_TEXT, "removed_prose": self.Y_TEXT, "added_prose_chars": "82", "segments": "[]"},
            3: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.Y_TEXT, "removed_prose": self.X_TEXT, "added_prose_chars": "87", "segments": "[]"},
            4: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.X_TEXT, "removed_prose": self.Y_TEXT, "added_prose_chars": "82", "segments": "[]"},
            5: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.Y_TEXT, "removed_prose": self.X_TEXT, "added_prose_chars": "87", "segments": "[]"},
        }
        arow = {
            "id": "1+2+3+4",
            "model": "gemini-4-carbon",
            "outcome": "ok",
            "value": {
                "category": "content_dispute",
                "confidence": 4,
                "evidence": ["salaisesta hyökkäyssopimuksesta"],
                "claim_en": "Secret offensive pact in autumn 1940.",
            },
        }

        self.assertEqual((1, 2, 1, 2), orient_cluster_units(members, sdiffs))

        cand_on = build_cluster_candidate(
            cluster_row=crow,
            member_rows=members,
            rev_by_id=revs,
            span_diffs_by_rev=sdiffs,
            s1_class_by_rev={},
            s4_war_revs=set(),
            s5_reasons_by_rev={},
            s6_bot_by_rev={},
            assessment_row=arow,
            sides=True,
            edit_war_source="passage",
        )
        # Side-1 units are U1 (ins=1) and U3 (ins=3). U4's reverting rev 5 is NOT a removal of X!
        self.assertEqual((1, 3), cand_on.inserting_rev_ids)
        self.assertEqual((2, 4), cand_on.removing_rev_ids)
        self.assertEqual(1, cand_on.evidence.distinct_inserting_editors)
        self.assertEqual(1, cand_on.evidence.reinsertion_count)
        self.assertTrue(cand_on.evidence.in_edit_war)
        ta_on = TierRules.Assess(cand_on.evidence)
        self.assertEqual(EvidenceTier.CONTESTED, ta_on.tier)

        # With --sides off, Reviewers 2 and 4 pollute inserting_rev_ids -> multiple_editors!
        cand_off = build_cluster_candidate(
            cluster_row=crow,
            member_rows=members,
            rev_by_id=revs,
            span_diffs_by_rev=sdiffs,
            s1_class_by_rev={},
            s4_war_revs=set(),
            s5_reasons_by_rev={},
            s6_bot_by_rev={},
            assessment_row=arow,
            sides=False,
            edit_war_source="passage",
        )
        self.assertEqual((1, 2, 3, 4), cand_off.inserting_rev_ids)
        self.assertGreaterEqual(cand_off.evidence.distinct_inserting_editors, 2)

    def test_single_revert_plus_counter_revert_is_one_removal_not_edit_war(self) -> None:
        # r1 (A adds X) -> r2 (B reverts to Y, U1 side 1) -> r3 (A reverts r2 back to X, U2 side 2, never reverted again)
        crow = {"cluster_id": "901", "n_units": "2", "n_pages": "1", "prose_chars": "82"}
        members = [
            {"cluster_id": "901", "page_id": "300", "reverting_rev_id": "2", "inserting_rev_ids": "1", "inserted_at": "2024-03-01 10:00:00", "removed_at": "2024-03-01 10:05:00", "account_key": "a"},
            {"cluster_id": "901", "page_id": "300", "reverting_rev_id": "3", "inserting_rev_ids": "2", "inserted_at": "2024-03-01 10:05:00", "removed_at": "2024-03-01 10:10:00", "account_key": "b"},
        ]
        revs = {
            1: {"page_id": "300", "rev_id": "1", "ts": "2024-03-01 10:00:00", "user": "EditorA", "anonymous": "0"},
            2: {"page_id": "300", "rev_id": "2", "ts": "2024-03-01 10:05:00", "user": "EditorB", "anonymous": "0"},
            3: {"page_id": "300", "rev_id": "3", "ts": "2024-03-01 10:10:00", "user": "EditorA", "anonymous": "0"},
        }
        sdiffs = {
            2: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.X_TEXT, "removed_prose": self.Y_TEXT, "added_prose_chars": "82", "segments": "[]"},
            3: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.Y_TEXT, "removed_prose": self.X_TEXT, "added_prose_chars": "87", "segments": "[]"},
        }
        cand = build_cluster_candidate(
            cluster_row=crow,
            member_rows=members,
            rev_by_id=revs,
            span_diffs_by_rev=sdiffs,
            s1_class_by_rev={},
            s4_war_revs=set(),
            s5_reasons_by_rev={},
            s6_bot_by_rev={},
            sides=True,
            edit_war_source="passage",
        )
        self.assertEqual((1,), cand.inserting_rev_ids)
        self.assertEqual((2,), cand.removing_rev_ids)
        self.assertEqual(0, cand.evidence.reinsertion_count)
        self.assertFalse(cand.evidence.in_edit_war)

    def test_passage_edit_war_vs_s4_and_same_editor_guard(self) -> None:
        crow = {"cluster_id": "902", "n_units": "1", "n_pages": "1", "prose_chars": "82"}
        singleton_members = [
            {"cluster_id": "902", "page_id": "300", "reverting_rev_id": "12", "inserting_rev_ids": "11", "inserted_at": "2024-03-01 10:00:00", "removed_at": "2024-03-01 10:05:00", "account_key": "a"},
        ]
        revs = {
            11: {"page_id": "300", "rev_id": "11", "ts": "2024-03-01 10:00:00", "user": "EditorA", "anonymous": "0"},
            12: {"page_id": "300", "rev_id": "12", "ts": "2024-03-01 10:05:00", "user": "EditorB", "anonymous": "0"},
        }
        sdiffs = {
            12: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.X_TEXT, "removed_prose": "", "added_prose_chars": "82", "segments": "[]"},
        }
        # Singleton in an S4 page-level edit war: False under `passage`, True under `s4`
        c_passage = build_cluster_candidate(
            crow, singleton_members, revs, sdiffs, {}, {11, 12}, {}, {}, edit_war_source="passage"
        )
        c_s4 = build_cluster_candidate(
            crow, singleton_members, revs, sdiffs, {}, {11, 12}, {}, {}, edit_war_source="s4"
        )
        self.assertFalse(c_passage.evidence.in_edit_war)
        self.assertTrue(c_s4.evidence.in_edit_war)

        # Two removals + 1 re-insertion where remover and re-inserter share the SAME editor grouping key -> False
        same_camp_members = [
            {"cluster_id": "903", "page_id": "300", "reverting_rev_id": "22", "inserting_rev_ids": "21", "inserted_at": "2024-03-01 10:00:00", "removed_at": "2024-03-01 10:05:00", "account_key": "a"},
            {"cluster_id": "903", "page_id": "300", "reverting_rev_id": "24", "inserting_rev_ids": "23", "inserted_at": "2024-03-01 10:10:00", "removed_at": "2024-03-01 10:15:00", "account_key": "a"},
        ]
        same_camp_revs = {
            21: {"page_id": "300", "rev_id": "21", "ts": "2024-03-01 10:00:00", "user": "85.76.1.10", "anonymous": "1"},
            22: {"page_id": "300", "rev_id": "22", "ts": "2024-03-01 10:05:00", "user": "85.76.2.20", "anonymous": "1"},
            23: {"page_id": "300", "rev_id": "23", "ts": "2024-03-01 10:10:00", "user": "85.76.3.30", "anonymous": "1"},
            24: {"page_id": "300", "rev_id": "24", "ts": "2024-03-01 10:15:00", "user": "85.76.4.40", "anonymous": "1"},
        }
        same_camp_diffs = {
            22: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.X_TEXT, "removed_prose": "", "added_prose_chars": "82", "segments": "[]"},
            24: {"is_substantive": "1", "is_blanking": "0", "added_prose": self.X_TEXT, "removed_prose": "", "added_prose_chars": "82", "segments": "[]"},
        }
        c_same_camp = build_cluster_candidate(
            {"cluster_id": "903", "n_units": "2", "n_pages": "1", "prose_chars": "82"},
            same_camp_members,
            same_camp_revs,
            same_camp_diffs,
            {},
            set(),
            {},
            {},
            edit_war_source="passage",
        )
        self.assertEqual(1, c_same_camp.evidence.reinsertion_count)
        self.assertFalse(c_same_camp.evidence.in_edit_war)

    def test_parse_threshold_overrides_all_fields(self) -> None:
        t = parse_threshold_overrides(
            [
                "min_model_confidence=4",
                "min_distinct_editors=3",
                "min_distinct_articles=4",
                "min_persistence_reinsertions=5",
                "min_persistence_span=14",
                "min_coordination_signals=3",
            ]
        )
        self.assertEqual(
            TierThresholds(
                min_model_confidence=4,
                min_distinct_editors=3,
                min_distinct_articles=4,
                min_persistence_reinsertions=5,
                min_persistence_span=timedelta(days=14),
                min_coordination_signals=3,
            ),
            t,
        )
        with self.assertRaises(ValueError):
            parse_threshold_overrides(["not_a_threshold=2"])
        with self.assertRaises(ValueError):
            parse_threshold_overrides(["min_model_confidence=9"])

    def test_brel_reads_only_cache_and_defaults_missing_to_unit_relevance(self) -> None:
        ev = create_contested_evidence()
        c_hit_low = VariantScoringAndAssemblyTests()._make_synthetic_cand(
            201, 500, 41, 42, ev, claim_en="Off-topic claim", prose_chars=300
        )
        c_miss = VariantScoringAndAssemblyTests()._make_synthetic_cand(
            202, 500, 51, 52, ev, claim_en="Uncached claim", prose_chars=250
        )
        with tempfile.TemporaryDirectory() as tmp:
            cache_file = pathlib.Path(tmp) / "embeddings.cache.json"
            q_vec = [1.0] + [0.0] * 1023
            low_vec = [0.1, 0.99] + [0.0] * 1022
            cache_doc = {
                "model": "bge-m3",
                "dim": 1024,
                "dtype": "float16_le_b64",
                "vectors": {
                    embed_cache_key("Target query"): encode_vector_f16_b64(q_vec),
                    embed_cache_key("Off-topic claim"): encode_vector_f16_b64(low_vec),
                },
            }
            cache_file.write_text(json.dumps(cache_doc), encoding="utf-8")
            lookup = load_embedding_cache(cache_file)
            self.assertIsNotNone(lookup("Target query"))
            self.assertIsNone(lookup("Uncached claim"))

            with mock.patch.object(aot, "embed", side_effect=AssertionError("aot.embed must not be called in B+rel")):
                incidents, diag = rank_and_assemble_incidents(
                    [c_hit_low, c_miss],
                    "Target query",
                    [500],
                    limit=10,
                    mode="B+rel",
                    embeddings_cache_path=cache_file,
                )
            # `c_miss` gets fallback rel=1.0 while `c_hit_low` gets cos ~ 0.1 -> `c_miss` ranks first, zero aot.embed calls!
            self.assertEqual(["51", "41"], [x["id"] for x in incidents])
            self.assertEqual(1, diag["rel_cache_misses"])


if __name__ == "__main__":
    unittest.main()
