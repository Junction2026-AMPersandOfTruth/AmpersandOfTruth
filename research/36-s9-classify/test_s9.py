#!/usr/bin/env python3
"""Offline unit tests for P1.S9 (`inputs.py`, `score.py`, and `prompts/`)."""
from __future__ import annotations

import io
import json
import pathlib
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stdout

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "lib"))

import inputs  # noqa: E402
import score  # noqa: E402

NL = chr(10)
GOLDEN_LEAK_KEYS = {"label", "exclusion", "category", "description", "why", "notes"}


def _make_classification_value(
    category: str = "pov_push",
    confidence: int = 4,
    evidence: list[str] | None = None,
    claim_en: str | None = "The edit pushes a one-sided claim.",
) -> dict:
    return {
        "what_changed_en": "Inserted loaded framing into the lead.",
        "evidence": evidence if evidence is not None else ["hyökkäyssota"],
        "claim_en": claim_en,
        "claim_fi": "Muokkaus esittää yksipuolisen väitteen." if claim_en else None,
        "narrative_en": "wartime framing" if claim_en else None,
        "category": category,
        "confidence": confidence,
        "historical_note_en": None,
        "uncertainty_note": None,
    }


class TestMaskSummary(unittest.TestCase):
    def test_ipv4_undo_summary_never_partially_replaced(self) -> None:
        linked = (
            "Kumottu muokkaus 8963132, jonka teki "
            "[[Toiminnot:Muokkaukset/85.76.12.34|85.76.12.34]] "
            "([[Keskustelu käyttäjästä:85.76.12.34|keskustelu]])"
        )
        self.assertEqual(
            inputs.mask_summary(linked, known_users=["85.76.12.34", "85", "76"]),
            "Kumottu muokkaus 8963132, jonka teki 85.76.x.x (keskustelu)",
        )

        unlinked = "Kumottu muokkaus 8963132, jonka teki 85.76.12.34 (keskustelu) - Asenteellinen kommentti."
        masked_unlinked = inputs.mask_summary(unlinked)
        self.assertEqual(
            masked_unlinked,
            "Kumottu muokkaus 8963132, jonka teki 85.76.x.x (keskustelu) - Asenteellinen kommentti.",
        )
        self.assertNotIn("[editor].", masked_unlinked)

    def test_ipv6_undo_and_flaggedrevs_never_partially_replaced(self) -> None:
        undo_v6 = (
            "Kumottu muokkaus 21467444, jonka teki "
            "[[Toiminnot:Muokkaukset/2001:14ba:1234::1|2001:14ba:1234::1]] "
            "([[Keskustelu käyttäjästä:2001:14ba:1234::1|keskustelu]]) - Lähteetöntä spekulointia."
        )
        self.assertEqual(
            inputs.mask_summary(undo_v6),
            "Kumottu muokkaus 21467444, jonka teki 2001:14BA:… (keskustelu) - Lähteetöntä spekulointia.",
        )

        flagged_v6 = (
            "Hylättiin viimeisimmät 2 tekstimuutosta (tehneet 2001:14ba:1234::1 ja 86.115.9.10) "
            "ja palautettiin versio 18837407, jonka on tehnyt 2001:14ba:1234::1: roskaa"
        )
        self.assertEqual(
            inputs.mask_summary(flagged_v6),
            "Hylättiin viimeisimmät 2 tekstimuutosta (tehneet 2001:14BA:… ja 86.115.x.x) "
            "ja palautettiin versio 18837407, jonka on tehnyt 2001:14BA:…: roskaa",
        )

    def test_named_dotted_temp_and_multi_editor_templates_redacted(self) -> None:
        dotted_undo = (
            "Kumottu muokkaus 12345, jonka teki [[Käyttäjä:J. K. Nummi|J. K. Nummi]] "
            "([[Keskustelu käyttäjästä:J. K. Nummi|keskustelu]]). Lähteetön."
        )
        self.assertEqual(
            inputs.mask_summary(dotted_undo),
            "Kumottu muokkaus 12345, jonka teki [editor] (keskustelu). Lähteetön.",
        )

        unlinked_dotted = (
            "Hylättiin viimeisimmät 3 tekstimuutosta (tehneet Matti_M, 62.197.4.5 ja J. K. Nummi) "
            "ja palautettiin versio 999, jonka on tehnyt ~2025-12345-67: omakustannespämmiä"
        )
        masked = inputs.mask_summary(unlinked_dotted)
        self.assertEqual(
            masked,
            "Hylättiin viimeisimmät 3 tekstimuutosta (tehneet [editor], 62.197.x.x ja [editor]) "
            "ja palautettiin versio 999, jonka on tehnyt [editor]: omakustannespämmiä",
        )

        genitive = "Käyttäjä SalaNimimerkki:n aloittaman muokkaussodan ehkäiseminen"
        self.assertEqual(
            inputs.mask_summary(genitive, known_users=["SalaNimimerkki"]),
            "Käyttäjä [editor]:n aloittaman muokkaussodan ehkäiseminen",
        )

        truncated_ip = "viimeisimpään käyttäjän [[User:85.76.12"
        self.assertEqual(inputs.mask_summary(truncated_ip), "viimeisimpään käyttäjän [editor]")


