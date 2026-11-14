import json
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import detect_domain as mod  # noqa: E402


class TestDomainDetector(unittest.TestCase):
    def test_domains_json_schema_and_justifications(self) -> None:
        raw = json.loads(mod.DOMAINS_PATH.read_text(encoding="utf-8"))
        self.assertIsInstance(raw, list)
        self.assertGreaterEqual(len(raw), 10)
        self.assertLessEqual(len(raw), 50)
        seen = set()
        for entry in raw:
            self.assertEqual(set(entry.keys()), {"domain", "category", "source"})
            self.assertIn(entry["category"], mod.VALID_CATEGORIES)
            self.assertTrue(entry["domain"] and "." in entry["domain"])
            self.assertGreaterEqual(len(entry["source"]), 20)
            self.assertNotIn(entry["domain"], seen)
            seen.add(entry["domain"])

    def test_curated_state_media_and_subdomain_blog_flagged(self) -> None:
        ref1 = "<ref>{{Verkkoviite | Osoite = https://www.rt.com/russia/12345/ | Nimeke = Uutinen}}</ref>"
        ref2 = "<ref>[http://esimerkki.blogspot.com/2020/05/teksti.html Blogikirjoitus]</ref>"
        cand = {
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "added", "kind": "reference", "text": ref1, "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                    {"op": "added", "kind": "reference", "text": ref2, "prose": "", "old_paragraph": 2, "new_paragraph": 2, "moved": False},
                ],
                "stats": {"is_blanking": False},
            }
        }
        out = mod.detect(cand)
        self.assertEqual(len(out), 2)
        self.assertEqual(out[0]["kind"], "source_domain")
        self.assertIn("rt.com", out[0]["label"])
        self.assertIn("state-controlled media", out[0]["label"])
        self.assertIn("esimerkki.blogspot.com", out[1]["label"])
        self.assertIn("blog or discussion forum", out[1]["label"])
        for sig in out:
            self.assertLessEqual(len(sig["evidence"]), 200)

    def test_reformatted_existing_domain_not_counted_as_added(self) -> None:
        cand = {
            "diff": {
                "contract": 1,
                "segments": [
                    {"op": "removed", "kind": "reference", "text": "<ref>http://kemppinen.blogspot.com/2007/01/a.html Kemppinen</ref>", "prose": "", "old_paragraph": 4, "new_paragraph": 4, "moved": False},
                    {"op": "added", "kind": "reference", "text": "<ref>Kemppinen http://kemppinen.blogspot.com/2007/01/a.html</ref>", "prose": "", "old_paragraph": 4, "new_paragraph": 4, "moved": False},
                ],
                "stats": {"is_blanking": False},
            }
        }
        self.assertEqual(mod.detect(cand), [])

    def test_reputable_domain_not_flagged_but_scored_in_corpus_stats(self) -> None:
        c_rev = {
            "rev_id": 101,
            "group": "reverted",
            "diff": {
                "segments": [
                    {"op": "added", "kind": "prose", "text": "[http://personal.inet.fi/koti/sivu.htm Sivu]", "prose": "", "old_paragraph": None, "new_paragraph": 3, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        c_ctl = {
            "rev_id": 102,
            "group": "control",
            "diff": {
                "segments": [
                    {"op": "added", "kind": "reference", "text": "<ref>https://www.hs.fi/kotimaa/art-1.html</ref>", "prose": "", "old_paragraph": 1, "new_paragraph": 1, "moved": False},
                ],
                "stats": {"is_blanking": False},
            },
        }
        self.assertEqual(len(mod.detect(c_rev)), 1)
        self.assertEqual(mod.detect(c_ctl), [])

        scores = {r["domain"]: r for r in mod.score_candidate_domains([c_rev, c_ctl])}
        self.assertEqual(scores["personal.inet.fi"]["reverted_rate"], 1.0)
        self.assertEqual(scores["hs.fi"]["reverted_rate"], 0.0)


if __name__ == "__main__":
    unittest.main()
