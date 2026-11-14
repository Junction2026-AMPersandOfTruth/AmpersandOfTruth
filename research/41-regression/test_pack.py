#!/usr/bin/env python3
"""Offline unit tests for research/41-regression/pack.py (P1.P3 #41)."""
from __future__ import annotations

import contextlib
import copy
import io
import json
import pathlib
import re
import sys
import tempfile
import unicodedata
import unittest
from typing import Any
from unittest import mock

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pack  # noqa: E402


def _make_article(
    page_id: int = 1256,
    title: str = "Talvisota",
    distance: float = 0.25,
    lang: str = "fi",
    wikidata_id: str | None = "Q135572",
    via_lang: str | None = None,
) -> dict[str, Any]:
    return {
        "lang": lang,
        "page_id": page_id,
        "title": title,
        "distance": distance,
        "wikidata_id": wikidata_id,
        "via_lang": via_lang,
        "url": f"https://{lang}.wikipedia.org/?curid={page_id}",
        "has_history": lang == "fi",
        "title_match": True,
    }


def _make_candidate(page_id: int, revert_rev_id: int, reverted_rev_id: int, restored_rev_id: int = 100) -> dict[str, Any]:
    return {
        "key": f"{page_id}-{revert_rev_id}",
        "article": _make_article(page_id=page_id),
        "revert": {
            "page_id": page_id,
            "rev_id": revert_rev_id,
            "timestamp": "2020-05-01T12:10:00Z",
            "user": "Editor 2",
            "comment": "Kumottu muokkaus",
            "restored_rev_id": restored_rev_id,
            "reverted_count": 1,
            "reverted_rev_id": reverted_rev_id,
            "reverted_user": "Editor 1",
        },
    }


def _make_incident(
    *,
    page_id: int = 1256,
    inserting_rev_ids: list[int] | None = None,
    removing_rev_ids: list[int] | None = None,
    tier: str = "contested",
    score: float = 212.4,
    claim_en: str | None = "Disputed casualty figure added without source",
    claim_fi: str | None = "Kiistanalainen tappioluku ilman lähdettä",
    narrative: str | None = "Inflates wartime losses",
    assessed: bool = True,
    quotes: list[str] | None = None,
    category: str = "pov_push",
    confidence: int = 4,
) -> dict[str, Any]:
    ins = inserting_rev_ids if inserting_rev_ids is not None else [8960194, 8960158]
    rem = removing_rev_ids if removing_rev_ids is not None else [8960166]
    inc_id = f"{page_id}-{rem[0]}"

    edits: list[dict[str, Any]] = []
    for idx, r in enumerate(sorted(ins)):
        edits.append(
            {
                "rev_id": r,
                "parent_rev_id": r - 1,
                "page_id": page_id,
                "user_display": "Editor 1" if idx == 0 else "85.76.x.x (anonymous)",
                "is_anonymous": idx > 0,
                "timestamp": f"2020-05-01T12:0{idx}:00Z",
                "role": "inserted" if idx == 0 else "reinserted",
                "summary": "Added claim",
                "diff_ref": f"https://fi.wikipedia.org/w/index.php?diff=prev&oldid={r}",
                "flags": [],
            }
        )
    for idx, r in enumerate(rem):
        edits.append(
            {
                "rev_id": r,
                "parent_rev_id": ins[0],
                "page_id": page_id,
                "user_display": "Editor 2",
                "is_anonymous": False,
                "timestamp": f"2020-05-01T12:1{idx}:00Z",
                "role": "removed",
                "summary": "Removed unsourced figure (85.76.x.x)",
                "diff_ref": f"https://fi.wikipedia.org/w/index.php?diff=prev&oldid={r}",
                "flags": [],
            }
        )

    assessment = None
    if assessed:
        q_list = quotes if quotes is not None else ["tappiot olivat 999 000 miestä"]
        assessment = {
            "category": category,
            "confidence": confidence,
            "what_changed_en": "Added an unsourced casualty claim.",
            "quotes": q_list,
            "has_verified_quote": True,
            "uncertainty_note": None,
            "historical_note_en": "Consensus historiography places losses far lower.",
        }

    return {
        "id": inc_id,
        "page_id": page_id,
        "other_page_ids": [],
        "inserting_rev_ids": ins,
        "removing_rev_ids": rem,
        "tier": tier,
        "score": score,
        "claim_en": claim_en if assessed else None,
        "claim_fi": claim_fi if assessed else None,
        "summary_en": "Added an unsourced casualty claim." if assessed else None,
        "narrative": narrative if assessed else None,
        "category": category if assessed else None,
        "articles": [{"page_id": page_id, "title_fi": "Talvisota", "title_en": "Winter War"}],
        "first_inserted_at": "2020-05-01T12:00:00Z",
        "last_removed_at": "2020-05-01T12:10:00Z",
        "total_exposure_seconds": 600,
        "reinsertion_count": max(0, len(ins) - 1),
        "edits": edits,
        "signals": [
            {"kind": "removal_reason", "label": "removed for lack of a reliable source", "evidence": "unsourced"},
            {"kind": "claim_content", "label": "automated assessment: a factual or narrative claim", "evidence": None},
        ],
        "confidence_note": None,
        "tier_reason": "Contested claim — because: removed for lack of a reliable source.",
        "assessment": assessment,
        "cluster_id": None,
    }


