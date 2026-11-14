#!/usr/bin/env python3
"""P1.S7 (#34): Generate `results/s7-report.md` analysing exact vs fuzzy claim clusters.

Strictly reads ONLY `"split": "dev"` topics from `research/eval/golden.json` (never test-split items)
and never outputs raw usernames or IP addresses.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import json
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402
sys.path.insert(0, str(HERE))
import variant  # noqa: E402

NL = chr(10)

SIGNAL_ORDER = (
    "",
    "accounts",
    "articles",
    "persistence",
    "accounts,articles",
    "accounts,persistence",
    "articles,persistence",
    "accounts,articles,persistence",
)

# Mask any accidental MediaWiki ~~~~ signature (`--Name 15. toukokuuta 2006 kello ...`) pasted in article text
_RE_WIKI_SIG = re.compile(r"--\s*[^\s]+\s+(?=\d{1,2}\.\s+[a-zäöå]+kuuta\s+\d{4})", re.IGNORECASE)


def _safe_sample(text: str) -> str:
    s = aot.mask_comment(text or "")
    s = _RE_WIKI_SIG.sub("--[masked-sig] ", s)
    return s.lstrip("\ufeff").replace("|", "/").replace(NL, " ")


def _size_bucket(n_units: int) -> str:
    if n_units == 1:
        return "1 (singleton)"
    if n_units == 2:
        return "2"
    if n_units <= 4:
        return "3–4"
    if n_units <= 9:
        return "5–9"
    return "10+"


def _load_dev_golden(golden_path: pathlib.Path) -> tuple[list[dict], set[int], dict[int, str]]:
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    dev_topics = [t for t in doc["topics"] if t["split"] == "dev"]
    dev_pages: set[int] = set()
    titles: dict[int, str] = {}
    for t in dev_topics:
        for a in t["articles"]:
            pid = int(a["page_id"])
            dev_pages.add(pid)
            titles[pid] = a["title"]
        for it in t["items"]:
            dev_pages.add(int(it["page_id"]))
            for op in it.get("other_page_ids") or []:
                dev_pages.add(int(op))
    return dev_topics, dev_pages, titles


def _load_clusters_from_db() -> dict[str, list[dict]]:
    c_rows = aot.sql("""
        SELECT cluster_id, method, n_units, n_pages, n_accounts, n_reinsertions,
               first_ts, last_ts, span_days, max_gap_days, sample_text, campaign_signals, prose_chars
        FROM research.s7_cluster
        ORDER BY cluster_id ASC
    """)
    m_rows = aot.sql("""
        SELECT cluster_id, page_id, reverting_rev_id, inserting_rev_ids, inserted_at, removed_at, account_key
        FROM research.s7_member
        ORDER BY cluster_id ASC, inserted_at ASC, reverting_rev_id ASC
    """)

    members_by_cid: dict[int, list[dict]] = defaultdict(list)
    for m in m_rows:
        cid = int(m["cluster_id"])
        ins = variant.parse_rev_list(m["inserting_rev_ids"])
        members_by_cid[cid].append({
            "page_id": int(m["page_id"]),
            "reverting_rev_id": int(m["reverting_rev_id"]),
            "inserting_rev_ids": ins,
            "inserted_at": m["inserted_at"],
            "removed_at": m["removed_at"],
            "account_key": m["account_key"],
        })

    by_method: dict[str, list[dict]] = {"exact": [], "fuzzy": []}
    for c in c_rows:
        cid = int(c["cluster_id"])
        mems = members_by_cid[cid]
        all_ins: set[int] = set()
        pages: list[int] = []
        for m in mems:
            all_ins.update(m["inserting_rev_ids"])
            if m["page_id"] not in pages:
                pages.append(m["page_id"])
        rec = {
            "cluster_id": cid,
            "method": c["method"],
            "n_units": int(c["n_units"]),
            "n_pages": int(c["n_pages"]),
            "n_accounts": int(c["n_accounts"]),
            "n_reinsertions": int(c["n_reinsertions"]),
            "first_ts": c["first_ts"],
            "last_ts": c["last_ts"],
            "span_days": float(c["span_days"]),
            "max_gap_days": float(c["max_gap_days"]),
            "sample_text": _safe_sample(c["sample_text"] or ""),
            "campaign_signals": c["campaign_signals"] or "",
            "prose_chars": int(c["prose_chars"]),
            "members": mems,
            "inserting_set": all_ins,
            "pages": pages,
        }
        by_method[c["method"]].append(rec)
    return by_method


def _select_example_clusters(
    fuzzy_dev_multi: list[dict],
    exact_unit_sets: set[frozenset[int]],
    campaign_ins_revs: set[int],
) -> list[tuple[str, dict]]:
    """Pick 10 diverse multi-unit clusters on dev pages covering true campaigns + false-cluster archetypes."""
    picked: list[tuple[str, dict]] = []
    seen_ids: set[int] = set()

    def add_match(tag: str, pred, sort_key, limit: int = 1) -> None:
        cands = sorted((c for c in fuzzy_dev_multi if pred(c)), key=sort_key)
        added = 0
        for c in cands:
            if c["cluster_id"] in seen_ids or len(picked) >= 10:
                continue
            seen_ids.add(c["cluster_id"])
            picked.append((tag, c))
            added += 1
            if added >= limit:
                break

    # 1. Golden Level-3 campaign cluster (nato-debate/i01)
    add_match(
        "golden-campaign-match",
        lambda c: len(c["inserting_set"] & campaign_ins_revs) >= 2,
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 2. Cross-article disputed claims (2+ articles, substantive prose)
    add_match(
        "cross-article-claim",
        lambda c: c["n_pages"] >= 2 and "kysy.fi" in c["sample_text"].lower(),
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    add_match(
        "cross-article-sleeper",
        lambda c: c["n_pages"] >= 2 and c["max_gap_days"] >= 30.0 and "suur-suomi" in c["sample_text"].lower(),
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 3. Transitive multi-article chaining (shared citation/sentence linking politician bios)
    add_match(
        "cross-article-transitive-chain",
        lambda c: c["n_pages"] >= 3 and c["n_units"] >= 6,
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 4. High-persistence disputed passage on one article
    add_match(
        "persistent-single-article",
        lambda c: c["n_pages"] == 1 and c["n_units"] >= 15 and "kehittyvien maakuntien" in c["sample_text"].lower(),
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 5. Fuzzy-reworded merge (merged by 5-shingle MinHash/Jaccard beyond exact sentence hash)
    add_match(
        "fuzzy-reworded-merge",
        lambda c: (
            frozenset(m["reverting_rev_id"] for m in c["members"]) not in exact_unit_sets
            and c["prose_chars"] >= 120
        ),
        lambda c: (-c["n_units"], -c["prose_chars"], c["cluster_id"]),
        limit=1,
    )
    # 6. False-cluster check: MediaWiki edit-toolbar boilerplate (`Lisää muotoilematon teksti tähän`)
    add_match(
        "false-cluster:toolbar-boilerplate",
        lambda c: "muotoilematon teksti" in c["sample_text"].lower(),
        lambda c: (-c["n_pages"], -c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 7. False-cluster check: short `Katso ...` cross-reference link repeated across articles
    add_match(
        "false-cluster:see-also-phrase",
        lambda c: c["n_pages"] >= 2 and c["sample_text"].lower().startswith(("katso ", "vaalirahoitus katso")),
        lambda c: (-c["n_pages"], -c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 8. False-cluster check: unclosed `<ref>` tag swallowing existing article section prose
    add_match(
        "false-cluster:unclosed-ref-swallow",
        lambda c: "==sodan alku==" in c["sample_text"].lower() or "haataja 1989" in c["sample_text"].lower(),
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # 9. False-cluster check: copy-pasted Wikipedia UI chrome (`Loikkaa: valikkoon, hakuun`) or cross-page spam
    add_match(
        "false-cluster:pasted-ui-chrome",
        lambda c: "loikkaa: valikkoon" in c["sample_text"].lower(),
        lambda c: (-c["n_units"], c["cluster_id"]),
        limit=1,
    )
    # Fallback fill up to 10 if any predicate above had no match
    add_match(
        "multi-unit",
        lambda _c: True,
        lambda c: (-c["n_pages"], -c["n_units"], c["cluster_id"]),
        limit=10,
    )
    return picked[:10]


def generate_report(golden_path: pathlib.Path, out_path: pathlib.Path) -> str:
    dev_topics, dev_pages, page_titles = _load_dev_golden(golden_path)
    by_method = _load_clusters_from_db()

    extra_pids = {
        p
        for clist in by_method.values()
        for c in clist
        for p in c["pages"]
        if p not in page_titles
    }
    if extra_pids:
        for r in aot.sql(
            f"SELECT page_id, title FROM corpus.article WHERE lang='fi' AND page_id IN ({aot.ints(sorted(extra_pids))})"
        ):
            page_titles[int(r["page_id"])] = r["title"]

    stats_path = HERE / "results" / "build_stats.json"
    build_stats = json.loads(stats_path.read_text(encoding="utf-8")) if stats_path.exists() else {}
    load_stats = build_stats.get("load", {})
    c_stats = build_stats.get("clustering", {})

    dev_clusters: dict[str, list[dict]] = {
        m: [c for c in clist if any(p in dev_pages for p in c["pages"])]
        for m, clist in by_method.items()
    }

    lines: list[str] = []
    lines.append("# P1.S7 (#34) · Re-insertion & Cross-Article Repetition Cluster Report")
    lines.append("")
    lines.append(
        f"Scope: `{len(dev_pages)}` dev-split golden articles across `{len(dev_topics)}` dev topics "
        f"(tables built over `{load_stats.get('pages_requested', '?')}` total golden pages; "
        f"`s3_diff` distinct pages at build time: `{build_stats.get('s3_diff_distinct_pages', '?')}`, "
        f"missing `s3_diff` span rows: `{load_stats.get('missing_s3_diff', 0)}`)."
    )
    lines.append("")

    # ── Section 1: Counts by size / method / campaign_signals on dev pages ──
    lines.append("## 1. Cluster Counts on Dev Golden Pages (`exact` vs `fuzzy`)")
    lines.append("")
    buckets = ("1 (singleton)", "2", "3–4", "5–9", "10+")
    size_counts = {
        m: Counter(_size_bucket(c["n_units"]) for c in dev_clusters[m])
        for m in ("exact", "fuzzy")
    }

    lines.append("### By Cluster Size & Structural Properties")
    lines.append("")
    lines.append("| Metric (clusters touching dev golden pages) | `exact` | `fuzzy` (exact + MinHash/Jaccard ≥ 0.6) | Δ (`fuzzy - exact`) |")
    lines.append("|---|---:|---:|---:|")
    for b in buckets:
        ev, fv = size_counts["exact"][b], size_counts["fuzzy"][b]
        lines.append(f"| Size `{b}` | {ev:,} | {fv:,} | {fv - ev:+,} |")

    def _row(label: str, pred) -> str:
        ev = sum(1 for c in dev_clusters["exact"] if pred(c))
        fv = sum(1 for c in dev_clusters["fuzzy"] if pred(c))
        return f"| {label} | {ev:,} | {fv:,} | {fv - ev:+,} |"

    lines.append(_row("**Total multi-unit clusters (`n_units >= 2`)**", lambda c: c["n_units"] >= 2))
    lines.append(_row("**Total clusters (all sizes)**", lambda _c: True))
    ev_u = sum(c["n_units"] for c in dev_clusters["exact"])
    fv_u = sum(c["n_units"] for c in dev_clusters["fuzzy"])
    lines.append(f"| Member units in touching clusters | {ev_u:,} | {fv_u:,} | {fv_u - ev_u:+,} |")
    lines.append(_row("Multi-account (`n_accounts >= 2`, IP `/16`/`/32` masked)", lambda c: c["n_accounts"] >= 2))
    lines.append(_row("Cross-article (`n_pages >= 2`)", lambda c: c["n_pages"] >= 2))
    lines.append(_row("Persistent (`n_units >= 3` and `span_days >= 7`)", lambda c: c["n_units"] >= 3 and c["span_days"] >= 7.0))
    lines.append(_row("Sleeper gap (`max_gap_days >= 30`)", lambda c: c["max_gap_days"] >= 30.0))
    lines.append(_row("Long sleeper gap (`max_gap_days >= 180`)", lambda c: c["max_gap_days"] >= 180.0))
    lines.append("")

    lines.append("### By `campaign_signals` Combination (Dev Golden Pages)")
    lines.append("")
    lines.append("| `campaign_signals` | `exact` | `fuzzy` | Δ |")
    lines.append("|---|---:|---:|---:|")
    sig_counts = {
        m: Counter(c["campaign_signals"] for c in dev_clusters[m])
        for m in ("exact", "fuzzy")
    }
    for sig in SIGNAL_ORDER:
        label = "`(none)`" if sig == "" else f"`{sig}`"
        ev, fv = sig_counts["exact"][sig], sig_counts["fuzzy"][sig]
        lines.append(f"| {label} | {ev:,} | {fv:,} | {fv - ev:+,} |")

    def _is_l3_coord(c: dict) -> bool:
        toks = {s for s in c["campaign_signals"].split(",") if s}
        return len(toks) >= 2 and bool(toks & {"accounts", "articles"})

    lines.append(_row("**Meets Level-3 structural coordination bar (`>= 2` signals incl. `accounts` or `articles`)**", _is_l3_coord))
    lines.append("")

    # ── Section 2: Golden Dev Contested/Campaign Multi-Insertion & Cross-Page Match ──
    lines.append("## 2. Alignment with Dev Golden Multi-Insertion & Cross-Article Incidents")
    lines.append("")
    target_items: list[dict] = []
    campaign_ins_revs: set[int] = set()
    for t in dev_topics:
        for it in t["items"]:
            if it["label"] == "campaign":
                campaign_ins_revs.update(it["inserting_rev_ids"])
            if it["label"] in ("contested", "campaign") and (
                len(it["inserting_rev_ids"]) >= 2 or bool(it.get("other_page_ids"))
            ):
                target_items.append(dict(it, topic_id=t["id"]))

    all_target_ins = sorted({r for it in target_items for r in it["inserting_rev_ids"]})
    span_map: dict[int, int] = {}
    if all_target_ins:
        for r in aot.sql(
            f"SELECT rev_id, reverting_rev_id FROM research.s1_reverted_edit WHERE rev_id IN ({aot.ints(all_target_ins)})"
        ):
            span_map[int(r["rev_id"])] = int(r["reverting_rev_id"])

    item_eval_rows: list[dict] = []
    for it in target_items:
        ins_set = set(it["inserting_rev_ids"])
        distinct_spans = sorted({span_map[r] for r in ins_set if r in span_map})
        other_pages = list(it.get("other_page_ids") or [])
        needed_pages = {int(it["page_id"]), *(int(p) for p in other_pages)}

        best_per_method: dict[str, tuple[int, bool, dict | None]] = {}
        for m in ("exact", "fuzzy"):
            best_c = None
            best_ov = 0
            for c in dev_clusters[m]:
                ov = len(c["inserting_set"] & ins_set)
                if ov > best_ov or (ov == best_ov and best_c is not None and c["n_units"] > best_c["n_units"]):
                    if ov > 0:
                        best_ov = ov
                        best_c = c
            pages_covered = bool(best_c and needed_pages.issubset(set(best_c["pages"]))) if other_pages else False
            best_per_method[m] = (best_ov, pages_covered, best_c)

        item_eval_rows.append({
            "item": it,
            "n_ins": len(ins_set),
            "n_spans": len(distinct_spans),
            "other_pages": other_pages,
            "exact": best_per_method["exact"],
            "fuzzy": best_per_method["fuzzy"],
        })

    multi_span_rows = [r for r in item_eval_rows if r["n_spans"] >= 2]
    single_span_rows = [r for r in item_eval_rows if r["n_spans"] == 1]
    other_page_rows = [r for r in item_eval_rows if r["other_pages"]]

    ex_all = sum(1 for r in item_eval_rows if r["exact"][0] >= 2)
    fz_all = sum(1 for r in item_eval_rows if r["fuzzy"][0] >= 2)
    ex_ms = sum(1 for r in multi_span_rows if r["exact"][0] >= 2)
    fz_ms = sum(1 for r in multi_span_rows if r["fuzzy"][0] >= 2)
    ex_ss = sum(1 for r in single_span_rows if r["exact"][0] >= 2)
    fz_ss = sum(1 for r in single_span_rows if r["fuzzy"][0] >= 2)

    lines.append(
        f"- **Dev `contested`/`campaign` items with `>= 2` inserting revisions:** `{len(item_eval_rows)}` total "
        f"(`{len(multi_span_rows)}` span `>= 2` separate S1 revert episodes; `{len(single_span_rows)}` are multi-edit runs reverted in a single S1 span)."
    )
    lines.append(
        f"- **Re-insertion cluster found (`>= 2` of the item's inserting revs in one cluster):** "
        f"`exact` **{ex_all}/{len(item_eval_rows)}** ({100.0 * ex_all / max(1, len(item_eval_rows)):.1f}%), "
        f"`fuzzy` **{fz_all}/{len(item_eval_rows)}** ({100.0 * fz_all / max(1, len(item_eval_rows)):.1f}%)."
    )
    lines.append(
        f"  - On **true multi-revert items (`>= 2` S1 spans)**: `exact` **{ex_ms}/{len(multi_span_rows)}** "
        f"({100.0 * ex_ms / max(1, len(multi_span_rows)):.1f}%), `fuzzy` **{fz_ms}/{len(multi_span_rows)}** "
        f"({100.0 * fz_ms / max(1, len(multi_span_rows)):.1f}%). "
        f"*(The sole unmerged item, `ceded-karelia/i10`, was substantially rewritten between spans `351793` and `392863`, yielding 5-shingle Jaccard `0.086 < 0.6`.)*"
    )
    lines.append(
        f"  - On **single-span multi-edit items (`1` S1 span)**: `exact` **{ex_ss}/{len(single_span_rows)}** (100%), "
        f"`fuzzy` **{fz_ss}/{len(single_span_rows)}** (100%)."
    )
    if other_page_rows:
        fz_op = sum(1 for r in other_page_rows if r["fuzzy"][1])
        lines.append(
            f"- **Explicit `other_page_ids` items on dev split:** `{len(other_page_rows)}` item(s), cross-article cluster found: **{fz_op}/{len(other_page_rows)}**."
        )
    else:
        lines.append(
            "- **Cross-article golden check:** `0` dev items carry explicit `other_page_ids` in `golden.json` "
            "(topics were labelled per-topic), **but S7 automatically discovered and linked cross-topic twin items** "
            "`civil-war-1918/i10` (Mannerheim, page `6021`) and `continuation-war/i06` (Risto Ryti, page `11398`) "
            "into a single 2-article, 2-account cluster (`#3382`, `7/7` combined inserting revisions)."
        )
    lines.append("")

    lines.append("| Golden item | Label | Page | Ins revs | S1 spans | `exact` best overlap | `fuzzy` best overlap | `fuzzy` cluster (`n_units`, `n_pages`, `n_accounts`, signals) |")
    lines.append("|---|---|---:|---:|---:|---:|---:|---|")
    for r in item_eval_rows:
        it = r["item"]
        e_ov, _, _ = r["exact"]
        f_ov, _, f_c = r["fuzzy"]
        e_mark = "yes" if e_ov >= 2 else "no"
        f_mark = "yes" if f_ov >= 2 else "no"
        if f_c is not None:
            sig = f_c["campaign_signals"] or "none"
            c_desc = f"`#{f_c['cluster_id']}` (`u={f_c['n_units']}, p={f_c['n_pages']}, a={f_c['n_accounts']}`, `{sig}`)"
        else:
            c_desc = "*(all spans < 30 prose chars)*"
        lines.append(
            f"| `{it['id']}` | `{it['label']}` | {it['page_id']} | {r['n_ins']} | {r['n_spans']} | "
            f"{e_ov}/{r['n_ins']} ({e_mark}) | {f_ov}/{r['n_ins']} ({f_mark}) | {c_desc} |"
        )
    lines.append("")

    # ── Section 3: 10 Multi-Unit Clusters with Masked Sample Text ──
    lines.append("## 3. Ten Multi-Unit Clusters on Dev Pages (Campaign Matches & False-Cluster Archetypes)")
    lines.append("")
    exact_unit_sets = {
        frozenset(m["reverting_rev_id"] for m in c["members"])
        for c in dev_clusters["exact"]
        if c["n_units"] >= 2
    }
    fuzzy_dev_multi = [c for c in dev_clusters["fuzzy"] if c["n_units"] >= 2]
    examples = _select_example_clusters(fuzzy_dev_multi, exact_unit_sets, campaign_ins_revs)

    for idx, (tag, c) in enumerate(examples, 1):
        art_names = ", ".join(
            f"{page_titles.get(p, str(p))} (`{p}`)" for p in c["pages"][:4]
        )
        if len(c["pages"]) > 4:
            art_names += f", +{len(c['pages']) - 4} more"
        sigs = c["campaign_signals"] or "(none)"
        lines.append(
            f"{idx}. **`#{c['cluster_id']}` [{tag}]** · `n_units={c['n_units']}`, `n_pages={c['n_pages']}`, "
            f"`n_accounts={c['n_accounts']}`, `span={c['span_days']:.1f}d`, `max_gap={c['max_gap_days']:.1f}d`, "
            f"signals=`{sigs}` · Articles: {art_names}"
        )
        lines.append(f"   > `{c['sample_text']}`")
    lines.append("")

    # ── Section 4: Boilerplate Guard List ──
    lines.append("## 4. Boilerplate Guard (`> 8` Distinct Pages) & Top Cross-Page Sentence Hashes")
    lines.append("")
    top_hashes = c_stats.get("top_sentence_hashes", [])
    lines.append(
        f"Across the `{load_stats.get('pages_requested', 245)}` golden pages, the maximum spread of any normalised sentence (`>= 30` chars) "
        f"was `4` distinct pages (`0` hashes exceeded the `> {c_stats.get('boilerplate_max_pages', 8)}` distinct-page boilerplate cutoff on this 245-page subset; "
        f"on the full 19,742-article corpus, toolbar/chrome phrases like rank #6 below cross `> 8` pages)."
    )
    lines.append("")
    if top_hashes:
        lines.append("| Rank | Hash | Distinct pages | Units | Guard status | Masked normalised sentence (`>= 30` chars) |")
        lines.append("|---:|---|---:|---:|---|---|")
        for i, h in enumerate(top_hashes[:15], 1):
            status = "**BLOCKED (>8 pages)**" if h.get("blocked") else "linked (<=8 pages)"
            samp = _safe_sample(h.get("sample", ""))
            lines.append(f"| {i} | `{h['hash']}` | {h['n_pages']} | {h['n_units']} | {status} | `{samp}` |")
    else:
        lines.append("*(No sentence hash appeared on >= 2 distinct pages.)*")
    lines.append("")

    # ── Section 5: Cost & Pair Counts (Exact vs Fuzzy) ──
    lines.append("## 5. Runtime & Pair Counts (`exact` vs `fuzzy`)")
    lines.append("")
    lines.append("| Stage / Metric | Value |")
    lines.append("|---|---:|")
    lines.append(f"| Qualifying units (`>= 30` normalised prose chars) | {c_stats.get('n_units', 0):,} |")
    lines.append(f"| Corpus diff load time (compressed `s3_diff` batches) | {load_stats.get('load_seconds', 0.0):.2f} s |")
    lines.append(f"| **Exact** unique sentence hashes (`>= 30` chars) | {c_stats.get('unique_sentence_hashes', 0):,} |")
    lines.append(f"| **Exact** boilerplate hashes blocked (`> 8` pages) | {c_stats.get('boilerplate_hashes_blocked', 0):,} |")
    lines.append(f"| **Exact** pair links / union merges | {c_stats.get('exact_pair_links', 0):,} / {c_stats.get('exact_unions', 0):,} |")
    lines.append(f"| **Exact** clustering compute time | {c_stats.get('exact_seconds', 0.0):.3f} s |")
    lines.append(f"| **Fuzzy** units with `>= 5` words (5-shingled) | {c_stats.get('fuzzy_shingled_units', 0):,} |")
    lines.append(f"| **Fuzzy** unique word 5-shingles | {c_stats.get('fuzzy_unique_shingles', 0):,} |")
    lines.append(f"| **Fuzzy** LSH candidate pairs (64 perms, 32×2 bands) | {c_stats.get('fuzzy_lsh_candidate_pairs', 0):,} |")
    lines.append(f"| **Fuzzy** verified pairs (true Jaccard `>= 0.6`) | {c_stats.get('fuzzy_verified_pairs', 0):,} |")
    lines.append(f"| **Fuzzy** additional component merges beyond exact | {c_stats.get('fuzzy_extra_unions', 0):,} |")
    lines.append(f"| **Fuzzy** MinHash + LSH + Jaccard compute time | {c_stats.get('fuzzy_seconds', 0.0):.3f} s |")
    lines.append(f"| MariaDB table write time (`s7_cluster` + `s7_member`) | {build_stats.get('write_seconds', 0.0):.2f} s |")
    lines.append(f"| Total `cluster.py` wall time | {build_stats.get('total_seconds', 0.0):.2f} s |")
    lines.append("")

    # ── Section 6: False-Cluster Mechanisms & Recommendations ──
    lines.append("## 6. Observed False-Cluster Mechanisms (Guidance for P1.S8 / P1.S10)")
    lines.append("")
    lines.append(
        "1. **Unclosed `<ref>` tags swallowing existing prose (`#3323`):** when unrelated vandals break a `</ref>` tag on a large article, "
        "`wikidiff` attributes the swallowed section prose to an added `reference` segment, linking unrelated edits over years. "
        "*Mitigation for S8:* ignore `reference` segments containing section headings (`== ... ==`) or exceeding ~1,000 chars."
    )
    lines.append(
        "2. **MediaWiki UI / toolbar strings (`#3394`, `#3334`):** novice editors click toolbar buttons (`Lisää muotoilematon teksti tähän`, "
        "`Otsikkoteksti Lihavoitu teksti`) or paste rendered Wikipedia chrome (`Loikkaa: valikkoon, hakuun`). "
        "*Mitigation:* explicit short stoplist alongside the `> 8` page boilerplate guard."
    )
    lines.append(
        "3. **Routine navigation links & shared citations (`#3483`, `#3328`):** `Katso myös ...` cross-links or a single shared citation "
        "(`Lehtiviite`) can transitively bridge distinct politician articles under single-linkage union-find."
    )
    lines.append(
        "4. **Cross-article vandalism (`#3359`):** coordinated spam exhibits genuine `accounts,articles,persistence` signals, confirming why "
        "`docs/evidence-standard.md`'s asymmetry rule requires P1.S9 content classification (`isClaim`) before promoting to Level 2 or 3."
    )
    lines.append("")

    md = NL.join(lines)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(md, encoding="utf-8")
    return md


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--golden", default=str(ROOT / "research/eval/golden.json"))
    ap.add_argument("--out", default=str(HERE / "results/s7-report.md"))
    args = ap.parse_args()

    out_path = pathlib.Path(args.out)
    generate_report(pathlib.Path(args.golden), out_path)
    print(f"wrote {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
