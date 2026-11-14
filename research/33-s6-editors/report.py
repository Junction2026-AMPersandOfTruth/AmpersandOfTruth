#!/usr/bin/env python3
"""P1.S6 (#33): Generate masked aggregate report metrics from `research.s6_*` and `s1_*`.

Writes `research/33-s6-editors/results/report_data.json` backing `README.md`.
Privacy rules enforced:
  - Never writes a raw username, unmasked IP/prefix, or raw edit summary to disk.
  - IP prefixes use `mask_ip_prefix` (`aot.user_display` without suffix -> `85.76.x.x` / `2001:14BB:…`).
  - Named accounts appear only in aggregate rows; individual coactivity examples are restricted to
    anonymous/temporary pairs formatted via `aot.user_display`.

Usage:
    python3 research/33-s6-editors/report.py
"""
from __future__ import annotations

import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
sys.path.insert(0, str(HERE))
import aot  # noqa: E402
from features import features_for, mask_ip_prefix  # noqa: E402

FEATURES = [
    "edits",
    "active_days",
    "lifespan_days",
    "distinct_pages",
    "top_page_share",
    "top_category_share",
    "reverted_ratio",
    "reverts_of_others_ratio",
    "summary_usage",
    "custom_summary_usage",
    "median_abs_bytes_delta",
]


def _num(v):
    if v is None:
        return None
    f = float(v)
    return int(f) if f.is_integer() and "." not in str(v) else round(f, 4)


def collect_kind_summary() -> list[dict]:
    rows = aot.sql("""
        SET SESSION max_statement_time = 55;
        WITH grp AS (
          SELECT CASE
                   WHEN kind = 'named' AND is_bot = 1 THEN 'named_bot'
                   WHEN kind = 'named' AND is_bot = 0 THEN 'named_human'
                   ELSE kind
                 END AS kind_group,
                 edits,
                 single_purpose,
                 throwaway,
                 coactivity_partners
          FROM research.s6_editor_profile
        )
        SELECT kind_group,
               COUNT(*) AS profiles,
               SUM(edits) AS edits,
               ROUND(100.0 * SUM(edits) / (SELECT SUM(edits) FROM research.s6_editor_profile), 2) AS pct_corpus_edits,
               SUM(single_purpose) AS single_purpose_n,
               ROUND(100.0 * SUM(single_purpose) / COUNT(*), 2) AS single_purpose_pct,
               SUM(IF(single_purpose = 1, edits, 0)) AS single_purpose_edits,
               SUM(throwaway) AS throwaway_n,
               ROUND(100.0 * SUM(throwaway) / COUNT(*), 2) AS throwaway_pct,
               SUM(IF(throwaway = 1, edits, 0)) AS throwaway_edits,
               SUM(IF(throwaway = 1 AND edits >= 2, 1, 0)) AS throwaway_ge2_n,
               SUM(IF(single_purpose = 1 AND throwaway = 1, 1, 0)) AS both_sp_and_tw_n,
               SUM(coactivity_partners > 0) AS in_coactivity_n
        FROM grp
        GROUP BY kind_group
        ORDER BY FIELD(kind_group, 'named_human', 'named_bot', 'ipv4', 'ipv6', 'temporary', 'hidden')
    """)
    return [{k: (v if k == "kind_group" else _num(v)) for k, v in r.items()} for r in rows]


