# P1.S4 (#31) · Edit-War, Ping-Pong & Burst Conflict Episodes + Page Contestedness

Precomputes sustained content-conflict episodes (`edit_war`, `ping_pong`, `burst`) across all **862,076** revisions
(**856,280** non-null revisions across **19,742** Finnish Wikipedia articles) on top of the P1.S1 (`#28`) tables in
`research.s1_*`, together with per-revision conflict membership (`research.s4_episode_rev`) and a per-article
contestedness score + peak conflict month (`research.s4_page_contest`) for retrieval re-ranking (P1.R4-D), the article
timeline (P2.6), and the hot-spots dashboard (P4.6).

## Contents & How to Run

| File | Purpose |
|---|---|
| `build.py` | Idempotent 11-step builder (`DROP` + `CREATE` for `research.s4_episode`, `research.s4_episode_rev`, `research.s4_page_contest`; cleans up `research.s4__*` staging tables; asserts invariants; writes `results/build_timings.json`). |
| `report.py` | Queries `research.s4_*` and `research.s1_*`, masks all summaries (`aot.mask_comment`) and editor counts (`aot.user_display`), and writes `results/report_data.json` + 10 seeded episodes per kind (`results/sample_*.md`). |
| `results/build_timings.json` | Sequential per-step wall times, invariant checks, and `5× median` vs `3σ` burst comparison counts. |
| `results/report_data.json` | Aggregate distributions, cross-kind overlaps, Top 30 pages, all 21 demo-topic core articles & episodes, and anniversary-alignment stats (privacy-safe: zero raw usernames or unmasked comments). |
| `results/sample_edit_war.md` | 10 seeded `edit_war` (`7d` default) episodes with `fi.wikipedia.org` diff links, masked summaries, and English gists. |
| `results/sample_ping_pong.md` | 10 seeded `ping_pong` episodes with diff + state-representative links, masked summaries, and English gists. |
| `results/sample_burst.md` | 10 seeded `burst` episodes with diff links, masked summaries, and English gists. |

```bash
# Ensure corpus SSH tunnel is running on 127.0.0.1:3307 and MYSQL_PWD / CORPUS_PW is exported:
python3 research/31-s4-conflict/build.py
python3 research/31-s4-conflict/report.py
```

### Downstream SQL Usage Patterns

```sql
-- 1. Ask "is this candidate edit inside a conflict episode?" (default 7d edit_war + ping_pong + burst):
SELECT e.episode_id, e.kind, e.start_ts, e.end_ts, e.revert_count, e.protection_hint, e.score
FROM research.s4_episode_rev er
JOIN research.s4_episode e USING (episode_id)
WHERE er.rev_id = @candidate_rev_id
  AND e.is_default = 1;

-- 2. Re-rank retrieved candidate articles by contestedness (P1.R4-D) or render hot-spots (P4.6):
SELECT page_id, episodes_edit_war, episodes_ping_pong, episodes_burst, contested_score, peak_month
FROM research.s4_page_contest
WHERE page_id IN (...);
```

---

## Tables & Scoring Formulas

### 1. `research.s4_episode` (`5,788` rows total; `5,178` with `is_default = 1`)
- **PK:** `episode_id INT UNSIGNED` · **Indexes:** `idx_page_id (page_id)`, `idx_kind (kind)`, `idx_page_default (page_id, is_default)`
- **Columns:** `episode_id, page_id, kind` (`'edit_war'|'ping_pong'|'burst'`), `start_ts, end_ts`, `` `window` `` (`'24h'|'7d'|'30d'|NULL` — note backticks when querying in MariaDB 12.3 because `WINDOW` is a SQL keyword), `is_default` (added helper: `1` for `edit_war` `'7d'`, `ping_pong`, and `burst`; `0` for `edit_war` `'24h'` and `'30d'`), `revert_count` (count of `s1_revert` rows with `class NOT IN ('excluded','bot','self_revert')` inside the episode), `named_participants`, `anon_participants`, `participant_users` (raw comma-separated non-bot `user` strings — stored in DB table only, never committed), `states` (`ping_pong`: distinct alternating SHA-1 count `>= 2`; `NULL` otherwise), `state_rev_ids` (`ping_pong`: comma-separated representative `rev_id` per alternating state), `first_rev_id, last_rev_id, edit_count` (non-null revisions in `[first_rev_id .. last_rev_id]`), `protection_hint` (`0|1`), `score`.
- **Episode `score` formula:**
  - `edit_war`: `3.0 + 1.5 * ln(1 + revert_count) + 0.8 * ln(1 + named_participants) + 1.0 * protection_hint`
  - `ping_pong`: `2.0 + 1.2 * ln(1 + revert_count) + 0.6 * ln(1 + named_participants) + 0.5 * max(states - 1, 1) + 1.0 * protection_hint`
  - `burst`: `1.0 + 0.6 * ln(1 + edit_count) + 1.2 * ln(1 + revert_count) + 0.4 * ln(1 + named_participants) + 1.0 * protection_hint`

### 2. `research.s4_episode_rev` (`82,909` `(episode_id, rev_id)` rows; `65,500` distinct revisions in `is_default = 1`)
- **PK:** `(episode_id, rev_id)` · **Index:** `idx_rev_id (rev_id)`
- Contains every non-null revision inside `[first_rev_id .. last_rev_id]` of each episode (`0` orphan revisions, `0` MediaWiki null revisions where `sha1 = parent.sha1`, and `COUNT(*) = edit_count` for all `5,788` episodes). Note that `441` `rev_deleted` revisions (`sha1 IS NULL`) present in `s1_reverted_edit` remain eligible as non-null revisions when they occur inside a conflict stretch, matching `s1_reverted_edit` semantics.

### 3. `research.s4_page_contest` (`19,742` rows — 1:1 with `corpus.article` / `s1_page_stats`)
- **PK:** `page_id INT UNSIGNED` · **Index:** `idx_contested_score (contested_score)`
- **Columns:** `page_id, episodes_edit_war` (default `7d`), `episodes_ping_pong, episodes_burst, contested_score, peak_month` (`'YYYY-MM'` or `NULL`), plus added columns `episodes_edit_war_24h, episodes_edit_war_30d, protected_episodes`.
- **`contested_score` formula** (`4,168` pages > `0`, `2,254` pages with ≥ 1 default episode, `15,574` quiet pages at `0.0`):
  ```text
  contested_score = round(
      4.0 * ln(1 + episodes_edit_war)
    + 1.5 * ln(1 + ew_revert_sum)
    + 2.2 * ln(1 + episodes_ping_pong)
    + 1.2 * ln(1 + reverted_bursts)
    + 0.3 * ln(1 + peaceful_bursts)
    + 1.5 * ln(1 + protected_episodes)
    + 1.2 * ln(1 + s1.edit_war_count + 0.5 * s1.dispute_count),
    4
  )
  ```
  *Why this weighting:* Sublinear `ln(1 + x)` scaling prevents overlapping `edit_war` + `ping_pong` + `burst` detections on the same incident from exploding multiplicatively. Burst episodes *with* reverts (`reverted_bursts`, weight `1.2`) receive **4×** the weight of zero-revert collaborative bursts (`peaceful_bursts`, weight `0.3`), preventing Featured Article drives or routine election updates from outranking disputed pages. Schoolyard `graffiti_cleanup` (`23,240` S1 reverts) contributes **0.0** unless an actual content ping-pong or burst fight occurs, while S1's non-graffiti `edit_war_count` and `dispute_count` separate focused lower-traffic controversy articles (e.g., *Mainilan laukaukset*, *Karjala-kysymys*, *Suomen sisällissodan nimet*) from genuinely uncontested stubs (`0.0`).
- **`peak_month`:** `NULL` when `contested_score = 0`; otherwise the `'YYYY-MM'` maximizing monthly weighted conflict activity (`3.0*(1+revert_count)` per `edit_war`, `2.0*(1+revert_count)` per `ping_pong`, `1.5*(1+revert_count)` per reverted `burst`, `0.25` per peaceful `burst`, plus `1.5` / `0.75` per S1 `edit_war` / `dispute` revert in that month).

