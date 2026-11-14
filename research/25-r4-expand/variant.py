#!/usr/bin/env python3
"""P1.R4 (#25): expand retrieval through the category tree, cross-language siblings, and revert activity.

    python3 research/25-r4-expand/variant.py --mode A|B|C|D [--seed-k 10] [--cap 20] < request.json

Modes (Contract §2.1 retrieval variant; logs to stderr):
  A  category neighbours: R1 top `seed-k` (all-langs fold) -> each seed fi page's most specific
     category (smallest member count in [2, max-cat-size], skipping 'Suomen historia' and maintenance
     patterns) -> candidate fi members ranked by VEC_DISTANCE_COSINE -> append <= `cap` new pages.
  B  category-scoped search: 3 best-matching categories from R1's top-5 fi hits -> walk
     `corpus.category_link` down via recursive CTE (depth <= 3; aborts if CTE > 5 s) -> rank all
     member fi pages by cosine distance.
  C  cross-language fold variants (`--fold rrf|all|fi|en`, default `rrf`): Reciprocal Rank Fusion
     (k0=60) of exact fi-only rank and wikidata-folded en-only rank, vs R1 all-langs raw-distance
     fold, exact fi-only, and en-only-then-fold.
  D  contested-first re-rank: re-rank R1's top 30 by `distance - w * log1p(reverts)` using
     `research.s1_page_stats.signal_total` if the table exists, else `research.prof_rev_reverts`
     `COUNT(*) GROUP BY page_id` (`--w`, default 0.02).
"""
from __future__ import annotations

import argparse
import json
import math
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

CTE_TIMEOUT_S = 5.0
RRF_K0 = 60
_TRANSIENT_SQL = ("ERROR 2026", "ERROR 2002", "ERROR 2013", "Lost connection")


def _sql(query: str, timeout: int = 110) -> list[dict]:
    """Call `aot.sql` with a single retry on transient SSH-tunnel TLS/socket resets."""
    for attempt in range(3):
        try:
            return aot.sql(query, timeout=timeout)
        except RuntimeError as exc:
            msg = str(exc)
            if attempt < 2 and any(code in msg for code in _TRANSIENT_SQL):
                time.sleep(0.3 * (attempt + 1))
                continue
            raise
    return []


def _why_from_via(via_lang: str) -> list[str]:
    return ["vector"] if via_lang == "fi" else ["vector", "wikidata"]


def _r1_fold(vec: list[float], want_fi: int) -> list[dict]:
    """Fetch HNSW all-langs nearest vectors and fold en/sv hits to fi via wikidata_id in one SQL round-trip."""
    fetch = max(want_fi * 3, 60)
    vlit = aot.vec_literal(vec)
    rows = _sql(f"""
        SELECT IF(a.lang = 'fi', a.page_id, fi.page_id) AS fi_page_id,
               IF(a.lang = 'fi', a.title, fi.title) AS fi_title,
               a.lang AS via_lang,
               v.distance
        FROM (
            SELECT lang, page_id, VEC_DISTANCE_COSINE(embedding, {vlit}) AS distance
            FROM corpus.article_vector
            ORDER BY distance ASC
            LIMIT {int(fetch)}
        ) v
        JOIN corpus.article a ON a.lang = v.lang AND a.page_id = v.page_id
        LEFT JOIN corpus.article fi
          ON a.lang <> 'fi'
         AND a.wikidata_id IS NOT NULL
         AND a.wikidata_id <> ''
         AND fi.lang = 'fi'
         AND fi.wikidata_id = a.wikidata_id
        WHERE a.lang = 'fi' OR fi.page_id IS NOT NULL
        ORDER BY v.distance ASC
    """)
    seen: dict[int, dict] = {}
    for r in rows:
        pid = int(r["fi_page_id"])
        if pid not in seen:
            seen[pid] = {
                "page_id": pid,
                "distance": float(r["distance"]),
                "via_lang": r["via_lang"],
                "title": r["fi_title"],
            }
            if len(seen) >= want_fi:
                break
    return list(seen.values())


