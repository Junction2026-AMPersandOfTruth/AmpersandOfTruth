# P1.S1 (#28) · `class_v2`: Rule Fixes from Independent Review

Applies the four rule fixes proposed by the independent 140-revert review (`research/28-s1-revert/review/README.md`) as
new columns `class_v2` and `exclude_reason_v2` (plus index `idx_page_class_v2 (page_id, class_v2)`) on
`research.s1_revert` across all **46,049** canonical reverts (**0 `NULL` rows**).

Existing `class` and `exclude_reason` columns (and `s1_reverted_edit`, `s1_page_stats`, `s1_user_stats`) are **never
modified** (`class_v2.py` snapshots and verifies `COUNT(*)`, `BIT_XOR(CRC32(...))`, and `SUM(CRC32(...))` over
`(reverting_rev_id, class, exclude_reason)` before and after execution).

## How to Run

```bash
# Build / rebuild class_v2 and exclude_reason_v2 on research.s1_revert (idempotent, resumable)
python3 research/28-s1-revert/class_v2.py

# Re-print corpus transition table and 140-row review score without rebuilding
python3 research/28-s1-revert/class_v2.py --check-only

# Validate signals variant on v1 (default) and v2
python3 research/eval/validate.py variant signals -- python3 research/28-s1-revert/variant.py
python3 research/eval/validate.py variant signals -- python3 research/28-s1-revert/variant.py --class-version v2
```

- **Runtime & DB load:** SQL staging runs in ~17 s (`max_statement_time = 85` s per statement). Only rows that can
  change outcome via `wikidiff` are diffed: **425** bot-reverter rows (Rule 3) and **5,773** Rule 1(a)-passing `dispute`
  candidates (**6,198** diffs total = **12,297** unique `(page_id, rev_id)` texts). Texts are fetched by PK via
  `aot.rev_texts` in batches of 20 with `time.sleep(0.2)` between uncached DB batches (~14.5 min cold, < 1 s warm) and
  diffed via `scripts/wikidiff.sh batch` in 31 chunks of 200 (~57 s cold, cached in
  `~/.cache/aot-research/s1-class-v2/diff_outcomes.json`). Warm re-runs complete in ~90 s.

---

## Rules Implemented (`class_v2` Cascade)

1. **Stage 1 — `excluded` (Rule 2):**
   - `exclude_reason_v2 = 'blanking_collision'`: `r_bytes = 0`, **or** (`restored_bytes <= 100` **and**
     `TIMESTAMPDIFF(SECOND, restored_ts, revert_ts) > 365 * 86400`).
   - `exclude_reason_v2 = 'wide_span'`: `reverted_count > 15` **and** (`seconds_to_revert > 30 * 86400` **or** `>= 3`
     distinct non-bot reverted users excluding `reverting_user`).
   - Rows freed from v1 exclusion enter the normal cascade below (including 48-hour mutual-revert pair detection).
2. **Stage 2 — `bot` & `interwiki_migration` (Rule 3):**
   - `is_bot = 1` (`reverting_user` is a bot in `research.s6_editor_profile` or every span edit is by a bot).
   - If `reverting_user` is a bot and its diff (`last_reverted_rev_id -> restored_rev_id`, identical to `R` by SHA-1)
     has `segments` removing **only** `interwiki` segments (`op == 'removed'`, `kind == 'interwiki'`, `moved == False`),
     set `class_v2 = 'excluded'`, `exclude_reason_v2 = 'interwiki_migration'`; otherwise `class_v2 = 'bot'`.
3. **Stage 3 — `self_revert`:**
   - Unchanged (`reverting_user IS NOT NULL` and every span edit has `user = reverting_user`, `utf8mb4_bin`).
4. **Stage 4 — `edit_war` (Rule 4):**
   - Either `R` belongs to a 48-hour mutual-revert window (`>= 3` non-Rule2-excluded, non-bot, non-self reverts on the
     same page within `172,800` s between the same unordered user pair `{U1, U2}` in both directions), **or**
   - **"Span contains a revert":** at least one reverted author (`se.user <> reverting_user`) is a named non-throwaway
     account (`kind = 'named' AND throwaway = 0` in `s6_editor_profile`), **and** the span contains a non-Rule2-excluded,
     non-bot, non-self inner revert `ir` made by a reverted user (`ir.reverting_user <> reverting_user`) within `7` days
     (`604,800` s) of `R`. Otherwise fall through.
5. **Stage 5 — `dispute` (Rule 1):**
   - Keep as `dispute` only if:
     - **(a)** at least one reverted author (`se.user <> reverting_user`) is a named account with `edits >= 5` and
       `throwaway = 0` in `research.s6_editor_profile` (`PK user utf8mb4_bin`), **or** `reinserted_rev_id` was authored
       by such an account, **and**
     - **(b)** `wikidiff` between `restored_rev_id` and `last_reverted_rev_id` has `stats.is_substantive == true` **and**
       `added_prose_chars + removed_prose_chars >= 30`.
