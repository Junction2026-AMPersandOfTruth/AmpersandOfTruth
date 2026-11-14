-- ============================================================================
-- Issue #17, Part A (prof_rev): Edit history profiling — revisions, reverts,
-- survival, S<->M join, and wikitext citations/templates.
-- Runnable as-is:
--   mariadb -h127.0.0.1 -P3307 -ucorpus_ro corpus --batch --raw < research/profiling/prof_rev/queries.sql
-- ============================================================================

-- Setup: Materialise scratch tables in research.prof_rev_* (canonical revert definition)
DROP TABLE IF EXISTS research.prof_rev_seq;
CREATE TABLE research.prof_rev_seq (
  page_id INT UNSIGNED NOT NULL,
  seq INT UNSIGNED NOT NULL,
  rev_id INT UNSIGNED NOT NULL,
  ts DATETIME NOT NULL,
  anonymous TINYINT(1) NOT NULL,
  sha1 CHAR(31) CHARACTER SET ascii DEFAULT NULL,
  bytes INT UNSIGNED NOT NULL,
  p_seq INT UNSIGNED DEFAULT NULL,
  p2_seq INT UNSIGNED DEFAULT NULL,
  PRIMARY KEY (page_id, seq),
  KEY idx_page_rev (page_id, rev_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
AS
WITH ordered AS (
  SELECT page_id, rev_id, ts, anonymous, sha1, bytes,
         ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq
  FROM corpus.revision
)
SELECT page_id,
       CAST(seq AS UNSIGNED) AS seq,
       rev_id, ts, anonymous, sha1, bytes,
       CAST(CASE WHEN sha1 IS NOT NULL THEN LAG(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq) END AS UNSIGNED) AS p_seq,
       CAST(CASE WHEN sha1 IS NOT NULL THEN LAG(seq, 2) OVER (PARTITION BY page_id, sha1 ORDER BY seq) END AS UNSIGNED) AS p2_seq
FROM ordered;

CREATE OR REPLACE TABLE research.prof_rev_pages (
  page_id INT UNSIGNED NOT NULL PRIMARY KEY,
  title VARCHAR(255) NOT NULL,
  rev_count INT UNSIGNED NOT NULL,
  first_ts DATETIME NOT NULL,
  last_ts DATETIME NOT NULL,
  latest_rev_id INT UNSIGNED NOT NULL,
  latest_bytes INT UNSIGNED NOT NULL,
  latest_sha1 CHAR(31) CHARACTER SET ascii DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
AS
SELECT agg.page_id,
       r_last.title,
       agg.rev_count,
       agg.first_ts,
       agg.last_ts,
       s_last.rev_id AS latest_rev_id,
       s_last.bytes AS latest_bytes,
       s_last.sha1 AS latest_sha1
FROM (
  SELECT page_id,
         CAST(COUNT(*) AS UNSIGNED) AS rev_count,
         MIN(ts) AS first_ts,
         MAX(ts) AS last_ts
  FROM research.prof_rev_seq
  GROUP BY page_id
) agg
JOIN research.prof_rev_seq s_last
  ON s_last.page_id = agg.page_id AND s_last.seq = agg.rev_count
JOIN corpus.revision r_last
  ON r_last.page_id = s_last.page_id AND r_last.rev_id = s_last.rev_id;

CREATE OR REPLACE TABLE research.prof_rev_reverts (
  page_id INT UNSIGNED NOT NULL,
  r_rev_id INT UNSIGNED NOT NULL,
  r_seq INT UNSIGNED NOT NULL,
  r_ts DATETIME NOT NULL,
  r_user VARCHAR(255) DEFAULT NULL,
  r_anonymous TINYINT(1) NOT NULL,
  r_comment VARCHAR(1000) DEFAULT NULL,
  r_bytes INT UNSIGNED NOT NULL,
  sha1 CHAR(31) CHARACTER SET ascii NOT NULL,
  p_rev_id INT UNSIGNED NOT NULL,
  p_seq INT UNSIGNED NOT NULL,
  p_ts DATETIME NOT NULL,
  p_user VARCHAR(255) DEFAULT NULL,
  p_bytes INT UNSIGNED NOT NULL,
  last_e_rev_id INT UNSIGNED NOT NULL,
  last_e_bytes INT UNSIGNED NOT NULL,
  run_len INT UNSIGNED NOT NULL,
  run_delta_bytes INT NOT NULL,
  PRIMARY KEY (page_id, r_rev_id),
  KEY idx_page_p_r (page_id, p_seq, r_seq),
  KEY idx_r_ts (r_ts)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
AS
SELECT sr.page_id,
       sr.rev_id AS r_rev_id,
       sr.seq AS r_seq,
       sr.ts AS r_ts,
       rr.user AS r_user,
       sr.anonymous AS r_anonymous,
       rr.comment AS r_comment,
       sr.bytes AS r_bytes,
       sr.sha1,
       sp.rev_id AS p_rev_id,
       sp.seq AS p_seq,
       sp.ts AS p_ts,
       rp.user AS p_user,
       sp.bytes AS p_bytes,
       se.rev_id AS last_e_rev_id,
       se.bytes AS last_e_bytes,
       CAST(sr.seq - sp.seq - 1 AS UNSIGNED) AS run_len,
       CAST(CAST(se.bytes AS SIGNED) - CAST(sp.bytes AS SIGNED) AS SIGNED) AS run_delta_bytes
FROM research.prof_rev_seq sr
JOIN research.prof_rev_seq sp
  ON sp.page_id = sr.page_id AND sp.seq = sr.p_seq
JOIN research.prof_rev_seq se
  ON se.page_id = sr.page_id AND se.seq = sr.seq - 1
JOIN corpus.revision rr
  ON rr.page_id = sr.page_id AND rr.rev_id = sr.rev_id
JOIN corpus.revision rp
  ON rp.page_id = sp.page_id AND rp.rev_id = sp.rev_id
WHERE sr.p_seq IS NOT NULL AND sr.p_seq < sr.seq - 1;

CREATE OR REPLACE TABLE research.prof_rev_reverted_edits (
  page_id INT UNSIGNED NOT NULL,
  rev_id INT UNSIGNED NOT NULL,
  r_rev_id INT UNSIGNED NOT NULL,
  e_seq INT UNSIGNED NOT NULL,
  title VARCHAR(255) NOT NULL,
  e_ts DATETIME NOT NULL,
  e_user VARCHAR(255) DEFAULT NULL,
  e_anonymous TINYINT(1) NOT NULL,
  e_comment VARCHAR(1000) DEFAULT NULL,
  e_sha1 CHAR(31) CHARACTER SET ascii DEFAULT NULL,
  e_bytes INT UNSIGNED NOT NULL,
  prev_sha1 CHAR(31) CHARACTER SET ascii DEFAULT NULL,
  prev_bytes INT UNSIGNED DEFAULT NULL,
  r_seq INT UNSIGNED NOT NULL,
  r_ts DATETIME NOT NULL,
  r_user VARCHAR(255) DEFAULT NULL,
  r_anonymous TINYINT(1) NOT NULL,
  r_comment VARCHAR(1000) DEFAULT NULL,
  p_rev_id INT UNSIGNED NOT NULL,
  p_seq INT UNSIGNED NOT NULL,
  p_bytes INT UNSIGNED NOT NULL,
  delta_vs_p INT NOT NULL,
  survival_sec BIGINT NOT NULL,
  revert_rank SMALLINT UNSIGNED NOT NULL,
  PRIMARY KEY (page_id, rev_id, r_rev_id),
  KEY idx_rank_ts (revert_rank, e_ts),
  KEY idx_rank_surv (revert_rank, survival_sec)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
AS
SELECT se.page_id,
       se.rev_id,
       rv.r_rev_id,
       se.seq AS e_seq,
       re.title,
       se.ts AS e_ts,
       re.user AS e_user,
       se.anonymous AS e_anonymous,
       re.comment AS e_comment,
       se.sha1 AS e_sha1,
       se.bytes AS e_bytes,
       sprev.sha1 AS prev_sha1,
       sprev.bytes AS prev_bytes,
       rv.r_seq,
       rv.r_ts,
       rv.r_user,
       rv.r_anonymous,
       rv.r_comment,
       rv.p_rev_id,
       rv.p_seq,
       rv.p_bytes,
       CAST(CAST(se.bytes AS SIGNED) - CAST(rv.p_bytes AS SIGNED) AS SIGNED) AS delta_vs_p,
       TIMESTAMPDIFF(SECOND, se.ts, rv.r_ts) AS survival_sec,
       CAST(ROW_NUMBER() OVER (PARTITION BY se.page_id, se.rev_id ORDER BY rv.r_seq) AS UNSIGNED) AS revert_rank
FROM research.prof_rev_reverts rv
JOIN research.prof_rev_seq se
  ON se.page_id = rv.page_id AND se.seq > rv.p_seq AND se.seq < rv.r_seq
LEFT JOIN research.prof_rev_seq sprev
  ON sprev.page_id = se.page_id AND sprev.seq = se.seq - 1
JOIN corpus.revision re
  ON re.page_id = se.page_id AND re.rev_id = se.rev_id;

-- Q2: Consecutive identical-sha1 null/log edit audit (captured before dropping staging table)
SELECT
  SUM(p_seq = seq - 1) AS consecutive_same_sha1,
  SUM(p_seq = seq - 1 AND p2_seq IS NOT NULL) AS consecutive_same_sha1_with_older_match
FROM research.prof_rev_seq;

DROP TABLE research.prof_rev_seq;

CREATE OR REPLACE TABLE research.prof_rev_q8_sample (
  page_id INT UNSIGNED NOT NULL,
  rev_id INT UNSIGNED NOT NULL,
  title VARCHAR(255) NOT NULL,
  latest_bytes INT UNSIGNED NOT NULL,
  PRIMARY KEY (page_id, rev_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
AS
SELECT page_id, latest_rev_id AS rev_id, title, latest_bytes
FROM research.prof_rev_pages
ORDER BY SHA2(CONCAT('prof_rev_q8:', page_id), 256), page_id
LIMIT 200;

-- Q1: Revisions per article — percentiles (min, p25, median, p75, p90, p99, max) and mean
SELECT DISTINCT
  COUNT(*) OVER () AS articles,
  SUM(rev_count) OVER () AS revisions,
  ROUND(AVG(rev_count) OVER (), 2) AS mean_rev,
  MIN(rev_count) OVER () AS min_rev,
  ROUND(PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY rev_count) OVER (), 2) AS p25,
  ROUND(PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY rev_count) OVER (), 2) AS median,
  ROUND(PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY rev_count) OVER (), 2) AS p75,
  ROUND(PERCENTILE_CONT(0.90) WITHIN GROUP (ORDER BY rev_count) OVER (), 2) AS p90,
  ROUND(PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY rev_count) OVER (), 2) AS p99,
  MAX(rev_count) OVER () AS max_rev
FROM research.prof_rev_pages;

-- Q1: Revisions per article — histogram buckets (1, 2-10, 11-100, 101-500, 501-1000, >1000)
SELECT
  CASE
    WHEN rev_count = 1 THEN '1'
    WHEN rev_count BETWEEN 2 AND 10 THEN '2-10'
    WHEN rev_count BETWEEN 11 AND 100 THEN '11-100'
    WHEN rev_count BETWEEN 101 AND 500 THEN '101-500'
    WHEN rev_count BETWEEN 501 AND 1000 THEN '501-1000'
    ELSE '>1000'
  END AS bucket,
  COUNT(*) AS articles,
  ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM research.prof_rev_pages), 2) AS pct_articles,
  SUM(rev_count) AS revisions,
  ROUND(100.0 * SUM(rev_count) / (SELECT SUM(rev_count) FROM research.prof_rev_pages), 2) AS pct_revisions
FROM research.prof_rev_pages
GROUP BY
  CASE
    WHEN rev_count = 1 THEN 1
    WHEN rev_count BETWEEN 2 AND 10 THEN 2
    WHEN rev_count BETWEEN 11 AND 100 THEN 3
    WHEN rev_count BETWEEN 101 AND 500 THEN 4
    WHEN rev_count BETWEEN 501 AND 1000 THEN 5
    ELSE 6
  END, bucket
ORDER BY MIN(rev_count);

-- Q1: Top 50 most-edited articles (confirming Suomen sisällissota = 3,357 at rank #1)
SELECT page_id, title, rev_count AS revisions, YEAR(first_ts) AS first_year, YEAR(last_ts) AS last_year
FROM research.prof_rev_pages
ORDER BY rev_count DESC, page_id ASC
LIMIT 50;

-- Q2: Identity reverts and reverted edits — overall totals and shares
SELECT
  (SELECT COUNT(*) FROM corpus.revision) AS total_edits,
  (SELECT COUNT(*) FROM research.prof_rev_reverts) AS total_reverts,
  ROUND(100.0 * (SELECT COUNT(*) FROM research.prof_rev_reverts) / (SELECT COUNT(*) FROM corpus.revision), 2) AS reverts_pct_of_edits,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1) AS reverted_edits_distinct,
  ROUND(100.0 * (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1) / (SELECT COUNT(*) FROM corpus.revision), 2) AS reverted_pct_of_edits,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits) AS reverted_edit_pairs_with_nested;

