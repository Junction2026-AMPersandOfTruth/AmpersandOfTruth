import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import detect_templates as mod  # noqa: E402

LF = chr(10)


class TestMaintenanceTemplates(unittest.TestCase):
    def test_removing_template_without_other_section_changes_is_strong(self) -> None:
        old = LF.join([
            "== Sota alkaa ==",
            "Joukkoja keskitettiin rajalle.{{lähde}}",
            "",
            "== Seuraukset ==",
            "Rauha solmittiin.",
            "",
        ])
        new = LF.join([
            "== Sota alkaa ==",
            "Joukkoja keskitettiin rajalle.",
            "",
            "== Seuraukset ==",
            "Rauha solmittiin.",
            "",
        ])
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "removed", "kind": "template", "text": "{{lähde}}", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["kind"], "maintenance_template")
        self.assertIn("Strong:", out[0]["label"])
        self.assertIn("without other section changes", out[0]["label"])
        self.assertEqual(out[0]["evidence"], "{{lähde}}")

    def test_removing_template_with_section_fix_is_regular_variant(self) -> None:
        old = LF.join(["== Sota alkaa ==", "Joukkoja keskitettiin rajalle.{{Kenen mukaan}}", ""])
        new = LF.join(["== Sota alkaa ==", "Mannerheimin mukaan joukkoja keskitettiin rajalle.<ref>Mannerheim 1952.</ref>", ""])
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "prose", "text": "Mannerheimin mukaan", "prose": "Mannerheimin mukaan", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                    {"op": "removed", "kind": "template", "text": "{{Kenen mukaan}}", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                    {"op": "added", "kind": "reference", "text": "<ref>Mannerheim 1952.</ref>", "prose": "Mannerheim 1952.", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertNotIn("Strong:", out[0]["label"])
        self.assertEqual(out[0]["label"], "Removed maintenance template {{Kenen mukaan}}")

    def test_adding_dispute_templates_detected(self) -> None:
        cand = {
            "old": "Artikkelin alku." + LF,
            "new": "{{Neutraalius}}" + LF + "Artikkelin alku." + LF,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "template", "text": "{{Neutraalius}}", "prose": "", "old_paragraph": None, "new_paragraph": 0, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["label"], "Added maintenance template {{Neutraalius}}")
        self.assertEqual(out[0]["evidence"], "{{Neutraalius}}")

    def test_non_maintenance_templates_ignored(self) -> None:
        cand = {
            "old": "Teksti." + LF,
            "new": "{{Pääartikkeli|Talvisota}}" + LF + "Teksti." + LF,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "template", "text": "{{Pääartikkeli|Talvisota}}", "prose": "", "old_paragraph": None, "new_paragraph": 0, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        self.assertEqual(mod.detect(cand), [])

    def test_inline_tag_deleted_with_whole_paragraph_ignored(self) -> None:
        old = LF.join(["== Osio ==", "Poistettava kappale kokonaisuudessaan.{{lähde}}", ""])
        new = "== Osio ==" + LF
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "removed", "kind": "prose", "text": "Poistettava kappale kokonaisuudessaan.", "prose": "Poistettava kappale kokonaisuudessaan.", "old_paragraph": 1, "new_paragraph": None, "moved": False},
                    {"op": "removed", "kind": "template", "text": "{{lähde}}", "prose": "", "old_paragraph": 1, "new_paragraph": None, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        out = mod.detect(cand)
        self.assertEqual(len(out), 1 - 1)


if __name__ == "__main__":
    unittest.main()
