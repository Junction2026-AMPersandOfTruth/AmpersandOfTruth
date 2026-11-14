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
import base64
from collections import defaultdict
import hashlib
import json
import math
import pathlib
import struct
import sys
import time
from typing import Any, Callable

HERE = pathlib.Path(__file__).resolve().parent
RESEARCH_DIR = HERE.parent
DEFAULT_EMBEDDINGS_CACHE = HERE / "results" / "embeddings.cache.json"

sys.path.insert(0, str(RESEARCH_DIR / "lib"))
sys.path.insert(0, str(RESEARCH_DIR / "eval"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
from evidence import (  # noqa: E402
    EDIT_WAR_SOURCES,
    VALID_DISABLES,
    AssessmentMeta,
    CandidateCluster,
    apply_disables,
    load_assessments,
    load_candidates_for_pages,
)
from export_classify import score_variant_a  # noqa: E402
from tiers import (  # noqa: E402
    DEFAULT_THRESHOLDS,
    TIER_LEVELS,
    EvidenceTier,
    IncidentEvidence,
    TierAssessment,
    TierThresholds,
    assess as assess_tier,
    parse_threshold_overrides,
    thresholds_to_dict,
)


def embed_cache_key(text: str) -> str:
    """Privacy-safe normalised text key (24-hex SHA-1 of whitespace-collapsed text)."""
    norm = " ".join((text or "").split())
    return hashlib.sha1(norm.encode("utf-8")).hexdigest()[:24]


def encode_vector_f16_b64(vec: list[float]) -> str:
    """Pack a float vector as little-endian IEEE-754 binary16 base64."""
    return base64.b64encode(struct.pack(f"<{len(vec)}e", *vec)).decode("ascii")


def decode_cached_vector(raw: Any, dtype: str | None = None, dim: int = 1024) -> list[float] | None:
    """Decode a cached vector from `list[float]` or base64 little-endian float16/float32."""
    if raw is None:
        return None
    if isinstance(raw, list):
        return [float(x) for x in raw]
    if isinstance(raw, str):
        buf = base64.b64decode(raw)
        if dtype == "float32_le_b64" or (dtype is None and len(buf) == dim * 4):
            n = len(buf) // 4
            return list(struct.unpack(f"<{n}f", buf))
        n = len(buf) // 2
        return list(struct.unpack(f"<{n}e", buf))
    return None


def load_embedding_cache(
    cache_path: str | pathlib.Path | None = DEFAULT_EMBEDDINGS_CACHE,
) -> Callable[[str], list[float] | None]:
    """Return a read-only lookup `text -> vector | None` backed solely by `cache_path` (no network calls)."""
    if cache_path is None:
        return lambda _text: None
    p = pathlib.Path(cache_path)
    if not p.exists():
        return lambda _text: None

    doc = json.loads(p.read_text(encoding="utf-8"))
    dtype = doc.get("dtype") if isinstance(doc, dict) else None
    dim = int(doc.get("dim") or 1024) if isinstance(doc, dict) else 1024
    vectors: dict[str, Any] = (
        doc["vectors"] if (isinstance(doc, dict) and isinstance(doc.get("vectors"), dict)) else doc
    )
    decoded_memo: dict[str, list[float] | None] = {}

    def lookup(text: str) -> list[float] | None:
        norm = " ".join((text or "").split())
        if not norm:
            return None
        hkey = embed_cache_key(norm)
        if hkey in decoded_memo:
            return decoded_memo[hkey]
        raw = vectors.get(hkey)
        if raw is None:
            raw = vectors.get(norm)
        if raw is None:
            raw = vectors.get(text)
        vec = decode_cached_vector(raw, dtype=dtype, dim=dim)
        decoded_memo[hkey] = vec
        return vec

    return lookup


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
    thresholds: TierThresholds = DEFAULT_THRESHOLDS,
    embed_fn: Callable[[str], list[float] | None] | None = None,
    embeddings_cache_path: str | pathlib.Path | None = DEFAULT_EMBEDDINGS_CACHE,
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
            "rel_cache_hits": 0,
            "rel_cache_misses": 0,
        }

    req_pages = {int(p) for p in requested_page_ids}
    min_level = TIER_LEVELS[min_tier]

    embedder: Callable[[str], list[float] | None] | None = None
    q_vec: list[float] | None = None
    rel_cache_hits = 0
    rel_cache_misses = 0
    if mode == "B+rel":
        # Read ONLY the precomputed cache at request time unless a test injects `embed_fn`.
        embedder = embed_fn if embed_fn is not None else load_embedding_cache(embeddings_cache_path)
        if request_text and request_text.strip():
            q_vec = embedder(request_text.strip())
            if q_vec is None:
                rel_cache_misses += 1
            else:
                rel_cache_hits += 1

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

        ta = assess_tier(ev, thresholds)
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
            c_vec = embedder(rel_text) if (embedder is not None and q_vec is not None and rel_text) else None
            if q_vec is not None and rel_text:
                if c_vec is not None:
                    rel_cache_hits += 1
                    rel = max(0.0, cosine_similarity(q_vec, c_vec))
                else:
                    rel_cache_misses += 1
                    rel = 1.0
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
        "rel_cache_hits": rel_cache_hits,
        "rel_cache_misses": rel_cache_misses,
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
    sides: bool = True,
    edit_war_source: str = "passage",
    thresholds: TierThresholds = DEFAULT_THRESHOLDS,
    embeddings_cache_path: str | pathlib.Path | None = DEFAULT_EMBEDDINGS_CACHE,
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
        sides=sides,
        edit_war_source=edit_war_source,
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
        thresholds=thresholds,
        embeddings_cache_path=embeddings_cache_path,
    )

    elapsed = time.monotonic() - t0
    aot.log(
        f"s10 ({mode}, sides={'on' if sides else 'off'}, ew={edit_war_source}): "
        f"pages={len(page_ids)}, clusters={load_stats['clusters']}, "
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
            "sides": "on" if sides else "off",
            "edit_war_source": edit_war_source,
            "thresholds": thresholds_to_dict(thresholds),
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
    ap.add_argument(
        "--threshold",
        action="append",
        default=[],
        metavar="NAME=VALUE",
        help="Override any `TierThresholds` field (repeatable, e.g. `--threshold min_coordination_signals=3`).",
    )
    ap.add_argument(
        "--edit-war-source",
        choices=sorted(EDIT_WAR_SOURCES),
        default="passage",
        help="Edit-war signal source: `passage` (cluster back-and-forth, default) or `s4` (page-level S4 episode).",
    )
    ap.add_argument(
        "--sides",
        choices=("on", "off"),
        default="on",
        help="Orient cluster units into side 1 vs opposite side 2 (default: `on`).",
    )
    ap.add_argument(
        "--embeddings-cache",
        default=str(DEFAULT_EMBEDDINGS_CACHE),
        help="Read-only bge-m3 embedding cache JSON path for `--mode B+rel`.",
    )
    ap.add_argument("--method", choices=("exact", "fuzzy"), default="fuzzy", help="S7 cluster method.")
    args = ap.parse_args(argv)

    try:
        thresholds = parse_threshold_overrides(args.threshold)
    except ValueError as ex:
        ap.error(str(ex))

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
        sides=(args.sides == "on"),
        edit_war_source=args.edit_war_source,
        thresholds=thresholds,
        embeddings_cache_path=args.embeddings_cache,
    )
    json.dump(resp, sys.stdout, ensure_ascii=False)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
