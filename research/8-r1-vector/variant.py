#!/usr/bin/env python3
"""P1.R1 (#8) baseline retrieval: embed the topic with bge-m3, cosine top-k over article_vector, fold to fi.

    python3 research/8-r1-vector/variant.py [--langs all|fi|en|sv] [--oos 0.55] < request.json

--langs all  (default) search every language, fold en/sv hits onto the fi sibling via wikidata_id.
--langs fi   only fi vectors (the index scan over-fetches 4×, then filters).
--oos D      answer out_of_scope when the best hit's cosine distance is above D (off by default; R1 calibrates it).
Contract: research/eval/CONTRACT.md §2.1. Logs to stderr.
"""
import argparse
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402


def retrieve(text: str, k: int, langs: str, oos: float | None) -> dict:
    t0 = time.monotonic()
    vec = aot.embed(text)
    t1 = time.monotonic()
    # Over-fetch: folding merges siblings and drops hits without a fi article.
    hits = aot.nearest(vec, k * 3, None if langs == "all" else (langs,))
    folded = aot.fold_to_fi(hits)[:k]
    t2 = time.monotonic()
    best = folded[0]["distance"] if folded else None
    out_of_scope = oos is not None and (best is None or best > oos)
    aot.log(f"r1: {len(hits)} hits → {len(folded)} fi pages, best distance {best}, "
            f"embed {t1 - t0:.2f}s, search {t2 - t1:.2f}s")
    return {
        "contract": 1,
        "out_of_scope": out_of_scope,
        "results": [{"page_id": h["page_id"], "score": round(1 - h["distance"], 5), "why": ["vector"]} for h in folded],
        "cost": aot.cost(),
        "debug": {"best_distance": best, "embed_s": round(t1 - t0, 3), "search_s": round(t2 - t1, 3),
                  "hits": [{"page_id": h["page_id"], "distance": round(h["distance"], 5), "via": h["via_lang"],
                            "title": h["title"]} for h in folded]},
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--langs", default="all", choices=["all", "fi", "en", "sv"])
    ap.add_argument("--oos", type=float, default=None)
    args = ap.parse_args()
    req = json.load(sys.stdin)
    if req["kind"] != "retrieval":
        sys.exit(f"r1 handles only kind 'retrieval', got {req['kind']!r}")
    json.dump(retrieve(req["text"], req["k"], args.langs, args.oos), sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
