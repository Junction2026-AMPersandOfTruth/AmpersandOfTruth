# P1.S7 (#34) · Re-insertion & Cross-Article Repetition Clusters Over Removed Content

Groups substantive removed content (`research.s3_diff` `kind='span'` joined to `research.s1_revert` + `research.s1_reverted_edit`) into **claim clusters** across all **245** golden articles (`4,012` qualifying removed-content units from `9,281` candidate spans, `0` missing `s3_diff` rows), surfacing repeated single-article re-insertions, multi-account coordination candidates, cross-article narrative pushes, and sleeper returns.

## Headline Numbers vs S1 Baseline (`s7-clusters` vs `s1-reverts`, Dev Split)

| Metric | `s1-reverts` (#28) | `s7-clusters` (#34) | Δ (`s7 - s1`) |
|---|---:|---:|---:|
| **`strict_p@10`** *(primary)* | `0.44` | **`0.68`** | **`+0.24`** |
| **`strict_p@25`** | `0.42` | **`0.65`** | **`+0.23`** |
| **`lenient_p@10`** | `0.85` | **`0.90`** | **`+0.06`** |
| **`lenient_p@25`** | `0.70` | **`0.93`** | **`+0.23`** |
| **`duplicates`** | `35` | **`1`** | **`-34` (-97%)** |
| **`recall`** *(contested + campaign)* | `0.58` (`42/73`) | **`0.55`** (`40/73`)* | `-0.03` |
| **`level_accuracy`** | `0.38` | **`0.41`** | `+0.03` |
| **`overclaim_rate`** | `0.13` | **`0.07`** | **`-0.06`** |
| **`quiet_shown`** (`border-crisis-2023`) | `26` | **`11`** (`1/1` TP kept) | **`-15`** |
| **`seconds_per_topic`** | `6.17 s` | **`1.99 s`** | **`-4.18 s`** |

*\*Note on recall:* S7 actually surfaces **41** of the 73 golden items in the top 50, including `continuation-war/i06` at rank #2 — because S7 linked `continuation-war/i06` (page `11398`) with its cross-topic twin `civil-war-1918/i10` (page `6021`) into a single 2-article cluster (`#3382`), the eval runner's 1-golden-item-per-incident matcher (`runner.py`) attributed that incident to `civil-war-1918/i10`.

### Golden Multi-Insertion & Campaign Cluster Alignment (`results/s7-report.md`)
- **33 / 34 (`97.1%`)** of dev `contested`/`campaign` items with `>= 2` inserting revisions have `>= 2` of their inserting revisions grouped into a single S7 cluster:
  - **23 / 24 (`95.8%`)** on true multi-revert items (`>= 2` separate S1 revert spans), including **7 / 7** inserting revisions across all 5 spans of Level-3 `nato-debate/i01` (`campaign`, `accounts,persistence`).
  - **10 / 10 (`100%`)** on multi-edit runs reverted in a single S1 span.
  - Only `ceded-karelia/i10` (`2` spans) stayed unmerged because its second insertion rewrote the passage (`5`-shingle Jaccard `0.086 < 0.6`).
- **Exact vs Fuzzy cost:** across `4,012` qualifying units, exact sentence hashing took **0.294 s** (`12,882` unique hashes, `5,021` pair links, `694` union merges → `299` multi-unit clusters); 64-perm seeded MinHash + `32×2` LSH banding + true Jaccard `>= 0.6` verification took **2.146 s** (`160,574` unique 5-shingles, `1,450` LSH candidate pairs, `829` verified pairs → `+16` additional cross-component merges).

## Method

1. **Units ("removed-content items"):** `research.s3_diff` (`kind='span'`) joined to `research.s1_revert` (`class NOT IN ('excluded', 'bot', 'self_revert')`) and `research.s1_reverted_edit`. Extracts only `prose` from added non-move `prose`/`template` segments plus raw `text` from added non-move `reference` segments. Units with `< 30` normalised prose characters are ignored (`5,269` short spans skipped, `4,012` kept).
2. **Exact matching + Boilerplate guard:** flattens wiki markup/tags, splits sentences, lowercases, strips punctuation, collapses whitespace, and hashes normalised sentences `>= 30` chars (`SHA-1[:16]`). Any sentence hash appearing on `> 8` distinct pages is blocked from linking in both `exact` and `fuzzy` sets.
3. **Fuzzy matching (`method='fuzzy'`):** starts from exact links, computes word `5`-shingles over non-boilerplate normalised text, hashes with **64 seeded Carter-Wegman permutations** (`P = 2^61 - 1`, `seed = 20261114`) bucketed into **32 bands × 2 rows** (`P(candidate | J=0.6) = 99.99994%`), and merges candidate pairs whose exact true 5-shingle Jaccard is **`>= 0.6`** via Union-Find.
4. **Privacy & Campaign signals:**
   - `account_key`: 12-hex-char SHA-1 of `aot.user_display(user)` (IPv4 `/16` and IPv6 `/32` masked ranges count as one editor; raw usernames/IPs are never stored). `sample_text` (`<= 300` chars) runs through `aot.mask_comment` and strips user-namespace links.
   - `campaign_signals`: comma-separated subset of `accounts` (`>= 2` distinct `account_key`s), `articles` (`>= 2` distinct `page_id`s), and `persistence` (`>= 3` unit insertions with `span_days >= 7.0`).
   - `max_gap_days`: longest quiet interval between latest prior `removed_at` and next `inserted_at` (sleeper pattern).
5. **Signals variant (`variant.py`):** emits one `tier = "reverted"` incident per `fuzzy` cluster touching requested `page_ids` (singletons included), with `page_id` = earliest member's page, `other_page_ids` = remaining distinct member pages, `inserting_rev_ids` = sorted union of member inserting revs, `removing_rev_ids` = sorted member `reverting_rev_id`s minus `inserting_rev_ids`, ranked by:
   `score = 2.0 * log2(n_units) + 1.5 * log2(n_accounts) + 1.5 * log2(n_pages) + 0.5 * log1p(span_days) + 0.6 * log1p(min(prose_chars, 4000))`

## Tables (`research.*`)

- `research.s7_cluster(cluster_id PK, method ENUM('exact','fuzzy'), n_units, n_pages, n_accounts, n_reinsertions, first_ts, last_ts, span_days, max_gap_days, sample_text, campaign_signals, prose_chars)` — `6,620` rows (`3,318` `exact` + `3,302` `fuzzy`).
- `research.s7_member(cluster_id, page_id, reverting_rev_id, inserting_rev_ids, inserted_at, removed_at, account_key)` — `8,024` rows (`4,012` per method).

## Commands

```bash
# Offline unit tests (normalisation, MinHash/LSH, union-find, boilerplate guard, campaign_signals, variant)
python3 -m unittest discover -s research/34-s7-repeat -p 'test_*.py' -v

# Build research.s7_cluster and research.s7_member across all golden pages
python3 research/34-s7-repeat/cluster.py

# Validate variant against CONTRACT.md §2.2
python3 research/eval/validate.py variant signals -- python3 research/34-s7-repeat/variant.py

# Score on golden dev split against S1 baseline
research/eval/run signals --name s7-clusters --out research/34-s7-repeat/results --baseline research/28-s1-revert/results/s1-reverts.dev.scorecard.json -- python3 research/34-s7-repeat/variant.py

# Generate human-inspection & golden alignment report -> results/s7-report.md
python3 research/34-s7-repeat/report.py
```
