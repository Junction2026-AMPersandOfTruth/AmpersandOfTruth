#!/usr/bin/env python3
"""Template variant — copy into research/<issue>-<slug>/variant.py and replace the two TODO blocks.

A variant is any executable: ONE JSON request on stdin, ONE JSON response on stdout, logs on stderr, exit 0.
Check it before the runner exists:

  python3 research/eval/validate.py variant retrieval -- python3 research/<issue>-<slug>/variant.py
  python3 research/eval/validate.py variant signals   -- python3 research/<issue>-<slug>/variant.py

Corpus access (never hard-code passwords): CORPUS_HOST (127.0.0.1), CORPUS_PORT (3307 = scripts/corpus-tunnel.sh),
CORPUS_USER (corpus_ro), CORPUS_PW, CORPUS_DB (corpus). Scratch tables: research.<issue>_*.
"""
import json
import sys


def retrieve(text: str, k: int) -> dict:
    # TODO: embed `text` with bge-m3, query article_vector, fold en/sv hits to the fi sibling via wikidata_id.
    print(f"retrieving for {text!r}", file=sys.stderr)
    results = [{"page_id": 900001, "score": 0.81, "why": ["vector"]}, {"page_id": 900002, "score": 0.64, "why": ["vector"]}]
    return {"contract": 1, "out_of_scope": False, "results": results[:k], "cost": {"llm_calls": 0}}


def find_incidents(text: str, page_ids: list[int], limit: int) -> dict:
    # TODO: mine the revisions of `page_ids`. Best first; only what the app would show; tier "reverted" until assessed.
    print(f"mining {len(page_ids)} article(s)", file=sys.stderr)
    incidents = [{"page_id": page_ids[0], "inserting_rev_ids": [5000501], "removing_rev_ids": [5000502], "tier": "reverted", "score": 1.0}]
    return {"contract": 1, "incidents": incidents[:limit], "cost": {"llm_calls": 0}}


def main() -> None:
    request = json.load(sys.stdin)
    if request["kind"] == "retrieval":
        response = retrieve(request["text"], request["k"])
    elif request["kind"] == "signals":
        response = find_incidents(request["text"], request["page_ids"], request["limit"])
    else:
        sys.exit(f"this variant does not handle kind {request['kind']!r}")
    json.dump(response, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