-- Q2: Identity reverts and reverted edits by year
WITH edits_y AS (
  SELECT YEAR(ts) AS yr, COUNT(*) AS total_edits
  FROM corpus.revision
  GROUP BY YEAR(ts)
),
rev_y AS (
  SELECT YEAR(r_ts) AS yr, COUNT(*) AS reverts
  FROM research.prof_rev_reverts
  GROUP BY YEAR(r_ts)
),
reved_y AS (
  SELECT YEAR(e_ts) AS yr, SUM(revert_rank = 1) AS reverted_edits
  FROM research.prof_rev_reverted_edits
  GROUP BY YEAR(e_ts)
)
SELECT e.yr,
       e.total_edits,
       COALESCE(r.reverts, 0) AS reverts,
       ROUND(100.0 * COALESCE(r.reverts, 0) / e.total_edits, 2) AS reverts_pct,
       COALESCE(re.reverted_edits, 0) AS reverted_edits,
       ROUND(100.0 * COALESCE(re.reverted_edits, 0) / e.total_edits, 2) AS reverted_edits_pct
FROM edits_y e
LEFT JOIN rev_y r ON r.yr = e.yr
LEFT JOIN reved_y re ON re.yr = e.yr
ORDER BY e.yr;

-- Q2: Reverted edits by anonymous vs named accounts (and share of each group's edits reverted)
SELECT
  CASE WHEN r.anonymous = 1 THEN 'Anonymous (IP + ~temp)' ELSE 'Named account' END AS account_group,
  r.anonymous,
  COUNT(*) AS group_total_edits,
  SUM(re.rev_id IS NOT NULL) AS group_reverted_edits,
  ROUND(100.0 * SUM(re.rev_id IS NOT NULL) / COUNT(*), 2) AS pct_of_group_reverted,
  ROUND(100.0 * SUM(re.rev_id IS NOT NULL) / (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1), 2) AS share_of_all_reverted
FROM corpus.revision r
LEFT JOIN research.prof_rev_reverted_edits re
  ON re.page_id = r.page_id AND re.rev_id = r.rev_id AND re.revert_rank = 1
GROUP BY r.anonymous
ORDER BY r.anonymous DESC;

-- Q2: Distribution of reverted-run length (including share of reverts reverting >1 edit)
SELECT
  COUNT(*) AS total_reverts,
  SUM(run_len > 1) AS reverts_gt_1_edit,
  ROUND(100.0 * SUM(run_len > 1) / COUNT(*), 2) AS pct_gt_1_edit,
  MAX(run_len) AS max_run_len
FROM research.prof_rev_reverts;

-- Q2: Reverted-run length buckets
SELECT
  CASE
    WHEN run_len = 1 THEN '1'
    WHEN run_len = 2 THEN '2'
    WHEN run_len = 3 THEN '3'
    WHEN run_len = 4 THEN '4'
    WHEN run_len = 5 THEN '5'
    WHEN run_len BETWEEN 6 AND 10 THEN '6-10'
    WHEN run_len BETWEEN 11 AND 20 THEN '11-20'
    WHEN run_len BETWEEN 21 AND 50 THEN '21-50'
    ELSE '>50'
  END AS run_len_bucket,
  COUNT(*) AS reverts,
  ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM research.prof_rev_reverts), 2) AS pct_reverts,
  SUM(run_len) AS reverted_slots
