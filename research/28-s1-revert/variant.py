#!/usr/bin/env python3
"""P1.S1 (#28) baseline signals variant (contract §2.2).

Reads precomputed `research.s1_revert` + `research.s1_reverted_edit` (no window functions at request time).
Supports `--class-version v1|v2` (default `v1`) to filter/rank on `class` vs `class_v2`.
Emits one `tier = "reverted"` incident per revert with selected class in (`dispute`, `edit_war`, `graffiti_cleanup`, `other`),
ranked by `log1p(max(net_bytes, 0)) + 1[dispute|edit_war] + 1[reinserted]`, best first, cut at `limit`.

    python3 research/eval/validate.py variant signals -- python3 research/28-s1-revert/variant.py
    python3 research/eval/validate.py variant signals -- python3 research/28-s1-revert/variant.py --class-version v2
"""
from __future__ import annotations

import argparse
import json
import math
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

SIGNAL_CLASSES = ("dispute", "edit_war", "graffiti_cleanup", "other")


def _iso_utc(dt_str: str | None) -> str | None:
    if not dt_str:
        return None
    return dt_str.strip().replace(" ", "T") + "Z"


def _score_revert(net_bytes: int, cls: str, reinserted_rev_id: str | None) -> float:
    s = math.log1p(max(net_bytes, 0))
    if cls in ("dispute", "edit_war"):
        s += 1.0
    if reinserted_rev_id is not None:
        s += 1.0
    return s


def _fetch_freed_v2_edits(missing_revs: list[dict]) -> dict[int, list[dict]]:
    """For the rare v1-`excluded` reverts freed under `class_v2` (0 rows in `s1_reverted_edit`),
    reconstruct their owned non-null reverted edits on those pages using `class_v2 <> 'excluded'`.
    """
    pids_csv = aot.ints({int(r["page_id"]) for r in missing_revs})
    rids_csv = aot.ints(int(r["reverting_rev_id"]) for r in missing_revs)
    rows = aot.sql(f"""
        WITH raw_ord AS (
          SELECT page_id, rev_id, ts, user, comment, sha1,
                 LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
          FROM corpus.revision
          WHERE page_id IN ({pids_csv})
        ),
        seq_tbl AS (
          SELECT page_id, rev_id, ts, user, comment,
                 ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq,
                 LAG(rev_id) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS parent_rev_id
          FROM raw_ord
          WHERE NOT (sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1)
        ),
        v2_revs AS (
          SELECT rv.page_id, rv.reverting_rev_id, sp.seq AS p_seq, sr.seq AS r_seq
          FROM research.s1_revert rv
          JOIN seq_tbl sp ON sp.page_id = rv.page_id AND sp.rev_id = rv.restored_rev_id
          JOIN seq_tbl sr ON sr.page_id = rv.page_id AND sr.rev_id = rv.reverting_rev_id
          WHERE rv.page_id IN ({pids_csv})
            AND rv.class_v2 <> 'excluded'
        ),
        ranked AS (
          SELECT se.page_id, se.rev_id, vr.reverting_rev_id, se.parent_rev_id, se.user, se.ts, se.comment,
                 ROW_NUMBER() OVER (PARTITION BY se.page_id, se.rev_id ORDER BY vr.r_seq ASC) AS rn
          FROM v2_revs vr
          JOIN seq_tbl se ON se.page_id = vr.page_id AND se.seq > vr.p_seq AND se.seq < vr.r_seq
        )
        SELECT page_id, rev_id, reverting_rev_id, parent_rev_id, user, ts, comment
        FROM ranked
        WHERE rn = 1 AND reverting_rev_id IN ({rids_csv})
        ORDER BY reverting_rev_id ASC, ts ASC, rev_id ASC
    """)
    out: dict[int, list[dict]] = {}
    for e in rows:
        out.setdefault(int(e["reverting_rev_id"]), []).append(e)
    return out


