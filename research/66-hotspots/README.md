# P4.6 (#66) · Precomputed Hot-Spots Aggregates (`research.hotspot_*`)

Materializes four small, read-ready aggregate tables in MariaDB (`research.hotspot_*`) from Phase 1 signals
(`research.s1_*` identity reverts, `research.s4_*` conflict episodes and page contestedness) and indexed `corpus.*`
tables so `/hotspots` loads the entire dashboard in a single lightweight round trip (zero `revision_text` touches, zero
request-time scans of `corpus.revision`).

## How to Run

```bash
# Ensure scripts/corpus-tunnel.sh is running on 127.0.0.1:3307 and CORPUS_PW / MYSQL_PWD is exported:
./research/66-hotspots/build.sh
```

- `build.sh` wraps `mariadb --protocol=TCP` over `build.sql` using standard repo env vars (`CORPUS_HOST`, `CORPUS_PORT`,
  `CORPUS_USER`, `CORPUS_PW` / `MYSQL_PWD`, `CORPUS_DB`).
- `build.sql` is idempotent (`DROP TABLE IF EXISTS` + `CREATE TABLE ... AS SELECT`) and runs under
  `SET SESSION max_statement_time = 55;` (~25 s end-to-end across all four tables on the 2-vCPU VPS).

---

## Tables & Row Counts

