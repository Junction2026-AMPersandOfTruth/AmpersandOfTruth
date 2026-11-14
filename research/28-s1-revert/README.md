# P1.S1 (#28) · Precomputed Identity Reverts, Rule-Based Classes & Baseline Signals Variant

Precomputes canonical SHA-1 identity reverts, rule-based revert classes, deduplicated reverted edits, and per-page /
per-user aggregates across all **862,076** revisions (**19,742** Finnish Wikipedia articles) in Dataset M into
`research.s1_*`, plus the contract §2.2 **baseline signals variant** (`variant.py`) that later P1.S experiments must beat.

## Files & Commands

| File | Purpose | Command |
|---|---|---|
| `build.py` | Drops and rebuilds `research.s1_revert`, `s1_reverted_edit`, `s1_page_stats`, `s1_user_stats`; verifies invariants; writes `results/build_timings.json` | `python3 research/28-s1-revert/build.py` |
| `variant.py` | Contract §2.2 `signals` variant over `s1_revert` + `s1_reverted_edit` (zero window functions at request time) | `python3 research/eval/validate.py variant signals -- python3 research/28-s1-revert/variant.py` |
| `report.py` | Computes class/year/exclusion/demo-topic metrics (`results/report_data.json`) and writes 20 seeded masked rows per class (`results/sample_<class>.md`) | `python3 research/28-s1-revert/report.py` |
| `results/sample_<class>.md` | 20 deterministically seeded (`s1-28-<class>`) revert samples per class with fiwiki diff links, masked users/summaries, bytes, and concise English summary gists | Reviewed for misclassification |

---

## Precomputed Tables (`research.s1_*`)

1. **`research.s1_revert`** (`46,049` rows, PK `reverting_rev_id`, indexed on `(page_id)`, `(page_id, class)`, `(reverting_user)`):
   - Required columns: `page_id, reverting_rev_id, restored_rev_id, first_reverted_rev_id, last_reverted_rev_id, reverted_count, reverting_user, reverting_anonymous, reverted_user_count, reverted_users_anonymous_only, revert_ts, first_reverted_ts, seconds_to_revert, r_bytes, net_bytes, comment_family, reinserted_rev_id, class, exclude_reason`.
   - Added columns: `owned_reverted_count` (count of rows in `s1_reverted_edit` owned by this revert), `reverting_comment` (enables `variant.py` to build `edits[]` without joining `corpus.revision`).
2. **`research.s1_reverted_edit`** (`58,886` rows, PK `rev_id`, indexed on `(page_id)`, `(reverting_rev_id)`, `(user)`):
   - Required columns: `page_id, rev_id, reverting_rev_id, user, anonymous, ts, bytes_delta, survival_seconds`.
   - Added columns: `parent_rev_id` (predecessor in non-null page order), `comment`.
   - Each reverted edit belongs to its **earliest non-excluded reverting R** only (`0` duplicate `rev_id`s; `0` null revisions).
3. **`research.s1_page_stats`** (`19,742` rows — every corpus article, PK `page_id`):
   - `page_id, title, revision_count, excluded_count, bot_count, self_revert_count, edit_war_count, dispute_count, graffiti_cleanup_count, other_count, total, non_excluded_total, signal_total, last_5y_total, last_5y_signal_total, first_revert_ts, last_revert_ts`.
   - `last_5y_*` is anchored deterministically to Dataset M's snapshot end (`2026-10-01 10:59:12` minus 5 years = `revert_ts >= '2021-10-01 10:59:12'`).
4. **`research.s1_user_stats`** (`73,436` rows — every non-null `user` in `corpus.revision`, PK `user` with `COLLATE utf8mb4_bin`):
   - Required columns: `user, edits, reverted_edits, reverts_made, distinct_pages`.
   - Added columns for P1.S6: `anonymous, is_bot, non_null_edits, reverted_edits_by_others, reverts_made_others, first_edit_ts, last_edit_ts`.

---

## Exact Definitions & Classification Thresholds

