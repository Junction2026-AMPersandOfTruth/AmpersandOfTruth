#!/usr/bin/env python3
"""P1.S6 (#33): Precompute per-editor behaviour profiles, IP-prefix aggregates,
24-hour same-side revert coactivity pairs, and an indexed revision-to-author lookup.

Builds on `research.s1_*` (`s1_user_stats`, `s1_revert`, `s1_reverted_edit`) without
recomputing reverts. Every SQL step runs sequentially with `max_statement_time = 55`
(< 60 s budget per step on the shared 2-vCPU corpus VPS) and writes timings + invariant
checks to `research/33-s6-editors/results/build_timings.json`.

Tables created:
  - `research.s6_editor_profile` (PK `user` COLLATE utf8mb4_bin; `''` = hidden editor)
  - `research.s6_ip_prefix`      (PK `ip_prefix`; IPv4 /24, IPv4 /16, IPv6 /64)
  - `research.s6_coactivity`     (PK `(user_a, user_b)`; >= 3 24h episodes on same side)
  - `research.s6_rev_author`     (PK `rev_id`; enables < 2 s lookup for 500 rev_ids)

Usage:
    python3 research/33-s6-editors/build.py
"""
from __future__ import annotations

import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

# Documented thresholds (also recorded in build_timings.json and README.md)
THRESHOLDS = {
    "single_purpose_min_edits": 5,
    "single_purpose_top_page_share": 0.80,
    "single_purpose_top_tight_category_share": 0.80,
    "tight_category_max_pages": 200,
    "throwaway_max_edits": 50,
    "throwaway_max_lifespan_days": 7.0,
    "throwaway_min_reverted_ratio_exclusive": 0.50,
    "coactivity_window_seconds": 86400,
    "coactivity_min_episodes": 3,
}

# Section-header-only comment regex (`/* Section */` auto-filled by MediaWiki when summary box left blank)
SECTION_ONLY_REGEX = r"^(/\*[^*]*\*+([^/*][^*]*\*+)*/[[:space:]]*)+$"


