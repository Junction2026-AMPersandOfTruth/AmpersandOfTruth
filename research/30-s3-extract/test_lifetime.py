"""Offline unit tests for `lifetime.py` (P1.S3 Approach B, #30)."""
from __future__ import annotations

import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import lifetime  # noqa: E402

NL = chr(10)
CR = chr(13)


class ProbeSelectionTests(unittest.TestCase):
    def test_selects_up_to_three_longest_verbatim_sentences_above_threshold(self) -> None:
        s1 = "Tämä ensimmäinen lause on täsmälleen kolmekymmentäseitsemän"
        s2 = "Toinen lisätty virke kertoo tapahtumasta huomattavasti pidemmin ja yksityiskohtaisemmin"
        s3 = "Kolmas virke ylittää myös kahdenkymmenenviiden merkin rajan selvästi"
        s4 = "Neljäs virke on yli 25 merkkiä pitkä"
        short = "Lyhyt virke"

        raw = f"{short}. {s1}! {s2}?{NL}{s3}."
        prose = f"{short}. {s1}! {s2}?{NL}{s3} … {s4}."
        # Note: s4 is in `prose` via ` … ` and we also include a second added prose segment for s4.
        segments = [
            {
                "op": "added",
                "kind": "prose",
                "moved": False,
                "text": raw,
                "prose": prose,
            },
            {
                "op": "added",
                "kind": "prose",
                "moved": False,
                "text": s4 + ".",
                "prose": s4 + ".",
            },
        ]
        probes = lifetime.select_probes(segments)
        self.assertEqual(len(probes), 3)
        self.assertEqual(probes, [s2, s3, s1])
        for p in probes:
            self.assertGreaterEqual(len(p), 25)
            self.assertTrue(any(p in seg["text"] for seg in segments))

    def test_skips_sentences_altered_by_inline_wikitext_markup_but_keeps_clean_ones(self) -> None:
        clean = "Tämä toinen lause esiintyy sellaisenaan raakatekstissä ilman linkkejä"
        seg = {
            "op": "added",
            "kind": "prose",
            "moved": False,
            "text": f"'''Urho Kekkonen''' oli [[Suomi|Suomen]] tasavallan pitkäaikainen presidentti. {clean}.",
            "prose": f"Urho Kekkonen oli Suomen tasavallan pitkäaikainen presidentti. {clean}.",
        }
        probes = lifetime.select_probes([seg])
        self.assertEqual(probes, [clean])

    def test_ignores_removed_moved_and_non_prose_segments_and_deduplicates(self) -> None:
        valid = "Vain lisätty varsinainen leipäteksti kelpaa seurantaan"
        segments = [
            {
                "op": "removed",
                "kind": "prose",
                "moved": False,
                "text": "Poistettu pitkä lause jota ei saa valita koettimeksi ollenkaan.",
                "prose": "Poistettu pitkä lause jota ei saa valita koettimeksi ollenkaan.",
            },
            {
                "op": "added",
                "kind": "prose",
                "moved": True,
                "text": "Siirretty kappale pysyi täysin ennallaan artikkelin rakenteessa.",
                "prose": "Siirretty kappale pysyi täysin ennallaan artikkelin rakenteessa.",
            },
            {
                "op": "added",
                "kind": "reference",
                "moved": False,
                "text": "<ref>Pitkä lähdeviite joka on yli kaksikymmentäviisi merkkiä pitkä.</ref>",
                "prose": "Pitkä lähdeviite joka on yli kaksikymmentäviisi merkkiä pitkä.",
            },
            {
                "op": "added",
                "kind": "template",
                "moved": False,
                "text": "{{Tietolaatikko | arvo = Pitkä tietolaatikon arvo yli kaksikymmentäviisi merkkiä}}",
                "prose": "Pitkä tietolaatikon arvo yli kaksikymmentäviisi merkkiä",
            },
            {"op": "added", "kind": "prose", "moved": False, "text": valid, "prose": valid},
            {"op": "added", "kind": "prose", "moved": False, "text": valid, "prose": valid},
        ]
        self.assertEqual(lifetime.select_probes(segments), [valid])
        self.assertEqual(lifetime.select_probes([]), [])
        self.assertEqual(lifetime.select_probes(None), [])


class NullRevisionFilteringTests(unittest.TestCase):
    def test_drops_consecutive_identical_sha1_and_keeps_later_reverts(self) -> None:
        revs = [
            {"rev_id": 10, "ts": "2020-01-01 00:00:00", "sha1": "aaa"},
            {"rev_id": 11, "ts": "2020-01-01 00:01:00", "sha1": "bbb"},
            {"rev_id": 12, "ts": "2020-01-01 00:02:00", "sha1": "bbb"},  # null rev (page move)
            {"rev_id": 13, "ts": "2020-01-01 00:02:00", "sha1": "bbb"},  # 2nd consecutive null rev
            {"rev_id": 14, "ts": "2020-01-01 00:03:00", "sha1": "aaa"},  # identity revert to 10 (NOT null!)
            {"rev_id": 15, "ts": "2020-01-01 00:04:00", "sha1": None},   # suppressed sha1 kept
            {"rev_id": 16, "ts": "2020-01-01 00:05:00", "sha1": None},   # None == None is not dropped as sha1 match
        ]
        # Pass out-of-order to verify sorting by (ts, rev_id) too.
        shuffled = [revs[4], revs[0], revs[2], revs[1], revs[3], revs[6], revs[5]]
        non_null = lifetime.drop_null_revisions(shuffled)
        self.assertEqual([r["rev_id"] for r in non_null], [10, 11, 14, 15, 16])

        after_11 = lifetime.revisions_after(non_null, 11)
        self.assertEqual([r["rev_id"] for r in after_11], [14, 15, 16])

        after_last = lifetime.revisions_after(non_null, 16)
        self.assertEqual(after_last, [])


