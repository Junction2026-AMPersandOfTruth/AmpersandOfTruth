#!/usr/bin/env python3
"""P1.S3 (#30) approach A signals variant over `research.s3_item` (CONTRACT.md §2.2).

Reads precomputed `research.s3_item` + `research.s5_revert_reason`, filters to substantive
non-blanking non-bot/non-self reverts, merges items on the same page that share normalised
`inserted_prose` or overlapping `inserting_rev_ids` into one incident per inserted content,
and ranks incidents with a transparent non-LLM formula.

    python3 research/eval/validate.py variant signals -- python3 research/30-s3-extract/variant.py
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import os
import pathlib
import re
import sys
import time
import unicodedata

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

EXCLUDED_CLASSES = ("excluded", "bot", "self_revert")
ACCURACY_POV_REASONS = frozenset({"factual_error", "neutrality_pov", "original_research"})
SOURCING_CONSENSUS_REASONS = frozenset({"sourcing", "consensus"})
DISPUTE_REASONS = ACCURACY_POV_REASONS | SOURCING_CONSENSUS_REASONS
NOISE_REASONS = frozenset({"vandalism_test", "maintenance", "copyright"})

_NL = chr(10)
_FRAG_JOIN_RE = re.compile(r'''\s+…\s+''')
_STRIP_CHARS = ".,;:!?'“”«»()[]{}-–— " + chr(9) + chr(10) + chr(13) + chr(34)

DEFAULT_FORMULA = "f3_dispute_gated"


def normalise_prose(prose: str | None) -> str:
    """Canonical form of `inserted_prose` for merging re-insertions on the same page.
    Returns `""` when no alphanumeric content is present (empty keys never merge)."""
    if not prose:
        return ""
    s = unicodedata.normalize("NFC", prose).casefold()
    s = _FRAG_JOIN_RE.sub(" ", s)
    s = " ".join(s.split()).strip(_STRIP_CHARS)
    s = " ".join(s.split())
    if not any(ch.isalnum() for ch in s):
        return ""
    return s


def _parse_ts(ts_str: str) -> dt.datetime:
    return dt.datetime.strptime(ts_str.strip(), "%Y-%m-%d %H:%M:%S").replace(tzinfo=dt.timezone.utc)


def _iso_utc(ts_str: str) -> str:
    return ts_str.strip().replace(" ", "T") + "Z"


def merged_exposure_seconds(windows: list[tuple[str, str]]) -> int:
    """Sum exposure duration across `(inserted_at, removed_at)` windows without double-counting overlaps."""
    parsed: list[tuple[dt.datetime, dt.datetime]] = []
    for start_s, end_s in windows:
        t1 = _parse_ts(start_s)
        t2 = _parse_ts(end_s)
        if t2 > t1:
            parsed.append((t1, t2))
    if not parsed:
        return 0
    parsed.sort(key=lambda iv: (iv[0], iv[1]))
    cur_s, cur_e = parsed[0]
    total = 0
    for s, e in parsed[1:]:
        if s <= cur_e:
            if e > cur_e:
                cur_e = e
        else:
            total += int((cur_e - cur_s).total_seconds())
            cur_s, cur_e = s, e
    total += int((cur_e - cur_s).total_seconds())
    return total


def _parse_int_csv(csv_str: str) -> list[int]:
    return [int(x) for x in csv_str.split(",") if x.strip()]


def _parse_editors(raw: str) -> list[str]:
    if not raw:
        return []
    s = raw.strip()
    if s.startswith("["):
        return [str(x) for x in json.loads(s)]
    return [x.strip() for x in s.split(",") if x.strip()]


def merge_page_items(items: list[dict]) -> list[list[dict]]:
    """Group items on one page into connected components sharing non-empty `normalise_prose`
    or overlapping `inserting_rev_ids`, guarding against `inserting ∩ removing` collisions."""
    if not items:
        return []
    ordered = sorted(items, key=lambda it: int(it["reverting_rev_id"]))
    n = len(ordered)
    parent = list(range(n))
    comp_ins: list[set[int]] = [set(it["inserting_rev_ids_list"]) for it in ordered]
    comp_rem: list[set[int]] = [{int(it["reverting_rev_id"])} for it in ordered]

    def find(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def try_union(a: int, b: int) -> None:
        ra, rb = find(a), find(b)
        if ra == rb:
            return
        merged_ins = comp_ins[ra] | comp_ins[rb]
        merged_rem = comp_rem[ra] | comp_rem[rb]
        # Never merge two spans if removing all reverting_rev_ids would leave zero inserting_rev_ids.
        if not (merged_ins - merged_rem):
            return
        if ra > rb:
            ra, rb = rb, ra
        parent[rb] = ra
        comp_ins[ra] = merged_ins
        comp_rem[ra] = merged_rem

    by_prose: dict[str, int] = {}
    by_ins_rev: dict[int, int] = {}
    for idx, it in enumerate(ordered):
        norm = normalise_prose(it.get("inserted_prose"))
        if norm:
            prev = by_prose.get(norm)
            if prev is not None:
                try_union(prev, idx)
            else:
                by_prose[norm] = idx
        for rev_id in it["inserting_rev_ids_list"]:
            prev = by_ins_rev.get(rev_id)
            if prev is not None:
                try_union(prev, idx)
            else:
                by_ins_rev[rev_id] = idx

    groups: dict[int, list[dict]] = {}
    for idx, it in enumerate(ordered):
        groups.setdefault(find(idx), []).append(it)
    return list(groups.values())


def score_incident(features: dict, formula: str = DEFAULT_FORMULA) -> float:
    """Compute a transparent ranking score for a merged incident."""
    prose_chars = int(features["prose_chars"])
    sentence_count = int(features["sentence_count"])
    refs_added = int(features["refs_added"])
    exposure_hours = float(features["total_exposure_seconds"]) / 3600.0
    reinsertions = int(features["reinsertion_count"])
    named_inserter = 1 if features["named_inserter"] else 0
    distinct_editors = int(features["distinct_editors"])
    classes = set(features["classes"])
    reasons = set(features["removal_reasons"])

    has_acc_pov = bool(reasons & ACCURACY_POV_REASONS)
    has_src_con = bool(reasons & SOURCING_CONSENSUS_REASONS)
    has_dispute_reason = has_acc_pov or has_src_con
    has_noise_reason = bool(reasons & NOISE_REASONS)

    if formula == "f1_additive":
        return (
            0.50 * math.log1p(min(prose_chars, 3000))
            + 0.40 * math.log1p(exposure_hours)
            + 1.50 * math.log1p(reinsertions)
            + 1.20 * named_inserter
            + (2.00 if has_dispute_reason else 0.0)
        )

    if formula == "f2_evidence_aligned":
        s = 0.60 * math.log1p(min(prose_chars, 2000))
        if prose_chars == 0:
            s -= 1.50
        elif sentence_count >= 1 and prose_chars >= 60:
            s += 0.40
        if refs_added > 0:
            s += 0.40
        s += 0.35 * math.log1p(exposure_hours)
        if has_acc_pov:
            s += 2.20
        elif has_src_con:
            s += 1.60
        s += 2.00 * math.log1p(reinsertions)
        if "edit_war" in classes:
            s += 1.80
        elif "dispute" in classes:
            s += 0.80
        elif classes == {"graffiti_cleanup"}:
            s -= 0.80
        s += 1.00 * named_inserter + 0.80 * min(max(distinct_editors - 1, 0), 2)
        if has_noise_reason and not has_dispute_reason:
            s -= 2.00
        return s

    if formula == "f3_dispute_gated":
        has_substantive_claim = prose_chars >= 50 and sentence_count >= 1
        has_community_dispute = reinsertions >= 1 or ("edit_war" in classes) or has_dispute_reason
        s = 0.55 * math.log1p(min(prose_chars, 1800))
        if prose_chars == 0:
            s -= 1.80
        elif has_substantive_claim:
            s += 0.50
        if refs_added > 0:
            s += 0.35
        s += 0.22 * math.log1p(exposure_hours)
        if has_acc_pov:
            s += 2.30
        elif has_src_con:
            s += 1.70
        s += 2.10 * math.log1p(reinsertions)
        if "edit_war" in classes:
            s += 1.90
        elif "dispute" in classes:
            s += 0.90
        elif classes == {"graffiti_cleanup"}:
            s -= 1.10
        if has_substantive_claim and has_community_dispute:
            s += 2.50
        s += 0.90 * named_inserter + 0.90 * min(max(distinct_editors - 1, 0), 2)
        if has_noise_reason and not has_dispute_reason:
            s -= 2.20
        return s

    raise ValueError(f"unknown scoring formula: {formula!r}")


def build_incident_from_group(
    page_id: int,
    group: list[dict],
    reasons_by_revert: dict[int, set[str]],
    formula: str = DEFAULT_FORMULA,
) -> dict:
    """Package a merged group of `s3_item` rows on one page into a CONTRACT §3 incident."""
    removing_rev_ids = sorted({int(it["reverting_rev_id"]) for it in group})
    removing_set = set(removing_rev_ids)
    raw_inserting = {r for it in group for r in it["inserting_rev_ids_list"]}
    clean_inserting = sorted(raw_inserting - removing_set)
    if not clean_inserting:
        clean_inserting = sorted(raw_inserting)

    inserting_set = set(clean_inserting)
    extra_reinsertions = {
        int(it["reinserted_rev_id"])
        for it in group
        if it.get("reinserted_rev_id") is not None
        and str(it["reinserted_rev_id"]) != ""
        and int(it["reinserted_rev_id"]) not in inserting_set
        and int(it["reinserted_rev_id"]) not in removing_set
    }
    reinsertion_count = (len(group) - 1) + len(extra_reinsertions)

    windows = [(str(it["inserted_at"]), str(it["removed_at"])) for it in group]
    total_exp = merged_exposure_seconds(windows)
    first_inserted = min(str(it["inserted_at"]) for it in group)
    last_removed = max(str(it["removed_at"]) for it in group)

    best_prose_item = max(group, key=lambda it: (len((it.get("inserted_prose") or "").strip()), int(it["reverting_rev_id"])))
    prose_text = (best_prose_item.get("inserted_prose") or "").strip()
    claim_fi = prose_text[:300] if prose_text else None

    all_editors = {ed for it in group for ed in it["inserting_editors_list"] if ed != "hidden"}
    named_inserter = any(int(it["inserting_anonymous_only"]) == 0 for it in group)
    classes = sorted({str(it["class"]) for it in group})
    reasons = sorted({r for rv in removing_rev_ids for r in reasons_by_revert.get(rv, set())})

    features = {
        "prose_chars": max(int(it["prose_chars"]) for it in group),
        "sentence_count": max(int(it["sentence_count"]) for it in group),
        "refs_added": max(int(it["refs_added"]) for it in group),
        "total_exposure_seconds": total_exp,
        "reinsertion_count": reinsertion_count,
        "named_inserter": named_inserter,
        "distinct_editors": len(all_editors),
        "classes": classes,
        "removal_reasons": reasons,
    }
    sc = round(score_incident(features, formula=formula), 4)

    return {
        "page_id": page_id,
        "inserting_rev_ids": clean_inserting,
        "removing_rev_ids": removing_rev_ids,
        "tier": "reverted",
        "score": sc,
        "claim_fi": claim_fi,
        "first_inserted_at": _iso_utc(first_inserted),
        "last_removed_at": _iso_utc(last_removed),
        "total_exposure_seconds": total_exp,
        "reinsertion_count": reinsertion_count,
    }


def find_incidents(text: str, page_ids: list[int], limit: int, formula: str = DEFAULT_FORMULA) -> dict:
    t0 = time.monotonic()
    clean_pages = sorted({int(pid) for pid in page_ids})
    if not clean_pages or limit <= 0:
        return {"contract": 1, "incidents": [], "cost": aot.cost()}

    pages_csv = aot.ints(clean_pages)
    excl_csv = ",".join(aot.quote(c) for c in EXCLUDED_CLASSES)

    rows = aot.sql(f"""
        SELECT reverting_rev_id, page_id, class, inserting_rev_ids, inserting_editors,
               inserting_anonymous_only, inserted_prose, inserted_at, removed_at,
               exposure_seconds, reinserted_rev_id, prose_chars, sentence_count,
               refs_added, is_substantive, is_blanking
        FROM research.s3_item
        WHERE page_id IN ({pages_csv})
          AND is_substantive = 1
          AND is_blanking = 0
          AND class NOT IN ({excl_csv})
        ORDER BY page_id ASC, reverting_rev_id ASC
    """)

    if not rows:
        elapsed = time.monotonic() - t0
        aot.log(f"s3a({formula}): {len(clean_pages)} page(s) -> 0 items in {elapsed:.3f}s")
        return {"contract": 1, "incidents": [], "cost": aot.cost()}

    rev_ids = sorted({int(r["reverting_rev_id"]) for r in rows})
    reasons_by_revert: dict[int, set[str]] = {}
    for start in range(0, len(rev_ids), 1000):
        chunk = rev_ids[start:start + 1000]
        for rr in aot.sql(f"""
            SELECT reverting_rev_id, removal_reasons
            FROM research.s5_revert_reason
            WHERE reverting_rev_id IN ({aot.ints(chunk)})
              AND removal_reasons <> ''
        """):
            reasons = {x.strip() for x in (rr.get("removal_reasons") or "").split(",") if x.strip()}
            if reasons:
                reasons_by_revert[int(rr["reverting_rev_id"])] = reasons

    by_page: dict[int, list[dict]] = {}
    for r in rows:
        r["inserting_rev_ids_list"] = _parse_int_csv(r["inserting_rev_ids"])
        r["inserting_editors_list"] = _parse_editors(r["inserting_editors"])
        by_page.setdefault(int(r["page_id"]), []).append(r)

    incidents: list[dict] = []
    for pid, page_items in by_page.items():
        for group in merge_page_items(page_items):
            incidents.append(build_incident_from_group(pid, group, reasons_by_revert, formula=formula))

    # Best score first; break ties deterministically by longer exposure then newest removing_rev_id.
    incidents.sort(key=lambda inc: (-inc["score"], -inc["total_exposure_seconds"], -max(inc["removing_rev_ids"])))
    top = incidents[:limit]

    elapsed = time.monotonic() - t0
    aot.log(
        f"s3a({formula}): {len(clean_pages)} page(s), {len(rows)} s3_item(s) -> "
        f"{len(incidents)} merged incident(s), returning {len(top)} in {elapsed:.3f}s"
    )
    return {
        "contract": 1,
        "incidents": top,
        "cost": aot.cost(),
        "debug": {
            "formula": formula,
            "raw_items": len(rows),
            "merged_incidents": len(incidents),
            "elapsed_s": round(elapsed, 3),
        },
    }


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--formula",
        default=os.environ.get("S3_FORMULA", DEFAULT_FORMULA),
        choices=["f1_additive", "f2_evidence_aligned", "f3_dispute_gated"],
        help="Scoring formula to rank merged incidents",
    )
    args = ap.parse_args(argv)

    req = json.load(sys.stdin)
    if req.get("kind") != "signals":
        sys.exit(f"s3a handles only kind 'signals', got {req.get('kind')!r}")
    resp = find_incidents(req["text"], req["page_ids"], int(req["limit"]), formula=args.formula)
    json.dump(resp, sys.stdout, ensure_ascii=False)
    sys.stdout.write(_NL)
    return 0


if __name__ == "__main__":
    sys.exit(main())