def collect_feature_distributions() -> dict[str, list[dict]]:
    out: dict[str, list[dict]] = {}
    for feat in FEATURES:
        where_clause = f"WHERE {feat} IS NOT NULL" if feat == "top_category_share" else ""
        rows = aot.sql(f"""
            SET SESSION max_statement_time = 55;
            WITH base AS (
              SELECT CASE
                       WHEN kind = 'named' AND is_bot = 1 THEN 'named_bot'
                       WHEN kind = 'named' AND is_bot = 0 THEN 'named_human'
                       ELSE kind
                     END AS kind_group,
                     {feat} AS val
              FROM research.s6_editor_profile
              {where_clause}
            ),
            pcts AS (
              SELECT kind_group,
                     val,
                     PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY val) OVER (PARTITION BY kind_group) AS p25,
                     PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY val) OVER (PARTITION BY kind_group) AS p50,
                     PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY val) OVER (PARTITION BY kind_group) AS p75,
                     PERCENTILE_CONT(0.90) WITHIN GROUP (ORDER BY val) OVER (PARTITION BY kind_group) AS p90
              FROM base
            )
            SELECT kind_group,
                   COUNT(*) AS n,
                   ROUND(AVG(val), 4) AS mean,
                   ROUND(MAX(p25), 4) AS p25,
                   ROUND(MAX(p50), 4) AS p50,
                   ROUND(MAX(p75), 4) AS p75,
                   ROUND(MAX(p90), 4) AS p90,
                   ROUND(MAX(val), 4) AS max
            FROM pcts
            GROUP BY kind_group
            ORDER BY FIELD(kind_group, 'named_human', 'named_bot', 'ipv4', 'ipv6', 'temporary', 'hidden')
        """)
        out[feat] = [{k: (v if k == "kind_group" else _num(v)) for k, v in r.items()} for r in rows]
    return out


def collect_s1_class_mix() -> list[dict]:
    """S1 revert-class mix of corpus edits by single-purpose vs non-single-purpose (and throwaway).
    Excludes bot accounts and hidden edits so bot maintenance does not skew 'others'."""
    rows = aot.sql("""
        SET SESSION max_statement_time = 55;
        WITH edit_cls AS (
          SELECT ep.single_purpose,
                 ep.throwaway,
                 ep.kind,
                 ep.edits AS author_edits,
                 COALESCE(rv.class, 'never_reverted') AS s1_class
          FROM research.s6_rev_author ra
          JOIN research.s6_editor_profile ep ON ep.user = ra.user
          LEFT JOIN research.s1_reverted_edit re ON re.rev_id = ra.rev_id
          LEFT JOIN research.s1_revert rv ON rv.reverting_rev_id = re.reverting_rev_id
          WHERE ep.is_bot = 0 AND ep.kind <> 'hidden'
        ),
        cohorts AS (
          SELECT 'single_purpose = 1 (all non-bot)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE single_purpose = 1 GROUP BY s1_class
          UNION ALL
          SELECT 'single_purpose = 0 (all non-bot)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE single_purpose = 0 GROUP BY s1_class
          UNION ALL
          SELECT 'single_purpose = 0 (edits >= 5, non-bot)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE single_purpose = 0 AND author_edits >= 5 GROUP BY s1_class
          UNION ALL
          SELECT 'single_purpose = 1 (named human only)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE single_purpose = 1 AND kind = 'named' GROUP BY s1_class
          UNION ALL
          SELECT 'single_purpose = 0 (named human, edits >= 5)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE single_purpose = 0 AND kind = 'named' AND author_edits >= 5 GROUP BY s1_class
          UNION ALL
          SELECT 'throwaway = 1 (all non-bot)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE throwaway = 1 GROUP BY s1_class
          UNION ALL
          SELECT 'throwaway = 0 (all non-bot)' AS cohort, s1_class, COUNT(*) AS n
          FROM edit_cls WHERE throwaway = 0 GROUP BY s1_class
        )
        SELECT cohort,
               SUM(n) AS total_edits,
               SUM(IF(s1_class = 'never_reverted', n, 0)) AS never_reverted,
               ROUND(100.0 * SUM(IF(s1_class = 'never_reverted', n, 0)) / SUM(n), 2) AS never_reverted_pct,
               SUM(IF(s1_class = 'edit_war', n, 0)) AS edit_war,
               ROUND(100.0 * SUM(IF(s1_class = 'edit_war', n, 0)) / SUM(n), 2) AS edit_war_pct,
               SUM(IF(s1_class = 'dispute', n, 0)) AS dispute,
               ROUND(100.0 * SUM(IF(s1_class = 'dispute', n, 0)) / SUM(n), 2) AS dispute_pct,
               SUM(IF(s1_class = 'graffiti_cleanup', n, 0)) AS graffiti_cleanup,
               ROUND(100.0 * SUM(IF(s1_class = 'graffiti_cleanup', n, 0)) / SUM(n), 2) AS graffiti_cleanup_pct,
               SUM(IF(s1_class = 'other', n, 0)) AS other_rev,
               ROUND(100.0 * SUM(IF(s1_class = 'other', n, 0)) / SUM(n), 2) AS other_pct,
               SUM(IF(s1_class = 'self_revert', n, 0)) AS self_revert,
               ROUND(100.0 * SUM(IF(s1_class = 'self_revert', n, 0)) / SUM(n), 2) AS self_revert_pct,
               SUM(IF(s1_class = 'bot', n, 0)) AS bot_rev,
               ROUND(100.0 * SUM(IF(s1_class = 'bot', n, 0)) / SUM(n), 2) AS bot_pct
        FROM cohorts
        GROUP BY cohort
    """)
    order = [
        "single_purpose = 1 (all non-bot)",
        "single_purpose = 0 (all non-bot)",
        "single_purpose = 0 (edits >= 5, non-bot)",
        "single_purpose = 1 (named human only)",
        "single_purpose = 0 (named human, edits >= 5)",
        "throwaway = 1 (all non-bot)",
        "throwaway = 0 (all non-bot)",
    ]
    by_c = {r["cohort"]: {k: (v if k == "cohort" else _num(v)) for k, v in r.items()} for r in rows}
    return [by_c[c] for c in order if c in by_c]


