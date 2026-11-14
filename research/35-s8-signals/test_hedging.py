"""Unit tests for detect_hedging.py (P1.S8)."""
import unittest

import detect_hedging


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


class DetectHedgingTests(unittest.TestCase):
    def test_verb_flip_osoitti_vs_vaitti(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Tutkimus osoitti tapahtumien kulun."),
                    _seg("added", "Tutkimus väitti tapahtumien kulun."),
                ],
                "stats": {"is_blanking": False, "removed_prose_chars": 35},
            }
        }
        sigs = detect_hedging.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertEqual(sigs[0]["kind"], "attribution_flip")
        self.assertIn("low weight", sigs[0]["label"].lower())
        self.assertIn("flipped", sigs[0]["label"].lower())
        self.assertIn("osoitti", sigs[0]["evidence"])
        self.assertIn("väitti", sigs[0]["evidence"])

    def test_hedge_words_added_and_removed(self) -> None:
        for word in ("väitetysti", "muka", "oletettavasti"):
            cand = {
                "diff": {
                    "segments": [_seg("added", f"Joukot toimivat {word} käskyn varassa.")],
                    "stats": {"is_blanking": False, "removed_prose_chars": 0},
                }
            }
            sigs = detect_hedging.detect(cand)
            self.assertEqual(len(sigs), 1, msg=f"Failed for {word}")
            self.assertEqual(sigs[0]["kind"], "attribution_flip")
            self.assertIn("low weight", sigs[0]["label"].lower())
            self.assertLessEqual(len(sigs[0]["evidence"]), 200)

    def test_scare_quotes_wrapped_around_existing_word_via_wikitext(self) -> None:
        cand = {
            "old": "Teon motiiviksi esitettiin ahvenanmaalaisten avunpyynnöt ruotsalaisille.",
            "new": 'Teon motiiviksi esitettiin ahvenanmaalaisten "avunpyynnöt" ruotsalaisille.',
            "diff": {
                "segments": [
                    _seg("added", '"', kind="markup"),
                    _seg("added", '"', kind="markup"),
                ],
                "stats": {"is_blanking": False, "removed_prose_chars": 0},
            },
        }
        sigs = detect_hedging.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertIn("Scare quotes added", sigs[0]["label"])
        self.assertIn("low weight", sigs[0]["label"].lower())
        self.assertIn('"avunpyynnöt"', sigs[0]["evidence"])

    def test_ignores_ns_abbreviation_expansion_and_quote_style_change(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "ns."),
                    _seg("added", "niin sanotun"),
                    _seg("removed", 'asemansa ”itsenäisenä” valtiona', old_p=2, new_p=2),
                    _seg("added", 'asema "itsenäisestä" valtiosta', old_p=2, new_p=2),
                ],
                "stats": {"is_blanking": False, "removed_prose_chars": 30},
            }
        }
        self.assertEqual(detect_hedging.detect(cand), [])

    def test_ignores_mukaan_directional_and_idiomatic_uses(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("added", "Kun mukaan lasketaan siviilit, määrä kasvoi."),
                    _seg("removed", "Saksan mukaan lähteminen oli vaikea päätös.", old_p=2, new_p=2),
                    _seg("added", "Kaikki alueet, Karjala mukaan lukien, evakuoitiin.", old_p=3, new_p=3),
                ],
                "stats": {"is_blanking": False, "removed_prose_chars": 45},
            }
        }
        self.assertEqual(detect_hedging.detect(cand), [])

    def test_detects_genitive_attribution_removal(self) -> None:
        cand = {
            "diff": {
                "segments": [
                    _seg("removed", "Kansallisbiografian mukaan luku oli noin 200."),
                ],
                "stats": {"is_blanking": False, "removed_prose_chars": 45},
            }
        }
        sigs = detect_hedging.detect(cand)
        self.assertEqual(len(sigs), 1)
        self.assertIn("removed", sigs[0]["label"].lower())
        self.assertIn("Kansallisbiografian mukaan", sigs[0]["evidence"])


if __name__ == "__main__":
    unittest.main()