def _is_oos(best_distance: float | None, oos: float | None) -> bool:
    return oos is not None and (best_distance is None or best_distance > oos)


# ── Mode A: Category neighbours ──────────────────────────────────────────────

def retrieve_mode_a(
    vec: list[float],
    k: int,
    seed_k: int,
    cap: int,
    max_cat_size: int,
    oos: float | None,
) -> tuple[list[dict], bool, dict]:
    t0 = time.monotonic()
    seeds = _r1_fold(vec, seed_k)
    t_seed = time.monotonic() - t0
    seed_ids = [h["page_id"] for h in seeds]
    seed_set = set(seed_ids)

    selected_cats: list[dict] = []
    cand_ids: list[int] = []
    appended: list[dict] = []
    t_cat = 0.0
    t_rank = 0.0

    if seed_ids and cap > 0:
        tc0 = time.monotonic()
        # Pick the single most specific valid category per seed page using covering index KEY(category)
        # scoped strictly to the categories attached to `seed_ids`:
        #   cnt >= 2 skips sterile singleton eponymous categories (458 in fi have cnt=1 -> 0 neighbours);
        #   cnt <= max_cat_size (default 150) skips broad occupational/umbrella rosters (e.g. Kansanedustajat=2435,
        #   Jääkärit=665, Suomalaiset upseerit=374) while keeping event categories like Jatkosota (125).
        cat_rows = _sql(f"""
            WITH seed_cats_raw AS (
                SELECT DISTINCT category
                FROM corpus.article_category
                WHERE lang = 'fi'
                  AND page_id IN ({aot.ints(seed_ids)})
                  AND category <> 'Suomen historia'
                  AND category NOT LIKE 'Wikipedia:%'
                  AND category NOT LIKE '%tynkä%'
                  AND category NOT LIKE '%Tynkä%'
            ),
            cat_sizes AS (
                SELECT ac.category, COUNT(*) AS cnt
                FROM seed_cats_raw sr
                JOIN corpus.article_category ac ON ac.category = sr.category AND ac.lang = 'fi'
                GROUP BY ac.category
            ),
            ranked_seed_cats AS (
                SELECT ac.page_id AS seed_page_id,
                       ac.category,
                       cs.cnt,
                       ROW_NUMBER() OVER (PARTITION BY ac.page_id ORDER BY cs.cnt ASC, ac.category ASC) AS rn
                FROM corpus.article_category ac
                JOIN cat_sizes cs ON cs.category = ac.category
                WHERE ac.lang = 'fi'
                  AND ac.page_id IN ({aot.ints(seed_ids)})
                  AND cs.cnt BETWEEN 2 AND {int(max_cat_size)}
            )
            SELECT DISTINCT sc.category, sc.cnt, ac.page_id AS cand_page_id
            FROM ranked_seed_cats sc
            JOIN corpus.article_category ac ON ac.lang = 'fi' AND ac.category = sc.category
            WHERE sc.rn = 1
        """)
        t_cat = time.monotonic() - tc0

        cats_seen: dict[str, int] = {}
        cands_seen: set[int] = set()
        for r in cat_rows:
            cats_seen[r["category"]] = int(r["cnt"])
            pid = int(r["cand_page_id"])
            if pid not in seed_set:
                cands_seen.add(pid)
        selected_cats = [{"category": c, "size": n} for c, n in sorted(cats_seen.items(), key=lambda x: (x[1], x[0]))]
        cand_ids = sorted(cands_seen)

        if cand_ids:
            tr0 = time.monotonic()
            vlit = aot.vec_literal(vec)
            # Compute distances strictly on candidate fi pages (join article outside LIMIT to keep sort row tiny).
            dist_rows = _sql(f"""
                SELECT top.page_id, a.title, top.distance
                FROM (
                    SELECT page_id, VEC_DISTANCE_COSINE(embedding, {vlit}) AS distance
                    FROM corpus.article_vector
                    WHERE lang = 'fi' AND page_id IN ({aot.ints(cand_ids)})
                    ORDER BY distance ASC
                    LIMIT {int(cap)}
                ) top
                JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = top.page_id
                ORDER BY top.distance ASC
            """)
            t_rank = time.monotonic() - tr0
            appended = [
                {
                    "page_id": int(r["page_id"]),
                    "distance": float(r["distance"]),
                    "title": r["title"],
                    "why": ["category", "vector"],
                    "source": "category_neighbour",
                }
                for r in dist_rows
            ]

    seed_items = [
        {
            "page_id": h["page_id"],
            "distance": h["distance"],
            "title": h["title"],
            "why": _why_from_via(h["via_lang"]),
            "source": f"seed:{h['via_lang']}",
        }
        for h in seeds
    ]
    untrimmed = seed_items + appended[:cap]
    final = untrimmed[:k]
    best = seeds[0]["distance"] if seeds else None
    aot.log(
        f"r4[A]: {len(seeds)} seeds + {len(appended)}/{len(cand_ids)} cat-neighbours "
        f"({len(selected_cats)} cats) -> {len(final)} returned (untrimmed {len(untrimmed)}), "
        f"seed {t_seed:.2f}s, cat {t_cat:.2f}s, rank {t_rank:.2f}s"
    )
    results = [
        {"page_id": x["page_id"], "score": round(1.0 - x["distance"], 5), "why": x["why"]}
        for x in final
    ]
    debug = {
        "mode": "A",
        "best_distance": best,
        "seed_count": len(seeds),
        "selected_categories": selected_cats,
        "candidate_count": len(cand_ids),
        "untrimmed_count": len(untrimmed),
        "returned_count": len(results),
        "seed_s": round(t_seed, 3),
        "cat_s": round(t_cat, 3),
        "rank_s": round(t_rank, 3),
        "untrimmed_page_ids": [x["page_id"] for x in untrimmed],
        "hits": [
            {"page_id": x["page_id"], "distance": round(x["distance"], 5), "source": x["source"], "title": x["title"]}
            for x in final
        ],
    }
    return results, _is_oos(best, oos), debug


