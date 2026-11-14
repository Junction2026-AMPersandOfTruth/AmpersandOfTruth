# P1.S3 (#30) · Approach A: Removed-Content Items from Revert Spans (`s3_item`) & Signals Variant (`s3a-items`)

Turns identity reverts (`research.s1_revert`) plus precomputed wikitext diffs (`research.s3_diff`) into content-level
removed-claim items (`research.s3_item`: *"this text was live on Wikipedia from `inserted_at` to `removed_at`"*),
evaluates golden-set recovery and live exposure durations on the `dev` split, and scores a merged signals variant
(`s3a-items`) against the `s1-reverts` baseline.

## Commands

```bash
# 1. Build research.s3_item across all pages present in research.s3_diff (245 golden pages -> 9,730 items)
python3 research/30-s3-extract/items.py

# 2. Generate dev-split golden recovery & exposure-duration report -> results/s3a-recovery.md
python3 research/30-s3-extract/report.py

# 3. Validate the CONTRACT §2.2 signals variant
python3 research/eval/validate.py variant signals -- python3 research/30-s3-extract/variant.py

# 4. Score s3a-items against the S1 baseline on the dev split
research/eval/run signals --name s3a-items --out research/30-s3-extract/results --baseline research/28-s1-revert/results/s1-reverts.dev.scorecard.json -- python3 research/30-s3-extract/variant.py

# 5. Offline unit tests (sentence counting, prose normalisation & merging, grouping keys, score formulas)
python3 -m unittest research/30-s3-extract/test_s3.py -v
```

---

## Table `research.s3_item`

One row per `research.s1_revert` (`class <> 'excluded'`) that has a `kind = 'span'` diff row in `research.s3_diff`.
Built across all **245** golden pages in `s3_diff` (**203** pages have non-excluded reverts; `0` missing or errored
span/edit diff rows), yielding **9,730** rows:

| Class | Rows |
|---|---:|
| `graffiti_cleanup` | 5,715 |
| `dispute` | 1,865 |
| `edit_war` | 1,521 |
| `self_revert` | 426 |
| `other` | 180 |
| `bot` | 23 |
| **Total** | **9,730** |

### Columns

| Column | Type | Definition |
|---|---|---|
| `reverting_rev_id` | `INT UNSIGNED PK` | Reverting revision ID (`s1_revert.reverting_rev_id`). |
| `page_id` | `INT UNSIGNED` | Finnish Wikipedia `page_id`. |
| `class` | `VARCHAR(32)` | S1 rule-based class (`dispute`, `edit_war`, `graffiti_cleanup`, `other`, `self_revert`, `bot`). |
| `inserting_rev_ids` | `MEDIUMTEXT` | Ascending comma-separated `rev_id` list: the span's reverted edits (`s1_reverted_edit`) whose `s3_diff` `kind='edit'` row has `added_prose_chars > 0 OR added_refs > 0`, falling back to all non-null reverted edits in the span (`1,728` pure-deletion/markup spans used the fallback). |
| `inserting_editors` | `MEDIUMTEXT` | JSON array of sorted distinct `EditorDisplay` grouping keys (`user:<name>`, `temp:<id>`, `ip4:<a>.<b>`, `ip6:<g1>:<g2>`, `hidden`) over `inserting_rev_ids` — never a raw IP. |
| `inserting_anonymous_only` | `TINYINT(1)` | `1` iff every revision in `inserting_rev_ids` was made by an anonymous IP or temporary account. |
| `removing_user_display` | `VARCHAR(300)` | Masked remover display label (`aot.user_display`). |
| `removal_summary` | `MEDIUMTEXT` | Edit summary of the revert with all IPv4/IPv6 addresses masked (`aot.mask_comment`). |
| `inserted_prose` | `MEDIUMTEXT` | Cleaned prose removed by the revert (`s3_diff.added_prose` on `restored -> last_reverted`). |
| `inserted_wikitext` | `MEDIUMTEXT` | Non-moved added segments' raw `text` joined by paragraph, capped at `≤ 20,000` chars. |
| `removed_prose` | `MEDIUMTEXT` | Cleaned baseline prose restored by the revert (`s3_diff.removed_prose`). |
| `segment_kinds` | `MEDIUMTEXT` | JSON object of non-moved `added`/`removed` segment counts per `kind` (`prose`, `reference`, `template`, `category`, `interwiki`, `file`, `markup`). |
| `inserted_at` | `DATETIME` | Span start UTC timestamp (`s1_revert.first_reverted_ts`). |
| `removed_at` | `DATETIME` | Revert UTC timestamp (`s1_revert.revert_ts`). |
| `exposure_seconds` | `BIGINT` | Seconds the span remained live (`max(0, seconds_to_revert)`). |
| `reinserted_rev_id` | `INT UNSIGNED NULL` | Earliest later revision on the page restoring `last_reverted_rev_id`'s SHA-1, if any. |
| `prose_chars` | `INT` | `s3_diff.added_prose_chars` on the span. |
| `sentence_count` | `INT` | Sentence count in `inserted_prose` (handles Finnish ordinal dates `6. joulukuuta`, dotted dates `7.3.1918`, initials, abbreviations, and `wikidiff` `' … '` separators). |
| `refs_added` | `INT` | `s3_diff.added_refs` on the span. |
| `refs_removed` | `INT` | `s3_diff.removed_refs` on the span. |
| `is_substantive` | `TINYINT(1)` | `s3_diff.is_substantive` (`added_prose_chars >= 20 OR removed_prose_chars >= 20 OR added_refs > 0 OR removed_refs > 0`). |
| `is_blanking` | `TINYINT(1)` | `s3_diff.is_blanking` (`removed > 500` prose chars and `added < 50`, or `> 80%` removed on a `> 200`-char page). |