### Canonical Revert & Re-insertion Definitions
- **Null-revision removal:** Per `page_id` ordered by `(ts, rev_id)`, any revision whose non-null `sha1` equals its immediate predecessor's `sha1` (`5,796` rows: page moves, protection logs, FlaggedRevs dummy entries) is dropped before assigning sequence numbers `seq = 1..N` (`856,280` non-null revisions).
- **Identity revert:** Revision `R` at `r_seq` with `R.sha1 IS NOT NULL` is an identity revert when an earlier revision `P` on the same page has `P.sha1 = R.sha1` and `p_seq < r_seq - 1`, taking the latest such `P` (`restored_rev_id`). The non-null revisions strictly between `P` and `R` (`p_seq < seq < r_seq`) form `R`'s **reverted span** (`reverted_count = r_seq - p_seq - 1`).
- **Nested-revert ownership:** In `s1_reverted_edit`, each reverted edit is assigned to its **earliest** non-excluded reverting `R` (`ORDER BY r_seq ASC`). Because `r_seq - 1` immediately precedes `R`, every non-excluded revert is guaranteed to own at least `last_reverted_rev_id` (`0` non-excluded reverts have `owned_reverted_count = 0`).
- **Re-insertion (`reinserted_rev_id`):** Earliest revision after `R` (`seq > r_seq`) on the same page whose `sha1` matches a non-empty (`bytes > 0`) revision in `R`'s reverted span, computed in O(N) via `LEAD(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq)`. Requiring `se.bytes > 0` prevents unrelated 0-byte page blankings years apart (`32` cases) from masquerading as content re-insertions.

### Rule-Based Classification Cascade (First Match Wins)
1. **`excluded`** — `exclude_reason` is non-null:
   - `'blanking_collision'`: `r_bytes = 0` or `restored` has `bytes <= 100` (catches empty-page blanking SHA-1 collisions and short `#OHJAUS` redirect collisions; checked before `wide_span` when both hold).
   - `'wide_span'`: `reverted_count > 15` (`span > 15` non-null revisions).
2. **`bot`** — reverting user matches bot naming (`anonymous = 0` and case-insensitive `bot(ti)?($|[0-9~_)-])` plus explicit non-suffix bots from `prof_ed` §5.4 / SUL interwiki bots: `Lab-ot, CommonsDelinker, ʬʭ, Overlord, Loveless, Ptbotgourou, RobotJcb, BOTarate, Bot-Schafter, BotMultichill, BotSottile, BotdeSki`, avoiding human accounts like `Bothnia` or `Botaurus`), **or** every revision in the span is by a bot.
3. **`self_revert`** — `reverting_user IS NOT NULL` and every revision in the span has `user = reverting_user` (`utf8mb4_bin`).
4. **`edit_war`** — either:
   - `R` belongs to a 48-hour window (`<= 172,800 s`) on `page_id` with `>= 3` non-excluded, non-bot, non-self reverts involving the same unordered user pair `{U1, U2}` (`U1 <> U2`) in **both directions** (`U1 -> U2` and `U2 -> U1`), **or**
   - `R`'s reverted span itself contains a non-excluded, non-bot, non-self revert (captures counter-reverts, including multi-patroller vs single re-inserter chains).
5. **`dispute`** — any reverted revision in the span is by a named account (`anonymous = 0 AND user IS NOT NULL`; note that the `18` `rev_deleted` rows with `user IS NULL` have `anonymous = 0` in MariaDB and are guarded against), **or** `reinserted_rev_id IS NOT NULL`.
6. **`graffiti_cleanup`** — `reverted_users_anonymous_only = 1` (all reverted edits have `anonymous = 1`, covering IPv4, IPv6, and `~2025-*`/`~2026-*` temporary accounts) and `seconds_to_revert < 86,400` (`< 24 h`).
7. **`other`** — remaining reverts (`2,246` rows: almost entirely anonymous/temporary-only spans surviving `>= 24 h`, plus rare `user IS NULL` suppressed-user spans).

---

## Reconciliation with Profiling (`prof_rev` / `prof_ed`)

