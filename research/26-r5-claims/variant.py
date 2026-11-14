#!/usr/bin/env python3
"""P1.R5 (#26, #94) retrieval variant: claim-arm search and RRF hybrid with R1 (contract §2.1).

    python3 research/26-r5-claims/variant.py [--mode claims|hybrid] < request.json

--mode claims  cosine top-k over research.r5_claim_vector, grouped by page_id (best passage wins), why: ["claim"].
--mode hybrid  (default) RRF (k=60) fusion of R1 (all-langs folded article vectors) and the claim arm.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

RRF_K = 60


def _sql_retry(query: str) -> list[dict]:
    for attempt in range(3):
        try:
            return aot.sql(query)
        except RuntimeError:
            if attempt == 2:
                raise
            time.sleep(0.3 * (attempt + 1))
    return []


def nearest_claims(vec: list[float], k_pages: int) -> tuple[list[dict], list[dict]]:
    """Query HNSW index on research.r5_claim_vector; return (raw_passages, grouped_by_page)."""
    fetch = max(k_pages * 8, 100)
    rows = _sql_retry(
        f"""
        SELECT c.passage_id, c.page_id, c.removing_rev_id, a.title, v.distance
        FROM (
            SELECT passage_id, VEC_DISTANCE_COSINE(embedding, {aot.vec_literal(vec)}) AS distance
            FROM research.r5_claim_vector
            ORDER BY distance
            LIMIT {int(fetch)}
        ) v
        JOIN research.r5_claim_vector c USING (passage_id)
        LEFT JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = c.page_id
        ORDER BY v.distance ASC, c.passage_id ASC
        """
    )
    raw_passages = [
        {
            "passage_id": r["passage_id"],
            "page_id": int(r["page_id"]),
            "removing_rev_id": int(r["removing_rev_id"]),
            "title": r["title"],
            "distance": float(r["distance"]),
        }
        for r in rows
    ]
    seen_pages: dict[int, dict] = {}
    for p in raw_passages:
        pid = p["page_id"]
        if pid not in seen_pages:
            seen_pages[pid] = p
            if len(seen_pages) >= k_pages:
                break
    return raw_passages, list(seen_pages.values())


def retrieve(text: str, k: int, mode: str) -> dict:
    t0 = time.monotonic()
    vec = aot.embed(text)
    t1 = time.monotonic()

    pool = max(k * 2, 50)
    raw_passages, claim_pages = nearest_claims(vec, pool if mode == "hybrid" else k)

    if mode == "claims":
        t2 = time.monotonic()
        top = claim_pages[:k]
        best_dist = top[0]["distance"] if top else None
        aot.log(
            f"r5(claims): {len(raw_passages)} passages -> {len(top)} pages, best distance {best_dist}, "
            f"embed {t1 - t0:.2f}s, search {t2 - t1:.2f}s"
        )
        return {
            "contract": 1,
            "out_of_scope": False,
            "results": [
                {"page_id": h["page_id"], "score": round(1.0 - h["distance"], 5), "why": ["claim"]}
                for h in top
            ],
            "cost": aot.cost(),
            "debug": {
                "mode": "claims",
                "best_distance": round(best_dist, 5) if best_dist is not None else None,
                "embed_s": round(t1 - t0, 3),
                "search_s": round(t2 - t1, 3),
                "top_passages": [
                    {
                        "passage_id": p["passage_id"],
                        "page_id": p["page_id"],
                        "removing_rev_id": p["removing_rev_id"],
                        "distance": round(p["distance"], 5),
                    }
                    for p in raw_passages[:20]
                ],
                "claim_pages": [
                    {
                        "page_id": p["page_id"],
                        "passage_id": p["passage_id"],
                        "distance": round(p["distance"], 5),
                        "title": p["title"],
                    }
                    for p in top
                ],
            },
        }

    # Hybrid mode: RRF (k=60) of R1 (all-langs folded article vectors) and claim arm.
    art_hits = aot.nearest(vec, pool * 3, None)
    r1_pages = aot.fold_to_fi(art_hits)[:pool]
    t2 = time.monotonic()

    rrf_scores: dict[int, float] = {}
    whys: dict[int, list[str]] = {}
    best_dists: dict[int, float] = {}

    for rank, h in enumerate(r1_pages, 1):
        pid = h["page_id"]
        rrf_scores[pid] = rrf_scores.get(pid, 0.0) + 1.0 / (RRF_K + rank)
        whys.setdefault(pid, []).append("vector")
        best_dists[pid] = min(best_dists.get(pid, h["distance"]), h["distance"])

    for rank, p in enumerate(claim_pages, 1):
        pid = p["page_id"]
        rrf_scores[pid] = rrf_scores.get(pid, 0.0) + 1.0 / (RRF_K + rank)
        whys.setdefault(pid, []).append("claim")
        best_dists[pid] = min(best_dists.get(pid, p["distance"]), p["distance"])

    fused_ids = sorted(
        rrf_scores.keys(),
        key=lambda pid: (-rrf_scores[pid], best_dists[pid], pid),
    )[:k]

    results = [
        {"page_id": pid, "score": round(rrf_scores[pid], 6), "why": whys[pid]}
        for pid in fused_ids
    ]
    aot.log(
        f"r5(hybrid): r1={len(r1_pages)} + claims={len(claim_pages)} -> {len(results)} pages, "
        f"embed {t1 - t0:.2f}s, search {t2 - t1:.2f}s"
    )
    return {
        "contract": 1,
        "out_of_scope": False,
        "results": results,
        "cost": aot.cost(),
        "debug": {
            "mode": "hybrid",
            "rrf_k": RRF_K,
            "embed_s": round(t1 - t0, 3),
            "search_s": round(t2 - t1, 3),
            "top_passages": [
                {
                    "passage_id": p["passage_id"],
                    "page_id": p["page_id"],
                    "removing_rev_id": p["removing_rev_id"],
                    "distance": round(p["distance"], 5),
                }
                for p in raw_passages[:20]
            ],
            "claim_pages": [
                {
                    "page_id": p["page_id"],
                    "passage_id": p["passage_id"],
                    "distance": round(p["distance"], 5),
                    "title": p["title"],
                }
                for p in claim_pages[:20]
            ],
            "r1_pages": [
                {
                    "page_id": h["page_id"],
                    "distance": round(h["distance"], 5),
                    "via": h["via_lang"],
                    "title": h["title"],
                }
                for h in r1_pages[:20]
            ],
        },
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--mode", default="hybrid", choices=["claims", "hybrid"])
    args = ap.parse_args()
    req = json.load(sys.stdin)
    if req.get("kind") != "retrieval":
        sys.exit(f"r5 handles only kind 'retrieval', got {req.get('kind')!r}")
    resp = retrieve(req["text"], int(req["k"]), args.mode)
    json.dump(resp, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
