SET NAMES utf8mb4;
SET sql_mode = CONCAT_WS(',', @@sql_mode, 'NO_BACKSLASH_ESCAPES');

-- Setup: Materialise canonical identity reverts (research.prof_ed_reverts)
-- and reverted edits strictly between P and R (research.prof_ed_reverted_edits).
-- Canonical definition: order a page's revisions by (ts, rev_id). R is an identity revert
-- if some earlier P on the same page has P.sha1 = R.sha1 (non-null) and P is not R's
-- immediate predecessor; take the latest such P. Revisions strictly between P and R are reverted edits.
CREATE OR REPLACE TABLE research.prof_ed_reverts (
  page_id INT UNSIGNED NOT NULL,
  title VARCHAR(255) NOT NULL,
  r_rev_id INT UNSIGNED NOT NULL,
  r_seq INT UNSIGNED NOT NULL,
  r_ts DATETIME NOT NULL,
  r_user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  r_anon TINYINT(1) NOT NULL,
  r_bytes INT UNSIGNED NOT NULL,
  r_comment VARCHAR(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_as_ci DEFAULT NULL,
  r_sha1 CHAR(31) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  p_rev_id INT UNSIGNED NOT NULL,
  p_seq INT UNSIGNED NOT NULL,
  p_ts DATETIME NOT NULL,
  span INT UNSIGNED NOT NULL,
  PRIMARY KEY (page_id, r_rev_id),
  KEY idx_r_rev (r_rev_id),
  KEY idx_page_span (page_id, p_seq, r_seq),
  KEY idx_r_user (r_user)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
AS
WITH ordered AS (
  SELECT page_id, rev_id, title, ts, user, anonymous, bytes, comment, sha1,
         ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq
  FROM corpus.revision
),
with_p AS (
  SELECT page_id, title,
         rev_id AS r_rev_id, seq AS r_seq, ts AS r_ts,
         user AS r_user, anonymous AS r_anon, bytes AS r_bytes,
         comment AS r_comment, sha1 AS r_sha1,
         LAG(rev_id) OVER (PARTITION BY page_id, sha1 ORDER BY seq) AS p_rev_id,
         LAG(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq) AS p_seq,
         LAG(ts) OVER (PARTITION BY page_id, sha1 ORDER BY seq) AS p_ts
  FROM ordered
  WHERE sha1 IS NOT NULL
)
SELECT page_id, title, r_rev_id, r_seq, r_ts, r_user, r_anon, r_bytes, r_comment, r_sha1,
       p_rev_id, p_seq, p_ts, (r_seq - p_seq - 1) AS span
FROM with_p
WHERE p_seq IS NOT NULL AND p_seq < r_seq - 1;

CREATE OR REPLACE TABLE research.prof_ed_reverted_edits (
  page_id INT UNSIGNED NOT NULL,
  title VARCHAR(255) NOT NULL,
  r_rev_id INT UNSIGNED NOT NULL,
  r_seq INT UNSIGNED NOT NULL,
  r_ts DATETIME NOT NULL,
  r_user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  r_anon TINYINT(1) NOT NULL,
  r_bytes INT UNSIGNED NOT NULL,
  p_rev_id INT UNSIGNED NOT NULL,
  p_seq INT UNSIGNED NOT NULL,
  span INT UNSIGNED NOT NULL,
  e_rev_id INT UNSIGNED NOT NULL,
  e_seq INT UNSIGNED NOT NULL,
  e_ts DATETIME NOT NULL,
  e_user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  e_anon TINYINT(1) NOT NULL,
  e_bytes INT UNSIGNED NOT NULL,
  survival_sec BIGINT NOT NULL,
  is_first_revert TINYINT(1) NOT NULL,
  PRIMARY KEY (page_id, r_rev_id, e_rev_id),
  KEY idx_e_rev (e_rev_id),
  KEY idx_e_user (e_user, is_first_revert),
  KEY idx_r_user (r_user)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
AS
WITH ordered AS (
  SELECT page_id, rev_id, ts, user, anonymous, bytes,
         ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq
  FROM corpus.revision
),
pairs AS (
  SELECT r.page_id, r.title,
         r.r_rev_id, r.r_seq, r.r_ts, r.r_user, r.r_anon, r.r_bytes,
         r.p_rev_id, r.p_seq, r.span,
         e.rev_id AS e_rev_id, e.seq AS e_seq, e.ts AS e_ts,
         e.user AS e_user, e.anonymous AS e_anon, e.bytes AS e_bytes,
         TIMESTAMPDIFF(SECOND, e.ts, r.r_ts) AS survival_sec,
         IF(r.r_seq = MIN(r.r_seq) OVER (PARTITION BY r.page_id, e.rev_id), 1, 0) AS is_first_revert
  FROM research.prof_ed_reverts r
  JOIN ordered e ON e.page_id = r.page_id AND e.seq > r.p_seq AND e.seq < r.r_seq
)
SELECT * FROM pairs;

-- Setup summary: global counts of revisions, pages, canonical reverts, and blanking/large-span edge cases
SELECT
  (SELECT COUNT(*) FROM corpus.revision) AS total_revisions,
  (SELECT COUNT(DISTINCT page_id) FROM corpus.revision) AS total_pages,
  (SELECT COUNT(*) FROM corpus.revision WHERE sha1 IS NULL) AS sha1_null_revisions,
  (SELECT COUNT(*) FROM research.prof_ed_reverts) AS canonical_reverts,
  (SELECT COUNT(*) FROM research.prof_ed_reverted_edits) AS reverted_edit_pairs,
  (SELECT SUM(is_first_revert) FROM research.prof_ed_reverted_edits) AS distinct_reverted_revisions,
  (SELECT SUM(r_bytes = 0) FROM research.prof_ed_reverts) AS blanking_reverts,
  (SELECT SUM(is_first_revert AND r_bytes = 0) FROM research.prof_ed_reverted_edits) AS distinct_edits_from_blanking_reverts,
  (SELECT SUM(r_bytes = 0) FROM research.prof_ed_reverted_edits) AS pairs_from_blanking_reverts;

-- Q4: Share of revisions with empty/NULL comment (plus section-only /* ... */ comments)
SELECT
  COUNT(*) AS total_revisions,
  SUM(comment IS NULL) AS null_comments,
  SUM(comment = '') AS empty_string_comments,
  SUM(comment IS NOT NULL AND TRIM(comment) = '') AS whitespace_only_comments,
  ROUND(100.0 * SUM(comment IS NULL OR TRIM(comment) = '') / COUNT(*), 2) AS empty_or_null_pct,
  SUM(comment REGEXP '^(/\*[^*]*\*/[[:space:]]*)+$') AS section_header_only_comments,
  ROUND(100.0 * SUM(comment REGEXP '^(/\*[^*]*\*/[[:space:]]*)+$') / COUNT(*), 2) AS section_only_pct,
  SUM(comment IS NOT NULL AND NOT (comment REGEXP '^(/\*[^*]*\*/[[:space:]]*)+$')) AS substantive_text_comments,
  ROUND(100.0 * SUM(comment IS NOT NULL AND NOT (comment REGEXP '^(/\*[^*]*\*/[[:space:]]*)+$')) / COUNT(*), 2) AS substantive_text_pct
FROM corpus.revision;

-- Q4: Top 40 most common comments after normalising (lowercase, strip /* section */ prefixes, usernames -> <user>, numbers -> <num>)
WITH step1 AS (
  SELECT
    TRIM(REGEXP_REPLACE(LOWER(comment COLLATE utf8mb4_uca1400_as_ci), '^(/\*[^*]*\*/[[:space:]]*)+', '')) AS c1
  FROM corpus.revision
  WHERE comment IS NOT NULL
),
step2 AS (
  SELECT
    REGEXP_REPLACE(
      REGEXP_REPLACE(
        REGEXP_REPLACE(
          REGEXP_REPLACE(
            REGEXP_REPLACE(
              REGEXP_REPLACE(
                REGEXP_REPLACE(
                  REGEXP_REPLACE(
                    c1,
                    '\[\[:?(special:contributions|toiminnot:contributions|toiminnot:muokkaukset)/[^|\]]+(\|[^\]]*)?\]\]([[:space:]]*\(\[\[:?(user[_ ]talk|keskustelu[_ ]käyttäjästä):[^\]]+\]\]\))?',
                    '<user>'
                  ),
                  '\[\[:?(user|käyttäjä):[^|\]]+(\|[^\]]*)?\]\]',
                  '<user>'
                ),
                '\[\[:?(user[_ ]talk|keskustelu[_ ]käyttäjästä):[^|\]]+(\|[^\]]*)?\]\]',
                '<user>'
              ),
              '~20[0-9]{2}-[0-9-]+',
              '<user>'
            ),
            '(jonka on tehnyt|versio [0-9]+ käyttäjältä) (.+?)(: |$)',
            '\1 <user>\3'
          ),
          'viimeisimpään käyttäjän (.+?) tekemään versioon',
          'viimeisimpään käyttäjän <user> tekemään versioon'
        ),
        'dated [-0-9: ]+ by (.+?) using',
        'dated <num> by <user> using'
      ),
      '[0-9]+',
      '<num>'
    ) AS norm_c
  FROM step1
  WHERE c1 <> ''
)
SELECT
  ROW_NUMBER() OVER (ORDER BY COUNT(*) DESC, norm_c COLLATE utf8mb4_bin ASC) AS rank_num,
  norm_c COLLATE utf8mb4_bin AS normalised_comment,
  COUNT(*) AS revision_count,
  ROUND(100.0 * COUNT(*) / 862076, 2) AS pct_all_revisions
FROM step2
GROUP BY normalised_comment
ORDER BY revision_count DESC, normalised_comment ASC
LIMIT 40;

-- Q4: Auto-generated revert summary patterns and precision/recall vs canonical sha1 reverts
WITH tagged AS (
  SELECT
    rev.rev_id,
    (r.r_rev_id IS NOT NULL) AS is_sha1_rev,
    rev.comment COLLATE utf8mb4_uca1400_as_ci AS c,
    COALESCE(rev.comment COLLATE utf8mb4_uca1400_as_ci REGEXP '(muokkaukset|vandalismi) kumottiin ja (sivu|artikkeli) palautettiin', 0) AS p_rollback,
    COALESCE(rev.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Hylättiin (viimeisin|viimeisimmät|viimeiset|viimeinen|ensimmäinen|ensimmäiset) ([0-9]+ )?tekstimuuto', 0) AS p_flaggedrevs,
    COALESCE(rev.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Kumottu muokkaus (#|\[\[Special:Diff/|\[\[Toiminnot:ero/)?[0-9]+', 0) AS p_undo_modern,
    COALESCE(rev.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'muokkaus [0-9]+ kumottiin|Käyttäjän [^ ]+ muokkaukset kumottiin$', 0) AS p_undo_old,
    COALESCE(rev.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Revert(ed)? to (the )?revision|Reverted edits by', 0) AS p_en_gadget,
    COALESCE(rev.comment COLLATE utf8mb4_uca1400_as_ci REGEXP 'Palautett(iin|u) .*muutokseen', 0) AS p_rollback_old,
    COALESCE(REGEXP_REPLACE(rev.comment COLLATE utf8mb4_uca1400_as_ci, '^(/\*[^*]*\*/[[:space:]]*)+', '') REGEXP '(^|[^[:alpha:]])(rv|rvv|revert|palautus|palautettu|palautetaan|palautin|kumottu|kumotaan|kumosin|kumous|hylätty)([^[:alpha:]]|$)', 0) AS p_manual
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverts r
    ON r.page_id = rev.page_id AND r.r_rev_id = rev.rev_id
)
SELECT pattern_name, matches_total, sha1_revert_tp,
       ROUND(100.0 * sha1_revert_tp / matches_total, 2) AS precision_pct,
       ROUND(100.0 * sha1_revert_tp / 46049, 2) AS recall_pct
FROM (
  SELECT 1 AS ord, '1. MediaWiki Rollback (muokkaukset/vandalismi kumottiin ja sivu/artikkeli palautettiin)' AS pattern_name, SUM(p_rollback) AS matches_total, SUM(p_rollback AND is_sha1_rev) AS sha1_revert_tp FROM tagged
  UNION ALL
  SELECT 2, '2. MediaWiki Undo modern (Kumottu muokkaus <id>, jonka teki ...)', SUM(p_undo_modern), SUM(p_undo_modern AND is_sha1_rev) FROM tagged
  UNION ALL
  SELECT 3, '3. FlaggedRevs Reject (Hylättiin ... tekstimuutos/tekstimuutosta ...)', SUM(p_flaggedrevs), SUM(p_flaggedrevs AND is_sha1_rev) FROM tagged
  UNION ALL
  SELECT 4, '4. MediaWiki Undo legacy (Käyttäjän ... muokkaus <id> kumottiin)', SUM(p_undo_old), SUM(p_undo_old AND is_sha1_rev) FROM tagged
  UNION ALL
  SELECT 5, '5. Navigation Popups / EN gadget (Revert(ed) to revision / Reverted edits by)', SUM(p_en_gadget), SUM(p_en_gadget AND is_sha1_rev) FROM tagged
  UNION ALL
  SELECT 6, '6. Legacy Rollback 2004-2006 (Palautettiin/Palautettu ... muutokseen)', SUM(p_rollback_old), SUM(p_rollback_old AND is_sha1_rev) FROM tagged
  UNION ALL
  SELECT 7, 'ALL AUTO-GENERATED TEMPLATES (union of 1-6)',
         SUM(p_rollback OR p_flaggedrevs OR p_undo_modern OR p_undo_old OR p_en_gadget OR p_rollback_old),
         SUM((p_rollback OR p_flaggedrevs OR p_undo_modern OR p_undo_old OR p_en_gadget OR p_rollback_old) AND is_sha1_rev)
  FROM tagged
  UNION ALL
  SELECT 8, 'AUTO-GENERATED + MANUAL REVERT WORDS (rv/rvv/palautus/kumottu/...)',
         SUM(p_rollback OR p_flaggedrevs OR p_undo_modern OR p_undo_old OR p_en_gadget OR p_rollback_old OR p_manual),
         SUM((p_rollback OR p_flaggedrevs OR p_undo_modern OR p_undo_old OR p_en_gadget OR p_rollback_old OR p_manual) AND is_sha1_rev)
  FROM tagged
) s
ORDER BY ord;

-- Q4: Breakdown of canonical sha1 reverts without a revert-indicating summary (false negatives of summary detection)
SELECT
  COUNT(*) AS total_sha1_reverts,
  SUM(r_comment IS NULL) AS sha1_reverts_null_comment,
  SUM(r_comment IS NOT NULL AND r_comment REGEXP '^(/\*[^*]*\*/[[:space:]]*)+$') AS sha1_reverts_section_only
FROM research.prof_ed_reverts;

-- Q4: Keyword frequencies (section-stripped body count, raw count incl. /* section */, and count inside canonical sha1 reverts)
WITH c_clean AS (
  SELECT
    rev.rev_id,
    rev.comment COLLATE utf8mb4_uca1400_as_ci AS raw_c,
    TRIM(REGEXP_REPLACE(rev.comment COLLATE utf8mb4_uca1400_as_ci, '^(/\*[^*]*\*/[[:space:]]*)+', '')) AS body_c,
    (r.r_rev_id IS NOT NULL) AS is_rev
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverts r ON r.page_id = rev.page_id AND r.r_rev_id = rev.rev_id
  WHERE rev.comment IS NOT NULL
)
SELECT kw, body_matches, raw_matches, in_sha1_reverts,
       ROUND(100.0 * in_sha1_reverts / NULLIF(body_matches, 0), 1) AS sha1_revert_pct
FROM (
  SELECT 1 AS ord, 'kumottu' AS kw, SUM(body_c LIKE '%kumottu%') AS body_matches, SUM(raw_c LIKE '%kumottu%') AS raw_matches, SUM(body_c LIKE '%kumottu%' AND is_rev) AS in_sha1_reverts FROM c_clean
  UNION ALL SELECT 2, 'hylätty', SUM(body_c LIKE '%hylätty%'), SUM(raw_c LIKE '%hylätty%'), SUM(body_c LIKE '%hylätty%' AND is_rev) FROM c_clean
  UNION ALL SELECT 3, 'palautettu', SUM(body_c LIKE '%palautettu%'), SUM(raw_c LIKE '%palautettu%'), SUM(body_c LIKE '%palautettu%' AND is_rev) FROM c_clean
  UNION ALL SELECT 4, 'vandalismi', SUM(body_c LIKE '%vandalismi%'), SUM(raw_c LIKE '%vandalismi%'), SUM(body_c LIKE '%vandalismi%' AND is_rev) FROM c_clean
  UNION ALL SELECT 5, 'propaganda', SUM(body_c LIKE '%propaganda%'), SUM(raw_c LIKE '%propaganda%'), SUM(body_c LIKE '%propaganda%' AND is_rev) FROM c_clean
  UNION ALL SELECT 6, 'POV (incl. NPOV, word-boundary)', SUM(body_c REGEXP '(^|[^[:alpha:]])n?pov([^[:alpha:]]|$)'), SUM(raw_c REGEXP '(^|[^[:alpha:]])n?pov([^[:alpha:]]|$)'), SUM(body_c REGEXP '(^|[^[:alpha:]])n?pov([^[:alpha:]]|$)' AND is_rev) FROM c_clean
  UNION ALL SELECT 7, '  └─ POV strict (excl. NPOV)', SUM(body_c REGEXP '(^|[^[:alpha:]])pov([^[:alpha:]]|$)'), SUM(raw_c REGEXP '(^|[^[:alpha:]])pov([^[:alpha:]]|$)'), SUM(body_c REGEXP '(^|[^[:alpha:]])pov([^[:alpha:]]|$)' AND is_rev) FROM c_clean
  UNION ALL SELECT 8, 'neutraalius', SUM(body_c LIKE '%neutraalius%'), SUM(raw_c LIKE '%neutraalius%'), SUM(body_c LIKE '%neutraalius%' AND is_rev) FROM c_clean
  UNION ALL SELECT 9, 'lähteetön', SUM(body_c LIKE '%lähteetön%'), SUM(raw_c LIKE '%lähteetön%'), SUM(body_c LIKE '%lähteetön%' AND is_rev) FROM c_clean
  UNION ALL SELECT 10, 'lähde?', SUM(body_c LIKE '%lähde?%'), SUM(raw_c LIKE '%lähde?%'), SUM(body_c LIKE '%lähde?%' AND is_rev) FROM c_clean
  UNION ALL SELECT 11, 'puolueellinen', SUM(body_c LIKE '%puolueellinen%'), SUM(raw_c LIKE '%puolueellinen%'), SUM(body_c LIKE '%puolueellinen%' AND is_rev) FROM c_clean
  UNION ALL SELECT 12, 'poliittinen', SUM(body_c LIKE '%poliittinen%'), SUM(raw_c LIKE '%poliittinen%'), SUM(body_c LIKE '%poliittinen%' AND is_rev) FROM c_clean
  UNION ALL SELECT 13, 'valhe', SUM(body_c LIKE '%valhe%'), SUM(raw_c LIKE '%valhe%'), SUM(body_c LIKE '%valhe%' AND is_rev) FROM c_clean
  UNION ALL SELECT 14, 'väärä', SUM(body_c LIKE '%väärä%'), SUM(raw_c LIKE '%väärä%'), SUM(body_c LIKE '%väärä%' AND is_rev) FROM c_clean
  UNION ALL SELECT 15, 'korjattu', SUM(body_c LIKE '%korjattu%'), SUM(raw_c LIKE '%korjattu%'), SUM(body_c LIKE '%korjattu%' AND is_rev) FROM c_clean
  UNION ALL SELECT 16, 'faktat', SUM(body_c LIKE '%faktat%'), SUM(raw_c LIKE '%faktat%'), SUM(body_c LIKE '%faktat%' AND is_rev) FROM c_clean
) q
ORDER BY ord;

-- Q4: 3 deterministic example diff links for each of the 15 required keywords
WITH c_clean AS (
  SELECT
    rev.rev_id,
    rev.title,
    TRIM(REGEXP_REPLACE(rev.comment COLLATE utf8mb4_uca1400_as_ci, '^(/\*[^*]*\*/[[:space:]]*)+', '')) AS body_c,
    (r.r_rev_id IS NOT NULL) AS is_rev
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverts r ON r.page_id = rev.page_id AND r.r_rev_id = rev.rev_id
  WHERE rev.comment IS NOT NULL
),
kw_hits AS (
  SELECT 'kumottu' AS kw, rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%kumottu%'
  UNION ALL SELECT 'hylätty', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%hylätty%'
  UNION ALL SELECT 'palautettu', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%palautettu%'
  UNION ALL SELECT 'vandalismi', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%vandalismi%'
  UNION ALL SELECT 'propaganda', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%propaganda%'
  UNION ALL SELECT 'POV', rev_id, title, is_rev FROM c_clean WHERE body_c REGEXP '(^|[^[:alpha:]])n?pov([^[:alpha:]]|$)'
  UNION ALL SELECT 'neutraalius', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%neutraalius%'
  UNION ALL SELECT 'lähteetön', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%lähteetön%'
  UNION ALL SELECT 'lähde?', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%lähde?%'
  UNION ALL SELECT 'puolueellinen', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%puolueellinen%'
  UNION ALL SELECT 'poliittinen', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%poliittinen%'
  UNION ALL SELECT 'valhe', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%valhe%'
  UNION ALL SELECT 'väärä', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%väärä%'
  UNION ALL SELECT 'korjattu', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%korjattu%'
  UNION ALL SELECT 'faktat', rev_id, title, is_rev FROM c_clean WHERE body_c LIKE '%faktat%'
),
ranked AS (
  SELECT kw, rev_id, title,
         ROW_NUMBER() OVER (PARTITION BY kw ORDER BY is_rev DESC, rev_id DESC) AS rn
  FROM kw_hits
)
SELECT kw, rn, rev_id, title, CONCAT('https://fi.wikipedia.org/w/index.php?diff=', rev_id) AS diff_url
FROM ranked
WHERE rn <= 3
ORDER BY FIELD(kw, 'kumottu','hylätty','palautettu','vandalismi','propaganda','POV','neutraalius','lähteetön','lähde?','puolueellinen','poliittinen','valhe','väärä','korjattu','faktat'), rn;

-- Q4: Additional telling Finnish words/stems found among summaries of reverts
WITH c_clean AS (
  SELECT
    TRIM(REGEXP_REPLACE(rev.comment COLLATE utf8mb4_uca1400_as_ci, '^(/\*[^*]*\*/[[:space:]]*)+', '')) AS body_c,
    (r.r_rev_id IS NOT NULL) AS is_rev
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverts r ON r.page_id = rev.page_id AND r.r_rev_id = rev.rev_id
  WHERE rev.comment IS NOT NULL
)
SELECT term_group, body_matches, in_sha1_reverts,
       ROUND(100.0 * in_sha1_reverts / NULLIF(body_matches, 0), 1) AS sha1_revert_pct
FROM (
  SELECT 1 AS ord, 'sotku / sotkeminen / sotkentaa (vandalism)' AS term_group, SUM(body_c REGEXP '(^|[^[:alpha:]])sotk(u|e|i|enta)') AS body_matches, SUM(body_c REGEXP '(^|[^[:alpha:]])sotk(u|e|i|enta)' AND is_rev) AS in_sha1_reverts FROM c_clean
  UNION ALL SELECT 2, 'eston kierto (block evasion / socking)', SUM(body_c LIKE '%eston kier%'), SUM(body_c LIKE '%eston kier%' AND is_rev) FROM c_clean
  UNION ALL SELECT 3, 'lähteettöm* (oblique/plural stem of lähteetön)', SUM(body_c LIKE '%lähteettöm%'), SUM(body_c LIKE '%lähteettöm%' AND is_rev) FROM c_clean
  UNION ALL SELECT 4, 'ei lähdettä / lähde puuttuu / ilman lähdettä', SUM(body_c LIKE '%ei lähdettä%' OR body_c LIKE '%lähde puuttuu%' OR body_c LIKE '%ilman lähdettä%'), SUM((body_c LIKE '%ei lähdettä%' OR body_c LIKE '%lähde puuttuu%' OR body_c LIKE '%ilman lähdettä%') AND is_rev) FROM c_clean
  UNION ALL SELECT 5, 'perusteeton / selittämätön (unexplained change)', SUM(body_c LIKE '%perusteet%' OR body_c LIKE '%selittämät%'), SUM((body_c LIKE '%perusteet%' OR body_c LIKE '%selittämät%') AND is_rev) FROM c_clean
  UNION ALL SELECT 6, 'mielipide / spekulaatio (opinion / speculation)', SUM(body_c REGEXP '(^|[^[:alpha:]])(mielipide|mielipite|spekulaatio)'), SUM(body_c REGEXP '(^|[^[:alpha:]])(mielipide|mielipite|spekulaatio)' AND is_rev) FROM c_clean
  UNION ALL SELECT 7, 'roska* (garbage / speedy-delete)', SUM(body_c LIKE '%roska%'), SUM(body_c LIKE '%roska%' AND is_rev) FROM c_clean
  UNION ALL SELECT 8, 'uutta tutkimusta / oma(a) tutkimus(ta) (OR)', SUM(body_c LIKE '%uutta tutkimusta%' OR body_c LIKE '%oma tutkimus%' OR body_c LIKE '%omaa tutkimusta%'), SUM((body_c LIKE '%uutta tutkimusta%' OR body_c LIKE '%oma tutkimus%' OR body_c LIKE '%omaa tutkimusta%') AND is_rev) FROM c_clean
  UNION ALL SELECT 9, 'konsensus* (consensus)', SUM(body_c LIKE '%konsensu%'), SUM(body_c LIKE '%konsensu%' AND is_rev) FROM c_clean
  UNION ALL SELECT 10, 'puolueelli* (incl. puolueellista / puolueellisuus)', SUM(body_c LIKE '%puolueelli%'), SUM(body_c LIKE '%puolueelli%' AND is_rev) FROM c_clean
  UNION ALL SELECT 11, 'häirik* (disruptive editing)', SUM(body_c LIKE '%häirik%'), SUM(body_c LIKE '%häirik%' AND is_rev) FROM c_clean
) t
ORDER BY ord;

-- Q5: Distinct editor counts, edit shares, and reverted-edit rates across named, IP, temporary (~20xx-), and null-user classes
WITH rev_tagged AS (
  SELECT
    rev.rev_id,
    rev.user COLLATE utf8mb4_bin AS u_exact,
    rev.user AS u_ci,
    CASE
      WHEN rev.user IS NULL THEN '4. NULL user (RevDel)'
      WHEN rev.anonymous = 0 THEN '1. Named accounts'
      WHEN rev.user LIKE '~20%' THEN '3. Temporary accounts (~20xx-)'
      ELSE '2. Anonymous (IP) editors'
    END AS editor_class,
    (re.e_rev_id IS NOT NULL) AS was_reverted
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverted_edits re
    ON re.page_id = rev.page_id AND re.e_rev_id = rev.rev_id AND re.is_first_revert = 1
)
SELECT
  editor_class,
  COUNT(DISTINCT u_exact) AS distinct_editors_exact,
  COUNT(DISTINCT u_ci) AS distinct_editors_default_collation,
  COUNT(*) AS edits,
  ROUND(100.0 * COUNT(*) / 862076, 2) AS pct_of_all_edits,
  SUM(was_reverted) AS reverted_edits,
  ROUND(100.0 * SUM(was_reverted) / COUNT(*), 2) AS reverted_pct
FROM rev_tagged
GROUP BY editor_class
ORDER BY editor_class;

-- Q5: Temporary account breakdown by year prefix (~2025-, ~2026-)
SELECT
  LEFT(user, 6) AS temp_prefix,
  COUNT(DISTINCT user COLLATE utf8mb4_bin) AS distinct_temp_accounts,
  COUNT(*) AS edits,
  MIN(ts) AS first_edit,
  MAX(ts) AS last_edit
FROM corpus.revision
WHERE anonymous = 1 AND user LIKE '~20%'
GROUP BY temp_prefix
ORDER BY temp_prefix;

-- Q5: Top 30 named accounts with highest reverted-edit ratio (>= 10 edits)
WITH user_stats AS (
  SELECT
    rev.user COLLATE utf8mb4_bin AS account,
    COUNT(*) AS edits,
    SUM(re.e_rev_id IS NOT NULL) AS reverted,
    ROUND(SUM(re.e_rev_id IS NOT NULL) / COUNT(*), 4) AS reverted_ratio,
    COUNT(DISTINCT rev.page_id) AS articles,
    MIN(rev.ts) AS first_edit,
    MAX(rev.ts) AS last_edit
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverted_edits re
    ON re.page_id = rev.page_id AND re.e_rev_id = rev.rev_id AND re.is_first_revert = 1
  WHERE rev.anonymous = 0 AND rev.user IS NOT NULL
  GROUP BY account
  HAVING COUNT(*) >= 10
)
SELECT
  ROW_NUMBER() OVER (ORDER BY reverted_ratio DESC, reverted DESC, edits DESC, account ASC) AS rank_num,
  account, edits, reverted, reverted_ratio, articles, first_edit, last_edit
FROM user_stats
ORDER BY reverted_ratio DESC, reverted DESC, edits DESC, account ASC
LIMIT 30;

-- Q5: Effect of 0-byte blanking collisions and self-reverts on the Top 30 reverted-ratio accounts
WITH top30 AS (
  SELECT
    rev.user COLLATE utf8mb4_bin AS account,
    COUNT(*) AS edits,
    SUM(re.e_rev_id IS NOT NULL) AS reverted_canonical,
    SUM(re.e_rev_id IS NOT NULL AND re.r_bytes = 0) AS from_blanking,
    SUM(re.e_rev_id IS NOT NULL AND re.r_user = rev.user COLLATE utf8mb4_bin) AS from_self_revert,
    SUM(re.e_rev_id IS NOT NULL AND re.r_bytes > 0 AND (re.r_user IS NULL OR re.r_user <> rev.user COLLATE utf8mb4_bin)) AS reverted_filtered
  FROM corpus.revision rev
  LEFT JOIN research.prof_ed_reverted_edits re
    ON re.page_id = rev.page_id AND re.e_rev_id = rev.rev_id AND re.is_first_revert = 1
  WHERE rev.anonymous = 0 AND rev.user IS NOT NULL
  GROUP BY account
  HAVING COUNT(*) >= 10
  ORDER BY (SUM(re.e_rev_id IS NOT NULL) / COUNT(*)) DESC, reverted_canonical DESC, edits DESC, account ASC
  LIMIT 30
)
SELECT * FROM top30 WHERE reverted_canonical <> reverted_filtered;

-- Q5: Single-article editors and their share of class edits and total corpus edits
WITH per_editor AS (
  SELECT
    rev.user COLLATE utf8mb4_bin AS u,
    CASE
      WHEN rev.anonymous = 0 THEN '1. Named accounts'
      WHEN rev.user LIKE '~20%' THEN '3. Temporary accounts (~20xx-)'
      ELSE '2. Anonymous (IP) editors'
    END AS editor_class,
    COUNT(*) AS edits,
    COUNT(DISTINCT rev.page_id) AS articles
  FROM corpus.revision rev
  WHERE rev.user IS NOT NULL
  GROUP BY u, editor_class
)
SELECT
  editor_class,
  COUNT(*) AS total_editors,
  SUM(articles = 1) AS single_article_editors,
  ROUND(100.0 * SUM(articles = 1) / COUNT(*), 2) AS pct_of_class_editors,
  SUM(CASE WHEN articles = 1 THEN edits ELSE 0 END) AS single_article_edits,
  ROUND(100.0 * SUM(CASE WHEN articles = 1 THEN edits ELSE 0 END) / SUM(edits), 2) AS pct_of_class_edits,
  ROUND(100.0 * SUM(CASE WHEN articles = 1 THEN edits ELSE 0 END) / 862076, 2) AS pct_of_all_corpus_edits
FROM per_editor
GROUP BY editor_class
ORDER BY editor_class;

-- Q5: Bot summary totals (strict -bot/-botti suffix vs extended bot set)
SELECT
  SUM(LOWER(user) REGEXP 'bot(ti)?$') AS bot_suffix_edits,
  COUNT(DISTINCT CASE WHEN LOWER(user) REGEXP 'bot(ti)?$' THEN user COLLATE utf8mb4_bin END) AS bot_suffix_accounts,
  ROUND(100.0 * SUM(LOWER(user) REGEXP 'bot(ti)?$') / 862076, 2) AS bot_suffix_pct_all_edits,
  SUM(LOWER(user) REGEXP 'bot(ti)?($|[0-9~_-])|^bot' OR user IN ('Lab-ot', 'CommonsDelinker', 'ʬʭ', 'Overlord', 'Loveless')) AS extended_bot_edits,
  COUNT(DISTINCT CASE WHEN LOWER(user) REGEXP 'bot(ti)?($|[0-9~_-])|^bot' OR user IN ('Lab-ot', 'CommonsDelinker', 'ʬʭ', 'Overlord', 'Loveless') THEN user COLLATE utf8mb4_bin END) AS extended_bot_accounts,
  ROUND(100.0 * SUM(LOWER(user) REGEXP 'bot(ti)?($|[0-9~_-])|^bot' OR user IN ('Lab-ot', 'CommonsDelinker', 'ʬʭ', 'Overlord', 'Loveless')) / 862076, 2) AS extended_bot_pct_all_edits
FROM corpus.revision
WHERE anonymous = 0 AND user IS NOT NULL;

-- Q5: Top 25 likely bots by edit volume + top human high-volume editors (>= 8,000 edits) for comparison
SELECT
  rev.user COLLATE utf8mb4_bin AS account,
  IF(LOWER(rev.user) REGEXP 'bot(ti)?($|[0-9~_-])|^bot' OR rev.user IN ('Lab-ot', 'CommonsDelinker', 'ʬʭ', 'Overlord', 'Loveless'), 'bot', 'human') AS kind,
  COUNT(*) AS edits,
  ROUND(100.0 * COUNT(*) / 862076, 2) AS pct_all_edits,
  COUNT(DISTINCT rev.page_id) AS articles,
  SUM(r.r_rev_id IS NOT NULL) AS reverts_made,
  SUM(re.e_rev_id IS NOT NULL) AS times_reverted
FROM corpus.revision rev
LEFT JOIN research.prof_ed_reverts r
  ON r.page_id = rev.page_id AND r.r_rev_id = rev.rev_id
LEFT JOIN research.prof_ed_reverted_edits re
  ON re.page_id = rev.page_id AND re.e_rev_id = rev.rev_id AND re.is_first_revert = 1
WHERE rev.anonymous = 0 AND rev.user IS NOT NULL
GROUP BY account, kind
HAVING (kind = 'bot' AND edits >= 350) OR edits >= 8000
ORDER BY kind ASC, edits DESC;

-- Q6: Corpus-wide named-on-named revert summary showing effect of excluding self-reverts and R.bytes = 0 blanking collisions
SELECT
  COUNT(DISTINCT r_rev_id) AS named_on_named_incl_self_and_blank_events,
  COUNT(*) AS named_on_named_incl_self_and_blank_pairs,
  COUNT(DISTINCT CASE WHEN r_user <> e_user THEN r_rev_id END) AS nvn_excl_self_events,
  SUM(r_user <> e_user) AS nvn_excl_self_pairs,
  COUNT(DISTINCT CASE WHEN r_user <> e_user AND r_bytes > 0 THEN r_rev_id END) AS nvn_filtered_events,
  SUM(r_user <> e_user AND r_bytes > 0) AS nvn_filtered_pairs,
  COUNT(DISTINCT CASE WHEN r_user <> e_user AND r_bytes > 0 THEN page_id END) AS nvn_filtered_articles
FROM research.prof_ed_reverted_edits
WHERE r_anon = 0 AND r_user IS NOT NULL
  AND e_anon = 0 AND e_user IS NOT NULL;

-- Q6: Top 30 edit-war candidate articles by named-vs-named revert exchanges (filtered: R.bytes > 0, r_user <> e_user)
WITH nvn AS (
  SELECT page_id, title, r_rev_id, r_ts, r_user, e_rev_id, e_user
  FROM research.prof_ed_reverted_edits
  WHERE r_anon = 0 AND r_user IS NOT NULL
    AND e_anon = 0 AND e_user IS NOT NULL
    AND r_user <> e_user
    AND r_bytes > 0
),
article_totals AS (
  SELECT
    page_id,
    MAX(title) AS title,
    COUNT(DISTINCT r_rev_id) AS exchanges,
    COUNT(*) AS reverted_edit_pairs
  FROM nvn
  GROUP BY page_id
),
article_users AS (
  SELECT page_id, COUNT(DISTINCT u) AS distinct_users
  FROM (
    SELECT page_id, r_user AS u FROM nvn
    UNION
    SELECT page_id, e_user AS u FROM nvn
  ) u_all
  GROUP BY page_id
),
monthly AS (
  SELECT
    page_id,
    DATE_FORMAT(r_ts, '%Y-%m') AS busiest_ym,
    COUNT(DISTINCT r_rev_id) AS busiest_ym_exchanges,
    MAX(r_rev_id) AS example_rev_id,
    ROW_NUMBER() OVER (
      PARTITION BY page_id
      ORDER BY COUNT(DISTINCT r_rev_id) DESC, DATE_FORMAT(r_ts, '%Y-%m') DESC
    ) AS rn
  FROM nvn
  GROUP BY page_id, DATE_FORMAT(r_ts, '%Y-%m')
)
SELECT
  ROW_NUMBER() OVER (ORDER BY t.exchanges DESC, t.reverted_edit_pairs DESC, t.page_id ASC) AS rank_num,
  t.page_id,
  t.title,
  t.exchanges,
  t.reverted_edit_pairs,
  u.distinct_users,
  m.busiest_ym,
  m.busiest_ym_exchanges,
  CONCAT('https://fi.wikipedia.org/w/index.php?diff=', m.example_rev_id) AS example_exchange_url
FROM article_totals t
JOIN article_users u ON u.page_id = t.page_id
JOIN monthly m ON m.page_id = t.page_id AND m.rn = 1
ORDER BY t.exchanges DESC, t.reverted_edit_pairs DESC, t.page_id ASC
LIMIT 30;

-- Q6 caveat check: Articles affected by R.bytes = 0 blanking collisions and large-span redirects in named-vs-named pairs
SELECT
  r.page_id,
  MAX(r.title) AS title,
  COUNT(DISTINCT r.r_rev_id) AS unfiltered_events,
  COUNT(*) AS unfiltered_pairs,
  COUNT(DISTINCT CASE WHEN r.r_bytes > 0 THEN r.r_rev_id END) AS filtered_events,
  SUM(r.r_bytes > 0) AS filtered_pairs,
  MAX(r.span) AS max_single_revert_span
FROM research.prof_ed_reverted_edits r
WHERE r.r_anon = 0 AND r.r_user IS NOT NULL
  AND r.e_anon = 0 AND r.e_user IS NOT NULL
  AND r.r_user <> r.e_user
GROUP BY r.page_id
ORDER BY unfiltered_pairs DESC
LIMIT 5;

-- Q6: Top 20 named user pairs by mutual reverts (filtered: R.bytes > 0, r_user <> e_user; extended to 23 so top 20 human-vs-human pairs are also shown)
WITH nvn AS (
  SELECT page_id, r_rev_id, r_user, e_user
  FROM research.prof_ed_reverted_edits
  WHERE r_anon = 0 AND r_user IS NOT NULL
    AND e_anon = 0 AND e_user IS NOT NULL
    AND r_user <> e_user
    AND r_bytes > 0
),
directed AS (
  SELECT
    r_user AS reverter,
    e_user AS reverted,
    COUNT(DISTINCT r_rev_id) AS rev_events,
    COUNT(*) AS edit_pairs
  FROM nvn
  GROUP BY reverter, reverted
),
mutual AS (
  SELECT
    d1.reverter AS user_a,
    d1.reverted AS user_b,
    d1.rev_events AS a_reverted_b,
    d2.rev_events AS b_reverted_a,
    (d1.rev_events + d2.rev_events) AS mutual_reverts,
    (d1.edit_pairs + d2.edit_pairs) AS mutual_edit_pairs
  FROM directed d1
  JOIN directed d2
    ON d1.reverter = d2.reverted AND d1.reverted = d2.reverter
  WHERE d1.reverter < d1.reverted
),
pair_pages AS (
  SELECT
    LEAST(r_user, e_user) AS user_a,
    GREATEST(r_user, e_user) AS user_b,
    COUNT(DISTINCT page_id) AS shared_articles
  FROM nvn
  GROUP BY user_a, user_b
)
SELECT
  ROW_NUMBER() OVER (ORDER BY m.mutual_reverts DESC, LEAST(m.a_reverted_b, m.b_reverted_a) DESC, m.user_a ASC) AS rank_num,
  m.user_a,
  m.user_b,
  m.a_reverted_b,
  m.b_reverted_a,
  m.mutual_reverts,
  m.mutual_edit_pairs,
  p.shared_articles
FROM mutual m
JOIN pair_pages p ON p.user_a = m.user_a AND p.user_b = m.user_b
ORDER BY m.mutual_reverts DESC, LEAST(m.a_reverted_b, m.b_reverted_a) DESC, m.user_a ASC
LIMIT 23;

-- Q5 note: Temporary accounts (~20xx-) with >= 10 edits ordered by reverted-edit ratio
SELECT
  rev.user COLLATE utf8mb4_bin AS temp_account,
  COUNT(*) AS edits,
  SUM(re.e_rev_id IS NOT NULL) AS reverted,
  ROUND(SUM(re.e_rev_id IS NOT NULL) / COUNT(*), 4) AS reverted_ratio,
  COUNT(DISTINCT rev.page_id) AS articles
FROM corpus.revision rev
LEFT JOIN research.prof_ed_reverted_edits re
  ON re.page_id = rev.page_id AND re.e_rev_id = rev.rev_id AND re.is_first_revert = 1
WHERE rev.anonymous = 1 AND rev.user LIKE '~20%'
GROUP BY temp_account
HAVING COUNT(*) >= 10
ORDER BY reverted_ratio DESC, reverted DESC
LIMIT 10;

-- Caveat check: No-op log revisions with sha1 identical to immediate predecessor
WITH ordered AS (
  SELECT page_id, sha1,
         ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS seq
  FROM corpus.revision
),
with_lags AS (
  SELECT sha1, seq,
         LAG(seq, 1) OVER (PARTITION BY page_id, sha1 ORDER BY seq) AS lag1_seq,
         LAG(seq, 2) OVER (PARTITION BY page_id, sha1 ORDER BY seq) AS lag2_seq
  FROM ordered
)
SELECT
  SUM(sha1 IS NOT NULL AND lag1_seq = seq - 1) AS noop_same_as_imm_prev,
  SUM(sha1 IS NOT NULL AND lag1_seq = seq - 1 AND lag2_seq IS NOT NULL) AS noop_with_earlier_sha1
FROM with_lags;
