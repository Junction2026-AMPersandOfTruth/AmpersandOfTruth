#!/usr/bin/env python3
"""P1.R3 (#24) LLM query understanding retrieval variants over the Finnish Wikipedia corpus.

    python3 research/24-r3-query/variant.py --mode A|B|C|C+|D|E [--scope] [--langs all|fi|en|sv] < request.json

Modes (all share ONE cached `expand-topic` LLM call per distinct topic text):
  A   embed `topic_fi` (Finnish translation) instead of raw text
  B   multi-query: embed raw text + `topic_fi` + `reformulations`; merge via RRF k=60 (or `--merge best_rank`)
  C   HyDE: embed `hyde_fi` (60–120-word hypothetical Finnish Wikipedia lead paragraph)
  C+  RRF (k=60) fusion of C (`hyde_fi`) and raw R1 (`text`)
  D   exact/prefix lookup of `title_guesses` on fi `article.title` (accent-sensitive), unioned in front of B
  E   scope-gated retrieval (`--scope` enabled on `--base`, default D): sets `out_of_scope = not in_scope`

Contract: research/eval/CONTRACT.md §2.1. Logs to stderr.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
sys.path.insert(0, str(HERE))
import aot  # noqa: E402
import expand  # noqa: E402

RRF_K = 60.0
_NORM_SQL_TITLE = "REPLACE(REPLACE(title, '–', '-'), '—', '-') COLLATE utf8mb4_uca1400_as_ci"
_TRANSIENT_SQL_CODES = ("ERROR 2002", "ERROR 2006", "ERROR 2013", "ERROR 2026")


def _retry_corpus(fn, *args, **kwargs):
    """Retry transient SSH-tunnel / TLS drops against the shared corpus MariaDB."""
    for attempt in range(3):
        try:
            return fn(*args, **kwargs)
        except RuntimeError as ex:
            msg = str(ex)
            if attempt < 2 and any(code in msg for code in _TRANSIENT_SQL_CODES):
                delay = 0.8 * (attempt + 1)
                aot.log(f"r3: transient corpus error ({msg[:120]}); retrying in {delay:.1f}s")
                time.sleep(delay)
                continue
            raise


def _norm_dash(s: str) -> str:
    return s.replace("–", "-").replace("—", "-")


def _clean_title_guesses(guesses: list[str] | None) -> list[str]:
    if not guesses:
        return []
    out: list[str] = []
    seen: set[str] = set()
    for raw in guesses:
        if not isinstance(raw, str):
            continue
        cleaned = " ".join(raw.replace("_", " ").strip(" []").split())
        if not cleaned:
            continue
        key = _norm_dash(cleaned).casefold()
        if key not in seen:
            seen.add(key)
            out.append(cleaned)
    return out


def _like_escape(s: str) -> str:
    return s.replace("!", "!!").replace("%", "!%").replace("_", "!_")


def lookup_titles_fi(raw_guesses: list[str] | None, max_prefix_per_miss: int = 2) -> list[dict]:
    """Exact match on fi `article.title` (accent-sensitive, dash-normalised), with prefix fallback on misses."""
    guesses = _clean_title_guesses(raw_guesses)
    if not guesses:
        return []

    norm_guesses = [_norm_dash(g) for g in guesses]
    in_list = ",".join(aot.quote(ng) for ng in norm_guesses)
    exact_rows = _retry_corpus(
        aot.sql,
        f"SELECT page_id, title FROM article WHERE lang = 'fi' AND {_NORM_SQL_TITLE} IN ({in_list})",
    )

    by_key: dict[str, list[dict]] = {}
    for r in exact_rows:
        key = _norm_dash(r["title"]).casefold()
        by_key.setdefault(key, []).append({"page_id": int(r["page_id"]), "title": r["title"], "match": "exact"})

    hits: list[dict] = []
    seen_pids: set[int] = set()
    unresolved: list[str] = []

    for ng in norm_guesses:
        matched = by_key.get(ng.casefold(), [])
        if matched:
            for m in matched:
                if m["page_id"] not in seen_pids:
                    seen_pids.add(m["page_id"])
                    hits.append(m)
        elif len(ng) >= 4:
            unresolved.append(ng)

    if unresolved:
        clauses = [
            f"{_NORM_SQL_TITLE} LIKE {aot.quote(_like_escape(ng) + '%')} ESCAPE '!'"
            for ng in unresolved
        ]
        pref_rows = _retry_corpus(
            aot.sql,
            f"SELECT page_id, title FROM article WHERE lang = 'fi' AND ({' OR '.join(clauses)}) "
            f"ORDER BY CHAR_LENGTH(title) ASC, page_id ASC LIMIT 40",
        )
        for ng in unresolved:
            prefix_key = ng.casefold()
            added = 0
            for r in pref_rows:
                pid = int(r["page_id"])
                if pid in seen_pids:
                    continue
                if _norm_dash(r["title"]).casefold().startswith(prefix_key):
                    seen_pids.add(pid)
                    hits.append({"page_id": pid, "title": r["title"], "match": "prefix"})
                    added += 1
                    if added >= max_prefix_per_miss:
                        break

    return hits


def _vector_arm(query_text: str, k: int, lang_filter: tuple[str, ...] | None) -> list[dict]:
    vec = aot.embed(query_text)
    raw_hits = _retry_corpus(aot.nearest, vec, max(k, 20) * 3, lang_filter)
    return _retry_corpus(aot.fold_to_fi, raw_hits)


def _dedup_queries(queries: list[str]) -> list[str]:
    out: list[str] = []
    seen: set[str] = set()
    for q in queries:
        if not isinstance(q, str):
            continue
        norm = " ".join(q.split())
        if not norm:
            continue
        key = norm.casefold()
        if key not in seen:
            seen.add(key)
            out.append(norm)
    return out


def _multi_query_b(
    queries: list[str],
    k: int,
    lang_filter: tuple[str, ...] | None,
    merge: str,
) -> list[dict]:
    best_rank: dict[int, int] = {}
    rrf: dict[int, float] = {}
    best_dist: dict[int, float] = {}
    titles: dict[int, str] = {}

    for q in queries:
        folded = _vector_arm(q, k, lang_filter)
        for rank, h in enumerate(folded, start=1):
            pid = h["page_id"]
            if rank < best_rank.get(pid, 10**9):
                best_rank[pid] = rank
            rrf[pid] = rrf.get(pid, 0.0) + 1.0 / (RRF_K + rank)
            if h["distance"] < best_dist.get(pid, 2.0):
                best_dist[pid] = h["distance"]
            titles.setdefault(pid, h["title"])

    if merge == "rrf":
        ordered = sorted(rrf, key=lambda p: (-rrf[p], best_rank[p], best_dist[p], p))
        return [
            {
                "page_id": pid,
                "score": round(rrf[pid], 5),
                "why": ["vector", "llm"],
                "title": titles[pid],
                "distance": best_dist[pid],
            }
            for pid in ordered[:k]
        ]

    # --merge best_rank: merge by best rank across queries; break equal-best-rank ties by RRF (k=60), then min cosine distance.
    ordered = sorted(best_rank, key=lambda p: (best_rank[p], -rrf[p], best_dist[p], p))
    return [
        {
            "page_id": pid,
            "score": round(1.0 / best_rank[pid] + rrf[pid] * 0.001, 5),
            "why": ["vector", "llm"],
            "title": titles[pid],
            "distance": best_dist[pid],
        }
        for pid in ordered[:k]
    ]


def _rrf_two(arm1: list[dict], arm2: list[dict], k: int, why: list[str]) -> list[dict]:
    rrf: dict[int, float] = {}
    best_dist: dict[int, float] = {}
    titles: dict[int, str] = {}
    for folded in (arm1, arm2):
        for rank, h in enumerate(folded, start=1):
            pid = h["page_id"]
            rrf[pid] = rrf.get(pid, 0.0) + 1.0 / (RRF_K + rank)
            if h["distance"] < best_dist.get(pid, 2.0):
                best_dist[pid] = h["distance"]
            titles.setdefault(pid, h["title"])
    ordered = sorted(rrf, key=lambda p: (-rrf[p], best_dist[p], p))
    return [
        {
            "page_id": pid,
            "score": round(rrf[pid], 5),
            "why": list(why),
            "title": titles[pid],
            "distance": best_dist[pid],
        }
        for pid in ordered[:k]
    ]


def retrieve(
    text: str,
    k: int,
    mode: str,
    langs: str,
    use_scope: bool,
    merge: str = "rrf",
    base_for_e: str = "D",
    analysis_scope: str | None = None,
) -> dict:
    t0 = time.monotonic()
    lang_filter = None if langs == "all" else (langs,)
    eff_scope_gate = use_scope or mode == "E"
    eff_mode = base_for_e if mode == "E" else mode

    exp, meta = expand.expand_topic(text, scope=analysis_scope)
    t_llm = time.monotonic()

    # Non-Ok fallback: degrade gracefully to R1 baseline vector retrieval.
    if exp is None:
        folded = _vector_arm(text, k, lang_filter)[:k]
        t_end = time.monotonic()
        aot.log(f"r3[{mode}]: fallback to R1 ({len(folded)} fi pages, total {t_end - t0:.2f}s)")
        return {
            "contract": 1,
            "out_of_scope": False,
            "results": [
                {"page_id": h["page_id"], "score": round(1.0 - h["distance"], 5), "why": ["vector"]}
                for h in folded
            ],
            "cost": {
                "llm_calls": meta["llm_calls"],
                "input_tokens": meta["input_tokens"],
                "output_tokens": meta["output_tokens"],
                "cost_usd": round(meta["cost_usd"], 6),
                "sql_queries": aot.STATS["sql_queries"],
            },
            "debug": {
                "mode": mode,
                "fallback_r1": True,
                "llm": meta,
                "total_s": round(t_end - t0, 3),
            },
        }

    out_of_scope = bool(eff_scope_gate and not exp.get("in_scope", True))
    title_hits: list[dict] = []

    if out_of_scope:
        ranked: list[dict] = []
    elif eff_mode == "R1":
        folded = _vector_arm(text, k, lang_filter)[:k]
        ranked = [
            {
                "page_id": h["page_id"],
                "score": round(1.0 - h["distance"], 5),
                "why": ["vector", "llm"],
                "title": h["title"],
                "distance": h["distance"],
            }
            for h in folded
        ]
    elif eff_mode == "A":
        q_fi = (exp.get("topic_fi") or "").strip() or text
        folded = _vector_arm(q_fi, k, lang_filter)[:k]
        ranked = [
            {
                "page_id": h["page_id"],
                "score": round(1.0 - h["distance"], 5),
                "why": ["vector", "llm"],
                "title": h["title"],
                "distance": h["distance"],
            }
            for h in folded
        ]
    elif eff_mode == "B":
        queries = _dedup_queries([text, exp.get("topic_fi") or "", *(exp.get("reformulations") or [])])
        ranked = _multi_query_b(queries, k, lang_filter, merge)
    elif eff_mode == "C":
        hyde = (exp.get("hyde_fi") or "").strip() or text
        folded = _vector_arm(hyde, k, lang_filter)[:k]
        ranked = [
            {
                "page_id": h["page_id"],
                "score": round(1.0 - h["distance"], 5),
                "why": ["vector", "llm"],
                "title": h["title"],
                "distance": h["distance"],
            }
            for h in folded
        ]
    elif eff_mode == "C+":
        hyde = (exp.get("hyde_fi") or "").strip() or text
        c_folded = _vector_arm(hyde, k, lang_filter)
        r1_folded = _vector_arm(text, k, lang_filter)
        ranked = _rrf_two(c_folded, r1_folded, k, ["vector", "llm"])
    elif eff_mode == "D":
        title_hits = lookup_titles_fi(exp.get("title_guesses"))
        queries = _dedup_queries([text, exp.get("topic_fi") or "", *(exp.get("reformulations") or [])])
        b_ranked = _multi_query_b(queries, k, lang_filter, merge)
        b_by_pid = {r["page_id"]: r for r in b_ranked}

        ranked = []
        seen_pids: set[int] = set()
        for idx, th in enumerate(title_hits):
            pid = th["page_id"]
            if pid in seen_pids:
                continue
            seen_pids.add(pid)
            why = ["keyword", "vector", "llm"] if pid in b_by_pid else ["keyword", "llm"]
            ranked.append(
                {
                    "page_id": pid,
                    "score": round(2.0 - idx * 0.01, 5),
                    "why": why,
                    "title": th["title"],
                }
            )
        for br in b_ranked:
            if br["page_id"] not in seen_pids:
                seen_pids.add(br["page_id"])
                ranked.append(br)
        ranked = ranked[:k]
    else:
        raise ValueError(f"unsupported mode: {eff_mode!r}")

    t_end = time.monotonic()
    aot.log(
        f"r3[{mode}{'+scope' if eff_scope_gate and mode != 'E' else ''}]: "
        f"in_scope={exp.get('in_scope')} oos={out_of_scope} hits={len(ranked)} "
        f"cache={meta['from_cache']} llm_ms={meta['latency_ms']} "
        f"sql={aot.STATS['sql_queries']} ({aot.STATS['sql_seconds']:.2f}s) "
        f"embed={aot.STATS['embed_calls']} ({aot.STATS['embed_seconds']:.2f}s) "
        f"wall={t_end - t0:.2f}s"
    )

    return {
        "contract": 1,
        "out_of_scope": out_of_scope,
        "results": [
            {"page_id": r["page_id"], "score": r["score"], "why": r["why"]}
            for r in ranked[:k]
        ],
        "cost": {
            "llm_calls": meta["llm_calls"],
            "input_tokens": meta["input_tokens"],
            "output_tokens": meta["output_tokens"],
            "cost_usd": round(meta["cost_usd"], 6),
            "sql_queries": aot.STATS["sql_queries"],
        },
        "debug": {
            "mode": mode,
            "effective_mode": eff_mode,
            "scope_enabled": eff_scope_gate,
            "merge": merge,
            "in_scope": exp.get("in_scope"),
            "scope_reason": exp.get("scope_reason"),
            "angle": exp.get("angle"),
            "topic_fi": exp.get("topic_fi"),
            "reformulations": exp.get("reformulations"),
            "title_guesses": exp.get("title_guesses"),
            "title_hits": title_hits,
            "keywords_fi": exp.get("keywords_fi"),
            "llm": meta,
            "llm_wall_s": round(t_llm - t0, 3),
            "retrieval_s": round(t_end - t_llm, 3),
            "hits": [{"page_id": r["page_id"], "score": r["score"], "title": r.get("title")} for r in ranked[:k]],
        },
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mode", default="D", choices=["A", "B", "C", "C+", "D", "E"])
    ap.add_argument("--scope", action="store_true", help="Set out_of_scope = not in_scope (always on for --mode E)")
    ap.add_argument("--base", default="D", choices=["R1", "A", "B", "C", "C+", "D"], help="Base retrieval arm for --mode E")
    ap.add_argument("--merge", default="rrf", choices=["rrf", "best_rank"], help="Multi-query merge rule for B/D (default: rrf k=60)")
    ap.add_argument("--langs", default="all", choices=["all", "fi", "en", "sv"])
    ap.add_argument("--analysis-scope", default=None, help="Ledger scope passed to scripts/analyze.sh")
    args = ap.parse_args()

    req = json.load(sys.stdin)
    if req.get("kind") != "retrieval":
        sys.exit(f"r3 handles only kind 'retrieval', got {req.get('kind')!r}")

    resp = retrieve(
        text=req["text"],
        k=req["k"],
        mode=args.mode,
        langs=args.langs,
        use_scope=args.scope,
        merge=args.merge,
        base_for_e=args.base,
        analysis_scope=args.analysis_scope,
    )
    json.dump(resp, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