# ── Mode B: Category-scoped search (recursive CTE depth <= 3) ────────────────

def retrieve_mode_b(
    vec: list[float],
    k: int,
    cap: int,
    oos: float | None,
) -> tuple[list[dict], bool, dict]:
    t0 = time.monotonic()
    top5 = _r1_fold(vec, 5)
    t_top5 = time.monotonic() - t0
    best = top5[0]["distance"] if top5 else None
    if not top5:
        return [], _is_oos(best, oos), {"mode": "B", "cte_s": 0.0, "candidate_count": 0}

    top5_ids = [h["page_id"] for h in top5]
    rank_by_pid = {h["page_id"]: idx + 1 for idx, h in enumerate(top5)}
    via_by_pid = {h["page_id"]: h["via_lang"] for h in top5}

    tc0 = time.monotonic()
    cat_rows = _sql(f"""
        WITH top5_cats_raw AS (
            SELECT DISTINCT category
            FROM corpus.article_category
            WHERE lang = 'fi'
              AND page_id IN ({aot.ints(top5_ids)})
              AND category <> 'Suomen historia'
              AND category NOT LIKE 'Wikipedia:%'
        ),
        cat_sizes AS (
            SELECT ac.category, COUNT(*) AS direct_cnt
            FROM top5_cats_raw tr
            JOIN corpus.article_category ac ON ac.category = tr.category AND ac.lang = 'fi'
            GROUP BY ac.category
        ),
        child_counts AS (
            SELECT cl.parent AS category, COUNT(*) AS child_cnt
            FROM top5_cats_raw tr
            JOIN corpus.category_link cl ON cl.parent = tr.category
            GROUP BY cl.parent
        )
        SELECT ac.page_id,
               ac.category,
               cs.direct_cnt,
               COALESCE(cc.child_cnt, 0) AS child_cnt
        FROM corpus.article_category ac
        JOIN cat_sizes cs ON cs.category = ac.category
        LEFT JOIN child_counts cc ON cc.category = ac.category
        WHERE ac.lang = 'fi'
          AND ac.page_id IN ({aot.ints(top5_ids)})
    """)
    t_pick = time.monotonic() - tc0

    # Score candidate categories from top-5 hits by reciprocal hit rank sum(1 / hit_rank).
    # Prefer categories that have >= 2 direct members or >= 1 child subcategory and <= 250 direct members
    # (skipping dead-end singletons and 2,435-member occupational rosters unless fewer than 3 categories qualify).
    cat_meta: dict[str, dict] = {}
    for r in cat_rows:
        cat = r["category"]
        pid = int(r["page_id"])
        direct_cnt = int(r["direct_cnt"])
        child_cnt = int(r["child_cnt"])
        entry = cat_meta.setdefault(
            cat,
            {"category": cat, "direct_cnt": direct_cnt, "child_cnt": child_cnt, "weight": 0.0},
        )
        entry["weight"] += 1.0 / rank_by_pid[pid]

    preferred = [
        m for m in cat_meta.values()
        if (m["direct_cnt"] >= 2 or m["child_cnt"] > 0) and m["direct_cnt"] <= 250
    ]
    fallback = [m for m in cat_meta.values() if m not in preferred]
    preferred.sort(key=lambda m: (-m["weight"], m["direct_cnt"], m["category"]))
    fallback.sort(key=lambda m: (-m["weight"], m["direct_cnt"], m["category"]))
    chosen = (preferred + fallback)[:3]
    root_cats = [m["category"] for m in chosen]

    if not root_cats:
        return [], _is_oos(best, oos), {"mode": "B", "cte_s": 0.0, "candidate_count": 0, "selected_categories": []}

    # Walk category_link down with a pure in-memory recursive CTE (depth <= 3), timed server-side
    # via SYSDATE(6) with a strict 5 s ceiling, and rank member vectors in the same session.
    quoted_roots = ", ".join(aot.quote(c) for c in root_cats)
    fetch_n = max(k, cap)
    vlit = aot.vec_literal(vec)
    cte_sql = f"""
        SELECT CAST(category AS CHAR(255)) AS cat, 0 AS depth
        FROM corpus.article_category
        WHERE lang = 'fi' AND category IN ({quoted_roots})
        GROUP BY category
        UNION
        SELECT cl.child, s.depth + 1
        FROM sub s
        JOIN corpus.category_link cl ON cl.parent = s.cat
        WHERE s.depth < 3
    """
    t_cte_wall0 = time.monotonic()
    try:
        ranked_rows = _sql(
            f"""
            SET @t0 = SYSDATE(6);
            SET @cte_stats = (
                WITH RECURSIVE sub AS ({cte_sql})
                SELECT CONCAT(COUNT(DISTINCT u.cat), ':', COUNT(DISTINCT ac.page_id))
                FROM (SELECT DISTINCT cat FROM sub) u
                JOIN corpus.article_category ac ON ac.lang = 'fi' AND ac.category = u.cat
            );
            SET @cte_us = TIMESTAMPDIFF(MICROSECOND, @t0, SYSDATE(6));
            WITH RECURSIVE sub AS ({cte_sql}),
            members AS (
                SELECT DISTINCT ac.page_id
                FROM (SELECT DISTINCT cat FROM sub) u
                JOIN corpus.article_category ac ON ac.lang = 'fi' AND ac.category = u.cat
            )
            SELECT top.page_id, a.title, top.distance, @cte_stats AS cte_stats, @cte_us AS cte_us
            FROM (
                SELECT v.page_id, VEC_DISTANCE_COSINE(v.embedding, {vlit}) AS distance
                FROM members m
                JOIN corpus.article_vector v ON v.lang = 'fi' AND v.page_id = m.page_id
                ORDER BY distance ASC
                LIMIT {int(fetch_n)}
            ) top
            JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = top.page_id
            ORDER BY top.distance ASC
            """,
            timeout=30,
        )
    except Exception as exc:
        cte_elapsed = time.monotonic() - t_cte_wall0
        sys.exit(f"r4[B] ABORT: category_link CTE query failed ({cte_elapsed:.2f}s): {exc}")
    t_session = time.monotonic() - t_cte_wall0

    if ranked_rows:
        t_cte = int(ranked_rows[0]["cte_us"]) / 1_000_000.0
        sc_str, mc_str = (ranked_rows[0]["cte_stats"] or "0:0").split(":")
        subcat_count = int(sc_str)
        member_count = int(mc_str)
    else:
        t_cte = 0.0
        subcat_count = 0
        member_count = 0

    if t_cte > CTE_TIMEOUT_S:
        sys.exit(f"r4[B] ABORT: category_link CTE took {t_cte:.3f}s (> {CTE_TIMEOUT_S:.1f}s budget)")
    t_rank = max(0.0, t_session - t_cte)

    untrimmed = []
    for r in ranked_rows:
        pid = int(r["page_id"])
        dist = float(r["distance"])
        why = ["category", "vector"]
        if pid in via_by_pid and via_by_pid[pid] != "fi":
            why = ["category", "vector", "wikidata"]
        untrimmed.append({"page_id": pid, "distance": dist, "title": r["title"], "why": why})

    final = untrimmed[:k]
    aot.log(
        f"r4[B]: roots={root_cats} -> {subcat_count} subcats, {member_count} member pages, "
        f"top5 {t_top5:.2f}s, pick {t_pick:.2f}s, CTE {t_cte:.4f}s, rank {t_rank:.2f}s"
    )
    results = [
        {"page_id": x["page_id"], "score": round(1.0 - x["distance"], 5), "why": x["why"]}
        for x in final
    ]
    debug = {
        "mode": "B",
        "best_distance": best,
        "selected_categories": [
            {"category": m["category"], "direct_cnt": m["direct_cnt"], "child_cnt": m["child_cnt"],
             "weight": round(m["weight"], 4)}
            for m in chosen
        ],
        "subcat_count": subcat_count,
        "candidate_count": member_count,
        "untrimmed_count": len(untrimmed),
        "returned_count": len(results),
        "top5_s": round(t_top5, 3),
        "pick_s": round(t_pick, 3),
        "cte_s": round(t_cte, 4),
        "rank_s": round(t_rank, 3),
        "hits": [
            {"page_id": x["page_id"], "distance": round(x["distance"], 5), "title": x["title"]}
            for x in final
        ],
    }
    return results, _is_oos(best, oos), debug


