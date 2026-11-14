#!/usr/bin/env python3
"""P1.R2 (#23) keyword FULLTEXT and hybrid (RRF) retrieval variants over corpus.article.

    python3 research/23-r2-keyword/variant.py --mode A|B|C|D [--rrf-k 60] [--depth 50] [--keywords FILE] < req.json

Modes:
  A  Natural-language FULLTEXT: MATCH(title, text) AGAINST (? IN NATURAL LANGUAGE MODE), fold to fi.
  B  Title-boosted FULLTEXT: score = 3 * title_match + body_match (word-aware LOCATE/REGEXP per query term on
     accent-sensitive title overlap + normalized natural-language FULLTEXT score), fold to fi.
  C  Hybrid RRF: vector top-depth (aot.nearest + fold) fused with keyword top-depth (mode A) via Σ 1/(rrf_k + rank).
  D  Hybrid RRF with boolean prefix wildcards (+stem1* +stem2* tiered with OR backfill) from --keywords JSONL;
     falls back to C when --keywords is omitted or has no entry for the request text.

Contract: research/eval/CONTRACT.md §2.1. Logs to stderr.
"""
from __future__ import annotations

import argparse
import json
import math
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

# InnoDB built-in default stopwords (INFORMATION_SCHEMA.INNODB_FT_DEFAULT_STOPWORD) plus standard 3-letter
# coordinating conjunctions ('and', 'och', 'vai') that InnoDB omits ('und' typo in upstream InnoDB list).
_STOPWORDS = frozenset({
    "a", "about", "an", "and", "are", "as", "at", "be", "by", "com", "de", "en", "for",
    "from", "how", "i", "in", "is", "it", "la", "och", "of", "on", "or", "that", "the",
    "this", "to", "und", "vai", "was", "what", "when", "where", "who", "will", "with", "www",
})

_FT_OPS_RE = re.compile(r'[+\-<>()~*"@:&|]+')
_WORD_RE = re.compile(r"[0-9A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+")
_STEM_TOKEN_RE = re.compile(r"([0-9A-Za-zÀ-ÖØ-öø-ÿĀ-ž]+)(\*?)")


def _db(fn, *args):
    """Run an aot DB helper, retrying once on transient SSH tunnel TLS/reconnect errors."""
    try:
        return fn(*args)
    except RuntimeError as ex:
        msg = str(ex).lower()
        if not any(t in msg for t in ("tls/ssl error", "unexpected eof", "can't connect", "lost connection")):
            raise
        time.sleep(0.4)
        return fn(*args)


def escape_fulltext(text: str) -> str:
    """Strip MariaDB FULLTEXT boolean operators +-<>()~*"@ (and :&|) from free text."""
    cleaned = _FT_OPS_RE.sub(" ", text)
    return " ".join(cleaned.split())


def content_terms(text: str) -> list[str]:
    """Distinct lowercase content tokens (>= 3 chars, matching innodb_ft_min_token_size=3, excluding stopwords)."""
    seen: list[str] = []
    for w in _WORD_RE.findall(escape_fulltext(text).lower()):
        if len(w) >= 3 and w not in _STOPWORDS and w not in seen:
            seen.append(w)
    return seen


def _title_term_matches(term: str, title_lower: str) -> bool:
    """Check whether one content term matches a lowercase title.

    Terms >= 5 chars match anywhere (capturing Finnish compound halves such as 'liike' in 'lapuanliike'
    or 'luokkasota'). Shorter 3-4 char terms require a non-alphanumeric left boundary so 'syy' matches
    'syyt' at word start without matching '-isyys' suffixes.
    """
    if len(term) >= 5:
        return term in title_lower
    return re.search(r"(?:^|[^0-9a-zà-öø-ÿā-ž])" + re.escape(term), title_lower) is not None


def _fold_scored_rows(scored_rows: list[tuple[float, dict]], limit: int) -> list[dict]:
    """Fold ranked (score, row) candidates to fi via aot.fold_to_fi while preserving score."""
    if not scored_rows:
        return []
    hits = [
        {
            "lang": r["lang"],
            "page_id": int(r["page_id"]),
            "title": r["title"],
            "wikidata_id": r["wikidata_id"],
            "distance": -float(sc),
        }
        for sc, r in scored_rows
    ]
    folded = _db(aot.fold_to_fi, hits)[:limit]
    return [
        {
            "page_id": h["page_id"],
            "score": -h["distance"],
            "via_lang": h["via_lang"],
            "title": h["title"],
        }
        for h in folded
    ]


