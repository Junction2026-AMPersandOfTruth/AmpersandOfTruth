#!/usr/bin/env python3
"""Offline unit tests for P1.S9b (`decisions.py`, `run.py`, `score.py`)."""
from __future__ import annotations

import copy
import io
import json
import pathlib
import sys
import tempfile
import unittest
import urllib.error

HERE = pathlib.Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import decisions  # noqa: E402
import run as s9b_run  # noqa: E402
import score as s9b_score  # noqa: E402


SAMPLE_ROW = {
    "_id": "winter-war/i01",
    "article_title": "Talvisota",
    "edit_timestamp": "2024-03-01T12:00:00Z",
    "editor_kind": "anonymous",
    "exposure": "14 min",
    "edit_summary": "Korjattu luku",
    "revert_summary": "Kumottu lähteetön muutos",
    "removed_text": "noin 25 000 kaatunutta",
    "inserted_text": "noin 250 000 kaatunutta",
    "context": "…" + ("Suomen tappiot talvisodassa olivat raskaat. " * 30) + "…",
    "facts": "Insertions: 1 (re-insertions: 0) by 1 distinct editor(s).",
}


class BodyBuildingTests(unittest.TestCase):
    def test_variant_a_and_c_request_bodies_valid(self) -> None:
        body_a = decisions.build_request_body(SAMPLE_ROW, include_facts=False)
        body_c = decisions.build_request_body(SAMPLE_ROW, include_facts=True)

        self.assertEqual(body_a["model"], "gemini-decision-1")
        self.assertGreaterEqual(len(body_a["questions"]), 1)
        self.assertLessEqual(len(body_a["questions"]), 1000)

        for key in body_a["questions"]:
            self.assertRegex(key, r"^[A-Za-z0-9_-]{1,64}$")

        expected_keys = {
            "nonsense",
            "factual_claim",
            "changes_number_date_name",
            "political_or_national_pov",
            "contradicts_replaced_text",
            "alters_citation",
            "removal_disputes_accuracy",
            "category",
            "level",
        }
        self.assertEqual(set(body_a["questions"].keys()), expected_keys)
        self.assertEqual(
            set(body_a["questions"]["category"]["criteria"].keys()),
            {
                "vandalism",
                "test_or_nonsense",
                "good_faith_error",
                "content_dispute",
                "pov_push",
                "false_factual_claim",
                "source_manipulation",
                "other",
                "none_of_these",
            },
        )
        self.assertEqual(len(body_a["questions"]["level"]["criteria"]), 4)

        self.assertNotIn("STRUCTURAL FACTS", body_a["input"])
        self.assertIn("STRUCTURAL FACTS", body_c["input"])
        self.assertIn("Talvisota", body_a["input"])
        self.assertIn("noin 250 000 kaatunutta", body_a["input"])

    def test_context_clipped_to_at_most_800_chars(self) -> None:
        centered = "…" + ("A " * 700) + "…"
        start_anchored = ("B " * 700) + "…"
        end_anchored = "…" + ("C " * 700)

        for raw in (centered, start_anchored, end_anchored):
            clipped = decisions.clip_context(raw, max_chars=800)
            self.assertLessEqual(len(clipped), 800)
            self.assertGreater(len(clipped), 700)

    def test_validate_request_body_rejects_bad_keys_and_over_1000_questions(self) -> None:
        bad_key_body = decisions.build_request_body(SAMPLE_ROW)
        bad_key_body["questions"]["bad key with spaces"] = {
            "type": "noul",
            "instructions": "Is this valid?",
        }
        with self.assertRaises(ValueError):
            decisions.validate_request_body(bad_key_body)

        too_long_key = "q" * 65
        bad_len_body = decisions.build_request_body(SAMPLE_ROW)
        bad_len_body["questions"][too_long_key] = {"type": "noul", "instructions": "Valid?"}
        with self.assertRaises(ValueError):
            decisions.validate_request_body(bad_len_body)

        too_many_body = {
            "model": "gemini-decision-1",
            "input": "hello",
            "questions": {
                f"q_{i}": {"type": "noul", "instructions": "Yes?"} for i in range(1001)
            },
        }
        with self.assertRaises(ValueError):
            decisions.validate_request_body(too_many_body)