def collect_cohort_comparison() -> dict[str, list[dict]]:
    """Compare authors of reverted edits in `dispute`/`edit_war` vs `graffiti_cleanup`
    vs never-reverted edits on the exact same pages (excluding bots, hidden users, and
    revert revisions themselves from never-reverted so patrol rollbacks don't skew 'never-reverted')."""
    base_cte = """
        WITH target_pages AS (
          SELECT DISTINCT re.page_id
          FROM research.s1_reverted_edit re
          JOIN research.s1_revert rv USING (reverting_rev_id)
          WHERE rv.class IN ('dispute', 'edit_war', 'graffiti_cleanup')
        ),
        page_edits AS (
          SELECT ra.rev_id,
                 ra.page_id,
                 ra.user,
                 CASE
                   WHEN rv.class IN ('dispute', 'edit_war') THEN 'dispute_or_edit_war'
                   WHEN rv.class = 'graffiti_cleanup' THEN 'graffiti_cleanup'
                   WHEN re.rev_id IS NULL AND r_self.reverting_rev_id IS NULL THEN 'never_reverted'
                   ELSE NULL
                 END AS bucket,
                 IF(rv.class IN ('dispute', 'edit_war', 'graffiti_cleanup', 'other'), 1, 0) AS this_rev_by_others
          FROM research.s6_rev_author ra
          JOIN target_pages tp ON tp.page_id = ra.page_id
          LEFT JOIN research.s1_reverted_edit re ON re.rev_id = ra.rev_id
          LEFT JOIN research.s1_revert rv ON rv.reverting_rev_id = re.reverting_rev_id
          LEFT JOIN research.s1_revert r_self ON r_self.reverting_rev_id = ra.rev_id
        )
    """

    all_rows = aot.sql(f"""
        SET SESSION max_statement_time = 55;
        {base_cte}
        SELECT pe.bucket,
               COUNT(*) AS edits_n,
               COUNT(DISTINCT pe.user) AS distinct_authors,
               ROUND(100.0 * AVG(ep.kind = 'named'), 2) AS named_pct,
               ROUND(100.0 * AVG(ep.kind IN ('ipv4', 'ipv6')), 2) AS ip_pct,
               ROUND(100.0 * AVG(ep.kind = 'temporary'), 2) AS temporary_pct,
               ROUND(AVG(ep.edits), 2) AS mean_edits,
               ROUND(AVG(LN(1 + ep.edits)), 3) AS mean_log1p_edits,
               ROUND(AVG(ep.active_days), 2) AS mean_active_days,
               ROUND(AVG(ep.lifespan_days), 2) AS mean_lifespan_days,
               ROUND(AVG(ep.distinct_pages), 2) AS mean_distinct_pages,
               ROUND(AVG(ep.top_page_share), 4) AS mean_top_page_share,
               ROUND(AVG(ep.top_category_share), 4) AS mean_top_category_share,
               ROUND(AVG(ep.reverted_ratio), 4) AS mean_reverted_ratio,
               ROUND(AVG(CASE WHEN ep.edits >= 2
                              THEN (ep.reverted_edits_by_others - pe.this_rev_by_others) / (ep.edits - 1)
                         END), 4) AS mean_loo_reverted_ratio_ge2,
               ROUND(AVG(ep.reverts_of_others_ratio), 4) AS mean_reverts_of_others_ratio,
               ROUND(AVG(ep.summary_usage), 4) AS mean_summary_usage,
               ROUND(AVG(ep.custom_summary_usage), 4) AS mean_custom_summary_usage,
               ROUND(AVG(ep.median_abs_bytes_delta), 2) AS mean_median_abs_bytes_delta,
               ROUND(100.0 * AVG(ep.single_purpose), 2) AS single_purpose_pct,
               ROUND(100.0 * AVG(ep.throwaway), 2) AS throwaway_pct,
               ROUND(100.0 * AVG(CASE WHEN ep.edits >= 2 THEN ep.throwaway END), 2) AS throwaway_pct_ge2,
               ROUND(100.0 * AVG(ep.coactivity_partners > 0), 2) AS coactivity_any_pct,
               ROUND(100.0 * AVG(ep.coactivity_reverted_partners > 0), 2) AS coactivity_reverted_pct,
               ROUND(100.0 * AVG(CASE WHEN pn.ip_prefix IS NOT NULL THEN pn.pages_with_reverted_edits >= 3 END), 2) AS ip_pfx_rev_pages_ge3_pct
        FROM page_edits pe
        JOIN research.s6_editor_profile ep ON ep.user = pe.user
        LEFT JOIN research.s6_ip_prefix pn ON pn.ip_prefix = ep.ip_prefix
        WHERE pe.bucket IS NOT NULL
          AND ep.is_bot = 0
          AND ep.kind <> 'hidden'
        GROUP BY pe.bucket
        ORDER BY FIELD(pe.bucket, 'dispute_or_edit_war', 'graffiti_cleanup', 'never_reverted')
    """)

    named_rows = aot.sql(f"""
        SET SESSION max_statement_time = 55;
        {base_cte}
        SELECT pe.bucket,
               COUNT(*) AS edits_n,
               COUNT(DISTINCT pe.user) AS distinct_authors,
               ROUND(AVG(ep.edits), 2) AS mean_edits,
               ROUND(AVG(LN(1 + ep.edits)), 3) AS mean_log1p_edits,
               ROUND(AVG(ep.active_days), 2) AS mean_active_days,
               ROUND(AVG(ep.lifespan_days), 2) AS mean_lifespan_days,
               ROUND(AVG(ep.distinct_pages), 2) AS mean_distinct_pages,
               ROUND(AVG(ep.top_page_share), 4) AS mean_top_page_share,
               ROUND(AVG(ep.top_category_share), 4) AS mean_top_category_share,
               ROUND(AVG(ep.reverted_ratio), 4) AS mean_reverted_ratio,
               ROUND(AVG(CASE WHEN ep.edits >= 2
                              THEN (ep.reverted_edits_by_others - pe.this_rev_by_others) / (ep.edits - 1)
                         END), 4) AS mean_loo_reverted_ratio_ge2,
               ROUND(AVG(ep.reverts_of_others_ratio), 4) AS mean_reverts_of_others_ratio,
               ROUND(AVG(ep.summary_usage), 4) AS mean_summary_usage,
               ROUND(AVG(ep.custom_summary_usage), 4) AS mean_custom_summary_usage,
               ROUND(AVG(ep.median_abs_bytes_delta), 2) AS mean_median_abs_bytes_delta,
               ROUND(100.0 * AVG(ep.single_purpose), 2) AS single_purpose_pct,
               ROUND(100.0 * AVG(ep.throwaway), 2) AS throwaway_pct,
               ROUND(100.0 * AVG(CASE WHEN ep.edits >= 2 THEN ep.throwaway END), 2) AS throwaway_pct_ge2,
               ROUND(100.0 * AVG(ep.coactivity_partners > 0), 2) AS coactivity_any_pct,
               ROUND(100.0 * AVG(ep.coactivity_reverted_partners > 0), 2) AS coactivity_reverted_pct
        FROM page_edits pe
        JOIN research.s6_editor_profile ep ON ep.user = pe.user
        WHERE pe.bucket IN ('dispute_or_edit_war', 'never_reverted')
          AND ep.is_bot = 0
          AND ep.kind = 'named'
        GROUP BY pe.bucket
        ORDER BY FIELD(pe.bucket, 'dispute_or_edit_war', 'never_reverted')
    """)

    anon_rows = aot.sql(f"""
        SET SESSION max_statement_time = 55;
        {base_cte}
        SELECT pe.bucket,
               COUNT(*) AS edits_n,
               COUNT(DISTINCT pe.user) AS distinct_authors,
               ROUND(AVG(ep.edits), 2) AS mean_edits,
               ROUND(AVG(ep.lifespan_days), 2) AS mean_lifespan_days,
               ROUND(AVG(ep.distinct_pages), 2) AS mean_distinct_pages,
               ROUND(AVG(ep.top_page_share), 4) AS mean_top_page_share,
               ROUND(AVG(CASE WHEN ep.edits >= 2
                              THEN (ep.reverted_edits_by_others - pe.this_rev_by_others) / (ep.edits - 1)
                         END), 4) AS mean_loo_reverted_ratio_ge2,
               ROUND(AVG(ep.summary_usage), 4) AS mean_summary_usage,
               ROUND(AVG(ep.custom_summary_usage), 4) AS mean_custom_summary_usage,
               ROUND(AVG(ep.median_abs_bytes_delta), 2) AS mean_median_abs_bytes_delta,
               ROUND(100.0 * AVG(ep.single_purpose), 2) AS single_purpose_pct,
               ROUND(100.0 * AVG(CASE WHEN pn.ip_prefix IS NOT NULL THEN pn.pages_with_reverted_edits >= 3 END), 2) AS ip_pfx_rev_pages_ge3_pct,
               ROUND(AVG(pn.reverted_edits), 2) AS mean_ip_pfx_reverted_edits
        FROM page_edits pe
        JOIN research.s6_editor_profile ep ON ep.user = pe.user
        LEFT JOIN research.s6_ip_prefix pn ON pn.ip_prefix = ep.ip_prefix
        WHERE pe.bucket IN ('dispute_or_edit_war', 'graffiti_cleanup', 'never_reverted')
          AND ep.kind IN ('ipv4', 'ipv6', 'temporary')
        GROUP BY pe.bucket
        ORDER BY FIELD(pe.bucket, 'dispute_or_edit_war', 'graffiti_cleanup', 'never_reverted')
    """)

    return {
        "all_non_bot": [{k: (v if k == "bucket" else _num(v)) for k, v in r.items()} for r in all_rows],
        "named_human_only": [{k: (v if k == "bucket" else _num(v)) for k, v in r.items()} for r in named_rows],
        "anonymous_and_temp_only": [{k: (v if k == "bucket" else _num(v)) for k, v in r.items()} for r in anon_rows],
    }


