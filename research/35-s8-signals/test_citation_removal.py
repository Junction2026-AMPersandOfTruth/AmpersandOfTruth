import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import detect_citation_removal as mod  # noqa: E402


def _cand(segments: list[dict], *, old: str = "", new: str = "", is_blanking: bool = False) -> dict:
    return {
        "old": old,
        "new": new,
        "diff": {
            "contract": 1,
            "segments": segments,
            "stats": {"is_blanking": is_blanking},
        },
    }


class TestCitationRemoval(unittest.TestCase):
    def test_ref_deleted_while_sentence_stays(self) -> None:
        ref = '<ref>{{Verkkoviite | Osoite = https://yle.fi/a/123 | Nimeke = Arkistolähde}}</ref>'
        cand = _cand(
            [
                {
                    "op": "removed",
                    "kind": "reference",
                    "text": ref,
                    "prose": "https://yle.fi/a/123; Arkistolähde",
                    "old_paragraph": 2,
                    "new_paragraph": 2,
                    "moved": False,
                }
            ],
            old=f"Sopimus solmittiin vuonna 1940.{ref}",
            new="Sopimus solmittiin vuonna 1940.",
        )
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["kind"], "citation_removed")
        self.assertIn("yle.fi", out[0]["label"])
        self.assertEqual(out[0]["evidence"], ref)
        self.assertLessEqual(len(out[0]["evidence"]), 200)

    def test_ref_deleted_while_sentence_altered(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "prose", "text": "Todellisuudessa kyseessä", "prose": "Todellisuudessa kyseessä", "old_paragraph": 0, "new_paragraph": 0, "moved": False},
                {"op": "added", "kind": "prose", "text": "Kyseessä saattoi olla", "prose": "Kyseessä saattoi olla", "old_paragraph": 0, "new_paragraph": 0, "moved": False},
                {"op": "removed", "kind": "reference", "text": "<ref name=Yle />", "prose": "", "old_paragraph": 0, "new_paragraph": 0, "moved": False},
            ]
        )
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["kind"], "citation_removed")
        self.assertEqual(out[0]["evidence"], "<ref name=Yle />")

    def test_whole_paragraph_deleted_not_flagged(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "prose", "text": "Koko kappale poistettiin.", "prose": "Koko kappale poistettiin.", "old_paragraph": 4, "new_paragraph": None, "moved": False},
                {"op": "removed", "kind": "reference", "text": "<ref>Manninen 1993, s. 12</ref>", "prose": "Manninen 1993, s. 12", "old_paragraph": 4, "new_paragraph": None, "moved": False},
            ]
        )
        self.assertEqual(mod.detect(cand), [])

    def test_section_blanking_not_flagged(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": "<ref>Jakobson 1955, s. 394.</ref>", "prose": "Jakobson 1955", "old_paragraph": 4, "new_paragraph": 3, "moved": False},
            ],
            is_blanking=True,
        )
        self.assertEqual(mod.detect(cand), [])

    def test_reformatted_or_shortened_named_ref_not_flagged(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": '<ref name="YLE">YLE: Suomen tie jatkosotaan, 2010.</ref>', "prose": "YLE", "old_paragraph": 5, "new_paragraph": 5, "moved": False},
                {"op": "added", "kind": "reference", "text": '<ref name="YLE"/>', "prose": "", "old_paragraph": 5, "new_paragraph": 5, "moved": False},
            ]
        )
        self.assertEqual(mod.detect(cand), [])

    def test_in_place_swap_deferred_to_citation_swapped(self) -> None:
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": "<ref>https://hs.fi/a/1</ref>", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                {"op": "added", "kind": "reference", "text": "<ref>https://mvlehti.net/b/2</ref>", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
            ]
        )
        self.assertEqual(mod.detect(cand), [])

    def test_evidence_truncated_to_200_chars(self) -> None:
        long_ref = "<ref>" + ("Arkistolähde ja sivuviittaus " * 10) + "</ref>"
        cand = _cand(
            [
                {"op": "removed", "kind": "reference", "text": long_ref, "prose": "Arkistolähde", "old_paragraph": 0, "new_paragraph": 0, "moved": False},
            ]
        )
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(len(out[0]["evidence"]), 200)
        self.assertTrue(long_ref.startswith(out[0]["evidence"]))


if __name__ == "__main__":
    unittest.main()