- **`s1_revert` total = `46,049`**, matching `prof_rev`'s `46,049` canonical reverts to the exact revision. Why? Dropping null revisions (`sha1 = immediate_prev_sha1`) removes consecutive duplicate-hash log entries; any true revert `R` had `R.sha1 <> prev.sha1` before and after dropping null revisions. However, dropping null revisions first fixes two subtle span artifacts compared to `prof_rev_reverts`:
  - **`407` reverts** shift `restored_rev_id` earlier past a null log revision to the actual content revision that created that state.
  - **`820` reverts** have shorter, cleaner `reverted_count` because **`839` null revisions** sitting inside reverted spans are no longer counted as reverted edits.
- **`s1_reverted_edit` total = `58,886`** vs `prof_rev`'s `64,966`:
  - `64,966` (profiling raw) − `839` (null revisions inside spans) = **`64,127`** distinct non-null reverted revisions across all `46,049` reverts.
  - Excluding `class = 'excluded'` (`172` reverts) from claiming rows in `s1_reverted_edit` saves **`5,241`** distinct legitimate revisions (`64,127 - 58,886 = 5,241`) from false blanking/wide-span attribution (such as `1,106` normal edits across 15 years of *Vanhoillislestadiolaisuus*), and rescues **`13` genuine vandal edits** (e.g. on *Urho Kekkonen*) where a second vandal blanked the page before a patroller reverted both.

---

## Results

### 1. Counts per Class & Auto-Summary Family Share

Auto-summary families `1..6` follow `prof_ed` §4.3 (`1` = MediaWiki Rollback, `2` = Modern Undo, `3` = FlaggedRevs Reject, `4` = Legacy Undo, `5` = EN gadget / Popups, `6` = 2004–06 Rollback). Across all `46,049` reverts, `32,743` (`71.10%`) match families 1–6 (exact match to `prof_ed` §4.3).

| Class | Reverts | % Reverts | `s1_reverted_edit` rows | Raw span slots | Auto-summary (`1–6`) | Auto-summary % | F1 Rollback | F2 Undo | F3 FlaggedRevs | F4 Undo old | F5 EN/Popups | F6 Old RB | Reinserted |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `excluded` | 172 | 0.37% | 0 *(saved)* | 6,776 | 44 | 25.58% | 14 | 11 | 12 | 3 | 3 | 1 | 98 |
| `bot` | 643 | 1.40% | 716 | 736 | 133 | 20.68% | 25 | 51 | 0 | 57 | 0 | 0 | 34 |
| `self_revert` | 3,628 | 7.88% | 4,150 | 4,195 | 938 | 25.85% | 125 | 691 | 0 | 122 | 0 | 0 | 167 |
| `edit_war` | 6,568 | 14.26% | 8,613 | 10,535 | 3,547 | 54.00% | 1,090 | 1,560 | 553 | 314 | 25 | 5 | 3,643 |
| `dispute` | 9,552 | 20.74% | 13,566 | 13,612 | 6,270 | 65.64% | 1,982 | 1,861 | 1,904 | 475 | 34 | 14 | 1,994 |
| `graffiti_cleanup` | 23,240 | 50.47% | 28,720 | 28,775 | 20,249 | 87.13% | 11,836 | 2,542 | 4,166 | 1,359 | 297 | 49 | 0 |
| `other` | 2,246 | 4.88% | 3,121 | 3,132 | 1,562 | 69.55% | 256 | 323 | 899 | 79 | 4 | 1 | 0 |
| **Total** | **46,049** | **100.00%** | **58,886** | **67,761** | **32,743** | **71.10%** | **15,328** | **7,039** | **7,534** | **2,409** | **363** | **70** | **5,936** |

### 2. Impact of `exclude_reason`

| `exclude_reason` | Reverts removed | `r_bytes = 0` | `r_bytes` 1–100 B | `r_bytes > 100` B | Raw span slots removed | Distinct edits saved from `s1_reverted_edit` |
|---|---:|---:|---:|---:|---:|---:|
| `blanking_collision` | 128 | 34 | 94 | 0 | 5,354 | — |
| `wide_span` (`span > 15`) | 44 | 0 | 0 | 44 | 1,422 | — |
| **Total excluded** | **172** | **34** | **94** | **44** | **6,776** | **5,241** *(+ 13 real vandal edits rescued)* |