def collect_ip_prefix_metrics() -> dict:
    summary = aot.sql("""
        SET SESSION max_statement_time = 55;
        SELECT prefix_kind,
               COUNT(*) AS total_prefixes,
               SUM(editors >= 2) AS multi_editor_prefixes,
               SUM(reverted_edits > 0) AS prefixes_with_reverts,
               SUM(pages_with_reverted_edits >= 3) AS prefixes_rev_ge3_pages,
               SUM(pages_with_reverted_edits >= 3 AND editors >= 2) AS multi_ed_rev_ge3_pages,
               SUM(edits) AS total_edits,
               SUM(reverted_edits) AS total_reverted_edits
        FROM research.s6_ip_prefix
        GROUP BY prefix_kind
        ORDER BY FIELD(prefix_kind, 'ipv4_24', 'ipv4_16', 'ipv6_64')
    """)

    def _top20(pkind: str) -> list[dict]:
        rows = aot.sql(f"""
            SELECT ip_prefix,
                   editors,
                   edits,
                   reverted_edits,
                   ROUND(100.0 * reverted_ratio, 1) AS reverted_pct,
                   distinct_pages,
                   pages_with_reverted_edits,
                   dispute_war_reverted_edits
            FROM research.s6_ip_prefix
            WHERE prefix_kind = {aot.quote(pkind)}
              AND pages_with_reverted_edits >= 3
            ORDER BY reverted_edits DESC, pages_with_reverted_edits DESC, ip_prefix ASC
            LIMIT 20
        """)
        out = []
        for rank, r in enumerate(rows, 1):
            out.append({
                "rank": rank,
                "masked_prefix": mask_ip_prefix(r["ip_prefix"]),
                "editors": int(r["editors"]),
                "edits": int(r["edits"]),
                "reverted_edits": int(r["reverted_edits"]),
                "reverted_pct": float(r["reverted_pct"]),
                "distinct_pages": int(r["distinct_pages"]),
                "pages_with_reverted_edits": int(r["pages_with_reverted_edits"]),
                "dispute_war_reverted_edits": int(r["dispute_war_reverted_edits"]),
            })
        return out

    return {
        "summary": [{k: (v if k == "prefix_kind" else _num(v)) for k, v in r.items()} for r in summary],
        "top20_ipv4_24": _top20("ipv4_24"),
        "top20_ipv6_64": _top20("ipv6_64"),
    }


