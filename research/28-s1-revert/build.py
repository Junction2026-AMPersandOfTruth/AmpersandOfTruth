#!/usr/bin/env python3
"""P1.S1 (#28): Precompute canonical identity reverts, rule-based revert classes,
reverted edits, and per-page / per-user aggregates across all 862,076 corpus revisions.

Re-runnable (drops and recreates `research.s1_*` tables; cleans up `research.s1__*` staging tables).
Each SQL step runs sequentially (< 90 s budget on the shared 2-vCPU corpus VPS) and writes timings
to `research/28-s1-revert/results/build_timings.json`.

Usage:
    python3 research/28-s1-revert/build.py
"""
from __future__ import annotations

import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

# Surgical bot predicate (prof_ed §5.4 + SUL / parenthetical / prefix bot accounts;
# avoids human names starting with "Bot..." such as Bothnia and Botaurus).
BOT_SQL_TEMPLATE = (
    "({anon_col} = 0 AND {user_col} IS NOT NULL AND ("
    "LOWER({user_col} COLLATE utf8mb4_uca1400_as_ci) REGEXP 'bot(ti)?($|[0-9~_)-])' "
    "OR {user_col} COLLATE utf8mb4_bin IN ("
    "'Lab-ot', 'CommonsDelinker', 'ʬʭ', 'Overlord', 'Loveless', "
    "'Ptbotgourou', 'RobotJcb', 'BOTarate', 'Bot-Schafter', 'BotMultichill', 'BotSottile', 'BotdeSki'"
    ")))"
)

# Dataset M ends at 2026-10-01 10:59:12 UTC; anchor the 5-year window deterministically to corpus end.
LAST_5Y_CUTOFF = "2021-10-01 10:59:12"


