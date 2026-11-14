#!/usr/bin/env python3
"""P1.S10 (#37) signals variant (CONTRACT.md §2.2): incident assembly, tiering, and ranking.

Modes:
  - A     : Hand-weighted additive score over IncidentEvidence, prose length, and exposure.
  - B     : Rule-based tiers first (`campaign > contested > reverted` via 1:1 `TierRules` port),
            ordered within each tier by Variant A's score.
  - B+rel : Mode B, with within-tier score multiplied by topic relevance (`max(0, cos(bge-m3(text), bge-m3(claim)))`).

Validation:
    python3 research/eval/validate.py variant signals -- python3 research/37-s10-assembly/variant.py --mode A
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import json
import math
import pathlib
import sys
import time
from typing import Any, Callable

HERE = pathlib.Path(__file__).resolve().parent
RESEARCH_DIR = HERE.parent
sys.path.insert(0, str(RESEARCH_DIR / "lib"))
sys.path.insert(0, str(RESEARCH_DIR / "eval"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
from evidence import (  # noqa: E402
    VALID_DISABLES,
    AssessmentMeta,
    CandidateCluster,
    apply_disables,
    load_assessments,
    load_candidates_for_pages,
)
from export_classify import score_variant_a  # noqa: E402
from tiers import (  # noqa: E402
    TIER_LEVELS,
    EvidenceTier,
    IncidentEvidence,
    TierAssessment,
    assess as assess_tier,
)


def cosine_similarity(u: list[float], v: list[float]) -> float:
    """Cosine similarity of two equal-length vectors (`0.0` if either norm is zero)."""
    dot = sum(a * b for a, b in zip(u, v))
    nu = math.sqrt(sum(a * a for a in u))
    nv = math.sqrt(sum(b * b for b in v))
    if nu <= 0.0 or nv <= 0.0:
        return 0.0
    return dot / (nu * nv)


def _article_bucket(cand: CandidateCluster, requested_pages: set[int]) -> int:
    if cand.page_id in requested_pages:
        return cand.page_id
    for pid in cand.other_page_ids:
        if pid in requested_pages:
            return pid
    return cand.page_id


def _claim_or_prose_for_relevance(cand: CandidateCluster, meta: AssessmentMeta | None) -> str:
    if meta is not None and meta.claim_en and meta.claim_en.strip():
        return meta.claim_en.strip()[:500]
    prose = (cand.inserted_prose_text or "").strip()
    if prose:
        return prose[:500]
    return (cand.inserted_raw_text or "").strip()[:500]


def format_assessment_payload(meta: AssessmentMeta | None) -> dict[str, Any] | None:
    """Format the #109 `assessment` object (`IncidentAssessment` shape), masking quotes for IP safety."""
    if meta is None:
        return None
    return {
        "category": meta.category.value,
        "confidence": meta.confidence,
        "what_changed_en": meta.what_changed_en,
        "quotes": [aot.mask_comment(q) for q in meta.quotes],
        "has_verified_quote": meta.has_verified_quote,
        "uncertainty_note": meta.uncertainty_note,
        "historical_note_en": meta.historical_note_en,
    }


def build_incident_dict(
    cand: CandidateCluster,
    ev: IncidentEvidence,
    ta: TierAssessment,
    meta: AssessmentMeta | None,
    score: float,
) -> dict[str, Any]:
    """Build one CONTRACT.md §3 (+ #109) incident dictionary."""
    assert ta.tier is not None
    inc: dict[str, Any] = {
        "id": cand.incident_key,
        "cluster_id": str(cand.cluster_id),
        "page_id": cand.page_id,
        "inserting_rev_ids": list(cand.inserting_rev_ids),
        "removing_rev_ids": list(cand.removing_rev_ids),
        "tier": ta.tier.value,
        "tier_reason": ta.because_sentence,
        "score": round(score, 6),
        "claim_en": meta.claim_en if meta else None,
        "claim_fi": meta.claim_fi if meta else None,
        "summary_en": meta.what_changed_en if meta else None,
        "narrative": meta.narrative_en if meta else None,
        "category": meta.category.value if meta else None,
        "confidence_note": meta.uncertainty_note if meta else None,
        "first_inserted_at": cand.first_inserted_at,
        "last_removed_at": cand.last_removed_at,
        "total_exposure_seconds": cand.total_exposure_seconds,
        "reinsertion_count": ev.reinsertion_count,
        "signals": [{"kind": r.kind.value, "label": r.text} for r in ta.reasons],
        "articles": [dict(a) for a in cand.articles],
        "assessment": format_assessment_payload(meta),
    }
    if cand.other_page_ids:
        inc["other_page_ids"] = list(cand.other_page_ids)
    return inc


