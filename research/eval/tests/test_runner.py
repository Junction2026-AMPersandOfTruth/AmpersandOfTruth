"""Runner tests on synthetic data (#21). Run: python3 -m unittest discover -s research/eval/tests"""
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

EVAL = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(EVAL))
import contract  # noqa: E402
import runner  # noqa: E402
from runner import Run  # noqa: E402


def _topic(tid, split, kind, articles=(), items=(), styles=("en", "fi", "broad")):
    return {"id": tid, "split": split, "kind": kind,
            "queries": [{"id": f"{tid}.{s}", "style": s, "lang": "fi" if s == "fi" else "en", "text": f"{tid} {s}"} for s in styles],
            "articles": [{"page_id": p, "grade": g, "title": f"Artikkeli {p}"} for p, g in articles],
            "items": list(items)}


def _item(iid, label, page, inserting, removing, exclusion=None, other=None):
    it = {"id": iid, "label": label, "page_id": page, "inserting_rev_ids": inserting, "removing_rev_ids": removing,
          "description": f"what {iid} added", "why": "test", "human_verified": True}
    if exclusion:
        it["exclusion"] = exclusion
    if other:
        it["other_page_ids"] = other
    return it


def golden():
    """Two dev topics, a quiet and an out-of-corpus control, and a sealed test topic."""
    return {"contract": 1, "version": "test-1", "dataset": "synthetic", "topics": [
        _topic("alpha", "dev", "topic", [(101, 2), (102, 1)], [
            _item("alpha/c1", "contested", 101, [11, 12], [13]),
            _item("alpha/k1", "campaign", 101, [21, 23], [22], other=[102]),
            _item("alpha/r1", "reverted", 102, [31], [32]),
            _item("alpha/n1", "not-incident", 101, [41], [42], exclusion="graffiti"),
        ]),
        _topic("beta", "dev", "topic", [(201, 2)], [_item("beta/c1", "contested", 201, [51], [52])]),
        _topic("quiet", "dev", "control-quiet", [(301, 2)]),
        _topic("void", "dev", "control-out-of-corpus", styles=("en",)),
        _topic("gamma", "test", "topic", [(401, 2)], [_item("gamma/c1", "contested", 401, [61], [62])]),
    ]}


def inc(inserting, tier="reverted", page=101, removing=None, **extra):
    return {"page_id": page, "inserting_rev_ids": inserting, "removing_rev_ids": removing or [9000 + inserting[0]],
            "tier": tier, "score": 1.0, **extra}


def sig_runs(answers: dict, golden_doc=None, split="dev"):
    """Runs for a signals scoring: {topic id: [incidents] | None (failed)}."""
    doc = golden_doc or golden()
    runs = []
    for rid, tid, req in runner.plan("signals", runner.split_topics(doc, split)):
        incidents = answers.get(rid, [])
        resp = None if incidents is None else {"contract": 1, "incidents": incidents}
        runs.append(Run(rid, tid, req, resp, 0.5, None if resp else "exit code 1"))
    return runs


def score_signals(answers, **kw):
    doc = kw.pop("golden_doc", None) or golden()
    return runner.score("signals", doc, "dev", sig_runs(answers, doc), **kw)


def row(card, uid):
    return next(r for r in card["per_topic"] if r["id"] == uid)


class GoldenFixture(unittest.TestCase):
    def test_fixture_is_a_valid_golden_set(self):
        self.assertEqual([], contract.validate("golden", golden()).errors)