# ── Mode C: Cross-language fold variants ─────────────────────────────────────

def retrieve_mode_c(
    vec: list[float],
    k: int,
    fold: str,
    oos: float | None,
) -> tuple[list[dict], bool, dict]:
    t0 = time.monotonic()
    vlit = aot.vec_literal(vec)
    depth = max(k * 3, 60)

    if fold == "all":
        folded = _r1_fold(vec, k)
        dt = time.monotonic() - t0
        best = folded[0]["distance"] if folded else None
        results = [
            {"page_id": h["page_id"], "score": round(1.0 - h["distance"], 5), "why": _why_from_via(h["via_lang"])}
            for h in folded
        ]
        debug = {
            "mode": "C",
            "fold": "all",
            "best_distance": best,
            "candidate_count": len(folded),
            "returned_count": len(results),
            "search_s": round(dt, 3),
            "hits": [
                {"page_id": h["page_id"], "distance": round(h["distance"], 5), "via": h["via_lang"], "title": h["title"]}
                for h in folded
            ],
        }
        return results, _is_oos(best, oos), debug

    if fold == "fi":
        rows = _sql(f"""
            SELECT top.page_id, a.title, top.distance
            FROM (
                SELECT page_id, VEC_DISTANCE_COSINE(embedding, {vlit}) AS distance
                FROM corpus.article_vector
                WHERE lang = 'fi'
                ORDER BY distance ASC
                LIMIT {int(k)}
            ) top
            JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = top.page_id
            ORDER BY top.distance ASC
        """)
        dt = time.monotonic() - t0
        best = float(rows[0]["distance"]) if rows else None
        results = [
            {"page_id": int(r["page_id"]), "score": round(1.0 - float(r["distance"]), 5), "why": ["vector"]}
            for r in rows
        ]
        debug = {
            "mode": "C",
            "fold": "fi",
            "best_distance": best,
            "candidate_count": len(rows),
            "returned_count": len(results),
            "search_s": round(dt, 3),
            "hits": [
                {"page_id": int(r["page_id"]), "distance": round(float(r["distance"]), 5), "via": "fi", "title": r["title"]}
                for r in rows
            ],
        }
        return results, _is_oos(best, oos), debug

    if fold == "en":
        rows = _sql(f"""
            SELECT fi.page_id AS fi_page_id, fi.title AS fi_title, top.distance
            FROM (
                SELECT page_id, VEC_DISTANCE_COSINE(embedding, {vlit}) AS distance
                FROM corpus.article_vector
                WHERE lang = 'en'
                ORDER BY distance ASC
                LIMIT {int(depth)}
            ) top
            JOIN corpus.article en_a ON en_a.lang = 'en' AND en_a.page_id = top.page_id
            JOIN corpus.article fi ON fi.lang = 'fi' AND fi.wikidata_id = en_a.wikidata_id
            WHERE en_a.wikidata_id IS NOT NULL AND en_a.wikidata_id <> ''
            ORDER BY top.distance ASC
        """)
        seen: dict[int, dict] = {}
        for r in rows:
            pid = int(r["fi_page_id"])
            if pid not in seen:
                seen[pid] = {"page_id": pid, "distance": float(r["distance"]), "title": r["fi_title"]}
        folded = list(seen.values())[:k]
        dt = time.monotonic() - t0
        best = folded[0]["distance"] if folded else None
        results = [
            {"page_id": h["page_id"], "score": round(1.0 - h["distance"], 5), "why": ["vector", "wikidata"]}
            for h in folded
        ]
        debug = {
            "mode": "C",
            "fold": "en",
            "best_distance": best,
            "candidate_count": len(seen),
            "returned_count": len(results),
            "search_s": round(dt, 3),
            "hits": [
                {"page_id": h["page_id"], "distance": round(h["distance"], 5), "via": "en", "title": h["title"]}
                for h in folded
            ],
        }
        return results, _is_oos(best, oos), debug

    # Default fold == "rrf": Reciprocal Rank Fusion of exact fi-rank and wikidata-folded en-rank.
    rows = _sql(f"""
        (SELECT 'fi' AS src, top.page_id AS fi_page_id, a.title AS fi_title, top.distance
         FROM (
             SELECT page_id, VEC_DISTANCE_COSINE(embedding, {vlit}) AS distance
             FROM corpus.article_vector
             WHERE lang = 'fi'
             ORDER BY distance ASC
             LIMIT {int(depth)}
         ) top
         JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = top.page_id)
        UNION ALL
        (SELECT 'en' AS src, fi.page_id AS fi_page_id, fi.title AS fi_title, top.distance
         FROM (
             SELECT page_id, VEC_DISTANCE_COSINE(embedding, {vlit}) AS distance
             FROM corpus.article_vector
             WHERE lang = 'en'
             ORDER BY distance ASC
             LIMIT {int(depth)}
         ) top
         JOIN corpus.article en_a ON en_a.lang = 'en' AND en_a.page_id = top.page_id
         JOIN corpus.article fi ON fi.lang = 'fi' AND fi.wikidata_id = en_a.wikidata_id
         WHERE en_a.wikidata_id IS NOT NULL AND en_a.wikidata_id <> '')
    """)
    dt = time.monotonic() - t0

    fi_rows = sorted(
        (r for r in rows if r["src"] == "fi"),
        key=lambda r: float(r["distance"]),
    )
    en_rows = sorted(
        (r for r in rows if r["src"] == "en"),
        key=lambda r: float(r["distance"]),
    )

    fi_rank: dict[int, int] = {}
    fi_dist: dict[int, float] = {}
    titles: dict[int, str] = {}
    for idx, r in enumerate(fi_rows, 1):
        pid = int(r["fi_page_id"])
        if pid not in fi_rank:
            fi_rank[pid] = idx
            fi_dist[pid] = float(r["distance"])
            titles[pid] = r["fi_title"]

    en_rank: dict[int, int] = {}
    en_dist: dict[int, float] = {}
    for idx, r in enumerate(en_rows, 1):
        pid = int(r["fi_page_id"])
        if pid not in en_rank:
            en_rank[pid] = idx
            en_dist[pid] = float(r["distance"])
            titles.setdefault(pid, r["fi_title"])

    all_pids = set(fi_rank) | set(en_rank)
    fused: list[dict] = []
    for pid in all_pids:
        r_fi = fi_rank.get(pid)
        r_en = en_rank.get(pid)
        rrf_score = (1.0 / (RRF_K0 + r_fi) if r_fi is not None else 0.0) + (
            1.0 / (RRF_K0 + r_en) if r_en is not None else 0.0
        )
        min_dist = min(
            d for d in (fi_dist.get(pid), en_dist.get(pid)) if d is not None
        )
        why = ["vector", "wikidata"] if r_en is not None else ["vector"]
        via = "fi+en" if (r_fi is not None and r_en is not None) else ("en" if r_en is not None else "fi")
        fused.append({
            "page_id": pid,
            "rrf_score": rrf_score,
            "min_distance": min_dist,
            "fi_rank": r_fi,
            "en_rank": r_en,
            "via": via,
            "why": why,
            "title": titles[pid],
        })

    fused.sort(key=lambda x: (-x["rrf_score"], x["min_distance"], x["page_id"]))
    final = fused[:k]
    best = min((x["min_distance"] for x in fused), default=None)
    aot.log(
        f"r4[C:rrf]: {len(fi_rank)} fi + {len(en_rank)} en-folded -> {len(all_pids)} union, "
        f"{len(final)} returned in {dt:.2f}s"
    )
    results = [
        {"page_id": x["page_id"], "score": round(x["rrf_score"], 6), "why": x["why"]}
        for x in final
    ]
    debug = {
        "mode": "C",
        "fold": "rrf",
        "best_distance": best,
        "fi_pool": len(fi_rank),
        "en_folded_pool": len(en_rank),
        "candidate_count": len(all_pids),
        "returned_count": len(results),
        "search_s": round(dt, 3),
        "hits": [
            {
                "page_id": x["page_id"],
                "rrf_score": round(x["rrf_score"], 6),
                "min_distance": round(x["min_distance"], 5),
                "fi_rank": x["fi_rank"],
                "en_rank": x["en_rank"],
                "via": x["via"],
                "title": x["title"],
            }
            for x in final
        ],
    }
    return results, _is_oos(best, oos), debug