def _events_to_jsonl(events: list[dict[str, Any]]) -> str:
    nl = chr(10)
    return nl.join(json.dumps(ev, ensure_ascii=False) for ev in events) + nl


def _build_synthetic_stream(
    *,
    in_scope: bool = True,
    scope_reason: str = "Core 20th-century Finnish history topic.",
    articles: list[dict[str, Any]] | None = None,
    include_graffiti: bool = True,
    include_null_assessment: bool = True,
    extra_incidents: list[dict[str, Any]] | None = None,
) -> list[dict[str, Any]]:
    arts = articles if articles is not None else [
        _make_article(1256, "Talvisota", 0.21, wikidata_id="Q135572", via_lang=None),
        _make_article(2000, "Mainilan laukaukset", 0.34, wikidata_id=None, via_lang="en"),
    ]

    cand_assessed = _make_candidate(1256, 8960166, 8960158)
    cand_unassessed = _make_candidate(1256, 8960200, 8960199)
    cand_graffiti = _make_candidate(1256, 8960300, 8960299)
    cand_bot = _make_candidate(1256, 8960400, 8960399)

    candidates = [cand_assessed]
    if include_null_assessment:
        candidates.append(cand_unassessed)
    if include_graffiti:
        candidates.append(cand_graffiti)
    candidates.append(cand_bot)

    inc_assessed = _make_incident(
        page_id=1256,
        inserting_rev_ids=[8960194, 8960158],
        removing_rev_ids=[8960166],
        tier="contested",
        score=212.4,
        assessed=True,
    )
    inc_unassessed = _make_incident(
        page_id=1256,
        inserting_rev_ids=[8960199],
        removing_rev_ids=[8960200],
        tier="reverted",
        score=104.2,
        assessed=False,
    )

    scored_events: list[dict[str, Any]] = []
    completed_incidents: list[dict[str, Any]] = [inc_assessed]

    idx = 1
    total = len(candidates) + len(extra_incidents or [])
    scored_events.append(
        {
            "event": "candidate_scored",
            "index": idx,
            "of": total,
            "candidate": cand_assessed,
            "incident": inc_assessed,
            "exclusion": None,
            "assessment_unavailable": False,
        }
    )
    idx += 1

    if include_null_assessment:
        completed_incidents.append(inc_unassessed)
        scored_events.append(
            {
                "event": "candidate_scored",
                "index": idx,
                "of": total,
                "candidate": cand_unassessed,
                "incident": inc_unassessed,
                "exclusion": None,
                "assessment_unavailable": True,
            }
        )
        idx += 1

    if include_graffiti:
        scored_events.append(
            {
                "event": "candidate_scored",
                "index": idx,
                "of": total,
                "candidate": cand_graffiti,
                "incident": None,
                "exclusion": "graffiti",
                "assessment_unavailable": False,
            }
        )
        idx += 1

    scored_events.append(
        {
            "event": "candidate_scored",
            "index": idx,
            "of": total,
            "candidate": cand_bot,
            "incident": None,
            "exclusion": "bot",
            "assessment_unavailable": False,
        }
    )
    idx += 1

    for extra in extra_incidents or []:
        rem_id = extra["removing_rev_ids"][0]
        ins_id = extra["inserting_rev_ids"][0]
        extra_cand = _make_candidate(extra["page_id"], rem_id, ins_id)
        candidates.append(extra_cand)
        completed_incidents.append(extra)
        scored_events.append(
            {
                "event": "candidate_scored",
                "index": idx,
                "of": total,
                "candidate": extra_cand,
                "incident": extra,
                "exclusion": None,
                "assessment_unavailable": extra.get("assessment") is None,
            }
        )
        idx += 1

    completed_incidents.sort(key=lambda x: (-float(x["score"]), str(x["id"])))

    return [
        {
            "event": "articles_found",
            "count": len(arts),
            "items": arts,
            "scope": {"in_scope": in_scope, "reason": scope_reason},
            "search_fallback": False,
        },
        {
            "event": "revisions_scanned",
            "count": 1450,
            "candidates": candidates,
        },
        *scored_events,
        {
            "event": "completed",
            "from_cache": True,
            "incidents": completed_incidents,
            "elapsed": "00:00:00.1200000",
            "analysed_at": "2026-11-14T20:00:00Z",
            "degraded": 1 if include_null_assessment else 0,
            "unranked": 0,
        },
    ]


