#!/usr/bin/env python3
"""Offline unit tests for P1.S3 (#30) approach A pure logic (`items.py` and `variant.py`).

    python3 -m unittest research/30-s3-extract/test_s3.py
"""
from __future__ import annotations

import pathlib
import sys
import unittest

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import items  # noqa: E402
import variant  # noqa: E402


class SentenceCountingTests(unittest.TestCase):
    def test_empty_and_punctuation_only(self) -> None:
        self.assertEqual(items.count_sentences(None), 0)
        self.assertEqual(items.count_sentences(""), 0)
        self.assertEqual(items.count_sentences("   " + chr(10) + "  …  --- "), 0)

    def test_finnish_ordinal_dates_and_dotted_dates(self) -> None:
        text = (
            "Leninin johtama Neuvosto-Venäjä tunnusti Suomen 6. joulukuuta 1917 "
            "julistaman itsenäisyyden ensimmäisenä valtiona."
        )
        self.assertEqual(items.count_sentences(text), 1)

        two = (
            "Ensimmäinen lentokone tuotiin sinne rautateitse 7.3.1918. "
            "17. maaliskuuta 1918 nousi lentokone ilmaan."
        )
        self.assertEqual(items.count_sentences(two), 2)

    def test_initials_and_abbreviations_do_not_split(self) -> None:
        self.assertEqual(
            items.count_sentences("Jussi T. Lappalainen kirjoitti kirjan. Se julkaistiin 1981."),
            2,
        )
        self.assertEqual(
            items.count_sentences("Mukana olivat mm. Suomi ja Ruotsi sekä esim. Norja."),
            1,
        )
        self.assertEqual(
            items.count_sentences("C. G. E. Mannerheim toimi ylipäällikkönä."),
            1,
        )

    def test_wikidiff_fragment_ellipsis_and_paragraphs(self) -> None:
        frag = "Muutos … porvaristoa … , koska ne saattoivat menettää valta-asemastaan"
        self.assertEqual(items.count_sentences(frag), 1)

        paras = "Ensimmäinen kappale. Toinen lause!" + chr(10) + "Kolmas kappale ilman pistettä"
        self.assertEqual(items.count_sentences(paras), 3)


class ItemExtractionHelperTests(unittest.TestCase):
    def test_editor_grouping_keys_never_leak_raw_ip(self) -> None:
        self.assertEqual(items.editor_grouping_key(None), "hidden")
        self.assertEqual(items.editor_grouping_key("  "), "hidden")
        self.assertEqual(items.editor_grouping_key("85.76.12.34"), "ip4:85.76")
        self.assertEqual(items.editor_grouping_key("2001:14bb:12:34::1"), "ip6:2001:14BB")
        self.assertEqual(items.editor_grouping_key("~2025-26183-73"), "temp:~2025-26183-73")
        self.assertEqual(items.editor_grouping_key("Matti Meikäläinen"), "user:Matti Meikäläinen")

    def test_choose_inserting_revs_filters_then_falls_back(self) -> None:
        span_edits = [
            {"rev_id": 102, "user": "85.76.1.2", "anonymous": 1},
            {"rev_id": 100, "user": "Matti", "anonymous": 0},
        ]
        diffs = {
            100: {"added_prose_chars": 0, "added_refs": 0},
            102: {"added_prose_chars": 45, "added_refs": 0},
        }
        chosen = items.choose_inserting_revs(span_edits, diffs)
        self.assertEqual([e["rev_id"] for e in chosen], [102])

        # Fallback when no edit added prose or refs: returns all span edits sorted ascending.
        diffs_zero = {
            100: {"added_prose_chars": 0, "added_refs": 0},
            102: {"added_prose_chars": 0, "added_refs": 0},
        }
        fallback = items.choose_inserting_revs(span_edits, diffs_zero)
        self.assertEqual([e["rev_id"] for e in fallback], [100, 102])

    def test_wikitext_and_segment_kinds_ignore_moves(self) -> None:
        segs = [
            {"op": "added", "kind": "prose", "text": "Väite tekstissä.", "new_paragraph": 4, "moved": False},
            {"op": "added", "kind": "reference", "text": "<ref>Lähde 2020</ref>", "new_paragraph": 4, "moved": False},
            {"op": "added", "kind": "prose", "text": "Siirretty kappale.", "new_paragraph": 8, "moved": True},
            {"op": "removed", "kind": "prose", "text": "Vanha lause.", "old_paragraph": 4, "moved": False},
        ]
        wt = items.extract_inserted_wikitext(segs, max_chars=20_000)
        self.assertEqual(wt, "Väite tekstissä.<ref>Lähde 2020</ref>")
        kinds = items.summarize_segment_kinds(segs)
        self.assertEqual(kinds, {
            "prose": {"added": 1, "removed": 1},
            "reference": {"added": 1, "removed": 0},
        })