*(Note: 19 of the 128 `blanking_collision` rows also had `span > 15`, up to 1,106 edits on Vanhoillislestadiolaisuus.)*

### 3. Counts per Year (2003–2026)

| Year | Reverts | `excluded` | `bot` | `self_revert` | `edit_war` | `dispute` | `graffiti_cleanup` | `other` | Reverted edits (by revert yr) | Reverted edits (by edit yr) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 2003 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 | 2 |
| 2004 | 136 | 4 | 0 | 22 | 13 | 24 | 69 | 4 | 181 | 182 |
| 2005 | 835 | 8 | 0 | 98 | 98 | 134 | 477 | 20 | 1,151 | 1,155 |
| 2006 | 2,418 | 31 | 0 | 233 | 284 | 494 | 1,318 | 58 | 3,175 | 3,182 |
| 2007 | 3,701 | 26 | 70 | 346 | 530 | 710 | 1,930 | 89 | 4,931 | 4,927 |
| 2008 | 3,435 | 30 | 21 | 294 | 480 | 712 | 1,816 | 82 | 4,303 | 4,327 |
| 2009 | 2,679 | 8 | 15 | 272 | 297 | 508 | 1,447 | 132 | 3,431 | 3,454 |
| 2010 | 2,329 | 4 | 97 | 224 | 281 | 415 | 1,206 | 102 | 2,933 | 2,952 |
| 2011 | 2,210 | 8 | 50 | 194 | 280 | 351 | 1,225 | 102 | 2,804 | 2,870 |
| 2012 | 2,275 | 12 | 33 | 168 | 483 | 447 | 1,067 | 65 | 2,769 | 2,919 |
| 2013 | 2,140 | 4 | 267 | 148 | 362 | 390 | 928 | 41 | 2,743 | 2,482 |
| 2014 | 1,763 | 5 | 7 | 142 | 418 | 385 | 772 | 34 | 2,194 | 2,199 |
| 2015 | 2,176 | 4 | 5 | 141 | 328 | 463 | 1,159 | 76 | 2,769 | 2,769 |
| 2016 | 2,066 | 2 | 4 | 131 | 294 | 420 | 1,047 | 168 | 2,727 | 2,747 |
| 2017 | 2,055 | 1 | 1 | 135 | 312 | 443 | 1,079 | 84 | 2,574 | 2,560 |
| 2018 | 1,998 | 2 | 5 | 137 | 332 | 417 | 1,022 | 83 | 2,599 | 2,612 |
| 2019 | 2,460 | 1 | 3 | 133 | 385 | 579 | 1,191 | 168 | 3,118 | 3,143 |
| 2020 | 2,408 | 3 | 1 | 188 | 357 | 564 | 979 | 316 | 3,012 | 2,982 |
| 2021 | 1,702 | 5 | 2 | 115 | 204 | 482 | 793 | 101 | 2,094 | 2,072 |
| 2022 | 1,604 | 2 | 35 | 107 | 176 | 418 | 759 | 107 | 2,070 | 2,069 |
| 2023 | 1,898 | 1 | 15 | 150 | 213 | 394 | 1,000 | 125 | 2,563 | 2,560 |
| 2024 | 1,629 | 5 | 2 | 92 | 210 | 339 | 865 | 116 | 2,076 | 2,077 |
| 2025 | 1,390 | 4 | 7 | 115 | 148 | 302 | 709 | 105 | 1,782 | 1,769 |
| 2026 *(to Oct 1)* | 741 | 2 | 3 | 42 | 83 | 161 | 382 | 68 | 886 | 875 |
| **Total** | **46,049** | **172** | **643** | **3,628** | **6,568** | **9,552** | **23,240** | **2,246** | **58,886** | **58,886** |