def keyword_mode_a(text: str, limit: int) -> tuple[list[dict], str]:
    """Mode A: MATCH(title, text) AGAINST (? IN NATURAL LANGUAGE MODE) over all languages, folded to fi."""
    cq = escape_fulltext(text)
    if not cq:
        return [], ""
    fetch = max(limit * 4, 60)
    rows = _db(aot.sql, f"""
        SELECT lang, page_id, title, wikidata_id,
               MATCH(title, text) AGAINST ({aot.quote(cq)} IN NATURAL LANGUAGE MODE) AS sc
        FROM article
        WHERE MATCH(title, text) AGAINST ({aot.quote(cq)} IN NATURAL LANGUAGE MODE)
        ORDER BY sc DESC
        LIMIT {int(fetch)}
    """)
    scored = [(float(r["sc"]), r) for r in rows]
    return _fold_scored_rows(scored, limit), cq


def keyword_mode_b(text: str, limit: int) -> tuple[list[dict], str]:
    """Mode B: score = 3 * title_match + body_match over FULLTEXT candidates, folded to fi."""
    cq = escape_fulltext(text)
    if not cq:
        return [], ""
    terms = content_terms(text)
    if not terms:
        return keyword_mode_a(text, limit)

    fetch = max(limit * 4, 200)
    col = "title COLLATE utf8mb4_uca1400_as_ci"
    th_expr = " + ".join(f"(LOCATE({aot.quote(t)}, {col}) > 0)" for t in terms)
    rows = _db(aot.sql, f"""
        SELECT lang, page_id, title, wikidata_id, MAX(ft) AS ft
        FROM (
            (SELECT lang, page_id, title, wikidata_id,
                    MATCH(title, text) AGAINST ({aot.quote(cq)} IN NATURAL LANGUAGE MODE) AS ft,
                    ({th_expr}) AS th
             FROM article
             WHERE MATCH(title, text) AGAINST ({aot.quote(cq)} IN NATURAL LANGUAGE MODE)
             ORDER BY th DESC, ft DESC
             LIMIT {int(fetch)})
            UNION ALL
            (SELECT lang, page_id, title, wikidata_id,
                    MATCH(title, text) AGAINST ({aot.quote(cq)} IN NATURAL LANGUAGE MODE) AS ft,
                    0 AS th
             FROM article
             WHERE MATCH(title, text) AGAINST ({aot.quote(cq)} IN NATURAL LANGUAGE MODE)
             ORDER BY ft DESC
             LIMIT {int(fetch)})
        ) u
        GROUP BY lang, page_id, title, wikidata_id
    """)
    if not rows:
        return [], cq

    max_ft = max((float(r["ft"]) for r in rows), default=1.0) or 1.0
    nq = len(terms)
    scored: list[tuple[float, dict]] = []
    for r in rows:
        title_lower = r["title"].lower()
        th = sum(1 for t in terms if _title_term_matches(t, title_lower))
        title_words = [w for w in _WORD_RE.findall(title_lower) if len(w) >= 3 and w not in _STOPWORDS]
        nt = max(len(title_words), 1)
        title_match = min(1.0, (th * th) / (nq * nt))
        body_match = float(r["ft"]) / max_ft
        score = 3.0 * title_match + body_match
        scored.append((score, r))
    scored.sort(key=lambda item: (item[0], float(item[1]["ft"])), reverse=True)
    return _fold_scored_rows(scored, limit), cq


def _sanitize_boolean_stems(raw_keywords: list[str]) -> list[str]:
    """Sanitize keyword entries into safe MariaDB BOOLEAN MODE word/prefix tokens (splitting hyphens)."""
    tokens: list[str] = []
    for item in raw_keywords:
        if not isinstance(item, str):
            continue
        for word, star in _STEM_TOKEN_RE.findall(item.lower()):
            # Avoid 1-char stems; allow 2-char only with '*', 3+ char with or without '*'.
            if len(word) < 2 or (len(word) == 2 and not star):
                continue
            tok = f"{word}{star}"
            if tok not in tokens:
                tokens.append(tok)
    return tokens


