#!/usr/bin/env python3
"""P1.S9b (#70): Score Gemini Decisions API outputs on the golden dev split and compare with S9.

Outputs:
  - results/s9b-scorecard.md
  - results/s9b-scorecard.json

Usage:
  python3 research/70-s9b-decisions/score.py
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import json
import math
import pathlib
import statistics
import sys
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]

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
PRED_CATEGORIES = (*CATEGORIES, "none_of_these")
CAT_SHORT = {
    "vandalism": "vand",
    "test_or_nonsense": "test",
    "good_faith_error": "gfe",
    "content_dispute": "disp",
    "pov_push": "pov",
    "false_factual_claim": "false",
    "source_manipulation": "src",
    "other": "other",
    "none_of_these": "none",
    "non_ok": "non_ok",
}
GRAFFITI_CATS = frozenset({"vandalism", "test_or_nonsense"})

LEVEL_LABELS = ("not-incident", "reverted", "contested", "campaign")
LEVEL_INDEX = {lbl: idx for idx, lbl in enumerate(LEVEL_LABELS)}
INCIDENT_LABELS = frozenset({"reverted", "contested", "campaign"})
STRICT_INCIDENT_LABELS = frozenset({"contested", "campaign"})

YES_NO_KEYS = (
    "nonsense",
    "factual_claim",
    "changes_number_date_name",
    "political_or_national_pov",
    "contradicts_replaced_text",
    "alters_citation",
    "removal_disputes_accuracy",
)

DECISIONS_PRICE_PER_M_INPUT_USD = 0.10


def load_jsonl(path: pathlib.Path) -> list[dict[str, Any]]:
    with path.open(encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def load_dev_golden(golden_path: pathlib.Path) -> dict[str, Any]:
    """Load ONLY `split == 'dev'` items from `research/eval/golden.json`."""
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    items_by_id: dict[str, dict[str, Any]] = {}
    ordered_ids: list[str] = []

    for topic in doc.get("topics") or ():
        if topic.get("split") != "dev":
            continue
        tid = str(topic["id"])
        for it in topic.get("items") or ():
            iid = str(it["id"])
            ordered_ids.append(iid)
            items_by_id[iid] = {
                "id": iid,
                "topic_id": tid,
                "page_id": int(it["page_id"]),
                "label": str(it["label"]),
                "level": LEVEL_INDEX[str(it["label"])],
                "exclusion": it.get("exclusion"),
                "category": it.get("category"),
            }

    return {
        "golden_version": str(doc.get("version") or ""),
        "dataset": str(doc.get("dataset") or ""),
        "ordered_ids": ordered_ids,
        "items_by_id": items_by_id,
    }


def _safe_div(num: float, den: float) -> float | None:
    return (num / den) if den > 0 else None


def _clamp01(p: float) -> float:
    return max(0.0, min(1.0, float(p)))


def percentile(values: list[float], q: float) -> float:
    """Linearly interpolated percentile for `q` in [0, 100]."""
    if not values:
        return 0.0
    xs = sorted(float(v) for v in values)
    if len(xs) == 1:
        return xs[0]
    pos = (max(0.0, min(100.0, q)) / 100.0) * (len(xs) - 1)
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    if lo == hi:
        return xs[lo]
    frac = pos - lo
    return xs[lo] + (xs[hi] - xs[lo]) * frac


def compute_auc(y_true: list[int], y_prob: list[float]) -> float | None:
    """Exact ROC AUC via Mann-Whitney U with 0.5 tie handling."""
    if len(y_true) != len(y_prob) or not y_true:
        return None
    pos = [float(p) for y, p in zip(y_true, y_prob) if y == 1]
    neg = [float(p) for y, p in zip(y_true, y_prob) if y == 0]
    if not pos or not neg:
        return None

    # Sort combined pairs to compute average ranks in O(N log N).
    paired = sorted((float(p), int(y)) for y, p in zip(y_true, y_prob))
    n = len(paired)
    rank_sum_pos = 0.0
    i = 0
    while i < n:
        j = i + 1
        while j < n and paired[j][0] == paired[i][0]:
            j += 1
        # 1-based average rank across tied group [i, j)
        avg_rank = 0.5 * ((i + 1) + j)
        pos_in_tie = sum(1 for k in range(i, j) if paired[k][1] == 1)
        rank_sum_pos += pos_in_tie * avg_rank
        i = j

    n_pos = len(pos)
    n_neg = len(neg)
    u_stat = rank_sum_pos - (n_pos * (n_pos + 1)) / 2.0
    return u_stat / (n_pos * n_neg)


def compute_brier(y_true: list[int], y_prob: list[float]) -> float | None:
    """Mean squared probability error (Brier score)."""
    if len(y_true) != len(y_prob) or not y_true:
        return None
    return sum((_clamp01(p) - int(y)) ** 2 for y, p in zip(y_true, y_prob)) / len(y_true)


def compute_reliability_buckets(
    y_true: list[int],
    y_prob: list[float],
    n_buckets: int = 5,
) -> dict[str, Any]:
    """Build an equal-width `n_buckets` reliability diagram over [0, 1] plus ECE."""
    buckets_p: list[list[float]] = [[] for _ in range(n_buckets)]
    buckets_y: list[list[int]] = [[] for _ in range(n_buckets)]

    for y, raw_p in zip(y_true, y_prob):
        p = _clamp01(raw_p)
        idx = min(n_buckets - 1, int(p * n_buckets))
        buckets_p[idx].append(p)
        buckets_y[idx].append(int(y))

    total_n = len(y_true)
    rows: list[dict[str, Any]] = []
    ece = 0.0
    for idx in range(n_buckets):
        lo = round(idx / n_buckets, 2)
        hi = round((idx + 1) / n_buckets, 2)
        cnt = len(buckets_p[idx])
        if cnt > 0:
            mean_p = sum(buckets_p[idx]) / cnt
            emp_r = sum(buckets_y[idx]) / cnt
            gap = abs(mean_p - emp_r)
            if total_n > 0:
                ece += (cnt / total_n) * gap
        else:
            mean_p = None
            emp_r = None
            gap = None
        bracket = f"[{lo:.1f}, {hi:.1f}]" if idx == n_buckets - 1 else f"[{lo:.1f}, {hi:.1f})"
        rows.append(
            {
                "bucket": idx,
                "range": bracket,
                "low": lo,
                "high": hi,
                "count": cnt,
                "mean_pred": mean_p,
                "empirical_rate": emp_r,
                "abs_gap": gap,
            }
        )
    return {"ece": ece if total_n > 0 else None, "buckets": rows}


def _eval_nonsense_cutoff(
    scored_items: list[dict[str, Any]],
    threshold: float,
    total_graffiti: int,
    total_not_incident: int,
    total_contested_campaign: int,
    total_reverted: int,
    total_items: int,
) -> dict[str, Any]:
    """Evaluate filtering rule `nonsense >= threshold` across all items."""
    graffiti_removed = 0
    not_incident_removed = 0
    cc_removed_ids: list[str] = []
    reverted_removed = 0
    total_removed = 0

    for it in scored_items:
        p = it.get("nonsense")
        if p is None or p < threshold:
            continue
        total_removed += 1
        lbl = it["label"]
        if lbl == "not-incident":
            not_incident_removed += 1
            if it.get("exclusion") == "graffiti":
                graffiti_removed += 1
        elif lbl in STRICT_INCIDENT_LABELS:
            cc_removed_ids.append(it["id"])
        elif lbl == "reverted":
            reverted_removed += 1

    return {
        "threshold": threshold,
        "graffiti_total": total_graffiti,
        "graffiti_removed": graffiti_removed,
        "graffiti_removed_share": _safe_div(graffiti_removed, total_graffiti),
        "not_incident_total": total_not_incident,
        "not_incident_removed": not_incident_removed,
        "not_incident_removed_share": _safe_div(not_incident_removed, total_not_incident),
        "contested_campaign_total": total_contested_campaign,
        "contested_campaign_removed": len(cc_removed_ids),
        "contested_campaign_removed_ids": cc_removed_ids,
        "reverted_total": total_reverted,
        "reverted_removed": reverted_removed,
        "reverted_removed_share": _safe_div(reverted_removed, total_reverted),
        "total_removed": total_removed,
        "total_removed_share": _safe_div(total_removed, total_items),
    }


def find_graffiti_threshold(
    scored_items: list[dict[str, Any]],
    total_graffiti: int | None = None,
    total_not_incident: int | None = None,
    total_contested_campaign: int | None = None,
    total_reverted: int | None = None,
    total_items: int | None = None,
) -> dict[str, Any]:
    """Find the optimal zero-loss `nonsense` threshold (`nonsense >= t`) keeping all contested/campaign items.

    Any threshold `t` must satisfy `t > max_cc`, where `max_cc` is the maximum `nonsense` score
    observed on any golden `contested` or `campaign` item.
      - `min_safe_threshold` (`> max_cc`, smallest observed `p > max_cc`): removes the maximum number
        of `not-incident` and `graffiti` items without losing any `contested`/`campaign` item.
      - `conservative_threshold` (`min(p for graffiti with p > max_cc)`): the highest threshold `t`
        that still achieves that same maximum `graffiti` removal count while keeping every
        `contested`/`campaign` item (minimizing collateral filtering on `reverted` items).
    """
    n_items = total_items if total_items is not None else len(scored_items)
    n_graff = (
        total_graffiti
        if total_graffiti is not None
        else sum(1 for x in scored_items if x["label"] == "not-incident" and x.get("exclusion") == "graffiti")
    )
    n_non_inc = (
        total_not_incident
        if total_not_incident is not None
        else sum(1 for x in scored_items if x["label"] == "not-incident")
    )
    n_cc = (
        total_contested_campaign
        if total_contested_campaign is not None
        else sum(1 for x in scored_items if x["label"] in STRICT_INCIDENT_LABELS)
    )
    n_rev = (
        total_reverted
        if total_reverted is not None
        else sum(1 for x in scored_items if x["label"] == "reverted")
    )

    cc_items = [
        (float(x["nonsense"]), str(x["id"]))
        for x in scored_items
        if x["label"] in STRICT_INCIDENT_LABELS and x.get("nonsense") is not None
    ]
    max_cc, max_cc_id = max(cc_items, key=lambda pair: pair[0]) if cc_items else (0.0, None)

    above_all = sorted(
        {float(x["nonsense"]) for x in scored_items if x.get("nonsense") is not None and float(x["nonsense"]) > max_cc}
    )
    above_graff = sorted(
        {
            float(x["nonsense"])
            for x in scored_items
            if x["label"] == "not-incident"
            and x.get("exclusion") == "graffiti"
            and x.get("nonsense") is not None
            and float(x["nonsense"]) > max_cc
        }
    )

    # Primary reported threshold: highest `t` that still achieves maximum golden-graffiti removal
    # while keeping 100% of contested/campaign items (falls back to smallest p > max_cc, else 1.0).
    if above_graff:
        primary_t = above_graff[0]
    elif above_all:
        primary_t = above_all[0]
    else:
        primary_t = 1.0

    aggressive_t = above_all[0] if above_all else 1.0

    primary_eval = _eval_nonsense_cutoff(
        scored_items, primary_t, n_graff, n_non_inc, n_cc, n_rev, n_items
    )
    aggressive_eval = _eval_nonsense_cutoff(
        scored_items, aggressive_t, n_graff, n_non_inc, n_cc, n_rev, n_items
    )

    fixed_points = {
        f"{ft:.2f}": _eval_nonsense_cutoff(scored_items, ft, n_graff, n_non_inc, n_cc, n_rev, n_items)
        for ft in (0.30, 0.50, 0.70, 0.80, 0.90)
    }

    return {
        "max_contested_campaign_nonsense": max_cc,
        "max_contested_campaign_id": max_cc_id,
        # Highest threshold that keeps every contested/campaign item while achieving max graffiti removal:
        **primary_eval,
        "aggressive_min_safe": aggressive_eval,
        "fixed_thresholds": fixed_points,
    }


def extract_level_probs(level_ans: dict[str, Any] | None) -> dict[int, float] | None:
    if not isinstance(level_ans, dict):
        return None
    probs = level_ans.get("probabilities")
    if not isinstance(probs, dict):
        return None
    out: dict[int, float] = {}
    for k in range(4):
        raw = probs.get(str(k), probs.get(k, 0.0))
        out[k] = float(raw or 0.0)
    return out


def score_decisions_variant(
    variant_name: str,
    rows: list[dict[str, Any]],
    golden: dict[str, Any],
) -> dict[str, Any]:
    items_by_id: dict[str, dict[str, Any]] = golden["items_by_id"]
    ordered_ids: list[str] = golden["ordered_ids"]
    rows_by_id = {str(r.get("_id") or r.get("id")): r for r in rows}

    n_total = len(ordered_ids)
    outcomes: Counter[str] = Counter()

    cat_confusion: dict[str, dict[str, int]] = {
        g: {p: 0 for p in (*PRED_CATEGORIES, "non_ok")} for g in CATEGORIES
    }
    cat_labelled = 0
    cat_ok = 0
    cat_exact = 0
    inc_cat_total = 0
    inc_cat_hits = 0
    strict_cat_total = 0
    strict_cat_hits = 0
    non_inc_cat_total = 0
    non_inc_cat_hits = 0

    # Category-based graffiti filter (for apples-to-apples check vs S9):
    cat_graff_removed = 0
    cat_non_inc_removed = 0
    cat_cc_removed = 0

    level_confusion: dict[str, dict[str, int]] = {
        g: {p: 0 for p in (*LEVEL_LABELS, "non_ok")} for g in LEVEL_LABELS
    }
    judge_ok = 0
    judge_exact = 0
    judge_within_one = 0
    judge_overclaim = 0
    judge_underclaim = 0

    y_is_inc: list[int] = []
    p_is_inc: list[float] = []
    y_ge_cont: list[int] = []
    p_ge_cont: list[float] = []

    yn_by_key: dict[str, list[tuple[dict[str, Any], float]]] = defaultdict(list)
    scored_for_graffiti: list[dict[str, Any]] = []

    latencies: list[float] = []
    input_tokens_list: list[int] = []

    total_graffiti = 0
    total_not_incident = 0
    total_contested_campaign = 0
    total_reverted = 0

    for iid in ordered_ids:
        g = items_by_id[iid]
        lbl = g["label"]
        g_lvl = g["level"]
        if lbl == "not-incident":
            total_not_incident += 1
            if g.get("exclusion") == "graffiti":
                total_graffiti += 1
        elif lbl in STRICT_INCIDENT_LABELS:
            total_contested_campaign += 1
        elif lbl == "reverted":
            total_reverted += 1

        row = rows_by_id.get(iid)
        if row is None:
            outc = "missing"
        else:
            outc = str(row.get("outcome") or "failed").lower()
            if row.get("latency_ms") is not None:
                latencies.append(float(row["latency_ms"]))
            usage = row.get("usage")
            if isinstance(usage, dict):
                in_tok = int(usage.get("total_input_tokens") or usage.get("total_tokens") or 0)
                input_tokens_list.append(in_tok)
        outcomes[outc] += 1

        answers = row.get("answers") if (row and outc == "ok" and isinstance(row.get("answers"), dict)) else None

        # 1. Category accuracy & confusion
        pred_cat: str | None = None
        pred_cat_prob: float = 0.0
        if answers and isinstance(answers.get("category"), dict):
            ch = str(answers["category"].get("choice") or "")
            if ch in PRED_CATEGORIES:
                pred_cat = ch
                pred_cat_prob = float(answers["category"].get("confidence") or 0.0)

        if pred_cat in GRAFFITI_CATS:
            if lbl == "not-incident":
                cat_non_inc_removed += 1
                if g.get("exclusion") == "graffiti":
                    cat_graff_removed += 1
            elif lbl in STRICT_INCIDENT_LABELS:
                cat_cc_removed += 1

        g_cat = g.get("category")
        if g_cat in CATEGORIES:
            cat_labelled += 1
            is_inc = lbl in INCIDENT_LABELS
            is_strict = lbl in STRICT_INCIDENT_LABELS
            if is_inc:
                inc_cat_total += 1
            else:
                non_inc_cat_total += 1
            if is_strict:
                strict_cat_total += 1

            if pred_cat is not None:
                cat_ok += 1
                cat_confusion[g_cat][pred_cat] += 1
                if pred_cat == g_cat:
                    cat_exact += 1
                    if is_inc:
                        inc_cat_hits += 1
                    else:
                        non_inc_cat_hits += 1
                    if is_strict:
                        strict_cat_hits += 1
            else:
                cat_confusion[g_cat]["non_ok"] += 1

        # 2. Yes/no (`noul`) questions & nonsense filter input
        p_nonsense: float | None = None
        if answers:
            for k in YES_NO_KEYS:
                q_ans = answers.get(k)
                if isinstance(q_ans, dict) and q_ans.get("noul") is not None:
                    val = float(q_ans["noul"])
                    yn_by_key[k].append((g, val))
                    if k == "nonsense":
                        p_nonsense = val

        scored_for_graffiti.append(
            {
                "id": iid,
                "label": lbl,
                "exclusion": g.get("exclusion"),
                "nonsense": p_nonsense,
                "pred_cat": pred_cat,
                "pred_cat_prob": pred_cat_prob,
            }
        )

        # 3. Ordered `level` probabilities, calibration & judge agreement
        lvl_probs = extract_level_probs(answers.get("level")) if answers else None
        if lvl_probs is not None:
            prob_ge_1 = _clamp01(lvl_probs[1] + lvl_probs[2] + lvl_probs[3])
            prob_ge_2 = _clamp01(lvl_probs[2] + lvl_probs[3])
            y_is_inc.append(1 if lbl != "not-incident" else 0)
            p_is_inc.append(prob_ge_1)
            y_ge_cont.append(1 if lbl in STRICT_INCIDENT_LABELS else 0)
            p_ge_cont.append(prob_ge_2)

            pred_lvl = max((0, 1, 2, 3), key=lambda k: (lvl_probs[k], -k))
            pred_lbl = LEVEL_LABELS[pred_lvl]
            level_confusion[lbl][pred_lbl] += 1
            judge_ok += 1
            if pred_lvl == g_lvl:
                judge_exact += 1
            if abs(pred_lvl - g_lvl) <= 1:
                judge_within_one += 1
            if pred_lvl > g_lvl:
                judge_overclaim += 1
            elif pred_lvl < g_lvl:
                judge_underclaim += 1
        else:
            level_confusion[lbl]["non_ok"] += 1

    graffiti_res = find_graffiti_threshold(
        scored_for_graffiti,
        total_graffiti=total_graffiti,
        total_not_incident=total_not_incident,
        total_contested_campaign=total_contested_campaign,
        total_reverted=total_reverted,
        total_items=n_total,
    )
    graffiti_res["choice_graffiti_any_conf"] = {
        "graffiti_removed": cat_graff_removed,
        "graffiti_removed_share": _safe_div(cat_graff_removed, total_graffiti),
        "not_incident_removed": cat_non_inc_removed,
        "not_incident_removed_share": _safe_div(cat_non_inc_removed, total_not_incident),
        "contested_campaign_removed": cat_cc_removed,
    }

    yes_no_summary: dict[str, Any] = {}
    for k in YES_NO_KEYS:
        pairs = yn_by_key.get(k, [])
        if not pairs:
            continue
        by_lbl: dict[str, list[float]] = defaultdict(list)
        for g, val in pairs:
            by_lbl[g["label"]].append(val)
        y_inc_k = [1 if g["label"] != "not-incident" else 0 for g, _ in pairs]
        y_cc_k = [1 if g["label"] in STRICT_INCIDENT_LABELS else 0 for g, _ in pairs]
        y_gr_k = [
            1 if (g["label"] == "not-incident" and g.get("exclusion") == "graffiti") else 0
            for g, _ in pairs
        ]
        vals_k = [v for _, v in pairs]
        yes_no_summary[k] = {
            "n": len(pairs),
            "mean_by_label": {
                lbl: (sum(by_lbl[lbl]) / len(by_lbl[lbl])) if by_lbl[lbl] else None
                for lbl in LEVEL_LABELS
            },
            "auc_is_incident": compute_auc(y_inc_k, vals_k),
            "auc_ge_contested": compute_auc(y_cc_k, vals_k),
            "auc_is_graffiti": compute_auc(y_gr_k, vals_k),
        }

    total_in_tokens = sum(input_tokens_list)
    denom_tok = max(1, len(input_tokens_list))
    mean_in_tokens = total_in_tokens / denom_tok if input_tokens_list else 0.0
    total_cost_usd = (total_in_tokens / 1_000_000.0) * DECISIONS_PRICE_PER_M_INPUT_USD
    mean_cost_usd = total_cost_usd / denom_tok if input_tokens_list else 0.0

    per_category: dict[str, dict[str, Any]] = {}
    for g_cat in CATEGORIES:
        tp = cat_confusion[g_cat][g_cat]
        sup = sum(cat_confusion[g_cat].values())
        pred_n = sum(cat_confusion[r_cat][g_cat] for r_cat in CATEGORIES)
        per_category[g_cat] = {
            "support": sup,
            "predicted": pred_n,
            "tp": tp,
            "precision": _safe_div(tp, pred_n),
            "recall": _safe_div(tp, sup),
        }

    return {
        "variant": variant_name,
        "kind": "decisions",
        "model": "gemini-decision-1",
        "items_total": n_total,
        "outcomes": dict(outcomes),
        "blocked_count": outcomes.get("blocked", 0),
        "blocked_rate": _safe_div(outcomes.get("blocked", 0), n_total),
        "category_accuracy": {
            "labelled_items": cat_labelled,
            "ok_labelled_items": cat_ok,
            "exact_hits": cat_exact,
            "accuracy_all": _safe_div(cat_exact, cat_labelled),
            "accuracy_ok": _safe_div(cat_exact, cat_ok),
            "incidents_accuracy": _safe_div(inc_cat_hits, inc_cat_total),
            "contested_campaign_accuracy": _safe_div(strict_cat_hits, strict_cat_total),
            "non_incidents_accuracy": _safe_div(non_inc_cat_hits, non_inc_cat_total),
            "none_of_these_chosen": sum(cat_confusion[g]["none_of_these"] for g in CATEGORIES),
        },
        "per_category": per_category,
        "category_confusion": cat_confusion,
        "graffiti_filter": graffiti_res,
        "calibration": {
            "is_incident": {
                "target": "label != not-incident from P(level >= 1)",
                "n": len(y_is_inc),
                "positive_rate": _safe_div(sum(y_is_inc), len(y_is_inc)),
                "auc": compute_auc(y_is_inc, p_is_inc),
                "brier": compute_brier(y_is_inc, p_is_inc),
                **compute_reliability_buckets(y_is_inc, p_is_inc, n_buckets=5),
            },
            "at_least_contested": {
                "target": "label in {contested, campaign} from P(level >= 2)",
                "n": len(y_ge_cont),
                "positive_rate": _safe_div(sum(y_ge_cont), len(y_ge_cont)),
                "auc": compute_auc(y_ge_cont, p_ge_cont),
                "brier": compute_brier(y_ge_cont, p_ge_cont),
                **compute_reliability_buckets(y_ge_cont, p_ge_cont, n_buckets=5),
            },
        },
        "judge_agreement": {
            "ok_items": judge_ok,
            "exact_hits": judge_exact,
            "exact_agreement_ok": _safe_div(judge_exact, judge_ok),
            "exact_agreement_all": _safe_div(judge_exact, n_total),
            "within_one_level_rate": _safe_div(judge_within_one, judge_ok),
            "overclaim_count": judge_overclaim,
            "overclaim_rate": _safe_div(judge_overclaim, judge_ok),
            "underclaim_count": judge_underclaim,
            "underclaim_rate": _safe_div(judge_underclaim, judge_ok),
            "confusion_4x4": level_confusion,
        },
        "yes_no_signals": yes_no_summary,
        "efficiency": {
            "latency_p50_ms": percentile(latencies, 50.0),
            "latency_p95_ms": percentile(latencies, 95.0),
            "latency_mean_ms": statistics.mean(latencies) if latencies else 0.0,
            "mean_input_tokens_per_item": mean_in_tokens,
            "mean_output_tokens_per_item": 0.0,
            "mean_total_tokens_per_item": mean_in_tokens,
            "total_input_tokens": total_in_tokens,
            "mean_cost_usd_per_item": mean_cost_usd,
            "total_cost_usd": total_cost_usd,
        },
    }


def score_generative_comparison(
    s9_a_path: pathlib.Path,
    golden: dict[str, Any],
) -> list[dict[str, Any]]:
    """Extract side-by-side comparison metrics from S9's generative `A.out.jsonl`."""
    if not s9_a_path.exists():
        return []

    rows = load_jsonl(s9_a_path)
    by_model: dict[str, dict[str, dict[str, Any]]] = defaultdict(dict)
    for r in rows:
        md = str(r.get("model") or "")
        iid = str(r.get("id") or "")
        if md and iid:
            by_model[md][iid] = r

    # Canonical order: fast -> hydrogen -> carbon (plus any extras)
    preferred = ["gemini-3.8-flash", "gemini-4-hydrogen", "gemini-4-carbon"]
    ordered_models = [m for m in preferred if m in by_model] + sorted(set(by_model) - set(preferred))

    items_by_id: dict[str, dict[str, Any]] = golden["items_by_id"]
    ordered_ids: list[str] = golden["ordered_ids"]
    n_total = len(ordered_ids)

    total_graffiti = sum(
        1 for iid in ordered_ids
        if items_by_id[iid]["label"] == "not-incident" and items_by_id[iid].get("exclusion") == "graffiti"
    )
    total_not_inc = sum(1 for iid in ordered_ids if items_by_id[iid]["label"] == "not-incident")
    total_cc = sum(1 for iid in ordered_ids if items_by_id[iid]["label"] in STRICT_INCIDENT_LABELS)

    out: list[dict[str, Any]] = []
    for md in ordered_models:
        m_rows = by_model[md]
        outcomes: Counter[str] = Counter()
        cat_labelled = 0
        cat_exact = 0
        graff_removed = 0
        non_inc_removed = 0
        cc_removed = 0
        latencies: list[float] = []
        tot_tokens: list[float] = []
        in_tokens: list[float] = []
        costs: list[float] = []

        for iid in ordered_ids:
            g = items_by_id[iid]
            r = m_rows.get(iid)
            if r is None:
                outcomes["missing"] += 1
                continue
            outc = str(r.get("outcome") or "failed").lower()
            outcomes[outc] += 1
            latencies.append(float(r.get("latency_ms") or 0.0))
            in_t = float(r.get("input_tokens") or 0.0)
            out_t = float(r.get("output_tokens") or 0.0)
            th_t = float(r.get("thought_tokens") or 0.0)
            in_tokens.append(in_t)
            tot_tokens.append(in_t + out_t + th_t)
            costs.append(float(r.get("cost_usd") or 0.0))

            val = r.get("value") if outc == "ok" and isinstance(r.get("value"), dict) else None
            pred_cat = str(val.get("category")) if val and val.get("category") in CATEGORIES else None
            conf = int(val.get("confidence") or 0) if val else 0

            if g.get("category") in CATEGORIES:
                cat_labelled += 1
                if pred_cat == g["category"]:
                    cat_exact += 1

            filtered = bool(pred_cat in GRAFFITI_CATS and conf >= 4)
            if filtered:
                if g["label"] == "not-incident":
                    non_inc_removed += 1
                    if g.get("exclusion") == "graffiti":
                        graff_removed += 1
                elif g["label"] in STRICT_INCIDENT_LABELS:
                    cc_removed += 1

        out.append(
            {
                "variant": f"S9-A ({md})",
                "kind": "generative",
                "model": md,
                "items_total": n_total,
                "outcomes": dict(outcomes),
                "blocked_count": outcomes.get("blocked", 0),
                "blocked_rate": _safe_div(outcomes.get("blocked", 0), n_total),
                "category_accuracy": {
                    "labelled_items": cat_labelled,
                    "exact_hits": cat_exact,
                    "accuracy_all": _safe_div(cat_exact, cat_labelled),
                },
                "graffiti_filter": {
                    "rule": "category in {vandalism, test_or_nonsense} and confidence >= 4",
                    "graffiti_total": total_graffiti,
                    "graffiti_removed": graff_removed,
                    "graffiti_removed_share": _safe_div(graff_removed, total_graffiti),
                    "not_incident_total": total_not_inc,
                    "not_incident_removed": non_inc_removed,
                    "not_incident_removed_share": _safe_div(non_inc_removed, total_not_inc),
                    "contested_campaign_total": total_cc,
                    "contested_campaign_removed": cc_removed,
                },
                "efficiency": {
                    "latency_p50_ms": percentile(latencies, 50.0),
                    "latency_p95_ms": percentile(latencies, 95.0),
                    "latency_mean_ms": statistics.mean(latencies) if latencies else 0.0,
                    "mean_input_tokens_per_item": statistics.mean(in_tokens) if in_tokens else 0.0,
                    "mean_total_tokens_per_item": statistics.mean(tot_tokens) if tot_tokens else 0.0,
                    "mean_cost_usd_per_item": statistics.mean(costs) if costs else 0.0,
                    "total_cost_usd": sum(costs),
                },
            }
        )
    return out