*(Notice the 2013 `bot` spike = `267` reverts from `Addbot` / `EmausBot` Wikidata interwiki migration, cleanly isolated into `bot`.)*

### 4. Build Time per Step (`build.py`)

Every SQL step runs sequentially under `SET SESSION max_statement_time = 85;` on the 2-vCPU corpus server:

| Step | Time (s) |
|---|---:|
| 1. Drop previous staging & target tables | 0.53 |
| 2. Order revisions per page, drop null revisions, compute SHA-1 lag/lead | 10.35 |
| 3. Detect canonical identity reverts (`P.sha1 = R.sha1`, latest `P < R - 1`) | 1.89 |
| 4. Aggregate span editor flags, bot/self flags, and re-insertion seq per revert | 6.51 |
| 5. Detect 48-hour mutual-revert edit-war user pairs | 7.14 |
| 6. Build `research.s1_reverted_edit` (earliest non-excluded reverting R per edit) | 8.36 |
| 7. Build `research.s1_revert` (auto-summary family 1–6, re-insertion, 7-way class) | 2.24 |
| 8. Build `research.s1_page_stats` (all 19,742 corpus pages) | 2.27 |
| 9. Build `research.s1_user_stats` (exact `utf8mb4_bin` usernames) | 9.56 |
| 10. Clean up staging tables | 0.53 |
| 11. Verify invariants (uniqueness, zero null revisions, zero orphan reverts) | 7.87 |
| **Total wall-clock** | **57.25** |

### 5. Demo-Topic Sanity Check (`prof_top/findings.md` §3 Core Articles)

| Topic / Core Article | `page_id` | Revs | `excl` | `bot` | `self` | `edit_war` | `dispute` | `graffiti` | `other` | Total | Signal (`ew+disp+graf+oth`) | Last 5y signal |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| **Topic 1 · 1918 Civil War** | | | | | | | | | | | | |
| Suomen sisällissota | 1150 | 3,357 | 0 | 1 | 13 | 55 | 68 | 201 | 6 | 344 | 330 | 35 |
| Carl Gustaf Emil Mannerheim | 6021 | 2,400 | 2 | 2 | 13 | 43 | 62 | 203 | 2 | 327 | 310 | 38 |
| Punakaarti | 9186 | 384 | 0 | 0 | 2 | 2 | 10 | 14 | 0 | 28 | 26 | 4 |
| Suojeluskunta | 7675 | 380 | 0 | 0 | 2 | 6 | 3 | 15 | 5 | 31 | 29 | 6 |
| Suomen sisällissodan nimet | 250972 | 194 | 0 | 0 | 1 | 2 | 4 | 9 | 3 | 19 | 18 | 4 |
| **Topic 2 · Continuation War** | | | | | | | | | | | | |
| Jatkosota | 440 | 1,671 | 1 | 0 | 7 | 53 | 39 | 118 | 2 | 220 | 212 | 18 |
| Risto Ryti | 11398 | 739 | 0 | 0 | 5 | 12 | 14 | 50 | 2 | 83 | 78 | 8 |
| Sotasyyllisyysoikeudenkäynti | 105157 | 586 | 0 | 0 | 2 | 3 | 6 | 9 | 0 | 20 | 18 | 8 |
| Itä-Karjalan keskitysleirit | 319344 | 202 | 0 | 0 | 2 | 6 | 6 | 5 | 0 | 19 | 17 | 5 |
| Suomalainen Waffen-SS-vapaaehtoispataljoona | 136099 | 459 | 0 | 1 | 1 | 2 | 2 | 2 | 0 | 8 | 6 | 0 |
| Naarajärven sotavankileiri | 622103 | 151 | 0 | 0 | 4 | 2 | 2 | 1 | 0 | 9 | 5 | 0 |
| **Topic 3 · Winter War** | | | | | | | | | | | | |
| Talvisota | 1256 | 2,143 | 0 | 0 | 20 | 34 | 62 | 208 | 4 | 328 | 308 | 33 |
| Simo Häyhä | 24889 | 915 | 0 | 0 | 8 | 28 | 33 | 179 | 3 | 251 | 243 | 39 |
| Moskovan rauhansopimus (1940) | 52097 | 212 | 0 | 0 | 0 | 4 | 2 | 10 | 1 | 17 | 17 | 1 |
| Mainilan laukaukset | 8295 | 177 | 0 | 0 | 1 | 6 | 4 | 5 | 0 | 16 | 15 | 2 |
| **Topic 4 · Ceded Karelia** | | | | | | | | | | | | |
| Karjala | 6626 | 572 | 0 | 0 | 2 | 53 | 24 | 26 | 1 | 106 | 104 | 5 |
| Suur-Suomi | 24094 | 668 | 0 | 1 | 1 | 24 | 23 | 15 | 2 | 66 | 64 | 7 |
| Viipuri | 1333 | 799 | 0 | 0 | 5 | 5 | 8 | 30 | 1 | 49 | 44 | 10 |
| Karjala-kysymys | 19086 | 669 | 0 | 0 | 0 | 4 | 13 | 18 | 0 | 35 | 35 | 4 |
| Käkisalmi | 12374 | 272 | 0 | 0 | 1 | 11 | 7 | 4 | 0 | 23 | 22 | 3 |
| Luovutettu Karjala | 9895 | 223 | 0 | 0 | 1 | 4 | 8 | 8 | 0 | 21 | 20 | 1 |

