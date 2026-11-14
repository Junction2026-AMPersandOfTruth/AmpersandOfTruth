# /// script
# dependencies = ["pymysql"]
# ///
"""Corpus check for every rev id in research/eval/golden.json (CONTRACT §1, §4):
the revision exists, sits on the item's page or one of its other_page_ids, and is not a null revision (sha1 == parent's).
Also: inserting revs come before the first removing rev of the same page.

    research/20-golden/golden.sh verify_revs.py [research/eval/golden.json]
"""
import json
import sys
from pathlib import Path

from q import connect

path = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "eval" / "golden.json")
doc = json.loads(path.read_text())
items = [it for t in doc["topics"] for it in t["items"]]
revs = sorted({r for it in items for r in it["inserting_rev_ids"] + it["removing_rev_ids"]})
info = {}
with connect() as c, c.cursor() as cur:
    for i in range(0, len(revs), 500):
        chunk = revs[i:i + 500]
        ph = ",".join(["%s"] * len(chunk))
        cur.execute(f"""SELECT r.rev_id, r.page_id, r.ts, r.sha1, p.sha1 FROM revision r
                        LEFT JOIN revision p ON p.page_id = r.page_id AND p.rev_id = r.parent_id
                        WHERE r.rev_id IN ({ph})""", chunk)
        for rev_id, page_id, ts, sha1, parent_sha1 in cur.fetchall():
            info[rev_id] = (page_id, ts, sha1 is not None and sha1 == parent_sha1)
errors = 0
for it in items:
    pages = {it["page_id"], *it.get("other_page_ids", [])}
    for kind in ("inserting_rev_ids", "removing_rev_ids"):
        for r in it[kind]:
            if r not in info:
                print(f"ERROR {it['id']}: {kind} {r} does not exist in corpus.revision"); errors += 1; continue
            page, _, null = info[r]
            if page not in pages:
                print(f"ERROR {it['id']}: {kind} {r} is on page {page}, not on {sorted(pages)}"); errors += 1
            if null:
                print(f"ERROR {it['id']}: {kind} {r} is a null revision (sha1 equals its parent's)"); errors += 1
    for p in pages:
        ins = [info[r][1] for r in it["inserting_rev_ids"] if r in info and info[r][0] == p]
        rem = [info[r][1] for r in it["removing_rev_ids"] if r in info and info[r][0] == p]
        if ins and rem and min(rem) < min(ins):
            print(f"WARN  {it['id']}: on page {p} a removal predates the first insertion (check the side of the edit war)")
print(f"{len(revs)} rev ids checked, {errors} error(s)")
sys.exit(1 if errors else 0)
