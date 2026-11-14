#!/usr/bin/env python3
"""P1.S3 (#30) approach A recovery & exposure-duration report on the dev golden split.

Reads `research/eval/golden.json` (strictly `split == "dev"` only — never touches test topics),
`research.s3_item`, `research.s1_revert`, `research.s1_reverted_edit`, `research.s3_diff`, and
`corpus.revision` / `corpus.article`, then writes `research/30-s3-extract/results/s3a-recovery.md`.

    python3 research/30-s3-extract/report.py
"""
from __future__ import annotations

import json
import math
import pathlib
import sys
from collections import Counter

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
sys.path.insert(0, str(HERE))
import aot  # noqa: E402
from variant import merged_exposure_seconds, normalise_prose  # noqa: E402

GOLDEN_PATH = HERE.parent / "eval" / "golden.json"
OUT_PATH = HERE / "results" / "s3a-recovery.md"
LABEL_ORDER = ("campaign", "contested", "reverted", "not-incident")


def percentile(sorted_vals: list[float], q: float) -> float:
    """Linear-interpolated percentile over a non-empty sorted list (`0.0 <= q <= 1.0`)."""
    if not sorted_vals:
        return 0.0
    if len(sorted_vals) == 1:
        return float(sorted_vals[0])
    pos = q * (len(sorted_vals) - 1)
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    if lo == hi:
        return float(sorted_vals[lo])
    frac = pos - lo
    return float(sorted_vals[lo]) * (1.0 - frac) + float(sorted_vals[hi]) * frac


def fmt_duration(seconds: float) -> str:
    days = seconds / 86400.0
    hours = seconds / 3600.0
    if days >= 1.0:
        return f"{days:.1f} d ({hours:.1f} h)"
    if hours >= 1.0:
        return f"{hours:.2f} h ({seconds / 60.0:.0f} min)"
    return f"{seconds / 60.0:.1f} min ({seconds:.0f} s)"


def _parse_int_csv(s: str) -> set[int]:
    return {int(x) for x in s.split(",") if x.strip()}


def _clean_snippet(prose: str, limit: int = 120) -> str:
    # Apply IP masking defensively on prose snippets so no raw IP can ever enter a report.
    masked = aot.mask_comment(prose)
    single = " ".join(masked.split())
    return single[:limit]


