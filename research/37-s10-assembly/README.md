# P1.S10 (#37) · Incident Assembly, Tiering & Ranking (Variants A / B / B+rel)

Combines S7 claim clusters (`research.s7_cluster` / `research.s7_member`, `method='fuzzy'`) with precomputed S1/S3/S4/S5/S6/S8 corpus signals and optional S9 Gemini `classify-edit` assessments into ranked, tiered `IncidentRecord`-compatible incidents (`CONTRACT.md` §2.2 / §3 + `#109` schema extensions).

---

## Round 1 Headline Numbers (No LLM Assessments Yet — Dev Split)

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
| `reinsertion_count` | `research.s7_member` | Number of cluster member insertions whose `inserted_at >= first_removed_at` (`first_removed_at = min(removed_at)` across members). |
| `insertion_span` | `corpus.revision.ts` / `s7_member.inserted_at` | `max(inserted_ts) - min(inserted_ts)` across inserting revisions (`timedelta(0)` for single-timestamp clusters). |
| `in_edit_war` | `research.s4_episode_rev` + `research.s4_episode` | `True` if any inserting or removing `rev_id` belongs to a default S4 episode (`is_default = 1`) with `kind IN ('edit_war', 'ping_pong')`. |
| `dispute_template_added` | `research.s3_diff.segments` (`kind='span'` / `'edit'`) | `True` if any non-moved `op='added'` segment matches `(?i)\{\{\s*(neutraalius\|lähteetön\|lähde\?\|korjattava\|kiistanalainen\|tarkista\|puolueellinen)`. |
| `distinct_inserting_editors` | `corpus.revision.user` | Count of distinct masked editor grouping keys across inserting revisions (`max(1, len(keys))`); IPv4 `/16` and IPv6 `/32` ranges count as one editor per `docs/evidence-standard.md` §3. |
| `distinct_articles` | `research.s7_cluster.n_pages` / `s7_member.page_id` | `max(1, n_pages, len(member_page_ids))`. |
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
- **Mode `B+rel` (tier + topic relevance):** Multiplies within-tier `score_A` by `max(0.0, cos(bge-m3(request.text), bge-m3(claim_en or prose[:500])))`.
- **Ablations (`--disable`, repeatable):** `removal_reason`, `reinsertion`, `edit_war`, `dispute_template`, `assessment` (ready for S11).

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
```