class TopicsAndHelpersTests(unittest.TestCase):
    def test_topics_json_matches_spec(self) -> None:
        topics = pack.load_topics()
        self.assertEqual(len(topics), 5)
        by_topic = {t["topic"]: t["kind"] for t in topics}
        self.assertEqual(by_topic["Suomen sisällissota"], "demo")
        self.assertEqual(by_topic["Jatkosota"], "demo")
        self.assertEqual(by_topic["Talvisota"], "demo")
        self.assertEqual(by_topic["French Revolution and the reign of terror"], "out-of-scope")
        self.assertEqual(
            by_topic[
                "Finland-Russia border crisis 2023: asylum seekers, closure of the eastern border crossings and the border fence"
            ],
            "quiet",
        )

    def test_topic_slug_and_nfd_resolution(self) -> None:
        self.assertEqual(pack.topic_slug("Suomen sisällissota"), "suomen-sisällissota")
        self.assertEqual(
            pack.topic_slug("  Finland-Russia border crisis 2023: asylum seekers! "),
            "finland-russia-border-crisis-2023-asylum-seekers",
        )
        with tempfile.TemporaryDirectory() as tmp:
            d = pathlib.Path(tmp)
            nfd_name = unicodedata.normalize("NFD", "suomen-sisällissota.jsonl")
            nfd_path = d / nfd_name
            nfd_path.write_text("", encoding="utf-8")
            resolved = pack.resolve_events_path(d, "Suomen sisällissota")
            self.assertTrue(resolved.exists())

    def test_read_pipeline_version_from_repo(self) -> None:
        ver = pack.read_pipeline_version()
        self.assertIsInstance(ver, int)
        self.assertGreaterEqual(ver, 1)


