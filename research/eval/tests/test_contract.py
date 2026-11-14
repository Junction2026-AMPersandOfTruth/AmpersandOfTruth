"""Run: python3 -m unittest discover -s research/eval/tests   (standard library only; not part of verify.sh)"""
import copy
import json
import subprocess
import sys
import unittest
from pathlib import Path

EVAL = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(EVAL))
import contract  # noqa: E402

EXAMPLES = EVAL / "examples"
EXAMPLE_FILES = {
    "golden": "golden.example.json",
    "retrieval-request": "retrieval.request.json",
    "retrieval-response": "retrieval.response.json",
    "signals-request": "signals.request.json",
    "signals-response": "signals.response.json",
    "pipeline-request": "pipeline.request.json",
    "pipeline-response": "pipeline.response.json",
    "judgements": "judgements.jsonl",
    "reviews": "reviews.jsonl",
}


def example(shape: str):
    text = (EXAMPLES / EXAMPLE_FILES[shape]).read_text(encoding="utf-8")
    if contract.SHAPES[shape][1]:
        return [json.loads(line) for line in text.splitlines() if line.strip()]
    return json.loads(text)


def golden():
    return copy.deepcopy(example("golden"))


def topic(doc, n=0):
    return doc["topics"][n]


class Examples(unittest.TestCase):
    def test_every_shape_has_a_valid_example(self):
        self.assertEqual(set(EXAMPLE_FILES), set(contract.SHAPES) - {"incident"})
        for shape in EXAMPLE_FILES:
            docs = example(shape) if contract.SHAPES[shape][1] else [example(shape)]
            for doc in docs:
                self.assertEqual([], contract.validate(shape, doc).errors, shape)

    def test_every_schema_only_uses_supported_keywords(self):
        for path in contract.SCHEMA_DIR.glob("*.schema.json"):
            contract.check_schema(None, json.loads(path.read_text(encoding="utf-8")))   # raises on an unsupported keyword

    def test_subset_interpreter_agrees_with_the_jsonschema_library(self):
        try:
            import jsonschema
            from referencing import Registry, Resource
        except ImportError:
            self.skipTest("jsonschema not installed")
        registry = Registry().with_resources(
            (p.name, Resource.from_contents(json.loads(p.read_text(encoding="utf-8")))) for p in contract.SCHEMA_DIR.glob("*.schema.json"))
        broken = golden()
        topic(broken)["items"][0]["inserting_rev_id"] = topic(broken)["items"][0].pop("inserting_rev_ids")
        cases = [(shape, doc) for shape in EXAMPLE_FILES for doc in (example(shape) if contract.SHAPES[shape][1] else [example(shape)])]
        cases += [("golden", broken), ("signals-response", {"contract": 1, "incidents": [{"page_id": "1"}]}), ("retrieval-response", {"contract": 2})]
        for shape, doc in cases:
            schema = contract.load_schema(contract.SHAPES[shape][0])
            theirs = not list(jsonschema.Draft202012Validator(schema, registry=registry).iter_errors(doc))
            ours = not contract.check_schema(doc, schema)
            self.assertEqual(theirs, ours, shape)

    def test_requests_the_runner_builds_match_the_examples(self):
        t = topic(example("golden"))
        self.assertEqual(example("retrieval-request"), contract.retrieval_request(t["queries"][0]))
        self.assertEqual(example("signals-request"), contract.signals_request(t))
        self.assertEqual(example("pipeline-request"), contract.pipeline_request(t["queries"][0]))


class GoldenRules(unittest.TestCase):
    def errors(self, doc, final=False):
        return contract.validate("golden", doc, final=final).errors

    def assertRejected(self, doc, fragment):
        errors = self.errors(doc)
        self.assertTrue(any(fragment in e for e in errors), f"expected an error containing {fragment!r}, got {errors}")

    def test_typo_in_a_field_name_is_caught(self):
        doc = golden()
        topic(doc)["items"][0]["removing_rev_id"] = [1]
        self.assertRejected(doc, "unknown field 'removing_rev_id'")

    def test_label_must_be_an_evidence_level_id(self):
        doc = golden()
        topic(doc)["items"][0]["label"] = "likely campaign"
        self.assertRejected(doc, "must be one of")

    def test_not_incident_needs_an_exclusion_and_incidents_must_not_have_one(self):
        doc = golden()
        del topic(doc)["items"][3]["exclusion"]
        self.assertRejected(doc, "needs an exclusion")
        doc = golden()
        topic(doc)["items"][0]["exclusion"] = "bot"
        self.assertRejected(doc, "only for label not-incident")

    def test_incident_needs_a_removal(self):
        doc = golden()
        topic(doc)["items"][2]["removing_rev_ids"] = []
        self.assertRejected(doc, "needs at least one removing rev id")

    def test_item_page_must_be_a_topic_article(self):
        doc = golden()
        topic(doc)["items"][0]["page_id"] = 123
        self.assertRejected(doc, "is not one of the topic's articles")

    def test_inserting_rev_shared_by_two_items_is_ambiguous(self):
        doc = golden()
        topic(doc)["items"][3]["inserting_rev_ids"] = [5000101]
        self.assertRejected(doc, "matching would be ambiguous")

    def test_rev_cannot_insert_and_remove(self):
        doc = golden()
        topic(doc)["items"][0]["removing_rev_ids"] = [5000101]
        self.assertRejected(doc, "both inserting and removing")

    def test_page_with_items_cannot_straddle_splits(self):
        doc = golden()
        other = copy.deepcopy(topic(doc))
        other.update(id="example-other", split="test")
        other["queries"] = [{"id": "example-other.en", "style": "en", "lang": "en", "text": "x"}]
        other["items"] = [dict(topic(doc)["items"][4], id="example-other/n01", inserting_rev_ids=[7], removing_rev_ids=[8])]
        doc["topics"].append(other)
        self.assertRejected(doc, "test split would leak")

    def test_ids_are_prefixed_and_unique(self):
        doc = golden()
        topic(doc)["queries"][0]["id"] = "something-else.en"
        self.assertRejected(doc, "must start with 'example-border-treaty.'")
        doc = golden()
        topic(doc)["items"][1]["id"] = topic(doc)["items"][0]["id"]
        self.assertRejected(doc, "duplicate item id")

    def test_out_of_corpus_control_has_no_articles(self):
        doc = golden()
        topic(doc, 2)["articles"] = [{"page_id": 1, "grade": 2, "title": "x"}]
        self.assertRejected(doc, "no articles and no items")

    def test_unverified_contested_label_warns_until_sign_off(self):
        doc = golden()
        topic(doc)["items"][0]["human_verified"] = False
        self.assertEqual([], self.errors(doc))
        self.assertTrue(any("not human-verified" in w for w in contract.validate("golden", doc).warnings))
        self.assertTrue(any("not human-verified" in e for e in self.errors(doc, final=True)))

    def test_size_targets_are_errors_only_at_sign_off(self):
        self.assertEqual([], self.errors(golden()))
        self.assertTrue(any("size:" in e for e in self.errors(golden(), final=True)))

    def test_counts(self):
        c = contract.golden_counts(example("golden"))
        self.assertEqual((1, 3, 2, 1, 1), (c["topics"], c["incidents"], c["non_incidents"], c["controls_quiet"], c["controls_out_of_corpus"]))


