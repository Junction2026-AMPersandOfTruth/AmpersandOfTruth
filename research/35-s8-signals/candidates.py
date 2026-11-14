#!/usr/bin/env python3
"""Build the shared S8 candidate set: reverted edits + non-reverted controls on the demo-topic core articles, diffed.

    python3 research/35-s8-signals/candidates.py [--per-group 150] [--seed 35]

Writes $AOT_CACHE_DIR/s8/candidates.jsonl (default ~/.cache/aot-research/s8/; NOT committed — contains wikitext):
one line per candidate edit = {page_id, rev_id, parent_rev_id, group: "reverted"|"control", s1_class, user_display,
summary (masked), ts, bytes_delta, old, new, diff} where diff is the wikidiff (CONTRACT.md §9) result for (old → new).
Both S8 detector workers read this file, so every detector is measured on the same edits.
"""
import argparse
import json
import pathlib
import random
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "research" / "lib"))
import aot  # noqa: E402

# Core articles of the top 4 demo topics (research/profiling/prof_top/findings.md §3).
CORE = [1150, 7675, 9186, 250972, 440, 11398, 105157, 319344, 1256, 24889, 52097, 8295, 6626, 24094, 1333, 19086,
        12374, 9895]
OUT = aot._CACHE.parent / "s8" / "candidates.jsonl"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--per-group", type=int, default=150)
    ap.add_argument("--seed", type=int, default=35)
    a = ap.parse_args()
    rnd = random.Random(a.seed)
    pages = aot.ints(CORE)
    reverted = aot.sql(f"""
        SELECT e.page_id, e.rev_id, e.parent_rev_id, e.user, e.ts, e.comment, e.bytes_delta, r.class AS s1_class
        FROM research.s1_reverted_edit e JOIN research.s1_revert r ON r.reverting_rev_id = e.reverting_rev_id
        WHERE e.page_id IN ({pages}) AND r.class IN ('dispute','edit_war','graffiti_cleanup','other')
          AND e.parent_rev_id IS NOT NULL AND ABS(e.bytes_delta) >= 10""")
    # Controls: non-null, non-reverted, non-revert edits of the same pages with a real change.
    control = aot.sql(f"""
        SELECT page_id, rev_id, parent_rev_id, user, ts, comment, bytes_delta, NULL AS s1_class FROM (
          SELECT v.page_id, v.rev_id, LAG(v.rev_id) OVER w AS parent_rev_id, v.user, v.ts, v.comment,
                 CAST(v.bytes AS SIGNED) - CAST(LAG(v.bytes) OVER w AS SIGNED) AS bytes_delta,
                 v.sha1, LAG(v.sha1) OVER w AS prev_sha1
          FROM corpus.revision v WHERE v.page_id IN ({pages}) WINDOW w AS (PARTITION BY v.page_id ORDER BY v.ts, v.rev_id)
        ) x
        WHERE parent_rev_id IS NOT NULL AND sha1 <> prev_sha1 AND ABS(bytes_delta) >= 10
          AND rev_id NOT IN (SELECT rev_id FROM research.s1_reverted_edit)
          AND rev_id NOT IN (SELECT reverting_rev_id FROM research.s1_revert)""")
    rows = [dict(r, group="reverted") for r in rnd.sample(reverted, min(a.per_group, len(reverted)))]
    rows += [dict(r, group="control") for r in rnd.sample(control, min(a.per_group, len(control)))]
    aot.log(f"s8 candidates: {len(reverted)} reverted / {len(control)} control available, taking {len(rows)}")

    texts = aot.rev_texts([(r["page_id"], r["rev_id"]) for r in rows] +
                          [(r["page_id"], r["parent_rev_id"]) for r in rows])
    rows = [r for r in rows if texts.get(int(r["rev_id"])) is not None and texts.get(int(r["parent_rev_id"])) is not None]
    pairs = "\n".join(json.dumps({"id": r["rev_id"], "old": texts[int(r["parent_rev_id"])], "new": texts[int(r["rev_id"])]},
                                 ensure_ascii=False) for r in rows)
    p = subprocess.run([str(ROOT / "scripts" / "wikidiff.sh"), "batch"], input=pairs, capture_output=True, text=True)
    if p.returncode != 0:
        sys.exit(f"wikidiff failed: {p.stderr[-500:]}")
    diffs = [json.loads(l) for l in p.stdout.splitlines() if l.strip()]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with OUT.open("w", encoding="utf-8") as f:
        for r, d in zip(rows, diffs):
            f.write(json.dumps({
                "page_id": int(r["page_id"]), "rev_id": int(r["rev_id"]), "parent_rev_id": int(r["parent_rev_id"]),
                "group": r["group"], "s1_class": r["s1_class"], "user_display": aot.user_display(r["user"]),
                "summary": aot.mask_comment(r["comment"]), "ts": r["ts"], "bytes_delta": int(r["bytes_delta"]),
                "old": texts[int(r["parent_rev_id"])], "new": texts[int(r["rev_id"])], "diff": d,
            }, ensure_ascii=False) + "\n")
    aot.log(f"wrote {len(rows)} candidates to {OUT}")


if __name__ == "__main__":
    main()
