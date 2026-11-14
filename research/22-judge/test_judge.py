#!/usr/bin/env python3
"""Offline unit tests for the P1.E3 judge harness (#22)."""
from __future__ import annotations

import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT / "research" / "eval"))

import calibrate  # noqa: E402
import contract  # noqa: E402
import evidence  # noqa: E402
import inputs  # noqa: E402
import tojsonl  # noqa: E402

NL = chr(10)


def _rev(
    rev_id: int,
    ts: str,
    user: str | None,
    *,
    page_id: int = 100,
    parent_id: int | None = None,
    bytes_val: int = 1000,
    comment: str = "",
) -> dict:
    return {
        "page_id": page_id,
        "rev_id": rev_id,
        "parent_id": parent_id,
        "title": "Testiartikkeli",
        "ts": ts,
        "dt": evidence.parse_ts(ts),
        "user": user,
        "anonymous": 0,
        "comment": comment,
        "bytes": bytes_val,
    }


class TestEvidenceBuilder(unittest.TestCase):
    def test_incident_key_sorts_and_deduplicates(self) -> None:
        self.assertEqual(evidence.incident_key([5000809, 5000801, 5000809]), "5000801+5000809")

    def test_editor_grouping_numbering_and_summary_scrubbing(self) -> None:
        st = evidence.CorpusStore(offline=True)
        st.titles[100] = "Viipurin taistelu (1944)"
        st.prev_bytes[99] = 1000
        st.revisions[101] = _rev(
            101,
            "2024-05-10 14:20:00",
            "85.76.12.34",
            parent_id=99,
            bytes_val=1412,
            comment="Lisätty lähde",
        )
        st.prev_bytes[101] = 1412
        st.revisions[102] = _rev(
            102,
            "2024-05-10 16:05:00",
            "Matti Meikäläinen",
            parent_id=101,
            bytes_val=1000,
            comment="Kumottu käyttäjän [[Toiminnot:Muokkaukset/85.76.12.34|85.76.12.34]] muokkaus",
        )
        st.prev_bytes[102] = 1000
        # Same /16 subnet as 85.76.12.34 -> must also be Editor 1 (anonymous)!
        st.revisions[103] = _rev(
            103,
            "2024-05-18 09:15:00",
            "85.76.220.99",
            parent_id=102,
            bytes_val=1412,
            comment="Palautettu Matti_Meikäläinen tekemä poisto (ks. Matti Meikäläinen)",
        )
        st.prev_bytes[103] = 1412
        st.revisions[104] = _rev(
            104,
            "2024-05-18 11:40:00",
            "2001:14bb:12:34::1",
            parent_id=103,
            bytes_val=1000,
            comment="Poistettu lähteetön väite (2001:14bb:99:88::2); palautettiin viimeisimpään käyttäjän UlkopuolinenTunnus tekemään versioon",
        )
        for rid in (101, 102, 103, 104):
            st.s3_edits[rid] = None
        st.page_s1_reverts[100] = [evidence.parse_ts("2024-05-10 16:05:00")]
        st.removal_reasons[102] = {"sourcing"}
        st.removal_reasons[104] = {"neutrality_pov", "sourcing"}

        # Pass unsorted ids to verify oldest-first ordering as well
        vars_dict, meta = evidence.build_with_meta(100, [103, 101], [104, 102], other_page_ids=[205], store=st)

        ins_lines = vars_dict["insertions"].splitlines()
        rem_lines = vars_dict["removals"].splitlines()
        self.assertEqual(len(ins_lines), 2)
        self.assertEqual(len(rem_lines), 2)

        # Both 85.76.12.34 and 85.76.220.99 share masked subnet -> Editor 1 (anonymous)
        self.assertIn("101 · 2024-05-10T14:20:00Z · Editor 1 (anonymous) · +412 B", ins_lines[0])
        self.assertIn("103 · 2024-05-18T09:15:00Z · Editor 1 (anonymous) · +412 B", ins_lines[1])
        # Removers numbered after inserters: Matti Meikäläinen -> Editor 2 (named), IPv6 -> Editor 3 (anonymous)
        self.assertIn("102 · 2024-05-10T16:05:00Z · Editor 2 (named) · -412 B", rem_lines[0])
        self.assertIn("104 · 2024-05-18T11:40:00Z · Editor 3 (anonymous) · -412 B", rem_lines[1])

        # Summaries have IPs masked and Matti Meikäläinen (both space and underscore) replaced with Editor 2
        self.assertIn("Editor 2", ins_lines[1])
        self.assertNotIn("Matti", vars_dict["insertions"])
        self.assertNotIn("85.76.12.34", vars_dict["removals"])
        self.assertIn("85.76.x.x", rem_lines[0])
        self.assertNotIn("UlkopuolinenTunnus", vars_dict["removals"])
        self.assertIn("käyttäjän [editor] tekemään versioon", rem_lines[1])

        # Privacy assertion passes cleanly
        inputs.assert_no_raw_identities(vars_dict, meta["involved_users"], context_id="test-1")

        # Check all 10 history lines and values:
        # visible_for = (16:05 - 14:20 = 1h45m) + (11:40 - 09:15 = 2h25m) = 4 hours, 10 minutes
        expected_history = [
            "visible_for: 4 hours, 10 minutes",
            "times_put_back_after_removal: 1",
            "distinct_inserting_editors: 1",
            "articles_with_same_content: 2",
            "identity_reverts_on_page_within_3_days: 1",
            "added_text_still_in_latest_revision: false",
            "inserter_bot_like_name: false",
            "same_account_removed_it: false",
            "dispute_templates_in_diff: false",
            "removal_reasons_from_summaries: neutrality_pov, sourcing",
        ]
        self.assertEqual(vars_dict["history"].splitlines(), expected_history)

    def test_history_days_kept_bot_self_removal_and_dispute_template(self) -> None:
        st = evidence.CorpusStore(offline=True)
        st.titles[50] = "Suomettuminen"
        st.prev_bytes[1] = 500
        st.revisions[10] = _rev(10, "2024-01-01 00:00:00", "AutoBot", page_id=50, parent_id=1, bytes_val=700)
        st.prev_bytes[10] = 700
        st.revisions[11] = _rev(11, "2024-01-03 05:30:00", "AutoBot", page_id=50, parent_id=10, bytes_val=500)
        st.s3_edits[10] = {
            "rev_id": 10,
            "page_id": 50,
            "old_rev_id": 1,
            "new_rev_id": 10,
            "error": None,
            "segments": [
                {
                    "op": "added",
                    "kind": "prose",
                    "text": "Tämä pitkä lisätty lause säilyi [[Suomi|Suomen]] artikkelin lopullisessa versiossa.",
                    "prose": "Tämä pitkä lisätty lause säilyi Suomen artikkelin lopullisessa versiossa.",
                    "old_paragraph": None,
                    "new_paragraph": 2,
                    "moved": False,
                }
            ],
        }
        st.s3_edits[11] = {
            "rev_id": 11,
            "page_id": 50,
            "old_rev_id": 10,
            "new_rev_id": 11,
            "error": None,
            "segments": [
                {
                    "op": "added",
                    "kind": "template",
                    "text": "{{Neutraalius|keskustelu}}",
                    "prose": "",
                    "old_paragraph": None,
                    "new_paragraph": 0,
                    "moved": False,
                }
            ],
        }
        raw_latest = "Johdanto. Tämä pitkä lisätty lause säilyi [[Suomi|Suomen]] artikkelin lopullisessa versiossa."
        st.latest_texts[50] = (raw_latest, evidence.strip_wikitext_inline(raw_latest))

        out = evidence.build(50, [10], [11], store=st)
        hist = dict(line.split(": ", 1) for line in out["history"].splitlines())
        self.assertEqual(hist["visible_for"], "2 days, 5 hours")
        self.assertEqual(hist["times_put_back_after_removal"], "0")
        self.assertEqual(hist["added_text_still_in_latest_revision"], "true")
        self.assertEqual(hist["inserter_bot_like_name"], "true")
        self.assertEqual(hist["same_account_removed_it"], "true")
        self.assertEqual(hist["dispute_templates_in_diff"], "true")

    def test_privacy_check_fails_loudly_on_raw_name_or_ip(self) -> None:
        clean = {
            "article_title": "Testi",
            "insertions": '1 · 2024-01-01T00:00:00Z · Editor 1 (named) · +10 B · summary: "ok"',
            "diff": "+ teksti",
            "removals": '2 · 2024-01-01T01:00:00Z · Editor 2 (anonymous) · -10 B · summary: "85.76.x.x"',
            "history": "visible_for: 1 hours, 0 minutes",
        }
        inputs.assert_no_raw_identities(clean, ["SalainenNimimerkki", "85.76.12.34", "2001:14bb::1"])

        bad_name = {**clean, "removals": clean["removals"] + " SalainenNimimerkki"}
        with self.assertRaises(ValueError):
            inputs.assert_no_raw_identities(bad_name, ["SalainenNimimerkki"], context_id="item-a")

        bad_ip4 = {**clean, "insertions": clean["insertions"] + " 85.76.12.34"}
        with self.assertRaises(ValueError):
            inputs.assert_no_raw_identities(bad_ip4, ["85.76.12.34"], context_id="item-b")

        bad_ip6 = {**clean, "history": clean["history"] + " 2001:14BB:0000:0000:0000:0000:0000:0001"}
        with self.assertRaises(ValueError):
            inputs.assert_no_raw_identities(bad_ip6, ["2001:14bb::1"], context_id="item-c")

        with self.assertRaises(ValueError):
            inputs.assert_blind_record({"_id": "x", "label": "contested"}, "item-d")

    def test_diff_rendering_blocks_moves_missing_and_caps(self) -> None:
        s3 = {
            1: {
                "segments": [
                    # moved block must be skipped
                    {"op": "added", "kind": "prose", "text": "Siirretty kappale", "old_paragraph": 4, "new_paragraph": 0, "moved": True},
                    # replaced in block (0, 1) -> ~ [-vanha-] {+uusi+}
                    {"op": "removed", "kind": "prose", "text": "vanha luku 100", "old_paragraph": 0, "new_paragraph": 1, "moved": False},
                    {"op": "added", "kind": "prose", "text": "uusi luku 9000", "old_paragraph": 0, "new_paragraph": 1, "moved": False},
                    # pure addition in new block 2
                    {"op": "added", "kind": "prose", "text": "Kokonaan uusi virke.", "old_paragraph": None, "new_paragraph": 2, "moved": False},
                    # pure removal in old block 3 with >600 chars -> capped at 600
                    {"op": "removed", "kind": "prose", "text": "X" * 700, "old_paragraph": 3, "new_paragraph": None, "moved": False},
                ]
            },
            2: None,  # missing row
        }
        diff_text, missing = evidence.render_diff([1, 2], s3)
        self.assertEqual(missing, 1)
        lines = diff_text.splitlines()
        self.assertEqual(lines[0], "@@ rev 1")
        self.assertEqual(lines[1], "~ [-vanha luku 100-] {+uusi luku 9000+}")
        self.assertEqual(lines[2], "+ Kokonaan uusi virke.")
        self.assertTrue(lines[3].startswith("- XXX"))
        self.assertEqual(len(lines[3]), 600)
        self.assertTrue(lines[3].endswith("…"))
        self.assertEqual(lines[4], "@@ rev 2")
        self.assertEqual(lines[5], "(diff not available for rev 2)")
        self.assertNotIn("Siirretty kappale", diff_text)

        # Total diff cap at 6000 chars ending with '… (truncated)'
        huge_s3 = {
            10: {
                "segments": [
                    {"op": "added", "kind": "prose", "text": f"Kappale {i}: " + (" sana" * 90), "old_paragraph": None, "new_paragraph": i, "moved": False}
                    for i in range(20)
                ]
            }
        }
        huge_diff, _ = evidence.render_diff([10], huge_s3)
        self.assertEqual(len(huge_diff), 6000)
        self.assertTrue(huge_diff.endswith("… (truncated)"))