def generate_report() -> str:
    golden = json.loads(GOLDEN_PATH.read_text(encoding="utf-8"))
    # Strictly dev split only (never inspect test split).
    dev_topics = [t for t in golden["topics"] if t["split"] == "dev"]
    dev_items: list[dict] = []
    article_titles: dict[int, str] = {}
    dev_article_pages: set[int] = set()
    for t in dev_topics:
        for a in t["articles"]:
            pid = int(a["page_id"])
            dev_article_pages.add(pid)
            article_titles.setdefault(pid, a["title"])
        for it in t["items"]:
            dev_items.append(dict(it, topic_id=t["id"]))

    item_pages = sorted({
        int(p)
        for it in dev_items
        for p in [it["page_id"], *it.get("other_page_ids", [])]
    })
    query_pages = sorted(dev_article_pages | set(item_pages))

    # Fill any missing titles from corpus.article.
    missing_title_pages = [p for p in query_pages if p not in article_titles]
    if missing_title_pages:
        for r in aot.sql(f"""
            SELECT page_id, title
            FROM corpus.article
            WHERE lang = 'fi' AND page_id IN ({aot.ints(missing_title_pages)})
        """):
            article_titles[int(r["page_id"])] = r["title"]

    # Fetch all s3_item rows on dev pages, plus overall substantive exposure across all s3_item rows.
    s3_rows_dev = aot.sql(f"""
        SELECT reverting_rev_id, page_id, class, inserting_rev_ids, inserted_prose,
               inserted_at, removed_at, exposure_seconds, prose_chars, sentence_count,
               refs_added, is_substantive, is_blanking
        FROM research.s3_item
        WHERE page_id IN ({aot.ints(query_pages)})
        ORDER BY reverting_rev_id ASC
    """)
    for r in s3_rows_dev:
        r["ins_set"] = _parse_int_csv(r["inserting_rev_ids"])

    all_substantive_exp = [
        int(r["exposure_seconds"])
        for r in aot.sql("SELECT exposure_seconds FROM research.s3_item WHERE is_substantive = 1")
    ]
    s3_total_meta = aot.sql("SELECT COUNT(*) AS rows_total, COUNT(DISTINCT page_id) AS pages FROM research.s3_item")[0]
    s3_diff_pages_n = int(aot.sql("SELECT COUNT(DISTINCT page_id) AS pages FROM research.s3_diff")[0]["pages"])

    # Index s3_item rows by inserting rev_id for fast matching.
    items_by_ins_rev: dict[int, list[dict]] = {}
    for r in s3_rows_dev:
        for rev_id in r["ins_set"]:
            items_by_ins_rev.setdefault(rev_id, []).append(r)

    # Fetch timestamps of every dev golden item's inserting revisions (scoped by page_id + rev_id PK).
    revs_by_page: dict[int, set[int]] = {}
    for it in dev_items:
        pages_for_it = [int(it["page_id"]), *(int(p) for p in it.get("other_page_ids", []))]
        for p in pages_for_it:
            revs_by_page.setdefault(p, set()).update(int(rv) for rv in it["inserting_rev_ids"])
            revs_by_page.setdefault(p, set()).update(int(rv) for rv in it["removing_rev_ids"])

    rev_ts_map: dict[int, str] = {}
    for pid, rset in sorted(revs_by_page.items()):
        if not rset:
            continue
        for row in aot.sql(f"""
            SELECT rev_id, ts
            FROM corpus.revision
            WHERE page_id = {pid} AND rev_id IN ({aot.ints(rset)})
        """):
            rev_ts_map[int(row["rev_id"])] = str(row["ts"])

    # Also load S1 reverts + S1 reverted edits + S3 edit diffs on item_pages to diagnose unrecovered items.
    s1_reverts_by_id: dict[int, dict] = {}
    for r in aot.sql(f"""
        SELECT page_id, reverting_rev_id, first_reverted_rev_id, last_reverted_rev_id, class, exclude_reason
        FROM research.s1_revert
        WHERE page_id IN ({aot.ints(item_pages)})
    """):
        s1_reverts_by_id[int(r["reverting_rev_id"])] = r

    s1_owned_by_ins: dict[int, list[int]] = {}
    for e in aot.sql(f"""
        SELECT rev_id, reverting_rev_id
        FROM research.s1_reverted_edit
        WHERE page_id IN ({aot.ints(item_pages)})
    """):
        s1_owned_by_ins.setdefault(int(e["rev_id"]), []).append(int(e["reverting_rev_id"]))

    s3_span_exists = {
        int(r["rev_id"])
        for r in aot.sql(f"""
            SELECT rev_id
            FROM research.s3_diff
            WHERE kind = 'span' AND page_id IN ({aot.ints(item_pages)}) AND error IS NULL
        """)
    }

    all_diag_revs = sorted({
        int(rv)
        for it in dev_items
        for rv in [*it["inserting_rev_ids"], *it["removing_rev_ids"]]
    })
    edit_diff_map: dict[int, dict] = {}
    for start in range(0, len(all_diag_revs), 1000):
        chunk = all_diag_revs[start:start + 1000]
        for d in aot.sql(f"""
            SELECT rev_id, added_prose, removed_prose, added_prose_chars, removed_prose_chars
            FROM research.s3_diff
            WHERE kind = 'edit' AND rev_id IN ({aot.ints(chunk)})
        """):
            edit_diff_map[int(d["rev_id"])] = d

    # ── 1. Per-label recovery, right removal, right window ───────────────────────────────────────────────
    per_label: dict[str, Counter[str]] = {lbl: Counter() for lbl in LABEL_ORDER}
    recovered_matches: dict[str, list[dict]] = {}
    unrecovered_rows: list[dict] = []

    for it in dev_items:
        lbl = it["label"]
        per_label[lbl]["total"] += 1
        gold_ins = {int(r) for r in it["inserting_rev_ids"]}
        gold_rem = {int(r) for r in it["removing_rev_ids"]}
        ins_timestamps = [rev_ts_map[r] for r in gold_ins if r in rev_ts_map]
        gold_first_ts = min(ins_timestamps) if ins_timestamps else None

        seen_rr: set[int] = set()
        matches: list[dict] = []
        for rv in sorted(gold_ins):
            for s3r in items_by_ins_rev.get(rv, []):
                rr = int(s3r["reverting_rev_id"])
                if rr not in seen_rr:
                    seen_rr.add(rr)
                    matches.append(s3r)

        if matches:
            recovered_matches[it["id"]] = matches
            per_label[lbl]["recovered"] += 1
            right_rem = any(int(m["reverting_rev_id"]) in gold_rem for m in matches)
            right_win = gold_first_ts is not None and any(str(m["inserted_at"]) == gold_first_ts for m in matches)
            right_all = gold_first_ts is not None and any(
                int(m["reverting_rev_id"]) in gold_rem and str(m["inserted_at"]) == gold_first_ts
                for m in matches
            )
            if right_rem:
                per_label[lbl]["right_removal"] += 1
            if right_win:
                per_label[lbl]["right_window"] += 1
            if right_all:
                per_label[lbl]["all_three"] += 1
        else:
            reason, detail = _classify_unrecovered(
                it, s1_reverts_by_id, s1_owned_by_ins, s3_span_exists, edit_diff_map
            )
            unrecovered_rows.append({
                "id": it["id"],
                "label": lbl,
                "exclusion": it.get("exclusion") or "–",
                "page_id": int(it["page_id"]),
                "inserting_rev_ids": sorted(gold_ins),
                "removing_rev_ids": sorted(gold_rem),
                "reason": reason,
                "detail": detail,
            })

    # ── 2. Agreement of is_substantive=0 with not-substantive & is_blanking=1 with blanking ──────────────
    sub_cm = Counter()
    blk_cm = Counter()
    blk_any_cm = Counter()
    incident_false_nonsub = 0
    incident_false_blk = 0
    incident_recovered_n = 0

    for it in dev_items:
        matches = recovered_matches.get(it["id"])
        if not matches:
            continue
        is_gold_nonsub = it["label"] == "not-incident" and it.get("exclusion") == "not-substantive"
        is_gold_blk = it["label"] == "not-incident" and it.get("exclusion") == "blanking"

        pred_nonsub = all(int(m["is_substantive"]) == 0 for m in matches)
        pred_blk_all = all(int(m["is_blanking"]) == 1 for m in matches)
        pred_blk_any = any(int(m["is_blanking"]) == 1 for m in matches)

        sub_cm[(is_gold_nonsub, pred_nonsub)] += 1
        blk_cm[(is_gold_blk, pred_blk_all)] += 1
        blk_any_cm[(is_gold_blk, pred_blk_any)] += 1

        if it["label"] in ("reverted", "contested", "campaign"):
            incident_recovered_n += 1
            if pred_nonsub:
                incident_false_nonsub += 1
            if pred_blk_any:
                incident_false_blk += 1

    # ── 3. Exposure-duration distributions & 5 longest-lived golden contested items ──────────────────────
    dev_sub_exp = sorted(int(r["exposure_seconds"]) for r in s3_rows_dev if int(r["is_substantive"]) == 1)
    dev_cand_exp = sorted(
        int(r["exposure_seconds"])
        for r in s3_rows_dev
        if int(r["is_substantive"]) == 1
        and int(r["is_blanking"]) == 0
        and r["class"] not in ("bot", "self_revert", "excluded")
    )
    all_sub_exp = sorted(all_substantive_exp)

    contested_durations: list[dict] = []
    for it in dev_items:
        if it["label"] != "contested":
            continue
        matches = recovered_matches.get(it["id"])
        if not matches:
            continue
        sub_matches = [m for m in matches if int(m["is_substantive"]) == 1] or matches
        gold_rem = {int(r) for r in it["removing_rev_ids"]}
        exact_rem_matches = [m for m in sub_matches if int(m["reverting_rev_id"]) in gold_rem]
        used = exact_rem_matches if exact_rem_matches else sub_matches

        windows = [(str(m["inserted_at"]), str(m["removed_at"])) for m in used]
        live_secs = merged_exposure_seconds(windows)
        best_prose_m = max(used, key=lambda m: (len((m.get("inserted_prose") or "").strip()), int(m["reverting_rev_id"])))
        prose = (best_prose_m.get("inserted_prose") or "").strip()
        if not prose:
            # Fall back to the inserting edit's added_prose if the span prose was empty.
            for rv in it["inserting_rev_ids"]:
                ep = (edit_diff_map.get(int(rv), {}).get("added_prose") or "").strip()
                if ep:
                    prose = ep
                    break
        contested_durations.append({
            "id": it["id"],
            "page_id": int(it["page_id"]),
            "article": article_titles.get(int(it["page_id"]), f"Page {it['page_id']}"),
            "snippet": _clean_snippet(prose, 120),
            "exposure_seconds": live_secs,
            "days": live_secs / 86400.0,
        })

    contested_exp_sorted = sorted(d["exposure_seconds"] for d in contested_durations)
    top5_contested = sorted(
        contested_durations,
        key=lambda d: (-d["exposure_seconds"], d["id"]),
    )[:5]

    # ── Render Markdown ──────────────────────────────────────────────────────────────────────────────────
    lines: list[str] = []
    lines.append("# P1.S3 Approach A (`s3_item`) · Dev Golden Recovery & Exposure Report (#30)")
    lines.append("")
    lines.append(
        f"Dataset `{golden['dataset']}` · Golden `{golden['version']}` (`dev` split only: "
        f"{len(dev_topics)} topics, {len(dev_items)} items across {len(item_pages)} item pages) · "
        f"`research.s3_diff` pages: `{s3_diff_pages_n}` · `research.s3_item` rows: "
        f"`{int(s3_total_meta['rows_total']):,}` across `{int(s3_total_meta['pages'])}` pages."
    )
    lines.append("")

    lines.append("## 1. Golden Item Recovery per Label (Dev Split)")
    lines.append("")
    lines.append("- **Recovered:** ≥ 1 `s3_item` row whose `inserting_rev_ids` intersects the golden item's `inserting_rev_ids`.")
    lines.append("- **Right removal:** ≥ 1 matching `s3_item` row whose `reverting_rev_id` is in the golden item's `removing_rev_ids`.")
    lines.append("- **Right window:** ≥ 1 matching `s3_item` row whose `inserted_at` equals the golden item's first inserting revision timestamp.")
    lines.append("- **All three:** ≥ 1 matching `s3_item` row satisfying intersection, right removal, and right window start simultaneously.")
    lines.append("")
    lines.append("| Label | Golden items | Recovered | % | Right removal | % of total | Right window | % of total | All three |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|---:|---:|")

    def _pct(num: int, den: int) -> str:
        return f"{100.0 * num / den:.1f}%" if den else "–"

    for lbl in LABEL_ORDER:
        c = per_label[lbl]
        tot = c["total"]
        lines.append(
            f"| `{lbl}` | {tot} | {c['recovered']} | {_pct(c['recovered'], tot)} | "
            f"{c['right_removal']} | {_pct(c['right_removal'], tot)} | "
            f"{c['right_window']} | {_pct(c['right_window'], tot)} | {c['all_three']} |"
        )

    strict_tot = sum(per_label[l]["total"] for l in ("campaign", "contested"))
    strict_rec = sum(per_label[l]["recovered"] for l in ("campaign", "contested"))
    strict_rem = sum(per_label[l]["right_removal"] for l in ("campaign", "contested"))
    strict_win = sum(per_label[l]["right_window"] for l in ("campaign", "contested"))
    strict_all = sum(per_label[l]["all_three"] for l in ("campaign", "contested"))
    lines.append(
        f"| **Strict incidents (`contested` + `campaign`)** | **{strict_tot}** | **{strict_rec}** | "
        f"**{_pct(strict_rec, strict_tot)}** | **{strict_rem}** | **{_pct(strict_rem, strict_tot)}** | "
        f"**{strict_win}** | **{_pct(strict_win, strict_tot)}** | **{strict_all}** |"
    )

    all_tot = sum(per_label[l]["total"] for l in LABEL_ORDER)
    all_rec = sum(per_label[l]["recovered"] for l in LABEL_ORDER)
    all_rem = sum(per_label[l]["right_removal"] for l in LABEL_ORDER)
    all_win = sum(per_label[l]["right_window"] for l in LABEL_ORDER)
    all_three = sum(per_label[l]["all_three"] for l in LABEL_ORDER)
    lines.append(
        f"| **All dev items** | **{all_tot}** | **{all_rec}** | **{_pct(all_rec, all_tot)}** | "
        f"**{all_rem}** | **{_pct(all_rem, all_tot)}** | **{all_win}** | **{_pct(all_win, all_tot)}** | **{all_three}** |"
    )
    lines.append("")

    # ── Section 2: Filter agreement ──────────────────────────────────────────────────────────────────────
    lines.append("## 2. Filter Agreement (`is_substantive=0` vs `not-substantive`, `is_blanking=1` vs `blanking`)")
    lines.append("")
    n_rec = len(recovered_matches)
    sub_tp = sub_cm[(True, True)]
    sub_fn = sub_cm[(True, False)]
    sub_fp = sub_cm[(False, True)]
    sub_tn = sub_cm[(False, False)]
    sub_agree = _pct(sub_tp + sub_tn, n_rec)

    blk_tp = blk_cm[(True, True)]
    blk_fn = blk_cm[(True, False)]
    blk_fp = blk_cm[(False, True)]
    blk_tn = blk_cm[(False, False)]
    blk_agree = _pct(blk_tp + blk_tn, n_rec)

    unrec_blanking = sum(1 for u in unrecovered_rows if u["exclusion"] == "blanking")
    unrec_nonsub = sum(1 for u in unrecovered_rows if u["exclusion"] == "not-substantive")

    lines.append(f"Evaluated over the **{n_rec}** recovered dev golden items (plus notes on unrecovered exclusions):")
    lines.append("")
    lines.append("| Rule vs Golden Exclusion | TP | FP | FN | TN | Overall Agreement | Exclusion Recall | Real Incidents Falsely Filtered |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|---:|")
    lines.append(
        f"| `is_substantive = 0` vs `exclusion = 'not-substantive'` | {sub_tp} | {sub_fp} | {sub_fn} | {sub_tn} | "
        f"{sub_agree} | {sub_tp} / {sub_tp + sub_fn} ({_pct(sub_tp, sub_tp + sub_fn)}) | "
        f"{incident_false_nonsub} / {incident_recovered_n} ({_pct(incident_false_nonsub, incident_recovered_n)}) |"
    )
    lines.append(
        f"| `is_blanking = 1` (all spans) vs `exclusion = 'blanking'` | {blk_tp} | {blk_fp} | {blk_fn} | {blk_tn} | "
        f"{blk_agree} | {blk_tp} / {blk_tp + blk_fn} ({_pct(blk_tp, blk_tp + blk_fn)}) | "
        f"{incident_false_blk} / {incident_recovered_n} ({_pct(incident_false_blk, incident_recovered_n)}) |"
    )
    if blk_any_cm != blk_cm:
        b2_tp = blk_any_cm[(True, True)]
        b2_fp = blk_any_cm[(False, True)]
        b2_fn = blk_any_cm[(True, False)]
        b2_tn = blk_any_cm[(False, False)]
        lines.append(
            f"| `is_blanking = 1` (any span) vs `exclusion = 'blanking'` | {b2_tp} | {b2_fp} | {b2_fn} | {b2_tn} | "
            f"{_pct(b2_tp + b2_tn, n_rec)} | {b2_tp} / {b2_tp + b2_fn} ({_pct(b2_tp, b2_tp + b2_fn)}) | "
            f"{incident_false_blk} / {incident_recovered_n} ({_pct(incident_false_blk, incident_recovered_n)}) |"
        )
    lines.append("")
    lines.append(
        f"- Note: `{unrec_blanking}` additional golden `blanking` item(s) were already filtered upstream by S1's "
        f"`excluded` (`blanking_collision`) guard, and `{unrec_nonsub}` golden `not-substantive` item(s) had no S1 identity revert."
    )
    lines.append("")

    # ── Section 3: Unrecovered golden items ──────────────────────────────────────────────────────────────
    lines.append("## 3. Unrecovered Dev Golden Items & Reasons")
    lines.append("")
    reason_order = ("no identity revert", "partial removal", "span missing from s3_diff", "excluded class")
    reason_counts = Counter(u["reason"] for u in unrecovered_rows)
    other_reasons = sorted(set(reason_counts) - set(reason_order))

    lines.append("| Reason | Total | `campaign` | `contested` | `reverted` | `not-incident` |")
    lines.append("|---|---:|---:|---:|---:|---:|")
    for rname in [*reason_order, *other_reasons]:
        sub_u = [u for u in unrecovered_rows if u["reason"] == rname]
        by_l = Counter(u["label"] for u in sub_u)
        lines.append(
            f"| `{rname}` | {len(sub_u)} | {by_l['campaign']} | {by_l['contested']} | "
            f"{by_l['reverted']} | {by_l['not-incident']} |"
        )
    lines.append(
        f"| **Total unrecovered** | **{len(unrecovered_rows)}** | "
        f"**{sum(1 for u in unrecovered_rows if u['label'] == 'campaign')}** | "
        f"**{sum(1 for u in unrecovered_rows if u['label'] == 'contested')}** | "
        f"**{sum(1 for u in unrecovered_rows if u['label'] == 'reverted')}** | "
        f"**{sum(1 for u in unrecovered_rows if u['label'] == 'not-incident')}** |"
    )
    lines.append("")

    if unrecovered_rows:
        lines.append("| Item | Label | Exclusion | Page | Inserting revs | Removing revs | Reason | Notes |")
        lines.append("|---|---|---|---:|---|---|---|---|")
        for u in unrecovered_rows:
            ins_s = ", ".join(str(x) for x in u["inserting_rev_ids"][:4])
            if len(u["inserting_rev_ids"]) > 4:
                ins_s += f" (+{len(u['inserting_rev_ids']) - 4})"
            rem_s = ", ".join(str(x) for x in u["removing_rev_ids"][:4]) or "–"
            if len(u["removing_rev_ids"]) > 4:
                rem_s += f" (+{len(u['removing_rev_ids']) - 4})"
            lines.append(
                f"| `{u['id']}` | `{u['label']}` | `{u['exclusion']}` | `{u['page_id']}` | "
                f"`{ins_s}` | `{rem_s}` | `{u['reason']}` | {u['detail']} |"
            )
        lines.append("")

    # ── Section 4: Exposure-duration distribution ────────────────────────────────────────────────────────
    lines.append("## 4. Exposure-Duration Distribution")
    lines.append("")
    lines.append("| Cohort | Count | Median | p75 | p90 | Max |")
    lines.append("|---|---:|---:|---:|---:|---:|")

    for cohort_name, vals in [
        ("Overall substantive `s3_item` rows (all `s3_diff` pages, `is_substantive=1`)", all_sub_exp),
        ("Dev-split substantive `s3_item` rows (`is_substantive=1`)", dev_sub_exp),
        ("Dev-split candidate `s3_item` rows (`is_substantive=1, is_blanking=0`, non-bot/non-self)", dev_cand_exp),
        ("Golden `contested` dev items (recovered live exposure)", contested_exp_sorted),
    ]:
        if vals:
            p50 = percentile(vals, 0.50)
            p75 = percentile(vals, 0.75)
            p90 = percentile(vals, 0.90)
            mx = float(vals[-1])
            lines.append(
                f"| {cohort_name} | {len(vals):,} | {fmt_duration(p50)} | "
                f"{fmt_duration(p75)} | {fmt_duration(p90)} | {fmt_duration(mx)} |"
            )
        else:
            lines.append(f"| {cohort_name} | 0 | – | – | – | – |")
    lines.append("")

    # ── Section 5: 5 longest-lived golden contested items ────────────────────────────────────────────────
    lines.append("## 5. Top 5 Longest-Lived Golden `contested` Items (Dev Split)")
    lines.append("")
    for idx, d in enumerate(top5_contested, 1):
        lines.append(f"{idx}. {d['article']}: {d['snippet']} — live for {d['days']:.1f} days (`{d['id']}`)")
    lines.append("")

    return chr(10).join(lines)


