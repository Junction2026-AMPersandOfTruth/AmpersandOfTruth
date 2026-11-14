-- =============================================================================
-- Issue #17, Part C (prof_top): Demo-topic shortlist & contested-narrative check
-- Runnable as-is against `corpus` (`mariadb -h127.0.0.1 -P3307 -ucorpus_ro corpus < research/profiling/prof_top/queries.sql`)
-- Note: Never scans `revision_text`. PK-only `revision_text` fetches (284 <= 300)
--       are executed with a hard guard in `profile_qr_qt.py`.
-- =============================================================================

-- Q1: Walk `category_link` recursively from 'Suomen historia' and report category count, max depth, and fi article count.
WITH RECURSIVE tree AS (
    SELECT CAST('Suomen historia' AS VARCHAR(255)) AS cat, 0 AS depth
    UNION
    SELECT cl.child, t.depth + 1
    FROM category_link cl
    JOIN tree t ON cl.parent = t.cat
)
SELECT
    COUNT(*) AS distinct_categories,
    MAX(depth) AS max_depth,
    (SELECT COUNT(DISTINCT ac.page_id)
     FROM article_category ac
     JOIN tree t2 ON ac.category = t2.cat
     WHERE ac.lang = 'fi') AS fi_pages_in_tree,
    (SELECT COUNT(*) FROM article WHERE lang = 'fi') AS total_fi_articles,
    (SELECT COUNT(DISTINCT page_id) FROM revision) AS distinct_pages_in_revision
FROM tree;

-- Q2: Materialize `research.prof_top_scope_pages` (19,285 tree pages + 15 political/historical pages outside `article_category`).
CREATE TABLE IF NOT EXISTS research.prof_top_scope_pages (
    page_id INT UNSIGNED NOT NULL PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    in_tree TINYINT(1) NOT NULL,
    outside_political_hist TINYINT(1) NOT NULL,
    KEY idx_in_tree (in_tree)
) AS
WITH RECURSIVE tree AS (
    SELECT CAST('Suomen historia' AS VARCHAR(255)) AS cat
    UNION
    SELECT cl.child
    FROM category_link cl
    JOIN tree t ON cl.parent = t.cat
),
tree_pages AS (
    SELECT DISTINCT a.page_id, a.title
    FROM article a
    JOIN article_category ac ON ac.lang = a.lang AND ac.page_id = a.page_id
    JOIN tree t ON t.cat = ac.category
    WHERE a.lang = 'fi'
),
outside_pages AS (
    SELECT r.page_id, MAX(r.title) AS title
    FROM revision r
    LEFT JOIN tree_pages tp ON tp.page_id = r.page_id
    WHERE tp.page_id IS NULL
      AND r.page_id IN (
          8957, 132582, 197500, 386197, 538512, 718858, 1264726,
          1358914, 1358920, 1677257, 1873813, 1882142, 1900176, 1906024, 1919970
      )
    GROUP BY r.page_id
)
SELECT page_id, title, 1 AS in_tree, 0 AS outside_political_hist FROM tree_pages
UNION ALL
SELECT page_id, title, 0 AS in_tree, 1 AS outside_political_hist FROM outside_pages;