FROM research.prof_rev_reverts
GROUP BY
  CASE
    WHEN run_len = 1 THEN 1
    WHEN run_len = 2 THEN 2
    WHEN run_len = 3 THEN 3
    WHEN run_len = 4 THEN 4
    WHEN run_len = 5 THEN 5
    WHEN run_len BETWEEN 6 AND 10 THEN 6
    WHEN run_len BETWEEN 11 AND 20 THEN 7
    WHEN run_len BETWEEN 21 AND 50 THEN 8
    ELSE 9
  END, run_len_bucket
ORDER BY MIN(run_len);

-- Q2: Pitfalls audit (null sha1, repeated 0-byte blankings, self-reverts, bots, null edits inside runs, temp accounts)
SELECT
  (SELECT COUNT(*) FROM corpus.revision WHERE sha1 IS NULL) AS null_sha1_revisions,
  (SELECT COUNT(DISTINCT page_id) FROM corpus.revision WHERE sha1 IS NULL) AS null_sha1_pages,
  (SELECT COUNT(*) FROM corpus.revision WHERE user IS NULL) AS null_user_revisions,
  (SELECT COUNT(*) FROM corpus.revision WHERE user LIKE '~202%' AND anonymous = 1) AS temp_account_revisions,
  (SELECT COUNT(*) FROM research.prof_rev_reverts WHERE r_bytes = 0) AS zero_byte_blanking_reverts,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits re JOIN research.prof_rev_reverts rv ON rv.page_id = re.page_id AND rv.r_rev_id = re.r_rev_id WHERE re.revert_rank = 1 AND rv.r_bytes = 0) AS distinct_edits_swept_by_blanking,
  (SELECT COUNT(*) FROM research.prof_rev_reverts rv WHERE rv.r_user IS NOT NULL AND NOT EXISTS (SELECT 1 FROM research.prof_rev_reverted_edits re WHERE re.page_id = rv.page_id AND re.r_rev_id = rv.r_rev_id AND (re.e_user IS NULL OR re.e_user <> rv.r_user))) AS pure_self_revert_runs,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_user IS NOT NULL AND r_user = e_user) AS self_reverted_edits_all,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_anonymous = 0 AND e_user IS NOT NULL AND r_user = e_user) AS self_reverted_edits_named,
  (SELECT COUNT(*) FROM research.prof_rev_reverts WHERE LOWER(COALESCE(r_user, '')) LIKE '%bot%') AS bot_reverts,
  (SELECT COUNT(*) FROM research.prof_rev_reverts WHERE r_user = 'Addbot') AS addbot_wikidata_reverts,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND LOWER(COALESCE(e_user, '')) LIKE '%bot%') AS bot_edits_reverted,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_sha1 IS NOT NULL AND e_sha1 = prev_sha1) AS null_log_edits_inside_runs;