---

## Baseline Signals Variant (`variant.py`)

- **Filter:** `class IN ('dispute', 'edit_war', 'graffiti_cleanup', 'other')`.
- **Score:** `log1p(max(net_bytes, 0)) + (1 if class in {'dispute', 'edit_war'} else 0) + (1 if reinserted_rev_id is not None else 0)`.
- **Queries:** 2 indexed SQL lookups (`s1_revert` for `page_ids`, then `s1_reverted_edit` for only the top `limit` `reverting_rev_id`s); zero window functions and zero `corpus.revision` / `revision_text` touches at request time.
- **Validation & Latency (over SSH tunnel to VPS):**
  - Default contract validator (`examples/signals.request.json`): **0.4 s**, `OK` (`0` warnings/errors).
  - Topic 1 core 4 articles (`1150, 6021, 9186, 7675` — `695` candidate reverts): **0.8 s**, `OK` (`50` incidents).
  - 20 core articles across Topics 1–4 (`1,901` candidate reverts): **1.1 s**, `OK` (`50` incidents; well below the `< 5 s` budget).

## Caveats for Downstream Tasks (S3, S4, S5, S6, R4-D)

1. **Partial reverts & manual rewordings are invisible to SHA-1:** As seen on *Suomalainen Waffen-SS-vapaaehtoispataljoona* (`0` SHA-1 signal reverts in the last 5 years despite active post-2019 historiography edits), nuanced edits undone via partial edits or talk-page-driven rewrites change the page hash and require P1.S5 (summary-based undo detection: ~`4,992` non-SHA-1 undo/rollback edits noted in `prof_ed` §4.4) and P1.S3 (token/diff survival).
2. **`graffiti_cleanup` volume vs `score`:** `graffiti_cleanup` makes up `50.47%` of all reverts (`23,240`), and large copy-paste additions by anonymous users have high `log1p(max(net_bytes, 0))`, outscoring small-byte named-editor disputes by `+2..+7` points in the naive S1 baseline — a primary target for P1.S3/S5/S9 triage to improve precision on.
3. **`other` class (`>= 24 h` anonymous reverts) is high-value:** Unlike sub-hour `graffiti_cleanup`, `other` (`2,246` reverts) frequently captures subtle unsourced numerical/biographical insertions that survived weeks or months before FlaggedRevs or talk-page review (see `results/sample_other.md`).
4. **Rare localized gadget summaries:** A handful of 2010–2014 reverts use Finnish-localized Navigation Popups (`Palautti sivun käyttäjän ... tekemään versioon <id> popupilla`), which falls outside `prof_ed` §4.3's 6 core regexes (`comment_family IS NULL`) — worth adding in P1.S5.