-- Q3: Materialize canonical identity reverts `research.prof_top_reverts` (latest earlier P with P.sha1 = R.sha1 and P_seq < R_seq - 1).
CREATE TABLE IF NOT EXISTS research.prof_top_reverts (
    page_id INT UNSIGNED NOT NULL,
    r_rev_id INT UNSIGNED NOT NULL,
    title VARCHAR(255) NOT NULL,
    in_tree TINYINT(1) NOT NULL,
    in_scope TINYINT(1) NOT NULL,
    p_rev_id INT UNSIGNED NOT NULL,
    p_seq INT UNSIGNED NOT NULL,
    p_ts DATETIME NOT NULL,
    p_bytes INT UNSIGNED NOT NULL,
    last_edit_rev_id INT UNSIGNED NOT NULL,
    last_edit_seq INT UNSIGNED NOT NULL,
    last_edit_ts DATETIME NOT NULL,
    last_edit_user VARCHAR(255) DEFAULT NULL,
    last_edit_anon TINYINT(1) NOT NULL,
    last_edit_comment VARCHAR(1000) DEFAULT NULL,
    last_edit_bytes INT UNSIGNED NOT NULL,
    r_seq INT UNSIGNED NOT NULL,
    r_ts DATETIME NOT NULL,
    r_user VARCHAR(255) DEFAULT NULL,
    r_anon TINYINT(1) NOT NULL,
    r_comment VARCHAR(1000) DEFAULT NULL,
    r_bytes INT UNSIGNED NOT NULL,
    sha1 CHAR(31) CHARACTER SET ascii NOT NULL,
    run_len INT UNSIGNED NOT NULL,
    net_bytes_added INT NOT NULL,
    PRIMARY KEY (page_id, r_rev_id),
    KEY idx_page_seqs (page_id, p_seq, r_seq),
    KEY idx_scope_bytes (in_scope, net_bytes_added)
) AS
WITH ordered AS (
    SELECT
        page_id, rev_id, title, ts, user, anonymous, comment, sha1, bytes,
        ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq,
        LAG( rev_id ) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_rev_id,
        LAG( ts ) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_ts,
        LAG( user ) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_user,
        LAG( anonymous ) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_anon,
        LAG( comment ) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_comment,
        LAG( bytes ) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_bytes,
        LAG( seq_marker ) OVER (PARTITION BY page_id, sha1 ORDER BY ts, rev_id) AS prev_same_sha1_seq
    FROM (
        SELECT page_id, rev_id, title, ts, user, anonymous, comment, sha1, bytes,
               ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq_marker
        FROM revision
    ) base
)
SELECT
    o.page_id,
    o.rev_id AS r_rev_id,
    COALESCE(sp.title, o.title) AS title,
    COALESCE(sp.in_tree, 0) AS in_tree,
    CASE WHEN sp.page_id IS NOT NULL THEN 1 ELSE 0 END AS in_scope,
    p.rev_id AS p_rev_id,
    p.seq_marker AS p_seq,
    p.ts AS p_ts,
    p.bytes AS p_bytes,
    o.prev_rev_id AS last_edit_rev_id,
    (o.seq - 1) AS last_edit_seq,
    o.prev_ts AS last_edit_ts,
    o.prev_user AS last_edit_user,
    o.prev_anon AS last_edit_anon,
    o.prev_comment AS last_edit_comment,
    o.prev_bytes AS last_edit_bytes,
    o.seq AS r_seq,
    o.ts AS r_ts,
    o.user AS r_user,
    o.anonymous AS r_anon,
    o.comment AS r_comment,
    o.bytes AS r_bytes,
    o.sha1,
    (o.seq - p.seq_marker - 1) AS run_len,
    CAST(o.prev_bytes AS SIGNED) - CAST(p.bytes AS SIGNED) AS net_bytes_added
FROM ordered o
JOIN (
    SELECT page_id, rev_id, ts, bytes,
           ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq_marker
    FROM revision
) p ON p.page_id = o.page_id AND p.seq_marker = o.prev_same_sha1_seq
LEFT JOIN research.prof_top_scope_pages sp ON sp.page_id = o.page_id
WHERE o.sha1 IS NOT NULL
  AND o.prev_same_sha1_seq IS NOT NULL
  AND o.prev_same_sha1_seq < o.seq - 1;

