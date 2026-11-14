#!/usr/bin/env python3
"""Self-check for one labeller or blind file (stdlib only; no corpus).

    python3 research/20-golden/check_topic.py labels/<topic>.labeller.json     # labeller output
    python3 research/20-golden/check_topic.py labels/<topic>.blind.json        # blind re-label output

Labeller file = {"topic": <golden topic object>, "item_sources": {"<item id>": ["c01", ...]}, "skipped": [...]}.
Checks: golden schema + contract rules (wrapped as a one-topic golden doc), every rev id and cid exists in the pack,
inserting revs are inserting edits (not removals) of the cited candidates, articles come from the pack.
Blind file = {"topic_id": ..., "labels": {"<item id>": {"label", "exclusion", "category", "why"}}}.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "eval"))
import contract  # noqa: E402

LABELS = {"campaign", "contested", "reverted", "not-incident"}
EXCLUSIONS = {"not-substantive", "self-removal", "bot", "blanking", "kept", "graffiti"}


def pack_for(topic_id: str) -> dict:
    return json.loads((HERE / "packs" / f"{topic_id}.json").read_text())


def check_labeller(doc: dict) -> list[str]:
    errs: list[str] = []
    t = doc.get("topic")
    if not isinstance(t, dict):
        return ["missing 'topic' object"]
    golden = {"contract": 1, "version": "check", "dataset": "m-2026-10", "topics": [t]}
    rep = contract.check_golden(golden)
    errs += rep.errors
    if t.get("kind") == "control-out-of-corpus":
        return errs
    pack = pack_for(t["id"])
    cands = {c["cid"]: c for c in pack["candidates"]}
    pages = {a["page_id"] for a in pack["article_candidates"]}
    for a in t.get("articles", []):
        if a["page_id"] not in pages:
            errs.append(f"article {a['page_id']} ({a.get('title')}) is not in the pack's article_candidates")
    sources = doc.get("item_sources", {})
    for it in t.get("items", []):
        cids = sources.get(it["id"])
        if not cids:
            errs.append(f"{it['id']}: no item_sources entry (which candidates is it built from?)"); continue
        missing = [c for c in cids if c not in cands]
        if missing:
            errs.append(f"{it['id']}: unknown candidates {missing}"); continue
        ins_ok = {e["rev_id"] for c in cids for e in cands[c]["edits"]}
        rem_ok = {cands[c]["removing_rev_id"] for c in cids}
        for r in it["inserting_rev_ids"]:
            if r not in ins_ok:
                errs.append(f"{it['id']}: inserting rev {r} is not an inserting edit of {cids}")
        for r in it["removing_rev_ids"]:
            if r not in rem_ok and r not in ins_ok:
                errs.append(f"{it['id']}: removing rev {r} is not a removal in {cids}")
        if it["removing_rev_ids"] != sorted(it["removing_rev_ids"]):
            errs.append(f"{it['id']}: removing_rev_ids must be oldest first (ascending)")
    return errs


def check_blind(doc: dict) -> list[str]:
    errs = []
    for k, v in doc.get("labels", {}).items():
        if v.get("label") not in LABELS:
            errs.append(f"{k}: label {v.get('label')!r}")
        if v.get("label") == "not-incident" and v.get("exclusion") not in EXCLUSIONS:
            errs.append(f"{k}: not-incident needs an exclusion")
        if v.get("label") != "not-incident" and v.get("exclusion"):
            errs.append(f"{k}: exclusion only for not-incident")
        if not v.get("why"):
            errs.append(f"{k}: why is required")
    return errs


def main() -> int:
    path = Path(sys.argv[1])
    doc = json.loads(path.read_text())
    errs = check_blind(doc) if "labels" in doc else check_labeller(doc)
    for e in errs:
        print("ERROR", e)
    if not errs:
        if "topic" in doc:
            items = doc["topic"]["items"]
            by = {}
            for it in items:
                by[it["label"]] = by.get(it["label"], 0) + 1
            print(f"OK {path.name}: {len(doc['topic']['articles'])} articles, {len(items)} items {by}")
        else:
            print(f"OK {path.name}: {len(doc['labels'])} labels")
    return 1 if errs else 0


if __name__ == "__main__":
    sys.exit(main())
