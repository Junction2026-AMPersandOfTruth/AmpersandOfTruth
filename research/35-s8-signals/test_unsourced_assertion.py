import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import detect_unsourced_assertion as mod  # noqa: E402

LF = chr(10)


class TestUnsourcedAssertion(unittest.TestCase):
    def test_substantive_unsourced_prose_in_referenced_section_flagged(self) -> None:
        old = LF.join([
            "== Tausta ==",
            "Suomi julistautui itsenäiseksi joulukuussa 1917.<ref>Haapala 1995, s. 45.</ref>",
            "",
        ])
        added = (
            "Lisäksi salaisissa neuvotteluissa sovittiin koko pohjoisen rajan siirtämisestä "
            "itään päin ilman eduskunnan virallista suostumusta."
        )
        new = old.rstrip() + " " + added + LF
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {
                        "op": "added",
                        "kind": "prose",
                        "text": added,
                        "prose": added,
                        "old_paragraph": 1,
                        "new_paragraph": 1,
                        "moved": False,
                    }
                ],
                "stats": {"is_blanking": False},
            },
        }
        out = mod.detect(cand)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]["kind"], "unsourced_addition")
        self.assertEqual(out[0]["evidence"], added[:200])
        self.assertLessEqual(len(out[0]["evidence"]), 200)

    def test_sourced_addition_not_flagged(self) -> None:
        old = LF.join(["== Tausta ==", "Alkuperäinen kappale.<ref>Manninen 1993.</ref>", ""])
        added = (
            "Tämä uusi virke on yli kahdeksankymmentä merkkiä pitkä ja sisältää uuden "
            "historiallisen väitteen lähteistettynä."
        )
        ref = "<ref>Tikka 2004, s. 88.</ref>"
        new = LF.join(["== Tausta ==", f"Alkuperäinen kappale.<ref>Manninen 1993.</ref> {added}{ref}", ""])
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "prose", "text": added, "prose": added, "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                    {"op": "added", "kind": "reference", "text": ref, "prose": "Tikka 2004, s. 88.", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        self.assertEqual(mod.detect(cand), [])

    def test_unreferenced_parent_section_not_flagged(self) -> None:
        old = LF.join(["== Tausta ==", "Tässä osiossa ei ole lainkaan viitteitä entuudestaan.", ""])
        added = (
            "Lisätään yli kahdeksankymmentä merkkiä tekstiä täysin viitteettömään osioon "
            "ilman lähdeviitettä."
        )
        new = old.rstrip() + " " + added + LF
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "prose", "text": added, "prose": added, "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        self.assertEqual(mod.detect(cand), [])

    def test_short_addition_below_80_chars_not_flagged(self) -> None:
        old = "Kappale, jossa on viite.<ref>Lähde 2001.</ref>" + LF
        added = "Lyhyt lisäys ilman viitettä."
        new = old.rstrip() + " " + added + LF
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "prose", "text": added, "prose": added, "old_paragraph": 0, "new_paragraph": 0, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        self.assertEqual(mod.detect(cand), [])

    def test_bibliography_appendix_section_not_flagged(self) -> None:
        old = LF.join([
            "Johdanto.<ref>Lähde 1999.</ref>",
            "",
            "== Kirjallisuutta ==",
            "* Vanha teos 1990.<ref>Huom.</ref>",
            "",
        ])
        entry = "* Mononen, Väinö: Talvisota Suomussalmella I. Helsinki: Books on Demand, 2021. ISBN 978-952-80-4321-8."
        new = old + entry + LF
        cand = {
            "old": old,
            "new": new,
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "prose", "text": entry, "prose": entry, "old_paragraph": None, "new_paragraph": 3, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        self.assertEqual(mod.detect(cand), [])


if __name__ == "__main__":
    unittest.main()
