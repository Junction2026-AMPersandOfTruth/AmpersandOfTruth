#!/usr/bin/env python3
"""P1.S6 (#33): Compact per-edit author feature lookup over precomputed `research.s6_*` tables.

Zero window functions and zero `corpus.revision` scans at request time:
`features_for(rev_ids)` resolves `rev_id -> user` via PK-indexed `research.s6_rev_author`,
joins PK-indexed `research.s6_editor_profile` and `research.s6_ip_prefix`, and returns
privacy-safe (`aot.user_display` / masked prefix) feature dicts ready to attach to candidate edits.

Library usage:
    from features import features_for
    feats = features_for([24295156, 24293890])

CLI usage:
    python3 research/33-s6-editors/features.py 24295156 24293890
    python3 research/33-s6-editors/features.py --benchmark 500
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time
from collections.abc import Iterable

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

_CHUNK_SIZE = 950


def mask_ip_prefix(ip_prefix: str | None) -> str | None:
    """Convert a CIDR prefix ('a.b.c.0/24' or 'h1:h2:h3:h4::/64') to its P0.7 masked form."""
    if not ip_prefix:
        return None
    addr = ip_prefix.split("/", 1)[0]
    disp = aot.user_display(addr)
    if disp.endswith(" (anonymous)"):
        return disp.removesuffix(" (anonymous)")
    return disp


def features_for(rev_ids: Iterable[int]) -> dict[int, dict]:
    """Return `{rev_id: feature_dict}` for each found `rev_id` using PK lookups only."""
    unique_ids = list(dict.fromkeys(int(r) for r in rev_ids))
    if not unique_ids:
        return {}

    out: dict[int, dict] = {}
    for start in range(0, len(unique_ids), _CHUNK_SIZE):
        batch = unique_ids[start : start + _CHUNK_SIZE]
        rows = aot.sql(f"""
            SELECT STRAIGHT_JOIN
                   ra.rev_id,
                   ra.page_id,
                   ep.user,
                   ep.kind,
                   ep.is_bot,
                   ep.edits,
                   ep.active_days,
                   ep.lifespan_days,
                   ep.distinct_pages,
                   ep.top_page_share,
                   ep.top_category_share,
                   ep.reverted_ratio,
                   ep.reverts_of_others_ratio,
                   ep.summary_usage,
                   ep.custom_summary_usage,
                   ep.median_abs_bytes_delta,
                   ep.single_purpose,
                   ep.throwaway,
                   ep.ip_prefix,
                   ep.coactivity_partners,
                   ep.coactivity_reverted_partners,
                   pn.editors AS pfx_editors,
                   pn.edits AS pfx_edits,
                   pn.reverted_edits AS pfx_reverted_edits,
                   pn.pages_with_reverted_edits AS pfx_pages_with_reverted_edits,
                   p16.editors AS pfx16_editors,
                   p16.reverted_edits AS pfx16_reverted_edits,
                   p16.pages_with_reverted_edits AS pfx16_pages_with_reverted_edits
            FROM research.s6_rev_author ra USE INDEX (PRIMARY)
            JOIN research.s6_editor_profile ep ON ep.user = ra.user
            LEFT JOIN research.s6_ip_prefix pn ON pn.ip_prefix = ep.ip_prefix
            LEFT JOIN research.s6_ip_prefix p16 ON p16.ip_prefix = ep.ip_prefix_16
            WHERE ra.rev_id IN ({aot.ints(batch)})
        """)
        for r in rows:
            rid = int(r["rev_id"])
            raw_user = r["user"] if r["user"] != "" else None
            out[rid] = {
                "page_id": int(r["page_id"]),
                "user_display": aot.user_display(raw_user),
                "kind": r["kind"],
                "is_bot": bool(int(r["is_bot"])),
                "edits": int(r["edits"]),
                "active_days": int(r["active_days"]),
                "lifespan_days": float(r["lifespan_days"]),
                "distinct_pages": int(r["distinct_pages"]),
                "top_page_share": float(r["top_page_share"]),
                "top_category_share": float(r["top_category_share"]) if r["top_category_share"] is not None else None,
                "reverted_ratio": float(r["reverted_ratio"]),
                "reverts_of_others_ratio": float(r["reverts_of_others_ratio"]),
                "summary_usage": float(r["summary_usage"]),
                "custom_summary_usage": float(r["custom_summary_usage"]),
                "median_abs_bytes_delta": float(r["median_abs_bytes_delta"]),
                "single_purpose": bool(int(r["single_purpose"])),
                "throwaway": bool(int(r["throwaway"])),
                "coactivity_partners": int(r["coactivity_partners"]),
                "coactivity_reverted_partners": int(r["coactivity_reverted_partners"]),
                "ip_prefix_masked": mask_ip_prefix(r["ip_prefix"]),
                "ip_prefix_editors": int(r["pfx_editors"]) if r["pfx_editors"] is not None else None,
                "ip_prefix_edits": int(r["pfx_edits"]) if r["pfx_edits"] is not None else None,
                "ip_prefix_reverted_edits": int(r["pfx_reverted_edits"]) if r["pfx_reverted_edits"] is not None else None,
                "ip_prefix_pages_with_reverted_edits": (
                    int(r["pfx_pages_with_reverted_edits"]) if r["pfx_pages_with_reverted_edits"] is not None else None
                ),
                "ip_prefix_16_editors": int(r["pfx16_editors"]) if r["pfx16_editors"] is not None else None,
                "ip_prefix_16_reverted_edits": (
                    int(r["pfx16_reverted_edits"]) if r["pfx16_reverted_edits"] is not None else None
                ),
                "ip_prefix_16_pages_with_reverted_edits": (
                    int(r["pfx16_pages_with_reverted_edits"])
                    if r["pfx16_pages_with_reverted_edits"] is not None
                    else None
                ),
            }
    return out


def _run_benchmark(n: int) -> dict:
    sample_rows = aot.sql(f"""
        SELECT rev_id
        FROM research.s6_rev_author
        ORDER BY MOD(rev_id * 2654435761, 4294967296)
        LIMIT {int(n)}
    """)
    rev_ids = [int(r["rev_id"]) for r in sample_rows]
    t0 = time.monotonic()
    res = features_for(rev_ids)
    elapsed = round(time.monotonic() - t0, 4)
    return {
        "requested": len(rev_ids),
        "returned": len(res),
        "seconds": elapsed,
        "under_2s_budget": elapsed < 2.0,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Lookup S6 editor features by rev_id.")
    parser.add_argument("rev_ids", nargs="*", type=int, help="Revision IDs to fetch features for")
    parser.add_argument("--benchmark", type=int, default=0, metavar="N", help="Benchmark N deterministic rev_ids")
    args = parser.parse_args()

    if args.benchmark > 0:
        stats = _run_benchmark(args.benchmark)
        print(json.dumps(stats, indent=2))
        if not stats["under_2s_budget"] and args.benchmark <= 500:
            raise SystemExit(f"Benchmark exceeded 2.0s budget: {stats}")
        return

    res = features_for(args.rev_ids)
    print(json.dumps({str(k): v for k, v in res.items()}, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