def _classify_unrecovered(
    it: dict,
    s1_reverts_by_id: dict[int, dict],
    s1_owned_by_ins: dict[int, list[int]],
    s3_span_exists: set[int],
    edit_diff_map: dict[int, dict],
) -> tuple[str, str]:
    """Classify why a dev golden item was not recovered into one of the four contract reasons."""
    gold_ins = [int(r) for r in it["inserting_rev_ids"]]
    gold_rem = [int(r) for r in it["removing_rev_ids"]]

    # 1. Check if a non-excluded S1 revert owned any inserting rev, and whether its span diff is missing.
    owned_reverts = sorted({rr for ins in gold_ins for rr in s1_owned_by_ins.get(ins, [])})
    if owned_reverts:
        missing = [rr for rr in owned_reverts if rr not in s3_span_exists]
        if missing:
            return "span missing from s3_diff", f"S1 revert(s) `{missing[0]}` missing from `s3_diff` (`kind='span'`)"
        # Otherwise, the span existed in s3_item, meaning another edit in the same multi-edit span added prose/refs
        # while the golden inserting rev had 0 added prose/refs.
        return "partial removal", f"Span `{owned_reverts[0]}` attributed insertion to sibling prose-adding edit in span"

    # 2. Check if any removing_rev_id is in s1_revert with class == 'excluded'.
    excl_rems = [
        s1_reverts_by_id[rm]
        for rm in gold_rem
        if rm in s1_reverts_by_id and s1_reverts_by_id[rm]["class"] == "excluded"
    ]
    if excl_rems:
        reasons = sorted({str(r.get("exclude_reason") or "excluded") for r in excl_rems})
        return "excluded class", f"S1 revert `{excl_rems[0]['reverting_rev_id']}` excluded (`{', '.join(reasons)}`)"

    # Also check if any non-excluded s1_revert in gold_rem is missing from s3_diff.
    non_excl_rems = [
        s1_reverts_by_id[rm]
        for rm in gold_rem
        if rm in s1_reverts_by_id and s1_reverts_by_id[rm]["class"] != "excluded"
    ]
    if non_excl_rems:
        missing = [int(r["reverting_rev_id"]) for r in non_excl_rems if int(r["reverting_rev_id"]) not in s3_span_exists]
        if missing:
            return "span missing from s3_diff", f"Removing S1 revert `{missing[0]}` missing from `s3_diff`"

    # 3. No removing revision at all (kept).
    if not gold_rem:
        return "no identity revert", "Content was kept (no removing revision)"

    # 4. Distinguish clean non-SHA1 removal after intermediate edits ("no identity revert")
    #    from partial/modified deletion or rewrite ("partial removal").
    ins_prose_parts = [
        normalise_prose((edit_diff_map.get(rv) or {}).get("added_prose"))
        for rv in gold_ins
    ]
    ins_norm = " ".join(p for p in ins_prose_parts if p)

    rem_diffs = [edit_diff_map.get(rm) for rm in gold_rem if rm in edit_diff_map]
    if rem_diffs and ins_norm:
        rem_norm = " ".join(
            p for d in rem_diffs if d and (p := normalise_prose(d.get("removed_prose")))
        )
        rem_added_chars = sum(int((d or {}).get("added_prose_chars") or 0) for d in rem_diffs)
        if rem_norm == ins_norm and rem_added_chars == 0:
            return (
                "no identity revert",
                "Exact added prose deleted later without restoring an earlier page SHA-1 (intermediate edits intervened)",
            )

    return (
        "partial removal",
        "Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert",
    )


def main() -> int:
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    md = generate_report()
    OUT_PATH.write_text(md, encoding="utf-8")
    print(f"Wrote {OUT_PATH} ({len(md.splitlines())} lines)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
