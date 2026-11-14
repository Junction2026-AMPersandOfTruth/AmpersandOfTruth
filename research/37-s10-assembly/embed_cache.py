#!/usr/bin/env python3
"""P1.S10 (#37): Precompute bge-m3 embeddings into `results/embeddings.cache.json` for `--mode B+rel`.

Embeds (one at a time via `aot.embed`, dev split ONLY — never reads test-split items):
  1. Each dev topic's English query text (`golden.json` query `style == "en"`, plus `contract.topic_text`).
  2. Each dev candidate's `claim_en` from `results/candidates.assess.carbon.jsonl`
     (fallback: first 500 chars of inserted prose).

Usage:
    python3 research/37-s10-assembly/embed_cache.py
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import socket
import sys
import time
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
RESEARCH_DIR = HERE.parent
ROOT = RESEARCH_DIR.parent

sys.path.insert(0, str(RESEARCH_DIR / "lib"))
sys.path.insert(0, str(RESEARCH_DIR / "eval"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
import contract  # noqa: E402
from evidence import load_assessments, load_candidates_for_pages  # noqa: E402
from tiers import assess as assess_tier  # noqa: E402
from variant import (  # noqa: E402
    DEFAULT_EMBEDDINGS_CACHE,
    _claim_or_prose_for_relevance,
    embed_cache_key,
    encode_vector_f16_b64,
)

DEFAULT_GOLDEN = RESEARCH_DIR / "eval" / "golden.json"
DEFAULT_ASSESSMENTS = HERE / "results" / "candidates.assess.carbon.jsonl"


def _configure_ollama_endpoint() -> None:
    """Avoid per-call ConnectionRefused overhead on :11434 when only the tunnel :11435 is listening."""
    if "OLLAMA_URL" in os.environ:
        return
    try:
        with socket.create_connection(("127.0.0.1", 11434), timeout=0.5):
            return
    except OSError:
        os.environ["OLLAMA_URL"] = "http://127.0.0.1:11435"


def collect_dev_texts(
    golden_path: pathlib.Path,
    assessments_path: pathlib.Path,
) -> tuple[list[str], dict[str, int]]:
    """Collect unique normalised query and candidate texts for `split == 'dev'` topics only."""
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    query_texts: list[str] = []
    dev_pages: set[int] = set()

    for topic in doc["topics"]:
        if topic.get("split") != "dev":
            continue
        for q in topic.get("queries") or ():
            if q.get("style") == "en" and q.get("text"):
                query_texts.append(" ".join(str(q["text"]).split()))
        if topic.get("queries"):
            query_texts.append(" ".join(contract.topic_text(topic).split()))
        for art in topic.get("articles") or ():
            dev_pages.add(int(art["page_id"]))

    assess_map = load_assessments(assessments_path) if assessments_path.exists() else {}
    cand_texts: list[str] = []

    # Include every `claim_en` from the assessments file directly
    for row in assess_map.values():
        val = row.get("value") if isinstance(row, dict) else None
        if isinstance(val, dict):
            ce = val.get("claim_en")
            if isinstance(ce, str) and ce.strip():
                cand_texts.append(" ".join(ce.strip()[:500].split()))

    # Include every non-excluded dev candidate (`claim_en` or fallback first 500 chars of inserted prose)
    for sides_flag in (True, False):
        cands, _ = load_candidates_for_pages(
            sorted(dev_pages),
            assessments_by_key=assess_map,
            sides=sides_flag,
            edit_war_source="passage",
        )
        for c in cands:
            if not assess_tier(c.evidence).is_incident:
                continue
            rel_txt = _claim_or_prose_for_relevance(c, c.assessment_meta)
            if rel_txt and rel_txt.strip():
                cand_texts.append(" ".join(rel_txt.split()))

    seen: set[str] = set()
    ordered_unique: list[str] = []
    for t in [*query_texts, *cand_texts]:
        if t and t not in seen:
            seen.add(t)
            ordered_unique.append(t)

    stats = {
        "dev_queries": len({t for t in query_texts if t}),
        "dev_pages": len(dev_pages),
        "unique_texts": len(ordered_unique),
    }
    return ordered_unique, stats


def write_cache_file(out_path: pathlib.Path, vectors: dict[str, str], dim: int = 1024) -> None:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    payload: dict[str, Any] = {
        "model": "bge-m3",
        "dim": dim,
        "dtype": "float16_le_b64",
        "count": len(vectors),
        "vectors": dict(sorted(vectors.items())),
    }
    tmp = out_path.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    tmp.replace(out_path)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--golden", type=pathlib.Path, default=DEFAULT_GOLDEN)
    ap.add_argument("--assessments", type=pathlib.Path, default=DEFAULT_ASSESSMENTS)
    ap.add_argument("--out", type=pathlib.Path, default=DEFAULT_EMBEDDINGS_CACHE)
    args = ap.parse_args(argv)

    _configure_ollama_endpoint()
    t0 = time.monotonic()

    existing: dict[str, str] = {}
    if args.out.exists():
        doc = json.loads(args.out.read_text(encoding="utf-8"))
        if isinstance(doc, dict) and isinstance(doc.get("vectors"), dict) and doc.get("dtype") == "float16_le_b64":
            existing.update({str(k): str(v) for k, v in doc["vectors"].items()})

    texts, stats = collect_dev_texts(args.golden, args.assessments)
    aot.log(
        f"embed_cache: {stats['unique_texts']} unique text(s) "
        f"({stats['dev_queries']} dev queries, {stats['dev_pages']} dev pages)"
    )

    newly_embedded = 0
    dim = 1024
    for idx, text in enumerate(texts, start=1):
        hkey = embed_cache_key(text)
        if hkey in existing:
            continue
        vec = aot.embed(text)
        dim = len(vec)
        existing[hkey] = encode_vector_f16_b64(vec)
        newly_embedded += 1
        if newly_embedded % 250 == 0:
            write_cache_file(args.out, existing, dim=dim)
            aot.log(f"  checkpoint {idx}/{len(texts)} ({len(existing)} cached, +{newly_embedded} new)")

    write_cache_file(args.out, existing, dim=dim)
    elapsed = round(time.monotonic() - t0, 2)
    size_mb = round(args.out.stat().st_size / (1024 * 1024), 2)
    print(
        f"wrote {len(existing)} bge-m3 embeddings (+{newly_embedded} computed, "
        f"{aot.STATS['embed_calls']} Ollama HTTP calls) -> {args.out} ({size_mb} MB in {elapsed}s)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