def keyword_mode_d_arm(stems: list[str], limit: int) -> tuple[list[dict], str]:
    """Boolean-mode prefix wildcard arm: strict '+s1* +s2*' first, backfilled by OR 's1* s2*' in one SQL call."""
    tokens = _sanitize_boolean_stems(stems)
    if not tokens:
        return [], ""
    q_and = " ".join(f"+{t}" for t in tokens)
    q_or = " ".join(tokens)
    fetch = max(limit * 4, 150)
    if len(tokens) == 1:
        rows = _db(aot.sql, f"""
            SELECT lang, page_id, title, wikidata_id,
                   MATCH(title, text) AGAINST ({aot.quote(q_and)} IN BOOLEAN MODE) AS sc
            FROM article
            WHERE MATCH(title, text) AGAINST ({aot.quote(q_and)} IN BOOLEAN MODE)
            ORDER BY sc DESC
            LIMIT {int(fetch)}
        """)
        scored = [(float(r["sc"]), r) for r in rows]
        return _fold_scored_rows(scored, limit), q_and

    rows = _db(aot.sql, f"""
        SELECT lang, page_id, title, wikidata_id, MAX(tier) AS tier, MAX(sc) AS sc
        FROM (
            (SELECT lang, page_id, title, wikidata_id, 1 AS tier,
                    MATCH(title, text) AGAINST ({aot.quote(q_and)} IN BOOLEAN MODE) AS sc
             FROM article
             WHERE MATCH(title, text) AGAINST ({aot.quote(q_and)} IN BOOLEAN MODE)
             ORDER BY sc DESC
             LIMIT {int(fetch)})
            UNION ALL
            (SELECT lang, page_id, title, wikidata_id, 0 AS tier,
                    MATCH(title, text) AGAINST ({aot.quote(q_or)} IN BOOLEAN MODE) AS sc
             FROM article
             WHERE MATCH(title, text) AGAINST ({aot.quote(q_or)} IN BOOLEAN MODE)
             ORDER BY sc DESC
             LIMIT {int(fetch)})
        ) u
        GROUP BY lang, page_id, title, wikidata_id
        ORDER BY tier DESC, sc DESC
        LIMIT {int(fetch)}
    """)
    max_or = max((float(r["sc"]) for r in rows if int(r["tier"]) == 0), default=0.0)
    scored = [
        ((max_or + 1.0 + float(r["sc"])) if int(r["tier"]) == 1 else float(r["sc"]), r)
        for r in rows
    ]
    return _fold_scored_rows(scored, limit), q_and


def vector_arm(text: str, depth: int) -> tuple[list[dict], float, float]:
    """Embed with bge-m3, fetch top-(depth*3) over article_vector, fold to fi top-depth."""
    t0 = time.monotonic()
    vec = aot.embed(text)
    t1 = time.monotonic()
    hits = _db(aot.nearest, vec, depth * 3, None)
    folded = _db(aot.fold_to_fi, hits)[:depth]
    t2 = time.monotonic()
    out = [
        {
            "page_id": h["page_id"],
            "score": 1.0 - h["distance"],
            "distance": h["distance"],
            "via_lang": h["via_lang"],
            "title": h["title"],
        }
        for h in folded
    ]
    return out, t1 - t0, t2 - t1


def rrf_fuse(vec_hits: list[dict], kw_hits: list[dict], rrf_k: int, k: int) -> tuple[list[dict], list[dict]]:
    """Reciprocal Rank Fusion Σ 1/(rrf_k + rank) with 1-based ranks."""
    vec_rank = {h["page_id"]: idx for idx, h in enumerate(vec_hits, 1)}
    kw_rank = {h["page_id"]: idx for idx, h in enumerate(kw_hits, 1)}
    meta: dict[int, dict] = {}
    for h in kw_hits:
        meta[h["page_id"]] = {"title": h["title"], "via": h["via_lang"]}
    for h in vec_hits:
        meta[h["page_id"]] = {"title": h["title"], "via": h["via_lang"]}

    all_pids = list(dict.fromkeys([*(h["page_id"] for h in vec_hits), *(h["page_id"] for h in kw_hits)]))
    fused = []
    for pid in all_pids:
        vr = vec_rank.get(pid)
        kr = kw_rank.get(pid)
        score = 0.0
        why: list[str] = []
        if vr is not None:
            score += 1.0 / (rrf_k + vr)
            why.append("vector")
        if kr is not None:
            score += 1.0 / (rrf_k + kr)
            why.append("keyword")
        fused.append((score, vr is not None, -(vr or 10**9), -(kr or 10**9), pid, vr, kr, why))

    fused.sort(key=lambda x: (x[0], x[1], x[2], x[3]), reverse=True)
    top = fused[:k]
    results = [
        {"page_id": pid, "score": round(sc, 6), "why": why}
        for sc, _, _, _, pid, _, _, why in top
    ]
    debug_hits = [
        {
            "page_id": pid,
            "score": round(sc, 6),
            "vector_rank": vr,
            "keyword_rank": kr,
            "via": meta[pid]["via"],
            "title": meta[pid]["title"],
        }
        for sc, _, _, _, pid, vr, kr, _ in top
    ]
    return results, debug_hits