| Table | Rows | Primary key | Description |
|---|---:|---|---|
| `research.hotspot_headline` | `1` | — | Corpus-wide totals: `revisions`, `articles`, `reverts`, `episodes`, `anon_reverted_share`. |
| `research.hotspot_article` | `200` | `page_id` | Top 200 Finnish Wikipedia articles by `s4_page_contest.contested_score`, with English sibling title (`wikidata_id`), S1 class-group breakdown (`reverts_dispute`, `reverts_vandalism`, `reverts_other`), default episode count, anonymous reverted-edit share, `first_ts`/`last_ts`, and 24-year revert sparkline (`2003..2026`). |
| `research.hotspot_month` | `288` | `month` | Full calendar spine `2003-01` … `2026-12` (`edits`, `reverts`, `episodes` started, plus `top_page_id`, `top_title_fi`, `top_title_en` driving that month's episodes). |
| `research.hotspot_category` | `50` | `category` | Top 50 thematic Finnish categories by aggregate contestedness (`articles`, `contest_score`, `reverts`, `dispute_reverts`, `episodes`). |

### Signal Definitions Used Consistently Across Tables

- **Non-excluded human reverts (`reverts`):** S1 identity reverts with `class NOT IN ('excluded', 'bot', 'self_revert')`
  (= `s1_page_stats.signal_total`, `41,606` corpus-wide), matching both `s4_episode.revert_count` and the Evidence
  Standard's exclusion of SHA-1 blanking/wide-span artifacts, bot edits, and self-reverts.
  - `reverts_dispute` = `edit_war_count + dispute_count` (named-editor reverts, mutual revert wars, and re-insertions).
  - `reverts_vandalism` = `graffiti_cleanup_count` (`< 24 h` anonymous-only reverts).
  - `reverts_other` = `other_count` (`>= 24 h` anonymous/other reverts).
  - `reverts = reverts_dispute + reverts_vandalism + reverts_other`.
- **Conflict episodes (`episodes`):** Default P1.S4 episodes (`s4_episode.is_default = 1`: `edit_war` 7-day window +
  `ping_pong` + `burst`, `5,178` corpus-wide).
- **Monthly top article (`hotspot_month.top_page_id`):** Among articles with `>= 1` default episode starting in month
  `YYYY-MM`, ranked by `MAX(weighted_episode_score) + 1.5 * ln(1 + episode_count)` where conflict episodes
  (`edit_war`, `ping_pong`, or `burst` with `revert_count > 0`) keep full `score` and zero-revert collaborative bursts
  are weighted `0.25 * score` (matching `s4_page_contest.peak_month`) with sublinear `ln(1 + count)` so a single day
  triggering overlapping `ping_pong` + `burst` rows does not double-count.
- **Category filter (`hotspot_category`):**
  1. `ac.lang = 'fi'` — restricts `corpus.article_category` to Finnish corpus `page_id`s (`en`/`sv` rows in that table
     hold foreign `page_id`s mapped onto the Finnish category tree).
  2. Maintenance, hidden, namespace-prefixed, and meta/list categories are excluded via `ac.category NOT LIKE '%:%'` and
     `NOT LOWER(ac.category) REGEXP '(^luettelot |tynkä|tyngät|korjattav|tarkistettav|lähteettöm|viitteettöm|ylläpito|piilotet|wikipedia|wikidata|malline|artikkelit|sivut|täsmenny|ohjaukset)'`
     (e.g. skips meta list categories such as *Luettelot Suomen historiasta* and *Luettelot suomalaisista poliitikoista*,
     plus any stub/cleanup/hidden maintenance labels if corpus categories are re-imported).
  3. `HAVING COUNT(DISTINCT ac.page_id) >= 3` — skips `496` single- or two-article eponymous categories (such as
     *Tarja Halonen* `[1 page]` or *Sauli Niinistö* `[1 page]`) that merely duplicate a single biography rather than
     grouping a theme.

---

## Sample Output (VPS Corpus)

### 1. `research.hotspot_headline` (`1` row)

| `revisions` | `articles` | `reverts` | `episodes` | `anon_reverted_share` |
|---:|---:|---:|---:|---:|
| `862,076` | `19,742` | `41,606` | `5,178` | `0.7011` (`70.1%`) |

### 2. `research.hotspot_article` (Top 10 of `200` rows)

| Rank | `page_id` | `title_fi` | `title_en` | `contest_score` | `reverts` (`disp` / `vand` / `oth`) | `episodes` | `anon_share` | Span (UTC) |
|---:|---:|---|---|---:|---:|---:|---:|---|
| 1 | `1187` | Tarja Halonen | Tarja Halonen | `32.4247` | `977` (`238 / 737 / 2`) | `48` | `0.8231` | `2003-02` – `2026-09` |
| 2 | `1137` | Sauli Niinistö | Sauli Niinistö | `31.7849` | `633` (`260 / 372 / 1`) | `36` | `0.6876` | `2003-07` – `2026-07` |
| 3 | `18561` | Pekka Haavisto | Pekka Haavisto | `30.2884` | `277` (`153 / 123 / 1`) | `31` | `0.6344` | `2004-09` – `2026-05` |
| 4 | `1069` | Suomen tasavallan presidentti | President of Finland | `28.6364` | `355` (`151 / 197 / 7`) | `37` | `0.6752` | `2003-05` – `2026-08` |
| 5 | `1150` | Suomen sisällissota | Finnish Civil War | `28.1747` | `330` (`123 / 201 / 6`) | `41` | `0.7040` | `2003-08` – `2026-09` |
| 6 | `200797` | Axel Olof Freudenthal | Axel Olof Freudenthal | `26.9642` | `101` (`82 / 16 / 3`) | `16` | `0.3636` | `2006-07` – `2026-09` |
| 7 | `6626` | Karjala | Karelia | `25.5938` | `104` (`77 / 26 / 1`) | `23` | `0.4538` | `2004-03` – `2026-09` |
| 8 | `436339` | Jokelan koulusurmat | Jokela school shooting | `25.0532` | `177` (`60 / 114 / 3`) | `19` | `0.7078` | `2007-11` – `2026-09` |
| 9 | `440` | Jatkosota | Continuation War | `24.2609` | `212` (`92 / 118 / 2`) | `24` | `0.6611` | `2003-08` – `2026-09` |
| 10 | `1445320` | Suomen Kansa Ensin | Finnish People First | `24.1091` | `105` (`70 / 33 / 2`) | `18` | `0.5132` | `2018-04` – `2026-09` |

### 3. `research.hotspot_month` (Top 8 spike months of `288` rows)

| `month` | `reverts` | `episodes` | `edits` | `top_page_id` | `top_title_fi` | `top_title_en` |
|---|---:|---:|---:|---:|---|---|
| `2007-01` | `483` | `95` | `7,197` | `498` | Kalevala | Kalevala |
| `2007-04` | `410` | `61` | `7,207` | `7314` | Mikael Agricola | Mikael Agricola |
| `2020-05` | `389` | `14` | `3,278` | `1562418` | Koronaviruspandemia Suomessa | COVID-19 pandemic in Finland |
| `2008-04` | `357` | `64` | `4,664` | `54186` | Suomen historialliset maakunnat | Historical provinces of Finland |
| `2008-02` | `345` | `48` | `5,561` | `12639` | Suvi Lindén | Suvi Lindén |
| `2012-01` | `326` | `49` | `3,580` | `18561` | Pekka Haavisto | Pekka Haavisto |
| `2007-11` | `322` | `44` | `6,122` | `436339` | Jokelan koulusurmat | Jokela school shooting |
| `2015-09` | `310` | `24` | `2,635` | `1137` | Sauli Niinistö | Sauli Niinistö |

### 4. `research.hotspot_category` (Top 10 of `50` rows)

| Rank | `category` | `articles` | `contest_score` | `reverts` | `dispute_reverts` | `episodes` |
|---:|---|---:|---:|---:|---:|---:|
| 1 | Kansanedustajat | `2,435` | `1,275.0530` | `5,561` | `2,275` | `592` |
| 2 | Suomen pääministerit | `43` | `214.5304` | `1,603` | `554` | `145` |
| 3 | Suomen valtiovarainministerit | `74` | `177.8201` | `1,168` | `386` | `97` |
| 4 | Suomalaiset feministit | `120` | `162.0313` | `562` | `250` | `68` |
| 5 | Eduskuntavaalit | `41` | `154.6960` | `369` | `228` | `69` |
| 6 | Suomen ulkoasiainministerit | `41` | `151.7860` | `1,166` | `480` | `110` |
| 7 | Suomen presidentinvaalit | `25` | `151.5568` | `567` | `357` | `100` |
| 8 | Suomen politiikka 2010-luvulla | `36` | `136.0466` | `473` | `240` | `95` |
| 9 | Suomen politiikka 2020-luvulla | `35` | `124.0943` | `467` | `221` | `87` |
| 10 | Suomen historia | `80` | `100.3568` | `432` | `196` | `72` |