# ── Mode D: Contested-first re-rank ──────────────────────────────────────────

def retrieve_mode_d(
    vec: list[float],
    k: int,
    w: float,
    oos: float | None,
) -> tuple[list[dict], bool, dict]:
    pool_size = max(30, k)
    t0 = time.monotonic()
    pool = _r1_fold(vec, pool_size)
    t_r1 = time.monotonic() - t0
    best = pool[0]["distance"] if pool else None

    reverts_by_pid: dict[int, int] = {}
    stats_source = "none"
    t_rev = 0.0
    if pool:
        pids = [h["page_id"] for h in pool]
        tr0 = time.monotonic()
        try:
            rows = _sql(
                f"SELECT page_id, signal_total AS reverts FROM research.s1_page_stats WHERE page_id IN ({aot.ints(pids)})"
            )
            stats_source = "research.s1_page_stats.signal_total"
        except RuntimeError as exc:
            if "s1_page_stats" not in str(exc):
                raise
            rows = _sql(
                f"SELECT page_id, COUNT(*) AS reverts FROM research.prof_rev_reverts "
                f"WHERE page_id IN ({aot.ints(pids)}) GROUP BY page_id"
            )
            stats_source = "research.prof_rev_reverts"
        t_rev = time.monotonic() - tr0
        reverts_by_pid = {int(r["page_id"]): int(r["reverts"]) for r in rows}

    reranked = []
    for orig_rank, h in enumerate(pool, 1):
        rev_cnt = reverts_by_pid.get(h["page_id"], 0)
        adj_dist = h["distance"] - w * math.log1p(rev_cnt)
        reranked.append({
            "page_id": h["page_id"],
            "orig_rank": orig_rank,
            "distance": h["distance"],
            "reverts": rev_cnt,
            "adj_distance": adj_dist,
            "why": _why_from_via(h["via_lang"]),
            "via": h["via_lang"],
            "title": h["title"],
        })

    reranked.sort(key=lambda x: (x["adj_distance"], x["distance"], x["page_id"]))
    final = reranked[:k]
    aot.log(
        f"r4[D]: pool={len(pool)}, w={w}, source={stats_source}, "
        f"r1 {t_r1:.2f}s, stats {t_rev:.2f}s"
    )
    results = [
        {"page_id": x["page_id"], "score": round(1.0 - x["adj_distance"], 5), "why": x["why"]}
        for x in final
    ]
    debug = {
        "mode": "D",
        "w": w,
        "stats_source": stats_source,
        "best_distance": best,
        "candidate_count": len(pool),
        "returned_count": len(results),
        "r1_s": round(t_r1, 3),
        "stats_s": round(t_rev, 3),
        "hits": [
            {
                "page_id": x["page_id"],
                "orig_rank": x["orig_rank"],
                "distance": round(x["distance"], 5),
                "reverts": x["reverts"],
                "adj_distance": round(x["adj_distance"], 5),
                "via": x["via"],
                "title": x["title"],
            }
            for x in final
        ],
    }
    return results, _is_oos(best, oos), debug