class TestInputAssemblyAndBlindness(unittest.TestCase):
    def setUp(self) -> None:
        self.parent_text = (
            "'''Talvisota''' oli sota vuosina 1939–1940. "
            + ("Taustaa ja neuvotteluja käsitellään tässä kappaleessa. " * 18)
            + NL + NL + "== Sodan syyt ==" + NL
            + "Neuvostoliitto aloitti hyökkäyksen 30. marraskuuta 1939 Mainilan laukausten jälkeen. "
            + ("Lisää tekstiä sodan alkuvaiheista ja rintamista. " * 20)
        )
        self.rev_by_id = {
            99: {
                "page_id": 10,
                "rev_id": 99,
                "parent_id": 90,
                "title": "Talvisota",
                "ts": "2020-01-01 09:00:00",
                "user": "ParentAuthor",
                "anonymous": "0",
                "comment": "alkuperäinen",
            },
            # Intentionally unordered ids/timestamps to test chronological sorting.
            102: {
                "page_id": 10,
                "rev_id": 102,
                "parent_id": 101,
                "title": "Talvisota",
                "ts": "2020-01-01 12:05:00",
                "user": "NamedPusher",
                "anonymous": "0",
                "comment": "/* Sodan syyt */ täsmennys",
            },
            101: {
                "page_id": 10,
                "rev_id": 101,
                "parent_id": 99,
                "title": "Talvisota",
                "ts": "2020-01-01 12:00:00",
                "user": "85.76.12.34",
                "anonymous": "1",
                "comment": "/* Sodan syyt */ korjaus",
            },
            103: {
                "page_id": 10,
                "rev_id": 103,
                "parent_id": 102,
                "title": "Talvisota",
                "ts": "2020-01-01 12:06:00",
                "user": "NamedPusher",
                "anonymous": "0",
                "comment": "/* Sodan syyt */ täsmennys",  # duplicate summary -> deduped
            },
            105: {
                "page_id": 10,
                "rev_id": 105,
                "parent_id": 103,
                "title": "Talvisota",
                "ts": "2020-01-01 14:30:00",
                "user": "PatrollerFi",
                "anonymous": "0",
                "comment": (
                    "Kumottu muokkaus 101, jonka teki "
                    "[[Toiminnot:Muokkaukset/85.76.12.34|85.76.12.34]] "
                    "([[Keskustelu käyttäjästä:85.76.12.34|keskustelu]])"
                ),
            },
            106: {
                "page_id": 10,
                "rev_id": 106,
                "parent_id": 105,
                "title": "Talvisota",
                "ts": "2020-01-01 15:00:00",
                "user": "PatrollerTwo",
                "anonymous": "0",
                "comment": "Käyttäjän NamedPusher muokkaukset kumottiin",
            },
        }
        self.s3_by_rev = {
            101: {
                "rev_id": 101,
                "page_id": 10,
                "old_rev_id": 99,
                "new_rev_id": 101,
                "error": None,
                "segments": json.dumps(
                    [
                        {
                            "op": "removed",
                            "kind": "prose",
                            "text": "Mainilan laukausten jälkeen",
                            "moved": False,
                            "old_paragraph": 1,
                        },
                        {
                            "op": "added",
                            "kind": "prose",
                            "text": "Suomen tykistön provokaation jälkeen",
                            "moved": False,
                            "new_paragraph": 1,
                        },
                        {
                            "op": "added",
                            "kind": "prose",
                            "text": "SIIRRETTY KAPPALE",
                            "moved": True,
                            "new_paragraph": 2,
                        },
                    ]
                ),
            },
            102: {
                "rev_id": 102,
                "page_id": 10,
                "old_rev_id": 101,
                "new_rev_id": 102,
                "error": None,
                "segments": json.dumps(
                    [
                        {
                            "op": "added",
                            "kind": "prose",
                            "text": "Suomen tykistön provokaation jälkeen",  # duplicate -> union dedupes
                            "moved": False,
                        },
                        {
                            "op": "added",
                            "kind": "prose",
                            "text": "ilman Neuvostoliiton syytä",
                            "moved": False,
                        },
                    ]
                ),
            },
            # rev 103 intentionally omitted to verify graceful missing-diff diagnostics
        }

    def test_multi_rev_item_assembly_and_blindness(self) -> None:
        golden_item_with_secrets = {
            "id": "winter-war/i99",
            "page_id": 10,
            "inserting_rev_ids": [102, 101, 103],
            "removing_rev_ids": [106, 105],
            "label": "contested",
            "exclusion": None,
            "category": "false_factual_claim",
            "description": "SECRET_GOLDEN_DESCRIPTION",
            "why": "SECRET_GOLDEN_WHY",
            "notes": "SECRET_GOLDEN_NOTES",
        }
        ins_revs_sorted = [self.rev_by_id[101], self.rev_by_id[102], self.rev_by_id[103]]
        rem_revs_sorted = [self.rev_by_id[105], self.rev_by_id[106]]
        facts = inputs.format_facts(
            ins_revs=ins_revs_sorted,
            rem_revs=rem_revs_sorted,
            s5_revert_by_rev={105: {"removal_reasons": "neutrality_pov,sourcing", "auto_family": "undo_modern"}},
            s5_summary_by_rev={101: {"insert_flags": "argues_position", "reasons": "", "auto_family": None}},
            s4_episodes_by_rev={
                101: [{"episode_id": 7, "kind": "edit_war", "revert_count": 4, "named_participants": 2, "protection_hint": 1}]
            },
            s6_by_rev={
                102: {
                    "edits": 12,
                    "distinct_pages": 1,
                    "reverted_edits_by_others": 7,
                    "single_purpose": 1,
                    "throwaway": 0,
                    "is_bot": 0,
                }
            },
        )

        rec_base, diag = inputs.assemble_item_record(
            item=golden_item_with_secrets,
            article_title="Talvisota",
            rev_by_id=self.rev_by_id,
            s3_by_rev=self.s3_by_rev,
            texts_by_rev={99: self.parent_text},
            include_facts=False,
        )
        rec_facts, _ = inputs.assemble_item_record(
            item=golden_item_with_secrets,
            article_title="Talvisota",
            rev_by_id=self.rev_by_id,
            s3_by_rev=self.s3_by_rev,
            texts_by_rev={99: self.parent_text},
            include_facts=True,
            facts_text=facts,
        )

        # Exact schema of variables + _id, zero golden keys
        self.assertEqual(set(rec_base.keys()), {"_id", *inputs.CLASSIFY_VARS})
        self.assertEqual(set(rec_facts.keys()), {"_id", *inputs.CLASSIFY_VARS, "facts"})
        self.assertFalse(GOLDEN_LEAK_KEYS & set(rec_base.keys()))
        self.assertFalse(GOLDEN_LEAK_KEYS & set(rec_facts.keys()))

        # Multi-rev aggregation checks
        self.assertEqual(rec_base["_id"], "winter-war/i99")
        self.assertEqual(rec_base["edit_timestamp"], "2020-01-01T12:00:00Z")
        self.assertEqual(rec_base["editor_kind"], "mixed")
        self.assertEqual(rec_base["exposure"], "2 h 30 min")
        self.assertEqual(rec_base["edit_summary"], "/* Sodan syyt */ korjaus | /* Sodan syyt */ täsmennys")
        self.assertEqual(
            rec_base["revert_summary"],
            "Kumottu muokkaus 101, jonka teki 85.76.x.x (keskustelu) | Käyttäjän [editor] muokkaukset kumottiin",
        )
        self.assertEqual(rec_base["removed_text"], "Mainilan laukausten jälkeen")
        self.assertEqual(
            rec_base["inserted_text"],
            "Suomen tykistön provokaation jälkeen" + NL + "ilman Neuvostoliiton syytä",
        )
        self.assertNotIn("SIIRRETTY KAPPALE", rec_base["inserted_text"])

        # Context window checks
        self.assertLessEqual(len(rec_base["context"]), 1200)
        self.assertIn("Mainilan laukausten jälkeen", rec_base["context"])

        # Diagnostics for missing diff rev 103
        self.assertTrue(diag["lacked_diff"])
        self.assertEqual(diag["missing_diff_revs"], [103])

        # Privacy & blindness check across serialized JSONL payloads
        dumped = json.dumps(rec_facts, ensure_ascii=False)
        for forbidden in (
            "SECRET_GOLDEN_DESCRIPTION",
            "SECRET_GOLDEN_WHY",
            "SECRET_GOLDEN_NOTES",
            "false_factual_claim",
            "NamedPusher",
            "PatrollerFi",
            "PatrollerTwo",
            "ParentAuthor",
            "85.76.12.34",
            "[editor].76",
        ):
            self.assertNotIn(forbidden, dumped)
        self.assertIn("re-insertions: 2", rec_facts["facts"])
        self.assertIn("neutrality_pov,sourcing", rec_facts["facts"])
        self.assertIn("edit_war", rec_facts["facts"])
        self.assertIn("single_purpose=yes", rec_facts["facts"])

    def test_context_extraction_branches(self) -> None:
        # 1. Pure insertion located via parent vs new wikitext prefix divergence
        parent_doc = ("Johdantokappale. " * 60) + NL + NL + "== Otsikko ==" + NL + ("Vanha kappale. " * 60)
        insert_pos = parent_doc.index("== Otsikko ==") + len("== Otsikko ==" + NL)
        new_doc = parent_doc[:insert_pos] + "UUSI LISÄTTY LAUSE. " + parent_doc[insert_pos:]
        ctx_div = inputs.extract_context(
            parent_text=parent_doc,
            first_diff_segments=[{"op": "added", "text": "UUSI LISÄTTY LAUSE.", "moved": False}],
            first_new_text=new_doc,
            first_comment=None,
        )
        self.assertLessEqual(len(ctx_div), 1200)
        self.assertIn("== Otsikko ==", ctx_div)

        # 2. Section heading fallback when first_new_text is unavailable
        ctx_sec = inputs.extract_context(
            parent_text=parent_doc,
            first_diff_segments=[{"op": "added", "text": "UUSI", "moved": False}],
            first_new_text=None,
            first_comment="/* Otsikko */ lisäys",
        )
        self.assertLessEqual(len(ctx_sec), 1200)
        self.assertIn("== Otsikko ==", ctx_sec)

        # 3. Paragraph-index fallback when neither anchor nor heading matches
        ctx_para = inputs.extract_context(
            parent_text=parent_doc,
            first_diff_segments=[{"op": "added", "text": "UUSI", "moved": False, "old_paragraph": 1}],
            first_new_text=None,
            first_comment=None,
        )
        self.assertLessEqual(len(ctx_para), 1200)
        self.assertIn("== Otsikko ==", ctx_para)

    def test_diff_capping_and_batch_stage2_refute_helpers(self) -> None:
        long_diff = [
            {
                "segments": [
                    {"op": "added", "text": "A" * 4500, "moved": False},
                    {"op": "removed", "text": "R" * 2500, "moved": False},
                ]
            }
        ]
        ins_txt, rem_txt = inputs.union_diff_texts(long_diff)
        self.assertEqual(len(ins_txt), inputs.MAX_INSERTED_CHARS)
        self.assertEqual(len(rem_txt), inputs.MAX_REMOVED_CHARS)
        self.assertTrue(ins_txt.endswith("…"))
        self.assertTrue(rem_txt.endswith("…"))

        # Batch by article chunks > 6 items into <page_id>:1 and <page_id>:2
        sample_records = []
        for idx in range(7):
            rec = {k: f"val-{idx}" for k in inputs.CLASSIFY_VARS}
            rec["_id"] = f"topic/i0{idx + 1}"
            rec["article_title"] = "Suomi"
            sample_records.append((555, rec))
        batches = inputs.batch_by_article(sample_records)
        self.assertEqual([b["_id"] for b in batches], ["555:1", "555:2"])
        self.assertEqual(batches[0]["candidate_count"], "6")
        self.assertEqual(batches[1]["candidate_count"], "1")

        # Stage-2 & refute builders
        base_rows = [r for _, r in sample_records[:3]]
        stage1_out = [
            {"id": "topic/i01", "model": "flash", "outcome": "ok", "value": _make_classification_value("vandalism", 5)},
            {"id": "topic/i02", "model": "flash", "outcome": "ok", "value": _make_classification_value("vandalism", 3)},
            {"id": "topic/i03", "model": "flash", "outcome": "ok", "value": _make_classification_value("pov_push", 4)},
        ]
        s2_in = inputs.build_stage2_inputs(base_rows, stage1_out)
        self.assertEqual([r["_id"] for r in s2_in], ["topic/i02", "topic/i03"])

        ref_in = inputs.build_refute_inputs(base_rows, stage1_out)
        self.assertEqual([r["_id"] for r in ref_in], ["topic/i03"])
        self.assertEqual(ref_in[0]["first_category"], "pov_push")
        self.assertEqual(ref_in[0]["first_confidence"], "4")

    def test_dev_split_filtering_ignores_test_topics(self) -> None:
        synthetic_golden = {
            "version": "1.0-synth",
            "dataset": "synth",
            "topics": [
                {
                    "id": "dev-topic",
                    "split": "dev",
                    "articles": [{"page_id": 10, "title": "Art A"}],
                    "items": [
                        {
                            "id": "dev-topic/i01",
                            "page_id": 10,
                            "inserting_rev_ids": [1],
                            "removing_rev_ids": [2],
                            "label": "contested",
                            "category": "pov_push",
                            "description": "secret",
                        }
                    ],
                },
                {
                    "id": "sealed-test-topic",
                    "split": "test",
                    "articles": [{"page_id": 20, "title": "Art B"}],
                    "items": [
                        {
                            "id": "sealed-test-topic/i01",
                            "page_id": 20,
                            "inserting_rev_ids": [3],
                            "removing_rev_ids": [4],
                            "label": "campaign",
                            "category": "false_factual_claim",
                            "description": "do not touch",
                        }
                    ],
                },
            ],
        }
        with tempfile.TemporaryDirectory() as tmp:
            gp = pathlib.Path(tmp) / "golden.json"
            gp.write_text(json.dumps(synthetic_golden), encoding="utf-8")
            dev_items, _ = inputs.load_dev_golden_items(gp)
            self.assertEqual([it["id"] for it in dev_items], ["dev-topic/i01"])
            self.assertFalse(GOLDEN_LEAK_KEYS & set(dev_items[0].keys()))

            g_loaded = score.load_dev_golden(gp)
            self.assertEqual(g_loaded["ordered_ids"], ["dev-topic/i01"])


