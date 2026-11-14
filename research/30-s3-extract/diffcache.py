# /// script
# dependencies = ["pymysql"]
# ///
"""P1.S3 (#30): precompute `wikidiff` results into `research.s3_diff`, so later stages never need dotnet.

Gemini workers run in a macOS sandbox where dotnet cannot start, so the diff engine (#29, CONTRACT.md §9) is run
once here and every S3/S7/S9/E3 script reads its results with plain SQL.

Rows (PK kind + rev_id):
  kind='edit'  rev_id = a revision, old_rev_id = the previous non-null revision of its page (NULL for a page's first)
  kind='span'  rev_id = an S1 reverting revision, old_rev_id = restored_rev_id, new_rev_id = last_reverted_rev_id:
               the net change the reverted span made (what the revert took out)
Which revisions (per page): every research.s1_reverted_edit row, every s1_revert span (class <> 'excluded') and
re-insertion (reinserted_rev_id), and every golden item's inserting and removing revisions. `--revs` adds more.

    research/30-s3-extract/s3.sh diffcache.py                 # all golden pages (golden.json), resumable
    research/30-s3-extract/s3.sh diffcache.py --pages 1256,8295
    research/30-s3-extract/s3.sh diffcache.py --revs 123,456  # extra 'edit' rows (pages looked up)
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import time
import zlib
from pathlib import Path

import pymysql

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "tools/AmpersandOfTruth.WikiDiff/bin/Release/net10.0/AmpersandOfTruth.WikiDiff.dll"

DDL = """
CREATE TABLE IF NOT EXISTS research.s3_diff (
  kind ENUM('edit','span') NOT NULL,
  rev_id INT UNSIGNED NOT NULL,
  page_id INT UNSIGNED NOT NULL,
  old_rev_id INT UNSIGNED NULL,
  new_rev_id INT UNSIGNED NOT NULL,
  added_prose MEDIUMTEXT NULL,
  removed_prose MEDIUMTEXT NULL,
  added_prose_chars INT NULL,
  removed_prose_chars INT NULL,
  added_refs INT NULL,
  removed_refs INT NULL,
  is_substantive TINYINT NULL,
  is_blanking TINYINT NULL,
  segments MEDIUMTEXT NULL COMMENT 'JSON array of CONTRACT.md section 9 segments',
  error VARCHAR(500) NULL,
  PRIMARY KEY (kind, rev_id),
  KEY (page_id)
) CHARSET=utf8mb4 COLLATE=utf8mb4_bin
"""


def log(*a):
    print(*a, file=sys.stderr, flush=True)


def connect():
    return pymysql.connect(
        host=os.environ.get("CORPUS_HOST", "127.0.0.1"), port=int(os.environ.get("CORPUS_PORT", "3307")),
        user=os.environ.get("CORPUS_USER", "corpus_ro"), password=os.environ["CORPUS_PW"],
        database=os.environ.get("CORPUS_DB", "corpus"), charset="utf8mb4", read_timeout=600, autocommit=True)


def connect_retry():
    for attempt in range(10):
        try:
            return connect()
        except pymysql.err.OperationalError as e:
            log(f"  connect failed ({e}); retry {attempt + 1}")
            time.sleep(3 + 3 * attempt)
    raise RuntimeError("database unreachable")


class Db:
    def __init__(self):
        self.c = connect_retry()

    def rows(self, sql, args=()):
        for attempt in range(6):
            try:
                with self.c.cursor() as cur:
                    cur.execute(sql, args)
                    if not cur.description:
                        return []
                    cols = [d[0] for d in cur.description]
                    return [dict(zip(cols, r)) for r in cur.fetchall()]
            except pymysql.err.OperationalError as e:  # flaky tunnel / busy VPS: reconnect and retry
                log(f"  db error ({e}); retry {attempt + 1}")
                time.sleep(2 + 3 * attempt)
                try:
                    self.c.close()
                except Exception:
                    pass
                self.c = connect_retry()
        raise RuntimeError("database unreachable")

    def many(self, sql, rows):
        for attempt in range(6):
            try:
                with self.c.cursor() as cur:
                    cur.executemany(sql, rows)
                return
            except pymysql.err.OperationalError as e:
                log(f"  db error ({e}); retry {attempt + 1}")
                time.sleep(2 + 3 * attempt)
                self.c = connect_retry()
        raise RuntimeError("database unreachable")


def golden_pages_and_revs() -> tuple[list[int], dict[int, set[int]]]:
    g = json.loads((ROOT / "research/eval/golden.json").read_text())
    pages: list[int] = []
    revs: dict[int, set[int]] = {}
    # Order: pages with dev items, other dev pages, then test pages (calibration needs the first group first).
    topics = sorted(g["topics"], key=lambda t: t["split"] != "dev")
    for t in topics:
        for it in t["items"]:
            if t["split"] == "dev" and it["page_id"] not in pages:
                pages.append(it["page_id"])
    for t in topics:
        for a in t["articles"]:
            if a["page_id"] not in pages:
                pages.append(a["page_id"])
        for it in t["items"]:
            for p in [it["page_id"], *it.get("other_page_ids", [])]:
                if p not in pages:
                    pages.append(p)
            for r in it["inserting_rev_ids"] + it["removing_rev_ids"]:
                revs.setdefault(it["page_id"], set()).add(r)
    return pages, revs


def nonnull_order(db: Db, page_id: int) -> dict[int, int | None]:
    """rev_id -> previous non-null rev_id (CONTRACT.md §1: a null revision repeats its predecessor's sha1)."""
    prev_of: dict[int, int | None] = {}
    last_sha, last_rev = None, None
    for r in db.rows("SELECT rev_id, sha1 FROM revision WHERE page_id=%s ORDER BY ts, rev_id", (page_id,)):
        if r["sha1"] is not None and r["sha1"] == last_sha:
            continue  # null revision: changes no text
        prev_of[r["rev_id"]] = last_rev
        last_sha, last_rev = r["sha1"], r["rev_id"]
    return prev_of


def run_wikidiff(reqs: list[dict]) -> list[dict]:
    with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False, encoding="utf-8") as f:
        for q in reqs:
            f.write(json.dumps(q, ensure_ascii=False) + "\n")
        path = f.name
    try:
        with open(path, encoding="utf-8") as fin:
            p = subprocess.run(["dotnet", str(DLL), "batch"], stdin=fin, capture_output=True, text=True, timeout=1800)
    finally:
        os.unlink(path)
    out = [json.loads(line) for line in p.stdout.splitlines() if line.strip()]
    if len(out) != len(reqs):
        raise RuntimeError(f"wikidiff returned {len(out)} of {len(reqs)} results: {p.stderr[-500:]}")
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", help="comma-separated page ids (default: every golden page)")
    ap.add_argument("--revs", help="extra revisions to diff against their previous non-null revision")
    ap.add_argument("--chunk", type=int, default=60, help="pairs per text fetch + wikidiff call")
    ap.add_argument("--no-build", action="store_true", help="skip the wikidiff build (parallel shards)")
    ap.add_argument("--shard", default="0/1", help="i/n: only every n-th page, starting at i (parallel runs)")
    args = ap.parse_args()

    if not args.no_build:
        subprocess.run(["dotnet", "build", str(ROOT / "tools/AmpersandOfTruth.WikiDiff"), "-c", "Release", "-v", "quiet",
                        "--nologo", "-clp:ErrorsOnly"], check=True, stdout=sys.stderr)
    db = Db()
    db.rows(DDL)
    gpages, grevs = golden_pages_and_revs()
    pages = [int(x) for x in args.pages.split(",")] if args.pages else gpages
    extra: dict[int, set[int]] = {p: set(v) for p, v in grevs.items() if p in pages}
    if args.revs:
        ids = [int(x) for x in args.revs.split(",")]
        for r in db.rows(f"SELECT page_id, rev_id FROM revision WHERE rev_id IN ({','.join(map(str, ids))})"):
            extra.setdefault(r["page_id"], set()).add(r["rev_id"])
            if r["page_id"] not in pages:
                pages.append(r["page_id"])

    si, sn = (int(x) for x in args.shard.split("/"))
    pages = pages[si::sn]
    t0, done_pairs, fetched_bytes = time.monotonic(), 0, 0
    for n, page in enumerate(pages, 1):
        have = {(r["kind"], r["rev_id"]) for r in db.rows("SELECT kind, rev_id FROM research.s3_diff WHERE page_id=%s", (page,))}
        prev_of = nonnull_order(db, page)
        pairs: list[tuple[str, int, int | None, int]] = []  # kind, rev_id, old, new
        for r in db.rows("SELECT reverting_rev_id, restored_rev_id, last_reverted_rev_id, reinserted_rev_id "
                         "FROM research.s1_revert WHERE page_id=%s AND class <> 'excluded'", (page,)):
            pairs.append(("span", r["reverting_rev_id"], r["restored_rev_id"], r["last_reverted_rev_id"]))
            if r["reinserted_rev_id"] and r["reinserted_rev_id"] in prev_of:
                pairs.append(("edit", r["reinserted_rev_id"], prev_of[r["reinserted_rev_id"]], r["reinserted_rev_id"]))
        for r in db.rows("SELECT rev_id FROM research.s1_reverted_edit WHERE page_id=%s", (page,)):
            if r["rev_id"] in prev_of:
                pairs.append(("edit", r["rev_id"], prev_of[r["rev_id"]], r["rev_id"]))
        for rev in extra.get(page, ()):
            if rev in prev_of:
                pairs.append(("edit", rev, prev_of[rev], rev))
            else:
                log(f"  page {page}: rev {rev} is a null revision or not on the page; skipped")
        seen: set[tuple[str, int]] = set()
        todo = []
        for p in pairs:
            if (p[0], p[1]) not in have and (p[0], p[1]) not in seen:
                seen.add((p[0], p[1]))
                todo.append(p)
        if not todo:
            continue
        todo.sort(key=lambda p: (p[3], p[0]))  # neighbours share texts
        texts: dict[int, str] = {}  # per page: every text is fetched once
        for i in range(0, len(todo), args.chunk):
            chunk = todo[i:i + args.chunk]
            need = sorted({x for p in chunk for x in (p[2], p[3]) if x is not None} - texts.keys())
            for j in range(0, len(need), 40):
                ids = ",".join(map(str, need[j:j + 40]))
                # COMPRESS() on the server: the tunnel (~0.5 MB/s) is the bottleneck, wikitext zips ~5x.
                for r in db.rows(f"SELECT rev_id, COMPRESS(text) z FROM revision_text WHERE page_id=%s AND rev_id IN ({ids})", (page,)):
                    z = r["z"] or b""
                    fetched_bytes += len(z)
                    texts[r["rev_id"]] = zlib.decompress(z[4:]).decode("utf-8", "replace") if len(z) > 4 else ""
            reqs = [{"id": f"{k}:{rev}", "old": texts.get(old) if old is not None else None, "new": texts.get(new)}
                    for k, rev, old, new in chunk]
            res = run_wikidiff(reqs)
            rows = []
            for (k, rev, old, new), d in zip(chunk, res):
                st = d.get("stats") or {}
                rows.append((k, rev, page, old, new, d.get("added_prose"), d.get("removed_prose"),
                             st.get("added_prose_chars"), st.get("removed_prose_chars"), st.get("added_refs"),
                             st.get("removed_refs"), st.get("is_substantive"), st.get("is_blanking"),
                             json.dumps(d.get("segments"), ensure_ascii=False) if "segments" in d else None,
                             (d.get("error") or None) and str(d["error"])[:500]))
            db.many("INSERT IGNORE INTO research.s3_diff (kind, rev_id, page_id, old_rev_id, new_rev_id, added_prose, "
                    "removed_prose, added_prose_chars, removed_prose_chars, added_refs, removed_refs, is_substantive, "
                    "is_blanking, segments, error) VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)", rows)
            done_pairs += len(chunk)
        el = time.monotonic() - t0
        log(f"[{n}/{len(pages)}] page {page}: +{len(todo)} pairs · total {done_pairs} pairs, "
            f"{fetched_bytes / 1e6:.0f} MB compressed text, {el:.0f} s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