def collect_coactivity_metrics() -> dict:
    summary = aot.sql("""
        SET SESSION max_statement_time = 55;
        SELECT LEAST(c.kind_a, c.kind_b) AS kind_1,
               GREATEST(c.kind_a, c.kind_b) AS kind_2,
               COUNT(*) AS pairs,
               SUM(c.same_side_reverted > 0) AS pairs_with_same_side_reverted,
               SUM(c.same_side_reverting > 0) AS pairs_with_same_side_reverting,
               SUM(c.same_side_reverted > 0 AND c.same_side_reverting > 0) AS pairs_with_both,
               SUM(ea.reverts_made_others >= 20 AND eb.reverts_made_others >= 20) AS both_patrollers_ge20_reverts,
               ROUND(AVG(c.pages), 2) AS mean_pages,
               ROUND(AVG(c.episodes), 2) AS mean_episodes,
               MAX(c.episodes) AS max_episodes
        FROM research.s6_coactivity c
        JOIN research.s6_editor_profile ea ON ea.user = c.user_a
        JOIN research.s6_editor_profile eb ON eb.user = c.user_b
        GROUP BY kind_1, kind_2
        ORDER BY pairs DESC
    """)

    totals_row = aot.sql("""
        SELECT
          (SELECT COUNT(*) FROM research.s6_coactivity) AS total_pairs,
          (SELECT COUNT(DISTINCT u) FROM (
             SELECT user_a AS u FROM research.s6_coactivity
             UNION
             SELECT user_b AS u FROM research.s6_coactivity
           ) x) AS distinct_accounts,
          (SELECT SUM(same_side_reverted > 0) FROM research.s6_coactivity) AS pairs_with_reverted,
          (SELECT SUM(same_side_reverting > 0) FROM research.s6_coactivity) AS pairs_with_reverting,
          (SELECT SUM(same_side_reverted > 0 AND same_side_reverting > 0) FROM research.s6_coactivity) AS pairs_with_both,
          (SELECT SUM(same_side_reverted >= 3) FROM research.s6_coactivity) AS pairs_reverted_ge3,
          (SELECT SUM(same_side_reverting >= 3) FROM research.s6_coactivity) AS pairs_reverting_ge3
    """)[0]

    # 5 masked examples: strictly anonymous/temporary pairs formatted via aot.user_display
    # (zero named accounts in individual example rows, per spec privacy rule).
    anon_examples = aot.sql("""
        SELECT user_a,
               user_b,
               kind_a,
               kind_b,
               pages,
               episodes,
               same_side_reverted,
               same_side_reverting,
               DATE(first_episode_ts) AS first_date,
               DATE(last_episode_ts) AS last_date
        FROM research.s6_coactivity
        WHERE kind_a IN ('ipv4', 'ipv6', 'temporary')
          AND kind_b IN ('ipv4', 'ipv6', 'temporary')
        ORDER BY episodes DESC, same_side_reverted DESC, pages DESC, user_a ASC
        LIMIT 5
    """)
    masked_examples = []
    for idx, r in enumerate(anon_examples, 1):
        masked_examples.append({
            "example": idx,
            "user_a_display": aot.user_display(r["user_a"]),
            "user_b_display": aot.user_display(r["user_b"]),
            "same_masked_range": aot.user_display(r["user_a"]) == aot.user_display(r["user_b"]),
            "kinds": f"{r['kind_a']}+{r['kind_b']}",
            "pages": int(r["pages"]),
            "episodes": int(r["episodes"]),
            "same_side_reverted": int(r["same_side_reverted"]),
            "same_side_reverting": int(r["same_side_reverting"]),
            "window": f"{r['first_date']} .. {r['last_date']}",
        })

    return {
        "totals": {k: int(v) for k, v in totals_row.items()},
        "by_kind_pair": [{k: (v if k in ("kind_1", "kind_2") else _num(v)) for k, v in r.items()} for r in summary],
        "masked_examples_anonymous_pairs": masked_examples,
    }


