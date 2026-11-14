#!/usr/bin/env python3
"""Blind re-label input for one topic (stdlib only).

    python3 research/20-golden/blind_input.py <topic>
    → labels/<topic>.blind-input.json   what the blind labeller reads: the labeller's items with every label, description,
                                        why and category removed, ids replaced by shuffled neutral ids (x01…), each with
                                        the full pack candidates it was built from
    → labels/<topic>.blind-map.json     neutral id → labeller item id (the blind labeller must not read this)
"""
from __future__ import annotations

import json
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
LABELS = HERE / "labels"


def main() -> int:
    topic_id = sys.argv[1]
    lab = json.loads((LABELS / f"{topic_id}.labeller.json").read_text())
    pack = json.loads((HERE / "packs" / f"{topic_id}.json").read_text())
    cands = {c["cid"]: c for c in pack["candidates"]}
    items = list(lab["topic"]["items"])
    random.Random(f"blind:{topic_id}").shuffle(items)
    mapping, out = {}, []
    for n, it in enumerate(items, 1):
        xid = f"x{n:02d}"
        mapping[xid] = it["id"]
        out.append({
            "id": xid,
            "page_id": it["page_id"],
            "other_page_ids": it.get("other_page_ids", []),
            "inserting_rev_ids": it["inserting_rev_ids"],
            "removing_rev_ids": it["removing_rev_ids"],
            "candidates": [cands[c] for c in lab["item_sources"][it["id"]]],
        })
    used = json.dumps(out, ensure_ascii=False)
    editors = {k: v for k, v in pack["editors"].items() if f'"{k}"' in used or f"{k} " in used or f"{k})" in used}
    doc = {"topic_id": topic_id, "topic_title": lab["topic"]["title"], "kind": lab["topic"]["kind"],
           "editors": editors, "items": out}
    (LABELS / f"{topic_id}.blind-input.json").write_text(json.dumps(doc, ensure_ascii=False, indent=1))
    (LABELS / f"{topic_id}.blind-map.json").write_text(json.dumps(mapping, indent=1))
    print(f"{topic_id}: {len(out)} items → labels/{topic_id}.blind-input.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