class TestCalibrateAndToJsonl(unittest.TestCase):
    def test_calibrate_metrics_and_markdown(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tdir = pathlib.Path(tmp)
            golden = {
                "contract": 1,
                "version": "1.0-test",
                "dataset": "M",
                "topics": [
                    {
                        "id": "t1",
                        "split": "dev",
                        "kind": "topic",
                        "queries": [{"id": "t1.en", "style": "en", "text": "test"}],
                        "articles": [{"page_id": 10, "grade": 2}],
                        "items": [
                            {
                                "id": "t1/i01",
                                "page_id": 10,
                                "inserting_rev_ids": [1001],
                                "removing_rev_ids": [1002],
                                "label": "campaign",
                                "category": "pov_push",
                                "human_verified": True,
                            },
                            {
                                "id": "t1/i02",
                                "page_id": 10,
                                "inserting_rev_ids": [1003],
                                "removing_rev_ids": [1004],
                                "label": "contested",
                                "category": "false_factual_claim",
                                "human_verified": True,
                            },
                            {
                                "id": "t1/i03",
                                "page_id": 10,
                                "inserting_rev_ids": [1005],
                                "removing_rev_ids": [1006],
                                "label": "reverted",
                                "category": "good_faith_error",
                                "human_verified": False,
                            },
                            {
                                "id": "t1/n01",
                                "page_id": 10,
                                "inserting_rev_ids": [1007],
                                "removing_rev_ids": [1008],
                                "label": "not-incident",
                                "exclusion": "graffiti",
                                "category": "vandalism",
                                "human_verified": False,
                            },
                        ],
                    }
                ],
            }
            gpath = tdir / "golden.json"
            gpath.write_text(json.dumps(golden), encoding="utf-8")

            in_lines = [
                {"_id": "t1/i01", "key": "1001", "article_title": "A", "insertions": "", "diff": "+ tarkka lainaus 1", "removals": "", "history": ""},
                {"_id": "t1/i02", "key": "1003", "article_title": "A", "insertions": "", "diff": "+ tarkka lainaus 2", "removals": "", "history": ""},
                {"_id": "t1/i03", "key": "1005", "article_title": "A", "insertions": "", "diff": "+ tarkka lainaus 3", "removals": "", "history": ""},
                {"_id": "t1/n01", "key": "1007", "article_title": "A", "insertions": "", "diff": "+ LOL VANDALISM", "removals": "", "history": ""},
            ]
            ipath = tdir / "inputs.jsonl"
            ipath.write_text(NL.join(json.dumps(x) for x in in_lines) + NL, encoding="utf-8")

            # 3 exact matches (campaign, contested, not-incident), 1 overclaim (reverted -> contested)
            batch_lines = [
                {
                    "id": "t1/i01",
                    "prompt": "judge-incident",
                    "prompt_version": "1.abc",
                    "model": "gemini-test",
                    "outcome": "Ok",
                    "value": {
                        "label": "campaign",
                        "exclusion": None,
                        "category": "pov_push",
                        "confidence": 5,
                        "evidence": ["tarkka lainaus 1"],
                        "reason_en": "Level 3 met.",
                    },
                    "latency_ms": 1000,
                    "input_tokens": 500,
                    "output_tokens": 80,
                    "cost_usd": 0.002,
                },
                {
                    "id": "t1/i02",
                    "prompt": "judge-incident",
                    "prompt_version": "1.abc",
                    "model": "gemini-test",
                    "outcome": "Ok",
                    "value": {
                        "label": "contested",
                        "exclusion": None,
                        "category": "false_factual_claim",
                        "confidence": 4,
                        "evidence": ["tarkka lainaus 2"],
                        "reason_en": "Level 2 met.",
                    },
                    "latency_ms": 1200,
                    "input_tokens": 600,
                    "output_tokens": 90,
                    "cost_usd": 0.003,
                },
                {
                    "id": "t1/i03",
                    "prompt": "judge-incident",
                    "prompt_version": "1.abc",
                    "model": "gemini-test",
                    "outcome": "Ok",
                    "value": {
                        "label": "contested",
                        "exclusion": None,
                        "category": "pov_push",
                        "confidence": 2,
                        "evidence": ["keksitty lainaus"],
                        "reason_en": "Misread as disputed.",
                    },
                    "latency_ms": 1400,
                    "input_tokens": 700,
                    "output_tokens": 100,
                    "cost_usd": 0.004,
                },
                {
                    "id": "t1/n01",
                    "prompt": "judge-incident",
                    "prompt_version": "1.abc",
                    "model": "gemini-test",
                    "outcome": "Ok",
                    "value": {
                        "label": "not-incident",
                        "exclusion": "graffiti",
                        "category": "vandalism",
                        "confidence": 5,
                        "evidence": ["LOL VANDALISM"],
                        "reason_en": "Obvious graffiti.",
                    },
                    "latency_ms": 800,
                    "input_tokens": 400,
                    "output_tokens": 50,
                    "cost_usd": 0.001,
                },
            ]
            bpath = tdir / "batch.jsonl"
            bpath.write_text(NL.join(json.dumps(x) for x in batch_lines) + NL, encoding="utf-8")

            reports, md = calibrate.calibrate_files([bpath], split="dev", golden_path=gpath, inputs_path=ipath)
            self.assertEqual(len(reports), 1)
            rep = reports[0]
            self.assertAlmostEqual(rep["exact_agreement"]["rate"], 0.75)
            self.assertAlmostEqual(rep["binary_agreement"]["rate"], 0.75)
            self.assertAlmostEqual(rep["overclaim"]["rate"], 0.25)
            self.assertAlmostEqual(rep["underclaim"]["rate"], 0.0)
            self.assertAlmostEqual(rep["exclusion_agreement"]["both_not_incident"]["rate"], 1.0)
            self.assertAlmostEqual(rep["category_agreement"]["rate"], 0.75)
            self.assertAlmostEqual(rep["mean_confidence"]["right"], (5 + 4 + 5) / 3)
            self.assertAlmostEqual(rep["mean_confidence"]["wrong"], 2.0)
            self.assertAlmostEqual(rep["evidence_verbatim"]["rate"], 0.75)
            self.assertEqual(rep["disagreements_total"], 1)
            self.assertEqual(rep["disagreements"][0]["id"], "t1/i03")
            self.assertIn("4×4 Confusion Matrix", md)
            self.assertIn("t1/i03", md)

    def test_tojsonl_judgements_and_reviews_pass_contract_validator(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tdir = pathlib.Path(tmp)
            judge_batch = [
                {
                    "id": "5000777",
                    "prompt": "judge-incident",
                    "prompt_version": "1.deadbeef",
                    "model": "gemini-4-argon",
                    "outcome": "Ok",
                    "value": {
                        "label": "not-incident",
                        "exclusion": "graffiti",
                        "category": "vandalism",
                        "confidence": 5,
                        "evidence": ["PERTTI ON TYHMÄ"],
                        "reason_en": "Step 1 graffiti exclusion.",
                    },
                },
                {
                    "id": "5000801+5000809",
                    "prompt": "judge-incident",
                    "prompt_version": "1.deadbeef",
                    "model": "gemini-4-argon",
                    "outcome": "Ok",
                    "value": {
                        "label": "contested",
                        "exclusion": None,
                        "category": "pov_push",
                        "confidence": 4,
                        "evidence": ["luovutettiin vapaaehtoisesti"],
                        "reason_en": "Removed with neutrality summary and put back once.",
                    },
                },
                {
                    "id": "5000999",
                    "prompt": "judge-incident",
                    "prompt_version": "1.deadbeef",
                    "model": "gemini-4-argon",
                    "outcome": "Blocked",
                    "value": None,
                },
            ]
            jb_path = tdir / "judge.batch.jsonl"
            j_out = tdir / "judgements.jsonl"
            jb_path.write_text(NL.join(json.dumps(x, ensure_ascii=False) for x in judge_batch) + NL, encoding="utf-8")

            written, skipped = tojsonl.convert_batch("judgements", jb_path, j_out)
            self.assertEqual(written, 2)
            self.assertEqual(skipped, 1)

            j_lines = [json.loads(line) for line in j_out.read_text(encoding="utf-8").splitlines() if line.strip()]
            self.assertEqual(j_lines[0]["judge"], "judge-incident@1.deadbeef/gemini-4-argon")
            for jl in j_lines:
                rep = contract.validate("judgements", jl)
                self.assertTrue(rep.ok, rep.errors)

            proc_j = subprocess.run(
                [sys.executable, str(ROOT / "research" / "eval" / "validate.py"), "judgements", str(j_out)],
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc_j.returncode, 0, proc_j.stdout + proc_j.stderr)

            review_batch = [
                {
                    "id": "5000801+5000809",
                    "prompt": "adversarial-review",
                    "prompt_version": "1.cafebabe",
                    "model": "gemini-4-carbon",
                    "outcome": "Ok",
                    "value": {
                        "verdict": "survives",
                        "benign_explanation": "none",
                        "reason": "Re-added after an explicit neutrality dispute.",
                        "would_change_mind": "Talk-page consensus agreeing with the wording.",
                        "quoted": ["luovutettiin vapaaehtoisesti"],
                    },
                }
            ]
            rb_path = tdir / "review.batch.jsonl"
            r_out = tdir / "reviews.jsonl"
            rb_path.write_text(NL.join(json.dumps(x, ensure_ascii=False) for x in review_batch) + NL, encoding="utf-8")

            r_written, r_skipped = tojsonl.convert_batch("reviews", rb_path, r_out)
            self.assertEqual(r_written, 1)
            self.assertEqual(r_skipped, 0)

            r_lines = [json.loads(line) for line in r_out.read_text(encoding="utf-8").splitlines() if line.strip()]
            self.assertEqual(r_lines[0]["reviewer"], "adversarial-review@1.cafebabe/gemini-4-carbon")
            for rl in r_lines:
                rep = contract.validate("reviews", rl)
                self.assertTrue(rep.ok, rep.errors)

            proc_r = subprocess.run(
                [sys.executable, str(ROOT / "research" / "eval" / "validate.py"), "reviews", str(r_out)],
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc_r.returncode, 0, proc_r.stdout + proc_r.stderr)


if __name__ == "__main__":
    unittest.main()
