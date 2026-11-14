"""Unit tests for detect_loaded_terms.py and loaded_pairs.json (P1.S8)."""
import unittest

import detect_loaded_terms


def _seg(op: str, prose: str, kind: str = "prose", old_p: int | None = 1, new_p: int | None = 1) -> dict:
    return {
        "op": op,
        "kind": kind,
        "text": prose,
        "prose": prose,
        "old_paragraph": old_p,
        "new_paragraph": new_p,
        "moved": False,
    }


class DetectLoadedTermsTests(unittest.TestCase):
    def test_loaded_pairs_json_schema_is_valid(self) -> None:
        pairs = detect_loaded_terms.load_pairs()
        self.assertGreaterEqual(len(pairs), 10)
        for entry in pairs:
            self.assertEqual(set(entry.keys()), {"a", "b", "note", "example_rev_id"})
            self.assertTrue(isinstance(entry["a"], str) and entry["a"].strip())
            self.assertTrue(isinstance(entry["b"], str) and entry["b"].strip())
            self.assertTrue(isinstance(entry["note"], str) and entry["note"].strip())
            self.assertIsInstance(entry["example_rev_id"], int)

    def test_inflected_karjala_swap_luovutettu_to_miehitetty(self) -> None:
        cand = {
            "old": "Artikkeli käsittelee [[Luovutettu Karjala|luovutetussa Karjalassa]] sijainneita kuntia.",
            "new": "Artikkeli käsittelee [[Miehitetty Karjala|miehitetyssä Karjalassa]] sijainneita kuntia.",
            "diff": {
                "segments": [
                    _seg("removed", "luovutetussa Karjalassa"),
                    _seg("added", "miehitetyssä Karjalassa"),
                ],
                "stats": {"is_blanking": False},
            },
        }
        sigs = detect_loaded_terms.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["kind"], "loaded_term")
        self.assertIn("luovutettu → miehitetty", sigs[0]["label"])
        self.assertIn("luovutetussa Karjalassa", sigs[0]["evidence"])
        self.assertIn("miehitetyssä Karjalassa", sigs[0]["evidence"])
        self.assertLessEqual(len(sigs[0]["evidence"]), 200)

    def test_inflected_sisallissota_vapaussota_and_reverse_direction(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Vapaussodassa kaatuneiden muistomerkki"),
                    _seg("added", "Sisällissodassa kaatuneiden muistomerkki"),
                ],
                "stats": {"is_blanking": False},
            }
        }
        sigs = detect_loaded_terms.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["label"], "Paired term swap: vapaussota → sisällissota")

    def test_custom_pairs_handle_finnish_stem_gradation(self) -> None:
        custom_pairs = [
            {"a": "punaiset", "b": "punikit", "note": "test", "example_rev_id": 1},
            {"a": "valkoiset", "b": "lahtarit", "note": "test", "example_rev_id": 2},
            {"a": "hyökkäys", "b": "vapautus", "note": "test", "example_rev_id": 3},
        ]
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Punaisten hyökkäyksessä vangittiin valkoisia."),
                    _seg("added", "Punikkien vapautuksessa vangittiin lahtareita."),
                ],
                "stats": {"is_blanking": False},
            }
        }
        sigs = detect_loaded_terms.detect(cand, pairs=custom_pairs)
        self.assertEqual(len(sigs), 3)
        labels = {s["label"] for s in sigs}
        self.assertIn("Paired term swap: punaiset → punikit", labels)
        self.assertIn("Paired term swap: hyökkäys → vapautus", labels)
        self.assertIn("Paired term swap: valkoiset → lahtarit", labels)

    def test_no_false_positive_when_same_term_reformatted(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Vapaussodan historian komitea, 1922"),
                    _seg("added", "Vapaussodan historian komitea (1922)"),
                ],
                "stats": {"is_blanking": False},
            }
        }
        self.assertEqual(detect_loaded_terms.detect(cand), [])

    def test_inflected_tti_gradation_valkoinen_senaatti(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "lopputulos = Suomen valkoisen senaatin voitto"),
                    _seg("added", "lopputulos = Suomen tasavallan joukkojen voitto"),
                ],
                "stats": {"is_blanking": False},
            }
        }
        sigs = detect_loaded_terms.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["label"], "Paired term swap: valkoinen senaatti → tasavallan joukot")


if __name__ == "__main__":
    unittest.main()