def build_steps() -> list[tuple[str, str]]:
    t = THRESHOLDS
    return [
        (
            "1. Drop previous staging & target tables",
            """
            SET SESSION max_statement_time = 55;
            DROP TABLE IF EXISTS
              research.s6__rev,
              research.s6__user_page,
              research.s6__user_rev_stats,
              research.s6__user_conc,
              research.s6_rev_author,
              research.s6_ip_prefix,
              research.s6_coactivity,
              research.s6_editor_profile;
            """,
        ),
        (
            "2. Build research.s6_coactivity from s1_revert + s1_reverted_edit",
            f"""
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6_coactivity (
              user_a VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              user_b VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              kind_a VARCHAR(16) CHARACTER SET ascii NOT NULL,
              kind_b VARCHAR(16) CHARACTER SET ascii NOT NULL,
              pages INT UNSIGNED NOT NULL,
              episodes INT UNSIGNED NOT NULL,
              same_side_reverted INT UNSIGNED NOT NULL,
              same_side_reverting INT UNSIGNED NOT NULL,
              first_episode_ts DATETIME NOT NULL,
              last_episode_ts DATETIME NOT NULL,
              PRIMARY KEY (user_a, user_b),
              KEY idx_user_b (user_b),
              KEY idx_episodes (episodes DESC)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            WITH u_ok AS (
              SELECT user,
                     CASE
                       WHEN anonymous = 1 AND user LIKE '~%' THEN 'temporary'
                       WHEN anonymous = 1 AND user LIKE '%:%' THEN 'ipv6'
                       WHEN anonymous = 1 THEN 'ipv4'
                       ELSE 'named'
                     END AS kind
              FROM research.s1_user_stats
              WHERE is_bot = 0
            ),
            rev_u AS (
              SELECT re.page_id,
                     re.reverting_rev_id,
                     re.user,
                     MIN(re.ts) AS min_ts,
                     MAX(re.ts) AS max_ts,
                     rv.revert_ts
              FROM research.s1_reverted_edit re
              JOIN research.s1_revert rv USING (reverting_rev_id)
              JOIN u_ok ON u_ok.user = re.user
              WHERE rv.class NOT IN ('excluded', 'bot', 'self_revert')
                AND (rv.reverting_user IS NULL OR re.user <> rv.reverting_user)
              GROUP BY re.page_id, re.reverting_rev_id, re.user, rv.revert_ts
            ),
            ev_reverted AS (
              SELECT a.page_id,
                     a.user AS user_a,
                     b.user AS user_b,
                     a.revert_ts AS ev_ts,
                     1 AS is_reverted,
                     0 AS is_reverting
              FROM rev_u a
              JOIN rev_u b
                ON b.page_id = a.page_id
               AND b.reverting_rev_id = a.reverting_rev_id
               AND a.user < b.user
              WHERE TIMESTAMPDIFF(SECOND, LEAST(a.min_ts, b.min_ts), GREATEST(a.max_ts, b.max_ts)) <= {t['coactivity_window_seconds']}
                 OR ABS(TIMESTAMPDIFF(SECOND, a.min_ts, b.min_ts)) <= {t['coactivity_window_seconds']}
            ),
            rv_target AS (
              SELECT DISTINCT
                     rv.page_id,
                     rv.reverting_rev_id,
                     rv.revert_ts,
                     rv.reverting_user AS rev_user,
                     re.user AS target_user
              FROM research.s1_revert rv
              JOIN research.s1_reverted_edit re USING (reverting_rev_id)
              JOIN u_ok u1 ON u1.user = rv.reverting_user
              JOIN u_ok u2 ON u2.user = re.user
              WHERE rv.class NOT IN ('excluded', 'bot', 'self_revert')
                AND rv.reverting_user <> re.user
            ),
            ev_reverting AS (
              SELECT a.page_id,
                     LEAST(a.rev_user, b.rev_user) AS user_a,
                     GREATEST(a.rev_user, b.rev_user) AS user_b,
                     LEAST(a.revert_ts, b.revert_ts) AS ev_ts,
                     0 AS is_reverted,
                     1 AS is_reverting
              FROM rv_target a
              JOIN rv_target b
                ON b.page_id = a.page_id
               AND b.target_user = a.target_user
               AND a.rev_user < b.rev_user
               AND ABS(TIMESTAMPDIFF(SECOND, a.revert_ts, b.revert_ts)) <= {t['coactivity_window_seconds']}
              GROUP BY a.page_id,
                       LEAST(a.rev_user, b.rev_user),
                       GREATEST(a.rev_user, b.rev_user),
                       LEAST(a.reverting_rev_id, b.reverting_rev_id),
                       GREATEST(a.reverting_rev_id, b.reverting_rev_id),
                       LEAST(a.revert_ts, b.revert_ts)
            ),
            ordered_ev AS (
              SELECT page_id, user_a, user_b, ev_ts, is_reverted, is_reverting,
                     LAG(ev_ts) OVER (PARTITION BY page_id, user_a, user_b ORDER BY ev_ts) AS prev_ts
              FROM (
                SELECT * FROM ev_reverted
                UNION ALL
                SELECT * FROM ev_reverting
              ) all_ev
            ),
            clustered AS (
              SELECT page_id, user_a, user_b, ev_ts, is_reverted, is_reverting,
                     SUM(IF(prev_ts IS NULL OR TIMESTAMPDIFF(SECOND, prev_ts, ev_ts) > {t['coactivity_window_seconds']}, 1, 0))
                       OVER (PARTITION BY page_id, user_a, user_b ORDER BY ev_ts ROWS UNBOUNDED PRECEDING) AS ep_num
              FROM ordered_ev
            ),
            per_ep AS (
              SELECT page_id, user_a, user_b, ep_num,
                     MAX(is_reverted) AS ep_reverted,
                     MAX(is_reverting) AS ep_reverting,
                     MIN(ev_ts) AS ep_start_ts,
                     MAX(ev_ts) AS ep_end_ts
              FROM clustered
              GROUP BY page_id, user_a, user_b, ep_num
            )
            SELECT p.user_a,
                   p.user_b,
                   ua.kind AS kind_a,
                   ub.kind AS kind_b,
                   CAST(COUNT(DISTINCT p.page_id) AS UNSIGNED) AS pages,
                   CAST(COUNT(*) AS UNSIGNED) AS episodes,
                   CAST(SUM(p.ep_reverted) AS UNSIGNED) AS same_side_reverted,
                   CAST(SUM(p.ep_reverting) AS UNSIGNED) AS same_side_reverting,
                   MIN(p.ep_start_ts) AS first_episode_ts,
                   MAX(p.ep_end_ts) AS last_episode_ts
            FROM per_ep p
            JOIN u_ok ua ON ua.user = p.user_a
            JOIN u_ok ub ON ub.user = p.user_b
            GROUP BY p.user_a, p.user_b, ua.kind, ub.kind
            HAVING COUNT(*) >= {t['coactivity_min_episodes']};
            """,
        ),
        (
            "3. Scan corpus.revision once for byte deltas, active days & comment flags",
            f"""
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6__rev (
              rev_id INT UNSIGNED NOT NULL,
              page_id INT UNSIGNED NOT NULL,
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              ts DATETIME NOT NULL,
              day DATE NOT NULL,
              abs_bytes_delta INT UNSIGNED NOT NULL,
              is_non_null TINYINT(1) NOT NULL,
              has_comment TINYINT(1) NOT NULL,
              has_custom_comment TINYINT(1) NOT NULL,
              KEY idx_user_page (user, page_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            WITH ord AS (
              SELECT rev_id,
                     page_id,
                     COALESCE(user, '') COLLATE utf8mb4_bin AS user,
                     ts,
                     bytes,
                     sha1,
                     comment,
                     LAG(bytes) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_bytes,
                     LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_sha1
              FROM corpus.revision
            )
            SELECT rev_id,
                   page_id,
                   user,
                   ts,
                   DATE(ts) AS day,
                   CAST(ABS(CAST(bytes AS SIGNED) - CAST(COALESCE(prev_bytes, 0) AS SIGNED)) AS UNSIGNED) AS abs_bytes_delta,
                   IF(sha1 IS NOT NULL AND prev_sha1 IS NOT NULL AND sha1 = prev_sha1, 0, 1) AS is_non_null,
                   IF(comment IS NOT NULL AND TRIM(comment) <> '', 1, 0) AS has_comment,
                   IF(comment IS NOT NULL AND TRIM(comment) <> ''
                      AND NOT (comment COLLATE utf8mb4_uca1400_as_ci REGEXP {aot.quote(SECTION_ONLY_REGEX)}), 1, 0) AS has_custom_comment
            FROM ord;
            """,
        ),
        (
            "4. Build research.s6_rev_author (PK rev_id for < 2 s feature lookup)",
            """
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6_rev_author (
              rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            SELECT rev_id, page_id, user
            FROM research.s6__rev;
            """,
        ),
        (
            "5. Aggregate user-page counts (s6__user_page)",
            """
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6__user_page (
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              page_id INT UNSIGNED NOT NULL,
              page_edits INT UNSIGNED NOT NULL,
              PRIMARY KEY (user, page_id),
              KEY idx_page_id (page_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            SELECT user,
                   page_id,
                   CAST(COUNT(*) AS UNSIGNED) AS page_edits
            FROM research.s6__rev
            GROUP BY user, page_id;
            """,
        ),
        (
            "6. Aggregate per-user active days, summary usage & median byte delta",
            """
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6__user_rev_stats (
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL PRIMARY KEY,
              first_ts DATETIME NOT NULL,
              last_ts DATETIME NOT NULL,
              edits INT UNSIGNED NOT NULL,
              non_null_edits INT UNSIGNED NOT NULL,
              active_days INT UNSIGNED NOT NULL,
              distinct_pages INT UNSIGNED NOT NULL,
              comment_edits INT UNSIGNED NOT NULL,
              custom_comment_edits INT UNSIGNED NOT NULL,
              median_abs_bytes_delta DOUBLE NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            WITH w AS (
              SELECT user,
                     ts,
                     day,
                     page_id,
                     is_non_null,
                     has_comment,
                     has_custom_comment,
                     MEDIAN(abs_bytes_delta) OVER (PARTITION BY user) AS med_abs_bytes
              FROM research.s6__rev
            )
            SELECT user,
                   MIN(ts) AS first_ts,
                   MAX(ts) AS last_ts,
                   CAST(COUNT(*) AS UNSIGNED) AS edits,
                   CAST(SUM(is_non_null) AS UNSIGNED) AS non_null_edits,
                   CAST(COUNT(DISTINCT day) AS UNSIGNED) AS active_days,
                   CAST(COUNT(DISTINCT page_id) AS UNSIGNED) AS distinct_pages,
                   CAST(SUM(has_comment) AS UNSIGNED) AS comment_edits,
                   CAST(SUM(has_custom_comment) AS UNSIGNED) AS custom_comment_edits,
                   CAST(ROUND(MAX(med_abs_bytes), 2) AS DOUBLE) AS median_abs_bytes_delta
            FROM w
            GROUP BY user;
            """,
        ),
        (
            "7. Compute per-user top page & Finnish category concentration (s6__user_conc)",
            f"""
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6__user_conc (
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL PRIMARY KEY,
              top_page_id INT UNSIGNED NOT NULL,
              top_page_edits INT UNSIGNED NOT NULL,
              distinct_categories INT UNSIGNED NOT NULL,
              top_category VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_as_ci DEFAULT NULL,
              top_category_edits INT UNSIGNED DEFAULT NULL,
              top_tight_category_edits INT UNSIGNED DEFAULT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            WITH tp_ranked AS (
              SELECT user,
                     page_id,
                     page_edits,
                     ROW_NUMBER() OVER (PARTITION BY user ORDER BY page_edits DESC, page_id ASC) AS rn
              FROM research.s6__user_page
            ),
            cat_sizes AS (
              SELECT category, COUNT(*) AS category_pages
              FROM corpus.article_category
              WHERE lang = 'fi'
              GROUP BY category
            ),
            user_cat AS (
              SELECT up.user,
                     ac.category,
                     cs.category_pages,
                     CAST(SUM(up.page_edits) AS UNSIGNED) AS cat_edits
              FROM research.s6__user_page up
              JOIN corpus.article_category ac ON ac.lang = 'fi' AND ac.page_id = up.page_id
              JOIN cat_sizes cs ON cs.category = ac.category
              GROUP BY up.user, ac.category, cs.category_pages
            ),
            uc_ranked AS (
              SELECT user,
                     category,
                     cat_edits,
                     COUNT(*) OVER (PARTITION BY user) AS distinct_categories,
                     MAX(IF(category_pages <= {t['tight_category_max_pages']}, cat_edits, 0))
                       OVER (PARTITION BY user) AS max_tight_cat_edits,
                     ROW_NUMBER() OVER (PARTITION BY user ORDER BY cat_edits DESC, category_pages ASC, category ASC) AS rn
              FROM user_cat
            )
            SELECT tp.user,
                   tp.page_id AS top_page_id,
                   tp.page_edits AS top_page_edits,
                   CAST(COALESCE(uc.distinct_categories, 0) AS UNSIGNED) AS distinct_categories,
                   uc.category AS top_category,
                   uc.cat_edits AS top_category_edits,
                   IF(uc.max_tight_cat_edits > 0, uc.max_tight_cat_edits, NULL) AS top_tight_category_edits
            FROM tp_ranked tp
            LEFT JOIN uc_ranked uc ON uc.user = tp.user AND uc.rn = 1
            WHERE tp.rn = 1;
            """,
        ),
        (
            "8. Build research.s6_editor_profile (reusing s1_user_stats + s6 aggregates)",
            f"""
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6_editor_profile (
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL PRIMARY KEY,
              kind VARCHAR(16) CHARACTER SET ascii NOT NULL,
              is_bot TINYINT(1) NOT NULL,
              first_ts DATETIME NOT NULL,
              last_ts DATETIME NOT NULL,
              lifespan_days DOUBLE NOT NULL,
              edits INT UNSIGNED NOT NULL,
              non_null_edits INT UNSIGNED NOT NULL,
              active_days INT UNSIGNED NOT NULL,
              distinct_pages INT UNSIGNED NOT NULL,
              top_page_id INT UNSIGNED NOT NULL,
              top_page_share DOUBLE NOT NULL,
              distinct_categories INT UNSIGNED NOT NULL,
              top_category VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_as_ci DEFAULT NULL,
              top_category_share DOUBLE DEFAULT NULL,
              top_tight_category_share DOUBLE DEFAULT NULL,
              reverted_edits INT UNSIGNED NOT NULL,
              reverted_edits_by_others INT UNSIGNED NOT NULL,
              reverts_made INT UNSIGNED NOT NULL,
              reverts_made_others INT UNSIGNED NOT NULL,
              reverted_ratio DOUBLE NOT NULL,
              reverts_of_others_ratio DOUBLE NOT NULL,
              summary_usage DOUBLE NOT NULL,
              custom_summary_usage DOUBLE NOT NULL,
              median_abs_bytes_delta DOUBLE NOT NULL,
              single_purpose TINYINT(1) NOT NULL,
              throwaway TINYINT(1) NOT NULL,
              ip_prefix VARCHAR(64) CHARACTER SET ascii DEFAULT NULL,
              ip_prefix_16 VARCHAR(32) CHARACTER SET ascii DEFAULT NULL,
              coactivity_partners INT UNSIGNED NOT NULL,
              coactivity_reverted_partners INT UNSIGNED NOT NULL,
              KEY idx_kind (kind),
              KEY idx_ip_prefix (ip_prefix),
              KEY idx_ip_prefix_16 (ip_prefix_16),
              KEY idx_single_purpose (single_purpose),
              KEY idx_throwaway (throwaway)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            WITH hidden_s1 AS (
              SELECT
                (SELECT CAST(COUNT(*) AS UNSIGNED) FROM research.s1_reverted_edit WHERE user IS NULL) AS rev_edits,
                (SELECT CAST(COALESCE(SUM(rv.class NOT IN ('self_revert', 'bot', 'excluded')), 0) AS UNSIGNED)
                 FROM research.s1_reverted_edit re
                 JOIN research.s1_revert rv USING (reverting_rev_id)
                 WHERE re.user IS NULL) AS rev_edits_others,
                (SELECT CAST(COALESCE(SUM(class <> 'excluded'), 0) AS UNSIGNED)
                 FROM research.s1_revert WHERE reverting_user IS NULL) AS revs_made,
                (SELECT CAST(COALESCE(SUM(class NOT IN ('excluded', 'self_revert')), 0) AS UNSIGNED)
                 FROM research.s1_revert WHERE reverting_user IS NULL) AS revs_made_others
            ),
            co_agg AS (
              SELECT u AS user,
                     CAST(COUNT(*) AS UNSIGNED) AS coactivity_partners,
                     CAST(SUM(same_side_reverted > 0) AS UNSIGNED) AS coactivity_reverted_partners
              FROM (
                SELECT user_a AS u, same_side_reverted FROM research.s6_coactivity
                UNION ALL
                SELECT user_b AS u, same_side_reverted FROM research.s6_coactivity
              ) pairs_u
              GROUP BY u
            ),
            assembled AS (
              SELECT ur.user,
                     CASE
                       WHEN ur.user = '' THEN 'hidden'
                       WHEN su.anonymous = 1 AND ur.user LIKE '~%' THEN 'temporary'
                       WHEN su.anonymous = 1 AND ur.user LIKE '%:%' THEN 'ipv6'
                       WHEN su.anonymous = 1 THEN 'ipv4'
                       ELSE 'named'
                     END AS kind,
                     COALESCE(su.is_bot, 0) AS is_bot,
                     ur.first_ts,
                     ur.last_ts,
                     CAST(ROUND(TIMESTAMPDIFF(SECOND, ur.first_ts, ur.last_ts) / 86400.0, 4) AS DOUBLE) AS lifespan_days,
                     ur.edits,
                     COALESCE(su.non_null_edits, ur.non_null_edits) AS non_null_edits,
                     ur.active_days,
                     COALESCE(su.distinct_pages, ur.distinct_pages) AS distinct_pages,
                     uc.top_page_id,
                     CAST(ROUND(uc.top_page_edits / ur.edits, 6) AS DOUBLE) AS top_page_share,
                     uc.distinct_categories,
                     uc.top_category,
                     IF(uc.top_category_edits IS NOT NULL,
                        CAST(ROUND(uc.top_category_edits / ur.edits, 6) AS DOUBLE),
                        NULL) AS top_category_share,
                     IF(uc.top_tight_category_edits IS NOT NULL,
                        CAST(ROUND(uc.top_tight_category_edits / ur.edits, 6) AS DOUBLE),
                        NULL) AS top_tight_category_share,
                     COALESCE(su.reverted_edits, hs.rev_edits) AS reverted_edits,
                     COALESCE(su.reverted_edits_by_others, hs.rev_edits_others) AS reverted_edits_by_others,
                     COALESCE(su.reverts_made, hs.revs_made) AS reverts_made,
                     COALESCE(su.reverts_made_others, hs.revs_made_others) AS reverts_made_others,
                     CAST(ROUND(COALESCE(su.reverted_edits_by_others, hs.rev_edits_others) / ur.edits, 6) AS DOUBLE) AS reverted_ratio,
                     CAST(ROUND(COALESCE(su.reverts_made_others, hs.revs_made_others) / ur.edits, 6) AS DOUBLE) AS reverts_of_others_ratio,
                     CAST(ROUND(ur.comment_edits / ur.edits, 6) AS DOUBLE) AS summary_usage,
                     CAST(ROUND(ur.custom_comment_edits / ur.edits, 6) AS DOUBLE) AS custom_summary_usage,
                     ur.median_abs_bytes_delta,
                     COALESCE(co.coactivity_partners, 0) AS coactivity_partners,
                     COALESCE(co.coactivity_reverted_partners, 0) AS coactivity_reverted_partners
              FROM research.s6__user_rev_stats ur
              JOIN research.s6__user_conc uc ON uc.user = ur.user
              CROSS JOIN hidden_s1 hs
              LEFT JOIN research.s1_user_stats su ON su.user = ur.user
              LEFT JOIN co_agg co ON co.user = ur.user
            )
            SELECT a.user,
                   a.kind,
                   a.is_bot,
                   a.first_ts,
                   a.last_ts,
                   a.lifespan_days,
                   a.edits,
                   a.non_null_edits,
                   a.active_days,
                   a.distinct_pages,
                   a.top_page_id,
                   a.top_page_share,
                   a.distinct_categories,
                   a.top_category,
                   a.top_category_share,
                   a.top_tight_category_share,
                   a.reverted_edits,
                   a.reverted_edits_by_others,
                   a.reverts_made,
                   a.reverts_made_others,
                   a.reverted_ratio,
                   a.reverts_of_others_ratio,
                   a.summary_usage,
                   a.custom_summary_usage,
                   a.median_abs_bytes_delta,
                   IF(a.is_bot = 0 AND a.kind <> 'hidden'
                      AND a.edits >= {t['single_purpose_min_edits']}
                      AND (a.top_page_share >= {t['single_purpose_top_page_share']}
                           OR COALESCE(a.top_tight_category_share, 0) >= {t['single_purpose_top_tight_category_share']}),
                      1, 0) AS single_purpose,
                   IF(a.is_bot = 0 AND a.kind <> 'hidden'
                      AND a.edits <= {t['throwaway_max_edits']}
                      AND a.lifespan_days <= {t['throwaway_max_lifespan_days']}
                      AND a.reverted_ratio > {t['throwaway_min_reverted_ratio_exclusive']},
                      1, 0) AS throwaway,
                   CASE
                     WHEN a.kind = 'ipv4' THEN CONCAT(INET_NTOA(INET_ATON(a.user) & 4294967040), '/24')
                     WHEN a.kind = 'ipv6' THEN CONCAT(INET6_NTOA(CONCAT(LEFT(INET6_ATON(a.user), 8), REPEAT(CHAR(0), 8))), '/64')
                     ELSE NULL
                   END AS ip_prefix,
                   CASE
                     WHEN a.kind = 'ipv4' THEN CONCAT(INET_NTOA(INET_ATON(a.user) & 4294901760), '/16')
                     ELSE NULL
                   END AS ip_prefix_16,
                   a.coactivity_partners,
                   a.coactivity_reverted_partners
            FROM assembled a;
            """,
        ),
        (
            "9. Build research.s6_ip_prefix (IPv4 /24, IPv4 /16, IPv6 /64)",
            """
            SET SESSION max_statement_time = 55;
            CREATE TABLE research.s6_ip_prefix (
              ip_prefix VARCHAR(64) CHARACTER SET ascii NOT NULL PRIMARY KEY,
              prefix_kind VARCHAR(16) CHARACTER SET ascii NOT NULL,
              editors INT UNSIGNED NOT NULL,
              edits INT UNSIGNED NOT NULL,
              reverted_edits INT UNSIGNED NOT NULL,
              distinct_pages INT UNSIGNED NOT NULL,
              pages_with_reverted_edits INT UNSIGNED NOT NULL,
              reverted_ratio DOUBLE NOT NULL,
              dispute_war_reverted_edits INT UNSIGNED NOT NULL,
              first_ts DATETIME NOT NULL,
              last_ts DATETIME NOT NULL,
              KEY idx_kind_reverted (prefix_kind, reverted_edits DESC, pages_with_reverted_edits DESC)
            ) ENGINE=InnoDB DEFAULT CHARSET=ascii COLLATE=ascii_general_ci
            AS
            WITH user_prefixes AS (
              SELECT user,
                     ip_prefix AS pfx,
                     IF(kind = 'ipv4', 'ipv4_24', 'ipv6_64') AS prefix_kind,
                     edits,
                     reverted_edits_by_others,
                     first_ts,
                     last_ts
              FROM research.s6_editor_profile
              WHERE kind IN ('ipv4', 'ipv6')
              UNION ALL
              SELECT user,
                     ip_prefix_16 AS pfx,
                     'ipv4_16' AS prefix_kind,
                     edits,
                     reverted_edits_by_others,
                     first_ts,
                     last_ts
              FROM research.s6_editor_profile
              WHERE kind = 'ipv4'
            ),
            pfx_users AS (
              SELECT pfx,
                     prefix_kind,
                     CAST(COUNT(*) AS UNSIGNED) AS editors,
                     CAST(SUM(edits) AS UNSIGNED) AS edits,
                     CAST(SUM(reverted_edits_by_others) AS UNSIGNED) AS reverted_edits,
                     MIN(first_ts) AS first_ts,
                     MAX(last_ts) AS last_ts
              FROM user_prefixes
              GROUP BY pfx, prefix_kind
            ),
            pfx_pages AS (
              SELECT upr.pfx,
                     CAST(COUNT(DISTINCT up.page_id) AS UNSIGNED) AS distinct_pages
              FROM user_prefixes upr
              JOIN research.s6__user_page up ON up.user = upr.user
              GROUP BY upr.pfx
            ),
            pfx_rev_pages AS (
              SELECT upr.pfx,
                     CAST(COUNT(DISTINCT re.page_id) AS UNSIGNED) AS pages_with_reverted_edits,
                     CAST(SUM(rv.class IN ('dispute', 'edit_war')) AS UNSIGNED) AS dispute_war_reverted_edits
              FROM user_prefixes upr
              JOIN research.s1_reverted_edit re ON re.user = upr.user
              JOIN research.s1_revert rv USING (reverting_rev_id)
              WHERE rv.class NOT IN ('excluded', 'bot', 'self_revert')
              GROUP BY upr.pfx
            )
            SELECT pu.pfx AS ip_prefix,
                   pu.prefix_kind,
                   pu.editors,
                   pu.edits,
                   pu.reverted_edits,
                   pp.distinct_pages,
                   COALESCE(prp.pages_with_reverted_edits, 0) AS pages_with_reverted_edits,
                   CAST(ROUND(pu.reverted_edits / pu.edits, 6) AS DOUBLE) AS reverted_ratio,
                   COALESCE(prp.dispute_war_reverted_edits, 0) AS dispute_war_reverted_edits,
                   pu.first_ts,
                   pu.last_ts
            FROM pfx_users pu
            JOIN pfx_pages pp ON pp.pfx = pu.pfx
            LEFT JOIN pfx_rev_pages prp ON prp.pfx = pu.pfx;
            """,
        ),
        (
            "10. Clean up staging tables",
            """
            SET SESSION max_statement_time = 55;
            DROP TABLE IF EXISTS
              research.s6__rev,
              research.s6__user_page,
              research.s6__user_rev_stats,
              research.s6__user_conc;
            """,
        ),
    ]