-- Q4: Materialize reverted edits `research.prof_top_reverted_edits` (revisions strictly between P and R, survival = first reverting R.ts - edit.ts).
CREATE TABLE IF NOT EXISTS research.prof_top_reverted_edits (
    page_id INT UNSIGNED NOT NULL,
    rev_id INT UNSIGNED NOT NULL,
    seq INT UNSIGNED NOT NULL,
    ts DATETIME NOT NULL,
    user VARCHAR(255) DEFAULT NULL,
    anonymous TINYINT(1) NOT NULL,
    bytes INT UNSIGNED NOT NULL,
    in_tree TINYINT(1) NOT NULL,
    in_scope TINYINT(1) NOT NULL,
    first_r_rev_id INT UNSIGNED NOT NULL,
    first_r_seq INT UNSIGNED NOT NULL,
    first_r_ts DATETIME NOT NULL,
    first_r_bytes INT UNSIGNED NOT NULL,
    survival_sec BIGINT NOT NULL,
    PRIMARY KEY (page_id, rev_id),
    KEY idx_scope (in_scope)
) AS
WITH ordered_revs AS (
    SELECT
        page_id, rev_id, ts, user, anonymous, bytes,
        ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq
    FROM revision
),
matched AS (
    SELECT
        e.page_id, e.rev_id, e.seq, e.ts, e.user, e.anonymous, e.bytes,
        rv.in_tree, rv.in_scope,
        rv.r_rev_id, rv.r_seq, rv.r_ts, rv.r_bytes,
        ROW_NUMBER() OVER (PARTITION BY e.page_id, e.rev_id ORDER BY rv.r_seq) AS rn
    FROM ordered_revs e
    JOIN research.prof_top_reverts rv
      ON rv.page_id = e.page_id
     AND e.seq > rv.p_seq
     AND e.seq < rv.r_seq
)
SELECT
    page_id,
    rev_id,
    seq,
    ts,
    user,
    anonymous,
    bytes,
    in_tree,
    in_scope,
    r_rev_id AS first_r_rev_id,
    r_seq AS first_r_seq,
    r_ts AS first_r_ts,
    r_bytes AS first_r_bytes,
    TIMESTAMPDIFF(SECOND, ts, r_ts) AS survival_sec
FROM matched
WHERE rn = 1;

-- Q5: Baseline scope, revert, reverted-edit, and Part-A pitfall filter counts reported in Section 1.
SELECT
    COUNT(*) AS scope_pages_total,
    SUM(in_tree = 1) AS tree_pages,
    SUM(outside_political_hist = 1) AS outside_political_hist_pages
FROM research.prof_top_scope_pages;

SELECT
    COUNT(*) AS total_reverts_corpus,
    COUNT(DISTINCT page_id) AS corpus_pages_with_reverts,
    SUM(in_tree = 1) AS reverts_in_tree,
    COUNT(DISTINCT CASE WHEN in_tree = 1 THEN page_id END) AS tree_pages_with_reverts,
    SUM(in_scope = 1) AS reverts_in_scope,
    COUNT(DISTINCT CASE WHEN in_scope = 1 THEN page_id END) AS scope_pages_with_reverts,
    SUM(in_scope = 1 AND net_bytes_added >= 50) AS reverts_in_scope_ge50b,
    SUM(in_scope = 1
        AND r_bytes > 0
        AND NOT (r_user IS NOT NULL AND last_edit_user IS NOT NULL AND r_user = last_edit_user)
        AND (r_user IS NULL OR LOWER(r_user) NOT LIKE '%bot%')) AS reverts_in_scope_clean,
    SUM(in_scope = 1
        AND net_bytes_added >= 50
        AND r_bytes > 0
        AND NOT (r_user IS NOT NULL AND last_edit_user IS NOT NULL AND r_user = last_edit_user)
        AND (r_user IS NULL OR LOWER(r_user) NOT LIKE '%bot%')) AS reverts_in_scope_ge50b_clean,
    SUM(in_scope = 1 AND r_bytes = 0) AS pitfall_zero_byte_r,
    SUM(in_scope = 1 AND r_user IS NOT NULL AND last_edit_user IS NOT NULL AND r_user = last_edit_user) AS pitfall_self_revert,
    SUM(in_scope = 1 AND r_user IS NOT NULL AND LOWER(r_user) LIKE '%bot%') AS pitfall_bot_reverter
FROM research.prof_top_reverts;