6. **Stages 6 & 7 — `graffiti_cleanup` vs `other` (Rule 1 fallback):**
   - `graffiti_cleanup` if all reverted authors (`se.user <> reverting_user`) are anonymous/temporary or treated as
     anonymous/throwaway (`kind IN ('ipv4','ipv6','temporary')` or `kind = 'named' AND (throwaway = 1 OR edits < 5)`)
     **and** `seconds_to_revert < 86400`; otherwise `other`.

### Threshold Sensitivity Check (No Overfitting)

Sweeping each numeric threshold individually across the 140 reviewed rows keeps agreement flat on a **103 / 140**
plateau across natural ranges (`min_edits` in `4..20`: 103; `min_prose_chars` in `10..50`: 103; `blanking_days` in
`180..730`: 103; `wide_span_days` in `7..60`: 103; `wide_span_users` in `2..10`: 103; `inner_revert_days` in `1..30`:
103). Extreme values (`min_prose_chars = 100` or `blanking_days = 1500`) move only **1 of 140** rows (`103 -> 104`), so
per the task spec all thresholds from `review/README.md` are kept as specified without tuning.

---

## Re-scored 140 Reviewed Reverts (`review/verdicts.jsonl`)

Overall agreement with the independent reviewer's ground-truth verdict (`EXPECTED_VERDICT_BY_CLASS` in
`review/review.py`) improves from **81 / 140 (57.9%)** in `class` (v1) to **103 / 140 (73.6%, +22 reverts / +15.7 pp)**
in `class_v2`.

### 1. Precision per Predicted Class Bucket (`v1` vs `class_v2`)

| Class | Expected verdict | v1 `n` | v1 agrees | v1 precision | `class_v2` `n` | `class_v2` agrees | `class_v2` precision |
|---|---|---:|---:|---:|---:|---:|---:|
| `excluded` | `collision_artifact` | 20 | 6 | 30.0% | 21 | 15 | **71.4%** |
| `bot` | `bot_maintenance` | 20 | 11 | 55.0% | 10 | 10 | **100.0%** |
| `self_revert` | `self_correction` | 20 | 20 | 100.0% | 20 | 20 | **100.0%** |
| `edit_war` | `edit_war` | 20 | 12 | 60.0% | 17 | 13 | **76.5%** |
| `dispute` | `content_dispute` | 20 | 6 | 30.0% | 7 | 5 | **71.4%** |
| `graffiti_cleanup` | `graffiti` | 20 | 15 | 75.0% | 34 | 19 | **55.9%** |
| `other` | `good_faith_fix` | 20 | 11 | 55.0% | 31 | 21 | **67.7%** |
| **Total** | — | **140** | **81** | **57.9%** | **140** | **103** | **73.6%** |

### 2. Agreement by Original Sampled v1 Stratum (20 per v1 Class)

| Original v1 stratum | `n` | v1 agrees | `class_v2` agrees | Net delta | What moved |
|---|---:|---:|---:|---:|---|
| `excluded` | 20 | 6 / 20 (30.0%) | **11 / 20 (55.0%)** | **+5** | 9 freed: 5 redirect wars → `edit_war`, 1 temporary-account rollback → `other`, 3 IP-only disputes/wars → `other`/`graffiti_cleanup` |
| `bot` | 20 | 11 / 20 (55.0%) | **19 / 20 (95.0%)** | **+8** | 9 Wikidata interwiki migrations → `excluded` (+9); 1 2012 bot interwiki removal → `excluded` (-1) |
| `self_revert` | 20 | 20 / 20 (100.0%) | **20 / 20 (100.0%)** | **+0** | All 20 unchanged |
| `edit_war` | 20 | 12 / 20 (60.0%) | **10 / 20 (50.0%)** | **-2** | 4 `good_faith_fix` + 1 `graffiti` demoted (+3 where landing matched); 5 anonymous-IP counter-reverts fell through to `graffiti_cleanup` (-5) while lifting `edit_war` precision from 60.0% → 76.5% |
| `dispute` | 20 | 6 / 20 (30.0%) | **17 / 20 (85.0%)** | **+11** | 4 newcomer graffiti → `graffiti_cleanup` (+4); 8 non-substantive / minor / slow newcomer fixes → `other` (+8); 1 IP-only reinsertion → `graffiti_cleanup` (-1) |
| `graffiti_cleanup` | 20 | 15 / 20 (75.0%) | **15 / 20 (75.0%)** | **+0** | All 20 unchanged |
| `other` | 20 | 11 / 20 (55.0%) | **11 / 20 (55.0%)** | **+0** | All 20 unchanged |
| **Total** | **140** | **81 / 140 (57.9%)** | **103 / 140 (73.6%)** | **+22** | — |

### 3. Confusion Matrix (`class_v2` × Reviewer `verdict`, `n = 140`)