def rank_and_assemble_incidents(
    candidates: list[CandidateCluster],
    request_text: str,
    requested_page_ids: list[int],
    limit: int,
    mode: str = "B",
    cap_per_article: int = 5,
    min_tier: EvidenceTier = EvidenceTier.REVERTED,
    disabled: set[str] | frozenset[str] = frozenset(),
    embed_fn: Callable[[str], list[float]] | None = None,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    """Pure ranking, tiering, per-article capping, and incident dictionary assembly over `candidates`."""
    if mode not in ("A", "B", "B+rel"):
        raise ValueError(f"Unsupported mode {mode!r}; expected A, B, or B+rel.")
    if limit <= 0 or not candidates:
        return [], {
            "evaluated": len(candidates),
            "excluded": 0,
            "below_min_tier": 0,
            "capped": 0,
            "returned": 0,
            "llm_calls": 0,
            "input_tokens": 0,
            "output_tokens": 0,
            "cost_usd": 0.0,
        }

    embedder = embed_fn or aot.embed
    req_pages = {int(p) for p in requested_page_ids}
    min_level = TIER_LEVELS[min_tier]

    q_vec: list[float] | None = None
    if mode == "B+rel" and request_text and request_text.strip():
        q_vec = embedder(request_text.strip())

    scored_rows: list[tuple[tuple[float, ...], float, CandidateCluster, IncidentEvidence, TierAssessment, AssessmentMeta | None]] = []
    excluded_count = 0
    below_min_count = 0

    llm_calls = 0
    input_tokens = 0
    output_tokens = 0
    cost_usd = 0.0

    for cand in candidates:
        if req_pages and not (cand.member_page_ids & req_pages):
            continue

        ev, meta = apply_disables(cand, disabled=disabled)
        if meta is not None:
            llm_calls += 1
            input_tokens += meta.input_tokens
            output_tokens += meta.output_tokens
            cost_usd += meta.cost_usd

        ta = assess_tier(ev)
        if not ta.is_incident or ta.tier is None:
            excluded_count += 1
            continue

        t_lvl = TIER_LEVELS[ta.tier]
        if t_lvl < min_level:
            below_min_count += 1
            continue

        a_score = score_variant_a(ev, cand.prose_chars, cand.total_exposure_seconds)

        if mode == "A":
            final_score = a_score
            sort_key: tuple[float, ...] = (
                -a_score,
                float(-cand.prose_chars),
                float(-cand.removing_rev_ids[-1]),
            )
        elif mode == "B":
            final_score = t_lvl * 100.0 + a_score
            sort_key = (
                float(-t_lvl),
                -a_score,
                float(-cand.prose_chars),
                float(-cand.removing_rev_ids[-1]),
            )
        else:  # B+rel
            rel_text = _claim_or_prose_for_relevance(cand, meta)
            if q_vec is not None and rel_text:
                c_vec = embedder(rel_text)
                rel = max(0.0, cosine_similarity(q_vec, c_vec))
            else:
                rel = 1.0
            within_score = a_score * rel
            final_score = t_lvl * 100.0 + within_score
            sort_key = (
                float(-t_lvl),
                -within_score,
                -a_score,
                float(-cand.prose_chars),
                float(-cand.removing_rev_ids[-1]),
            )

        scored_rows.append((sort_key, final_score, cand, ev, ta, meta))

    scored_rows.sort(key=lambda row: row[0])

    incidents: list[dict[str, Any]] = []
    seen_keys: set[str] = set()
    per_page_counts: dict[int, int] = defaultdict(int)
    capped_count = 0

    for _, final_score, cand, ev, ta, meta in scored_rows:
        if len(incidents) >= limit:
            break
        if cand.incident_key in seen_keys:
            continue
        bucket = _article_bucket(cand, req_pages) if req_pages else cand.page_id
        if cap_per_article > 0 and per_page_counts[bucket] >= cap_per_article:
            capped_count += 1
            continue
        per_page_counts[bucket] += 1
        seen_keys.add(cand.incident_key)
        incidents.append(build_incident_dict(cand, ev, ta, meta, final_score))

    diag = {
        "evaluated": len(candidates),
        "excluded": excluded_count,
        "below_min_tier": below_min_count,
        "capped": capped_count,
        "returned": len(incidents),
        "llm_calls": llm_calls,
        "input_tokens": input_tokens,
        "output_tokens": output_tokens,
        "cost_usd": round(cost_usd, 6),
    }
    return incidents, diag


def run_signals_request(
    req: dict[str, Any],
    mode: str = "B",
    assessments_path: str | None = None,
    assessment_model: str | None = None,
    cap_per_article: int = 5,
    min_tier: EvidenceTier = EvidenceTier.REVERTED,
    disabled: set[str] | frozenset[str] = frozenset(),
    method: str = "fuzzy",
) -> dict[str, Any]:
    t0 = time.monotonic()
    page_ids = [int(p) for p in req.get("page_ids") or ()]
    limit = int(req.get("limit") or 50)
    text = str(req.get("text") or "")

    if not page_ids or limit <= 0:
        return {
            "contract": 1,
            "incidents": [],
            "cost": {"llm_calls": 0, "input_tokens": 0, "output_tokens": 0, "cost_usd": 0.0, "sql_queries": 0},
        }

    use_assessments = bool(assessments_path) and ("assessment" not in disabled)
    assessments_by_key = (
        load_assessments(assessments_path, model=assessment_model) if use_assessments else {}
    )

    candidates, load_stats = load_candidates_for_pages(
        page_ids,
        assessments_by_key=assessments_by_key if use_assessments else None,
        method=method,
        include_edit_diffs_for_assessed=use_assessments,
    )

    incidents, rank_diag = rank_and_assemble_incidents(
        candidates=candidates,
        request_text=text,
        requested_page_ids=page_ids,
        limit=limit,
        mode=mode,
        cap_per_article=cap_per_article,
        min_tier=min_tier,
        disabled=disabled,
    )

    elapsed = time.monotonic() - t0
    aot.log(
        f"s10 ({mode}): pages={len(page_ids)}, clusters={load_stats['clusters']}, "
        f"excl={rank_diag['excluded']}, capped={rank_diag['capped']} -> "
        f"{len(incidents)} incident(s) in {elapsed:.2f}s"
    )

    return {
        "contract": 1,
        "incidents": incidents,
        "cost": {
            "llm_calls": rank_diag["llm_calls"],
            "input_tokens": rank_diag["input_tokens"],
            "output_tokens": rank_diag["output_tokens"],
            "cost_usd": rank_diag["cost_usd"],
            "sql_queries": aot.STATS["sql_queries"],
        },
        "debug": {
            "mode": mode,
            "method": method,
            "cap_per_article": cap_per_article,
            "min_tier": min_tier.value,
            "disabled": sorted(disabled),
            **load_stats,
            **rank_diag,
            "elapsed_s": round(elapsed, 3),
        },
    }


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--mode", choices=("A", "B", "B+rel"), default="B", help="Scoring/ranking mode.")
    ap.add_argument(
        "--assessments",
        help="Optional S9 `classify-edit` batch JSONL path (or `path:model`).",
    )
    ap.add_argument("--assessment-model", help="Explicit model filter when `--assessments` has multiple models.")
    ap.add_argument(
        "--cap-per-article",
        type=int,
        default=5,
        help="Maximum shown incidents per article (`<= 0` disables cap; default: 5).",
    )
    ap.add_argument(
        "--min-tier",
        choices=("reverted", "contested", "campaign"),
        default="reverted",
        help="Minimum evidence tier to emit (default: reverted).",
    )
    ap.add_argument(
        "--disable",
        action="append",
        default=[],
        choices=sorted(VALID_DISABLES),
        help="Ablate a signal (`removal_reason`, `reinsertion`, `edit_war`, `dispute_template`, `assessment`).",
    )
    ap.add_argument("--method", choices=("exact", "fuzzy"), default="fuzzy", help="S7 cluster method.")
    args = ap.parse_args(argv)

    req = json.load(sys.stdin)
    if req.get("kind") != "signals":
        sys.exit(f"s10 variant handles only kind 'signals', got {req.get('kind')!r}")

    resp = run_signals_request(
        req=req,
        mode=args.mode,
        assessments_path=args.assessments,
        assessment_model=args.assessment_model,
        cap_per_article=args.cap_per_article,
        min_tier=EvidenceTier(args.min_tier),
        disabled=frozenset(args.disable or ()),
        method=args.method,
    )
    json.dump(resp, sys.stdout, ensure_ascii=False)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