class FixtureBuildingTests(unittest.TestCase):
    def test_build_fixture_contract_graffiti_null_assessment_and_top_n(self) -> None:
        extra = [
            _make_incident(
                page_id=1256,
                inserting_rev_ids=[9000 + i],
                removing_rev_ids=[9100 + i],
                tier="reverted",
                score=100.0 + i,
                assessed=True,
            )
            for i in range(3)
        ]
        demo_lines = _events_to_jsonl(_build_synthetic_stream(extra_incidents=extra)).splitlines()
        oos_lines = _events_to_jsonl(
            _build_synthetic_stream(
                in_scope=False,
                scope_reason="French history outside Finnish Wikipedia focus.",
                include_graffiti=False,
                include_null_assessment=False,
            )
        ).splitlines()
        quiet_lines = _events_to_jsonl(
            _build_synthetic_stream(
                in_scope=True,
                scope_reason="Recent Finnish border policy.",
                include_graffiti=False,
                include_null_assessment=False,
            )
        ).splitlines()

        topics_spec = [
            {"topic": "Talvisota", "kind": "demo"},
            {"topic": "French Revolution and the reign of terror", "kind": "out-of-scope"},
            {"topic": "Border crisis", "kind": "quiet"},
        ]
        runs = {
            "Talvisota": pack.parse_events_jsonl(demo_lines),
            "French Revolution and the reign of terror": pack.parse_events_jsonl(oos_lines),
            "Border crisis": pack.parse_events_jsonl(quiet_lines),
        }

        fixture = pack.build_fixture(
            topics_spec,
            runs,
            recorded_at="2026-11-14T21:00:00Z",
            git_commit="abc1234",
            pipeline_version=3,
            top_n=3,
        )

        self.assertEqual(fixture["version"], 1)
        self.assertEqual(fixture["recorded_at"], "2026-11-14T21:00:00Z")
        self.assertEqual(fixture["git_commit"], "abc1234")
        self.assertEqual(fixture["pipeline_version"], 3)
        self.assertEqual(fixture["top_n"], 3)
        self.assertEqual(len(fixture["topics"]), 3)

        demo_entry = fixture["topics"][0]
        self.assertEqual(demo_entry["topic"], "Talvisota")
        self.assertEqual(demo_entry["kind"], "demo")
        self.assertEqual(
            demo_entry["scope"],
            {"in_scope": True, "reason": "Core 20th-century Finnish history topic."},
        )
        # Articles preserve TopicArticle contract fields only (no UI computed fields)
        self.assertEqual(
            demo_entry["articles"],
            [
                {"lang": "fi", "page_id": 1256, "title": "Talvisota", "distance": 0.21, "wikidata_id": "Q135572"},
                {"lang": "fi", "page_id": 2000, "title": "Mainilan laukaukset", "distance": 0.34, "via_lang": "en"},
            ],
        )
        # 4 base candidates + 3 extra = 7
        self.assertEqual(demo_entry["candidates"], 7)

        # Classifications:
        # - 1256-8960166: rebuilt from incident assessment
        # - 1256-8960200: null assessment -> MUST be skipped
        # - 1256-8960300: graffiti exclusion -> vandalism confidence 5 + nulls + empty evidence
        # - 1256-8960400: bot exclusion -> MUST be omitted
        classifications = demo_entry["classifications"]
        self.assertIn("1256-8960166", classifications)
        self.assertNotIn("1256-8960200", classifications)
        self.assertIn("1256-8960300", classifications)
        self.assertNotIn("1256-8960400", classifications)

        self.assertEqual(
            classifications["1256-8960166"],
            {
                "category": "pov_push",
                "confidence": 4,
                "what_changed_en": "Added an unsourced casualty claim.",
                "evidence": ["tappiot olivat 999 000 miestä"],
                "claim_en": "Disputed casualty figure added without source",
                "claim_fi": "Kiistanalainen tappioluku ilman lähdettä",
                "narrative_en": "Inflates wartime losses",
                "historical_note_en": "Consensus historiography places losses far lower.",
                "uncertainty_note": None,
            },
        )
        self.assertEqual(
            classifications["1256-8960300"],
            {
                "category": "vandalism",
                "confidence": 5,
                "what_changed_en": None,
                "evidence": [],
                "claim_en": None,
                "claim_fi": None,
                "narrative_en": None,
                "historical_note_en": None,
                "uncertainty_note": None,
            },
        )

        # Top-N incidents capped at 3, ranked 1..3, with CONTRACT §1 ascending '+' key
        self.assertEqual(len(demo_entry["incidents"]), 3)
        top1 = demo_entry["incidents"][0]
        self.assertEqual(
            top1,
            {
                "rank": 1,
                "id": "1256-8960166",
                "key": "8960158+8960194",
                "page_id": 1256,
                "tier": "contested",
                "score": 212.4,
                "inserting_rev_ids": [8960194, 8960158],
                "removing_rev_ids": [8960166],
                "claim_en": "Disputed casualty figure added without source",
            },
        )

        # Expectations per topic kind
        self.assertEqual(
            demo_entry["expect"],
            {"no_new_campaign": True, "max_contested_or_above": None, "out_of_scope": None},
        )
        self.assertEqual(
            fixture["topics"][1]["expect"],
            {"no_new_campaign": None, "max_contested_or_above": None, "out_of_scope": True},
        )
        self.assertEqual(
            fixture["topics"][2]["expect"],
            {"no_new_campaign": None, "max_contested_or_above": 1, "out_of_scope": None},
        )

    def test_stage_failed_and_incomplete_stream_raise(self) -> None:
        failed_lines = [
            json.dumps({"event": "stage_failed", "stage": "find_articles", "kind": "corpus_unavailable", "error": "down"})
        ]
        with self.assertRaises(RuntimeError):
            pack.parse_events_jsonl(failed_lines)

        incomplete_lines = [
            json.dumps({"event": "articles_found", "count": 0, "items": [], "scope": None})
        ]
        with self.assertRaises(RuntimeError):
            pack.parse_events_jsonl(incomplete_lines)

    def test_cli_record_and_diff_roundtrip_offline(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = pathlib.Path(tmp)
            events_dir = tmp_path / "events"
            events_dir.mkdir()
            topics_file = tmp_path / "topics.json"
            out_fixture = tmp_path / "demo-pack.json"

            topics_spec = [
                {"topic": "Suomen sisällissota", "kind": "demo"},
                {"topic": "French Revolution and the reign of terror", "kind": "out-of-scope"},
                {"topic": "Border crisis", "kind": "quiet"},
            ]
            topics_file.write_text(json.dumps(topics_spec), encoding="utf-8")

            (events_dir / f"{pack.topic_slug('Suomen sisällissota')}.jsonl").write_text(
                _events_to_jsonl(_build_synthetic_stream()),
                encoding="utf-8",
            )
            (events_dir / f"{pack.topic_slug('French Revolution and the reign of terror')}.jsonl").write_text(
                _events_to_jsonl(_build_synthetic_stream(in_scope=False)),
                encoding="utf-8",
            )
            (events_dir / f"{pack.topic_slug('Border crisis')}.jsonl").write_text(
                _events_to_jsonl(_build_synthetic_stream(include_null_assessment=False)),
                encoding="utf-8",
            )

            stderr_buf = io.StringIO()
            with contextlib.redirect_stderr(stderr_buf):
                rc_rec = pack.main(
                    [
                        "record",
                        "--topics",
                        str(topics_file),
                        "--events-dir",
                        str(events_dir),
                        "--out",
                        str(out_fixture),
                    ]
                )
            self.assertEqual(rc_rec, 0)
            self.assertTrue(out_fixture.exists())

            stdout_buf = io.StringIO()
            with contextlib.redirect_stdout(stdout_buf):
                rc_diff = pack.main(
                    [
                        "diff",
                        "--fixture",
                        str(out_fixture),
                        "--events-dir",
                        str(events_dir),
                    ]
                )
            self.assertEqual(rc_diff, 0)
            self.assertIn("PASSED", stdout_buf.getvalue())


class DiffPackTests(unittest.TestCase):
    def _baseline_and_runs(self) -> tuple[dict[str, Any], dict[str, pack.ParsedRun]]:
        inc1 = _make_incident(page_id=1256, inserting_rev_ids=[101], removing_rev_ids=[201], tier="contested", score=220.0)
        inc2 = _make_incident(page_id=1256, inserting_rev_ids=[102], removing_rev_ids=[202], tier="reverted", score=115.0)
        demo_run = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(
                    include_graffiti=False,
                    include_null_assessment=False,
                    extra_incidents=[inc1, inc2],
                )
            ).splitlines()
        )
        oos_run = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(in_scope=False, include_graffiti=False, include_null_assessment=False)
            ).splitlines()
        )
        quiet_run = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(include_graffiti=False, include_null_assessment=False)
            ).splitlines()
        )
        specs = [
            {"topic": "Talvisota", "kind": "demo"},
            {"topic": "French Revolution", "kind": "out-of-scope"},
            {"topic": "Quiet border", "kind": "quiet"},
        ]
        runs = {
            "Talvisota": demo_run,
            "French Revolution": oos_run,
            "Quiet border": quiet_run,
        }
        fixture = pack.build_fixture(
            specs,
            runs,
            recorded_at="2026-11-14T21:00:00Z",
            git_commit="abc1234",
            pipeline_version=3,
            top_n=10,
        )
        return fixture, runs

    def test_detects_added_removed_moved_tier_articles_and_candidates_without_breaking_expect(self) -> None:
        fixture, runs = self._baseline_and_runs()

        # Baseline Talvisota top incidents:
        # #1 1256-201 (contested, 220.0)
        # #2 1256-8960166 (contested, 212.4)
        # #3 1256-202 (reverted, 115.0)
        # Now: remove 1256-202, demote 1256-201 to reverted (105.0) so 1256-8960166 moves #2 -> #1,
        # and add new 1256-299 (contested, 205.0) at #2! Also add an article and candidate.
        inc1_demoted = _make_incident(page_id=1256, inserting_rev_ids=[101], removing_rev_ids=[201], tier="reverted", score=105.0)
        inc_added = _make_incident(page_id=1256, inserting_rev_ids=[199], removing_rev_ids=[299], tier="contested", score=205.0)
        new_articles = [
            _make_article(2000, "Mainilan laukaukset", 0.20),
            _make_article(1256, "Talvisota", 0.22),
            _make_article(3000, "Raatteen tien taistelu", 0.40),
        ]
        modified_demo = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(
                    articles=new_articles,
                    include_graffiti=True,
                    include_null_assessment=False,
                    extra_incidents=[inc1_demoted, inc_added],
                )
            ).splitlines()
        )
        runs["Talvisota"] = modified_demo

        report = pack.diff_pack(fixture, runs)
        self.assertTrue(report.ok)  # No campaign added on demo, quiet & oos untouched
        td = report.topics[0]
        self.assertEqual([i["id"] for i in td.added], ["1256-299"])
        self.assertEqual([i["id"] for i in td.removed], ["1256-202"])
        moved_map = {inc["id"]: (old_r, new_r) for inc, old_r, new_r in td.moved}
        self.assertEqual(moved_map["1256-8960166"], (2, 1))
        self.assertEqual(moved_map["1256-201"], (1, 3))
        self.assertEqual(td.tier_changes, [("1256-201", "contested", "reverted")])
        self.assertEqual([a["page_id"] for a in td.articles_added], [3000])
        self.assertTrue(td.articles_reordered)
        self.assertEqual(td.old_candidates, 4)
        self.assertEqual(td.new_candidates, 5)
        self.assertIn("Added in top 10", report.markdown)
        self.assertIn("Removed from top 10", report.markdown)
        self.assertIn("Moved in top 10", report.markdown)
        self.assertIn("Tier changed", report.markdown)

    def test_new_campaign_flagged_loudly_and_exits_nonzero(self) -> None:
        fixture, runs = self._baseline_and_runs()
        inc_campaign = _make_incident(
            page_id=1256,
            inserting_rev_ids=[501],
            removing_rev_ids=[601],
            tier="campaign",
            score=318.5,
            claim_en="Coordinated cross-account narrative push",
        )
        runs["Talvisota"] = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(
                    include_graffiti=False,
                    include_null_assessment=False,
                    extra_incidents=[inc_campaign],
                )
            ).splitlines()
        )

        report = pack.diff_pack(fixture, runs)
        self.assertFalse(report.ok)
        self.assertEqual([c["id"] for c in report.topics[0].new_campaigns], ["1256-601"])
        self.assertIn("WARNING — NEW `campaign` ITEM(S) DETECTED", report.markdown)
        self.assertIn("expect.no_new_campaign", report.markdown)

        # CLI diff returns exit code 1 when expectation breaks
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = pathlib.Path(tmp)
            fix_path = tmp_path / "fixture.json"
            ev_dir = tmp_path / "events"
            ev_dir.mkdir()
            fix_path.write_text(json.dumps(fixture), encoding="utf-8")
            (ev_dir / f"{pack.topic_slug('Talvisota')}.jsonl").write_text(
                _events_to_jsonl(
                    _build_synthetic_stream(
                        include_graffiti=False,
                        include_null_assessment=False,
                        extra_incidents=[inc_campaign],
                    )
                ),
                encoding="utf-8",
            )
            (ev_dir / f"{pack.topic_slug('French Revolution')}.jsonl").write_text(
                _events_to_jsonl(
                    _build_synthetic_stream(in_scope=False, include_graffiti=False, include_null_assessment=False)
                ),
                encoding="utf-8",
            )
            (ev_dir / f"{pack.topic_slug('Quiet border')}.jsonl").write_text(
                _events_to_jsonl(_build_synthetic_stream(include_graffiti=False, include_null_assessment=False)),
                encoding="utf-8",
            )
            with contextlib.redirect_stdout(io.StringIO()):
                rc = pack.main(["diff", "--fixture", str(fix_path), "--events-dir", str(ev_dir)])
            self.assertEqual(rc, 1)

    def test_scope_change_and_quiet_threshold_break_expectations(self) -> None:
        fixture, runs = self._baseline_and_runs()

        # Flip out-of-scope topic to in_scope=True
        runs["French Revolution"] = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(in_scope=True, include_graffiti=False, include_null_assessment=False)
            ).splitlines()
        )
        # Add an extra contested incident on the quiet topic (baseline had 1, now 2 > max 1)
        extra_contested = _make_incident(
            page_id=1256,
            inserting_rev_ids=[701],
            removing_rev_ids=[801],
            tier="contested",
            score=208.0,
        )
        runs["Quiet border"] = pack.parse_events_jsonl(
            _events_to_jsonl(
                _build_synthetic_stream(
                    include_graffiti=False,
                    include_null_assessment=False,
                    extra_incidents=[extra_contested],
                )
            ).splitlines()
        )

        report = pack.diff_pack(fixture, runs)
        self.assertFalse(report.ok)
        oos_diff = report.topics[1]
        quiet_diff = report.topics[2]
        self.assertTrue(oos_diff.scope_changed)
        self.assertTrue(any("expect.out_of_scope" in b for b in oos_diff.broken_expectations))
        self.assertTrue(any("expect.max_contested_or_above" in b for b in quiet_diff.broken_expectations))