class Matching(unittest.TestCase):
    def index(self):
        return runner.GoldenIndex(runner.split_topics(golden(), "dev"))

    def test_highest_level_wins_then_overlap_then_smallest_id(self):
        doc = golden()
        doc["topics"][0]["items"].append(_item("alpha/c0", "contested", 101, [14, 15], [16]))
        idx = runner.GoldenIndex(runner.split_topics(doc, "dev"))
        o = runner.match([inc([11, 21])], idx)           # contested c1 and campaign k1 → campaign
        self.assertEqual(("campaign", "alpha/k1"), (o[0]["outcome"], o[0]["item_id"]))
        o = runner.match([inc([11, 12, 14])], idx)       # two contested: overlap 2 beats 1
        self.assertEqual("alpha/c1", o[0]["item_id"])
        o = runner.match([inc([11, 14])], idx)           # equal level and overlap: smallest id
        self.assertEqual("alpha/c0", o[0]["item_id"])

    def test_second_incident_takes_the_next_free_item_else_is_a_duplicate(self):
        o = runner.match([inc([11, 21]), inc([12, 23]), inc([12])], self.index())
        self.assertEqual(["campaign", "contested", "duplicate"], [x["outcome"] for x in o])
        self.assertEqual("alpha/c1", o[2]["item_id"])   # names the item it repeats

    def test_no_intersection_is_unlabelled_and_removing_revs_do_not_match(self):
        o = runner.match([inc([999]), inc([13])], self.index())
        self.assertEqual(["unlabelled", "unlabelled"], [x["outcome"] for x in o])

    def test_items_of_other_topics_in_the_split_are_candidates_but_not_the_test_split(self):
        card = score_signals({"alpha": [inc([51]), inc([61])]})
        self.assertEqual(["contested", "unlabelled"], [o["outcome"] for o in row(card, "alpha")["ranked"]])

    def test_match_state_is_per_topic(self):
        doc = golden()
        doc["topics"][1]["articles"].append({"page_id": 101, "grade": 1, "title": "shared"})
        card = score_signals({"alpha": [inc([11])], "beta": [inc([11])]}, golden_doc=doc)
        self.assertEqual("contested", row(card, "beta")["ranked"][0]["outcome"])
        self.assertEqual(0, card["metrics"]["duplicates"])


class SignalMetrics(unittest.TestCase):
    def test_precision_ignores_unlabelled_and_counts_duplicates_as_false_positives(self):
        card = score_signals({"alpha": [inc([11]), inc([999]), inc([12]), inc([31]), inc([41])]})
        r = row(card, "alpha")
        # labelled: contested, duplicate, reverted, not-incident → strict 1/4, lenient 2/4
        self.assertEqual((0.25, 0.5, 1), (r["strict_p@10"], r["lenient_p@10"], r["unlabelled@10"]))

    def test_precision_is_the_mean_over_topics_with_a_labelled_outcome(self):
        card = score_signals({"alpha": [inc([11])], "beta": [inc([999])], "quiet": [inc([41], page=301)]})
        self.assertIsNone(row(card, "beta")["strict_p@10"])
        self.assertEqual(1.0, card["metrics"]["strict_p@10"])        # beta has nothing labelled; quiet is a control
        self.assertEqual(0.5, card["metrics"]["unlabelled@10"])      # mean over the two topics

    def test_only_the_top_k_counts(self):
        card = score_signals({"alpha": [inc([1000 + i]) for i in range(10)] + [inc([11])]})
        r = row(card, "alpha")
        self.assertIsNone(r["strict_p@10"])
        self.assertEqual(1.0, r["strict_p@25"])

    def test_recall_is_pooled_over_expected_items_of_the_topic_articles(self):
        card = score_signals({"alpha": [inc([21])], "beta": []})
        # expected: alpha → c1, k1 (contested/campaign on 101/102); beta → c1
        self.assertEqual((2, ["alpha/k1"], ["alpha/c1"]), (row(card, "alpha")["expected"], row(card, "alpha")["found"], row(card, "alpha")["missed"]))
        self.assertAlmostEqual(1 / 3, card["metrics"]["recall"])

    def test_a_failed_request_scores_as_empty(self):
        card = score_signals({"alpha": None})
        self.assertEqual("exit code 1", card["failures"][0]["reason"])
        self.assertEqual(0, row(card, "alpha")["shown"])
        self.assertEqual(0.0, card["metrics"]["recall"])

    def test_level_accuracy_and_overclaims(self):
        card = score_signals({"alpha": [inc([21], tier="contested"),      # campaign shown lower: wrong, not overclaim
                                        inc([11], tier="contested"),      # right
                                        inc([31], tier="contested"),      # reverted shown higher: overclaim
                                        inc([41], tier="reverted")]})     # not-incident shown at all: wrong + overclaim
        m = card["metrics"]
        self.assertEqual((0.25, 0.5), (m["level_accuracy"], m["overclaim_rate"]))

    def test_quiet_controls_are_counted_separately(self):
        card = score_signals({"quiet": [inc([7001], page=301, tier="contested"), inc([7002], page=301)]})
        self.assertEqual((2, 1), (card["metrics"]["quiet_shown"], card["metrics"]["quiet_contested_or_above"]))
        self.assertIsNone(card["metrics"]["strict_p@10"])

    def test_out_of_corpus_controls_get_no_signals_request(self):
        self.assertNotIn("void", [rid for rid, _, _ in runner.plan("signals", runner.split_topics(golden(), "dev"))])

    def test_limit_cuts_the_list(self):
        card = runner.score("signals", golden(), "dev", sig_runs({"alpha": [inc([999]), inc([11])]}), limit=1)
        self.assertEqual(1, row(card, "alpha")["shown"])
        self.assertEqual(0.0, card["metrics"]["recall"])