-- Q2: Sanity-check 3 reverts by eye
(SELECT 'case1_single_pov' AS tag, rv.page_id, p.title, rv.p_rev_id, rv.last_e_rev_id, rv.r_rev_id, rv.run_len, rv.p_ts, rv.r_ts, rv.p_bytes, rv.last_e_bytes, rv.r_bytes, rv.r_user, LEFT(REPLACE(REPLACE(COALESCE(rv.r_comment, ''), CHAR(10), ' '), CHAR(9), ' '), 100) AS r_comment
 FROM research.prof_rev_reverts rv JOIN research.prof_rev_pages p ON p.page_id = rv.page_id
 WHERE rv.page_id = 1150 AND rv.r_rev_id = 24234990)
UNION ALL
(SELECT 'case2_multi_spam', rv.page_id, p.title, rv.p_rev_id, rv.last_e_rev_id, rv.r_rev_id, rv.run_len, rv.p_ts, rv.r_ts, rv.p_bytes, rv.last_e_bytes, rv.r_bytes, rv.r_user, LEFT(REPLACE(REPLACE(COALESCE(rv.r_comment, ''), CHAR(10), ' '), CHAR(9), ' '), 100)
 FROM research.prof_rev_reverts rv JOIN research.prof_rev_pages p ON p.page_id = rv.page_id
 WHERE rv.page_id = 1256 AND rv.r_rev_id = 19690942)
