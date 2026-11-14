#!/usr/bin/env python3
"""P1.S9 (#36): Score Gemini edit-classification batch outputs against the golden dev split.

Usage:
  # Score one or more single-item / batch-by-article runs (Markdown on stdout, or --json):
  python3 research/36-s9-classify/score.py results/a.jsonl results/c.jsonl results/d.jsonl 
      --inputs research/36-s9-classify/results/golden-dev.classify.jsonl

  # Variant B (two-stage: fast stage-1 graffiti filter + strong stage-2):
  python3 research/36-s9-classify/score.py 
      --stage1 results/a-flash.jsonl --stage2 results/a-hydrogen.jsonl 
      --inputs research/36-s9-classify/results/golden-dev.classify.jsonl

  # Variant E (first-pass classify + second-pass refute-classification):
  python3 research/36-s9-classify/score.py results/a-hydrogen.jsonl 
      --refute results/e-refute.jsonl 
      --inputs research/36-s9-classify/results/golden-dev.classify.jsonl

Both `--stage1`/`--stage2` and `--refute`/`--refute-base` accept `<path>` or `<path>:<model>` when a
batch JSONL contains multiple models from `--model a,b`.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import copy
import json
import pathlib
import statistics
import sys
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]

NL = chr(10)

CATEGORIES = (
    "vandalism",
    "test_or_nonsense",
    "good_faith_error",
    "content_dispute",
    "pov_push",
    "false_factual_claim",
    "source_manipulation",
    "other",
)
CAT_SHORT = {
    "vandalism": "vand",
    "test_or_nonsense": "test",
    "good_faith_error": "gfe",
    "content_dispute": "disp",
    "pov_push": "pov",
    "false_factual_claim": "false",
    "source_manipulation": "src",
    "other": "other",
}
COARSE_BUCKET = {
    "vandalism": "noise",
    "test_or_nonsense": "noise",
    "good_faith_error": "benign",
    "content_dispute": "benign",
    "other": "benign",
    "pov_push": "narrative",
    "false_factual_claim": "narrative",
    "source_manipulation": "narrative",
}
GRAFFITI_CATS = frozenset({"vandalism", "test_or_nonsense"})
INCIDENT_LABELS = frozenset({"reverted", "contested", "campaign"})
STRICT_INCIDENT_LABELS = frozenset({"contested", "campaign"})
EVIDENCE_FIELDS = ("removed_text", "inserted_text", "edit_summary", "revert_summary", "context")
MAX_BATCH_ITEMS = 6


def load_jsonl(path: pathlib.Path) -> list[dict[str, Any]]:
    with path.open(encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def load_dev_golden(golden_path: pathlib.Path) -> dict[str, Any]:
    """Load ONLY `split == 'dev'` topics and items from `golden.json`."""
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    items_by_id: dict[str, dict[str, Any]] = {}
    ordered_ids: list[str] = []
    expected_batches: dict[str, list[str]] = {}
    by_page: dict[int, list[str]] = {}
    page_order: list[int] = []
    topics_with_items: list[str] = []

    for topic in doc.get("topics") or ():
        if topic.get("split") != "dev":
            continue
        t_items = topic.get("items") or []
        if t_items:
            topics_with_items.append(str(topic["id"]))
        for it in t_items:
            iid = str(it["id"])
            pid = int(it["page_id"])
            ordered_ids.append(iid)
            items_by_id[iid] = {
                "id": iid,
                "topic_id": str(topic["id"]),
                "page_id": pid,
                "label": str(it["label"]),
                "exclusion": it.get("exclusion"),
                "category": it.get("category"),
            }
            if pid not in by_page:
                by_page[pid] = []
                page_order.append(pid)
            by_page[pid].append(iid)

    for pid in page_order:
        cids = by_page[pid]
        for idx, start in enumerate(range(0, len(cids), MAX_BATCH_ITEMS), 1):
            expected_batches[f"{pid}:{idx}"] = cids[start : start + MAX_BATCH_ITEMS]

    return {
        "golden_version": str(doc.get("version") or ""),
        "dataset": str(doc.get("dataset") or ""),
        "dev_topics_with_items": topics_with_items,
        "ordered_ids": ordered_ids,
        "items_by_id": items_by_id,
        "expected_batches": expected_batches,
    }


def load_inputs_map(inputs_path: pathlib.Path | None) -> dict[str, dict[str, str]]:
    if inputs_path is None or not inputs_path.exists():
        return {}
    out: dict[str, dict[str, str]] = {}
    for row in load_jsonl(inputs_path):
        rid = row.get("_id") or row.get("id")
        if rid:
            out[str(rid)] = {str(k): str(v) if v is not None else "" for k, v in row.items()}
    return out


def load_batch_mapping(batch_inputs_path: pathlib.Path | None, fallback: dict[str, list[str]]) -> dict[str, list[str]]:
    mapping = dict(fallback)
    if batch_inputs_path and batch_inputs_path.exists():
        for row in load_jsonl(batch_inputs_path):
            bid = row.get("_id") or row.get("id")
            raw_ids = row.get("candidate_ids") or ""
            cids = [c.strip() for c in str(raw_ids).split(",") if c.strip()]
            if bid and cids:
                mapping[str(bid)] = cids
    return mapping


def _norm_ws_case(text: str) -> str:
    return " ".join(text.split()).casefold()


def check_evidence_spans(
    evidence: list[Any] | None,
    input_rec: dict[str, str] | None,
) -> dict[str, Any]:
    """Check whether quoted `evidence` spans appear verbatim in the candidate's input fields."""
    spans = [str(s) for s in (evidence or ()) if isinstance(s, str) and s.strip()]
    if not input_rec:
        return {
            "checked": False,
            "total": len(spans),
            "exact": 0,
            "normalised": 0,
            "all_exact": False,
            "all_normalised": False,
        }

    fields = [input_rec.get(f) or "" for f in EVIDENCE_FIELDS]
    joined_norm = _norm_ws_case(NL.join(fields))

    exact_hits = 0
    norm_hits = 0
    for sp in spans:
        is_exact = any(sp in fld for fld in fields)
        if is_exact:
            exact_hits += 1
            norm_hits += 1
        else:
            ns = _norm_ws_case(sp)
            if ns and ns in joined_norm:
                norm_hits += 1

    return {
        "checked": True,
        "total": len(spans),
        "exact": exact_hits,
        "normalised": norm_hits,
        "all_exact": len(spans) > 0 and exact_hits == len(spans),
        "all_normalised": len(spans) > 0 and norm_hits == len(spans),
    }


