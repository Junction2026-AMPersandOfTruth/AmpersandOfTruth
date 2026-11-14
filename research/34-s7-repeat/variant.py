#!/usr/bin/env python3
"""P1.S7 (#34) signals variant (CONTRACT.md §2.2): one incident per claim cluster.

Reads precomputed `research.s7_cluster` + `research.s7_member` (method='fuzzy' by default).
Emits one `tier = "reverted"` incident per cluster that touches any requested `page_ids`
(singletons included), ranked by:

    score = 2.0 * log2(n_units)
          + 1.5 * log2(n_accounts)
          + 1.5 * log2(n_pages)
          + 0.5 * log1p(span_days)
          + 0.6 * log1p(min(prose_chars, 4000))

Validation:
    python3 research/eval/validate.py variant signals -- python3 research/34-s7-repeat/variant.py
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import json
import math
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402


def iso_utc(dt_str: str | None) -> str | None:
    if not dt_str:
        return None
    return dt_str.strip()[:19].replace(" ", "T") + "Z"


def score_cluster(
    n_units: int,
    n_accounts: int,
    n_pages: int,
    span_days: float,
    prose_chars: int,
) -> float:
    """Monotonic ranking formula combining repetition, multi-editor, cross-page, persistence, and prose length."""
    return (
        2.0 * math.log2(max(1, n_units))
        + 1.5 * math.log2(max(1, n_accounts))
        + 1.5 * math.log2(max(1, n_pages))
        + 0.5 * math.log1p(max(0.0, span_days))
        + 0.6 * math.log1p(min(max(0, prose_chars), 4000))
    )


def parse_rev_list(raw: str) -> list[int]:
    s = (raw or "").strip()
    if not s:
        return []
    if s.startswith("["):
        return [int(x) for x in json.loads(s)]
    return [int(x) for x in s.split(",") if x.strip()]


def build_incident_from_cluster(meta: dict, members: list[dict]) -> tuple[float, int, int, dict] | None:
    """Assemble one CONTRACT.md §3 incident dict from a cluster row and its ordered member rows."""
    if not members:
        return None

    ordered = sorted(members, key=lambda m: (m["inserted_at"], int(m["reverting_rev_id"])))
    first_page_id = int(ordered[0]["page_id"])
    other_page_ids = list(
        dict.fromkeys(int(m["page_id"]) for m in ordered if int(m["page_id"]) != first_page_id)
    )

    ins_set: set[int] = set()
    for m in ordered:
        ins_set.update(parse_rev_list(m["inserting_rev_ids"]))
    if not ins_set:
        return None
    inserting_rev_ids = sorted(ins_set)

    # Contract §3 invariant: inserting_rev_ids and removing_rev_ids must be disjoint.
    # Because every unit satisfies reverting_rev_id > max(unit.inserting_rev_ids),
    # max(all_reverting_rev_ids) > max(inserting_rev_ids), so removing - inserting is always non-empty.
    raw_removing = sorted({int(m["reverting_rev_id"]) for m in ordered})
    removing_rev_ids = [r for r in raw_removing if r not in ins_set]
    if not removing_rev_ids:
        removing_rev_ids = [raw_removing[-1]]

    n_units = int(meta["n_units"])
    n_pages = int(meta["n_pages"])
    n_accounts = int(meta["n_accounts"])
    n_reinsertions = int(meta["n_reinsertions"])
    span_days = float(meta["span_days"])
    prose_chars = int(meta["prose_chars"])

    sc = score_cluster(n_units, n_accounts, n_pages, span_days, prose_chars)

    signals_payload: list[dict] = []
    sig_tokens = {s for s in (meta.get("campaign_signals") or "").split(",") if s}
    if n_reinsertions >= 1:
        signals_payload.append({
            "kind": "reinserted",
            "label": f"put back {n_reinsertions} time(s) after removal",
            "evidence": f"n_units={n_units}",
        })
    if "accounts" in sig_tokens:
        signals_payload.append({
            "kind": "multiple_editors",
            "label": f"added by {n_accounts} distinct editors",
            "evidence": f"n_accounts={n_accounts}",
        })
    if "articles" in sig_tokens:
        signals_payload.append({
            "kind": "multiple_articles",
            "label": f"added across {n_pages} articles",
            "evidence": f"n_pages={n_pages}",
        })
    if "persistence" in sig_tokens:
        signals_payload.append({
            "kind": "persistence",
            "label": f"repeated over {span_days:.1f} days",
            "evidence": f"span_days={span_days:.2f}",
        })

    last_removed = max(m["removed_at"] for m in ordered)
    incident: dict = {
        "page_id": first_page_id,
        "inserting_rev_ids": inserting_rev_ids,
        "removing_rev_ids": removing_rev_ids,
        "tier": "reverted",
        "score": round(sc, 6),
        "first_inserted_at": iso_utc(ordered[0]["inserted_at"]),
        "last_removed_at": iso_utc(last_removed),
        "reinsertion_count": n_reinsertions,
    }
    if other_page_ids:
        incident["other_page_ids"] = other_page_ids
    if signals_payload:
        incident["signals"] = signals_payload

    return sc, prose_chars, removing_rev_ids[-1], incident


def find_incidents(text: str, page_ids: list[int], limit: int, method: str = "fuzzy") -> dict:
    t0 = time.monotonic()
    clean_pages = sorted({int(pid) for pid in page_ids})
    if not clean_pages or limit <= 0:
        return {"contract": 1, "incidents": [], "cost": aot.cost()}

    pages_csv = aot.ints(clean_pages)
    rows = aot.sql(f"""
        SELECT c.cluster_id,
               c.n_units,
               c.n_pages,
               c.n_accounts,
               c.n_reinsertions,
               c.first_ts,
               c.last_ts,
               c.span_days,
               c.max_gap_days,
               c.campaign_signals,
               c.prose_chars,
               m.page_id,
               m.reverting_rev_id,
               m.inserting_rev_ids,
               m.inserted_at,
               m.removed_at
        FROM research.s7_cluster c
        JOIN research.s7_member m ON m.cluster_id = c.cluster_id
        WHERE c.method = {aot.quote(method)}
          AND c.cluster_id IN (
              SELECT DISTINCT m2.cluster_id
              FROM research.s7_member m2
              WHERE m2.page_id IN ({pages_csv})
          )
        ORDER BY c.cluster_id ASC, m.inserted_at ASC, m.reverting_rev_id ASC
    """)

    meta_by_cid: dict[int, dict] = {}
    members_by_cid: dict[int, list[dict]] = defaultdict(list)
    for r in rows:
        cid = int(r["cluster_id"])
        if cid not in meta_by_cid:
            meta_by_cid[cid] = r
        members_by_cid[cid].append(r)

    scored: list[tuple[float, int, int, dict]] = []
    for cid, meta in meta_by_cid.items():
        built = build_incident_from_cluster(meta, members_by_cid[cid])
        if built is not None:
            scored.append(built)

    # Highest score first; tie-break by longer prose then newer removing rev id.
    scored.sort(key=lambda item: (-item[0], -item[1], -item[2]))
    incidents = [item[3] for item in scored[:limit]]

    elapsed = time.monotonic() - t0
    aot.log(
        f"s7 ({method}): {len(clean_pages)} page(s), {len(scored)} touching cluster(s) -> "
        f"{len(incidents)} incident(s) in {elapsed:.3f}s"
    )
    return {
        "contract": 1,
        "incidents": incidents,
        "cost": aot.cost(),
        "debug": {
            "method": method,
            "candidate_clusters": len(scored),
            "returned_incidents": len(incidents),
            "elapsed_s": round(elapsed, 3),
        },
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--method", choices=("exact", "fuzzy"), default="fuzzy")
    args = ap.parse_args()

    req = json.load(sys.stdin)
    if req.get("kind") != "signals":
        sys.exit(f"s7 handles only kind 'signals', got {req.get('kind')!r}")
    resp = find_incidents(req["text"], req["page_ids"], req["limit"], method=args.method)
    json.dump(resp, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
