#!/usr/bin/env python3
"""P1.S10 (#37): Export top dev-split candidate clusters per topic in `classify-edit` JSONL format.

Usage:
    python3 research/37-s10-assembly/export_classify.py --per-topic 60

Reads ONLY `split == "dev"` topics from `research/eval/golden.json` (never touches test split),
loads S7 `fuzzy` claim clusters per topic, filters structural exclusions via `tiers.assess`,
ranks by Variant A's additive score, takes top `--per-topic N` per topic (deduplicated by `_id` =
incident key), and builds `classify-edit` prompt input rows via S9's `assemble_item_record`.
"""
from __future__ import annotations

import argparse
import base64
from collections import defaultdict
import importlib.util
import json
import math
import pathlib
import sys
import zlib
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
RESEARCH_DIR = HERE.parent
REPO_ROOT = HERE.parents[1]
sys.path.insert(0, str(RESEARCH_DIR / "lib"))
sys.path.insert(0, str(RESEARCH_DIR / "eval"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
from evidence import (  # noqa: E402
    CandidateCluster,
    load_candidates_for_pages,
    sql_query,
)
from tiers import (  # noqa: E402
    CLAIM_CATEGORIES,
    GRAFFITI_CATEGORIES,
    ContentCategory,
    IncidentEvidence,
    RemovalReason,
    assess as assess_tier,
)


def _load_s9_inputs():
    spec = importlib.util.spec_from_file_location("s9_inputs", RESEARCH_DIR / "36-s9-classify" / "inputs.py")
    if spec is None or spec.loader is None:
        raise RuntimeError("Cannot load research/36-s9-classify/inputs.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


_s9_inputs = _load_s9_inputs()
assemble_item_record = _s9_inputs.assemble_item_record
format_facts = _s9_inputs.format_facts
parse_db_timestamp = _s9_inputs.parse_db_timestamp
write_jsonl = _s9_inputs.write_jsonl

NL = chr(10)
DISPUTE_REMOVAL_REASONS = frozenset({RemovalReason.ACCURACY, RemovalReason.NEUTRALITY, RemovalReason.SOURCING})


def score_variant_a(
    ev: IncidentEvidence,
    prose_chars: int = 0,
    total_exposure_seconds: int = 0,
) -> float:
    """Variant A hand-weighted additive score over `IncidentEvidence`, prose length, and exposure.

    Terms (guaranteed strictly positive, `>= 0.1`):
      - Base offset: `+2.0`
      - Substantiveness: `+0.6 * log1p(min(prose_chars, 4000))`
      - Exposure: `+0.25 * log1p(min(total_exposure_seconds, 365d) / 60)` (minutes live, capped at 1y)
      - Removal reason (S5): `+2.5` for accuracy/neutrality/sourcing, `+0.5` for other, `-1.5` for vandalism
      - Re-insertions (S7): `+2.2 * log1p(reinsertion_count)`
      - Edit war (S4): `+1.8` if `in_edit_war`
      - Dispute template (S8): `+1.5` if `dispute_template_added`
      - Distinct editors (S7): `+1.8 * log2(distinct_inserting_editors)`
      - Distinct articles (S7): `+2.0 * log2(distinct_articles)`
      - Persistence span (S7): `+0.5 * log1p(span_days)` (`+1.0` bonus if `reinsertion_count >= 2` and `span_days >= 7`)
      - Source manipulation (S8/S9): `+1.5` if `source_manipulation`
      - Content assessment (S9, when present):
          * claim category (`content_dispute | pov_push | false_factual_claim | source_manipulation`):
            `+3.0 * (confidence / 5)` + (`+1.0` if `has_verified_quote`)
          * `good_faith_error | other`: `-0.5 * (confidence / 5)`
          * low-confidence `vandalism | test_or_nonsense`: `-2.5 * (confidence / 5)`
    """
    score = 2.0
    score += 0.6 * math.log1p(min(max(0, int(prose_chars)), 4000))
    exposure_min = min(max(0, int(total_exposure_seconds)), 86400 * 365) / 60.0
    score += 0.25 * math.log1p(exposure_min)

    if ev.removal_reason in DISPUTE_REMOVAL_REASONS:
        score += 2.5
    elif ev.removal_reason == RemovalReason.OTHER:
        score += 0.5
    elif ev.removal_reason == RemovalReason.VANDALISM:
        score -= 1.5

    if ev.reinsertion_count > 0:
        score += 2.2 * math.log1p(ev.reinsertion_count)

    if ev.in_edit_war:
        score += 1.8

    if ev.dispute_template_added:
        score += 1.5

    if ev.distinct_inserting_editors > 1:
        score += 1.8 * math.log2(ev.distinct_inserting_editors)

    if ev.distinct_articles > 1:
        score += 2.0 * math.log2(ev.distinct_articles)

    span_days = max(0.0, ev.insertion_span.total_seconds() / 86400.0)
    if span_days > 0.0:
        score += 0.5 * math.log1p(span_days)
    if ev.reinsertion_count >= 2 and span_days >= 7.0:
        score += 1.0

    if ev.source_manipulation:
        score += 1.5

    if ev.assessment is not None:
        conf_scale = max(1, min(5, ev.assessment.confidence)) / 5.0
        if ev.assessment.category in CLAIM_CATEGORIES:
            score += 3.0 * conf_scale
            if ev.assessment.has_verified_quote:
                score += 1.0
        elif ev.assessment.category in (ContentCategory.GOOD_FAITH_ERROR, ContentCategory.OTHER):
            score -= 0.5 * conf_scale
        elif ev.assessment.category in GRAFFITI_CATEGORIES:
            score -= 2.5 * conf_scale

    return max(0.1, score)


def _article_bucket_for_topic(cand: CandidateCluster, topic_pages: set[int]) -> int:
    if cand.page_id in topic_pages:
        return cand.page_id
    for pid in cand.other_page_ids:
        if pid in topic_pages:
            return pid
    return cand.page_id


def select_top_for_topic(
    candidates: list[CandidateCluster],
    topic_page_ids: list[int],
    per_topic: int = 60,
    primary_cap_per_article: int = 5,
) -> list[CandidateCluster]:
    """Rank a topic's non-excluded clusters by Variant A and select up to `per_topic` candidates.

    Pass 1 selects with `primary_cap_per_article=5` (matching `variant.py`'s default `--cap-per-article 5`
    so every incident Variant A shows in top-50 is covered), and Pass 2 backfills remaining slots up to
    `per_topic` in Variant A score order so central high-traffic articles have candidates classified when
    Gemini excludes graffiti from their top-5 slots.
    """
    tpages = {int(p) for p in topic_page_ids}
    scored: list[tuple[float, int, int, CandidateCluster]] = []
    for cand in candidates:
        if not (cand.member_page_ids & tpages):
            continue
        ta = assess_tier(cand.evidence)
        if not ta.is_incident:
            continue
        sc = score_variant_a(cand.evidence, cand.prose_chars, cand.total_exposure_seconds)
        scored.append((sc, cand.prose_chars, cand.removing_rev_ids[-1], cand))

    scored.sort(key=lambda x: (-x[0], -x[1], -x[2]))

    selected: list[CandidateCluster] = []
    selected_keys: set[str] = set()
    counts: dict[int, int] = defaultdict(int)

    # Pass 1: fill up to min(50, per_topic) using Variant A's default per-article cap (5)
    pass1_limit = min(50, per_topic) if primary_cap_per_article > 0 else per_topic
    for _, _, _, cand in scored:
        if len(selected) >= pass1_limit:
            break
        bucket = _article_bucket_for_topic(cand, tpages)
        if primary_cap_per_article > 0 and counts[bucket] >= primary_cap_per_article:
            continue
        counts[bucket] += 1
        selected.append(cand)
        selected_keys.add(cand.incident_key)

    # Pass 2: backfill remaining slots up to per_topic in pure Variant A score order
    if len(selected) < per_topic:
        for _, _, _, cand in scored:
            if len(selected) >= per_topic:
                break
            if cand.incident_key in selected_keys:
                continue
            selected.append(cand)
            selected_keys.add(cand.incident_key)

    return selected


def _fetch_compressed_texts_pairs(pairs: list[tuple[int, int]]) -> dict[int, str]:
    """PK-only (`page_id` AND `rev_id`) compressed revision_text fetch in batches of 25 pairs."""
    out: dict[int, str] = {}
    unique_pairs = sorted({(int(p), int(r)) for p, r in pairs})
    for start in range(0, len(unique_pairs), 25):
        chunk = unique_pairs[start : start + 25]
        cond = " OR ".join(f"(page_id = {p} AND rev_id = {r})" for p, r in chunk)
        rows = sql_query(
            f"SELECT rev_id, TO_BASE64(COMPRESS(text)) AS bz "
            f"FROM corpus.revision_text "
            f"WHERE {cond}"
        )
        for row in rows:
            rid = int(row["rev_id"])
            bz = (row.get("bz") or "").replace(NL, "").strip()
            if not bz:
                out[rid] = ""
                continue
            raw = base64.b64decode(bz)
            out[rid] = zlib.decompress(raw[4:]).decode("utf-8", "replace") if len(raw) > 4 else ""
    return out


def generate_from_corpus(
    items: list[dict[str, Any]],
    fallback_titles: dict[int, str] | None = None,
    include_facts: bool = False,
) -> tuple[list[tuple[int, dict[str, str]]], dict[str, Any]]:
    """Adapted from S9 `inputs.generate_from_corpus`: assemble `classify-edit` records for candidate items."""
    page_ids = sorted({int(it["page_id"]) for it in items} | {int(p) for it in items for p in it["other_page_ids"]})
    ins_ids = sorted({int(r) for it in items for r in it["inserting_rev_ids"]})
    rem_ids = sorted({int(r) for it in items for r in it["removing_rev_ids"]})
    all_rev_ids = sorted(set(ins_ids) | set(rem_ids))

    titles_by_page = dict(fallback_titles or {})
    if page_ids:
        for r in sql_query(
            f"SELECT page_id, title FROM corpus.article WHERE lang = 'fi' AND page_id IN ({aot.ints(page_ids)})"
        ):
            titles_by_page[int(r["page_id"])] = str(r["title"])

    rev_by_id: dict[int, dict[str, Any]] = {}
    if all_rev_ids and page_ids:
        pages_sql = aot.ints(page_ids)
        for start in range(0, len(all_rev_ids), 800):
            batch = all_rev_ids[start : start + 800]
            rows = sql_query(
                f"SELECT page_id, rev_id, parent_id, title, ts, user, anonymous, comment "
                f"FROM corpus.revision "
                f"WHERE page_id IN ({pages_sql}) AND rev_id IN ({aot.ints(batch)})"
            )
            for r in rows:
                rid = int(r["rev_id"])
                rev_by_id[rid] = r
                titles_by_page.setdefault(int(r["page_id"]), str(r["title"]))

    s3_by_rev: dict[int, dict[str, Any]] = {}
    if ins_ids:
        for start in range(0, len(ins_ids), 350):
            batch = ins_ids[start : start + 350]
            rows = sql_query(
                f"SELECT rev_id, page_id, old_rev_id, new_rev_id, segments, error "
                f"FROM research.s3_diff "
                f"WHERE kind = 'edit' AND rev_id IN ({aot.ints(batch)})"
            )
            for r in rows:
                s3_by_rev[int(r["rev_id"])] = r

    parent_ids: set[int] = set()
    need_text_pairs: list[tuple[int, int]] = []
    for it in items:
        item_ins = sorted(
            (rev_by_id[r] for r in it["inserting_rev_ids"] if r in rev_by_id),
            key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
        )
        if not item_ins:
            continue
        first_ins = item_ins[0]
        first_rid = int(first_ins["rev_id"])
        first_page = int(first_ins["page_id"])
        s3_first = s3_by_rev.get(first_rid)
        if s3_first is not None:
            p_rid = int(s3_first["old_rev_id"]) if s3_first.get("old_rev_id") is not None else None
        else:
            p_rid = int(first_ins["parent_id"]) if first_ins.get("parent_id") is not None else None

        if p_rid is not None:
            parent_ids.add(p_rid)
            need_text_pairs.append((first_page, p_rid))
            has_removed_seg = False
            if s3_first and s3_first.get("segments") and not s3_first.get("error"):
                for seg in json.loads(s3_first["segments"]):
                    if not seg.get("moved") and seg.get("op") == "removed" and (seg.get("text") or "").strip():
                        has_removed_seg = True
                        break
            if not has_removed_seg:
                need_text_pairs.append((first_page, first_rid))

    extra_parents = sorted(parent_ids - set(rev_by_id))
    if extra_parents and page_ids:
        pages_sql = aot.ints(page_ids)
        for start in range(0, len(extra_parents), 800):
            batch = extra_parents[start : start + 800]
            for r in sql_query(
                f"SELECT page_id, rev_id, parent_id, title, ts, user, anonymous, comment "
                f"FROM corpus.revision "
                f"WHERE page_id IN ({pages_sql}) AND rev_id IN ({aot.ints(batch)})"
            ):
                rev_by_id[int(r["rev_id"])] = r

    texts_by_rev = _fetch_compressed_texts_pairs(need_text_pairs)

    s5_revert_by_rev: dict[int, dict[str, Any]] = {}
    s5_summary_by_rev: dict[int, dict[str, Any]] = {}
    s4_episodes_by_rev: dict[int, list[dict[str, Any]]] = defaultdict(list)
    s6_by_rev: dict[int, dict[str, Any]] = {}

    if include_facts:
        if rem_ids:
            for r in sql_query(
                f"SELECT reverting_rev_id, removal_reasons, auto_family "
                f"FROM research.s5_revert_reason "
                f"WHERE reverting_rev_id IN ({aot.ints(rem_ids)})"
            ):
                s5_revert_by_rev[int(r["reverting_rev_id"])] = r
        if all_rev_ids:
            for r in sql_query(
                f"SELECT rev_id, auto_family, reasons, insert_flags "
                f"FROM research.s5_summary "
                f"WHERE rev_id IN ({aot.ints(all_rev_ids)})"
            ):
                s5_summary_by_rev[int(r["rev_id"])] = r
            for r in sql_query(
                f"SELECT er.rev_id, e.episode_id, e.kind, e.revert_count, e.named_participants, e.protection_hint "
                f"FROM research.s4_episode_rev er "
                f"JOIN research.s4_episode e USING (episode_id) "
                f"WHERE er.rev_id IN ({aot.ints(all_rev_ids)}) AND e.is_default = 1"
            ):
                s4_episodes_by_rev[int(r["rev_id"])].append(r)
        if ins_ids:
            for r in sql_query(
                f"SELECT STRAIGHT_JOIN ra.rev_id, ep.kind, ep.is_bot, ep.edits, ep.distinct_pages, "
                f"ep.reverted_edits_by_others, ep.single_purpose, ep.throwaway "
                f"FROM research.s6_rev_author ra USE INDEX (PRIMARY) "
                f"JOIN research.s6_editor_profile ep ON ep.user = ra.user "
                f"WHERE ra.rev_id IN ({aot.ints(ins_ids)})"
            ):
                s6_by_rev[int(r["rev_id"])] = r

    records_with_pages: list[tuple[int, dict[str, str]]] = []
    lacked_diff_items: list[str] = []
    missing_diff_revs_all: set[int] = set()

    for it in items:
        pid = int(it["page_id"])
        ins_revs_sorted = sorted(
            (rev_by_id[r] for r in it["inserting_rev_ids"] if r in rev_by_id),
            key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
        )
        rem_revs_sorted = sorted(
            (rev_by_id[r] for r in it["removing_rev_ids"] if r in rev_by_id),
            key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
        )
        facts_str = (
            format_facts(
                ins_revs=ins_revs_sorted,
                rem_revs=rem_revs_sorted,
                s5_revert_by_rev=s5_revert_by_rev,
                s5_summary_by_rev=s5_summary_by_rev,
                s4_episodes_by_rev=s4_episodes_by_rev,
                s6_by_rev=s6_by_rev,
            )
            if include_facts
            else None
        )
        rec, diag = assemble_item_record(
            item=it,
            article_title=titles_by_page.get(pid, f"page:{pid}"),
            rev_by_id=rev_by_id,
            s3_by_rev=s3_by_rev,
            texts_by_rev=texts_by_rev,
            include_facts=include_facts,
            facts_text=facts_str,
        )
        records_with_pages.append((pid, rec))
        if diag["lacked_diff"]:
            lacked_diff_items.append(str(diag["id"]))
            missing_diff_revs_all.update(diag["missing_diff_revs"])

    stats = {
        "candidates": len(items),
        "total_inserting_revs": len(ins_ids),
        "lacked_diff_candidates": len(lacked_diff_items),
        "lacked_diff_ids": lacked_diff_items,
        "missing_diff_revs": len(missing_diff_revs_all),
        "sql_queries": aot.STATS["sql_queries"],
        "sql_seconds": round(aot.STATS["sql_seconds"], 2),
    }
    return records_with_pages, stats



def _check_split(split: str) -> None:
    """The test split is sealed (CONTRACT.md §6): only the S11 decision (#38) may read it, with EVAL_ALLOW_TEST=1."""
    import os
    if split == "test" and os.environ.get("EVAL_ALLOW_TEST") != "1":
        raise SystemExit("the test split is sealed: set EVAL_ALLOW_TEST=1 (decision issues R6/S11 only)")


def load_dev_topics(golden_path: pathlib.Path, split: str = "dev") -> tuple[list[dict[str, Any]], dict[int, str]]:
    """Read the topics of one split (default dev) with articles from `golden.json`: article lists and text only, never item labels."""
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    topics: list[dict[str, Any]] = []
    fallback_titles: dict[int, str] = {}
    for topic in doc.get("topics") or ():
        if topic.get("split") != split:
            continue
        articles = topic.get("articles") or ()
        if not articles:
            continue
        pids: list[int] = []
        for art in articles:
            pid = int(art["page_id"])
            pids.append(pid)
            fallback_titles.setdefault(pid, str(art["title"]))
        topics.append({"id": str(topic["id"]), "kind": str(topic["kind"]), "page_ids": pids})
    return topics, fallback_titles


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--per-topic", type=int, default=60, help="Top Variant-A candidates per dev topic (default: 60).")
    ap.add_argument(
        "--golden",
        type=pathlib.Path,
        default=REPO_ROOT / "research/eval/golden.json",
        help="Path to golden.json (only split='dev' topics are read).",
    )
    ap.add_argument(
        "--out",
        type=pathlib.Path,
        default=HERE / "results/candidates.classify.jsonl",
        help="Output JSONL path.",
    )
    ap.add_argument("--facts", action="store_true", help="Optionally include S9 `facts` variable.")
    ap.add_argument("--split", choices=("dev", "test"), default="dev", help="test needs EVAL_ALLOW_TEST=1 (S11 only).")
    args = ap.parse_args(argv)
    _check_split(args.split)

    dev_topics, fallback_titles = load_dev_topics(args.golden, args.split)
    all_dev_pages = sorted({pid for t in dev_topics for pid in t["page_ids"]})
    all_candidates, load_stats = load_candidates_for_pages(
        all_dev_pages,
        assessments_by_key=None,
        method="fuzzy",
        include_edit_diffs_for_assessed=False,
    )

    items: list[dict[str, Any]] = []
    seen_keys: set[str] = set()
    per_topic_counts: list[str] = []

    for topic in dev_topics:
        top_cands = select_top_for_topic(
            all_candidates,
            topic["page_ids"],
            per_topic=args.per_topic,
        )
        added_new = 0
        for cand in top_cands:
            if cand.incident_key in seen_keys:
                continue
            seen_keys.add(cand.incident_key)
            added_new += 1
            items.append(
                {
                    "id": cand.incident_key,
                    "page_id": cand.page_id,
                    "other_page_ids": list(cand.other_page_ids),
                    "inserting_rev_ids": list(cand.inserting_rev_ids),
                    "removing_rev_ids": list(cand.removing_rev_ids),
                }
            )
        per_topic_counts.append(f"{topic['id']}={len(top_cands)} (+{added_new} new)")

    records_with_pages, gen_stats = generate_from_corpus(
        items=items,
        fallback_titles=fallback_titles,
        include_facts=args.facts,
    )
    out_rows = [rec for _, rec in records_with_pages]
    write_jsonl(args.out, out_rows)

    summary = (
        f"exported {len(out_rows)} unique candidates across {len(dev_topics)} dev topics "
        f"(per_topic={args.per_topic}; span missing_s3_diffs={load_stats['missing_s3_diffs']}; "
        f"edit lacked_diff={gen_stats['lacked_diff_candidates']} candidates / "
        f"{gen_stats['missing_diff_revs']} inserting revs; "
        f"{gen_stats['sql_queries']} SQL queries in {gen_stats['sql_seconds']}s) -> {args.out}"
    )
    print(summary)
    aot.log("per-topic:", ", ".join(per_topic_counts))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