class ResponseRules(unittest.TestCase):
    def test_incident_minimum_is_five_fields_and_extras_are_allowed(self):
        minimal = {"page_id": 1, "inserting_rev_ids": [10], "removing_rev_ids": [11], "tier": "reverted", "score": 0}
        self.assertEqual([], contract.validate("incident", minimal).errors)
        self.assertEqual([], contract.validate("incident", dict(minimal, my_debug={"x": 1})).errors)
        for missing in minimal:
            self.assertFalse(contract.validate("incident", {k: v for k, v in minimal.items() if k != missing}).ok, missing)

    def test_tier_must_be_a_level_id(self):
        resp = example("signals-response")
        resp["incidents"][0]["tier"] = "not-incident"
        self.assertFalse(contract.validate("signals-response", resp).ok)

    def test_duplicate_results_are_rejected(self):
        resp = example("retrieval-response")
        resp["results"].append({"page_id": 900001})
        self.assertFalse(contract.validate("retrieval-response", resp).ok)

    def test_cross_checks_against_the_request_are_warnings(self):
        resp = example("signals-response")
        resp["incidents"][1]["page_id"] = 4242
        resp["incidents"].append(dict(resp["incidents"][2]))
        rep = contract.validate("signals-response", resp, request=example("signals-request"))
        self.assertTrue(rep.ok)
        self.assertTrue(any("was not in the request's page_ids" in w for w in rep.warnings))
        self.assertTrue(any("scored as a duplicate" in w for w in rep.warnings))

    def test_true_is_not_the_contract_number(self):
        self.assertFalse(contract.validate("retrieval-response", {"contract": True, "out_of_scope": False, "results": []}).ok)

    def test_incident_key_is_order_independent(self):
        self.assertEqual("7+30+200", contract.incident_key([200, 7, 30, 7]))

    def test_judgement_exclusion_pairing(self):
        line = example("judgements")[1]
        self.assertFalse(contract.validate("judgements", dict(line, label="not-incident")).ok)
        self.assertFalse(contract.validate("judgements", dict(line, exclusion="bot")).ok)


class Cli(unittest.TestCase):
    def run_cli(self, *args):
        return subprocess.run([sys.executable, str(EVAL / "validate.py"), *args], capture_output=True, text=True)

    def test_template_variant_passes_for_both_kinds(self):
        for kind in ("retrieval", "signals"):
            done = self.run_cli("variant", kind, "--", sys.executable, str(EXAMPLES / "variant_template.py"))
            self.assertEqual(0, done.returncode, done.stdout + done.stderr)

    def test_variant_that_prints_logs_on_stdout_fails(self):
        done = self.run_cli("variant", "retrieval", "--", sys.executable, "-c", "print('loading…'); print('{}')")
        self.assertEqual(1, done.returncode)
        self.assertIn("Logs belong on stderr", done.stdout)

    def test_golden_example_passes_and_fails_sign_off(self):
        self.assertEqual(0, self.run_cli("golden", str(EXAMPLES / "golden.example.json")).returncode)
        self.assertEqual(1, self.run_cli("golden", str(EXAMPLES / "golden.example.json"), "--final").returncode)

    def test_duplicate_key_in_jsonl_fails(self):
        line = (EXAMPLES / "reviews.jsonl").read_text(encoding="utf-8").splitlines()[0]
        done = subprocess.run([sys.executable, str(EVAL / "validate.py"), "reviews", "-"], input=line + "\n" + line + "\n",
                              capture_output=True, text=True)
        self.assertEqual(1, done.returncode)
        self.assertIn("appears twice", done.stdout)


if __name__ == "__main__":
    unittest.main()