class TestPromptsAndSchemas(unittest.TestCase):
    def test_variant_prompts_and_schemas_valid(self) -> None:
        prompts_dir = HERE / "prompts"
        expected = {
            "classify-edit-facts": ("Classify", {
                "article_title", "edit_timestamp", "editor_kind", "exposure",
                "edit_summary", "revert_summary", "removed_text", "inserted_text", "context", "facts",
            }),
            "classify-edit-batch": ("Classify", {
                "article_title", "candidate_count", "candidate_ids", "candidates",
            }),
            "refute-classification": ("Judge", {
                "article_title", "edit_timestamp", "editor_kind", "exposure",
                "first_category", "first_confidence", "first_what_changed_en", "first_claim_en",
                "edit_summary", "revert_summary", "removed_text", "inserted_text", "context",
            }),
        }
        placeholder_re = re.compile(r"\{\{\s*(data:)?([a-z0-9_]+)\s*\}\}")

        for pid, (expected_job, expected_vars) in expected.items():
            md_path = prompts_dir / f"{pid}.md"
            sc_path = prompts_dir / f"{pid}.schema.json"
            self.assertTrue(md_path.exists(), f"Missing {md_path}")
            self.assertTrue(sc_path.exists(), f"Missing {sc_path}")

            md_text = md_path.read_text(encoding="utf-8")
            self.assertTrue(md_text.startswith("---" + NL))
            self.assertIn(f"id: {pid}" + NL, md_text)
            self.assertIn(f"job: {expected_job}" + NL, md_text)
            self.assertIn("temperature: 0" + NL, md_text)
            self.assertIn(NL + "<<<system>>>" + NL, md_text)
            self.assertIn(NL + "<<<user>>>" + NL, md_text)

            found_vars = {m.group(2) for m in placeholder_re.finditer(md_text)}
            self.assertEqual(found_vars, expected_vars)

            schema = json.loads(sc_path.read_text(encoding="utf-8"))
            self.assertEqual(schema["type"], "object")
            self.assertIs(schema["additionalProperties"], False)
            self.assertEqual(set(schema["required"]), set(schema["properties"].keys()))


