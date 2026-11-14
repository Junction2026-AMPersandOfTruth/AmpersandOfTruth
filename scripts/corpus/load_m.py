#!/usr/bin/env python3
"""Stream Dataset M (versions-01..16.parquet) into MariaDB `revision` + `revision_text` (issue #14).

Why split `revision` + `revision_text`:
  In InnoDB DYNAMIC row format, ~4 KB compressed MEDIUMTEXT values stay inline on clustered leaf pages,
  growing `revision` from ~130 MB to ~5.6 GB and overflowing the 3 GB buffer pool on metadata/signal scans.
  Keeping `revision` narrow (~380 MB data + indexes for all 862,076 rows) pins all edit metadata permanently
  in RAM while `revision_text` (~5.6 GB `MEDIUMTEXT COMPRESSED`) is read by `(page_id, rev_id)` PK only when
  diffing or viewing wikitext.

Supports subset loading via `--articles N` (top-N most-edited pages) and/or `--pages <file>` (union).
Idempotent and resumable: already-loaded files/rows are skipped via PK checks + `INSERT IGNORE`.
"""
from __future__ import annotations

import argparse
from collections import Counter
import glob
import os
import sys
import time
import pyarrow.parquet as pq

try:
    import mariadb as _db_driver
    _USE_MARIADB = True
    PH = "?"
except ImportError:
    import pymysql as _db_driver
    _USE_MARIADB = False
    PH = "%s"

SCHEMA_SQL = [
    """
    CREATE TABLE IF NOT EXISTS revision (
      page_id   INT UNSIGNED  NOT NULL,
      rev_id    INT UNSIGNED  NOT NULL,
      parent_id INT UNSIGNED  NULL,
      title     VARCHAR(255)  NOT NULL,
      ts        DATETIME      NOT NULL,
      user      VARCHAR(255)  NULL,
      anonymous BOOL          NOT NULL,
      comment   VARCHAR(1000) NULL,
      minor     BOOL          NOT NULL,
      sha1      CHAR(31) CHARACTER SET ascii NULL,
      bytes     INT UNSIGNED  NOT NULL,
      PRIMARY KEY (page_id, rev_id),
      KEY idx_page_ts   (page_id, ts),
      KEY idx_page_sha1 (page_id, sha1),
      KEY idx_sha1      (sha1),
      KEY idx_user      (user),
      KEY idx_ts        (ts)
    ) CHARACTER SET utf8mb4
    """,
    """
    CREATE TABLE IF NOT EXISTS revision_text (
      page_id INT UNSIGNED          NOT NULL,
      rev_id  INT UNSIGNED          NOT NULL,
      text    MEDIUMTEXT COMPRESSED NOT NULL,
      PRIMARY KEY (page_id, rev_id)
    ) CHARACTER SET utf8mb4
    """,
]

INSERT_META_SQL = f"""
INSERT IGNORE INTO revision
  (page_id, rev_id, parent_id, title, ts, user, anonymous, comment, minor, sha1, bytes)
VALUES ({", ".join([PH] * 11)})
"""

INSERT_TEXT_SQL = f"""
INSERT IGNORE INTO revision_text (page_id, rev_id, text)
VALUES ({", ".join([PH] * 3)})
"""


def parse_pages_file(path: str) -> set[int]:
    pages: set[int] = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            raw = line.split("#", 1)[0].strip()
            if not raw:
                continue
            first = raw.replace(",", " ").split()[0].strip()
            if first.lower() == "page_id":
                continue
            pages.add(int(first))
    return pages


def top_n_articles(parquet_files: list[str], n: int) -> set[int]:
    counts: Counter[int] = Counter()
    for path in parquet_files:
        col = pq.read_table(path, columns=["page_id"]).column("page_id").to_pylist()
        counts.update(col)
    return {pid for pid, _ in counts.most_common(n)}