# ── Entrypoint ───────────────────────────────────────────────────────────────

def retrieve(
    text: str,
    k: int,
    mode: str,
    seed_k: int,
    cap: int,
    max_cat_size: int,
    fold: str,
    w: float,
    oos: float | None,
) -> dict:
    t0 = time.monotonic()
    vec = aot.embed(text)
    t_embed = time.monotonic() - t0

    if mode == "A":
        results, out_of_scope, debug = retrieve_mode_a(vec, k, seed_k, cap, max_cat_size, oos)
    elif mode == "B":
        results, out_of_scope, debug = retrieve_mode_b(vec, k, cap, oos)
    elif mode == "C":
        results, out_of_scope, debug = retrieve_mode_c(vec, k, fold, oos)
    elif mode == "D":
        results, out_of_scope, debug = retrieve_mode_d(vec, k, w, oos)
    else:
        raise ValueError(f"unsupported mode {mode!r}")

    debug["embed_s"] = round(t_embed, 3)
    return {
        "contract": 1,
        "out_of_scope": out_of_scope,
        "results": results,
        "cost": aot.cost(),
        "debug": debug,
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mode", default="A", choices=["A", "B", "C", "D"], help="Expansion/re-rank variant (default: A)")
    ap.add_argument("--seed-k", type=int, default=10, help="Mode A seed top-k from R1 all-langs fold (default: 10)")
    ap.add_argument("--cap", type=int, default=20, help="Max expansion candidates appended/ranked (default: 20)")
    ap.add_argument("--max-cat-size", type=int, default=150, help="Mode A max category member count cutoff (default: 150)")
    ap.add_argument("--fold", default="rrf", choices=["rrf", "all", "fi", "en"], help="Mode C fold strategy (default: rrf)")
    ap.add_argument("--w", type=float, default=0.02, help="Mode D revert weight in distance - w*log1p(reverts) (default: 0.02)")
    ap.add_argument("--oos", type=float, default=None, help="Optional out-of-scope cosine distance threshold")
    args = ap.parse_args()

    req = json.load(sys.stdin)
    if req.get("kind") != "retrieval":
        sys.exit(f"r4 handles only kind 'retrieval', got {req.get('kind')!r}")
    resp = retrieve(
        text=req["text"],
        k=req["k"],
        mode=args.mode,
        seed_k=args.seed_k,
        cap=args.cap,
        max_cat_size=args.max_cat_size,
        fold=args.fold,
        w=args.w,
        oos=args.oos,
    )
    json.dump(resp, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