class CheckPackTests(unittest.TestCase):
    def _make_fake_sql(
        self,
        revisions: dict[tuple[int, int], str],
        texts: dict[tuple[int, int], str],
    ) -> tuple[Any, list[str]]:
        queries: list[str] = []
        pair_re = re.compile(r"\(page_id\s*=\s*(\d+)\s+AND\s+rev_id\s*=\s*(\d+)\)")

        def fake_sql(query: str) -> list[dict[str, Any]]:
            queries.append(query)
            pairs = [(int(m.group(1)), int(m.group(2))) for m in pair_re.finditer(query)]
            if "FROM corpus.revision_text" in query:
                return [
                    {"page_id": str(pid), "rev_id": str(rid), "text": texts[(pid, rid)]}
                    for pid, rid in pairs
                    if (pid, rid) in texts
                ]
            if "FROM corpus.revision" in query:
                return [
                    {"page_id": str(pid), "rev_id": str(rid), "ts": revisions[(pid, rid)]}
                    for pid, rid in pairs
                    if (pid, rid) in revisions
                ]
            raise AssertionError(f"Unexpected SQL query: {query}")

        return fake_sql, queries

    def test_clean_incidents_pass_and_cache_revisions(self) -> None:
        inc = _make_incident(
            page_id=1256,
            inserting_rev_ids=[8960158, 8960194],
            removing_rev_ids=[8960166],
            quotes=["tappiot olivat 999 000 miestä"],
        )
        # Share an inserting rev across two topics to verify per-rev caching
        run1 = pack.ParsedRun(scope=None, articles=[], candidates=1, classifications={}, incidents=[inc])
        run2 = pack.ParsedRun(scope=None, articles=[], candidates=1, classifications={}, incidents=[copy.deepcopy(inc)])

        revisions = {
            (1256, 8960158): "2020-05-01 12:00:00",  # MariaDB DATETIME format
            (1256, 8960194): "2020-05-01T12:01:00Z",
            (1256, 8960166): "2020-05-01 12:10:00",
        }
        texts = {
            (1256, 8960158): "Johdanto ilman lukua.",
            # Quote appears only in the second inserting rev!
            (1256, 8960194): "Talvisodassa tappiot olivat 999 000 miestä joidenkin väitteiden mukaan.",
        }
        fake_sql, queries = self._make_fake_sql(revisions, texts)

        report = pack.check_pack([("Talvisota", run1), ("Jatkosota", run2)], sql_fn=fake_sql)
        self.assertTrue(report.ok, msg=report.markdown)
        self.assertEqual(len(queries), 2)  # 1 batched revision query + 1 batched revision_text query across both topics
        self.assertIn("PASSED", report.markdown)

    def test_detects_missing_quote_missing_rev_bad_timestamp_bad_links_and_raw_ips(self) -> None:
        bad_inc = _make_incident(
            page_id=1256,
            inserting_rev_ids=[8960158],
            removing_rev_ids=[8960166],
            quotes=["tätä lainausta ei ole tekstissä"],
        )
        # (b) edits[0] wrong timestamp; edits[1] missing in corpus.revision
        bad_inc["edits"][0]["timestamp"] = "2020-05-01T12:59:59Z"
        # (c) edits[0].diff_ref points to wrong domain; extra_url points to unrelated rev
        bad_inc["edits"][0]["diff_ref"] = "https://en.wikipedia.org/w/index.php?diff=prev&oldid=8960158"
        bad_inc["extra_url"] = "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=9999999"
        # (d) RFC 5737 / RFC 3849 documentation-reserved test IPs left unmasked in text fields
        raw_v4 = "198.51.100.42"
        raw_v6 = "2001:db8:85a3::8a2e:370:7334"
        bad_inc["edits"][0]["summary"] = f"Reverted edit by {raw_v4}"
        bad_inc["claim_en"] = f"Claim attributed to {raw_v6} in comment"

        run = pack.ParsedRun(scope=None, articles=[], candidates=1, classifications={}, incidents=[bad_inc])
        revisions = {
            (1256, 8960158): "2020-05-01 12:00:00",
            # (1256, 8960166) intentionally omitted -> missing rev_id
        }
        texts = {
            (1256, 8960158): "Aivan muuta tekstiä artikkelissa.",
        }
        fake_sql, _ = self._make_fake_sql(revisions, texts)

        report = pack.check_pack([("Talvisota", run)], sql_fn=fake_sql)
        self.assertFalse(report.ok)

        fields = {f.field for f in report.topics[0].failures}
        self.assertIn("assessment.quotes[0]", fields)
        self.assertIn("edits[0].timestamp", fields)
        self.assertIn("edits[1].rev_id", fields)
        self.assertIn("edits[0].diff_ref", fields)
        self.assertIn("extra_url", fields)
        self.assertIn("edits[0].summary", fields)
        self.assertIn("claim_en", fields)

        # Privacy check: Markdown report must NOT echo the raw test IPs, only masked forms
        self.assertNotIn(raw_v4, report.markdown)
        self.assertNotIn(raw_v6, report.markdown)
        self.assertIn("198.51.x.x (anonymous)", report.markdown)
        self.assertIn("2001:DB8:… (anonymous)", report.markdown)

    def test_find_raw_ips_ignores_masked_ips_dates_and_timestamps(self) -> None:
        safe = (
            "85.76.x.x (anonymous) edited on 13.3.1940 at 18:20:00 and 2020-05-01T12:00:00Z "
            "with 2001:14BA:… (anonymous) and ~2025-26183-73 (temporary account)."
        )
        self.assertEqual(pack.find_raw_ips(safe), [])

    def test_crlf_quote_and_cli_check_exit_codes(self) -> None:
        lf = chr(10)
        crlf = chr(13) + chr(10)
        inc = _make_incident(
            page_id=1256,
            inserting_rev_ids=[8960158],
            removing_rev_ids=[8960166],
            quotes=[f"Ensimmäinen rivi{lf}Toinen rivi"],
        )
        stream = [
            {"event": "articles_found", "count": 1, "items": [_make_article()], "scope": {"in_scope": True, "reason": "ok"}},
            {"event": "revisions_scanned", "count": 10, "candidates": [_make_candidate(1256, 8960166, 8960158)]},
            {
                "event": "candidate_scored",
                "index": 1,
                "of": 1,
                "candidate": _make_candidate(1256, 8960166, 8960158),
                "incident": inc,
                "exclusion": None,
                "assessment_unavailable": False,
            },
            {"event": "completed", "from_cache": True, "incidents": [inc], "elapsed": "00:00:00.1000000"},
        ]
        revisions = {
            (1256, 8960158): "2020-05-01 12:00:00",
            (1256, 8960166): "2020-05-01 12:10:00",
        }
        texts_ok = {(1256, 8960158): f"Alku.{crlf}Ensimmäinen rivi{crlf}Toinen rivi{crlf}Loppu."}
        fake_sql_ok, _ = self._make_fake_sql(revisions, texts_ok)
        fake_sql_fail, _ = self._make_fake_sql(revisions, {(1256, 8960158): "Ei täsmää."})

        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = pathlib.Path(tmp)
            topics_file = tmp_path / "topics.json"
            ev_dir = tmp_path / "events"
            ev_dir.mkdir()
            topics_file.write_text(
                json.dumps(
                    [
                        {"topic": "Talvisota", "kind": "demo"},
                        {"topic": "French Revolution", "kind": "out-of-scope"},
                    ]
                ),
                encoding="utf-8",
            )
            (ev_dir / "talvisota.jsonl").write_text(_events_to_jsonl(stream), encoding="utf-8")

            with mock.patch.object(pack.aot, "sql", side_effect=fake_sql_ok), contextlib.redirect_stdout(io.StringIO()):
                rc_ok = pack.main(["check", "--topics", str(topics_file), "--events-dir", str(ev_dir)])
            self.assertEqual(rc_ok, 0)

            with mock.patch.object(pack.aot, "sql", side_effect=fake_sql_fail), contextlib.redirect_stdout(io.StringIO()):
                rc_fail = pack.main(["check", "--topics", str(topics_file), "--events-dir", str(ev_dir)])
            self.assertEqual(rc_fail, 1)


if __name__ == "__main__":
    unittest.main()
