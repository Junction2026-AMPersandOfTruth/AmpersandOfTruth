#!/usr/bin/env python3
"""P1.R5 (#26, #94) Step 3: load passages.jsonl + vectors.jsonl into research.r5_claim_vector.

    python3 research/26-r5-claims/load.py [--passages FILE] [--vectors FILE] [--batch 200]
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "research" / "lib"))
import aot  # noqa: E402

MAX_BATCH = 200


def _cache_root() -> pathlib.Path:
    return pathlib.Path(os.environ.get("AOT_CACHE_DIR", pathlib.Path.home() / ".cache" / "aot-research"))


def _retry_sql(query: str, timeout: int = 110) -> list[dict]:
    for attempt in range(4):
        try:
            return aot.sql(query, timeout=timeout)
        except RuntimeError as ex:
            if attempt == 3:
                raise
            wait = 0.5 * (attempt + 1)
            aot.log(f"[r5 load] transient SQL error ({ex}); retrying in {wait:.1f}s...")
            time.sleep(wait)
    return []


def main() -> None:
    default_dir = _cache_root() / "r5"
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--passages", type=pathlib.Path, default=default_dir / "passages.jsonl", help="passages.jsonl path")
    ap.add_argument("--vectors", "--in", dest="vectors", type=pathlib.Path, default=default_dir / "vectors.jsonl",
                    help="vectors.jsonl path")
    ap.add_argument("--batch", type=int, default=100, help="INSERT batch size (<= 200, default 100)")
    args = ap.parse_args()

    if args.batch < 1 or args.batch > MAX_BATCH:
        ap.error(f"--batch must be between 1 and {MAX_BATCH}")
    if not args.passages.exists():
        sys.exit(f"passages file not found: {args.passages}")
    if not args.vectors.exists():
        sys.exit(f"vectors file not found: {args.vectors}")

    passages_by_id: dict[str, dict] = {}
    with args.passages.open("r", encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s:
                continue
            rec = json.loads(s)
            passages_by_id[str(rec["passage_id"])] = rec

    vectors_by_id: dict[str, list[float]] = {}
    with args.vectors.open("r", encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s:
                continue
            rec = json.loads(s)
            pid = str(rec["passage_id"])
            if pid in passages_by_id:
                vectors_by_id[pid] = rec["embedding"]

    rows_to_load: list[tuple[dict, list[float]]] = [
        (p, vectors_by_id[pid]) for pid, p in passages_by_id.items() if pid in vectors_by_id
    ]
    if not rows_to_load:
        sys.exit(f"0 matching passage_ids between {args.passages} ({len(passages_by_id)}) and {args.vectors}")

    aot.log(
        f"[r5 load] loading {len(rows_to_load)}/{len(passages_by_id)} passage vector(s) "
        f"into research.r5_claim_vector (batch={args.batch})"
    )

    t0 = time.monotonic()
    _retry_sql("DROP TABLE IF EXISTS research.r5_claim_vector__tmp")
    _retry_sql(
        """
        CREATE TABLE research.r5_claim_vector__tmp (
          passage_id VARCHAR(32) NOT NULL PRIMARY KEY,
          page_id INT NOT NULL,
          removing_rev_id INT NOT NULL,
          inserting_rev_ids VARCHAR(500) NOT NULL,
          text TEXT NOT NULL,
          embedding VECTOR(1024) NOT NULL,
          KEY idx_page_id (page_id),
          VECTOR INDEX (embedding) M=16 DISTANCE=cosine
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
        """
    )

    total_batches = (len(rows_to_load) + args.batch - 1) // args.batch
    for b_idx, i in enumerate(range(0, len(rows_to_load), args.batch), 1):
        chunk = rows_to_load[i:i + args.batch]
        values_sql = []
        for p, vec in chunk:
            ins_str = json.dumps([int(x) for x in p["inserting_rev_ids"]], separators=(",", ":"))
            if len(ins_str) > 500:
                ins_str = ins_str[:500]
            values_sql.append(
                "("
                + ", ".join(
                    [
                        aot.quote(str(p["passage_id"])[:32]),
                        str(int(p["page_id"])),
                        str(int(p["removing_rev_id"])),
                        aot.quote(ins_str),
                        aot.quote(str(p["text"])),
                        aot.vec_literal(vec),
                    ]
                )
                + ")"
            )
        stmt = (
            "INSERT INTO research.r5_claim_vector__tmp "
            "(passage_id, page_id, removing_rev_id, inserting_rev_ids, text, embedding) VALUES "
            + ", ".join(values_sql)
        )
        _retry_sql(stmt)
        if b_idx % 10 == 0 or b_idx == total_batches:
            aot.log(f"[r5 load] inserted {min(i + args.batch, len(rows_to_load))}/{len(rows_to_load)} rows "
                    f"({b_idx}/{total_batches} batches)")
        if i + args.batch < len(rows_to_load):
            time.sleep(0.05)

    _retry_sql(
        """
        DROP TABLE IF EXISTS research.r5_claim_vector;
        RENAME TABLE research.r5_claim_vector__tmp TO research.r5_claim_vector;
        """
    )
    count_row = _retry_sql(
        "SELECT COUNT(*) AS n, COUNT(DISTINCT page_id) AS pages FROM research.r5_claim_vector"
    )[0]
    elapsed = time.monotonic() - t0
    print(
        f"Loaded {count_row['n']} passage vectors across {count_row['pages']} pages "
        f"into research.r5_claim_vector in {elapsed:.1f}s"
    )


if __name__ == "__main__":
    main()