| `class_v2` \ `verdict` | `collision_artifact` | `bot_maintenance` | `self_correction` | `edit_war` | `content_dispute` | `graffiti` | `good_faith_fix` | Total |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `excluded` | **15** | 2 | 0 | 2 | 1 | 1 | 0 | **21** |
| `bot` | 0 | **10** | 0 | 0 | 0 | 0 | 0 | **10** |
| `self_revert` | 0 | 0 | **20** | 0 | 0 | 0 | 0 | **20** |
| `edit_war` | 0 | 0 | 0 | **13** | 0 | 4 | 0 | **17** |
| `dispute` | 0 | 0 | 0 | 0 | **5** | 0 | 2 | **7** |
| `graffiti_cleanup` | 0 | 0 | 0 | 6 | 2 | **19** | 7 | **34** |
| `other` | 0 | 0 | 0 | 0 | 5 | 5 | **21** | **31** |
| **Total** | **15** | **12** | **20** | **21** | **13** | **29** | **30** | **140** |

---

## Corpus-wide Transition Table (`class` v1 → `class_v2`, 46,049 Reverts)

| v1 \ v2 | `excluded` | `bot` | `self_revert` | `edit_war` | `dispute` | `graffiti_cleanup` | `other` | v1 Total |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `excluded` | **82** | 2 | 4 | 39 | 16 | 18 | 11 | **172** |
| `bot` | 268 | **375** | 0 | 0 | 0 | 0 | 0 | **643** |
| `self_revert` | 0 | 0 | **3,628** | 0 | 0 | 0 | 0 | **3,628** |
| `edit_war` | 0 | 0 | 0 | **4,231** | 277 | 1,712 | 348 | **6,568** |
| `dispute` | 0 | 0 | 0 | 31 | **2,579** | 3,955 | 2,987 | **9,552** |
| `graffiti_cleanup` | 0 | 0 | 0 | 6 | 0 | **23,234** | 0 | **23,240** |
| `other` | 0 | 0 | 0 | 0 | 0 | 0 | **2,246** | **2,246** |
| **v2 Total** | **350** | **377** | **3,632** | **4,307** | **2,872** | **28,919** | **5,592** | **46,049** |

### Exclusion Reason Transition (`exclude_reason` v1 vs `exclude_reason_v2`)

| `exclude_reason` (v1) | `exclude_reason_v2` (v2) | Reverts | Notes |
|---|---|---:|---|
| `(none)` | `interwiki_migration` | 268 | 2013 Wikidata interwiki-link removals formerly in `bot` |
| `blanking_collision` | `(none)` | 73 | Short redirect / stub reverts (`<= 100` B, `<= 365` d) freed into cascade |
| `blanking_collision` | `blanking_collision` | 55 | `r_bytes = 0` (34) + long-gap `<= 100` B collisions (`> 365` d: 21) |
| `wide_span` | `(none)` | 17 | Single/dual-editor rollbacks (`<= 30` d, `< 3` non-bot users) freed into cascade |
| `wide_span` | `wide_span` | 27 | Multi-user (`>= 3`) or `> 30`-day wide spans retained |

### Impact on Contested Pool & Demo-Core Articles

- **Corpus-wide high-signal pool (`edit_war + dispute`):** shrinks by **55.5%** from **16,120 (35.0%)** in v1 to
  **7,179 (15.6%)** in `class_v2`, closely matching `prof_top` §2's human audit estimate (~16% narrative/POV-pushing
  among substantive reverts).
- **21 Demo-Core articles (`report.py` Topics 1–4, 2,020 reverts):** `dispute` tightens from **400 → 148** (-63.0%) and
  `edit_war` from **359 → 249** (-30.6%), concentrating the contested pool at **397 reverts (19.7%)** while routing
  throwaway/newcomer vandalism to `graffiti_cleanup` (`1,130 → 1,403`) and minor/non-prose edits to `other` (`32 → 122`).

---

## Caveats & Notes for Downstream Lanes

1. **Anonymous counter-revert cycles:** Rule 4 requires at least one named non-throwaway reverted author on inner-revert
   `edit_war` spans so fast IP graffiti re-insertions do not pollute `edit_war`. A side effect is that genuine
   ideological counter-revert cycles conducted from rotating anonymous IPs (e.g., long-term block-evading edits reverted
   by multiple patrollers) fall through to `graffiti_cleanup` (`< 24` h) or `other` (`>= 24` h) unless the same IP and
   patroller hit `>= 3` mutual reverts within 48 h. Combining `class_v2` with S5 edit-summary signals (`eston kiertoa`)
   or S8 text signals recovers these cleanly.
2. **Variant fallback for freed v1 exclusions:** Because `research.s1_reverted_edit` remains frozen on v1 (`exclude_reason
   IS NULL`), the 84 non-bot/non-self reverts freed from `excluded` under `class_v2` have 0 rows in `s1_reverted_edit`.
   `variant.py --class-version v2` detects any top-ranked revert missing from `s1_reverted_edit` and reconstructs its
   deduplicated non-null span edits on that page (`class_v2 <> 'excluded'`) on the fly.

## Reviewer note (orchestrator)

The rules were **derived from the same 140 reviewed rows** they are scored on here, so 103/140 is an in-sample figure and
says the fixes do what was intended, not how well they generalise. The threshold-sweep plateau above shows they are not
tuned to individual rows. The real test is `strict_p@10` of `variant.py --class-version v2` vs `v1` on the golden set (#20/#21).
