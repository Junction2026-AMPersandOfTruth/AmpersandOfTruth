"""Unit tests for detect_numbers.py (P1.S8)."""
import unittest

import detect_numbers


def _seg(op: str, prose: str, kind: str = "prose", old_p: int | None = 1, new_p: int | None = 1, text: str | None = None) -> dict:
    return {
        "op": op,
        "kind": kind,
        "text": prose if text is None else text,
        "prose": prose,
        "old_paragraph": old_p,
        "new_paragraph": new_p,
        "moved": False,
    }


class DetectNumbersTests(unittest.TestCase):
    def test_finnish_space_thousand_separator_without_source(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Kaatuneita oli 25 000 miestä."),
                    _seg("added", "Kaatuneita oli 250 000 miestä."),
                ],
                "stats": {"added_refs": 0, "removed_refs": 0, "is_blanking": False},
            }
        }
        sigs = detect_numbers.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["kind"], "number_changed")
        self.assertEqual(sigs[0]["label"], "Quantity changed without a new source")
        self.assertEqual(sigs[0]["evidence"], "25 000 → 250 000")

    def test_nbsp_thousand_separator_split_by_wikidiff_reconstructed_from_wikitext(self) -> None:
        # Wikidiff splits "25&nbsp;000" -> "250&nbsp;000" into "25" -> "250" because "&nbsp;000" is unchanged.
        cand = {
            "old": "Taistelussa kaatui 25&nbsp;000 sotilasta.",
            "new": "Taistelussa kaatui 250&nbsp;000 sotilasta.",
            "diff": {
                "segments": [
                    _seg("removed", "25"),
                    _seg("added", "250"),
                ],
                "stats": {"added_refs": 0, "removed_refs": 0, "is_blanking": False},
            },
        }
        sigs = detect_numbers.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["evidence"], "25 000 → 250 000")
        self.assertEqual(sigs[0]["label"], "Quantity changed without a new source")

    def test_finnish_decimal_comma_with_new_source(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Kannatus oli 12,4 %."),
                    _seg("added", "Kannatus oli 32,8 %."),
                    _seg("added", "Tilastokeskus 2024", kind="reference", text="<ref>Tilastokeskus 2024</ref>"),
                ],
                "stats": {"added_refs": 1, "removed_refs": 0, "is_blanking": False},
            }
        }
        sigs = detect_numbers.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["label"], "Quantity changed (with new source)")
        self.assertEqual(sigs[0]["evidence"], "12,4 → 32,8")

    def test_distinguishes_year_from_quantity(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Sopimus solmittiin vuonna 1940."),
                    _seg("added", "Sopimus solmittiin vuonna 1944."),
                ],
                "stats": {"added_refs": 0, "removed_refs": 0, "is_blanking": False},
            }
        }
        sigs = detect_numbers.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["label"], "Year changed without a new source")
        self.assertEqual(sigs[0]["evidence"], "1940 → 1944")

    def test_ignores_formatting_only_separator_and_dash_changes(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "4000"),
                    _seg("added", "4&nbsp;000", text="4&nbsp;000"),
                    _seg("removed", "1939-1940", old_p=2, new_p=2),
                    _seg("added", "1939–1940", old_p=2, new_p=2),
                ],
                "stats": {"added_refs": 0, "removed_refs": 0, "is_blanking": False},
            }
        }
        self.assertEqual(detect_numbers.detect(cand), [])

    def test_ignores_prose_rewrite_with_different_words(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Vuonna 1939 alkoi talvisota."),
                    _seg("added", "Vuonna 1941 syttyi jatkosota."),
                ],
                "stats": {"added_refs": 0, "removed_refs": 0, "is_blanking": False},
            }
        }
        self.assertEqual(detect_numbers.detect(cand), [])

    def test_broken_ref_swallowing_existing_prose_is_not_counted_as_new_source(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "6 000–9 000"),
                    _seg("added", "4000"),
                    _seg("removed", "Dokumentti 2005 http://example.org", kind="reference"),
                    _seg("removed", "Aatteen sammuminen tapahtui 1940-luvulla.", old_p=2, new_p=None),
                    _seg("added", "Dokumentti 2005 Aatteen sammuminen tapahtui 1940-luvulla.", kind="reference"),
                ],
                "stats": {"added_refs": 1, "removed_refs": 1, "is_blanking": False},
            }
        }
        sigs = detect_numbers.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["label"], "Quantity changed without a new source")
        self.assertEqual(sigs[0]["evidence"], "6 000–9 000 → 4000")


if __name__ == "__main__":
    unittest.main()
