# P1.S10 (#37) · Incident Assembly, Tiering & Ranking (Variants A / B / B+rel)

Combines S7 claim clusters (`research.s7_cluster` / `research.s7_member`, `method='fuzzy'`) with precomputed S1/S3/S4/S5/S6/S8 corpus signals and optional S9 Gemini `classify-edit` assessments into ranked, tiered `IncidentRecord`-compatible incidents (`CONTRACT.md` §2.2 / §3 + `#109` schema extensions).

---

## Tuning Summary: Over-Claiming Fix, Ablations & `B+rel` (Golden Dev Split)

In initial LLM-tiered scoring (`s10-b`), S10 achieved `strict_p@10 = 0.79` and `level_accuracy = 0.67` with `0` duplicates, but **over-claimed 26% (`0.26`)** of matched golden items and emitted `2` contested+ incidents on the quiet control (`border-crisis-2023`, which has `1` true contested item). Three root causes drove those promotions:
1. **Two-sided ping-pong clusters (`--sides on`, default):** S1 emits revert spans for both camps of an edit war (`X -> Y` and `Y -> X`), and S7 merges them when `X` and `Y` share surrounding paragraph sentences or shingles. Counting both camps in `DistinctInsertingEditors` falsely triggered `multiple_editors` -> `campaign`.
2. **Page-level S4 edit wars (`--edit-war-source passage`, default):** Checking `s4_episode_rev` promoted single-revert bystanders on busy pages (`quiet_contested_or_above = 2` instead of `1`).
3. **2-of-4 coordination threshold (`--threshold min_coordination_signals=3`):** Multi-year content disputes on a single article routinely accumulate `multiple_editors` + `persistence` (or `source_change`) without coordinated campaigning.

### Golden Dev Ablation Table (`limit = 50`, `cap_per_article = 5`, `gemini-4-carbon` assessments)

| Variant | Configuration | `strict_p@10` | `strict_p@25` | `level_accuracy` | `overclaim_rate` | `recall` | `duplicates` | `quiet_contested_or_above` |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `s10-a` | Mode `A` (no LLM assessments) | `0.68` | `0.61` | `0.34` | `0.12` | `0.58` | `0` | `0` |
| `s10-b` *(baseline)* | Mode `B`, `--sides off`, `--edit-war-source s4` | `0.79` | `0.68` | `0.67` | `0.26` | **`0.62`** | `0` | `2` *(1 FP)* |
| `s10-b-noew` | `s10-b` + `--disable edit_war` | `0.79` | `0.67` | `0.59` | `0.27` | `0.58` | `0` | `1` |
| `s10-b2` | Mode `B`, `--sides on`, `--edit-war-source passage` | `0.79` | `0.70` | `0.60` | `0.26` | `0.58` | `0` | **`1`** *(0 FP)* |
| `s10-b2-noreason` | `s10-b2` + `--disable removal_reason` | `0.76` | `0.70` | `0.57` | `0.24` | `0.55` | `0` | **`1`** |
| `s10-b2-noreinsert` | `s10-b2` + `--disable reinsertion` | `0.84` | `0.72` | `0.60` | `0.25` | `0.58` | `0` | **`1`** |
| `s10-b2-noew` | `s10-b2` + `--disable edit_war` | `0.79` | `0.71` | `0.60` | `0.26` | `0.58` | `0` | **`1`** |
| `s10-b2-notemplate` | `s10-b2` + `--disable dispute_template` | `0.79` | `0.70` | `0.59` | `0.26` | `0.58` | `0` | **`1`** |
| `s10-b2-noassess` | `s10-b2` + `--disable assessment` | `0.68` | `0.63` | `0.33` | `0.10` | `0.58` | `0` | `0` |
| `s10-b2-coord3` | `s10-b2` + `--threshold min_coordination_signals=3` | `0.81` | `0.70` | `0.63` | `0.22` | `0.58` | `0` | **`1`** |
| `s10-b2-conf4` | `s10-b2` + `--threshold min_model_confidence=4` | `0.80` | `0.69` | `0.62` | `0.22` | `0.56` | `0` | `0` *(misses TP)* |
| **`s10-b2-rel`** | **Mode `B+rel` (cached `bge-m3`), `--sides on`, `passage` EW** | **`0.83`** | **`0.74`** | **`0.65`** | **`0.20`** | **`0.60`** | **`0`** | **`1`** *(0 FP)* |
| **`s10-b2-rel-coord3`** ⭐ | **`s10-b2-rel` + `--threshold min_coordination_signals=3`** | **`0.86`** | **`0.75`** | **`0.68`** | **`0.16`** | **`0.60`** | **`0`** | **`1`** *(0 FP)* |