def _is_cache_hit(row: dict[str, Any] | None) -> bool:
    return bool(row and (row.get("from_cache") or row.get("cache_hit")))


def is_graffiti_filtered(row: dict[str, Any] | None) -> bool:
    if not row or str(row.get("outcome") or "").lower() != "ok":
        return False
    val = row.get("value") or {}
    return val.get("category") in GRAFFITI_CATS and int(val.get("confidence") or 0) >= 4


def split_batch_rows(
    rows: list[dict[str, Any]],
    expected_batches: dict[str, list[str]],
) -> list[dict[str, Any]]:
    """Expand `classify-edit-batch` rows (`<page_id>:<n>`) into item-level rows with 1/m cost & tokens."""
    expanded: list[dict[str, Any]] = []
    for row in rows:
        prompt = str(row.get("prompt") or "")
        val = row.get("value")
        is_batch = prompt == "classify-edit-batch" or (
            isinstance(val, dict) and isinstance(val.get("items"), list)
        )
        if not is_batch:
            r_copy = copy.deepcopy(row)
            r_copy.setdefault("calls", 1.0)
            expanded.append(r_copy)
            continue

        bid = str(row.get("id") or "")
        outcome = str(row.get("outcome") or "failed").lower()
        items_list: list[dict[str, Any]] = []
        if outcome == "ok":
            if isinstance(val, dict) and isinstance(val.get("items"), list):
                items_list = [x for x in val["items"] if isinstance(x, dict)]
            elif isinstance(val, list):
                items_list = [x for x in val if isinstance(x, dict)]

        by_cid: dict[str, dict[str, Any]] = {}
        for entry in items_list:
            cid = entry.get("candidate_id")
            if cid and str(cid) not in by_cid:
                by_cid[str(cid)] = entry

        expected_cids = expected_batches.get(bid) or list(by_cid.keys())
        if not expected_cids:
            continue

        m = float(len(expected_cids))
        per_calls = float(row.get("calls", 1.0)) / m
        per_lat = float(row.get("latency_ms") or 0.0) / m
        per_in = float(row.get("input_tokens") or 0.0) / m
        per_out = float(row.get("output_tokens") or 0.0) / m
        per_th = float(row.get("thought_tokens") or 0.0) / m
        per_cached = float(row.get("cached_tokens") or 0.0) / m
        per_cost = float(row.get("cost_usd") or 0.0) / m
        cache_hit = _is_cache_hit(row)

        for cid in expected_cids:
            base_item = {
                "id": cid,
                "prompt": prompt or "classify-edit-batch",
                "prompt_version": row.get("prompt_version") or str(row.get("version", 1)),
                "model": row.get("model", ""),
                "calls": per_calls,
                "from_cache": cache_hit,
                "latency_ms": per_lat,
                "input_tokens": per_in,
                "output_tokens": per_out,
                "thought_tokens": per_th,
                "cached_tokens": per_cached,
                "cost_usd": per_cost,
            }
            if outcome != "ok":
                base_item["outcome"] = outcome
                base_item["value"] = None
                base_item["error"] = row.get("error")
            elif cid in by_cid:
                item_val = {k: v for k, v in by_cid[cid].items() if k != "candidate_id"}
                base_item["outcome"] = "ok"
                base_item["value"] = item_val
                base_item["error"] = None
            else:
                base_item["outcome"] = "invalid"
                base_item["value"] = None
                base_item["error"] = f"missing candidate_id '{cid}' in batch {bid}"
            expanded.append(base_item)

    return expanded