def benchmark_features_500() -> dict:
    sample_rows = aot.sql("""
        SELECT rev_id
        FROM research.s6_rev_author
        ORDER BY MOD(rev_id * 2654435761, 4294967296)
        LIMIT 500
    """)
    rev_ids = [int(r["rev_id"]) for r in sample_rows]
    t0 = time.monotonic()
    res = features_for(rev_ids)
    elapsed = round(time.monotonic() - t0, 4)
    assert len(res) == 500, f"Expected 500 results, got {len(res)}"
    assert elapsed < 2.0, f"features_for(500) exceeded 2.0 s: {elapsed}"
    return {"n_rev_ids": 500, "returned": len(res), "seconds": elapsed}


def main() -> None:
    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)

    t0 = time.monotonic()
    data: dict = {}

    timings_file = out_dir / "build_timings.json"
    if timings_file.exists():
        data["build_timings"] = json.loads(timings_file.read_text(encoding="utf-8"))

    aot.log("[s6 report] collecting kind summary...")
    data["kind_summary"] = collect_kind_summary()

    aot.log("[s6 report] collecting feature distributions by kind...")
    data["feature_distributions"] = collect_feature_distributions()

    aot.log("[s6 report] collecting S1 class mix by single_purpose & throwaway...")
    data["s1_class_mix"] = collect_s1_class_mix()

    aot.log("[s6 report] collecting cohort comparison (dispute/edit_war vs graffiti vs never_reverted)...")
    data["cohort_comparison"] = collect_cohort_comparison()

    aot.log("[s6 report] collecting IP prefix stats & masked top-20 lists...")
    data["ip_prefixes"] = collect_ip_prefix_metrics()

    aot.log("[s6 report] collecting coactivity counts & 5 masked examples...")
    data["coactivity"] = collect_coactivity_metrics()

    aot.log("[s6 report] benchmarking features_for(500 rev_ids)...")
    data["features_benchmark_500"] = benchmark_features_500()

    data["report_elapsed_seconds"] = round(time.monotonic() - t0, 2)
    (out_dir / "report_data.json").write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + chr(10),
        encoding="utf-8",
    )
    aot.log(f"[s6 report] wrote results/report_data.json in {data['report_elapsed_seconds']:.2f} s")


if __name__ == "__main__":
    main()