def build_steps() -> list[tuple[str, str]]:
    r_bot = BOT_SQL_TEMPLATE.format(anon_col="sr.anonymous", user_col="sr.user")
    e_bot = BOT_SQL_TEMPLATE.format(anon_col="se.anonymous", user_col="se.user")
    u_bot = BOT_SQL_TEMPLATE.format(anon_col="MAX(r.anonymous)", user_col="r.user")

    return [
        (
            "1. Drop previous staging & target tables",
            """
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS
              research.s1__seq,
              research.s1__raw_rev,
              research.s1__span_agg,
              research.s1__edges,
              research.s1__war_pair48h,
              research.s1__owned_counts,
              research.s1_reverted_edit,
              research.s1_revert,
              research.s1_page_stats,
              research.s1_user_stats;
            """,
        ),
        (
            "2. Order revisions per page, drop null revisions, compute SHA-1 lag/lead",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1__seq (
              page_id INT UNSIGNED NOT NULL,
              seq INT UNSIGNED NOT NULL,
              rev_id INT UNSIGNED NOT NULL,
              parent_rev_id INT UNSIGNED DEFAULT NULL,
              ts DATETIME NOT NULL,
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              anonymous TINYINT(1) NOT NULL,
              bytes INT UNSIGNED NOT NULL,
              bytes_delta INT NOT NULL,
              sha1 CHAR(31) CHARACTER SET ascii DEFAULT NULL,
              p_seq INT UNSIGNED DEFAULT NULL,
              next_sha1_seq INT UNSIGNED DEFAULT NULL,
              PRIMARY KEY (page_id, seq),
              KEY idx_rev (rev_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH raw_ord AS (
              SELECT page_id, rev_id, ts, user, anonymous, bytes, sha1,
                     LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
              FROM corpus.revision
            ),
            non_null AS (
              SELECT page_id, rev_id, ts, user, anonymous, bytes, sha1,
                     ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq,
                     LAG(rev_id) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS parent_rev_id,
                     LAG(bytes) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_bytes
              FROM raw_ord
              WHERE NOT (sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1)
            )
            SELECT page_id,
                   CAST(seq AS UNSIGNED) AS seq,
                   rev_id,
                   parent_rev_id,
                   ts,
                   user,
                   anonymous,
                   bytes,
                   CAST(CAST(bytes AS SIGNED) - CAST(COALESCE(prev_bytes, 0) AS SIGNED) AS SIGNED) AS bytes_delta,
                   sha1,
                   CAST(CASE WHEN sha1 IS NOT NULL THEN LAG(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq) END AS UNSIGNED) AS p_seq,
                   CAST(CASE WHEN sha1 IS NOT NULL THEN LEAD(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq) END AS UNSIGNED) AS next_sha1_seq
            FROM non_null;
            """,
        ),
        (
            "3. Detect canonical identity reverts (P.sha1 = R.sha1, latest P < R - 1)",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1__raw_rev (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              r_seq INT UNSIGNED NOT NULL,
              p_seq INT UNSIGNED NOT NULL,
              restored_rev_id INT UNSIGNED NOT NULL,
              first_reverted_rev_id INT UNSIGNED NOT NULL,
              last_reverted_rev_id INT UNSIGNED NOT NULL,
              reverted_count INT UNSIGNED NOT NULL,
              revert_ts DATETIME NOT NULL,
              first_reverted_ts DATETIME NOT NULL,
              seconds_to_revert BIGINT NOT NULL,
              r_bytes INT UNSIGNED NOT NULL,
              restored_bytes INT UNSIGNED NOT NULL,
              net_bytes INT NOT NULL,
              exclude_reason VARCHAR(24) CHARACTER SET ascii DEFAULT NULL,
              KEY idx_page_seqs (page_id, p_seq, r_seq),
              KEY idx_page_rseq (page_id, r_seq)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            SELECT sr.rev_id AS reverting_rev_id,
                   sr.page_id,
                   sr.seq AS r_seq,
                   sp.seq AS p_seq,
                   sp.rev_id AS restored_rev_id,
                   sf.rev_id AS first_reverted_rev_id,
                   sl.rev_id AS last_reverted_rev_id,
                   CAST(sr.seq - sp.seq - 1 AS UNSIGNED) AS reverted_count,
                   sr.ts AS revert_ts,
                   sf.ts AS first_reverted_ts,
                   TIMESTAMPDIFF(SECOND, sf.ts, sr.ts) AS seconds_to_revert,
                   sr.bytes AS r_bytes,
                   sp.bytes AS restored_bytes,
                   CAST(CAST(sl.bytes AS SIGNED) - CAST(sp.bytes AS SIGNED) AS SIGNED) AS net_bytes,
                   CASE
                     WHEN sr.bytes = 0 OR sp.bytes <= 100 THEN 'blanking_collision'
                     WHEN (sr.seq - sp.seq - 1) > 15 THEN 'wide_span'
                     ELSE NULL
                   END AS exclude_reason
            FROM research.s1__seq sr
            JOIN research.s1__seq sp ON sp.page_id = sr.page_id AND sp.seq = sr.p_seq
            JOIN research.s1__seq sf ON sf.page_id = sr.page_id AND sf.seq = sp.seq + 1
            JOIN research.s1__seq sl ON sl.page_id = sr.page_id AND sl.seq = sr.seq - 1
            WHERE sr.p_seq IS NOT NULL AND sr.p_seq < sr.seq - 1;
            """,
        ),
        (
            "4. Aggregate span editor flags, bot/self flags, and re-insertion seq per revert",
            f"""
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1__span_agg (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              r_seq INT UNSIGNED NOT NULL,
              p_seq INT UNSIGNED NOT NULL,
              reverting_user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              reverting_anonymous TINYINT(1) NOT NULL,
              reverted_user_count INT UNSIGNED NOT NULL,
              reverted_users_anonymous_only TINYINT(1) NOT NULL,
              any_reverted_named TINYINT(1) NOT NULL,
              is_excl TINYINT(1) NOT NULL,
              is_bot TINYINT(1) NOT NULL,
              is_self TINYINT(1) NOT NULL,
              first_reins_seq INT UNSIGNED DEFAULT NULL,
              KEY idx_page_rseq (page_id, r_seq)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            SELECT r.reverting_rev_id,
                   r.page_id,
                   r.r_seq,
                   r.p_seq,
                   sr.user AS reverting_user,
                   sr.anonymous AS reverting_anonymous,
                   CAST(COUNT(DISTINCT COALESCE(se.user, CONCAT('__null_rev_', se.rev_id))) AS UNSIGNED) AS reverted_user_count,
                   MIN(IF(se.anonymous = 1, 1, 0)) AS reverted_users_anonymous_only,
                   MAX(IF(se.anonymous = 0 AND se.user IS NOT NULL, 1, 0)) AS any_reverted_named,
                   IF(r.exclude_reason IS NOT NULL, 1, 0) AS is_excl,
                   IF(({r_bot}) OR MIN(IF({e_bot}, 1, 0)) = 1, 1, 0) AS is_bot,
                   IF(sr.user IS NOT NULL AND MIN(IF(se.user IS NOT NULL AND se.user = sr.user, 1, 0)) = 1, 1, 0) AS is_self,
                   MIN(CASE WHEN se.next_sha1_seq > r.r_seq AND se.bytes > 0 THEN se.next_sha1_seq END) AS first_reins_seq
            FROM research.s1__raw_rev r
            JOIN research.s1__seq sr ON sr.page_id = r.page_id AND sr.seq = r.r_seq
            JOIN research.s1__seq se ON se.page_id = r.page_id AND se.seq > r.p_seq AND se.seq < r.r_seq
            GROUP BY r.reverting_rev_id, r.page_id, r.r_seq, r.p_seq, sr.user, sr.anonymous, r.exclude_reason;
            """,
        ),
        (
            "5. Detect 48-hour mutual-revert edit-war user pairs",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1__edges (
              page_id INT UNSIGNED NOT NULL,
              u_lo VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              u_hi VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              reverting_rev_id INT UNSIGNED NOT NULL,
              revert_ts DATETIME NOT NULL,
              dir TINYINT UNSIGNED NOT NULL,
              PRIMARY KEY (page_id, u_lo(120), u_hi(120), reverting_rev_id, dir),
              KEY idx_pair_ts (page_id, u_lo(120), u_hi(120), revert_ts)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            SELECT DISTINCT
                   sa.page_id,
                   LEAST(sa.reverting_user, se.user) AS u_lo,
                   GREATEST(sa.reverting_user, se.user) AS u_hi,
                   sa.reverting_rev_id,
                   r.revert_ts,
                   IF(sa.reverting_user < se.user, 1, 2) AS dir
            FROM research.s1__span_agg sa
            JOIN research.s1__raw_rev r USING (reverting_rev_id)
            JOIN research.s1__seq se ON se.page_id = sa.page_id AND se.seq > sa.p_seq AND se.seq < sa.r_seq
            WHERE sa.is_excl = 0 AND sa.is_bot = 0 AND sa.is_self = 0
              AND sa.reverting_user IS NOT NULL AND se.user IS NOT NULL
              AND sa.reverting_user <> se.user;

            CREATE TABLE research.s1__war_pair48h (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            WITH valid_windows AS (
              SELECT e1.page_id, e1.u_lo, e1.u_hi, e1.reverting_rev_id AS anchor_rev_id, e1.revert_ts AS anchor_ts
              FROM research.s1__edges e1
              JOIN research.s1__edges e2
                ON e2.page_id = e1.page_id AND e2.u_lo = e1.u_lo AND e2.u_hi = e1.u_hi
               AND e2.revert_ts >= e1.revert_ts
               AND TIMESTAMPDIFF(SECOND, e1.revert_ts, e2.revert_ts) <= 172800
              GROUP BY e1.page_id, e1.u_lo, e1.u_hi, e1.reverting_rev_id, e1.revert_ts
              HAVING COUNT(DISTINCT e2.reverting_rev_id) >= 3 AND COUNT(DISTINCT e2.dir) = 2
            )
            SELECT DISTINCT e2.reverting_rev_id
            FROM valid_windows vw
            JOIN research.s1__edges e2
              ON e2.page_id = vw.page_id AND e2.u_lo = vw.u_lo AND e2.u_hi = vw.u_hi
             AND e2.revert_ts >= vw.anchor_ts
             AND TIMESTAMPDIFF(SECOND, vw.anchor_ts, e2.revert_ts) <= 172800;
            """,
        ),
        (
            "6. Build research.s1_reverted_edit (earliest non-excluded reverting R per edit)",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_reverted_edit (
              page_id INT UNSIGNED NOT NULL,
              rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              reverting_rev_id INT UNSIGNED NOT NULL,
              parent_rev_id INT UNSIGNED DEFAULT NULL,
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              anonymous TINYINT(1) NOT NULL,
              ts DATETIME NOT NULL,
              bytes_delta INT NOT NULL,
              survival_seconds BIGINT NOT NULL,
              comment VARCHAR(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_as_ci DEFAULT NULL,
              KEY idx_page_id (page_id),
              KEY idx_reverting_rev_id (reverting_rev_id),
              KEY idx_user (user)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH ranked AS (
              SELECT se.page_id,
                     se.rev_id,
                     r.reverting_rev_id,
                     se.parent_rev_id,
                     se.user,
                     se.anonymous,
                     se.ts,
                     se.bytes_delta,
                     TIMESTAMPDIFF(SECOND, se.ts, r.revert_ts) AS survival_seconds,
                     ROW_NUMBER() OVER (PARTITION BY se.page_id, se.rev_id ORDER BY r.r_seq ASC) AS rn
              FROM research.s1__raw_rev r
              JOIN research.s1__seq se ON se.page_id = r.page_id AND se.seq > r.p_seq AND se.seq < r.r_seq
              WHERE r.exclude_reason IS NULL
            )
            SELECT rk.page_id,
                   rk.rev_id,
                   rk.reverting_rev_id,
                   rk.parent_rev_id,
                   rk.user,
                   rk.anonymous,
                   rk.ts,
                   rk.bytes_delta,
                   rk.survival_seconds,
                   cr.comment
            FROM ranked rk
            JOIN corpus.revision cr ON cr.page_id = rk.page_id AND cr.rev_id = rk.rev_id
            WHERE rk.rn = 1;
            """,
        ),
        (
            "7. Build research.s1_revert (auto-summary family 1-6, re-insertion, 7-way class)",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1__owned_counts (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              owned_reverted_count INT UNSIGNED NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            SELECT reverting_rev_id, CAST(COUNT(*) AS UNSIGNED) AS owned_reverted_count
            FROM research.s1_reverted_edit
            GROUP BY reverting_rev_id;

            CREATE TABLE research.s1_revert (
              page_id INT UNSIGNED NOT NULL,
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              restored_rev_id INT UNSIGNED NOT NULL,
              first_reverted_rev_id INT UNSIGNED NOT NULL,
              last_reverted_rev_id INT UNSIGNED NOT NULL,
              reverted_count INT UNSIGNED NOT NULL,
              owned_reverted_count INT UNSIGNED NOT NULL,
              reverting_user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              reverting_anonymous TINYINT(1) NOT NULL,
              reverted_user_count INT UNSIGNED NOT NULL,
              reverted_users_anonymous_only TINYINT(1) NOT NULL,
              revert_ts DATETIME NOT NULL,
              first_reverted_ts DATETIME NOT NULL,
              seconds_to_revert BIGINT NOT NULL,
              r_bytes INT UNSIGNED NOT NULL,
              net_bytes INT NOT NULL,
              comment_family TINYINT UNSIGNED DEFAULT NULL,
              reinserted_rev_id INT UNSIGNED DEFAULT NULL,
              class VARCHAR(20) CHARACTER SET ascii NOT NULL,
              exclude_reason VARCHAR(24) CHARACTER SET ascii DEFAULT NULL,
              reverting_comment VARCHAR(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_as_ci DEFAULT NULL,
              KEY idx_page_id (page_id),
              KEY idx_page_class (page_id, class),
              KEY idx_reverting_user (reverting_user)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            SELECT r.page_id,
                   r.reverting_rev_id,
                   r.restored_rev_id,
                   r.first_reverted_rev_id,
                   r.last_reverted_rev_id,
                   r.reverted_count,
                   COALESCE(oc.owned_reverted_count, 0) AS owned_reverted_count,
                   sa.reverting_user,
                   sa.reverting_anonymous,
                   sa.reverted_user_count,
                   sa.reverted_users_anonymous_only,
                   r.revert_ts,
                   r.first_reverted_ts,
                   r.seconds_to_revert,
                   r.r_bytes,
                   r.net_bytes,
                   CASE
                     WHEN cr.comment COLLATE utf8mb4_uca1400_as_ci REGEXP '(muokkaukset|vandalismi) kumottiin ja (sivu|artikkeli) palautettiin' THEN 1
                     WHEN cr.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Kumottu muokkaus (#|[[][[]Special:Diff/|[[][[]Toiminnot:ero/)?[0-9]+' THEN 2
                     WHEN cr.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Hylättiin (viimeisin|viimeisimmät|viimeiset|viimeinen|ensimmäinen|ensimmäiset) ([0-9]+ )?tekstimuuto' THEN 3
                     WHEN cr.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'muokkaus [0-9]+ kumottiin|Käyttäjän [^ ]+ muokkaukset kumottiin$' THEN 4
                     WHEN cr.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Revert(ed)? to (the )?revision|Reverted edits by' THEN 5
                     WHEN cr.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Palautett(iin|u) .*muutokseen' THEN 6
                     ELSE NULL
                   END AS comment_family,
                   sreins.rev_id AS reinserted_rev_id,
                   CASE
                     WHEN r.exclude_reason IS NOT NULL THEN 'excluded'
                     WHEN sa.is_bot = 1 THEN 'bot'
                     WHEN sa.is_self = 1 THEN 'self_revert'
                     WHEN wp.reverting_rev_id IS NOT NULL
                          OR EXISTS (
                            SELECT 1 FROM research.s1__span_agg ir
                            WHERE ir.page_id = r.page_id
                              AND ir.r_seq > r.p_seq AND ir.r_seq < r.r_seq
                              AND ir.is_excl = 0 AND ir.is_bot = 0 AND ir.is_self = 0
                          ) THEN 'edit_war'
                     WHEN sa.any_reverted_named = 1 OR sreins.rev_id IS NOT NULL THEN 'dispute'
                     WHEN sa.reverted_users_anonymous_only = 1 AND r.seconds_to_revert < 86400 THEN 'graffiti_cleanup'
                     ELSE 'other'
                   END AS class,
                   r.exclude_reason,
                   cr.comment AS reverting_comment
            FROM research.s1__raw_rev r
            JOIN research.s1__span_agg sa USING (reverting_rev_id)
            JOIN corpus.revision cr ON cr.page_id = r.page_id AND cr.rev_id = r.reverting_rev_id
            LEFT JOIN research.s1__seq sreins ON sreins.page_id = r.page_id AND sreins.seq = sa.first_reins_seq
            LEFT JOIN research.s1__war_pair48h wp ON wp.reverting_rev_id = r.reverting_rev_id
            LEFT JOIN research.s1__owned_counts oc ON oc.reverting_rev_id = r.reverting_rev_id;
            """,
        ),
        (
            "8. Build research.s1_page_stats (all 19,742 corpus pages)",
            f"""
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_page_stats (
              page_id INT UNSIGNED NOT NULL PRIMARY KEY,
              title VARCHAR(255) NOT NULL,
              revision_count INT UNSIGNED NOT NULL,
              excluded_count INT UNSIGNED NOT NULL,
              bot_count INT UNSIGNED NOT NULL,
              self_revert_count INT UNSIGNED NOT NULL,
              edit_war_count INT UNSIGNED NOT NULL,
              dispute_count INT UNSIGNED NOT NULL,
              graffiti_cleanup_count INT UNSIGNED NOT NULL,
              other_count INT UNSIGNED NOT NULL,
              total INT UNSIGNED NOT NULL,
              non_excluded_total INT UNSIGNED NOT NULL,
              signal_total INT UNSIGNED NOT NULL,
              last_5y_total INT UNSIGNED NOT NULL,
              last_5y_signal_total INT UNSIGNED NOT NULL,
              first_revert_ts DATETIME DEFAULT NULL,
              last_revert_ts DATETIME DEFAULT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH pages AS (
              SELECT page_id, MAX(title) AS title, CAST(COUNT(*) AS UNSIGNED) AS revision_count
              FROM corpus.revision
              GROUP BY page_id
            ),
            rev_agg AS (
              SELECT page_id,
                     CAST(SUM(class = 'excluded') AS UNSIGNED) AS excluded_count,
                     CAST(SUM(class = 'bot') AS UNSIGNED) AS bot_count,
                     CAST(SUM(class = 'self_revert') AS UNSIGNED) AS self_revert_count,
                     CAST(SUM(class = 'edit_war') AS UNSIGNED) AS edit_war_count,
                     CAST(SUM(class = 'dispute') AS UNSIGNED) AS dispute_count,
                     CAST(SUM(class = 'graffiti_cleanup') AS UNSIGNED) AS graffiti_cleanup_count,
                     CAST(SUM(class = 'other') AS UNSIGNED) AS other_count,
                     CAST(COUNT(*) AS UNSIGNED) AS total,
                     CAST(SUM(class <> 'excluded') AS UNSIGNED) AS non_excluded_total,
                     CAST(SUM(class IN ('edit_war', 'dispute', 'graffiti_cleanup', 'other')) AS UNSIGNED) AS signal_total,
                     CAST(SUM(revert_ts >= '{LAST_5Y_CUTOFF}') AS UNSIGNED) AS last_5y_total,
                     CAST(SUM(revert_ts >= '{LAST_5Y_CUTOFF}' AND class IN ('edit_war', 'dispute', 'graffiti_cleanup', 'other')) AS UNSIGNED) AS last_5y_signal_total,
                     MIN(revert_ts) AS first_revert_ts,
                     MAX(revert_ts) AS last_revert_ts
              FROM research.s1_revert
              GROUP BY page_id
            )
            SELECT p.page_id,
                   p.title,
                   p.revision_count,
                   COALESCE(a.excluded_count, 0) AS excluded_count,
                   COALESCE(a.bot_count, 0) AS bot_count,
                   COALESCE(a.self_revert_count, 0) AS self_revert_count,
                   COALESCE(a.edit_war_count, 0) AS edit_war_count,
                   COALESCE(a.dispute_count, 0) AS dispute_count,
                   COALESCE(a.graffiti_cleanup_count, 0) AS graffiti_cleanup_count,
                   COALESCE(a.other_count, 0) AS other_count,
                   COALESCE(a.total, 0) AS total,
                   COALESCE(a.non_excluded_total, 0) AS non_excluded_total,
                   COALESCE(a.signal_total, 0) AS signal_total,
                   COALESCE(a.last_5y_total, 0) AS last_5y_total,
                   COALESCE(a.last_5y_signal_total, 0) AS last_5y_signal_total,
                   a.first_revert_ts,
                   a.last_revert_ts
            FROM pages p
            LEFT JOIN rev_agg a ON a.page_id = p.page_id;
            """,
        ),
        (
            "9. Build research.s1_user_stats (exact utf8mb4_bin usernames)",
            f"""
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_user_stats (
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL PRIMARY KEY,
              anonymous TINYINT(1) NOT NULL,
              is_bot TINYINT(1) NOT NULL,
              edits INT UNSIGNED NOT NULL,
              non_null_edits INT UNSIGNED NOT NULL,
              reverted_edits INT UNSIGNED NOT NULL,
              reverted_edits_by_others INT UNSIGNED NOT NULL,
              reverts_made INT UNSIGNED NOT NULL,
              reverts_made_others INT UNSIGNED NOT NULL,
              distinct_pages INT UNSIGNED NOT NULL,
              first_edit_ts DATETIME NOT NULL,
              last_edit_ts DATETIME NOT NULL,
              KEY idx_reverted_edits (reverted_edits),
              KEY idx_reverts_made (reverts_made)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            WITH base_u AS (
              SELECT r.user COLLATE utf8mb4_bin AS user,
                     MAX(r.anonymous) AS anonymous,
                     IF({u_bot}, 1, 0) AS is_bot,
                     CAST(COUNT(*) AS UNSIGNED) AS edits,
                     CAST(COUNT(DISTINCT r.page_id) AS UNSIGNED) AS distinct_pages,
                     MIN(r.ts) AS first_edit_ts,
                     MAX(r.ts) AS last_edit_ts
              FROM corpus.revision r
              WHERE r.user IS NOT NULL
              GROUP BY r.user COLLATE utf8mb4_bin
            ),
            nn_u AS (
              SELECT user, CAST(COUNT(*) AS UNSIGNED) AS non_null_edits
              FROM research.s1__seq
              WHERE user IS NOT NULL
              GROUP BY user
            ),
            re_u AS (
              SELECT re.user,
                     CAST(COUNT(*) AS UNSIGNED) AS reverted_edits,
                     CAST(SUM(rv.class NOT IN ('self_revert', 'bot', 'excluded')) AS UNSIGNED) AS reverted_edits_by_others
              FROM research.s1_reverted_edit re
              JOIN research.s1_revert rv USING (reverting_rev_id)
              WHERE re.user IS NOT NULL
              GROUP BY re.user
            ),
            rv_u AS (
              SELECT reverting_user AS user,
                     CAST(SUM(class <> 'excluded') AS UNSIGNED) AS reverts_made,
                     CAST(SUM(class NOT IN ('excluded', 'self_revert')) AS UNSIGNED) AS reverts_made_others
              FROM research.s1_revert
              WHERE reverting_user IS NOT NULL
              GROUP BY reverting_user
            )
            SELECT b.user,
                   b.anonymous,
                   b.is_bot,
                   b.edits,
                   COALESCE(nn.non_null_edits, 0) AS non_null_edits,
                   COALESCE(re.reverted_edits, 0) AS reverted_edits,
                   COALESCE(re.reverted_edits_by_others, 0) AS reverted_edits_by_others,
                   COALESCE(rv.reverts_made, 0) AS reverts_made,
                   COALESCE(rv.reverts_made_others, 0) AS reverts_made_others,
                   b.distinct_pages,
                   b.first_edit_ts,
                   b.last_edit_ts
            FROM base_u b
            LEFT JOIN nn_u nn ON nn.user = b.user
            LEFT JOIN re_u re ON re.user = b.user
            LEFT JOIN rv_u rv ON rv.user = b.user;
            """,
        ),
        (
            "10. Clean up staging tables",
            """
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS
              research.s1__seq,
              research.s1__raw_rev,
              research.s1__span_agg,
              research.s1__edges,
              research.s1__war_pair48h,
              research.s1__owned_counts;
            """,
        ),
    ]


def verify_invariants() -> dict:
    """Assert acceptance invariants directly on the built tables."""
    checks = aot.sql("""
        SELECT
          (SELECT COUNT(*) FROM research.s1_revert) AS s1_revert_rows,
          (SELECT COUNT(*) FROM research.s1_reverted_edit) AS s1_reverted_edit_rows,
          (SELECT COUNT(*) FROM research.s1_page_stats) AS s1_page_stats_rows,
          (SELECT COUNT(*) FROM research.s1_user_stats) AS s1_user_stats_rows,
          (SELECT COUNT(*) - COUNT(DISTINCT rev_id) FROM research.s1_reverted_edit) AS duplicate_reverted_edits,
          (SELECT COUNT(*)
           FROM (
             SELECT page_id, rev_id, sha1,
                    LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
             FROM corpus.revision
           ) raw_r
           JOIN research.s1_reverted_edit re ON re.page_id = raw_r.page_id AND re.rev_id = raw_r.rev_id
           WHERE raw_r.sha1 IS NOT NULL AND raw_r.imm_prev_sha1 IS NOT NULL AND raw_r.sha1 = raw_r.imm_prev_sha1
          ) AS null_revs_in_reverted_edit,
          (SELECT COUNT(*) FROM research.s1_revert WHERE class <> 'excluded' AND owned_reverted_count = 0) AS non_excluded_zero_owned
    """)[0]
    counts = {k: int(v) for k, v in checks.items()}
    assert counts["duplicate_reverted_edits"] == 0, f"duplicate rev_id in s1_reverted_edit: {counts}"
    assert counts["null_revs_in_reverted_edit"] == 0, f"null revision found in s1_reverted_edit: {counts}"
    assert counts["non_excluded_zero_owned"] == 0, f"non-excluded revert with 0 owned edits: {counts}"
    return counts


def main() -> None:
    # Clean up any leftover exploratory tables if present before starting.
    aot.sql("DROP TABLE IF EXISTS research.s1__pairs;")

    timings: list[dict] = []
    total_start = time.monotonic()
    for step_name, query in build_steps():
        t0 = time.monotonic()
        aot.sql(query, timeout=90)
        elapsed = round(time.monotonic() - t0, 2)
        if elapsed >= 90.0:
            raise RuntimeError(f"Step {step_name!r} exceeded 90 s limit ({elapsed:.2f} s)")
        aot.log(f"[s1 build] {step_name}: {elapsed:.2f} s")
        timings.append({"step": step_name, "seconds": elapsed})

    t0 = time.monotonic()
    invariants = verify_invariants()
    verify_s = round(time.monotonic() - t0, 2)
    timings.append({"step": "11. Verify invariants (uniqueness, zero null revisions)", "seconds": verify_s})
    total_s = round(time.monotonic() - total_start, 2)
    aot.log(f"[s1 build] ALL DONE in {total_s:.2f} s — {invariants}")

    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "build_timings.json").write_text(
        json.dumps({"total_seconds": total_s, "steps": timings, "invariants": invariants}, indent=2) + chr(10),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