def group_runs(
    rows: list[dict[str, Any]],
    expected_batches: dict[str, list[str]],
) -> dict[tuple[str, str], dict[str, dict[str, Any]]]:
    """Split any batch rows and group item rows by `(prompt, model) -> {item_id: row}`."""
    expanded = split_batch_rows(rows, expected_batches)
    grouped: dict[tuple[str, str], dict[str, dict[str, Any]]] = defaultdict(dict)
    for r in expanded:
        key = (str(r.get("prompt") or "unknown"), str(r.get("model") or "unknown"))
        grouped[key][str(r["id"])] = r
    return dict(grouped)


def combine_two_stage(
    stage1_by_id: dict[str, dict[str, Any]],
    stage2_by_id: dict[str, dict[str, Any]],
    stage1_label: str,
    stage2_label: str,
    item_ids: list[str],
) -> tuple[dict[str, dict[str, Any]], dict[str, int]]:
    """Combine stage-1 fast run and stage-2 strong run into Variant B item rows."""
    combined: dict[str, dict[str, Any]] = {}
    stage1_kept = 0
    stage2_routed = 0

    for iid in item_ids:
        r1 = stage1_by_id.get(iid)
        if r1 is not None and is_graffiti_filtered(r1):
            stage1_kept += 1
            out = copy.deepcopy(r1)
            out["prompt"] = "classify-edit-two-stage"
            out["model"] = f"{stage1_label} -> {stage2_label}"
            out["calls"] = float(r1.get("calls", 1.0))
            combined[iid] = out
            continue

        stage2_routed += 1
        r2 = stage2_by_id.get(iid)
        c1 = float(r1.get("calls", 1.0)) if r1 is not None else 0.0
        c2 = float(r2.get("calls", 1.0)) if r2 is not None else 0.0
        lat = (float(r1.get("latency_ms") or 0.0) if r1 else 0.0) + (float(r2.get("latency_ms") or 0.0) if r2 else 0.0)
        in_t = (float(r1.get("input_tokens") or 0.0) if r1 else 0.0) + (float(r2.get("input_tokens") or 0.0) if r2 else 0.0)
        out_t = (float(r1.get("output_tokens") or 0.0) if r1 else 0.0) + (float(r2.get("output_tokens") or 0.0) if r2 else 0.0)
        th_t = (float(r1.get("thought_tokens") or 0.0) if r1 else 0.0) + (float(r2.get("thought_tokens") or 0.0) if r2 else 0.0)
        ca_t = (float(r1.get("cached_tokens") or 0.0) if r1 else 0.0) + (float(r2.get("cached_tokens") or 0.0) if r2 else 0.0)
        cost = (float(r1.get("cost_usd") or 0.0) if r1 else 0.0) + (float(r2.get("cost_usd") or 0.0) if r2 else 0.0)
        both_cached = _is_cache_hit(r1) and _is_cache_hit(r2)

        if r2 is None:
            combined[iid] = {
                "id": iid,
                "prompt": "classify-edit-two-stage",
                "model": f"{stage1_label} -> {stage2_label}",
                "outcome": "missing",
                "value": None,
                "error": "missing stage-2 row",
                "calls": c1,
                "from_cache": both_cached,
                "latency_ms": lat,
                "input_tokens": in_t,
                "output_tokens": out_t,
                "thought_tokens": th_t,
                "cached_tokens": ca_t,
                "cost_usd": cost,
            }
        else:
            out = copy.deepcopy(r2)
            out["prompt"] = "classify-edit-two-stage"
            out["model"] = f"{stage1_label} -> {stage2_label}"
            out["calls"] = c1 + c2
            out["from_cache"] = both_cached
            out["latency_ms"] = lat
            out["input_tokens"] = in_t
            out["output_tokens"] = out_t
            out["thought_tokens"] = th_t
            out["cached_tokens"] = ca_t
            out["cost_usd"] = cost
            combined[iid] = out

    meta = {"stage1_filtered": stage1_kept, "stage2_routed": stage2_routed}
    return combined, meta