def verify_invariants() -> dict[str, int]:
    """Verify table completeness, referential integrity, and domain bounds."""
    row = aot.sql("""
        SELECT
          (SELECT COUNT(*) FROM research.s6_editor_profile) AS profile_rows,
          (SELECT SUM(edits) FROM research.s6_editor_profile) AS profile_edits_sum,
          (SELECT COUNT(*) FROM corpus.revision) AS corpus_revisions,
          (SELECT COUNT(*) FROM research.s6_rev_author) AS rev_author_rows,
          (SELECT COUNT(*)
           FROM research.s6_rev_author ra
           LEFT JOIN research.s6_editor_profile ep ON ep.user = ra.user
           WHERE ep.user IS NULL) AS orphan_rev_authors,
          (SELECT COUNT(*) FROM research.s6_editor_profile
           WHERE is_bot = 1 AND (single_purpose = 1 OR throwaway = 1)) AS bot_flagged_sp_or_tw,
          (SELECT COUNT(*) FROM research.s6_editor_profile
           WHERE kind IN ('named', 'temporary', 'hidden') AND (ip_prefix IS NOT NULL OR ip_prefix_16 IS NOT NULL)) AS non_ip_with_prefix,
          (SELECT COUNT(*) FROM research.s6_editor_profile
           WHERE kind IN ('ipv4', 'ipv6') AND ip_prefix IS NULL) AS ip_missing_prefix,
          (SELECT COUNT(*)
           FROM research.s6_editor_profile ep
           LEFT JOIN research.s6_ip_prefix p ON p.ip_prefix = ep.ip_prefix
           WHERE ep.ip_prefix IS NOT NULL AND p.ip_prefix IS NULL) AS missing_narrow_prefix_fk,
          (SELECT COUNT(*)
           FROM research.s6_editor_profile ep
           LEFT JOIN research.s6_ip_prefix p16 ON p16.ip_prefix = ep.ip_prefix_16
           WHERE ep.ip_prefix_16 IS NOT NULL AND p16.ip_prefix IS NULL) AS missing_16_prefix_fk,
          (SELECT COUNT(*) FROM research.s6_ip_prefix) AS ip_prefix_rows,
          (SELECT COUNT(*) FROM research.s6_coactivity) AS coactivity_rows,
          (SELECT COUNT(*) FROM research.s6_coactivity WHERE episodes < 3 OR user_a >= user_b) AS invalid_coactivity_rows
    """)[0]
    counts = {k: int(v) for k, v in row.items()}
    assert counts["profile_edits_sum"] == counts["corpus_revisions"], f"Edit sum mismatch: {counts}"
    assert counts["rev_author_rows"] == counts["corpus_revisions"], f"s6_rev_author row mismatch: {counts}"
    assert counts["orphan_rev_authors"] == 0, f"Orphan authors in s6_rev_author: {counts}"
    assert counts["bot_flagged_sp_or_tw"] == 0, f"Bot flagged as single_purpose/throwaway: {counts}"
    assert counts["non_ip_with_prefix"] == 0, f"Non-IP has ip_prefix: {counts}"
    assert counts["ip_missing_prefix"] == 0, f"IP missing ip_prefix: {counts}"
    assert counts["missing_narrow_prefix_fk"] == 0, f"Missing narrow prefix in s6_ip_prefix: {counts}"
    assert counts["missing_16_prefix_fk"] == 0, f"Missing /16 prefix in s6_ip_prefix: {counts}"
    assert counts["invalid_coactivity_rows"] == 0, f"Invalid coactivity rows: {counts}"
    return counts


def main() -> None:
    timings: list[dict] = []
    total_start = time.monotonic()

    for step_name, query in build_steps():
        t0 = time.monotonic()
        aot.sql(query, timeout=60)
        elapsed = round(time.monotonic() - t0, 2)
        if elapsed >= 60.0:
            raise RuntimeError(f"Step {step_name!r} exceeded 60 s limit ({elapsed:.2f} s)")
        aot.log(f"[s6 build] {step_name}: {elapsed:.2f} s")
        timings.append({"step": step_name, "seconds": elapsed})

    t0 = time.monotonic()
    invariants = verify_invariants()
    verify_s = round(time.monotonic() - t0, 2)
    timings.append({"step": "11. Verify invariants (completeness, PK/FK, bounds)", "seconds": verify_s})
    total_s = round(time.monotonic() - total_start, 2)
    aot.log(f"[s6 build] ALL DONE in {total_s:.2f} s — {invariants}")

    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "build_timings.json").write_text(
        json.dumps(
            {
                "total_seconds": total_s,
                "max_step_seconds": max(s["seconds"] for s in timings),
                "thresholds": THRESHOLDS,
                "steps": timings,
                "invariants": invariants,
            },
            indent=2,
        )
        + chr(10),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