*(Note: `border-crisis-2023` contains exactly `1` golden `contested` incident, `border-crisis-2023/i01`. `quiet_contested_or_above = 1` with `Found / expected = 1 / 1` means `1` true positive at `#1` and `0` false level-2+ incidents.)*

---

## Cluster Side Orientation & Passage-Level Edit War Rules

### 1. Side Orientation (`orient_cluster_units` in `evidence.py`, `--sides on|off`)

Within each S7 cluster, member spans are ordered chronologically by `(inserted_at, reverting_rev_id)`. Unit `0` (the earliest reverted insertion) defines **Side 1** (the claim `X`). Every subsequent unit `i` is compared against all earlier oriented units `j < i` using `added_prose` / `removed_prose` (plus `<ref>` / template segment fallback as in S7) from `research.s3_diff` (`kind='span'`), normalised identically to S7:
- Let `(X_j, Y_j)` be `(added_j, removed_j)` when `side[j] == 1`, or `(removed_j, added_j)` when `side[j] == 2`.
- Unit `i` matches **Side 2 (opposite side)** of unit `j` if `added_i` matches `Y_j` or `removed_i` matches `X_j` with word 5-shingle Jaccard `>= 0.6` or distinctive sentence-hash overlap (`sents(added) - sents(removed)`, preventing unchanged context sentences in the same paragraph from matching both sides).
- Unit `i` is assigned **Side 2** iff it has an opposite-side match whose alignment strength exceeds any same-side match (`added_i ~ X_j` / `removed_i ~ Y_j`); otherwise it stays on **Side 1**. (On the dev split, `19 / 234` multi-unit clusters contain Side-2 units.)

**How Side-1 and Side-2 units map to incident fields:**
- `inserting_rev_ids`, `DistinctInsertingEditors`, `ReinsertionCount`, `InsertionSpan`, `KeptInLatestRevision` sentence probes, `prose_chars`, `total_exposure_seconds`, `DisputeTemplateAdded`, and diff-side `SourceManipulation` use **only Side-1 units**.
- **Side-2 units count as removals of the claim via their `inserting_rev_ids` — their `reverting_rev_id`s are NOT removals of the claim** (because reverting a Side-2 counter-edit puts Side-1 claim `X` *back* into the article).

#### Worked Example

Suppose an article starts at revision `r0` with consensus text `Y`:

1. **`r1` (Editor A)** replaces `Y` with claim `X`.
2. **`r2` (Editor B)** identity-reverts `r1` back to `r0` (`Y`).
   - S1 emits revert span **`U1`**: `inserting_rev_ids = [r1]` (Editor A), `reverting_rev_id = r2` (Editor B), `added_prose = X`, `removed_prose = Y`.
   - `U1` is the earliest unit -> **Side 1**.
3. **`r3` (Editor A)** identity-reverts `r2` back to `r1` (`X`), restoring claim `X`.
   - Because `r3` restores `r1`'s `sha1`, S1 also emits revert span **`U2`** treating `r2` as the "reverted edit" and `r3` as the "reverting rev": `inserting_rev_ids = [r2]` (Editor B), `reverting_rev_id = r3` (Editor A), `added_prose = Y`, `removed_prose = X`.
   - `U2`'s `added_prose` (`Y`) matches `U1`'s `removed_prose` (`Y`) and `U2`'s `removed_prose` (`X`) matches `U1`'s `added_prose` (`X`) -> **Side 2**.
   - Crucially, `U2.reverting_rev_id = r3` **restored `X`** — it is *not* a removal of `X`. Meanwhile `U2.inserting_rev_ids = [r2]` is the same removal event `r2` already recorded by `U1.reverting_rev_id` (so if the cluster stops after `[U1, U2]`, as in dev cluster `3496` / `austerity-politics/i13`, `inserting_rev_ids = [r1]`, `removing_rev_ids = [r2]`, `removal_count = 1`, `reinsertion_count = 0`, `in_edit_war = False`, and the incident is accurately tiered `reverted`).
4. **`r4` (Editor C)** identity-reverts `r3` back to `r2` (`Y`).
   - S1 emits **`U3`**: `inserting_rev_ids = [r3]` (Editor A), `reverting_rev_id = r4` (Editor C), `added_prose = X`, `removed_prose = Y` -> **Side 1**.