def combine_refute(
    first_by_id: dict[str, dict[str, Any]],
    refute_by_id: dict[str, dict[str, Any]],
    first_label: str,
    refute_label: str,
    item_ids: list[str],
) -> tuple[dict[str, dict[str, Any]], dict[str, int]]:
    """Combine first-pass classification with Variant E `refute-classification` second pass."""
    combined: dict[str, dict[str, Any]] = {}
    checked = 0
    upheld = 0
    downgraded = 0
    refute_non_ok = 0

    for iid in item_ids:
        r1 = first_by_id.get(iid)
        if r1 is None:
            continue
        out = copy.deepcopy(r1)
        out["prompt"] = "classify-edit+refute"
        out["model"] = f"{first_label} + {refute_label}"
        out["calls"] = float(r1.get("calls", 1.0))

        rr = refute_by_id.get(iid)
        if rr is not None:
            checked += 1
            out["calls"] += float(rr.get("calls", 1.0))
            out["latency_ms"] = float(r1.get("latency_ms") or 0.0) + float(rr.get("latency_ms") or 0.0)
            out["input_tokens"] = float(r1.get("input_tokens") or 0.0) + float(rr.get("input_tokens") or 0.0)
            out["output_tokens"] = float(r1.get("output_tokens") or 0.0) + float(rr.get("output_tokens") or 0.0)
            out["thought_tokens"] = float(r1.get("thought_tokens") or 0.0) + float(rr.get("thought_tokens") or 0.0)
            out["cached_tokens"] = float(r1.get("cached_tokens") or 0.0) + float(rr.get("cached_tokens") or 0.0)
            out["cost_usd"] = float(r1.get("cost_usd") or 0.0) + float(rr.get("cost_usd") or 0.0)
            out["from_cache"] = _is_cache_hit(r1) and _is_cache_hit(rr)

            if str(r1.get("outcome") or "").lower() == "ok" and str(rr.get("outcome") or "").lower() == "ok":
                rv = rr.get("value") or {}
                verdict = rv.get("verdict")
                corr_cat = rv.get("corrected_category")
                if verdict == "downgraded" and corr_cat in CATEGORIES:
                    downgraded += 1
                    val = copy.deepcopy(out.get("value") or {})
                    val["category"] = corr_cat
                    if rv.get("confidence") is not None:
                        val["confidence"] = int(rv["confidence"])
                    if rv.get("evidence"):
                        val["evidence"] = list(rv["evidence"])
                    out["value"] = val
                else:
                    upheld += 1
            else:
                refute_non_ok += 1

        combined[iid] = out

    meta = {
        "refute_checked": checked,
        "refute_upheld": upheld,
        "refute_downgraded": downgraded,
        "refute_non_ok": refute_non_ok,
    }
    return combined, meta


def _safe_div(num: float, den: float) -> float | None:
    return (num / den) if den > 0 else None