class JudgedMetrics(unittest.TestCase):
    def test_judge_labels_unlabelled_hits_benign_reviews_and_unjudged_hits_are_misses(self):
        answers = {"alpha": [inc([11]), inc([801]), inc([802]), inc([803]), inc([41])]}
        judgements = {"801": {"key": "801", "label": "contested"}, "802": {"key": "802", "label": "campaign"},
                      "41": {"key": "41", "label": "contested"}}      # golden not-incident wins over the judge
        reviews = {"802": {"key": "802", "verdict": "benign", "reason": "routine", "reviewer": "t"},
                   "11": {"key": "11", "verdict": "unclear", "reason": "maybe a wording dispute", "reviewer": "t"}}
        card = score_signals(answers, judgements=judgements, reviews=reviews)
        r = row(card, "alpha")
        self.assertEqual(2 / 5, r["strict_p@10_judged"])                  # 11 (golden) + 801 (judge)
        self.assertEqual(1, r["unjudged@10"])                             # 803
        self.assertEqual(0.5, r["strict_p@10"])                           # unjudged metric unchanged: 11 vs 41
        self.assertEqual(["alpha/c1"], [c["item_id"] for c in card["challenges"]])

    def test_judged_keys_only_appear_with_judgements(self):
        self.assertNotIn("strict_p@10_judged", score_signals({"alpha": [inc([11])]})["metrics"])

    def test_unlabelled_lines_carry_the_contract_fields(self):
        card = score_signals({"alpha": [inc([11]), inc([902, 901], claim_en="x")]})
        self.assertEqual([{"key": "901+902", "topic_id": "alpha", "rank": 2, "page_id": 101, "inserting_rev_ids": [902, 901],
                           "removing_rev_ids": [9902], "tier": "reverted", "score": 1.0, "claim_en": "x"}], card["unlabelled"])


class RetrievalMetrics(unittest.TestCase):
    def runs(self, answers):
        doc = golden()
        out = []
        for rid, tid, req in runner.plan("retrieval", runner.split_topics(doc, "dev")):
            a = answers.get(rid, {"results": []})
            resp = {"contract": 1, "out_of_scope": a.get("oos", False), "results": [{"page_id": p} for p in a["results"]]}
            out.append(Run(rid, tid, req, resp, 0.1))
        return doc, out

    def test_recall_ndcg_and_out_of_scope(self):
        doc, runs = self.runs({
            "alpha.en": {"results": [101, 102]},                 # ideal
            "alpha.fi": {"results": [102, 999, 101]},            # both found, worse order
            "alpha.broad": {"results": [999] * 0 + [5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 101]},  # central one at rank 11
            "beta.en": {"results": [201], "oos": True},          # false alarm: scored as empty
            "void.en": {"results": [], "oos": True},             # detected
        })
        card = runner.score("retrieval", doc, "dev", runs)
        q = {r["query_id"]: r for r in card["per_topic"]}
        self.assertEqual((1.0, 1.0), (q["alpha.en"]["recall@5"], q["alpha.en"]["ndcg@10"]))
        ideal = 2 + 1 / 1.584962500721156
        self.assertAlmostEqual((1 + 2 / 2) / ideal, q["alpha.fi"]["ndcg@10"])
        self.assertEqual((0.0, 0.5), (q["alpha.broad"]["recall@10"], q["alpha.broad"]["recall@20"]))
        self.assertEqual(0.0, q["beta.en"]["recall@20"])
        m = card["metrics"]
        self.assertEqual(1.0, m["oos_detection_rate"])
        self.assertAlmostEqual(1 / 9, m["oos_false_alarm_rate"])          # 9 queries on alpha, beta, quiet
        self.assertEqual(3, card["by_style"]["en"]["queries"])            # out-of-corpus queries are not in the headline
        self.assertNotIn("sv", card["by_style"])
        self.assertEqual("alpha.en", card["best"][0]["query_id"])