class TestScorer(unittest.TestCase):
    def setUp(self) -> None:
        self.golden_doc = {
            "version": "1.0-test",
            "dataset": "unit",
            "topics": [
                {
                    "id": "t1",
                    "split": "dev",
                    "items": [
                        {"id": "t1/i01", "page_id": 100, "label": "contested", "exclusion": None, "category": "pov_push"},
                        {"id": "t1/i02", "page_id": 100, "label": "campaign", "exclusion": None, "category": "false_factual_claim"},
                        {"id": "t1/n01", "page_id": 100, "label": "not-incident", "exclusion": "graffiti", "category": "vandalism"},
                    ],
                },
                {
                    "id": "t2",
                    "split": "dev",
                    "items": [
                        {"id": "t2/n01", "page_id": 200, "label": "not-incident", "exclusion": "good_faith", "category": "good_faith_error"},
                    ],
                },
                {
                    "id": "t_sealed",
                    "split": "test",
                    "items": [
                        {"id": "t_sealed/i01", "page_id": 999, "label": "contested", "exclusion": None, "category": "pov_push"},
                    ],
                },
            ],
        }
        self.input_rows = [
            {
                "_id": "t1/i01",
                "article_title": "A1",
                "edit_timestamp": "2020-01-01T00:00:00Z",
                "editor_kind": "anonymous",
                "exposure": "5 min",
                "edit_summary": "",
                "revert_summary": "rv pov",
                "removed_text": "",
                "inserted_text": "hyökkäyssota oli oikeutettu",
                "context": "ctx",
            },
            {
                "_id": "t1/i02",
                "article_title": "A1",
                "edit_timestamp": "2020-01-02T00:00:00Z",
                "editor_kind": "named",
                "exposure": "1 h",
                "edit_summary": "",
                "revert_summary": "väärä luku",
                "removed_text": "8 400",
                "inserted_text": "840",
                "context": "ctx",
            },
            {
                "_id": "t1/n01",
                "article_title": "A1",
                "edit_timestamp": "2020-01-03T00:00:00Z",
                "editor_kind": "anonymous",
                "exposure": "10 s",
                "edit_summary": "",
                "revert_summary": "sotkua",
                "removed_text": "",
                "inserted_text": "moi äiti asdf",
                "context": "ctx",
            },
            {
                "_id": "t2/n01",
                "article_title": "A2",
                "edit_timestamp": "2020-01-04T00:00:00Z",
                "editor_kind": "named",
                "exposure": "2 days",
                "edit_summary": "lisäys",
                "revert_summary": "lähteetön",
                "removed_text": "",
                "inserted_text": "uusi sivulause",
                "context": "ctx",
            },
        ]

    def test_variants_a_b_d_e_scoring_and_cli(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            tmp = pathlib.Path(tmpdir)
            golden_path = tmp / "golden.json"
            inputs_path = tmp / "inputs.jsonl"
            batch_inputs_path = tmp / "batch-inputs.jsonl"
            golden_path.write_text(json.dumps(self.golden_doc), encoding="utf-8")
            inputs.write_jsonl(inputs_path, self.input_rows)
            inputs.write_jsonl(
                batch_inputs_path,
                [
                    {"_id": "100:1", "candidate_ids": "t1/i01, t1/i02, t1/n01"},
                    {"_id": "200:1", "candidate_ids": "t2/n01"},
                ],
            )

            # Stage-1 fast run (Variant A flash): filters graffiti t1/n01, ALSO wrongly filters campaign t1/i02!
            stage1_rows = [
                {
                    "id": "t1/i01",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-3.8-flash",
                    "outcome": "ok",
                    "value": _make_classification_value("pov_push", 4, ["hyökkäyssota oli"]),
                    "from_cache": False,
                    "latency_ms": 400,
                    "input_tokens": 500,
                    "output_tokens": 80,
                    "thought_tokens": 20,
                    "cost_usd": 0.001,
                },
                {
                    "id": "t1/i02",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-3.8-flash",
                    "outcome": "ok",
                    "value": _make_classification_value("vandalism", 4, ["840"], claim_en=None),
                    "from_cache": False,
                    "latency_ms": 400,
                    "input_tokens": 500,
                    "output_tokens": 80,
                    "thought_tokens": 20,
                    "cost_usd": 0.001,
                },
                {
                    "id": "t1/n01",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-3.8-flash",
                    "outcome": "ok",
                    "value": _make_classification_value("vandalism", 5, ["moi äiti"], claim_en=None),
                    "from_cache": True,
                    "latency_ms": 5,
                    "input_tokens": 500,
                    "output_tokens": 60,
                    "thought_tokens": 10,
                    "cost_usd": 0.001,
                },
                {
                    "id": "t2/n01",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-3.8-flash",
                    "outcome": "ok",
                    "value": _make_classification_value("pov_push", 3, ["NOT_IN_INPUT"]),
                    "from_cache": False,
                    "latency_ms": 400,
                    "input_tokens": 500,
                    "output_tokens": 80,
                    "thought_tokens": 20,
                    "cost_usd": 0.001,
                },
            ]

            # Strong run (Variant A hydrogen): gets 3/4 right, over-calls t2/n01 as pov_push (which Variant E fixes)
            strong_rows = [
                {
                    "id": "t1/i01",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-4-hydrogen",
                    "outcome": "ok",
                    "value": _make_classification_value("pov_push", 5, ["hyökkäyssota oli oikeutettu"]),
                    "from_cache": False,
                    "latency_ms": 1000,
                    "input_tokens": 600,
                    "output_tokens": 100,
                    "thought_tokens": 50,
                    "cost_usd": 0.003,
                },
                {
                    "id": "t1/i02",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-4-hydrogen",
                    "outcome": "ok",
                    "value": _make_classification_value("false_factual_claim", 5, ["8 400", "840"]),
                    "from_cache": False,
                    "latency_ms": 1000,
                    "input_tokens": 600,
                    "output_tokens": 100,
                    "thought_tokens": 50,
                    "cost_usd": 0.003,
                },
                {
                    "id": "t1/n01",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-4-hydrogen",
                    "outcome": "ok",
                    "value": _make_classification_value("vandalism", 5, ["moi äiti asdf"], claim_en=None),
                    "from_cache": False,
                    "latency_ms": 1000,
                    "input_tokens": 600,
                    "output_tokens": 100,
                    "thought_tokens": 50,
                    "cost_usd": 0.003,
                },
                {
                    "id": "t2/n01",
                    "prompt": "classify-edit",
                    "prompt_version": "1.aaaa1111",
                    "model": "gemini-4-hydrogen",
                    "outcome": "ok",
                    "value": _make_classification_value("pov_push", 3, ["uusi sivulause"]),
                    "from_cache": False,
                    "latency_ms": 1000,
                    "input_tokens": 600,
                    "output_tokens": 100,
                    "thought_tokens": 50,
                    "cost_usd": 0.003,
                },
            ]

            # Variant D batch-by-article run: 100:1 omits t1/n01 -> marked invalid; 200:1 ok
            variant_d_rows = [
                {
                    "id": "100:1",
                    "prompt": "classify-edit-batch",
                    "prompt_version": "1.bbbb2222",
                    "model": "gemini-4-hydrogen",
                    "outcome": "ok",
                    "value": {
                        "items": [
                            {"candidate_id": "t1/i01", **_make_classification_value("pov_push", 4, ["hyökkäyssota"])},
                            {"candidate_id": "t1/i02", **_make_classification_value("false_factual_claim", 4, ["840"])},
                        ]
                    },
                    "from_cache": False,
                    "latency_ms": 1500,
                    "input_tokens": 1200,
                    "output_tokens": 300,
                    "thought_tokens": 90,
                    "cost_usd": 0.006,
                },
                {
                    "id": "200:1",
                    "prompt": "classify-edit-batch",
                    "prompt_version": "1.bbbb2222",
                    "model": "gemini-4-hydrogen",
                    "outcome": "ok",
                    "value": {
                        "items": [
                            {"candidate_id": "t2/n01", **_make_classification_value("good_faith_error", 4, ["uusi sivulause"], claim_en=None)}
                        ]
                    },
                    "from_cache": False,
                    "latency_ms": 700,
                    "input_tokens": 600,
                    "output_tokens": 120,
                    "thought_tokens": 30,
                    "cost_usd": 0.003,
                },
            ]

            # Variant E refute run on strong_rows: upholds t1/i01, downgrades t2/n01 -> good_faith_error
            refute_rows = [
                {
                    "id": "t1/i01",
                    "prompt": "refute-classification",
                    "prompt_version": "1.eeee5555",
                    "model": "gemini-4-carbon",
                    "outcome": "ok",
                    "value": {
                        "ordinary_reading_en": "Attempted copy-edit of wartime terminology.",
                        "verdict": "upheld",
                        "corrected_category": "pov_push",
                        "confidence": 5,
                        "evidence": ["hyökkäyssota oli oikeutettu"],
                        "reason_en": "Explicitly one-sided justification.",
                    },
                    "from_cache": False,
                    "latency_ms": 1200,
                    "input_tokens": 700,
                    "output_tokens": 90,
                    "thought_tokens": 60,
                    "cost_usd": 0.004,
                },
                {
                    "id": "t2/n01",
                    "prompt": "refute-classification",
                    "prompt_version": "1.eeee5555",
                    "model": "gemini-4-carbon",
                    "outcome": "ok",
                    "value": {
                        "ordinary_reading_en": "Unsourced clause added in good faith.",
                        "verdict": "downgraded",
                        "corrected_category": "good_faith_error",
                        "confidence": 4,
                        "evidence": ["lähteetön"],
                        "reason_en": "Reverted purely as unsourced without ideological slant.",
                    },
                    "from_cache": False,
                    "latency_ms": 1200,
                    "input_tokens": 700,
                    "output_tokens": 90,
                    "thought_tokens": 60,
                    "cost_usd": 0.004,
                },
            ]

            p_s1 = tmp / "s1.jsonl"
            p_strong = tmp / "strong.jsonl"
            p_batch = tmp / "batch.jsonl"
            p_refute = tmp / "refute.jsonl"
            out_md = tmp / "scorecard.md"
            out_json = tmp / "scorecard.json"
            inputs.write_jsonl(p_s1, stage1_rows)
            inputs.write_jsonl(p_strong, strong_rows)
            inputs.write_jsonl(p_batch, variant_d_rows)
            inputs.write_jsonl(p_refute, refute_rows)

            buf = io.StringIO()
            with redirect_stdout(buf):
                rc = score.main(
                    [
                        str(p_strong),
                        str(p_s1),
                        str(p_batch),
                        "--stage1",
                        str(p_s1),
                        "--stage2",
                        str(p_strong),
                        "--refute",
                        str(p_refute),
                        "--refute-base",
                        str(p_strong),
                        "--golden",
                        str(golden_path),
                        "--inputs",
                        str(inputs_path),
                        "--batch-inputs",
                        str(batch_inputs_path),
                        "--out-md",
                        str(out_md),
                        "--out-json",
                        str(out_json),
                    ]
                )
            self.assertEqual(rc, 0)
            self.assertIn("P1.S9 Classification Scorecard", buf.getvalue())
            report = json.loads(out_json.read_text(encoding="utf-8"))
            self.assertEqual(report["dev_items"], 4)

            runs = {(r["prompt"], r["model"]): r for r in report["runs"]}

            # 1. Stage-1 flash check: cache_hits=1, graffiti filtered 1/1, contested/campaign wrongly filtered = 1 (t1/i02), 3/4 verbatim spans
            r_flash = runs[("classify-edit", "gemini-3.8-flash")]
            self.assertEqual(r_flash["efficiency"]["cache_hits"], 1)
            self.assertEqual(r_flash["graffiti_filter"]["graffiti_not_incidents_filtered"], 1)
            self.assertEqual(r_flash["graffiti_filter"]["contested_campaign_wrongly_filtered"], 1)
            self.assertEqual(r_flash["graffiti_filter"]["contested_campaign_wrongly_filtered_ids"], ["t1/i02"])
            self.assertEqual(r_flash["evidence_verbatim"]["spans_exact"], 3)
            self.assertEqual(r_flash["evidence_verbatim"]["spans_total"], 4)

            # 2. Variant B two-stage: 2 items kept at stage 1 (t1/i02, t1/n01 -> 1 call each) + 2 routed to stage 2 (2 calls each) = 6 total calls
            r_b = runs[("classify-edit-two-stage", "gemini-3.8-flash -> gemini-4-hydrogen")]
            self.assertEqual(r_b["variant_meta"], {"stage1_filtered": 2, "stage2_routed": 2})
            self.assertAlmostEqual(r_b["efficiency"]["total_calls"], 6.0)
            self.assertEqual(r_b["graffiti_filter"]["contested_campaign_wrongly_filtered"], 1)

            # 3. Variant D batch-by-article: 2 batch calls across 4 items = 0.50 calls/item; missing t1/n01 -> invalid
            r_d = runs[("classify-edit-batch", "gemini-4-hydrogen")]
            self.assertEqual(r_d["outcomes"], {"ok": 3, "invalid": 1})
            self.assertEqual(r_d["category_accuracy"]["exact_hits"], 3)
            self.assertAlmostEqual(r_d["efficiency"]["total_calls"], 2.0)
            self.assertAlmostEqual(r_d["efficiency"]["mean_calls_per_item"], 0.5)

            # 4. Variant E refute: t2/n01 downgraded from pov_push -> good_faith_error, lifting accuracy from 3/4 (75%) to 4/4 (100%)
            r_e = runs[("classify-edit+refute", "gemini-4-hydrogen + gemini-4-carbon")]
            self.assertEqual(
                r_e["variant_meta"],
                {"refute_checked": 2, "refute_upheld": 1, "refute_downgraded": 1, "refute_non_ok": 0},
            )
            self.assertAlmostEqual(r_e["category_accuracy"]["accuracy_all"], 1.0)
            self.assertAlmostEqual(r_e["efficiency"]["total_calls"], 6.0)
            self.assertEqual(r_e["evidence_verbatim"]["spans_exact"], 5)


if __name__ == "__main__":
    unittest.main()