def score_run(
    prompt: str,
    model: str,
    rows_by_id: dict[str, dict[str, Any]],
    golden: dict[str, Any],
    inputs_by_id: dict[str, dict[str, str]] | None = None,
    variant_meta: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Compute all S9 metrics for a single `(prompt, model)` run over golden dev items."""
    inputs_map = inputs_by_id or {}
    items_by_id: dict[str, dict[str, Any]] = golden["items_by_id"]
    eval_ids = list(golden["ordered_ids"])
    n_items = len(eval_ids)

    outcomes: Counter[str] = Counter()
    confusion: dict[str, dict[str, int]] = {
        g: {p: 0 for p in (*CATEGORIES, "non_ok")} for g in CATEGORIES
    }

    cat_total = 0
    cat_ok = 0
    cat_exact_hits = 0
    cat_coarse_hits = 0

    inc_cat_total = 0
    inc_cat_hits = 0
    strict_inc_cat_total = 0
    strict_inc_cat_hits = 0
    non_inc_cat_total = 0
    non_inc_cat_hits = 0

    graffiti_total = 0
    graffiti_filtered = 0
    strict_inc_total = 0
    strict_inc_wrongly_filtered_ids: list[str] = []
    reverted_filtered = 0
    total_filtered_any = 0

    ev_spans_total = 0
    ev_spans_exact = 0
    ev_spans_norm = 0
    ev_items_checked = 0
    ev_items_all_exact = 0
    ev_items_all_norm = 0

    calls_sum = 0.0
    lat_all: list[float] = []
    lat_live: list[float] = []
    cache_hits = 0
    in_tok_sum = 0.0
    out_tok_sum = 0.0
    th_tok_sum = 0.0
    cost_sum = 0.0

    topic_totals: dict[str, dict[str, float]] = {
        tid: {"calls": 0.0, "tokens": 0.0, "latency_ms": 0.0, "cost_usd": 0.0, "items": 0.0}
        for tid in golden.get("dev_topics_with_items") or ()
    }

    for iid in eval_ids:
        g = items_by_id[iid]
        tid = g["topic_id"]
        t_acc = topic_totals.setdefault(
            tid, {"calls": 0.0, "tokens": 0.0, "latency_ms": 0.0, "cost_usd": 0.0, "items": 0.0}
        )
        t_acc["items"] += 1.0

        row = rows_by_id.get(iid)
        if row is None:
            outcomes["missing"] += 1
        else:
            outc = str(row.get("outcome") or "failed").lower()
            outcomes[outc] += 1
            c_val = float(row.get("calls", 1.0))
            lat_val = float(row.get("latency_ms") or 0.0)
            in_t = float(row.get("input_tokens") or 0.0)
            out_t = float(row.get("output_tokens") or 0.0)
            th_t = float(row.get("thought_tokens") or 0.0)
            cost_v = float(row.get("cost_usd") or 0.0)

            calls_sum += c_val
            lat_all.append(lat_val)
            if _is_cache_hit(row):
                cache_hits += 1
            else:
                lat_live.append(lat_val)
            in_tok_sum += in_t
            out_tok_sum += out_t
            th_tok_sum += th_t
            cost_sum += cost_v

            t_acc["calls"] += c_val
            t_acc["tokens"] += in_t + out_t + th_t
            t_acc["latency_ms"] += lat_val
            t_acc["cost_usd"] += cost_v

        is_ok = row is not None and str(row.get("outcome") or "").lower() == "ok" and isinstance(row.get("value"), dict)
        pred_cat = str(row["value"].get("category")) if is_ok and row["value"].get("category") in CATEGORIES else None

        # Category accuracy & confusion table
        g_cat = g.get("category")
        if g_cat in CATEGORIES:
            cat_total += 1
            is_incident = g["label"] in INCIDENT_LABELS
            is_strict_inc = g["label"] in STRICT_INCIDENT_LABELS
            if is_incident:
                inc_cat_total += 1
            else:
                non_inc_cat_total += 1
            if is_strict_inc:
                strict_inc_cat_total += 1

            if pred_cat is not None:
                cat_ok += 1
                confusion[g_cat][pred_cat] += 1
                if pred_cat == g_cat:
                    cat_exact_hits += 1
                    if is_incident:
                        inc_cat_hits += 1
                    else:
                        non_inc_cat_hits += 1
                    if is_strict_inc:
                        strict_inc_cat_hits += 1
                if COARSE_BUCKET[pred_cat] == COARSE_BUCKET[g_cat]:
                    cat_coarse_hits += 1
            else:
                confusion[g_cat]["non_ok"] += 1

        # Graffiti filter metrics
        filtered = is_graffiti_filtered(row)
        if filtered:
            total_filtered_any += 1
        if g["label"] == "not-incident" and g.get("exclusion") == "graffiti":
            graffiti_total += 1
            if filtered:
                graffiti_filtered += 1
        if g["label"] in STRICT_INCIDENT_LABELS:
            strict_inc_total += 1
            if filtered:
                strict_inc_wrongly_filtered_ids.append(iid)
        elif g["label"] == "reverted" and filtered:
            reverted_filtered += 1

        # Verbatim evidence check
        if is_ok and inputs_map:
            ev_res = check_evidence_spans(row["value"].get("evidence"), inputs_map.get(iid))
            if ev_res["checked"]:
                ev_items_checked += 1
                ev_spans_total += ev_res["total"]
                ev_spans_exact += ev_res["exact"]
                ev_spans_norm += ev_res["normalised"]
                if ev_res["all_exact"]:
                    ev_items_all_exact += 1
                if ev_res["all_normalised"]:
                    ev_items_all_norm += 1

    n_topics = len(topic_totals)
    lat_series = lat_live if lat_live else lat_all
    mean_lat_ms = statistics.mean(lat_series) if lat_series else 0.0
    p50_lat_ms = statistics.median(lat_series) if lat_series else 0.0

    denom_present = max(1, len(lat_all))
    total_tokens = in_tok_sum + out_tok_sum + th_tok_sum

    per_category: dict[str, dict[str, Any]] = {}
    for cat in CATEGORIES:
        tp = confusion[cat][cat]
        support = sum(confusion[cat].values())
        predicted = sum(confusion[g_c][cat] for g_c in CATEGORIES)
        per_category[cat] = {
            "support": support,
            "predicted": predicted,
            "tp": tp,
            "precision": _safe_div(tp, predicted),
            "recall": _safe_div(tp, support),
        }

    return {
        "prompt": prompt,
        "model": model,
        "items_total": n_items,
        "outcomes": dict(outcomes),
        "category_accuracy": {
            "labelled_items": cat_total,
            "ok_labelled_items": cat_ok,
            "exact_hits": cat_exact_hits,
            "accuracy_all": _safe_div(cat_exact_hits, cat_total),
            "accuracy_ok": _safe_div(cat_exact_hits, cat_ok),
            "coarse_3way_accuracy_all": _safe_div(cat_coarse_hits, cat_total),
            "incidents_labelled": inc_cat_total,
            "incidents_accuracy": _safe_div(inc_cat_hits, inc_cat_total),
            "contested_campaign_labelled": strict_inc_cat_total,
            "contested_campaign_accuracy": _safe_div(strict_inc_cat_hits, strict_inc_cat_total),
            "non_incidents_labelled": non_inc_cat_total,
            "non_incidents_accuracy": _safe_div(non_inc_cat_hits, non_inc_cat_total),
        },
        "per_category": per_category,
        "confusion": confusion,
        "graffiti_filter": {
            "rule": "category in {vandalism, test_or_nonsense} and confidence >= 4",
            "total_filtered": total_filtered_any,
            "total_filtered_share": _safe_div(total_filtered_any, n_items),
            "graffiti_not_incidents_total": graffiti_total,
            "graffiti_not_incidents_filtered": graffiti_filtered,
            "graffiti_not_incidents_filtered_share": _safe_div(graffiti_filtered, graffiti_total),
            "contested_campaign_total": strict_inc_total,
            "contested_campaign_wrongly_filtered": len(strict_inc_wrongly_filtered_ids),
            "contested_campaign_wrongly_filtered_ids": strict_inc_wrongly_filtered_ids,
            "reverted_filtered": reverted_filtered,
        },
        "evidence_verbatim": {
            "inputs_checked": bool(inputs_map),
            "ok_items_checked": ev_items_checked,
            "spans_total": ev_spans_total,
            "spans_exact": ev_spans_exact,
            "spans_exact_rate": _safe_div(ev_spans_exact, ev_spans_total),
            "spans_normalised": ev_spans_norm,
            "spans_normalised_rate": _safe_div(ev_spans_norm, ev_spans_total),
            "items_all_spans_exact_rate": _safe_div(ev_items_all_exact, ev_items_checked),
        },
        "efficiency": {
            "total_calls": round(calls_sum, 2),
            "cache_hits": cache_hits,
            "mean_calls_per_item": calls_sum / denom_present,
            "mean_latency_ms_per_item": mean_lat_ms,
            "median_latency_ms_per_item": p50_lat_ms,
            "mean_input_tokens_per_item": in_tok_sum / denom_present,
            "mean_output_tokens_per_item": out_tok_sum / denom_present,
            "mean_thought_tokens_per_item": th_tok_sum / denom_present,
            "mean_total_tokens_per_item": total_tokens / denom_present,
            "mean_cost_usd_per_item": cost_sum / denom_present,
            "total_cost_usd": cost_sum,
            "topics_with_items": n_topics,
            "mean_calls_per_topic": (calls_sum / n_topics) if n_topics else 0.0,
            "mean_tokens_per_topic": (total_tokens / n_topics) if n_topics else 0.0,
            "mean_latency_ms_per_topic": (sum(lat_all) / n_topics) if n_topics else 0.0,
            "mean_cost_usd_per_topic": (cost_sum / n_topics) if n_topics else 0.0,
        },
        "variant_meta": variant_meta or {},
    }


def _fmt_pct(val: float | None) -> str:
    return "—" if val is None else f"{val * 100:.1f}%"


def render_markdown(report: dict[str, Any]) -> str:
    lines: list[str] = []
    lines.append(
        f"# P1.S9 Classification Scorecard (golden `{report['golden_version']}`, dev split, {report['dev_items']} items)"
    )
    lines.append("")
    lines.append(
        "| Prompt | Model | ok / N | Cat acc (all) | Incidents acc | Non-inc acc | "
        "Graffiti filt | Contested/camp wrongly filt | Verbatim ev | Calls/item | Mean ms/item | Tok/item | $/item |"
    )
    lines.append("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")

    for r in report["runs"]:
        ok_n = r["outcomes"].get("ok", 0)
        tot_n = r["items_total"]
        ca = r["category_accuracy"]
        gf = r["graffiti_filter"]
        ev = r["evidence_verbatim"]
        ef = r["efficiency"]
        gf_str = (
            f"{gf['graffiti_not_incidents_filtered']}/{gf['graffiti_not_incidents_total']} "
            f"({_fmt_pct(gf['graffiti_not_incidents_filtered_share'])})"
        )
        ev_str = _fmt_pct(ev["spans_exact_rate"]) if ev["inputs_checked"] else "—"
        lines.append(
            f"| `{r['prompt']}` | `{r['model']}` | {ok_n}/{tot_n} | "
            f"{_fmt_pct(ca['accuracy_all'])} | {_fmt_pct(ca['incidents_accuracy'])} | "
            f"{_fmt_pct(ca['non_incidents_accuracy'])} | {gf_str} | "
            f"**{gf['contested_campaign_wrongly_filtered']}**/{gf['contested_campaign_total']} | "
            f"{ev_str} | {ef['mean_calls_per_item']:.2f} | {ef['mean_latency_ms_per_item']:.0f} | "
            f"{ef['mean_total_tokens_per_item']:.0f} | ${ef['mean_cost_usd_per_item']:.5f} |"
        )

    lines.append("")
    lines.append("## Per-Topic Cost & Throughput (`dev` topics with items)")
    lines.append("")
    lines.append("| Prompt | Model | Topics | Calls / topic | Tokens / topic | Latency ms / topic | Total $ |")
    lines.append("|---|---|---:|---:|---:|---:|---:|")
    for r in report["runs"]:
        ef = r["efficiency"]
        lines.append(
            f"| `{r['prompt']}` | `{r['model']}` | {ef['topics_with_items']} | "
            f"{ef['mean_calls_per_topic']:.1f} | {ef['mean_tokens_per_topic']:.0f} | "
            f"{ef['mean_latency_ms_per_topic']:.0f} | ${ef['total_cost_usd']:.4f} |"
        )

    for r in report["runs"]:
        lines.append("")
        lines.append(f"## `{r['prompt']}` · `{r['model']}`")
        out_pairs = ", ".join(f"{k}={v}" for k, v in sorted(r["outcomes"].items()))
        lines.append(f"- **Outcomes:** {out_pairs}")
        ca = r["category_accuracy"]
        lines.append(
            f"- **Category accuracy:** {ca['exact_hits']}/{ca['labelled_items']} "
            f"({_fmt_pct(ca['accuracy_all'])} all, {_fmt_pct(ca['accuracy_ok'])} on ok; "
            f"coarse 3-way {_fmt_pct(ca['coarse_3way_accuracy_all'])}; "
            f"contested+campaign {_fmt_pct(ca['contested_campaign_accuracy'])})"
        )
        gf = r["graffiti_filter"]
        wrong_ids = (
            f" ({', '.join(gf['contested_campaign_wrongly_filtered_ids'])})"
            if gf["contested_campaign_wrongly_filtered_ids"]
            else ""
        )
        lines.append(
            f"- **Graffiti filter (`conf >= 4`):** filtered {gf['total_filtered']}/{r['items_total']} total "
            f"({_fmt_pct(gf['total_filtered_share'])}); golden `graffiti` filtered "
            f"{gf['graffiti_not_incidents_filtered']}/{gf['graffiti_not_incidents_total']} "
            f"({_fmt_pct(gf['graffiti_not_incidents_filtered_share'])}); "
            f"contested/campaign wrongly filtered: **{gf['contested_campaign_wrongly_filtered']}**{wrong_ids}"
        )
        ev = r["evidence_verbatim"]
        if ev["inputs_checked"]:
            lines.append(
                f"- **Evidence verbatim:** {ev['spans_exact']}/{ev['spans_total']} exact "
                f"({_fmt_pct(ev['spans_exact_rate'])}), {ev['spans_normalised']}/{ev['spans_total']} normalised "
                f"({_fmt_pct(ev['spans_normalised_rate'])}); all-spans-exact items: "
                f"{_fmt_pct(ev['items_all_spans_exact_rate'])}"
            )
        if r.get("variant_meta"):
            meta_str = ", ".join(f"{k}={v}" for k, v in r["variant_meta"].items())
            lines.append(f"- **Variant stage stats:** {meta_str}")

        lines.append("")
        cols = [*CATEGORIES, "non_ok"]
        hdr = "| Golden \ Pred | " + " | ".join(CAT_SHORT.get(c, c) for c in cols) + " | Recall |"
        sep = "|---|" + "|".join("---:" for _ in cols) + "|---:|"
        lines.append(hdr)
        lines.append(sep)
        for g_cat in CATEGORIES:
            row_counts = r["confusion"][g_cat]
            rec_val = r["per_category"][g_cat]["recall"]
            cells = [
                f"**{row_counts[c]}**" if c == g_cat and row_counts[c] else str(row_counts[c])
                for c in cols
            ]
            lines.append(f"| `{CAT_SHORT[g_cat]}` | " + " | ".join(cells) + f" | {_fmt_pct(rec_val)} |")

    return NL.join(lines) + NL


def _parse_spec_and_load(
    spec: str,
    expected_batches: dict[str, list[str]],
) -> list[tuple[str, str, dict[str, dict[str, Any]]]]:
    """Load `<path>` or `<path>:<model>` into a list of `(prompt, model, rows_by_id)`."""
    path_str = spec
    model_filter: str | None = None
    p_cand = pathlib.Path(spec)
    if not p_cand.exists() and ":" in spec:
        lhs, rhs = spec.rsplit(":", 1)
        if pathlib.Path(lhs).exists():
            path_str, model_filter = lhs, rhs

    grouped = group_runs(load_jsonl(pathlib.Path(path_str)), expected_batches)
    out: list[tuple[str, str, dict[str, dict[str, Any]]]] = []
    for (pr, md), by_id in sorted(grouped.items()):
        if model_filter is None or md == model_filter:
            out.append((pr, md, by_id))
    if not out:
        raise ValueError(f"No matching runs found in '{spec}'.")
    return out


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Score P1.S9 edit-classification batch outputs against golden dev.")
    ap.add_argument("batch_outs", nargs="*", help="Batch output JSONL files from scripts/analyze.sh batch.")
    ap.add_argument(
        "--golden",
        type=pathlib.Path,
        default=REPO_ROOT / "research/eval/golden.json",
        help="Path to golden.json (only split='dev' is read).",
    )
    ap.add_argument(
        "--inputs",
        type=pathlib.Path,
        default=HERE / "results/golden-dev.classify.jsonl",
        help="Input JSONL for programmatic verbatim evidence verification.",
    )
    ap.add_argument(
        "--batch-inputs",
        type=pathlib.Path,
        default=HERE / "results/golden-dev.classify-batch.jsonl",
        help="Batch-by-article input JSONL for Variant D candidate_id mapping.",
    )
    ap.add_argument("--stage1", action="append", default=[], help="Variant B stage-1 fast batch output (<path>[:model]).")
    ap.add_argument("--stage2", action="append", default=[], help="Variant B stage-2 strong batch output (<path>[:model]).")
    ap.add_argument("--refute", action="append", default=[], help="Variant E refute-classification output (<path>[:model]).")
    ap.add_argument(
        "--refute-base",
        help="Variant E first-pass batch output (<path>[:model]); defaults to first positional single-item run.",
    )
    ap.add_argument("--json", action="store_true", help="Emit JSON scorecard on stdout instead of Markdown.")
    ap.add_argument("--out-json", type=pathlib.Path, help="Optional path to also write JSON scorecard.")
    ap.add_argument("--out-md", type=pathlib.Path, help="Optional path to also write Markdown scorecard.")
    args = ap.parse_args(argv)

    if not args.batch_outs and not (args.stage1 and args.stage2) and not args.refute:
        ap.error("Provide at least one <batch-out.jsonl>, or --stage1/--stage2, or --refute.")
    if len(args.stage1) != len(args.stage2):
        ap.error("--stage1 and --stage2 must be given the same number of times.")

    golden = load_dev_golden(args.golden)
    expected_batches = load_batch_mapping(args.batch_inputs, golden["expected_batches"])
    inputs_map = load_inputs_map(args.inputs)

    evaluated_runs: list[dict[str, Any]] = []
    positional_loaded: list[tuple[str, str, dict[str, dict[str, Any]]]] = []

    for path_str in args.batch_outs:
        for pr, md, by_id in _parse_spec_and_load(path_str, expected_batches):
            positional_loaded.append((pr, md, by_id))
            evaluated_runs.append(score_run(pr, md, by_id, golden, inputs_map))

    # Variant B: two-stage combinations
    for s1_spec, s2_spec in zip(args.stage1, args.stage2):
        s1_runs = _parse_spec_and_load(s1_spec, expected_batches)
        s2_runs = _parse_spec_and_load(s2_spec, expected_batches)
        for _, m1, b1 in s1_runs:
            for _, m2, b2 in s2_runs:
                comb, meta = combine_two_stage(b1, b2, m1, m2, golden["ordered_ids"])
                evaluated_runs.append(
                    score_run("classify-edit-two-stage", f"{m1} -> {m2}", comb, golden, inputs_map, variant_meta=meta)
                )

    # Variant E: self-check refutation combinations
    if args.refute:
        if args.refute_base:
            base_runs = _parse_spec_and_load(args.refute_base, expected_batches)
        else:
            base_runs = [x for x in positional_loaded if x[0] != "refute-classification"][:1]
            if not base_runs:
                ap.error("--refute requires either a positional first-pass <batch-out.jsonl> or --refute-base.")
        for ref_spec in args.refute:
            ref_runs = _parse_spec_and_load(ref_spec, expected_batches)
            for _, m_base, b_base in base_runs:
                for _, m_ref, b_ref in ref_runs:
                    comb, meta = combine_refute(b_base, b_ref, m_base, m_ref, golden["ordered_ids"])
                    evaluated_runs.append(
                        score_run(
                            "classify-edit+refute",
                            f"{m_base} + {m_ref}",
                            comb,
                            golden,
                            inputs_map,
                            variant_meta=meta,
                        )
                    )

    report = {
        "golden_version": golden["golden_version"],
        "dataset": golden["dataset"],
        "split": "dev",
        "dev_items": len(golden["ordered_ids"]),
        "runs": evaluated_runs,
    }

    md_text = render_markdown(report)
    json_text = json.dumps(report, ensure_ascii=False, indent=2) + NL

    if args.out_md:
        args.out_md.parent.mkdir(parents=True, exist_ok=True)
        args.out_md.write_text(md_text, encoding="utf-8")
    if args.out_json:
        args.out_json.parent.mkdir(parents=True, exist_ok=True)
        args.out_json.write_text(json_text, encoding="utf-8")

    sys.stdout.write(json_text if args.json else md_text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