class HalfRuleTests(unittest.TestCase):
    def test_at_least_half_rule_for_one_two_and_three_probes(self) -> None:
        # 1 probe: need 1/1
        self.assertTrue(lifetime.probes_present([1]))
        self.assertFalse(lifetime.probes_present([0]))

        # 2 probes: need >= 1/2
        self.assertTrue(lifetime.probes_present([42, 0]))
        self.assertTrue(lifetime.probes_present([0, 99]))
        self.assertTrue(lifetime.probes_present([12, 99]))
        self.assertFalse(lifetime.probes_present([0, 0]))

        # 3 probes: need >= 2/3
        self.assertTrue(lifetime.probes_present([10, 20, 0]))
        self.assertTrue(lifetime.probes_present([0, 20, 30]))
        self.assertTrue(lifetime.probes_present([10, 20, 30]))
        self.assertFalse(lifetime.probes_present([10, 0, 0]))
        self.assertFalse(lifetime.probes_present([0, 0, 0]))

        with self.assertRaises(ValueError):
            lifetime.probes_present([])


class BinarySearchTrackingTests(unittest.TestCase):
    def test_empty_horizon_means_still_present_with_zero_queries(self) -> None:
        res = lifetime.track_revisions([], lambda _r: False)
        self.assertIsNone(res.removing_rev_id)
        self.assertFalse(res.reappeared)
        self.assertIsNone(res.reappeared_rev_id)
        self.assertTrue(res.still_present_latest)
        self.assertEqual(res.queries, 0)
        self.assertTrue(res.boundary_verified)

    def test_never_removed_content(self) -> None:
        revs = [100 + i for i in range(50)]
        calls: list[int] = []

        def present(rev_id: int) -> bool:
            calls.append(rev_id)
            return True

        res = lifetime.track_revisions(revs, present)
        self.assertIsNone(res.removing_rev_id)
        self.assertFalse(res.reappeared)
        self.assertIsNone(res.reappeared_rev_id)
        self.assertTrue(res.still_present_latest)
        self.assertEqual(res.queries, len(calls))
        self.assertLessEqual(res.queries, 8)

    def test_permanent_removal_at_first_middle_and_last_revision(self) -> None:
        revs = [1000 + i for i in range(32)]
        for rem_idx in (0, 1, 7, 16, 31):
            with self.subTest(rem_idx=rem_idx):
                calls: list[int] = []

                def present(rev_id: int, cut: int = revs[rem_idx]) -> bool:
                    calls.append(rev_id)
                    return rev_id < cut

                res = lifetime.track_revisions(revs, present)
                self.assertEqual(res.removing_rev_id, revs[rem_idx])
                self.assertFalse(res.reappeared)
                self.assertIsNone(res.reappeared_rev_id)
                self.assertFalse(res.still_present_latest)
                self.assertTrue(res.boundary_verified)
                self.assertEqual(res.queries, len(set(calls)))
                self.assertEqual(len(calls), len(set(calls)))  # memoized: never queries same rev twice
                self.assertLessEqual(res.queries, 10)

    def test_removal_and_reappearance_binary_search(self) -> None:
        # Present on indices 0..4, absent on 5..44 (includes initial mid=32), reappears at 45..63.
        revs = [2000 + i for i in range(64)]
        calls: list[int] = []

        def present(rev_id: int) -> bool:
            calls.append(rev_id)
            idx = rev_id - 2000
            return idx < 5 or idx >= 45

        res = lifetime.track_revisions(revs, present)
        self.assertEqual(res.removing_rev_id, 2005)
        self.assertTrue(res.reappeared)
        self.assertEqual(res.reappeared_rev_id, 2045)
        self.assertTrue(res.still_present_latest)
        self.assertTrue(res.boundary_verified)
        self.assertEqual(res.queries, len(calls))
        self.assertLessEqual(res.queries, 40)

    def test_respects_query_cap(self) -> None:
        revs = list(range(1, 10000))
        calls = 0

        def present(rev_id: int) -> bool:
            nonlocal calls
            calls += 1
            return rev_id < 1234 or rev_id >= 7890

        res = lifetime.track_revisions(revs, present, max_queries=8)
        self.assertEqual(calls, 8)
        self.assertEqual(res.queries, 8)


if __name__ == "__main__":
    unittest.main()