UNION ALL
(SELECT 'case3_blank_collision', rv.page_id, p.title, rv.p_rev_id, rv.last_e_rev_id, rv.r_rev_id, rv.run_len, rv.p_ts, rv.r_ts, rv.p_bytes, rv.last_e_bytes, rv.r_bytes, rv.r_user, LEFT(REPLACE(REPLACE(COALESCE(rv.r_comment, ''), CHAR(10), ' '), CHAR(9), ' '), 100)
 FROM research.prof_rev_reverts rv JOIN research.prof_rev_pages p ON p.page_id = rv.page_id
 WHERE rv.page_id = 11594 AND rv.r_rev_id = 19772752);

-- Q3: Size change of the reverted run (bytes vs P) in buckets — per reverted run (N=46,049) and per distinct reverted edit (N=64,966)
WITH run_b AS (
  SELECT
    CASE
      WHEN run_delta_bytes < -1000 THEN 1
      WHEN run_delta_bytes BETWEEN -1000 AND -101 THEN 2
      WHEN run_delta_bytes BETWEEN -100 AND -1 THEN 3
      WHEN run_delta_bytes = 0 THEN 4
      WHEN run_delta_bytes BETWEEN 1 AND 100 THEN 5
      WHEN run_delta_bytes BETWEEN 101 AND 1000 THEN 6
      ELSE 7
    END AS ord,
    CASE
      WHEN run_delta_bytes < -1000 THEN '< -1000'
      WHEN run_delta_bytes BETWEEN -1000 AND -101 THEN '-1000..-101'
      WHEN run_delta_bytes BETWEEN -100 AND -1 THEN '-100..-1'
      WHEN run_delta_bytes = 0 THEN '0'
      WHEN run_delta_bytes BETWEEN 1 AND 100 THEN '1..100'
      WHEN run_delta_bytes BETWEEN 101 AND 1000 THEN '101..1000'
      ELSE '> 1000'
    END AS size_bucket,
    COUNT(*) AS reverted_runs,
    ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM research.prof_rev_reverts), 2) AS pct_runs
  FROM research.prof_rev_reverts
  GROUP BY ord, size_bucket
),
edit_b AS (
  SELECT
    CASE
      WHEN delta_vs_p < -1000 THEN 1
      WHEN delta_vs_p BETWEEN -1000 AND -101 THEN 2
      WHEN delta_vs_p BETWEEN -100 AND -1 THEN 3
      WHEN delta_vs_p = 0 THEN 4
      WHEN delta_vs_p BETWEEN 1 AND 100 THEN 5
      WHEN delta_vs_p BETWEEN 101 AND 1000 THEN 6
      ELSE 7
    END AS ord,
    COUNT(*) AS reverted_edits,
    ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1), 2) AS pct_edits,
    SUM(e_anonymous = 1) AS anon_edits,
    SUM(e_anonymous = 0) AS named_edits
  FROM research.prof_rev_reverted_edits
  WHERE revert_rank = 1
  GROUP BY ord
)
SELECT r.size_bucket,
       r.reverted_runs,
       r.pct_runs,
       e.reverted_edits,
       e.pct_edits,
       e.anon_edits,
       e.named_edits