def _fmt_pct(v: float | None) -> str:
    return "—" if v is None else f"{v * 100:.1f}%"


def _fmt_f3(v: float | None) -> str:
    return "—" if v is None else f"{v:.3f}"


def render_markdown(report: dict[str, Any]) -> str:
    lines: list[str] = []
    lines.append(
        f"# P1.S9b Gemini Decisions (`gemini-decision-1`) Scorecard "
        f"(golden `{report['golden_version']}`, dev split, {report['dev_items']} items)"
    )
    lines.append("")
    lines.append("## 1. Side-by-Side Comparison: Decisions API vs Generative S9 (`A.out.jsonl`)")
    lines.append("")
    lines.append(
        "| Run | Model | ok / N | Blocked | Cat acc (235) | Graffiti rule / cutoff | "
        "Graffiti rem (29) | Not-inc rem (94) | Contested/camp lost (64) | p50 / p95 ms | Tok/item | $/item |"
    )
    lines.append("|---|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|")

    for d in report["decisions"]:
        ok_n = d["outcomes"].get("ok", 0)
        tot_n = d["items_total"]
        ca = d["category_accuracy"]
        gf = d["graffiti_filter"]
        ef = d["efficiency"]
        rule_str = f"`nonsense >= {gf['threshold']:.3f}`"
        lines.append(
            f"| **Decisions {d['variant']}** | `{d['model']}` | {ok_n}/{tot_n} | {_fmt_pct(d['blocked_rate'])} | "
            f"{_fmt_pct(ca['accuracy_all'])} | {rule_str} | "
            f"{gf['graffiti_removed']}/{gf['graffiti_total']} ({_fmt_pct(gf['graffiti_removed_share'])}) | "
            f"{gf['not_incident_removed']}/{gf['not_incident_total']} ({_fmt_pct(gf['not_incident_removed_share'])}) | "
            f"**{gf['contested_campaign_removed']}**/{gf['contested_campaign_total']} | "
            f"{ef['latency_p50_ms']:.0f} / {ef['latency_p95_ms']:.0f} | "
            f"{ef['mean_total_tokens_per_item']:.0f} | ${ef['mean_cost_usd_per_item']:.6f} |"
        )

    for g in report["generative_comparison"]:
        ok_n = g["outcomes"].get("ok", 0)
        tot_n = g["items_total"]
        ca = g["category_accuracy"]
        gf = g["graffiti_filter"]
        ef = g["efficiency"]
        lines.append(
            f"| {g['variant']} | `{g['model']}` | {ok_n}/{tot_n} | {_fmt_pct(g['blocked_rate'])} | "
            f"{_fmt_pct(ca['accuracy_all'])} | `vand/test, conf>=4` | "
            f"{gf['graffiti_removed']}/{gf['graffiti_total']} ({_fmt_pct(gf['graffiti_removed_share'])}) | "
            f"{gf['not_incident_removed']}/{gf['not_incident_total']} ({_fmt_pct(gf['not_incident_removed_share'])}) | "
            f"**{gf['contested_campaign_removed']}**/{gf['contested_campaign_total']} | "
            f"{ef['latency_p50_ms']:.0f} / {ef['latency_p95_ms']:.0f} | "
            f"{ef['mean_total_tokens_per_item']:.0f} | ${ef['mean_cost_usd_per_item']:.5f} |"
        )

    lines.append("")
    lines.append("## 2. Graffiti Filter Detail (`nonsense` Probability)")
    lines.append("")
    lines.append(
        "| Variant | Max `nonsense` on contested/campaign | Highest max-graff safe `t` | Aggressive min-safe `t` | "
        "Graffiti rem @ `t` | All not-inc rem @ `t` (aggressive) | Reverted rem @ `t` (aggressive) |"
    )
    lines.append("|---|---:|---:|---:|---:|---:|---:|")
    for d in report["decisions"]:
        gf = d["graffiti_filter"]
        ag = gf["aggressive_min_safe"]
        max_cc_str = f"{gf['max_contested_campaign_nonsense']:.4f} (`{gf['max_contested_campaign_id']}`)"
        lines.append(
            f"| Decisions {d['variant']} | {max_cc_str} | `{gf['threshold']:.4f}` | `{ag['threshold']:.4f}` | "
            f"{gf['graffiti_removed']}/{gf['graffiti_total']} ({_fmt_pct(gf['graffiti_removed_share'])}) | "
            f"{gf['not_incident_removed']}/{gf['not_incident_total']} ({_fmt_pct(gf['not_incident_removed_share'])}) · "
            f"agg {ag['not_incident_removed']}/{ag['not_incident_total']} ({_fmt_pct(ag['not_incident_removed_share'])}) | "
            f"{gf['reverted_removed']}/{gf['reverted_total']} · agg {ag['reverted_removed']}/{ag['reverted_total']} |"
        )

    lines.append("")
    lines.append("## 3. Probability Calibration & Discrimination (`level` Score Distribution)")
    lines.append("")
    lines.append("| Variant | Task | N | Base rate | ROC AUC | Brier | ECE (5-bin) |")
    lines.append("|---|---|---:|---:|---:|---:|---:|")
    for d in report["decisions"]:
        for task_key, task_label in (
            ("is_incident", "Is an incident (`label != not-incident`, `P(level >= 1)`)"),
            ("at_least_contested", "`>= contested` (`label in {contested, campaign}`, `P(level >= 2)`)"),
        ):
            cal = d["calibration"][task_key]
            lines.append(
                f"| Decisions {d['variant']} | {task_label} | {cal['n']} | {_fmt_pct(cal['positive_rate'])} | "
                f"**{_fmt_f3(cal['auc'])}** | **{_fmt_f3(cal['brier'])}** | {_fmt_f3(cal['ece'])} |"
            )

    for d in report["decisions"]:
        lines.append("")
        lines.append(f"### 5-Bucket Reliability Tables — Decisions Variant {d['variant']}")
        lines.append("")
        lines.append(
            "| Bucket | `P(level >= 1)` n | Mean pred | Actual `is_incident` | "
            "`P(level >= 2)` n | Mean pred | Actual `>= contested` |"
        )
        lines.append("|---|---:|---:|---:|---:|---:|---:|")
        b_inc = d["calibration"]["is_incident"]["buckets"]
        b_cc = d["calibration"]["at_least_contested"]["buckets"]
        for bi, bc in zip(b_inc, b_cc):
            lines.append(
                f"| `{bi['range']}` | {bi['count']} | {_fmt_f3(bi['mean_pred'])} | {_fmt_pct(bi['empirical_rate'])} | "
                f"{bc['count']} | {_fmt_f3(bc['mean_pred'])} | {_fmt_pct(bc['empirical_rate'])} |"
            )

    lines.append("")
    lines.append("## 4. Judge Agreement (`argmax(level)` vs Golden Label)")
    lines.append("")
    lines.append("| Variant | ok / N | Exact agreement (ok) | Exact agreement (all) | Within ±1 level | Overclaim rate | Underclaim rate |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|")
    for d in report["decisions"]:
        ja = d["judge_agreement"]
        lines.append(
            f"| Decisions {d['variant']} | {ja['ok_items']}/{d['items_total']} | "
            f"**{_fmt_pct(ja['exact_agreement_ok'])}** ({ja['exact_hits']}/{ja['ok_items']}) | "
            f"{_fmt_pct(ja['exact_agreement_all'])} | {_fmt_pct(ja['within_one_level_rate'])} | "
            f"{_fmt_pct(ja['overclaim_rate'])} ({ja['overclaim_count']}) | "
            f"{_fmt_pct(ja['underclaim_rate'])} ({ja['underclaim_count']}) |"
        )

    for d in report["decisions"]:
        ja = d["judge_agreement"]
        lines.append("")
        lines.append(f"### 4×4 Level Confusion — Decisions Variant {d['variant']} (rows = Golden, cols = `argmax(level)`)")
        lines.append("")
        lines.append("| Golden \ Pred | `not-incident` (0) | `reverted` (1) | `contested` (2) | `campaign` (3) | `non_ok` | Row total |")
        lines.append("|---|---:|---:|---:|---:|---:|---:|")
        for g_lbl in LEVEL_LABELS:
            row_c = ja["confusion_4x4"][g_lbl]
            row_tot = sum(row_c.values())
            cells = [
                f"**{row_c[p]}**" if p == g_lbl and row_c[p] else str(row_c[p])
                for p in (*LEVEL_LABELS, "non_ok")
            ]
            lines.append(f"| `{g_lbl}` | " + " | ".join(cells) + f" | {row_tot} |")

    for d in report["decisions"]:
        lines.append("")
        lines.append(f"## 5. Category Confusion — Decisions Variant {d['variant']}")
        ca = d["category_accuracy"]
        lines.append(
            f"- **Exact accuracy:** {ca['exact_hits']}/{ca['labelled_items']} ({_fmt_pct(ca['accuracy_all'])} all, "
            f"{_fmt_pct(ca['accuracy_ok'])} on ok; incidents {_fmt_pct(ca['incidents_accuracy'])}; "
            f"contested+campaign {_fmt_pct(ca['contested_campaign_accuracy'])}; "
            f"non-incidents {_fmt_pct(ca['non_incidents_accuracy'])}; `none_of_these` chosen {ca['none_of_these_chosen']} times)."
        )
        lines.append("")
        cols = [*PRED_CATEGORIES, "non_ok"]
        lines.append("| Golden \ Pred | " + " | ".join(CAT_SHORT[c] for c in cols) + " | Recall |")
        lines.append("|---|" + "|".join("---:" for _ in cols) + "|---:|")
        for g_cat in CATEGORIES:
            rc = d["category_confusion"][g_cat]
            rec_v = d["per_category"][g_cat]["recall"]
            cells = [
                f"**{rc[c]}**" if c == g_cat and rc[c] else str(rc[c])
                for c in cols
            ]
            lines.append(f"| `{CAT_SHORT[g_cat]}` | " + " | ".join(cells) + f" | {_fmt_pct(rec_v)} |")

    lines.append("")
    lines.append("## 6. Auxiliary Yes/No (`noul`) Question Discrimination")
    lines.append("")
    lines.append(
        "| Variant | Question | Mean (`not-inc`) | Mean (`reverted`) | Mean (`contested`) | Mean (`campaign`) | "
        "AUC (`is_incident`) | AUC (`>= contested`) | AUC (`is_graffiti`) |"
    )
    lines.append("|---|---|---:|---:|---:|---:|---:|---:|---:|")
    for d in report["decisions"]:
        for k in YES_NO_KEYS:
            yn = d["yes_no_signals"].get(k)
            if not yn:
                continue
            m = yn["mean_by_label"]
            lines.append(
                f"| {d['variant']} | `{k}` | {_fmt_f3(m['not-incident'])} | {_fmt_f3(m['reverted'])} | "
                f"{_fmt_f3(m['contested'])} | {_fmt_f3(m['campaign'])} | "
                f"{_fmt_f3(yn['auc_is_incident'])} | {_fmt_f3(yn['auc_ge_contested'])} | {_fmt_f3(yn['auc_is_graffiti'])} |"
            )

    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Score P1.S9b Gemini Decisions API runs on golden dev.")
    ap.add_argument(
        "--golden",
        type=pathlib.Path,
        default=REPO_ROOT / "research" / "eval" / "golden.json",
        help="Path to golden.json (only split='dev' is read).",
    )
    ap.add_argument(
        "--variant-a",
        type=pathlib.Path,
        default=HERE / "results" / "decisions.A.jsonl",
        help="Path to Variant A Decisions JSONL output.",
    )
    ap.add_argument(
        "--variant-c",
        type=pathlib.Path,
        default=HERE / "results" / "decisions.C.jsonl",
        help="Path to Variant C Decisions JSONL output.",
    )
    ap.add_argument(
        "--s9-a",
        type=pathlib.Path,
        default=REPO_ROOT / "research" / "36-s9-classify" / "results" / "A.out.jsonl",
        help="Path to generative S9 A.out.jsonl for side-by-side comparison.",
    )
    ap.add_argument(
        "--out-md",
        type=pathlib.Path,
        default=HERE / "results" / "s9b-scorecard.md",
        help="Output Markdown scorecard path.",
    )
    ap.add_argument(
        "--out-json",
        type=pathlib.Path,
        default=HERE / "results" / "s9b-scorecard.json",
        help="Output JSON scorecard path.",
    )
    ap.add_argument("--json", action="store_true", help="Print JSON to stdout instead of Markdown.")
    args = ap.parse_args(argv)

    golden = load_dev_golden(args.golden)
    decisions_scored: list[dict[str, Any]] = []

    for label, path in (("A", args.variant_a), ("C", args.variant_c)):
        if path and path.exists():
            decisions_scored.append(score_decisions_variant(label, load_jsonl(path), golden))

    if not decisions_scored:
        raise FileNotFoundError(
            f"Neither {args.variant_a} nor {args.variant_c} exists; run `run.py --variant A` and `C` first."
        )

    gen_comparison = score_generative_comparison(args.s9_a, golden)

    report = {
        "golden_version": golden["golden_version"],
        "dataset": golden["dataset"],
        "split": "dev",
        "dev_items": len(golden["ordered_ids"]),
        "decisions_price_per_m_input_usd": DECISIONS_PRICE_PER_M_INPUT_USD,
        "decisions": decisions_scored,
        "generative_comparison": gen_comparison,
    }

    md_text = render_markdown(report)
    json_text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"

    args.out_md.parent.mkdir(parents=True, exist_ok=True)
    args.out_md.write_text(md_text, encoding="utf-8")
    args.out_json.parent.mkdir(parents=True, exist_ok=True)
    args.out_json.write_text(json_text, encoding="utf-8")

    sys.stdout.write(json_text if args.json else md_text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