class ResponseParsingAndClientTests(unittest.TestCase):
    def test_parse_ok_and_400_safety_vs_invalid(self) -> None:
        ok_payload = json.dumps(
            {
                "answers": {
                    "nonsense": {"type": "noul", "noul": 0.02, "confidence": 0.98},
                    "category": {
                        "type": "choice",
                        "choice": "false_factual_claim",
                        "confidence": 0.62,
                        "probabilities": {"false_factual_claim": 0.62, "pov_push": 0.38},
                    },
                    "level": {
                        "type": "score",
                        "score": 1.85,
                        "confidence": 0.70,
                        "probabilities": {"0": 0.05, "1": 0.20, "2": 0.60, "3": 0.15},
                    },
                },
                "usage": {"total_input_tokens": 412, "total_output_tokens": 0, "total_tokens": 412},
            }
        ).encode("utf-8")
        parsed_ok = decisions.parse_success_payload(ok_payload, latency_ms=1950)
        self.assertEqual(parsed_ok["outcome"], "ok")
        self.assertEqual(parsed_ok["usage"]["total_input_tokens"], 412)
        self.assertEqual(parsed_ok["latency_ms"], 1950)

        safety_400 = json.dumps(
            {"error": {"message": "Input blocked by safety filter.", "code": "invalid_request"}}
        ).encode("utf-8")
        parsed_blocked = decisions.parse_error_payload(400, safety_400, latency_ms=320)
        self.assertEqual(parsed_blocked["outcome"], "blocked")
        self.assertIsNone(parsed_blocked["answers"])

        invalid_400 = json.dumps(
            {"error": {"message": "Malformed criteria field.", "code": "invalid_request"}}
        ).encode("utf-8")
        parsed_invalid = decisions.parse_error_payload(400, invalid_400, latency_ms=210)
        self.assertEqual(parsed_invalid["outcome"], "invalid")

    def test_client_retries_429_caches_result_and_never_writes_key(self) -> None:
        fake_secret = "FAKE_SECRET_DO_NOT_LEAK_999"
        calls = 0

        def fake_transport(req, timeout_s):
            nonlocal calls
            calls += 1
            if calls == 1:
                raise urllib.error.HTTPError(
                    req.full_url,
                    429,
                    "Too Many Requests",
                    hdrs={"Retry-After": "0.01"},  # type: ignore[arg-type]
                    fp=io.BytesIO(b'{"error": {"message": "rate limit"}}'),
                )
            resp = {
                "answers": {"nonsense": {"type": "noul", "noul": 0.01, "confidence": 0.99}},
                "usage": {"total_input_tokens": 250, "total_output_tokens": 0, "total_tokens": 250},
            }
            return 200, json.dumps(resp).encode("utf-8"), {}

        with tempfile.TemporaryDirectory() as tmp:
            cache_dir = pathlib.Path(tmp) / ".cache"
            sleeps: list[float] = []
            client = decisions.DecisionsClient(
                api_key=fake_secret,
                cache_dir=cache_dir,
                transport=fake_transport,
                sleep_fn=sleeps.append,
            )
            body = decisions.build_request_body(SAMPLE_ROW)
            res1 = client.decide(body)
            self.assertEqual(res1["outcome"], "ok")
            self.assertEqual(calls, 2)
            self.assertEqual(len(sleeps), 1)

            # Second call should come from disk cache without invoking transport.
            res2 = client.decide(body)
            self.assertEqual(res2["outcome"], "ok")
            self.assertEqual(calls, 2)

            for cf in cache_dir.glob("*.json"):
                self.assertNotIn(fake_secret, cf.read_text(encoding="utf-8"))

    def test_dry_run_writes_requests_and_probe_without_key(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = pathlib.Path(tmp)
            in_file = tmp_path / "in.jsonl"
            out_dir = tmp_path / "results"
            in_file.write_text(json.dumps(SAMPLE_ROW) + "\n", encoding="utf-8")

            out_path = s9b_run.run_variant(
                variant="A",
                in_path=in_file,
                out_dir=out_dir,
                dry_run=True,
                write_probe_file=True,
            )
            self.assertTrue(out_path.exists())
            self.assertEqual(out_path.name, "requests.A.jsonl")
            probe_path = out_dir / "probe.json"
            self.assertTrue(probe_path.exists())
            probe_doc = json.loads(probe_path.read_text(encoding="utf-8"))
            self.assertEqual(probe_doc["_id"], "winter-war/i01")
            self.assertEqual(probe_doc["mode"], "dry-run")


class ScoringMetricsTests(unittest.TestCase):
    def test_graffiti_threshold_search_keeps_all_contested_and_campaign(self) -> None:
        synthetic_items = [
            {"id": "cc1", "label": "contested", "exclusion": None, "nonsense": 0.02},
            {"id": "cc2", "label": "campaign", "exclusion": None, "nonsense": 0.18},
            {"id": "gr1", "label": "not-incident", "exclusion": "graffiti", "nonsense": 0.12},  # below cc2
            {"id": "gr2", "label": "not-incident", "exclusion": "graffiti", "nonsense": 0.65},
            {"id": "gr3", "label": "not-incident", "exclusion": "graffiti", "nonsense": 0.96},
            {"id": "ni1", "label": "not-incident", "exclusion": "blanking", "nonsense": 0.40},
            {"id": "rv1", "label": "reverted", "exclusion": None, "nonsense": 0.30},
        ]
        res = s9b_score.find_graffiti_threshold(synthetic_items)
        self.assertAlmostEqual(res["max_contested_campaign_nonsense"], 0.18)
        self.assertEqual(res["max_contested_campaign_id"], "cc2")
        # Highest threshold keeping max graffiti recall (gr2=0.65, gr3=0.96) is 0.65:
        self.assertAlmostEqual(res["threshold"], 0.65)
        self.assertEqual(res["contested_campaign_removed"], 0)
        self.assertEqual(res["graffiti_removed"], 2)
        self.assertAlmostEqual(res["graffiti_removed_share"], 2 / 3)
        self.assertEqual(res["not_incident_removed"], 2)
        self.assertEqual(res["reverted_removed"], 0)

        # Aggressive min-safe threshold (> 0.18 -> 0.30) also keeps 0 contested/campaign lost:
        agg = res["aggressive_min_safe"]
        self.assertAlmostEqual(agg["threshold"], 0.30)
        self.assertEqual(agg["contested_campaign_removed"], 0)
        self.assertEqual(agg["graffiti_removed"], 2)
        self.assertEqual(agg["not_incident_removed"], 3)

    def test_auc_brier_and_reliability_on_synthetic_probabilities(self) -> None:
        # Perfect separation -> AUC = 1.0
        self.assertAlmostEqual(s9b_score.compute_auc([0, 0, 1, 1], [0.1, 0.2, 0.8, 0.9]), 1.0)
        # Inverted -> AUC = 0.0
        self.assertAlmostEqual(s9b_score.compute_auc([0, 0, 1, 1], [0.8, 0.9, 0.1, 0.2]), 0.0)
        # Exact tie -> AUC = 0.5
        self.assertAlmostEqual(s9b_score.compute_auc([0, 1], [0.5, 0.5]), 0.5)
        # Partial tie: neg=[0.2, 0.5], pos=[0.5, 0.9] -> pairs: (0.5>0.2)=1, (0.5==0.5)=0.5, (0.9>0.2)=1, (0.9>0.5)=1 => 3.5/4 = 0.875
        self.assertAlmostEqual(s9b_score.compute_auc([0, 0, 1, 1], [0.2, 0.5, 0.5, 0.9]), 0.875)

        # Brier: ((0.1-0)^2 + (0.8-1)^2) / 2 = (0.01 + 0.04) / 2 = 0.025
        self.assertAlmostEqual(s9b_score.compute_brier([0, 1], [0.1, 0.8]), 0.025)

        rel = s9b_score.compute_reliability_buckets(
            [0, 0, 1, 1, 1],
            [0.0, 0.15, 0.50, 0.85, 1.0],
            n_buckets=5,
        )
        buckets = rel["buckets"]
        self.assertEqual(len(buckets), 5)
        self.assertEqual(buckets[0]["count"], 2)  # 0.0 and 0.15
        self.assertEqual(buckets[2]["count"], 1)  # 0.50
        self.assertEqual(buckets[4]["count"], 2)  # 0.85 and 1.0
        self.assertAlmostEqual(buckets[4]["empirical_rate"], 1.0)

    def test_end_to_end_variant_scoring_and_judge_agreement(self) -> None:
        golden = {
            "golden_version": "test-1",
            "dataset": "synthetic",
            "ordered_ids": ["i1", "i2"],
            "items_by_id": {
                "i1": {
                    "id": "i1",
                    "topic_id": "t1",
                    "page_id": 1,
                    "label": "not-incident",
                    "level": 0,
                    "exclusion": "graffiti",
                    "category": "vandalism",
                },
                "i2": {
                    "id": "i2",
                    "topic_id": "t1",
                    "page_id": 1,
                    "label": "contested",
                    "level": 2,
                    "exclusion": None,
                    "category": "pov_push",
                },
            },
        }
        rows = [
            {
                "_id": "i1",
                "outcome": "ok",
                "answers": {
                    "nonsense": {"type": "noul", "noul": 0.97, "confidence": 0.97},
                    "category": {"type": "choice", "choice": "vandalism", "confidence": 0.91},
                    "level": {
                        "type": "score",
                        "score": 0.05,
                        "probabilities": {"0": 0.95, "1": 0.05, "2": 0.0, "3": 0.0},
                    },
                },
                "usage": {"total_input_tokens": 500},
                "latency_ms": 1800,
            },
            {
                "_id": "i2",
                "outcome": "ok",
                "answers": {
                    "nonsense": {"type": "noul", "noul": 0.02, "confidence": 0.98},
                    "category": {"type": "choice", "choice": "pov_push", "confidence": 0.74},
                    "level": {
                        "type": "score",
                        "score": 2.0,
                        "probabilities": {"0": 0.02, "1": 0.18, "2": 0.70, "3": 0.10},
                    },
                },
                "usage": {"total_input_tokens": 500},
                "latency_ms": 2200,
            },
        ]
        scored = s9b_score.score_decisions_variant("A", copy.deepcopy(rows), golden)
        self.assertAlmostEqual(scored["category_accuracy"]["accuracy_all"], 1.0)
        self.assertAlmostEqual(scored["judge_agreement"]["exact_agreement_ok"], 1.0)
        self.assertAlmostEqual(scored["efficiency"]["mean_cost_usd_per_item"], 0.00005)


if __name__ == "__main__":
    unittest.main()