FROM run_b r
JOIN edit_b e ON e.ord = r.ord
ORDER BY r.ord;

-- Q3: Survival time buckets (<1m, 1-10m, 10-60m, 1-24h, 1-7d, 7-30d, 30-365d, >1y), overall and split anonymous/named
SELECT
  CASE
    WHEN survival_sec < 60 THEN '< 1 min'
    WHEN survival_sec < 600 THEN '1-10 min'
    WHEN survival_sec < 3600 THEN '10-60 min'
    WHEN survival_sec < 86400 THEN '1-24 h'
    WHEN survival_sec < 604800 THEN '1-7 d'
    WHEN survival_sec < 2592000 THEN '7-30 d'
    WHEN survival_sec < 31536000 THEN '30-365 d'
    ELSE '> 1 y'
  END AS survival_bucket,
  COUNT(*) AS overall_edits,
  ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1), 2) AS pct_overall,
  SUM(e_anonymous = 1) AS anon_edits,
  ROUND(100.0 * SUM(e_anonymous = 1) / (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_anonymous = 1), 2) AS pct_anon,
  SUM(e_anonymous = 0) AS named_edits,
  ROUND(100.0 * SUM(e_anonymous = 0) / (SELECT COUNT(*) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_anonymous = 0), 2) AS pct_named
FROM research.prof_rev_reverted_edits
WHERE revert_rank = 1
GROUP BY
  CASE
    WHEN survival_sec < 60 THEN 1
    WHEN survival_sec < 600 THEN 2
    WHEN survival_sec < 3600 THEN 3
    WHEN survival_sec < 86400 THEN 4
    WHEN survival_sec < 604800 THEN 5
    WHEN survival_sec < 2592000 THEN 6
    WHEN survival_sec < 31536000 THEN 7
    ELSE 8
  END, survival_bucket
ORDER BY MIN(survival_sec);