def load_keywords_map(path: pathlib.Path | None) -> dict[str, list[str]]:
    """Load {"text": "...", "keywords": [...]} JSONL keyed by whitespace-normalised text."""
    if path is None:
        return {}
    mapping: dict[str, list[str]] = {}
    for line_no, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line:
            continue
        obj = json.loads(line)
        if not isinstance(obj, dict) or "text" not in obj or "keywords" not in obj:
            raise ValueError(f"{path}:{line_no}: expected JSON object with 'text' and 'keywords'")
        norm = " ".join(str(obj["text"]).split())
        mapping[norm] = list(obj["keywords"])
    return mapping


def retrieve(
    text: str,
    k: int,
    mode: str,
    rrf_k: int,
    depth: int,
    kw_map: dict[str, list[str]],
) -> dict:
    t_start = time.monotonic()
    norm_text = " ".join(text.split())

    if mode in ("A", "B"):
        t_kw0 = time.monotonic()
        kw_hits, kw_query = (keyword_mode_a(text, k) if mode == "A" else keyword_mode_b(text, k))
        kw_s = time.monotonic() - t_kw0
        total_s = time.monotonic() - t_start
        aot.log(f"r2[{mode}]: {len(kw_hits)} fi pages, keyword {kw_s:.2f}s")
        return {
            "contract": 1,
            "out_of_scope": False,
            "results": [
                {"page_id": h["page_id"], "score": round(h["score"], 5), "why": ["keyword"]}
                for h in kw_hits[:k]
            ],
            "cost": aot.cost(),
            "debug": {
                "mode": mode,
                "effective_mode": mode,
                "fallback_to_c": False,
                "rrf_k": rrf_k,
                "depth": depth,
                "keyword_query": kw_query,
                "timings_s": {
                    "embed_s": 0.0,
                    "vector_s": 0.0,
                    "keyword_s": round(kw_s, 3),
                    "total_s": round(total_s, 3),
                },
                "arm_counts": {"vector_folded": 0, "keyword_folded": len(kw_hits)},
                "hits": [
                    {
                        "page_id": h["page_id"],
                        "score": round(h["score"], 5),
                        "vector_rank": None,
                        "keyword_rank": idx,
                        "via": h["via_lang"],
                        "title": h["title"],
                    }
                    for idx, h in enumerate(kw_hits[:k], 1)
                ],
            },
        }

    # Modes C and D (hybrid RRF)
    arm_depth = max(depth, k)
    vec_hits, embed_s, vec_s = vector_arm(text, arm_depth)

    fallback_to_c = False
    effective_mode = mode
    t_kw0 = time.monotonic()
    if mode == "D":
        stems = kw_map.get(norm_text)
        if stems:
            kw_hits, kw_query = keyword_mode_d_arm(stems, arm_depth)
        else:
            fallback_to_c = True
            effective_mode = "C"
            kw_hits, kw_query = keyword_mode_a(text, arm_depth)
    else:
        kw_hits, kw_query = keyword_mode_a(text, arm_depth)
    kw_s = time.monotonic() - t_kw0

    results, debug_hits = rrf_fuse(vec_hits, kw_hits, rrf_k, k)
    total_s = time.monotonic() - t_start
    aot.log(
        f"r2[{mode}->{effective_mode}]: vec={len(vec_hits)} kw={len(kw_hits)} → {len(results)} fi pages, "
        f"embed {embed_s:.2f}s, vec {vec_s:.2f}s, kw {kw_s:.2f}s"
    )
    return {
        "contract": 1,
        "out_of_scope": False,
        "results": results,
        "cost": aot.cost(),
        "debug": {
            "mode": mode,
            "effective_mode": effective_mode,
            "fallback_to_c": fallback_to_c,
            "rrf_k": rrf_k,
            "depth": arm_depth,
            "keyword_query": kw_query,
            "timings_s": {
                "embed_s": round(embed_s, 3),
                "vector_s": round(vec_s, 3),
                "keyword_s": round(kw_s, 3),
                "total_s": round(total_s, 3),
            },
            "arm_counts": {"vector_folded": len(vec_hits), "keyword_folded": len(kw_hits)},
            "hits": debug_hits,
        },
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="C", choices=["A", "B", "C", "D"])
    ap.add_argument("--rrf-k", type=int, default=60)
    ap.add_argument("--depth", type=int, default=50)
    ap.add_argument("--keywords", type=pathlib.Path, default=None)
    args = ap.parse_args()

    if args.rrf_k < 1:
        sys.exit("--rrf-k must be >= 1")
    if args.depth < 1:
        sys.exit("--depth must be >= 1")

    kw_map = load_keywords_map(args.keywords)
    req = json.load(sys.stdin)
    if req.get("kind") != "retrieval":
        sys.exit(f"r2 handles only kind 'retrieval', got {req.get('kind')!r}")
    resp = retrieve(req["text"], req["k"], args.mode, args.rrf_k, args.depth, kw_map)
    json.dump(resp, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