SCRIPT = textwrap.dedent("""
    import json, os, sys, time
    req = json.load(sys.stdin)
    answers = json.load(open(os.environ["FAKE_ANSWERS"]))
    a = answers.get(req.get("topic_id") or req.get("query_id"), {"response": {"contract": 1, "incidents": [], "results": [], "out_of_scope": False}})
    print("thinking about", req.get("topic_id") or req.get("query_id"), file=sys.stderr)
    if "sleep" in a: time.sleep(a["sleep"])
    if "exit" in a: sys.exit(a["exit"])
    if "stdout" in a: print(a["stdout"]); sys.exit(0)
    json.dump(a["response"], sys.stdout)
""")


class Cli(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        (self.dir / "golden.json").write_text(json.dumps(golden()), encoding="utf-8")
        (self.dir / "fake.py").write_text(SCRIPT, encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def run_cli(self, answers, *args, env=None):
        (self.dir / "answers.json").write_text(json.dumps(answers), encoding="utf-8")
        e = {k: v for k, v in os.environ.items() if k != "EVAL_ALLOW_TEST"}
        e.update(FAKE_ANSWERS=str(self.dir / "answers.json"), **(env or {}))
        cmd = [str(EVAL / "run"), *args]
        if args and args[0] != "replay":
            cmd = [str(EVAL / "run"), *args, "--golden", str(self.dir / "golden.json"), "--out", str(self.dir / "out"),
                   "--", sys.executable, str(self.dir / "fake.py")]
        return subprocess.run(cmd, capture_output=True, text=True, env=e, timeout=60)

    def card(self, name, split="dev"):
        return json.loads((self.dir / "out" / f"{name}.{split}.scorecard.json").read_text(encoding="utf-8"))

    def ok(self, incidents):
        return {"response": {"contract": 1, "incidents": incidents}}

    def test_signals_run_writes_every_file_and_prints_the_scorecard(self):
        done = self.run_cli({"alpha": self.ok([inc([21], tier="campaign"), inc([777])])}, "signals", "--name", "s1-test")
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertIn("### Eval scorecard · `s1-test` · signals · dev split", done.stdout)
        for suffix in ("scorecard.json", "scorecard.md", "unlabelled.jsonl", "responses.jsonl", "log"):
            self.assertTrue((self.dir / "out" / f"s1-test.dev.{suffix}").exists(), suffix)
        card = self.card("s1-test")
        for f in ("contract", "kind", "variant", "split", "golden_version", "dataset", "git_commit", "ran_at", "command",
                  "metrics", "per_topic", "best", "worst", "unlabelled", "failures", "cost"):
            self.assertIn(f, card)
        self.assertEqual("test-1", card["golden_version"])
        self.assertEqual("alpha/k1", card["best"][0]["item_id"])
        self.assertIn("thinking about alpha", (self.dir / "out" / "s1-test.dev.log").read_text(encoding="utf-8"))
        lines = (self.dir / "out" / "s1-test.dev.unlabelled.jsonl").read_text(encoding="utf-8").splitlines()
        self.assertEqual("777", json.loads(lines[0])["key"])

    def test_failures_are_scored_as_empty_and_listed(self):
        bad = {"alpha": {"exit": 3}, "beta": {"stdout": "loading…"}, "quiet": {"response": {"contract": 1, "incidents": [{"page_id": 1}]}}}
        done = self.run_cli(bad, "signals", "--name", "broken", "--timeout", "30")
        self.assertEqual(1, done.returncode, done.stderr)          # exit 1 only when every request failed
        reasons = {f["id"]: f["reason"] for f in self.card("broken")["failures"]}
        self.assertEqual("exit code 3", reasons["alpha"])
        self.assertIn("logs belong on stderr", reasons["beta"])
        self.assertIn("does not match the contract", reasons["quiet"])
        self.assertIn("**3 failed**", done.stdout)

    def test_timeout_kills_the_variant(self):
        answers = {t: {"sleep": 30} for t in ("alpha", "beta", "quiet")}
        done = self.run_cli(answers, "signals", "--name", "slow", "--timeout", "0.5", "--jobs", "3")
        self.assertEqual(1, done.returncode)            # every request failed
        self.assertTrue(all("no answer within" in f["reason"] for f in self.card("slow")["failures"]))

    def test_test_split_is_sealed(self):
        done = self.run_cli({}, "signals", "--name", "peek", "--split", "test")
        self.assertEqual(2, done.returncode)
        self.assertIn("sealed", done.stderr)
        done = self.run_cli({}, "signals", "--name", "decide", "--split", "test", env={"EVAL_ALLOW_TEST": "1"})
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertEqual(["gamma"], [r["id"] for r in self.card("decide", "test")["per_topic"]])

    def test_replay_rescores_with_judgements_and_compares_with_a_baseline(self):
        self.run_cli({"alpha": self.ok([inc([11]), inc([801])])}, "signals", "--name", "base")
        judgements = self.dir / "j.jsonl"
        judgements.write_text(json.dumps({"key": "801", "label": "campaign", "confidence": 4, "evidence": [], "judge": "human:t"}) + "\n")
        out = self.dir / "out"
        done = self.run_cli({}, "replay", str(out / "base.dev.responses.jsonl"), "--name", "base-judged", "--golden",
                            str(self.dir / "golden.json"), "--judgements", str(judgements), "--baseline", str(out / "base.dev.scorecard.json"))
        self.assertEqual(0, done.returncode, done.stderr)
        card = self.card("base-judged")
        self.assertEqual(1.0, card["metrics"]["strict_p@10_judged"])
        self.assertEqual(self.card("base")["metrics"]["strict_p@10"], card["metrics"]["strict_p@10"])
        self.assertIn("Δ vs `base`", done.stdout)
        self.assertEqual(self.card("base")["ran_at"], card["ran_at"])

    def test_retrieval_and_pipeline_runs(self):
        answers = {"alpha.en": {"response": {"contract": 1, "out_of_scope": False, "results": [{"page_id": 101}],
                                             "incidents": [inc([11], tier="contested")]}},
                   "void.en": {"response": {"contract": 1, "out_of_scope": True, "results": [], "incidents": [inc([11])]}}}
        done = self.run_cli(answers, "retrieval", "--name", "r-test")
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertEqual(10, self.card("r-test")["requests"])
        done = self.run_cli(answers, "pipeline", "--name", "p-test")
        self.assertEqual(0, done.returncode, done.stderr)
        m = self.card("p-test")["metrics"]
        self.assertEqual((1.0, 0, 1.0), (m["oos_detection_rate"], m["oos_incidents_shown"], m["strict_p@10"]))
        self.assertAlmostEqual(0.5 / 9, m["recall@10"])     # one of two articles, on one of nine scored queries

    def test_usage_errors(self):
        done = subprocess.run([str(EVAL / "run"), "signals", "--name", "x", "--golden", str(self.dir / "missing.json"), "--", "true"],
                              capture_output=True, text=True)
        self.assertEqual(2, done.returncode)
        self.assertIn("P1.E1", done.stderr)
        done = subprocess.run([str(EVAL / "run"), "signals", "--name", "Bad Name", "--", "true"], capture_output=True, text=True)
        self.assertEqual(2, done.returncode)
        done = self.run_cli({}, "signals", "--name", "part", "--topics", "nope")
        self.assertEqual(2, done.returncode)
        self.assertIn("not in the dev split", done.stderr)


if __name__ == "__main__":
    unittest.main()