-- Q3: Median survival seconds (overall, anonymous, named) and blanking share of >1y bucket
SELECT
  (SELECT ROUND(MEDIAN(survival_sec) OVER (), 1) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 LIMIT 1) AS median_sec_overall,
  (SELECT ROUND(MEDIAN(survival_sec) OVER (), 1) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_anonymous = 1 LIMIT 1) AS median_sec_anon,
  (SELECT ROUND(MEDIAN(survival_sec) OVER (), 1) FROM research.prof_rev_reverted_edits WHERE revert_rank = 1 AND e_anonymous = 0 LIMIT 1) AS median_sec_named,
  (SELECT COUNT(*) FROM research.prof_rev_reverted_edits re JOIN research.prof_rev_reverts rv ON rv.page_id = re.page_id AND rv.r_rev_id = re.r_rev_id WHERE re.revert_rank = 1 AND re.survival_sec >= 31536000 AND rv.r_bytes = 0) AS gt_1y_from_zero_byte_blanking;

-- Q3: 20 longest-surviving reverted edits (exact canonical definition)
SELECT re.rev_id,
       CONCAT('https://fi.wikipedia.org/w/index.php?diff=', re.rev_id) AS edit_diff_link,
       re.r_rev_id,
       re.title AS article,
       COALESCE(re.e_user, '(null)') AS user,
       re.e_anonymous,
       ROUND(re.survival_sec / 86400.0, 1) AS survival_days,
       ROUND(re.survival_sec / (86400.0 * 365.25), 2) AS survival_years,
       LEFT(REPLACE(REPLACE(COALESCE(re.e_comment, ''), CHAR(10), ' '), CHAR(9), ' '), 100) AS comment
FROM research.prof_rev_reverted_edits re
WHERE re.revert_rank = 1
ORDER BY re.survival_sec DESC, re.rev_id ASC
LIMIT 20;

-- Q3: Top 10 longest-surviving single-edit non-blanking reverted edits (run_len = 1, p_bytes > 100)
SELECT re.rev_id,
       CONCAT('https://fi.wikipedia.org/w/index.php?diff=', re.rev_id) AS edit_diff_link,
       re.r_rev_id,
       re.title AS article,
       COALESCE(re.e_user, '(null)') AS user,
       ROUND(re.survival_sec / (86400.0 * 365.25), 2) AS survival_years,
       LEFT(REPLACE(REPLACE(COALESCE(re.e_comment, ''), CHAR(10), ' '), CHAR(9), ' '), 80) AS e_comment,
       LEFT(REPLACE(REPLACE(COALESCE(re.r_comment, ''), CHAR(10), ' '), CHAR(9), ' '), 80) AS r_comment
FROM research.prof_rev_reverted_edits re
JOIN research.prof_rev_reverts rv ON rv.page_id = re.page_id AND rv.r_rev_id = re.r_rev_id
WHERE re.revert_rank = 1 AND rv.run_len = 1 AND rv.p_bytes > 100
ORDER BY re.survival_sec DESC, re.rev_id ASC
LIMIT 10;

-- Q7: S <-> M join counts, missing counts on each side, and snapshot timestamps
SELECT
  (SELECT COUNT(*) FROM research.prof_rev_pages) AS m_distinct_pages,
  (SELECT COUNT(*) FROM corpus.article WHERE lang = 'fi') AS s_fi_pages,
  (SELECT COUNT(*) FROM research.prof_rev_pages p JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = p.page_id) AS joined_pages,
  (SELECT COUNT(*) FROM research.prof_rev_pages p LEFT JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = p.page_id WHERE a.page_id IS NULL) AS in_m_missing_in_s,
  (SELECT COUNT(*) FROM corpus.article a LEFT JOIN research.prof_rev_pages p ON p.page_id = a.page_id WHERE a.lang = 'fi' AND p.page_id IS NULL) AS in_s_missing_in_m,
  (SELECT MAX(date_modified) FROM corpus.article WHERE lang = 'fi') AS s_fi_max_date_modified,
  (SELECT MAX(last_ts) FROM research.prof_rev_pages) AS m_max_ts,
  (SELECT COUNT(*) FROM research.prof_rev_pages p LEFT JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = p.page_id WHERE a.page_id IS NULL AND p.first_ts > '2025-08-20 23:00:44') AS m_only_created_after_s_snapshot;

-- Q7: Title agreement on joined pages (case/accent-insensitive vs exact UTF-8 binary)
SELECT
  COUNT(*) AS joined_pages,
  SUM(p.title <> a.title) AS title_mismatches_ci,
  SUM(BINARY p.title <> BINARY a.title) AS title_mismatches_exact,
  SUM(p.latest_rev_id <> a.rev_id) AS latest_rev_id_mismatches
FROM research.prof_rev_pages p
JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = p.page_id;