class NormalisationAndMergingTests(unittest.TestCase):
    def test_normalise_prose(self) -> None:
        a = "  Suomi liittyi   sotaan Saksan rinnalla. "
        b = "suomi liittyi … sotaan saksan rinnalla!"
        self.assertEqual(variant.normalise_prose(a), "suomi liittyi sotaan saksan rinnalla")
        self.assertEqual(variant.normalise_prose(a), variant.normalise_prose(b))
        self.assertEqual(variant.normalise_prose(""), "")
        self.assertEqual(variant.normalise_prose("... --- !!!"), "")

    def test_merge_by_equal_prose_and_overlapping_revs_but_not_empty_prose(self) -> None:
        def mk(rev: int, ins: list[int], prose: str, t1: str, t2: str, reins: int | None = None) -> dict:
            return {
                "reverting_rev_id": rev,
                "page_id": 1256,
                "class": "edit_war",
                "inserting_rev_ids": ",".join(str(x) for x in ins),
                "inserting_rev_ids_list": ins,
                "inserting_editors": '["user:EditorA"]',
                "inserting_editors_list": ["user:EditorA"],
                "inserting_anonymous_only": 0,
                "inserted_prose": prose,
                "inserted_at": t1,
                "removed_at": t2,
                "exposure_seconds": 3600,
                "reinserted_rev_id": reins,
                "prose_chars": len(prose),
                "sentence_count": 1 if prose else 0,
                "refs_added": 0,
            }

        # Items 1 and 2 have identical normalised prose (re-insertion);
        # Items 3 and 4 both have empty inserted_prose and disjoint inserting_rev_ids -> must NOT merge!
        # Item 5 overlaps Item 2's inserting_rev_id [20] -> merges transitively with {1, 2}.
        i1 = mk(11, [10], "Kiistanalainen väite.", "2020-01-01 10:00:00", "2020-01-01 11:00:00", reins=20)
        i2 = mk(21, [20], "  kiistanalainen   väite! ", "2020-01-01 12:00:00", "2020-01-01 14:00:00")
        i3 = mk(31, [30], "", "2020-01-02 10:00:00", "2020-01-02 11:00:00")
        i4 = mk(41, [40], "", "2020-01-03 10:00:00", "2020-01-03 11:00:00")
        i5 = mk(51, [20, 50], "Laajennettu kiistanalainen väite.", "2020-01-01 13:00:00", "2020-01-01 15:00:00")

        groups = variant.merge_page_items([i1, i2, i3, i4, i5])
        self.assertEqual(len(groups), 3)

        inc_main = variant.build_incident_from_group(1256, groups[0], {})
        self.assertEqual(inc_main["inserting_rev_ids"], [10, 20, 50])
        self.assertEqual(inc_main["removing_rev_ids"], [11, 21, 51])
        # Windows: [10:00, 11:00] (1h) + union([12:00, 14:00], [13:00, 15:00]) = [12:00, 15:00] (3h) => 4h = 14400s.
        self.assertEqual(inc_main["total_exposure_seconds"], 14_400)
        # 3 merged spans -> 2 reinsertions (reins=20 is already in inserting_rev_ids, not double-counted).
        self.assertEqual(inc_main["reinsertion_count"], 2)
        self.assertEqual(inc_main["first_inserted_at"], "2020-01-01T10:00:00Z")
        self.assertEqual(inc_main["last_removed_at"], "2020-01-01T15:00:00Z")


class ScoreFormulaTests(unittest.TestCase):
    def test_contested_claim_outranks_drive_by_graffiti_across_all_formulas(self) -> None:
        contested_feat = {
            "prose_chars": 380,
            "sentence_count": 3,
            "refs_added": 1,
            "total_exposure_seconds": 5 * 86400,
            "reinsertion_count": 2,
            "named_inserter": True,
            "distinct_editors": 2,
            "classes": ["edit_war", "dispute"],
            "removal_reasons": ["neutrality_pov", "sourcing"],
        }
        graffiti_feat = {
            "prose_chars": 28,
            "sentence_count": 1,
            "refs_added": 0,
            "total_exposure_seconds": 90,
            "reinsertion_count": 0,
            "named_inserter": False,
            "distinct_editors": 1,
            "classes": ["graffiti_cleanup"],
            "removal_reasons": ["vandalism_test"],
        }
        for fname in ("f1_additive", "f2_evidence_aligned", "f3_dispute_gated"):
            s_good = variant.score_incident(contested_feat, formula=fname)
            s_bad = variant.score_incident(graffiti_feat, formula=fname)
            self.assertGreater(s_good, s_bad + 4.0, msg=f"formula {fname} failed separation")


if __name__ == "__main__":
    unittest.main()
