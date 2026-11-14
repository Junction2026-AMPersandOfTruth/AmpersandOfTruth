import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import detect_citation_swap as mod  # noqa: E402


def _cand(segments: list[dict], *, old: str = "", is_blanking: bool = False) -> dict:
    return {
        "old": old,
        "diff": {
            "contract": 1,
            "segments": segments,
            "stats": {"is_blanking": is_blanking},
        },
    }


class TestCitationSwap(unittest.TestCase):
    def test_domain_swap_detected_with_old_to_new_label(self) -> None:
        old_ref = "<ref>{{Verkkoviite | Osoite = https://www.hs.fi/kotimaa/art-1.html | Nimeke = Arkistotutkimus}}</ref>"
        new_ref = "<ref>{{Verkkoviite | Osoite = https://mvlehti.net/juttu-2 | Nimeke = Vaihtoehtoinen näkemys}}</ref>"
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": old_ref, "prose": "", "old_paragraph": 3, "new_paragraph": 3, "moved": False},
                {"op": "added", "kind": "reference", "text": new_ref, "prose": "", "old_paragraph": 3, "new_paragraph": 3, "moved": False},
            ]
        )
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["kind"], "citation_swapped")
        self.assertIn("hs.fi", out[0]["label"])
        self.assertIn("mvlehti.net", out[0]["label"])
        self.assertIn("→", out[0]["label"])
        self.assertEqual(out[0]["evidence"], new_ref[:200])
        self.assertLessEqual(len(out[0]["evidence"]), 200)

    def test_print_source_swap_detected(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": "<ref>Kansallisbiografia 2003, s. 112.</ref>", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                {"op": "added", "kind": "reference", "text": "<ref>Hurtti-Ukko 1941</ref>", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
            ]
        )
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertIn("Kansallisbiografia 2003", out[0]["label"])
        self.assertIn("Hurtti-Ukko 1941", out[0]["label"])
        self.assertEqual(out[0]["evidence"], "<ref>Hurtti-Ukko 1941</ref>")

    def test_wayback_archive_wrapping_not_flagged(self) -> None:
        old_ref = "<ref>[http://www.aamulehti.fi/artikkeli/123.html Venäläinen haaveili Karjalan liittämisestä]</ref>"
        new_ref = (
            "<ref>{{Verkkoviite | Osoite = <!-- http://www.aamulehti.fi/artikkeli/123.html --> "
            "| Nimeke = Venäläinen haaveili Karjalan liittämisestä "
            "| arkisto = https://web.archive.org/web/20110406174524/http://www.aamulehti.fi/artikkeli/123.html }}</ref>"
        )
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": old_ref, "prose": "", "old_paragraph": 2, "new_paragraph": 2, "moved": False},
                {"op": "added", "kind": "reference", "text": new_ref, "prose": "", "old_paragraph": 2, "new_paragraph": 2, "moved": False},
            ]
        )
        self.assertEqual(mod.detect(cand), [])

    def test_harvard_style_formatting_and_expansion_not_flagged(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": "<ref>Upton 1980, Haapala 1995</ref>", "prose": "", "old_paragraph": 8, "new_paragraph": 8, "moved": False},
                {"op": "added", "kind": "reference", "text": "<ref>Upton 1980, Ylikangas 1986, Haapala 1995</ref>", "prose": "", "old_paragraph": 8, "new_paragraph": 8, "moved": False},
            ]
        )
        self.assertEqual(mod.detect(cand), [])

    def test_empty_citation_template_with_only_access_date_ignored(self) -> None:
        empty_ref = "<ref>{{Verkkoviite | Osoite =  | Nimeke = | Viitattu = 18.4.2026 }}</ref>"
        added_ref = "<ref>{{Verkkoviite | Osoite = https://www.hs.fi/suomi/art-1.html | Nimeke = Uutinen }}</ref>"
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": empty_ref, "prose": "", "old_paragraph": 4, "new_paragraph": 4, "moved": False},
                {"op": "added", "kind": "reference", "text": added_ref, "prose": "", "old_paragraph": 4, "new_paragraph": 4, "moved": False},
            ]
        )
        self.assertEqual(mod.detect(cand), [])


if __name__ == "__main__":
    unittest.main()