-- Q7: 10 example pages in revision (M) missing from article (S, lang='fi')
SELECT p.page_id, p.title, p.rev_count, p.latest_bytes, DATE(p.first_ts) AS first_edit, DATE(p.last_ts) AS last_edit
FROM research.prof_rev_pages p
LEFT JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = p.page_id
WHERE a.page_id IS NULL
ORDER BY p.page_id ASC
LIMIT 10;

-- Q7: Example pages in article (S, lang='fi') missing from revision (M) — returns 0 rows
SELECT a.page_id, a.title
FROM corpus.article a
LEFT JOIN research.prof_rev_pages p ON p.page_id = a.page_id
WHERE a.lang = 'fi' AND p.page_id IS NULL
ORDER BY a.page_id ASC
LIMIT 10;

-- Q7: Sample title mismatches on joined pages (moves between 2025-08-20 S snapshot and 2026-10-01 M snapshot)
SELECT p.page_id, a.title AS article_s_title, p.title AS revision_m_title, a.rev_id AS s_rev_id, p.latest_rev_id AS m_latest_rev_id
FROM research.prof_rev_pages p
JOIN corpus.article a ON a.lang = 'fi' AND a.page_id = p.page_id
WHERE BINARY p.title <> BINARY a.title
ORDER BY p.page_id ASC
LIMIT 10;

-- Q8: Typical wikitext size from revision.bytes (latest revision per page: percentiles)
SELECT DISTINCT
  COUNT(*) OVER () AS pages,
  ROUND(AVG(latest_bytes) OVER (), 1) AS mean_bytes,
  MIN(latest_bytes) OVER () AS min_bytes,
  ROUND(PERCENTILE_CONT(0.10) WITHIN GROUP (ORDER BY latest_bytes) OVER (), 1) AS p10_bytes,
  ROUND(PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY latest_bytes) OVER (), 1) AS p25_bytes,
  ROUND(PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY latest_bytes) OVER (), 1) AS median_bytes,
  ROUND(PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY latest_bytes) OVER (), 1) AS p75_bytes,
  ROUND(PERCENTILE_CONT(0.90) WITHIN GROUP (ORDER BY latest_bytes) OVER (), 1) AS p90_bytes,
  ROUND(PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY latest_bytes) OVER (), 1) AS p99_bytes,
  MAX(latest_bytes) OVER () AS max_bytes
FROM research.prof_rev_pages;

-- Q8: 200-page random sample (PK lookup on revision_text only) — <ref share, median <ref count, and dispute templates
-- (See also research/profiling/prof_rev/analyze_q8_sample.py for full Top-30 template parsing on this sample.)
WITH sample_texts AS (
  SELECT s.page_id,
         s.rev_id,
         s.title,
         REGEXP_REPLACE(rt.text, '(?is)<!--.*?-->|<nowiki>.*?</nowiki>', '') AS clean_text,
         CAST((CHAR_LENGTH(rt.text) - CHAR_LENGTH(REGEXP_REPLACE(rt.text, '(?i)<ref(?![a-z])', ''))) / 4 AS UNSIGNED) AS ref_count
  FROM research.prof_rev_q8_sample s
  JOIN corpus.revision_text rt ON rt.page_id = s.page_id AND rt.rev_id = s.rev_id
)
SELECT
  COUNT(*) AS sample_pages,
  SUM(ref_count > 0) AS pages_with_ref,
  ROUND(100.0 * SUM(ref_count > 0) / COUNT(*), 2) AS pct_with_ref,
  (SELECT ROUND(MEDIAN(ref_count) OVER (), 1) FROM sample_texts LIMIT 1) AS median_ref_count,
  (SELECT ROUND(MEDIAN(ref_count) OVER (), 1) FROM sample_texts WHERE ref_count > 0 LIMIT 1) AS median_ref_citing_only,
  ROUND(AVG(ref_count), 2) AS mean_ref_count,
  MAX(ref_count) AS max_ref_count,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*neutraalius') AS tpl_neutraalius,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*lähteetön') AS tpl_lahteeton,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*lähde[?]') AS tpl_lahde_question_mark,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*lähde[[:space:]]*[|}]') AS tpl_lahde_actual,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*korjattava') AS tpl_korjattava,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*pov(?![a-z])') AS tpl_pov,
  SUM(clean_text REGEXP '(?i)[{][{][[:space:]]*kiistanalainen') AS tpl_kiistanalainen
FROM sample_texts;