---

## Detection Rules & Threshold Calibration

### 1. `edit_war` (`24h`, `7d`, `30d` sliding windows — **`7d` chosen as default**)
- **Input:** `s1_revert` rows with `class NOT IN ('excluded', 'bot', 'self_revert')` where a named non-bot editor (`reverting_anonymous = 0`, `s1_user_stats.is_bot = 0`) reverts ≥ 1 distinct named non-bot editor (`ed_user != reverting_user`) inside the revert's non-null span `(restored_seq, reverting_seq)`.
  - *Why anchor windows on named-vs-named reverts rather than counting incidental IP graffiti reverts toward the `≥ 3` threshold:* On high-traffic biographies (e.g., *Tarja Halonen*), counting unrelated `graffiti_cleanup` reverts inside a `30d` window inflates revert totals from `1,835` to `2,976` and chains 2-revert named exchanges with unrelated classroom vandalism weeks later into 83-day stretches. Requiring ≥ 3 named-vs-named reverts in `[t0, t0 + W]` with at least one mutual pair (`A → B` and `B → A` both in the window) captures both 2-editor wars and multi-patroller vs named-editor wars (`A ↔ B` plus `C → B`) cleanly.
- **Span start bound:** An episode starts at the earliest reverted edit within `W` prior to the window's first revert (`ed_ts >= first_revert_ts - W`), falling back to `first_revert_seq` when editors war today over reverting a dormant edit from months earlier (preventing a 20-minute revert war over a 4-month-old edit from dragging intermediate bot/unrelated revisions into `s4_episode_rev`). Overlapping windows (in timestamp or sequence) are merged per `(page_id, window)`.
- **Why `7d` is the default window:**
  - `24h` (`270` episodes on `212` pages, median `7.16 h`) misses multi-day slow-burn revert exchanges where named Finnish Wikipedians revert once per evening (`prof_rev` §Q3 showed `16.9%` of named reverted edits survive `1–30 days`). Moving `24h → 7d` recovers **+55 episodes (+20.4%)** and **+47 articles (+22.2%)**, including *Suojeluskunta* (`2020-06`), *Timo Soini* (`2` episodes vs `0` at `24h`), and multi-day wars on *Suomen sisällissota* and *Käkisalmi*.
  - `30d` (`340` episodes on `279` pages) adds only `+15` net episodes (`+4.6%`) over `7d` while **3.5×-ing mean duration** (`55.6 h → 191.8 h`, p90 `6.1 d → 26.4 d`, max `57.4 d`) and pulling `+3,121` mostly non-reverted intermediate edits into `s4_episode_rev` by chaining separate monthly disputes on busy pages (*Karjala* merges from `5` to `4` episodes; *Suur-Suomi* merges from `2` to `1`). **`7d`** hits the sweet spot.