SELECT
    COUNT(*) AS total_reverted_edits_corpus,
    SUM(in_tree = 1) AS reverted_edits_in_tree,
    SUM(in_scope = 1) AS reverted_edits_in_scope
FROM research.prof_top_reverted_edits;

-- Q6: Deterministic pool of 12,878 substantive reverted runs (`in_scope = 1 AND net_bytes_added >= 50`)
--     from which `profile_qr_qt.py` draws `random.Random(42).sample(rows, 100)` (count verification here).
SELECT COUNT(*) AS qr_eligible_runs
FROM research.prof_top_reverts
WHERE in_scope = 1
  AND net_bytes_added >= 50;

-- Q7: Top scope articles ranked by pitfall-filtered substantive (`>= 50` B) reverts (excluding R.bytes=0, self-reverts, *bot* reverters).
SELECT
    page_id,
    MAX(title) AS title,
    COUNT(*) AS clean_reverts,
    SUM(net_bytes_added >= 50) AS clean_ge50b_reverts
FROM research.prof_top_reverts
WHERE in_scope = 1
  AND r_bytes > 0
  AND NOT (r_user IS NOT NULL AND last_edit_user IS NOT NULL AND r_user = last_edit_user)
  AND (r_user IS NULL OR LOWER(r_user) NOT LIKE '%bot%')
GROUP BY page_id
ORDER BY clean_ge50b_reverts DESC, clean_reverts DESC
LIMIT 40;

-- Q8: Per-article pitfall-filtered revert counts (`clean_reverts` and `clean_ge50b`) for all Q-T shortlist articles in Section 3.
SELECT
    page_id,
    MAX(title) AS title,
    COUNT(*) AS clean_reverts,
    SUM(net_bytes_added >= 50) AS clean_ge50b
FROM research.prof_top_reverts
WHERE in_scope = 1
  AND r_bytes > 0
  AND NOT (r_user IS NOT NULL AND last_edit_user IS NOT NULL AND r_user = last_edit_user)
  AND (r_user IS NULL OR LOWER(r_user) NOT LIKE '%bot%')
  AND title IN (
      'Suomen sisällissota', 'Carl Gustaf Emil Mannerheim', 'Punakaarti', 'Suojeluskunta', 'Suomen sisällissodan nimet',
      'Jatkosota', 'Risto Ryti', 'Sotasyyllisyysoikeudenkäynti', 'Itä-Karjalan keskitysleirit',
      'Suomalainen Waffen-SS-vapaaehtoispataljoona', 'Naarajärven sotavankileiri',
      'Talvisota', 'Simo Häyhä', 'Moskovan rauhansopimus (1940)', 'Mainilan laukaukset',
      'Karjala', 'Suur-Suomi', 'Viipuri', 'Karjala-kysymys', 'Käkisalmi', 'Luovutettu Karjala',
      'Sauli Niinistö', 'Luettelo Suomen historian sodista', 'Suomen Nato-keskustelu',
      'Tarja Halonen', 'Urho Kekkonen', 'Paavo Lipponen', 'Suomi–Neuvostoliitto-Seura', 'Venäläisvastaisuus', 'Suomettuminen',
      'Axel Olof Freudenthal', 'Suomalaisuuden liitto', 'Fennomania', 'Ahvenanmaan kysymys', 'Jomalan kirkko',
      'Saamelaiset', 'Suomen muinaiset kuninkaat', 'Suomen esihistoria', 'Saamelaisten historia', 'Metsäsaamelaiset',
      'Timo Soini', 'Suomen Kansa Ensin', 'Lapuanliike', 'Marco de Wit', 'Ano Turtiainen', 'Isänmaallinen kansanliike',
      'Alexander Stubb', 'Matti Vanhanen', 'Sanna Marin', 'Jyrki Katainen', 'Juha Sipilä', 'Suvi Lindén', 'Osmo Soininvaara'
  )
GROUP BY page_id
ORDER BY title;