def find_incidents(text: str, page_ids: list[int], limit: int, class_version: str = "v1") -> dict:
    t0 = time.monotonic()
    cls_col = "class_v2" if class_version == "v2" else "class"
    clean_pages = sorted({int(pid) for pid in page_ids})
    if not clean_pages or limit <= 0:
        return {"contract": 1, "incidents": [], "cost": aot.cost()}

    pages_csv = ",".join(str(p) for p in clean_pages)
    classes_csv = ",".join(f"'{c}'" for c in SIGNAL_CLASSES)

    rev_rows = aot.sql(f"""
        SELECT page_id,
               reverting_rev_id,
               restored_rev_id,
               last_reverted_rev_id,
               reverting_user,
               revert_ts,
               first_reverted_ts,
               seconds_to_revert,
               net_bytes,
               reinserted_rev_id,
               {cls_col} AS active_class,
               reverting_comment
        FROM research.s1_revert
        WHERE page_id IN ({pages_csv})
          AND {cls_col} IN ({classes_csv})
    """)

    scored: list[tuple[float, int, int, dict]] = []
    for r in rev_rows:
        net_b = int(r["net_bytes"])
        cls = r["active_class"]
        reins = r["reinserted_rev_id"]
        sc = _score_revert(net_b, cls, reins)
        scored.append((sc, int(r["seconds_to_revert"]), int(r["reverting_rev_id"]), r))

    # Best score first; break ties deterministically by longer exposure then newer revert_id.
    scored.sort(key=lambda item: (-item[0], -item[1], -item[2]))
    top = scored[:limit]

    if not top:
        elapsed = time.monotonic() - t0
        aot.log(f"s1({class_version}): {len(clean_pages)} page(s) -> 0 candidate reverts in {elapsed:.3f}s")
        return {
            "contract": 1,
            "incidents": [],
            "cost": aot.cost(),
            "debug": {"class_version": class_version, "candidate_reverts": 0, "elapsed_s": round(elapsed, 3)},
        }

    top_rev_ids_csv = ",".join(str(item[2]) for item in top)
    edit_rows = aot.sql(f"""
        SELECT page_id,
               rev_id,
               reverting_rev_id,
               parent_rev_id,
               user,
               ts,
               comment
        FROM research.s1_reverted_edit
        WHERE reverting_rev_id IN ({top_rev_ids_csv})
        ORDER BY reverting_rev_id ASC, ts ASC, rev_id ASC
    """)

    edits_by_revert: dict[int, list[dict]] = {}
    for e in edit_rows:
        edits_by_revert.setdefault(int(e["reverting_rev_id"]), []).append(e)

    if class_version == "v2":
        missing_revs = [r for _, _, r_rev_id, r in top if r_rev_id not in edits_by_revert]
        if missing_revs:
            edits_by_revert.update(_fetch_freed_v2_edits(missing_revs))

    incidents: list[dict] = []
    for sc, sec_to_rev, r_rev_id, r in top:
        span_edits = edits_by_revert.get(r_rev_id, [])
        if not span_edits:
            continue

        page_id = int(r["page_id"])
        inserting_rev_ids = sorted(int(e["rev_id"]) for e in span_edits)

        edits_payload: list[dict] = []
        for e in span_edits:
            u = e["user"]
            edits_payload.append({
                "rev_id": int(e["rev_id"]),
                "parent_rev_id": int(e["parent_rev_id"]) if e["parent_rev_id"] is not None else None,
                "page_id": page_id,
                "user_display": aot.user_display(u),
                "is_anonymous": aot.is_anonymous(u),
                "timestamp": _iso_utc(e["ts"]),
                "role": "inserted",
                "summary": aot.mask_comment(e["comment"]),
            })

        ru = r["reverting_user"]
        edits_payload.append({
            "rev_id": r_rev_id,
            "parent_rev_id": int(r["last_reverted_rev_id"]),
            "page_id": page_id,
            "user_display": aot.user_display(ru),
            "is_anonymous": aot.is_anonymous(ru),
            "timestamp": _iso_utc(r["revert_ts"]),
            "role": "removed",
            "summary": aot.mask_comment(r["reverting_comment"]),
        })

        incidents.append({
            "page_id": page_id,
            "inserting_rev_ids": inserting_rev_ids,
            "removing_rev_ids": [r_rev_id],
            "tier": "reverted",
            "score": round(sc, 6),
            "first_inserted_at": _iso_utc(r["first_reverted_ts"]),
            "last_removed_at": _iso_utc(r["revert_ts"]),
            "total_exposure_seconds": max(0, sec_to_rev),
            "reinsertion_count": 1 if r["reinserted_rev_id"] is not None else 0,
            "edits": edits_payload,
        })

    elapsed = time.monotonic() - t0
    aot.log(f"s1({class_version}): {len(clean_pages)} page(s), {len(rev_rows)} candidate reverts -> "
            f"{len(incidents)} incident(s) in {elapsed:.3f}s")
    return {
        "contract": 1,
        "incidents": incidents,
        "cost": aot.cost(),
        "debug": {
            "class_version": class_version,
            "candidate_reverts": len(rev_rows),
            "returned_incidents": len(incidents),
            "elapsed_s": round(elapsed, 3),
        },
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--class-version", choices=("v1", "v2"), default="v1")
    args = parser.parse_args()

    req = json.load(sys.stdin)
    if req.get("kind") != "signals":
        sys.exit(f"s1 handles only kind 'signals', got {req.get('kind')!r}")
    resp = find_incidents(req["text"], req["page_ids"], req["limit"], class_version=args.class_version)
    json.dump(resp, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
