-- P4.6 (#66) Hot-spots dashboard precompute.
-- Idempotent: drops and recreates research.hotspot_{headline,article,month,category}.
-- Reads only precomputed P1 tables (research.s1_*, research.s4_*) and indexed corpus tables;
-- never touches corpus.revision_text. Every statement runs well under the 60 s per-query budget.

SET SESSION max_statement_time = 55;

-- 1. Single-row corpus & dispute headline summary.
DROP TABLE IF EXISTS research.hotspot_headline;
CREATE TABLE research.hotspot_headline AS
SELECT
    CAST(ps_tot.revisions AS UNSIGNED) AS revisions,
    CAST(ps_tot.articles AS UNSIGNED) AS articles,
    CAST(ps_tot.reverts AS UNSIGNED) AS reverts,
    CAST(ep_tot.episodes AS UNSIGNED) AS episodes,
    ROUND(COALESCE(re_tot.anon_reverted / NULLIF(re_tot.total_reverted, 0), 0.0), 4) AS anon_reverted_share
FROM (
    SELECT
        SUM(revision_count) AS revisions,
        COUNT(*) AS articles,
        SUM(signal_total) AS reverts
    FROM research.s1_page_stats
) ps_tot
CROSS JOIN (
    SELECT COUNT(*) AS episodes
    FROM research.s4_episode
    WHERE is_default = 1
) ep_tot
CROSS JOIN (
    SELECT
        COUNT(*) AS total_reverted,
        SUM(CASE WHEN re.anonymous <> 0 THEN 1 ELSE 0 END) AS anon_reverted
    FROM research.s1_reverted_edit re
    JOIN research.s1_revert r ON r.reverting_rev_id = re.reverting_rev_id
    WHERE r.class NOT IN ('excluded', 'bot', 'self_revert')
) re_tot;

-- 2. Top 200 Finnish articles by S4 contestedness score, with S1 class-group breakdown,
--    anonymous reverted-edit share, revision span, English sibling title, and 24-year revert sparkline.
DROP TABLE IF EXISTS research.hotspot_article;
CREATE TABLE research.hotspot_article (
    PRIMARY KEY (page_id),
    INDEX idx_contest_score (contest_score)
) AS
WITH RECURSIVE yrs AS (
    SELECT 2003 AS yr
    UNION ALL
    SELECT yr + 1 FROM yrs WHERE yr < 2026
),
top200 AS (
    SELECT
        pc.page_id,
        pc.contested_score AS contest_score,
        (pc.episodes_edit_war + pc.episodes_ping_pong + pc.episodes_burst) AS episodes,
        ps.title AS rev_title,
        ps.signal_total AS reverts,
        (ps.edit_war_count + ps.dispute_count) AS reverts_dispute,
        ps.graffiti_cleanup_count AS reverts_vandalism,
        ps.other_count AS reverts_other
    FROM research.s4_page_contest pc
    JOIN research.s1_page_stats ps ON ps.page_id = pc.page_id
    ORDER BY pc.contested_score DESC, ps.signal_total DESC, pc.page_id ASC
    LIMIT 200
),
rev_span AS (
    SELECT
        r.page_id,
        MIN(r.ts) AS first_ts,
        MAX(r.ts) AS last_ts
    FROM corpus.revision r
    JOIN top200 t ON t.page_id = r.page_id
    GROUP BY r.page_id
),
anon_stats AS (
    SELECT
        re.page_id,
        SUM(CASE WHEN re.anonymous <> 0 THEN 1 ELSE 0 END) / COUNT(*) AS anon_share
    FROM research.s1_reverted_edit re
    JOIN research.s1_revert rv ON rv.reverting_rev_id = re.reverting_rev_id
    JOIN top200 t ON t.page_id = re.page_id
    WHERE rv.class NOT IN ('excluded', 'bot', 'self_revert')
    GROUP BY re.page_id
),
yr_revs AS (
    SELECT
        rv.page_id,
        YEAR(rv.revert_ts) AS yr,
        COUNT(*) AS cnt
    FROM research.s1_revert rv
    JOIN top200 t ON t.page_id = rv.page_id
    WHERE rv.class NOT IN ('excluded', 'bot', 'self_revert')
      AND YEAR(rv.revert_ts) BETWEEN 2003 AND 2026
    GROUP BY rv.page_id, YEAR(rv.revert_ts)
),
spark AS (
    SELECT
        t.page_id,
        GROUP_CONCAT(COALESCE(yr_revs.cnt, 0) ORDER BY yrs.yr SEPARATOR ',') AS yearly_reverts
    FROM top200 t
    CROSS JOIN yrs
    LEFT JOIN yr_revs ON yr_revs.page_id = t.page_id AND yr_revs.yr = yrs.yr
    GROUP BY t.page_id
)
SELECT
    CAST(t.page_id AS UNSIGNED) AS page_id,
    CAST(COALESCE(afi.title, t.rev_title) AS CHAR(255)) AS title_fi,
    CAST(
        (SELECT aen.title
         FROM corpus.article aen
         WHERE aen.lang = 'en'
           AND afi.wikidata_id IS NOT NULL
           AND afi.wikidata_id <> ''
           AND aen.wikidata_id = afi.wikidata_id
         LIMIT 1) AS CHAR(255)
    ) AS title_en,
    ROUND(t.contest_score, 4) AS contest_score,
    CAST(t.reverts AS UNSIGNED) AS reverts,
    CAST(t.reverts_dispute AS UNSIGNED) AS reverts_dispute,
    CAST(t.reverts_vandalism AS UNSIGNED) AS reverts_vandalism,
    CAST(t.reverts_other AS UNSIGNED) AS reverts_other,
    CAST(t.episodes AS UNSIGNED) AS episodes,
    ROUND(COALESCE(an.anon_share, 0.0), 4) AS anon_share,
    rs.first_ts,
    rs.last_ts,
    CAST(sp.yearly_reverts AS CHAR(255)) AS yearly_reverts
FROM top200 t
JOIN rev_span rs ON rs.page_id = t.page_id
JOIN spark sp ON sp.page_id = t.page_id
LEFT JOIN anon_stats an ON an.page_id = t.page_id
LEFT JOIN corpus.article afi ON afi.lang = 'fi' AND afi.page_id = t.page_id
ORDER BY t.contest_score DESC, t.reverts DESC, t.page_id ASC;

-- 3. Monthly activity series (2003-01 .. 2026-12 = 288 months) with top episode-driving article.
DROP TABLE IF EXISTS research.hotspot_month;
CREATE TABLE research.hotspot_month (
    PRIMARY KEY (`month`)
) AS
WITH RECURSIVE months AS (
    SELECT DATE('2003-01-01') AS m_start
    UNION ALL
    SELECT DATE_ADD(m_start, INTERVAL 1 MONTH)
    FROM months
    WHERE m_start < DATE('2026-12-01')
),
edit_mo AS (
    SELECT DATE_FORMAT(ts, '%Y-%m') AS ym, COUNT(*) AS edits
    FROM corpus.revision
    GROUP BY ym
),
rev_mo AS (
    SELECT DATE_FORMAT(revert_ts, '%Y-%m') AS ym, COUNT(*) AS reverts
    FROM research.s1_revert
    WHERE class NOT IN ('excluded', 'bot', 'self_revert')
    GROUP BY ym
),
ep_mo AS (
    SELECT DATE_FORMAT(start_ts, '%Y-%m') AS ym, COUNT(*) AS episodes
    FROM research.s4_episode
    WHERE is_default = 1
    GROUP BY ym
),
ep_top AS (
    SELECT
        DATE_FORMAT(e.start_ts, '%Y-%m') AS ym,
        e.page_id,
        ROW_NUMBER() OVER (
            PARTITION BY DATE_FORMAT(e.start_ts, '%Y-%m')
            ORDER BY (
                MAX(CASE
                    WHEN e.kind IN ('edit_war', 'ping_pong') OR e.revert_count > 0 THEN e.score
                    ELSE 0.25 * e.score
                END) + 1.5 * LN(1 + COUNT(*))
            ) DESC,
            MAX(e.revert_count) DESC,
            COUNT(*) DESC,
            e.page_id ASC
        ) AS rn
    FROM research.s4_episode e
    WHERE e.is_default = 1
    GROUP BY ym, e.page_id
)
SELECT
    CAST(DATE_FORMAT(m.m_start, '%Y-%m') AS CHAR(7)) AS `month`,
    CAST(COALESCE(rm.reverts, 0) AS UNSIGNED) AS reverts,
    CAST(COALESCE(em.episodes, 0) AS UNSIGNED) AS episodes,
    CAST(COALESCE(edm.edits, 0) AS UNSIGNED) AS edits,
    CAST(et.page_id AS UNSIGNED) AS top_page_id,
    CAST(COALESCE(afi.title, ps.title) AS CHAR(255)) AS top_title_fi,
    CAST(
        (SELECT aen.title
         FROM corpus.article aen
         WHERE aen.lang = 'en'
           AND afi.wikidata_id IS NOT NULL
           AND afi.wikidata_id <> ''
           AND aen.wikidata_id = afi.wikidata_id
         LIMIT 1) AS CHAR(255)
    ) AS top_title_en
FROM months m
LEFT JOIN rev_mo rm ON rm.ym = DATE_FORMAT(m.m_start, '%Y-%m')
LEFT JOIN ep_mo em ON em.ym = DATE_FORMAT(m.m_start, '%Y-%m')
LEFT JOIN edit_mo edm ON edm.ym = DATE_FORMAT(m.m_start, '%Y-%m')
LEFT JOIN ep_top et ON et.ym = DATE_FORMAT(m.m_start, '%Y-%m') AND et.rn = 1
LEFT JOIN research.s1_page_stats ps ON ps.page_id = et.page_id
LEFT JOIN corpus.article afi ON afi.lang = 'fi' AND afi.page_id = et.page_id
ORDER BY `month` ASC;

-- 4. Top 50 thematic categories by aggregate dispute activity (Finnish corpus pages only;
--    skips maintenance/hidden/list/meta categories and < 3-page eponymous categories).
DROP TABLE IF EXISTS research.hotspot_category;
CREATE TABLE research.hotspot_category (
    PRIMARY KEY (category),
    INDEX idx_contest_score (contest_score)
) AS
SELECT
    CAST(ac.category AS CHAR(255)) AS category,
    CAST(COUNT(DISTINCT ac.page_id) AS UNSIGNED) AS articles,
    ROUND(SUM(pc.contested_score), 4) AS contest_score,
    CAST(SUM(ps.signal_total) AS UNSIGNED) AS reverts,
    CAST(SUM(ps.edit_war_count + ps.dispute_count) AS UNSIGNED) AS dispute_reverts,
    CAST(SUM(pc.episodes_edit_war + pc.episodes_ping_pong + pc.episodes_burst) AS UNSIGNED) AS episodes
FROM corpus.article_category ac
JOIN research.s4_page_contest pc ON pc.page_id = ac.page_id
JOIN research.s1_page_stats ps ON ps.page_id = ac.page_id
WHERE ac.lang = 'fi'
  AND ac.category NOT LIKE '%:%'
  AND NOT LOWER(ac.category) REGEXP '(^luettelot |tynkä|tyngät|korjattav|tarkistettav|lähteettöm|viitteettöm|ylläpito|piilotet|wikipedia|wikidata|malline|artikkelit|sivut|täsmenny|ohjaukset)'
GROUP BY ac.category
HAVING COUNT(DISTINCT ac.page_id) >= 3
ORDER BY contest_score DESC, dispute_reverts DESC, ac.category ASC
LIMIT 50;