---

## Signals Variant (`variant.py`) & Scoring Formula

1. **Candidate filter:** `is_substantive = 1`, `is_blanking = 0`, and `class NOT IN ('excluded', 'bot', 'self_revert')`.
2. **One incident per inserted content (per-page Union-Find merge):** merges items on the same `page_id` that share
   equal non-empty normalised `inserted_prose` (Unicode NFC, casefolded, `' … '` collapsed, outer punctuation/whitespace
   trimmed) **or** overlapping `inserting_rev_ids` (while guarding `inserting_rev_ids ∩ removing_rev_ids = ∅`).
   - `total_exposure_seconds`: union of `[inserted_at, removed_at]` intervals across merged spans (no double-counting).
   - `reinsertion_count`: `(len(merged_spans) - 1) + count(distinct unmerged reinserted_rev_id)`.
   - `tier`: always `"reverted"` (evidence tiering is P1.S9/S10 #37).
   - Optional §3 fields populated: `claim_fi` (first 300 chars of `inserted_prose`), `first_inserted_at`,
     `last_removed_at`, `total_exposure_seconds`, `reinsertion_count`.
3. **Selected scoring formula (`f3_dispute_gated`, default):** combines `s3_item` substantiveness/persistence/editor
   features with `research.s5_revert_reason` lexicon labels:

   ```text
   has_acc_pov            = removal_reasons intersects {factual_error, neutrality_pov, original_research}
   has_src_con            = removal_reasons intersects {sourcing, consensus}
   has_dispute_reason     = has_acc_pov or has_src_con
   has_noise_reason       = removal_reasons intersects {vandalism_test, maintenance, copyright}
   has_substantive_claim  = (prose_chars >= 50) and (sentence_count >= 1)
   has_community_dispute  = (reinsertion_count >= 1) or ('edit_war' in classes) or has_dispute_reason

   score = 0.55 * ln(1 + min(prose_chars, 1800))
         + (-1.80 if prose_chars == 0 else +0.50 if has_substantive_claim else 0.0)
         + (0.35 if refs_added > 0 else 0.0)
         + 0.22 * ln(1 + total_exposure_seconds / 3600)
         + (2.30 if has_acc_pov else 1.70 if has_src_con else 0.0)
         + 2.10 * ln(1 + reinsertion_count)
         + (1.90 if 'edit_war' in classes else 0.90 if 'dispute' in classes else -1.10 if classes == {'graffiti_cleanup'} else 0.0)
         + (2.50 if has_substantive_claim and has_community_dispute else 0.0)
         + 0.90 * named_inserter + 0.90 * min(max(distinct_editors - 1, 0), 2)
         - (2.20 if has_noise_reason and not has_dispute_reason else 0.0)
   ```

### Scoring Formula Comparison on Dev Split (`limit = 50`, 10 topics)

| Variant / Formula | `strict_p@10` | `strict_p@25` | `lenient_p@10` | `lenient_p@25` | `recall` | `duplicates` | `quiet_shown` | `s/topic` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `s1-reverts` (P1.S1 baseline) | 0.44 | 0.42 | 0.85 | 0.70 | 0.58 | 35 | 26 | 6.17 |
| `f1_additive` (5-term log-linear baseline) | 0.53 | 0.56 | 0.83 | 0.89 | 0.60 | 6 | 19 | 4.41 |
| `f2_evidence_aligned` (additive + S5 noise penalty) | 0.65 | 0.52 | 0.92 | 0.82 | 0.63 | 9 | 19 | 3.88 |
| **`s3a-items` (`f3_dispute_gated`, kept)** | **0.76** (**+0.32**) | **0.61** (**+0.19**) | **0.95** (**+0.10**) | **0.83** (**+0.13**) | **0.62** (**+0.04**) | **10** (**-25**) | **19** (**-7**) | **2.98** |

---

## Headline Recovery & Exposure Numbers (`results/s3a-recovery.md`)

- **Dev golden recovery (238 items across 11 dev topics):**
  - `campaign`: **1 / 1 (100.0%)** recovered, 100.0% right removal, 100.0% right window start.
  - `contested`: **52 / 63 (82.5%)** recovered, 82.5% right removal, **49 / 63 (77.8%)** exact window start.
  - `reverted`: **70 / 80 (87.5%)** recovered, 87.5% right removal, **65 / 80 (81.2%)** exact window start.
  - Strict incidents (`contested` + `campaign`): **53 / 64 (82.8%)** recovered; the remaining **11** strict items had no
    S1 SHA-1 identity revert (`8` partial/rewritten removals, `3` clean deletions after unrelated intermediate edits).
- **Filter agreement on recovered dev items (205 items):**
  - `is_substantive = 0` vs `not-substantive`: **92.2%** overall agreement; **1.6% (2 / 123)** false-filter rate on real incidents.
  - `is_blanking = 1` vs `blanking`: **99.0%** overall agreement (**8 / 8 = 100%** recall on recovered blanking items, plus 5 caught upstream by S1 `blanking_collision`); **0.8% (1 / 123)** false-blanking rate on real incidents.
- **Exposure duration (how long removed claims stayed live):**
  - Overall substantive `s3_item` spans (`n = 8,747`): median **3.3 min**, p75 **30.6 min**, p90 **7.95 h**, max **413.1 days**.
  - Recovered golden `contested` dev items (`n = 52`): median **10.25 h**, p75 **1.3 days**, p90 **2.5 days**, max **38.0 days**
    — contested claims survive **~185× longer at the median** than typical reverted edits.