def file_already_loaded(cur, path: str, wanted: set[int] | None) -> tuple[bool, int]:
    pids = pq.read_table(path, columns=["page_id"]).column("page_id").to_pylist()
    if wanted is not None:
        pids = [p for p in pids if p in wanted]
    expected = len(pids)
    if expected == 0:
        return True, 0
    distinct_pages = sorted(set(pids))
    placeholders = ",".join([PH] * len(distinct_pages))
    cur.execute(f"SELECT COUNT(*) FROM revision WHERE page_id IN ({placeholders})", distinct_pages)
    meta_cnt = cur.fetchone()[0]
    if meta_cnt < expected:
        return False, expected
    cur.execute(f"SELECT COUNT(*) FROM revision_text WHERE page_id IN ({placeholders})", distinct_pages)
    text_cnt = cur.fetchone()[0]
    return text_cnt >= expected, expected


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", default=os.environ.get("CORPUS_DATA_DIR", "data/corpus/m"))
    parser.add_argument("--articles", type=int, default=None, help="Load top-N most-edited articles")
    parser.add_argument("--pages", default=None, help="File of page_ids to load (union with --articles if both set)")
    parser.add_argument("--files", nargs="*", default=None, help="Specific parquet filenames to load")
    parser.add_argument("--batch-rows", type=int, default=400, help="Max rows per multi-row INSERT")
    parser.add_argument("--max-batch-mb", type=float, default=8.0, help="Max uncompressed text MB per batch (<16M packet limit)")
    args = parser.parse_args()

    if args.files:
        parquet_files = [os.path.join(args.data_dir, os.path.basename(f)) for f in args.files]
    else:
        parquet_files = sorted(glob.glob(os.path.join(args.data_dir, "versions-*.parquet")))
    if not parquet_files:
        sys.exit(f"No versions-*.parquet files found in {args.data_dir}")

    wanted: set[int] | None = None
    if args.articles is not None or args.pages is not None:
        wanted = set()
        if args.articles is not None:
            top = top_n_articles(parquet_files, args.articles)
            print(f"🔎 Selected top {len(top):,} most-edited articles")
            wanted |= top
        if args.pages is not None:
            listed = parse_pages_file(args.pages)
            print(f"🔎 Selected {len(listed):,} articles from {args.pages}")
            wanted |= listed
        print(f"📌 Subset target: {len(wanted):,} distinct page_ids")

    conn_kwargs = dict(
        host=os.environ.get("DB_HOST", "127.0.0.1"),
        port=int(os.environ.get("DB_PORT", "3307")),
        user=os.environ.get("DB_USER", "corpus_rw"),
        password=os.environ["DB_PASSWORD"],
        database=os.environ.get("DB_NAME", "corpus"),
        autocommit=False,
    )
    if not _USE_MARIADB:
        conn_kwargs["charset"] = "utf8mb4"
    con = _db_driver.connect(**conn_kwargs)
    cur = con.cursor()
    for stmt in SCHEMA_SQL:
        cur.execute(stmt)
    con.commit()

    max_batch_bytes = int(args.max_batch_mb * 1024 * 1024)
    t_start = time.perf_counter()
    total_processed = 0

    for path in parquet_files:
        fname = os.path.basename(path)
        done, expected_rows = file_already_loaded(cur, path, wanted)
        if expected_rows == 0:
            print(f"⏭️  {fname}: 0 matching rows for subset")
            continue
        if done:
            print(f"✅ {fname}: all {expected_rows:,} rows already present, skipping")
            total_processed += expected_rows
            continue

        f_start = time.perf_counter()
        pf = pq.ParquetFile(path)
        buf_meta: list[tuple] = []
        buf_text: list[tuple] = []
        buf_bytes = 0
        file_rows = 0

        for batch in pf.iter_batches(batch_size=500):
            d = batch.to_pydict()
            page_ids = d["page_id"]
            rev_ids = d["rev_id"]
            parent_ids = d["parent_id"]
            titles = d["title"]
            timestamps = d["timestamp"]
            users = d["user"]
            anons = d["anonymous"]
            comments = d["comment"]
            minors = d["minor"]
            sha1s = d["sha1"]
            byte_lens = d["bytes"]
            texts = d["text"]

            for i in range(batch.num_rows):
                pid = page_ids[i]
                if wanted is not None and pid not in wanted:
                    continue
                txt = texts[i]
                txt_bytes = len(txt.encode("utf-8")) if txt else 0
                if buf_meta and (len(buf_meta) >= args.batch_rows or buf_bytes + txt_bytes > max_batch_bytes):
                    cur.executemany(INSERT_META_SQL, buf_meta)
                    cur.executemany(INSERT_TEXT_SQL, buf_text)
                    con.commit()
                    buf_meta.clear()
                    buf_text.clear()
                    buf_bytes = 0

                rid = rev_ids[i]
                ts_naive = timestamps[i].replace(tzinfo=None)
                buf_meta.append((
                    pid, rid, parent_ids[i], titles[i], ts_naive,
                    users[i], anons[i], comments[i], minors[i], sha1s[i], byte_lens[i]
                ))
                buf_text.append((pid, rid, txt))
                buf_bytes += txt_bytes
                file_rows += 1

        if buf_meta:
            cur.executemany(INSERT_META_SQL, buf_meta)
            cur.executemany(INSERT_TEXT_SQL, buf_text)
            con.commit()

        dt = time.perf_counter() - f_start
        total_processed += file_rows
        print(f"📦 {fname}: loaded {file_rows:,} rows in {dt:.1f}s ({file_rows / max(dt, 0.001):,.0f} rows/s)", flush=True)

    cur.execute("ANALYZE TABLE revision, revision_text")
    cur.fetchall()
    con.commit()

    elapsed = time.perf_counter() - t_start
    cur.execute("SELECT COUNT(*), COUNT(DISTINCT page_id) FROM revision")
    rev_rows, rev_pages = cur.fetchone()
    cur.execute("SELECT COUNT(*) FROM revision_text")
    txt_rows = cur.fetchone()[0]
    assert rev_rows == txt_rows, f"Row mismatch: revision={rev_rows} vs revision_text={txt_rows}"

    if wanted is None and args.files is None:
        assert rev_rows == 862076 and rev_pages == 19742, (
            f"Full M count mismatch: got {rev_rows} rows / {rev_pages} pages, expected 862076 / 19742"
        )

    cur.execute(
        f"""
        SELECT table_name,
               ROUND(data_length/1048576, 2),
               ROUND(index_length/1048576, 2),
               ROUND((data_length+index_length)/1048576, 2)
        FROM information_schema.tables
        WHERE table_schema = {PH} AND table_name IN ('revision', 'revision_text')
        ORDER BY table_name
        """,
        (os.environ.get("DB_NAME", "corpus"),),
    )
    sizes = cur.fetchall()

    print()
    print(f"✅ Dataset M ready in {elapsed:.1f}s")
    print(f"   revision / revision_text: {rev_rows:,} rows across {rev_pages:,} articles")
    for tname, data_mb, idx_mb, tot_mb in sizes:
        print(f"   {tname:15s}: {tot_mb:8.2f} MB (data {data_mb} MB, indexes {idx_mb} MB)")
    con.close()


if __name__ == "__main__":
    main()