5. **`r5` (Editor A)** identity-reverts `r4` back to `r3` (`X`).
   - S1 emits **`U4`**: `inserting_rev_ids = [r4]` (Editor C), `reverting_rev_id = r5` (Editor A), `added_prose = Y`, `removed_prose = X` -> **Side 2** (`r5` is excluded from `removing_rev_ids`).

Assembled result over `[U1 (Side 1), U2 (Side 2), U3 (Side 1), U4 (Side 2)]`:
- `inserting_rev_ids = [r1, r3]` (Editor A only -> `DistinctInsertingEditors = 1`, vs `[r1, r2, r3, r4]` and `3` editors with `--sides off`).
- `removing_rev_ids = [r2, r4]` (Editors B & C; `U2.rev = r3` and `U4.rev = r5` are excluded because they restored `X`; if an unmirrored Side-2 unit `U2'(ins=[r_edit], rev=r_restore)` removed `X` via a non-revert edit `r_edit` not in Side-1 `reverting_rev_id`s, `r_edit` is included in `removing_rev_ids` and counted as a distinct removal).
- `ReinsertionCount = 1` (`U3` inserted at `r3` after `U1` was removed at `r2`), `removal_count = 2` (`r2`, `r4`), `InEditWar = True`, tier = **`contested`** (instead of over-claiming `campaign`).

### 2. Passage-Level Edit War (`--edit-war-source passage|s4`)

Under `--edit-war-source passage` (default), `InEditWar` is `True` iff **within the single cluster**:
1. Side-1 content was removed `>= 2` distinct times (`len(side1_rem_revs) + unmirrored_side2_removals >= 2`),
2. Side-1 content was put back `>= 1` time (`reinsertion_count >= 1` over Side-1 units), and
3. The remover grouping keys and re-inserter grouping keys form different editor camps (`bool(reinserters - removers) and bool(removers - reinserters)`).

The legacy page-wide S4 episode check remains available behind `--edit-war-source s4`.

---

## Recommendation for S11 (#38) & C# `TierThresholds` Changes

### 1. Configuration S11 (#38) Should Take
Take **`s10-b2-rel-coord3`**:
- **Ranking mode:** `B+rel` (tier-first ordering `campaign > contested > reverted`, within-tier additive score multiplied by `max(0, cos(bge-m3(topic_query_en), bge-m3(claim_en ?? inserted_prose[:500])))` from cached embeddings).
- **Cluster orientation:** `--sides on` (Side-1 claim orientation, excluding Side-2 counter-reverts from `inserting_rev_ids`, `DistinctInsertingEditors`, `ReinsertionCount`, and `InsertionSpan`, and excluding Side-2 `reverting_rev_id`s from `removing_rev_ids`).
- **Edit-war signal:** `--edit-war-source passage` (cluster-local `>= 2` removals and `>= 1` re-insertion across distinct editor grouping keys).
- **All 5 dispute/assessment signals enabled** (`removal_reason`, `reinsertion`, `edit_war`, `dispute_template`, `assessment`):
  - Ablating `assessment` (`s10-b2-noassess`) collapses `strict_p@10` (`0.79 -> 0.68`) and `level_accuracy` (`0.60 -> 0.33`).
  - Ablating `removal_reason` (`s10-b2-noreason`) hurts both `strict_p@10` (`-0.03`) and `recall` (`-0.03`).
  - Ablating `reinsertion` (`s10-b2-noreinsert`) improved Mode `B` `strict_p@10` (`0.84`) because tangential 1-reinsertion edits otherwise tied on tier with on-topic disputes, **but `B+rel` solves that ranking tie directly while preserving higher recall (`0.60` vs `0.58`), higher `strict_p@10` (`0.86`), higher `strict_p@25` (`0.75`), and higher `level_accuracy` (`0.68`)**.

### 2. Exact C# `TierThresholds` Change (`src/AmpersandOfTruth.Web/Features/Evidence/IncidentEvidence.cs`)
Change **one** threshold default in `TierThresholds` and keep the other five unchanged:
- **Change `MinCoordinationSignals` from `2` to `3`:**
  ```csharp
  public int MinCoordinationSignals { get; init; } = 3; // was 2
  ```
  *Why:* On golden dev, raising `min_coordination_signals` from `2` to `3` lowers `overclaim_rate` by `-4` pp in both Mode `B` (`0.26 -> 0.22`) and Mode `B+rel` (`0.20 -> 0.16`), raises `level_accuracy` (`0.65 -> 0.68`), and raises `strict_p@10` (`0.83 -> 0.86`, `lenient_p@10 = 1.00`) without losing any true `campaign` recall (`nato-debate/i01` fires `>= 3` coordination signals). Two signals (`multiple_editors + persistence` or `multiple_editors + source_change`) frequently co-occur organically on long-lived contested passages without coordinated campaigning.