### 2. `ping_pong` (interleaved SHA-1 state alternation)
- **Rule:** On the non-null revision sequence per page, filter candidate states to `sha1 IS NOT NULL AND bytes > 100` (matching S1's `blanking_collision` guard so `0 B` page blankings and `≤ 100 B` redirect stubs cannot collide across unrelated vandals). Track trajectories `[pre_state(s_0)] + active_edits[s_1 .. s_k]` where consecutive visits to recurring states have **≤ 3 unrelated non-null edits between them** (`max_gap_edits = 3`) and **≤ 7 days between active state transitions** (`max_step = 7d`).
- **True interleaving check:** Consecutive visits to the *same* state (`A → X → A`) are collapsed before counting state occurrences; at least **2 distinct states must each appear ≥ 2 times** in the collapsed sequence (`A → B → A → B…`), eliminating false positives from two separate ordinary reverts back-to-back (`A → X → A` then `C → Y → C`).
- **Pre-state baseline & editor diversity:** Treating `s_0` (the live revision before the fight) as the pre-state allows `A(pre) → B → A → B` to be detected even if baseline `A` was written months earlier, while keeping `s_0` itself *out* of `[first_rev_id .. last_rev_id]` so innocent baseline authors are never marked as conflict participants and episode durations reflect the actual fight (`median = 15.05 h`, `max = 26.2 d`). Stretches require **≥ 2 masked non-bot participants** (`aot.user_display`, collapsing `/16` IPv4 and `/32` IPv6 ranges per `docs/evidence-standard.md` §3).

### 3. `burst` (`> 5×` trailing 90-day median vs `> mean + 3σ` — **intersection settled on**)
- **Empirical comparison across all 19,742 pages** (on non-null, non-bot daily edit counts `human_edits ≥ 10`):

  | Burst candidate rule (trailing 90 calendar days `[d-90 .. d-1]`) | Qualifying page-days | Merged episodes | Key behavior observed |
  |---|---:|---:|---|
  | `human_edits ≥ 10` (all human editors ≥ 1, raw `> 5× median_90d`) | `5,489` | `4,718` | Includes **`2,040` single-editor solo-drafting days** (`99.80%` [`2,036 / 2,040`] have **0 reverts**). |
  | `human_edits > mean_90d + 3σ_90d` **without** `≥ 10` floor (`≥ 5` edits, `≥ 2` editors) | `11,678` | — | Fires on quiet stubs (`mean ≈ 0, σ ≈ 0`) for routine 5-edit updates (`> 19,900` days without `≥ 2` editor floor). |
  | `human_edits ≥ 10` AND `masked_human_editors ≥ 2` AND **`> 5× median_90d`** | `3,434` | `2,979` | Only **`66 / 3,434` (`1.92%`)** days have `median_90d > 0` because 99% of articles are unedited on ≥ 45 of 90 calendar days (`5 × 0 = 0`), letting routine 12-edit days pass during multi-month campaigns (*Koronaviruspandemia Suomessa* +15 d, *Hanoi Rocks* +8 d). |
  | **Chosen:** `human_edits ≥ 10` AND `masked_human_editors ≥ 2` AND **`> mean_90d + 3σ_90d`** *(100% subset of `> 5× median_90d`)* | **`3,164`** | **`2,843`** | **Every single one** of the `3,164` days (`100.0%`) satisfies **both** `> 5× median_90d` **and** `> mean_90d + 3σ_90d`, while `3σ` cleanly prunes `270` routine high-baseline days on chronically active pages. Consecutive burst days merge into one episode. |

### 4. `protection_hint` Calibration
- Triggered (`protection_hint = 1`) if **either**:
  1. **Explicit protection summary (`528` total corpus hits, `469` after excluding unprotection notes like `-suojattu` / `ei enää ole suojattu`):** Any revision on `page_id` (including MediaWiki null log revisions `Suojasi sivun [[...]]`, which account for `306 / 530` protection comments!) in `[start_ts - 1h, end_ts + 72h]` whose section-stripped comment matches `(^|[^[:alpha:]])(suojas|suojattu|puolisuoja)` (`COLLATE utf8mb4_uca1400_as_ci`).
  2. **Post-burst 14-day anonymous-edit drop (`kind = 'burst'`, `end_ts ≤ 2026-09-17`):** Naive `anon_edits_next_14d = 0` fires on **`56.0%` (`1,600 / 2,857`)** of all bursts (`2.4%` overlap with explicit protection logs) because most Finnish history articles naturally go quiet after a burst. Requiring **`anon_edits_in_burst ≥ 2` AND `anon_edits_next_14d = 0` AND `named_edits_next_14d ≥ 2`** (anons were active during the burst, then dropped to zero for ≥ 14 days while named/autoconfirmed editors continued editing) cuts false positives by **9.2×** (`1,600 → 173`) and raises protection-log overlap density **3.9×** (`2.4% → 9.2%`), yielding `248` total burst protection hints (`8.72%`).

### 5. Anniversary Alignment on Demo Articles (Note Only)
- Across all **`109`** burst episodes on the **21** core demo articles (`prof_top` §3):
  - **Exact `MM-DD` recurrence on the same page across 23 years is rare (`3` pairs total, consistent with chance birthday collisions):** *Suomen sisällissota* on `05-15` (`2007`, `2008` — Civil War end / *Kaatuneitten muistopäivä* eve) and `12-08` (`2006`, `2014`), plus *Talvisota* on `12-04` (`2009`, `2019` — 70th and 80th Winter War outbreak week).
  - **Loose `±3 day` clustering appears around major national/war commemorations, especially round-number decennial anniversaries:** Finnish Independence Day `Dec 6 ±3d` (`8` bursts across *Talvisota* `2007, 2009×2, 2019`, *Suomen sisällissota* `2006, 2014`, *Mannerheim* `2010`, *Viipuri* `2006`), Civil War end `May 15 ±3d` (`6` bursts across *Suomen sisällissota* `2007, 2008, 2014`, *Simo Häyhä* `2018`, *Waffen-SS* `2009`, *Karjala* `2008`), Winter War outbreak `Nov 30 ±3d` (`3` bursts, plus a `2009-11-30` 70th-anniversary `ping_pong` on *Talvisota*), Moscow Peace `Mar 13 ±3d` (`3` bursts, plus a `2020-03-12..13` 80th-anniversary `ping_pong` on *Moskovan rauhansopimus (1940)*), and Civil War outbreak `Jan 27 ±3d` (`3` bursts). Continuation War outbreak (`Jun 25 ±3d`) had `0` bursts. **Conclusion:** bursts respond to decennial media attention and Independence Day/May commemoration weeks rather than recurring on fixed calendar dates every year.

---

## Build Performance & Invariants (`< 90 s` per step, sequential)

Measured on the shared 2-vCPU VPS (`SET SESSION max_statement_time = 85;` enforced on every SQL call). Both an uncontended run (`69.68 s` total, max step `20.71 s`) and the final rebuild under concurrent team DB load (`234.43 s` total, max step `50.02 s < 90 s`) completed well inside the `< 90 s` per-step limit:

| Step | Uncontended (s) | Under shared load (s) |
|---|---:|---:|
| 1. Drop previous `s4` staging & target tables | `2.66` | `15.17` |
| 2. Materialize non-null revision sequence (`s4__seq`) with S1 bot flags | `8.52` | `20.07` |
| 3. Detect `edit_war` episodes (`24h`, `7d`, `30d`) from `s1_revert` | `5.72` | `28.01` |
| 4. Detect `ping_pong` episodes from interleaved SHA-1 states | `20.71` | `50.02` |
| 5. Detect `burst` episodes (trailing 90d median & `3σ`) | `6.81` | `15.92` |
| 6. Stage episode spans & index protection log revisions | `7.81` | `48.55` |
| 7. Build `research.s4_episode_rev` (`82,909` rows) | `1.69` | `10.93` |
| 8. Build `research.s4_episode` (`5,788` rows) | `2.16` | `16.61` |
| 9. Build `research.s4_page_contest` (`19,742` pages) | `0.59` | `8.31` |
| 10. Clean up `s4__*` staging tables | `2.91` | `8.74` |
| 11. Verify invariants (corpus rev existence, zero null revs, counts) | `10.11` | `12.09` |
| **Total** | **`69.68`** | **`234.43`** |

Verified invariants (`results/build_timings.json`): `missing_in_corpus_revision = 0`, `null_revs_in_episode_rev = 0`, `edit_count_mismatch = 0`, `page_contest_rows = 19,742`.

---

## Episode Counts, Distributions & Overlaps

### Counts per Kind and Window

| Kind (`window`) | Default? | Episodes | Distinct pages | Total edits | Total reverts | Episodes w/ ≥1 revert | `protection_hint = 1` |
|---|---:|---:|---:|---:|---:|---:|---:|
| `edit_war` (`24h`) | `0` | `270` | `212` | `2,630` | `1,305` | `270` (`100.0%`) | `23` (`8.52%`) |
| **`edit_war` (`7d`)** | **`1`** | **`325`** | **`259`** | **`5,099`** | **`1,845`** | **`325` (`100.0%`)** | **`29` (`8.92%`)** |
| `edit_war` (`30d`) | `0` | `340` | `279` | `8,220` | `2,372` | `340` (`100.0%`) | `37` (`10.88%`) |
| **`ping_pong`** (`NULL`) | **`1`** | **`2,010`** | **`1,096`** | **`12,183`** | **`7,757`** | **`2,009` (`99.95%`)** | **`155` (`7.71%`)** |
| **`burst`** (`NULL`) | **`1`** | **`2,843`** | **`1,668`** | **`54,777`** | **`3,501`** | **`956` (`33.63%`)** | **`248` (`8.72%`)** |
| **Default set (`is_default = 1`)** | — | **`5,178`** | **`2,254`** | **`65,500` distinct** | — | **`3,290` (`63.54%`)** | **`432` (`8.34%`)** |

*`ping_pong` distinct alternating `states` breakdown (`n = 2,010`):* `2` states = `1,762` (`87.66%`), `3` = `151` (`7.51%`), `4` = `65` (`3.23%`), `5` = `16`, `6` = `9`, `7` = `5`, `8` = `1` (*Jatkosota* Dec 2015), `9` = `1`.

### Distribution of Length (Edits & Duration) and Participants

| Group | `edit_count` min / p50 / p90 / max | Duration (hours) min / p50 / p90 / max | `revert_count` min / p50 / p90 / max | `named_participants` min / p50 / p90 / max | Masked participants (`aot.user_display`) min / p50 / p90 / max |
|---|---|---|---|---|---|
| `edit_war` (`24h`) | `3 / 6.0 / 16.0 / 91` | `0.02 / 7.16 / 24.67 / 53.86` | `3 / 4.0 / 8.1 / 33` | `2 / 3.0 / 5.0 / 21` | `2 / 3.0 / 6.0 / 31` |
| **`edit_war` (`7d`)** | **`3 / 7.0 / 23.6 / 551`** | **`0.02 / 25.04 / 145.91 / 461.45`** | **`3 / 4.0 / 9.0 / 53`** | **`2 / 3.0 / 7.6 / 106`** | **`2 / 3.0 / 9.0 / 165`** |
| `edit_war` (`30d`) | `3 / 8.0 / 52.3 / 710` | `0.02 / 58.53 / 632.64 / 1377.38` | `3 / 4.0 / 11.1 / 79` | `2 / 3.0 / 12.0 / 120` | `2 / 3.0 / 14.1 / 186` |
| **`ping_pong`** | **`3 / 5.0 / 10.0 / 52`** | **`0.01 / 15.05 / 124.20 / 627.98`** | **`0 / 3.0 / 7.0 / 48`** | **`0 / 2.0 / 4.0 / 13`** | **`2 / 3.0 / 5.0 / 21`** |
| **`burst`** | **`10 / 14.0 / 33.0 / 585`** | **`0.05 / 6.74 / 21.03 / 109.37`** | **`0 / 0.0 / 4.0 / 37`** | **`0 / 2.0 / 6.0 / 103`** | **`1* / 3.0 / 7.0 / 157`** |

*\*One `burst` episode has `masked_participants = 1` where two compressed/uncompressed IPv6 representations differed in raw SQL `SUBSTRING_INDEX` before Python `/32` normalization (`p25 = 2.0`, `p50 = 3.0`).*

### Cross-Kind Revision Overlap & S1 Reverted-Edit Selectivity (`is_default = 1`)

- **Revision overlap (`65,500` distinct non-null revisions in default episodes):** `edit_war:7d` (`5,099` revs) overlaps `ping_pong` (`12,183` revs) on **`2,301` revisions** (`45.1%` of `edit_war` revs) and overlaps `burst` (`54,777` revs) on **`2,681` revisions** (`52.6%`); `ping_pong` and `burst` share `2,430` revisions; `853` revisions belong to all three simultaneously.
- **Selectivity on S1 candidate reverted edits (`s1_reverted_edit` joined with `s1_revert` signal classes):**
  - `edit_war` class: **`6,834 / 8,613` (`79.35%`)** fall inside an S4 default episode.
  - `dispute` class: **`3,740 / 13,566` (`27.57%`)** fall inside an S4 default episode.
  - `graffiti_cleanup` class: **`2,781 / 28,720` (`9.68%`)** fall inside an S4 default episode (**8.2×** lower than `edit_war`).
  - `other` class (`≥ 24 h` lone anonymous reverts): **`27 / 3,121` (`0.87%`)** — confirming that slow-surviving subtle anonymous insertions happen outside noisy flare-ups and need P1.S3/S5/S9 rather than episode filters.

---

## Top 30 Pages by `contested_score` (of `19,742` corpus pages)

| Rank | `page_id` | Article | `contested_score` | `peak_month` | `ew_7d` (`24h`/`30d`) | `ping_pong` | `burst` | `prot_eps` | S1 `ew` / `dispute` |
|---:|---:|---|---:|---|---:|---:|---:|---:|---:|
| 1 | `1187` | Tarja Halonen | `32.4247` | `2008-02` | `4` (`2`/`4`) | `23` | `21` | `8` | `112 / 126` |
| 2 | `1137` | Sauli Niinistö | `31.7849` | `2013-06` | `4` (`3`/`3`) | `17` | `15` | `14` | `129 / 131` |
| 3 | `18561` | Pekka Haavisto | `30.2884` | `2012-01` | `3` (`4`/`4`) | `19` | `9` | `10` | `93 / 60` |
| 4 | `1069` | Suomen tasavallan presidentti | `28.6364` | `2012-02` | `4` (`4`/`4`) | `24` | `9` | `5` | `68 / 83` |
| 5 | `1150` | Suomen sisällissota | `28.1747` | `2007-08` | `4` (`3`/`4`) | `17` | `20` | `4` | `55 / 68` |
| 6 | `200797` | Axel Olof Freudenthal | `26.9642` | `2006-07` | `3` (`4`/`3`) | `9` | `4` | `8` | `58 / 24` |
| 7 | `6626` | Karjala | `25.5938` | `2008-05` | `5` (`6`/`4`) | `12` | `6` | `0` | `53 / 24` |
| 8 | `436339` | Jokelan koulusurmat | `25.0532` | `2007-11` | `2` (`2`/`2`) | `9` | `8` | `5` | `18 / 42` |
| 9 | `440` | Jatkosota | `24.2609` | `2015-12` | `2` (`2`/`3`) | `16` | `6` | `3` | `53 / 39` |
| 10 | `1445320` | Suomen Kansa Ensin | `24.1091` | `2018-11` | `2` (`1`/`2`) | `11` | `5` | `7` | `39 / 31` |
| 11 | `1401247` | Turun isku 2017 | `23.7050` | `2018-10` | `3` (`3`/`3`) | `10` | `3` | `4` | `31 / 23` |
| 12 | `1121620` | Sanna Marin | `23.3747` | `2023-03` | `2` (`2`/`3`) | `7` | `9` | `3` | `16 / 75` |
| 13 | `1633208` | Mona Bling | `23.0519` | `2021-08` | `3` (`2`/`2`) | `4` | `2` | `5` | `34 / 10` |
| 14 | `11396` | Alexander Stubb | `22.7140` | `2014-06` | `1` (`1`/`1`) | `14` | `12` | `7` | `52 / 52` |
| 15 | `102657` | Jaama | `22.7052` | `2006-08` | `2` (`3`/`2`) | `6` | `4` | `7` | `23 / 10` |
| 16 | `926229` | Juha Sipilä | `22.3350` | `2019-03` | `5` (`3`/`5`) | `8` | `2` | `0` | `23 / 39` |
| 17 | `1679936` | Sinimusta Liike | `21.7728` | `2026-01` | `2` (`2`/`2`) | `8` | `4` | `6` | `25 / 20` |
| 18 | `1275` | Urho Kekkonen | `21.3123` | `2006-07` | `1` (`1`/`1`) | `13` | `15` | `3` | `45 / 64` |
| 19 | `24094` | Suur-Suomi | `20.6594` | `2005-08` | `2` (`2`/`1`) | `8` | `7` | `0` | `24 / 23` |
| 20 | `752` | Mauno Koivisto | `20.4494` | `2011-09` | `2` (`2`/`2`) | `12` | `6` | `0` | `29 / 24` |
| 21 | `1170256` | Anne Berner | `20.2427` | `2015-09` | `2` (`1`/`2`) | `7` | `3` | `4` | `14 / 15` |
| 22 | `1257152` | Katri Kulmuni | `20.2227` | `2021-12` | `2` (`3`/`2`) | `3` | `4` | `2` | `33 / 9` |
| 23 | `10907` | Ilkka Kanerva | `20.1087` | `2008-03` | `1` (`3`/`1`) | `7` | `6` | `0` | `27 / 29` |
| 24 | `109123` | Marja Tiura | `19.9798` | `2011-04` | `1` (`1`/`1`) | `6` | `4` | `5` | `52 / 14` |
| 25 | `23596` | Irwin Goodman | `19.9669` | `2023-09` | `2` (`2`/`1`) | `5` | `4` | `3` | `31 / 29` |
| 26 | `41622` | Timo Soini | `19.9024` | `2010-12` | `2` (`0`/`2`) | `8` | `5` | `0` | `37 / 62` |
| 27 | `10858` | Jyrki Katainen | `19.8838` | `2013-05` | `1` (`1`/`1`) | `5` | `11` | `4` | `38 / 19` |
| 28 | `439` | Johannes Virolainen | `19.4149` | `2009-01` | `4` (`5`/`4`) | `4` | `1` | `0` | `26 / 11` |
| 29 | `9059` | Osmo Soininvaara | `19.1677` | `2009-02` | `3` (`1`/`2`) | `5` | `2` | `1` | `9 / 13` |
| 30 | `72145` | Suomalaisuuden liitto | `19.1421` | `2007-05` | `1` (`3`/`1`) | `8` | `3` | `1` | `24 / 20` |

---

## Top 4 Demo Topics (`prof_top` §3): Contestedness Ranks & Complete Episode List

All **21** core articles across Topics 1–4 rank within the **top 6.0%** (`#5` to `#1,189` out of `19,742` pages; `15,574` pages are tied at `0.0000`). Participant counts below are strictly **masked participant counts** (`aot.user_display`, collapsing `/16` IPv4 and `/32` IPv6 ranges; editor names are never shown).

### Summary Ranks Across All 19,742 Pages

| Topic | Article (`page_id`) | Rank / 19,742 | `contested_score` | `peak_month` | `ew` `24h/7d/30d` | `ping_pong` | `burst` | Default episodes |
|---|---|---:|---:|---|---:|---:|---:|---:|
| 1 | Suomen sisällissota (`1150`) | **#5** | `28.1747` | `2007-08` | `3 / 4 / 4` | `17` | `20` | `41` |
| 1 | Carl Gustaf Emil Mannerheim (`6021`) | **#41** | `17.0173` | `2007-10` | `0 / 0 / 1` | `13` | `22` | `35` |
| 1 | Suojeluskunta (`7675`) | **#209** | `9.2797` | `2020-06` | `0 / 1 / 1` | `1` | `0` | `2` |
| 1 | Punakaarti (`9186`) | **#1,123** | `2.4953` | `2005-04` | `0 / 0 / 0` | `0` | `0` | `0` |
| 1 | Suomen sisällissodan nimet (`250972`) | **#1,189** | `2.1393` | `2017-08` | `0 / 0 / 0` | `0` | `1` | `1` |
| 2 | Jatkosota (`440`) | **#9** | `24.2609` | `2015-12` | `2 / 2 / 3` | `16` | `6` | `24` |
| 2 | Risto Ryti (`11398`) | **#79** | `14.0614` | `2005-08` | `1 / 1 / 1` | `7` | `2` | `10` |
| 2 | Suomalainen Waffen-SS-vapaaehtoispataljoona (`136099`) | **#250** | `8.4564` | `2012-05` | `1 / 1 / 1` | `1` | `3` | `5` |
| 2 | Naarajärven sotavankileiri (`622103`) | **#395** | `5.8761` | `2019-06` | `0 / 0 / 0` | `1` | `2` | `3` |
| 2 | Itä-Karjalan keskitysleirit (`319344`) | **#431** | `5.3880` | `2009-10` | `0 / 0 / 0` | `2` | `1` | `3` |
| 2 | Sotasyyllisyysoikeudenkäynti (`105157`) | **#451** | `5.2293` | `2023-12` | `0 / 0 / 0` | `1` | `6` | `7` |
| 3 | Simo Häyhä (`24889`) | **#60** | `15.3065` | `2009-12` | `1 / 1 / 1` | `5` | `4` | `10` |
| 3 | Talvisota (`1256`) | **#89** | `13.6589` | `2007-05` | `0 / 0 / 1` | `9` | `17` | `26` |
| 3 | Mainilan laukaukset (`8295`) | **#159** | `10.5138` | `2015-11` | `1 / 1 / 1` | `2` | `0` | `3` |
| 3 | Moskovan rauhansopimus (1940) (`52097`) | **#548** | `4.5671` | `2020-03` | `0 / 0 / 0` | `2` | `0` | `2` |
| 4 | Karjala (`6626`) | **#7** | `25.5938` | `2008-05` | `6 / 5 / 4` | `12` | `6` | `23` |
| 4 | Suur-Suomi (`24094`) | **#19** | `20.6594` | `2005-08` | `2 / 2 / 1` | `8` | `7` | `17` |
| 4 | Käkisalmi (`12374`) | **#36** | `17.2688` | `2006-09` | `2 / 3 / 2` | `4` | `1` | `8` |
| 4 | Karjala-kysymys (`19086`) | **#309** | `7.7818` | `2005-07` | `0 / 0 / 0` | `3` | `6` | `9` |
| 4 | Viipuri (`1333`) | **#321** | `7.4674` | `2009-10` | `0 / 0 / 0` | `2` | `4` | `6` |
| 4 | Luovutettu Karjala (`9895`) | **#428** | `5.4093` | `2013-02` | `0 / 0 / 0` | `1` | `1` | `2` |

### All Default Episodes on the 21 Demo Core Articles (`n = 237`; compact format `dates · kind · rev=<revert_count> · mp=<masked_participants>`)

#### Topic 1 · Finnish Civil War (1918)
- **Suomen sisällissota** (`41` episodes): `2005-08-17` burst (`rev=0, mp=6`); `2005-09-02` burst (`rev=0, mp=2`); `2005-09-29..30` burst (`rev=0, mp=7`); `2005-10-20` burst (`rev=0, mp=5`); `2006-02-01` burst (`rev=0, mp=3`); `2006-04-02..07` edit_war (`rev=5, mp=15`); `2006-04-05` burst (`rev=0, mp=6`); `2006-04-07` ping_pong (`rev=3, mp=2`); `2006-08-10` burst (`rev=0, mp=2`); `2006-08-12..13` burst (`rev=0, mp=4`); `2006-12-08` burst (`rev=0, mp=3`); `2007-05-15` burst (`rev=4, mp=6, prot=1`); `2007-05-17` ping_pong (`rev=2, mp=3`); `2007-08-07` burst (`rev=3, mp=5`); `2007-08-07` ping_pong (`rev=2, mp=3`); `2007-08-09..10` burst (`rev=10, mp=7`); `2007-08-09` ping_pong (`rev=3, mp=2`); `2007-08-09` ping_pong (`rev=3, mp=2`); `2008-02-03` burst (`rev=0, mp=3`); `2008-02-24..25` ping_pong (`rev=3, mp=4`); `2008-03-25..26` edit_war (`rev=6, mp=2`); `2008-03-25..26` ping_pong (`rev=6, mp=2`); `2008-03-28` burst (`rev=0, mp=2`); `2008-05-06` burst (`rev=4, mp=4`); `2008-05-06` edit_war (`rev=4, mp=3`); `2008-05-06` ping_pong (`rev=4, mp=3`); `2008-05-15` burst (`rev=2, mp=4`); `2008-05-15` ping_pong (`rev=2, mp=2`); `2008-10-12..13` ping_pong (`rev=3, mp=3`); `2010-02-08..11` edit_war (`rev=3, mp=4`); `2010-02-08..11` ping_pong (`rev=3, mp=3`); `2014-01-02..05` ping_pong (`rev=5, mp=2, states=4`); `2014-01-16` burst (`rev=5, mp=4, prot=1`); `2014-01-16` ping_pong (`rev=3, mp=2, prot=1`); `2014-05-18` burst (`rev=0, mp=3`); `2014-12-08` burst (`rev=0, mp=3`); `2015-05-15..17` ping_pong (`rev=3, mp=3, states=3`); `2015-11-08` burst (`rev=0, mp=3`); `2017-08-28` ping_pong (`rev=4, mp=3`); `2019-02-06..11` ping_pong (`rev=11, mp=6, states=6, prot=1`); `2023-03-01..08` ping_pong (`rev=3, mp=5`).
- **Carl Gustaf Emil Mannerheim** (`35` episodes): `2005-11-16` burst (`rev=3, mp=5`); `2005-12-22` burst (`rev=1, mp=4`); `2006-06-07` burst (`rev=0, mp=3`); `2006-09-14` ping_pong (`rev=2, mp=3`); `2006-09-30` burst (`rev=1, mp=4, prot=1`); `2006-11-28` burst (`rev=1, mp=4`); `2006-11-30` burst (`rev=1, mp=6`); `2006-12-14` burst (`rev=0, mp=3`); `2007-03-27` burst (`rev=0, mp=4`); `2007-04-06` burst (`rev=0, mp=3`); `2007-04-13` burst (`rev=1, mp=5`); `2007-10-17..29` ping_pong (`rev=11, mp=11, states=3`); `2007-10-20` burst (`rev=6, mp=5`); `2008-01-20` burst (`rev=0, mp=5`); `2008-01-28` burst (`rev=1, mp=7`); `2008-02-01` burst (`rev=2, mp=5`); `2008-02-01` ping_pong (`rev=2, mp=2`); `2008-03-04..08` ping_pong (`rev=3, mp=5`); `2008-03-10` burst (`rev=0, mp=7`); `2008-10-10` burst (`rev=0, mp=2`); `2010-11-02` burst (`rev=1, mp=3`); `2010-12-07` burst (`rev=1, mp=4`); `2012-02-12..21` ping_pong (`rev=9, mp=12, states=4`); `2012-02-29` burst (`rev=3, mp=6`); `2012-08-18..26` ping_pong (`rev=6, mp=11`); `2013-11-03..05` ping_pong (`rev=5, mp=5, states=4`); `2015-05-03..10` ping_pong (`rev=4, mp=4`); `2015-09-09..16` ping_pong (`rev=5, mp=6`); `2016-04-11..12` ping_pong (`rev=3, mp=3`); `2017-06-17..19` ping_pong (`rev=7, mp=5`); `2018-12-10` burst (`rev=2, mp=2, prot=1`); `2020-05-24..27` ping_pong (`rev=8, mp=5`); `2020-11-05..06` ping_pong (`rev=9, mp=10`); `2020-11-05` burst (`rev=6, mp=9`); `2025-01-08` burst (`rev=3, mp=2, prot=1`).
- **Suojeluskunta** (`2` episodes): `2020-06-13..16` edit_war (`rev=4, mp=2`); `2020-06-13..16` ping_pong (`rev=4, mp=2`).
- **Punakaarti** (`0` episodes; ranked `#1,123` via S1 `edit_war`/`dispute` reverts).
- **Suomen sisällissodan nimet** (`1` episode): `2006-11-05` burst (`rev=0, mp=3`).

#### Topic 2 · Continuation War, Finland–Germany, East Karelia & Waffen-SS
- **Jatkosota** (`24` episodes): `2005-07-05` ping_pong (`rev=14, mp=8, states=6, prot=1`); `2005-07-05` burst (`rev=14, mp=9, prot=1`); `2005-08-19..20` ping_pong (`rev=5, mp=3`); `2007-01-24` burst (`rev=3, mp=5`); `2007-07-06..07` ping_pong (`rev=2, mp=2`); `2008-02-08` burst (`rev=3, mp=4`); `2008-11-12` ping_pong (`rev=3, mp=4`); `2010-01-19..24` ping_pong (`rev=3, mp=4`); `2010-08-27..28` ping_pong (`rev=5, mp=3`); `2012-11-10..18` ping_pong (`rev=2, mp=2`); `2012-11-30` ping_pong (`rev=3, mp=4`); `2013-06-27..28` ping_pong (`rev=4, mp=3, states=3`); `2014-01-09..14` ping_pong (`rev=11, mp=5`); `2014-01-14` burst (`rev=7, mp=4, prot=1`); `2014-04-23..24` ping_pong (`rev=3, mp=2`); `2014-09-09..10` ping_pong (`rev=2, mp=3`); `2015-12-23..24` burst (`rev=10, mp=3`); `2015-12-23..24` edit_war (`rev=10, mp=3`); `2015-12-23..24` ping_pong (`rev=10, mp=3, states=8`); `2016-02-08..09` edit_war (`rev=4, mp=3`); `2016-02-08..09` ping_pong (`rev=4, mp=3, states=3`); `2016-02-09` burst (`rev=4, mp=3`); `2018-03-26..27` ping_pong (`rev=1, mp=2`); `2021-12-20` ping_pong (`rev=1, mp=2`).
- **Risto Ryti** (`10` episodes): `2005-08-27` edit_war (`rev=3, mp=3`); `2005-08-27` ping_pong (`rev=3, mp=3`); `2006-12-11` burst (`rev=4, mp=4`); `2007-10-20` ping_pong (`rev=4, mp=5`); `2012-02-12..13` ping_pong (`rev=2, mp=3`); `2013-11-03..05` ping_pong (`rev=3, mp=4, states=3`); `2015-09-11..23` ping_pong (`rev=5, mp=7`); `2021-10-16` burst (`rev=0, mp=2`); `2025-02-28..03-01` ping_pong (`rev=2, mp=3`); `2025-03-30..04-04` ping_pong (`rev=3, mp=3`).
- **Suomalainen Waffen-SS-vapaaehtoispataljoona** (`5` episodes): `2009-05-18` burst (`rev=0, mp=3`); `2012-05-31` edit_war (`rev=3, mp=3`); `2012-05-31` ping_pong (`rev=3, mp=3`); `2020-03-08..09` burst (`rev=0, mp=2`); `2022-11-03` burst (`rev=0, mp=3`).
- **Naarajärven sotavankileiri** (`3` episodes): `2009-01-11` burst (`rev=0, mp=3`); `2019-06-12..13` ping_pong (`rev=2, mp=2, prot=1`); `2019-06-13` burst (`rev=4, mp=4, prot=1`).
- **Itä-Karjalan keskitysleirit** (`3` episodes): `2008-09-25..26` burst (`rev=0, mp=4`); `2009-02-11` ping_pong (`rev=3, mp=3`); `2009-10-14` ping_pong (`rev=3, mp=3, states=3`).
- **Sotasyyllisyysoikeudenkäynti** (`7` episodes): `2008-12-21` burst (`rev=0, mp=2`); `2009-04-23` burst (`rev=0, mp=2`); `2009-11-17` burst (`rev=0, mp=2`); `2010-11-06` burst (`rev=0, mp=2`); `2023-12-29..30` burst (`rev=4, mp=3`); `2023-12-29..30` ping_pong (`rev=4, mp=3, states=3`); `2026-09-28` burst (`rev=0, mp=2`).

#### Topic 3 · Winter War, Shelling of Mainila & Moscow Peace
- **Simo Häyhä** (`10` episodes): `2009-12-11..12` edit_war (`rev=3, mp=2`); `2009-12-11..12` ping_pong (`rev=3, mp=2`); `2010-12-20` burst (`rev=1, mp=5`); `2012-06-04..10` ping_pong (`rev=4, mp=3`); `2013-06-09..15` ping_pong (`rev=5, mp=5`); `2016-01-11` burst (`rev=2, mp=5`); `2016-03-10` burst (`rev=5, mp=5`); `2017-07-18..24` ping_pong (`rev=4, mp=5`); `2018-05-15` burst (`rev=5, mp=6`); `2023-01-15` ping_pong (`rev=3, mp=2`).
- **Talvisota** (`26` episodes): `2005-08-18..20` ping_pong (`rev=3, mp=3`); `2006-09-04` burst (`rev=4, mp=4`); `2006-09-14` burst (`rev=0, mp=2`); `2006-10-02` burst (`rev=0, mp=4`); `2006-12-18..19` burst (`rev=1, mp=6`); `2007-03-02` burst (`rev=6, mp=5`); `2007-05-09` ping_pong (`rev=3, mp=3`); `2007-05-31..06-06` ping_pong (`rev=5, mp=6`); `2007-07-16` burst (`rev=0, mp=3`); `2007-12-07` burst (`rev=4, mp=7`); `2008-01-11` burst (`rev=5, mp=6`); `2008-01-27` burst (`rev=3, mp=8`); `2008-12-19` burst (`rev=0, mp=2`); `2009-11-30..12-01` ping_pong (`rev=3, mp=3`); `2009-12-04` burst (`rev=0, mp=3`); `2009-12-06` burst (`rev=1, mp=5`); `2009-12-15` burst (`rev=1, mp=2`); `2010-08-27..28` ping_pong (`rev=7, mp=3`); `2010-11-04..05` ping_pong (`rev=4, mp=4`); `2013-06-26..30` ping_pong (`rev=6, mp=5, states=3`); `2014-04-23..24` ping_pong (`rev=3, mp=2`); `2017-11-24` burst (`rev=4, mp=4`); `2018-01-01` ping_pong (`rev=4, mp=3`); `2019-08-24` burst (`rev=0, mp=2`); `2019-12-04` burst (`rev=3, mp=5`); `2022-11-05` burst (`rev=4, mp=5`).
- **Mainilan laukaukset** (`3` episodes): `2015-11-25` edit_war (`rev=5, mp=2`); `2015-11-25` ping_pong (`rev=5, mp=2`); `2015-12-21` ping_pong (`rev=2, mp=2`).
- **Moskovan rauhansopimus (1940)** (`2` episodes): `2015-05-16` ping_pong (`rev=3, mp=3`); `2020-03-12..13` ping_pong (`rev=4, mp=5`).

#### Topic 4 · Ceded Karelia, Vyborg & Greater Finland
- **Karjala** (`23` episodes): `2005-07-29` burst (`rev=0, mp=4`); `2007-07-18..19` edit_war (`rev=4, mp=3`); `2007-07-18..19` ping_pong (`rev=4, mp=3`); `2007-08-30` ping_pong (`rev=4, mp=3`); `2007-08-30` burst (`rev=4, mp=3`); `2007-10-15` burst (`rev=0, mp=2`); `2008-04-15..16` edit_war (`rev=4, mp=4`); `2008-04-15..16` ping_pong (`rev=4, mp=4, states=4`); `2008-05-08..23` edit_war (`rev=21, mp=9`); `2008-05-08..09` ping_pong (`rev=7, mp=3, states=3`); `2008-05-09` burst (`rev=5, mp=4`); `2008-05-14` burst (`rev=3, mp=4`); `2008-05-14..22` ping_pong (`rev=8, mp=6, states=7`); `2008-05-25` burst (`rev=1, mp=2`); `2008-05-26` ping_pong (`rev=2, mp=3`); `2008-10-14..18` edit_war (`rev=6, mp=5`); `2008-10-17..20` ping_pong (`rev=6, mp=5`); `2009-08-02..03` edit_war (`rev=3, mp=3`); `2009-08-02..03` ping_pong (`rev=3, mp=3`); `2009-11-29` ping_pong (`rev=3, mp=2`); `2011-01-04..05` ping_pong (`rev=6, mp=4`); `2012-06-12..18` ping_pong (`rev=12, mp=6`); `2016-04-14..16` ping_pong (`rev=2, mp=3`).
- **Suur-Suomi** (`17` episodes): `2005-07-25` burst (`rev=4, mp=4`); `2005-07-25..08-01` edit_war (`rev=5, mp=11`); `2005-07-25` ping_pong (`rev=4, mp=4, states=3`); `2005-08-27` burst (`rev=12, mp=11`); `2005-08-27` ping_pong (`rev=5, mp=3`); `2005-08-27..30` edit_war (`rev=18, mp=15`); `2005-08-27` ping_pong (`rev=2, mp=2`); `2005-08-28` ping_pong (`rev=2, mp=2`); `2005-08-29` burst (`rev=2, mp=7`); `2005-08-30` ping_pong (`rev=4, mp=4`); `2005-09-08` ping_pong (`rev=3, mp=2`); `2005-12-10` ping_pong (`rev=2, mp=3`); `2006-03-14` ping_pong (`rev=2, mp=2`); `2006-03-14` burst (`rev=2, mp=4`); `2006-04-30` burst (`rev=0, mp=4`); `2006-05-19` burst (`rev=0, mp=3`); `2007-03-27` burst (`rev=0, mp=2`).
- **Käkisalmi** (`8` episodes): `2006-09-03..04` edit_war (`rev=5, mp=8`); `2006-09-03` ping_pong (`rev=4, mp=3`); `2006-09-03` burst (`rev=4, mp=7`); `2006-09-12..14` edit_war (`rev=3, mp=4`); `2006-09-12..14` ping_pong (`rev=3, mp=4`); `2007-07-13..25` edit_war (`rev=6, mp=4`); `2007-07-13..25` ping_pong (`rev=6, mp=4, states=4`); `2008-08-23..24` ping_pong (`rev=2, mp=2`).
- **Karjala-kysymys** (`9` episodes): `2004-12-23` burst (`rev=0, mp=3`); `2005-07-16` burst (`rev=0, mp=2`); `2005-07-20` burst (`rev=2, mp=5`); `2005-07-26` ping_pong (`rev=2, mp=2`); `2005-09-18` burst (`rev=2, mp=8`); `2006-01-12` burst (`rev=0, mp=3`); `2006-04-15..16` ping_pong (`rev=3, mp=3`); `2009-10-22` burst (`rev=0, mp=3`); `2013-12-12` ping_pong (`rev=2, mp=2`).
- **Viipuri** (`6` episodes): `2006-12-03` burst (`rev=0, mp=4, prot=1`); `2007-04-09` burst (`rev=0, mp=2`); `2009-10-08` burst (`rev=3, mp=5`); `2009-10-08` ping_pong (`rev=3, mp=4`); `2015-04-17..21` ping_pong (`rev=4, mp=4`); `2025-09-14` burst (`rev=0, mp=3`).
- **Luovutettu Karjala** (`2` episodes): `2013-02-14..15` ping_pong (`rev=4, mp=4, prot=1`); `2015-11-08` burst (`rev=0, mp=2`).

---

## Sampled Episodes (10 per Kind, Seeded — Full Masked Summaries in `results/sample_*.md`)

### `edit_war` (`7d` default, seed `'s4-31-edit_war'`, [`results/sample_edit_war.md`](results/sample_edit_war.md)) — **10 / 10 substantive editorial or policy conflicts**

| # | Article (`episode_id`) | Dates (UTC) | Diff link | One-line English gist (from `aot.mask_comment`) |
|---:|---|---|---|---|
| 1 | Jatkosota (`276`) | `2015-12-23..24` | [diff=15412511](https://fi.wikipedia.org/w/index.php?diff=15412511) | 19 edits (10 reverts, 3 participants) — dispute over whether Finland & Germany conducted coordinated pre-war preparation (`koordinoidut toimet jatkosotaa varten`). |
| 2 | Tarja Halonen (`298`) | `2008-01-18..23` | [diff=4137156](https://fi.wikipedia.org/w/index.php?diff=4137156) | 23 edits (14 reverts, 11 participants) — 5-day biography revert volley via auto-rollbacks and undos. |
| 3 | Käkisalmi (`343`) | `2006-09-12..14` | [diff=1620929](https://fi.wikipedia.org/w/index.php?diff=1620929) | 5 edits (3 reverts, 4 participants) — dispute over Karelian/Russian renaming phrasing (`Nimenmuutoksen toteaminen samoin kuin Primorskin (ent. Koivisto)`). |
| 4 | Demi (lehti) (`449`) | `2006-06-19` | [diff=1275924](https://fi.wikipedia.org/w/index.php?diff=1275924) | 16 edits (6 reverts, 10 participants) — external-link (`Aiheesta muualla`) revert exchange. |
| 5 | Koverin keskitysleiri (`483`) | `2010-03-26..28` | [diff=8233897](https://fi.wikipedia.org/w/index.php?diff=8233897) | 14 edits (7 reverts, 4 participants) — camp-conditions (`Olot leirillä`) framing dispute over postwar memoir labels. |
| 6 | Sampsa Kataja (`485`) | `2011-03-18..19` | [diff=9933684](https://fi.wikipedia.org/w/index.php?diff=9933684) | 5 edits (3 reverts, 3 participants) — sourced-content revert dispute (`rv (lähteellistä asiaa)`) in section `Vaalirahoitus ja Keva`. |
| 7 | Laukaansuu (`495`) | `2019-08-21` | [diff=18356772](https://fi.wikipedia.org/w/index.php?diff=18356772) | 5 edits (4 reverts, 2 participants) — back-and-forth FlaggedRevs rejections and undos on Ingrian locality article. |
| 8 | Luettelo keisarillisista vierailuista Suomessa (`503`) | `2008-12-27` | [diff=6019277](https://fi.wikipedia.org/w/index.php?diff=6019277) | 12 edits (6 reverts, 3 participants) — dispute over flag icons and links in section headers (`kts. keskustelu`). |
| 9 | Juha Sipilä (`532`) | `2016-08-16..22` | [diff=15891324](https://fi.wikipedia.org/w/index.php?diff=15891324) | 11 edits (6 reverts, 9 participants) — repeated non-neutral commentary insertions and undos around basic-income trial. |
| 10 | Mikkel Näkkäläjärvi (`562`) | `2019-05-20..24` | [diff=18162419](https://fi.wikipedia.org/w/index.php?diff=18162419) | 60 edits (16 reverts, 20 participants + protection hint) — NPOV dispute in `Yksityiselämä` ending in page protection. |

### `ping_pong` (seed `'s4-31-ping_pong'`, [`results/sample_ping_pong.md`](results/sample_ping_pong.md)) — **9 / 10 content/category/abuse tug-of-wars + 1 procedural template undo**

| # | Article (`episode_id`) | Dates (UTC) | Diff link (`states`) | One-line English gist (from `aot.mask_comment`) |
|---:|---|---|---|---|
| 1 | Jatkosota (`956`) | `2013-06-27..28` | [diff=13350166](https://fi.wikipedia.org/w/index.php?diff=13350166) (`3`) | 8 edits (4 reverts, 3 participants) — alternating edits and FlaggedRevs/auto-rollback reverts in `Tapahtumat juuri ennen sodan alkua`. |
| 2 | Timo Soini (`1757`) | `2010-12-02` | [diff=9466240](https://fi.wikipedia.org/w/index.php?diff=9466240) (`2`) | 14 edits (12 reverts, 9 participants) — rapid 39-minute multi-patroller vs IP state alternation. |
| 3 | Eduskuntavaalit 1939 (`2119`) | `2007-01-19..21` | [diff=2222514](https://fi.wikipedia.org/w/index.php?diff=2222514) (`2`) | 4 edits (3 reverts, 2 participants) — repeated anonymous edit undone twice via auto-rollback. |
| 4 | Leo Komarov (`2193`) | `2025-04-20..25` | [diff=23184868](https://fi.wikipedia.org/w/index.php?diff=23184868) (`2`) | 4 edits (3 reverts, 2 participants) — dispute over whether junior medals belong in the infobox medal table (`Junnumitalien poisto mitalitaulukosta`). |
| 5 | Me (lehti) (`2483`) | `2020-08-31..09-01` | [diff=19131924](https://fi.wikipedia.org/w/index.php?diff=19131924) (`2`) | 4 edits (3 reverts, 3 participants) — repeated block-evasion edits (`rv, eston kiertoa`) and counter-undo. |
| 6 | Suuri Venäjän sota (`2512`) | `2010-05-30..06-01` | [diff=8562648](https://fi.wikipedia.org/w/index.php?diff=8562648) (`2`) | 7 edits (3 reverts, 2 participants) — HotCat tug-of-war over removing/restoring category `Suomen sodat`. |
| 7 | Markantalo (`2531`) | `2009-02-06..14` | [diff=6249728](https://fi.wikipedia.org/w/index.php?diff=6249728) (`2`) | 11 edits (5 reverts, 5 participants) — ownership claim (`omistaja`) repeatedly undone and counter-reverted (`mikäs tuossa vikana?`). |
| 8 | Suomenlinnan vankileirin muistopaikka (`2749`) | `2020-05-27` | [diff=18950708](https://fi.wikipedia.org/w/index.php?diff=18950708) (`2`) | 3 edits (2 reverts, 2 participants) — Civil War categorization dispute (`aihe koskee Suomen sisällissotaa` vs `luokittelu käytännön mukaisesti`). |
| 9 | Turun isku 2017 (`2860`) | `2018-10-27..28` | [diff=17667712](https://fi.wikipedia.org/w/index.php?diff=17667712) (`2`) | 11 edits (10 reverts, 4 participants + protection hint) — war over page-rename template pending final court verdict, ending in protection. |
| 10 | Suomen presidentinvaali 2024 (`2865`) | `2022-12-16..17` | [diff=21117295](https://fi.wikipedia.org/w/index.php?diff=21117295) (`3`) | 8 edits (5 reverts, 2 participants) — 3-state dispute over Hjallis Harkimo campaign wording (`ei Harkimon vaalisivu, siis pois`). |

### `burst` (seed `'s4-31-burst'`, [`results/sample_burst.md`](results/sample_burst.md)) — **2 / 10 conflict bursts w/ reverts vs 8 / 10 collaborative/news bursts**

| # | Article (`episode_id`) | Dates (UTC) | Diff link | One-line English gist (from `aot.mask_comment`) |
|---:|---|---|---|---|
| 1 | Konginkankaan linja-autoturma (`3277`) | `2020-03-10` | [diff=18784756](https://fi.wikipedia.org/w/index.php?diff=18784756) | 16 edits (0 reverts, 2 participants) — collaborative expansion of crash sequence (`Onnettomuuden kulku`). |
| 2 | Esikoislestadiolaisuus (`3576`) | `2007-03-23` | [diff=2555850](https://fi.wikipedia.org/w/index.php?diff=2555850) | 10 edits (0 reverts, 2 participants) — peaceful section additions across `Käytännön usko` and `Oppi`. |
| 3 | Hjallis Harkimo (`3811`) | `2014-01-02` | [diff=13765311](https://fi.wikipedia.org/w/index.php?diff=13765311) | **Conflict burst:** 22 edits (20 reverts, 4 participants) — intense 2-hour IP vs patroller revert storm. |
| 4 | Veljekset Salmela (`4494`) | `2007-01-29` | [diff=2276604](https://fi.wikipedia.org/w/index.php?diff=2276604) | 10 edits (0 reverts, 6 participants + protection hint via 14d anon drop) — multi-editor stub creation & fact correction. |
| 5 | Vanhasen II hallitus (`4565`) | `2010-06-23` | [diff=8681514](https://fi.wikipedia.org/w/index.php?diff=8681514) | 12 edits (0 reverts, 4 participants) — cabinet end-of-term updates and copyedits in `Hallituspohja` / `Salkkujako`. |
| 6 | Edvard Ainasoja (`4714`) | `2007-11-12` | [diff=3795412](https://fi.wikipedia.org/w/index.php?diff=3795412) | 10 edits (0 reverts, 3 participants) — collaborative new biography creation and wikification (`w, kh`). |
| 7 | Suomen Kirkon Sisälähetysseura (`5145`) | `2011-09-12` | [diff=10668025](https://fi.wikipedia.org/w/index.php?diff=10668025) | 11 edits (0 reverts, 2 participants) — peaceful text and image additions (`+ kuva`). |
| 8 | Suomen vuoden 2012 presidentinvaalin kannatusmittaukset (`5188`) | `2012-01-08` | [diff=11123421](https://fi.wikipedia.org/w/index.php?diff=11123421) | 12 edits (0 reverts, 3 participants) — election-season polling table updates. |
| 9 | Suomen Kansa Ensin (`5503`) | `2019-03-19` | [diff=18015097](https://fi.wikipedia.org/w/index.php?diff=18015097) | **Conflict burst:** 10 edits (2 reverts, 5 participants) — contested party-leadership updates (`puoluejohto`) with FlaggedRevs rejections. |
| 10 | Hofe (`5577`) | `2019-11-24` | [diff=18558328](https://fi.wikipedia.org/w/index.php?diff=18558328) | 10 edits (0 reverts, 3 participants) — cooperative new-release and lead-definition cleanup. |

---

## Caveats & Guidance for Downstream Consumers

1. **`burst` alone is an attention signal, not a conflict signal (`33.6%` contain reverts):** Two-thirds (`1,887 / 2,843`) of multi-editor bursts have `revert_count = 0` (collaborative stub expansions, breaking news, election tables). Downstream candidate triage (P1.S9) should distinguish `kind IN ('edit_war', 'ping_pong')` or `kind = 'burst' AND revert_count > 0` from peaceful bursts (`revert_count = 0`). `contested_score` already downweights zero-revert bursts (`0.3` vs `1.2`).
2. **MariaDB reserved keyword `` `window` ``:** Always backtick `` `window` `` in SQL queries on `research.s4_episode` (`WHERE `window` = '7d'`, or use `WHERE is_default = 1` to select `edit_war:7d` + `ping_pong` + `burst` in one predicate).
3. **Partial/manual rewrites outside exact SHA-1:** Both `edit_war` (via `s1_revert`) and `ping_pong` rely on exact SHA-1 identity restoration. Post-2019 talk-page-negotiated rewrites or partial undos (e.g., *Suomalainen Waffen-SS-vapaaehtoispataljoona*'s March 2020 `42`-edit burst with `0` SHA-1 reverts) surface here as `burst` episodes and require P1.S5 summary-based undo signals + P1.S3 token survival to pinpoint disputed sentences.
4. **Post-burst 14d anon-drop is noisy on newly created stubs:** As seen in `burst` sample #4 (*Veljekset Salmela*), an IP creating or tweaking a new stub on day 1 followed by named-only cleanup in the next fortnight satisfies the conditioned 14-day anon-drop rule without actual admin protection. Explicit `suojas*/suojattu*/puolisuoja*` log comments (`469` corpus revisions) are high-precision; treat `protection_hint = 1` on `revert_count = 0` bursts as weak.