- **Keep `MinModelConfidence = 3`** (raising to `4` in `s10-b2-conf4` drops recall `-2` pp and demotes the true `border-crisis-2023/i01` incident whose S9 confidence is `3`).
- **Keep `MinDistinctEditors = 2`, `MinDistinctArticles = 2`, `MinPersistenceReinsertions = 2`, and `MinPersistenceSpan = TimeSpan.FromDays(7)` unchanged.**

---

## Round 1 Headline Numbers (No LLM Assessments — Historical Reference)

> Note: `research/eval/run` enforces lowercase `--name` (`^[a-z0-9][a-z0-9._+-]*$`), so the Round 1 scorecards are committed as `s10-a` and `s10-b-noassess`. Without `--assessments`, `TierRules` caps every non-excluded incident at `reverted` (the honest Level-1 floor per `docs/evidence-standard.md` §1), so `s10-a` and `s10-b-noassess` emit the same incident ordering in Round 1 and diverge once `--assessments` promotes verified disputed claims to `contested` / `campaign`.

| Metric | `s1-reverts` (#28) | `s7-clusters` (#34) | `s10-a` (#37) | `s10-b-noassess` (#37) | Δ (`s10 - s7`) |
|---|---:|---:|---:|---:|---:|
| **`strict_p@10`** *(primary)* | `0.44` | `0.68` | **`0.68`** | **`0.68`** | `0.00` |
| **`strict_p@25`** | `0.42` | `0.65` | **`0.61`** | **`0.61`** | `-0.04` |
| **`lenient_p@10`** | `0.85` | `0.90` | **`0.92`** | **`0.92`** | **`+0.02`** |
| **`lenient_p@25`** | `0.70` | `0.93` | **`0.93`** | **`0.93`** | `0.00` |
| **`recall`** *(contested + campaign)* | `0.58` | `0.55` | **`0.58`** | **`0.58`** | **`+0.03`** |
| **`duplicates`** | `35` | `1` | **`0`** | **`0`** | **`-1` (-100%)** |
| **`unlabelled@10`** | `6.78` | `6.78` | **`6.00`** | **`6.00`** | **`-0.78`** |
| **`quiet_shown`** (`border-crisis-2023`) | `26` | `11` | **`9`** (`1/1` TP at #1) | **`9`** (`1/1` TP at #1) | **`-2`** |
| **`level_accuracy`** *(no LLM -> all `reverted`)* | `0.38` | `0.41` | `0.34` | `0.34` | `-0.07` |

Per-topic highlights in `s10-a` / `s10-b-noassess`: `strict_p@10 = 1.00` on both `finlandization` and `austerity-politics`, `0.80` on `ceded-karelia` and `nato-debate`, `0` duplicate incidents across all 10 topics, and `border-crisis-2023/i01` ranked `#1` on the quiet control topic with only `9` total shown items.

---

## `IncidentEvidence` Signal Mappings (`evidence.py`)

One `IncidentEvidence` instance is assembled per S7 `fuzzy` cluster (`tiers.py` ports `TierRules.cs` + `IncidentEvidence.cs` 1:1):

| `IncidentEvidence` field | Research table / source | Mapping rule |
|---|---|---|
| `is_substantive` | `research.s3_diff` (`kind='span'`) | `True` if any member span has `is_substantive = 1` (fallback `True` when `s7_cluster.prose_chars >= 30` if all span diffs are missing). |
| `is_blanking` | `research.s3_diff` (`kind='span'`) | `True` if member span diffs exist and **every** member span has `is_blanking = 1`. |
| `removed_by_other_account` | `corpus.revision` | `True` iff the masked editor grouping keys (`EditorDisplay` IPv4 `/16`, IPv6 `/32`, or normalized username) of removing revisions and inserting revisions are disjoint. |
| `inserted_by_bot` | `research.s6_editor_profile`, `research.s6_rev_author`, `research.s1_revert` | `True` if any inserting revision has `s6_editor_profile.is_bot = 1`, or **every** member `s1_revert.class = 'bot'`. |
| `kept_in_latest_revision` | `corpus.revision` (latest `rev_id` per page) + `corpus.revision_text` | Extracts the longest non-moved added prose sentence `>= 25` chars (`longest_added_sentence`) and checks `LOCATE(probe, rt.text) > 0` server-side on `(page_id, latest_rev_id)` PK without transferring full article wikitext. |
| `removal_reason` | `research.s5_revert_reason.removal_reasons` | Union of comma-separated labels across removing revisions, mapped with exact `RevertReasons.cs` priority: `factual_error` -> `Accuracy` > `neutrality_pov` -> `Neutrality` > `sourcing` / `original_research` -> `Sourcing` > `vandalism_test` -> `Vandalism` > any other non-empty label -> `Other` > `None`. |
| `reinsertion_count` | `research.s7_member` | Number of **Side-1** cluster member insertions whose `inserted_at >= first_removed_at` (`first_removed_at = min(removed_at)` across Side-1 members; all members when `--sides off`). |
| `insertion_span` | `corpus.revision.ts` / `s7_member.inserted_at` | `max(inserted_ts) - min(inserted_ts)` across **Side-1** inserting revisions (`timedelta(0)` for single-timestamp clusters). |
| `in_edit_war` | Cluster units (`passage`, default) or `research.s4_episode*` (`s4`) | Under `--edit-war-source passage`: `True` iff Side-1 content was removed `>= 2` times and put back `>= 1` time by different editor grouping keys within the cluster. Under `--edit-war-source s4`: `True` if any cluster rev belongs to a default S4 `edit_war` / `ping_pong` episode. |
| `dispute_template_added` | `research.s3_diff.segments` (`kind='span'` / `'edit'`) | `True` if any non-moved Side-1 `op='added'` segment matches `(?i)\{\{\s*(neutraalius\|lähteetön\|lähde\?\|korjattava\|kiistanalainen\|tarkista\|puolueellinen)`. |
| `distinct_inserting_editors` | `corpus.revision.user` | Count of distinct masked editor grouping keys across **Side-1** inserting revisions (`max(1, len(keys))`); IPv4 `/16` and IPv6 `/32` ranges count as one editor per `docs/evidence-standard.md` §3. |
| `distinct_articles` | `research.s7_cluster.n_pages` / `s7_member.page_id` | Distinct Side-1 `page_id` count when Side-2 units are stripped (`max(1, n_pages)` otherwise). |
| `source_manipulation` | `research.s3_diff.segments` + S9 `assessment` | `True` if any member diff removes or replaces a `<ref>` URL/citation while adding prose (`has_ref_manipulation`), or if `assessment.category == 'source_manipulation'`. |
| `assessment` | `--assessments <jsonl>` (S9 `classify-edit`) | Parsed when `outcome == 'ok'`: `(category, confidence, has_verified_quote)` where `has_verified_quote` is `True` iff at least one quoted span (`>= 3` chars) in `value.evidence[]` appears verbatim in the cluster's inserted text (`kind='edit'` + `kind='span'`). `None` if absent or `--disable assessment`. |

---

## Scoring & Ranking Modes (`variant.py` & `export_classify.py`)

- **Structural & graffiti exclusions:** Any candidate where `TierRules.Assess(ev).is_incident` is `False` (`not-substantive`, `self-removal`, `bot`, `blanking`, `kept`, or confident `graffiti` when assessed) is never emitted.
- **Per-article cap:** `--cap-per-article 5` (default; `<= 0` disables) caps emitted incidents per topic article bucket after sorting so a single high-traffic article cannot monopolize the top-50 feed.
- **Mode `A` (additive score):**
  ```text
  score_A = max(0.1,
      2.0
    + 0.60 * log1p(min(prose_chars, 4000))
    + 0.25 * log1p(min(exposure_seconds, 365d) / 60)
    + w_reason                     # +2.5 accuracy/neutrality/sourcing | +0.5 other | -1.5 vandalism | 0 none
    + 2.20 * log1p(reinsertion_count)
    + 1.80 * I(in_edit_war)
    + 1.50 * I(dispute_template_added)
    + 1.80 * log2(distinct_inserting_editors)
    + 2.00 * log2(distinct_articles)
    + 0.50 * log1p(span_days) + 1.00 * I(reinsertion_count >= 2 and span_days >= 7)
    + 1.50 * I(source_manipulation)
    + w_assessment                 # claim: +3.0*(conf/5) + 1.0*I(verified_quote) | gfe/other: -0.5*(conf/5) | low-conf graffiti: -2.5*(conf/5)
  )
  ```
- **Mode `B` (tiers first, `A` within tier):** Partitions into `campaign` (3) > `contested` (2) > `reverted` (1) via `TierRules.Assess`, ordering within each tier by `score_A` (`score = 100 * tier_level + score_A`).
- **Mode `B+rel` (tier + cached topic relevance):** Multiplies within-tier `score_A` by `max(0.0, cos(bge-m3(request.text), bge-m3(claim_en or prose[:500])))` reading **only** `results/embeddings.cache.json` (`embed_cache.py`; missing vectors default to `relevance = 1.0`, zero network embedding calls at request time).
- **Tuning & ablation flags (`variant.py`):**
  - `--sides on|off` (default `on`): orient cluster units relative to earliest unit.
  - `--edit-war-source passage|s4` (default `passage`): passage-level vs S4 page-level `InEditWar`.
  - `--threshold name=value` (repeatable): override any `TierThresholds` field (`min_model_confidence`, `min_distinct_editors`, `min_distinct_articles`, `min_persistence_reinsertions`, `min_persistence_span`, `min_coordination_signals`).
  - `--disable <signal>` (repeatable): `removal_reason`, `reinsertion`, `edit_war`, `dispute_template`, `assessment`.

---

## Signal Firing Rates on Dev Split Candidates

Evaluated across all **2,540** S7 `fuzzy` clusters touching the 10 dev-split topics (`0` missing `s3_diff` span rows), **2,414** structurally non-excluded candidates (`126` excluded: `97` blanking, `24` kept-in-latest, `5` self-removal, `0` false exclusions on golden `contested`/`campaign`), and the subsets matching dev golden `contested`/`campaign` (`n = 52`) vs golden `reverted` (`n = 59`) items:

| `IncidentEvidence` signal | All dev clusters (`n = 2,540`) | Non-excluded (`n = 2,414`) | Matched golden `contested`/`campaign` (`n = 52`) | Matched golden `reverted` (`n = 59`) |
|---|---:|---:|---:|---:|
| `is_substantive` | `2,540 (100.0%)` | `2,414 (100.0%)` | `52 (100.0%)` | `59 (100.0%)` |
| `not removed_by_other_account` *(self-removal)* | `5 (0.2%)` | `0 (0.0%)` | `0 (0.0%)` | `0 (0.0%)` |
| `inserted_by_bot` | `0 (0.0%)` | `0 (0.0%)` | `0 (0.0%)` | `0 (0.0%)` |
| `is_blanking` | `97 (3.8%)` | `0 (0.0%)` | `0 (0.0%)` | `1 (1.7%)` |
| `kept_in_latest_revision` | `24 (0.9%)` | `0 (0.0%)` | `0 (0.0%)` | `0 (0.0%)` |
| **`removal_reason` in `{accuracy, neutrality, sourcing}`** | **`204 (8.0%)`** | **`201 (8.3%)`** | **`22 (42.3%)`** | **`9 (15.3%)`** |
| ↳ `accuracy` (`factual_error`) | `30 (1.2%)` | `29 (1.2%)` | `6 (11.5%)` | `3 (5.1%)` |
| ↳ `neutrality` (`neutrality_pov`) | `50 (2.0%)` | `50 (2.1%)` | `12 (23.1%)` | `0 (0.0%)` |
| ↳ `sourcing` (`sourcing` / `original_research`) | `124 (4.9%)` | `122 (5.1%)` | `4 (7.7%)` | `6 (10.2%)` |
| ↳ `vandalism` (`vandalism_test`) | `78 (3.1%)` | `75 (3.1%)` | `2 (3.8%)` | `3 (5.1%)` |
| ↳ `other` | `97 (3.8%)` | `94 (3.9%)` | `5 (9.6%)` | `11 (18.6%)` |
| **`reinsertion_count >= 1`** | **`225 (8.9%)`** | **`214 (8.9%)`** | **`27 (51.9%)`** | **`18 (30.5%)`** |
| **`in_edit_war`** *(S4 `edit_war` / `ping_pong`)* | **`396 (15.6%)`** | **`374 (15.5%)`** | **`33 (63.5%)`** | **`19 (32.2%)`** |
| **`dispute_template_added`** | **`6 (0.2%)`** | **`6 (0.2%)`** | **`2 (3.8%)`** | **`0 (0.0%)`** |
| **ANY Level-2 community dispute signal** | **`611 (24.1%)`** | **`584 (24.2%)`** | **`45 (86.5%)`** | **`25 (42.4%)`** |
| `distinct_inserting_editors >= 2` | `199 (7.8%)` | `190 (7.9%)` | `21 (40.4%)` | `17 (28.8%)` |
| `distinct_articles >= 2` | `15 (0.6%)` | `15 (0.6%)` | `2 (3.8%)` | `1 (1.7%)` |
| `persistence` (`reinsertion >= 2` & `span >= 7d`) | `44 (1.7%)` | `43 (1.8%)` | `5 (9.6%)` | `6 (10.2%)` |
| `source_manipulation` *(diff `<ref>` removal/swap)* | `132 (5.2%)` | `128 (5.3%)` | `4 (7.7%)` | `5 (8.5%)` |

---

## Candidate Export (`results/candidates.classify.jsonl`)

`python3 research/37-s10-assembly/export_classify.py --per-topic 60` exported **511** unique candidate rows (`60` per each of the 9 non-quiet dev topics + `10` on `border-crisis-2023`, deduplicated by `_id = incident_key`):
- `0` missing `s3_diff` (`kind='span'`) rows across all 2,540 clusters.
- `0` candidates / `0` inserting revisions lacked `s3_diff` (`kind='edit'`) rows.

---

## Commands

```bash
# 1. Offline unit tests (TierRulesTests.cs 1:1 parity + synthetic evidence & ranking tests)
python3 -m unittest discover -s research/37-s10-assembly -p 'test_*.py' -v

# 2. Export top-60 Variant A candidates per dev topic for S9 Gemini classification
python3 research/37-s10-assembly/export_classify.py --per-topic 60

# 3. Validate signals contract compliance
python3 research/eval/validate.py variant signals -- python3 research/37-s10-assembly/variant.py --mode A
python3 research/eval/validate.py variant signals -- python3 research/37-s10-assembly/variant.py --mode B

# 4. Round 1 dev scorecards (no LLM assessments yet)
research/eval/run signals --name s10-a --out research/37-s10-assembly/results --baseline research/34-s7-repeat/results/s7-clusters.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode A
research/eval/run signals --name s10-b-noassess --out research/37-s10-assembly/results --baseline research/34-s7-repeat/results/s7-clusters.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B

# 5. Orchestrator Round 2: run Gemini batch classification & score Variants B, B+rel, and A+assess
research/22-judge/analyze.sh batch classify-edit --in research/37-s10-assembly/results/candidates.classify.jsonl --out research/37-s10-assembly/results/candidates.assess.carbon.jsonl --model gemini-4-carbon

research/eval/run signals --name s10-b --out research/37-s10-assembly/results --baseline research/34-s7-repeat/results/s7-clusters.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl

research/eval/run signals --name s10-b-rel --out research/37-s10-assembly/results --baseline research/37-s10-assembly/results/s10-b.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B+rel --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl

research/eval/run signals --name s10-a-assess --out research/37-s10-assembly/results --baseline research/34-s7-repeat/results/s7-clusters.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode A --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl

# 6. Tuning ablations (sides on + passage edit war) & cached B+rel
research/eval/run signals --name s10-b2 --out research/37-s10-assembly/results --baseline research/37-s10-assembly/results/s10-b.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl
research/eval/run signals --name s10-b2-coord3 --out research/37-s10-assembly/results --baseline research/37-s10-assembly/results/s10-b.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl --threshold min_coordination_signals=3

python3 research/37-s10-assembly/embed_cache.py
research/eval/run signals --name s10-b2-rel --out research/37-s10-assembly/results --baseline research/37-s10-assembly/results/s10-b.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B+rel --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl
research/eval/run signals --name s10-b2-rel-coord3 --out research/37-s10-assembly/results --baseline research/37-s10-assembly/results/s10-b.dev.scorecard.json -- python3 research/37-s10-assembly/variant.py --mode B+rel --assessments research/37-s10-assembly/results/candidates.assess.carbon.jsonl --threshold min_coordination_signals=3
```

## Tuning and ablations (2026-11-14, golden `2026-11-14.1`, dev split, limit 50, 0 failures in every run)

Sides, passage edit war, threshold flags, ablations and the embedding cache by a Gemini worker (`s10-tuning`, timed
out after the runs); the two combined runs at the bottom and this section by the orchestrator. Baseline for Δ in the
scorecards: `s10-b`. Assessments: `results/candidates.assess.carbon.jsonl` (S9 `classify-edit` on `gemini-4-carbon`).

| Run | Config | strict_p@10 | strict_p@25 | recall | level_accuracy | overclaim_rate | quiet_contested_or_above | seconds_per_topic |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `s10-a` | A additive, all reverted | 0.68 | 0.61 | 0.58 | 0.34 | 0.12 | 0 | 9.89 |
| `s10-b` | B: tiers + carbon assessment (S4 page edit war, no sides) | 0.79 | 0.68 | 0.62 | 0.67 | 0.26 | 2 | 14.23 |
| `s10-b-noew` | B without edit war | 0.79 | 0.67 | 0.58 | 0.59 | 0.27 | 1 | 13.55 |
| `s10-b2` | B2 = B + sides + passage edit war | 0.79 | 0.70 | 0.58 | 0.60 | 0.26 | 1 | 26.84 |
| `s10-b2-noassess` | B2 without assessment | 0.68 | 0.63 | 0.58 | 0.33 | 0.10 | 0 | 7.22 |
| `s10-b2-noreason` | B2 without removal reason | 0.76 | 0.70 | 0.55 | 0.57 | 0.24 | 1 | 22.70 |
| `s10-b2-noreinsert` | B2 without re-insertion | 0.84 | 0.72 | 0.58 | 0.60 | 0.25 | 1 | 20.97 |
| `s10-b2-noew` | B2 without edit war | 0.79 | 0.71 | 0.58 | 0.60 | 0.26 | 1 | 24.32 |
| `s10-b2-notemplate` | B2 without dispute template | 0.79 | 0.70 | 0.58 | 0.59 | 0.26 | 1 | 27.24 |
| `s10-b2-conf4` | B2, min_model_confidence=4 | 0.80 | 0.69 | 0.56 | 0.62 | 0.22 | 0 | 5.96 |
| `s10-b2-coord3` | B2, min_coordination_signals=3 | 0.81 | 0.70 | 0.58 | 0.63 | 0.22 | 1 | 6.57 |
| `s10-b2-rel` | B2 + topic relevance | 0.83 | 0.74 | 0.60 | 0.65 | 0.20 | 1 | 10.08 |
| `s10-b2-rel-coord3` | **B2 + relevance + coord 3 (recommended)** | 0.86 | 0.75 | 0.60 | 0.68 | 0.16 | 1 | 25.25 |
| `s10-b-rel-coord3-nosides` | B + relevance + coord 3, no sides | 0.86 | 0.70 | 0.63 | 0.74 | 0.17 | 2 | 23.64 |

**Reading.**
- The assessment carries the levels: without it (`-noassess`) level accuracy falls to 0.33 (everything `reverted`).
- Sides + passage edit war (B2) alone did **not** cut over-claiming (0.26) and lost level accuracy (0.67 → 0.60); it
  does halve the quiet-control level-2+ hits (2 → 1). No single signal ablation moves over-claiming below 0.24.
- What cuts it: **topic relevance** in the ranking (0.26 → 0.20; strict p@10 0.83) and **requiring 3 coordination
  signals for `campaign`** (0.22). Together: overclaim **0.16**, strict p@10 **0.86**, strict p@25 0.75, level accuracy
  0.68, recall 0.60.
- Without sides (S4 page-level edit war) the same combination gets higher level accuracy (0.74) and recall (0.63) but
  lower p@25 (0.70) and 2 quiet-control level-2+ hits. We take sides on: the empty state matters more for trust.
- `seconds_per_topic` varies 6–27 s with VPS load (several runs overlapped); not a latency measurement.
- Caveat: 15 configurations were compared on dev, so the winner is tuned in-sample. S11 (#38) must confirm on test.

**Recommendation for S11 (#38) and the app.**
1. `TierThresholds.MinCoordinationSignals`: **2 → 3** (C# default in `Features/Evidence/IncidentEvidence.cs`); keep the
   other defaults (confidence 4 loses recall: 0.56).
2. `InEditWar` = passage level (the claim's own content removed ≥ 2× and put back ≥ 1× by different editors), never
   page-level episode membership; count inserting editors only on the claim's side of an edit war.
3. Rank within tier by A's score × cosine(bge-m3 of the topic, bge-m3 of `claim_en`).

