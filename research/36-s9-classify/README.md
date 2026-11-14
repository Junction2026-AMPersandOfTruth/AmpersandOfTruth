# P1.S9 (#36) · LLM Classification of Candidate Edits

Bake-off harness for candidate-edit classification on the **golden `dev` split** (`research/eval/golden.json`, `split == "dev"` only — **238 items** across **11 topics / 49 articles**, **0 items / 0 revisions missing `research.s3_diff`** when generated against the corpus; `--batch-by-article` packs them into **66 batch rows** of ≤ 6 candidates per article).

## Layout

| Path | Role |
|---|---|
| `inputs.py` | Builds blind JSONL batch inputs from corpus revisions, precomputed `research.s3_diff` (`kind='edit'`), parent `corpus.revision_text`, and optional `s4`/`s5`/`s6` structural tables; also prepares Stage-2 (Variant B) and Refute (Variant E) follow-up inputs offline. |
| `prompts/classify-edit-facts.{md,schema.json}` | **Variant C** prompt (`job: Classify`, `temperature: 0`): app's `classify-edit` + `{{data:facts}}` supporting-context block and guardrail instruction. |
| `prompts/classify-edit-batch.{md,schema.json}` | **Variant D** prompt (`job: Classify`, `temperature: 0`): classifies up to 6 candidates from one article in one call, returning `items[]` keyed by `candidate_id`. |
| `prompts/refute-classification.{md,schema.json}` | **Variant E** prompt (`job: Judge`, `temperature: 0`): argues the strongest ordinary-editing reading of a first-pass classification and returns `verdict ∈ {upheld, downgraded}` + `corrected_category`. |
| `score.py` | Offline scorer producing Markdown (stdout / `--out-md`) and JSON (`--json` / `--out-json`): outcomes, exact & 3-way category accuracy, confusion matrix, graffiti filter recall vs wrongly filtered `contested`/`campaign` incidents, verbatim evidence span check, and latency/tokens/cost per item & per topic. |
| `test_s9.py` | Offline `unittest` suite covering multi-revision input assembly, blindness/privacy checks, Finnish IPv4/IPv6/username summary masking, prompt/schema checks, and `score.py` across variants A–E. |
| `results/` | Generated input JSONLs (`golden-dev.classify*.jsonl`), Gemini batch outputs, and scorecards. |

---

## Variants A–E

- **Variant A — Single call per candidate (`classify-edit`):** Runs the app's embedded `src/AmpersandOfTruth.Web/Features/Analysis/Prompts/classify-edit.md` prompt once per item, comparing fast (`gemini-3.8-flash`) and strong (`gemini-4-hydrogen`) models.
- **Variant B — Two-stage cascade (`classify-edit` fast → strong):** Stage 1 runs a fast model on all 238 items. Items classified as obvious graffiti (`category ∈ {vandalism, test_or_nonsense}` with `confidence >= 4`) stop at Stage 1 (`1` call); all remaining items (`confidence <= 3`, non-graffiti categories, or non-`ok` outcomes) are routed to Stage 2 on a stronger model (`2` calls total).
- **Variant C — Structural facts vs text-only (`classify-edit-facts` vs `classify-edit`):** Adds `{{data:facts}}` (re-insertion count, editor count, S5 revert/insert summary flags, S4 conflict-episode overlap, S6 corpus-local editor profile flags) with an explicit rule that structural facts may calibrate confidence or break ties on borderline edits, never override an ordinary mistake, cleanup, or balanced dispute shown by the diff.
- **Variant D — Batch by article (`classify-edit-batch`):** Packs up to 6 candidates from the same `page_id` into one call (`_id = "<page_id>:<n>"`, 66 calls for 238 items). `score.py` splits each batch response back into item rows by `candidate_id` with `1/m` amortized calls, latency, tokens, and cost (missing `candidate_id`s are scored as `outcome: "invalid"`).
- **Variant E — Self-check refutation (`classify-edit` + `refute-classification`):** Runs a second sceptical pass (`job: Judge`, e.g. `gemini-4-carbon`) on substantive incident classifications (`pov_push`, `false_factual_claim`, `source_manipulation` by default). The second call constructs the strongest plausible ordinary-editing reading (`good_faith_error`, `content_dispute`, `other`, or `test_or_nonsense`) and returns `upheld` (keeps first-pass category) or `downgraded` (replaces with `corrected_category`).

---

## Exact Commands

### 1. Build inputs

Corpus-backed inputs (requires the MariaDB corpus tunnel on `127.0.0.1:3307`):

```bash
# Base per-item inputs for Variants A, B (stage 1), and E (pass 1) -> results/golden-dev.classify.jsonl (238 rows)
python3 research/36-s9-classify/inputs.py

# Variant C inputs with structural facts -> results/golden-dev.classify-facts.jsonl (238 rows)
python3 research/36-s9-classify/inputs.py --facts

# Variant D article-batched inputs (<= 6 items/call) -> results/golden-dev.classify-batch.jsonl (66 rows)
python3 research/36-s9-classify/inputs.py --batch-by-article
```

Offline follow-up inputs (no corpus query needed once `golden-dev.classify.jsonl` exists):

```bash
# Variant B stage-2 subset (items NOT filtered as high-confidence graffiti by stage 1)
python3 research/36-s9-classify/inputs.py --in-base research/36-s9-classify/results/golden-dev.classify.jsonl --stage2-from research/36-s9-classify/results/a-flash.out.jsonl --stage2-model gemini-3.8-flash --out research/36-s9-classify/results/b-stage2.in.jsonl

# Variant E second-pass refutation inputs (substantive categories from pass 1)
python3 research/36-s9-classify/inputs.py --in-base research/36-s9-classify/results/golden-dev.classify.jsonl --refute-from research/36-s9-classify/results/a-hydrogen.out.jsonl --refute-model gemini-4-hydrogen --out research/36-s9-classify/results/e-refute.in.jsonl
```

### 2. Run Gemini batches (outside seatbelt sandbox)

`PromptLibrary` loads embedded app prompts (`classify-edit`) first and then loads `Gemini__PromptDirectory`, so the same env prefix works across all variants:

```bash
# Variant A (single call — fast & strong models; also serves as Variant B stage 1 and C/E baseline)
Gemini__PromptDirectory=research/36-s9-classify/prompts research/22-judge/analyze.sh batch classify-edit --in research/36-s9-classify/results/golden-dev.classify.jsonl --out research/36-s9-classify/results/a-flash.out.jsonl --model gemini-3.8-flash --scope s9-a-flash --concurrency 16

Gemini__PromptDirectory=research/36-s9-classify/prompts research/22-judge/analyze.sh batch classify-edit --in research/36-s9-classify/results/golden-dev.classify.jsonl --out research/36-s9-classify/results/a-hydrogen.out.jsonl --model gemini-4-hydrogen --scope s9-a-hydrogen --concurrency 16

# Variant B (stage 2 strong model on non-graffiti items routed from stage 1; skip if a-hydrogen.out.jsonl already ran all 238)
Gemini__PromptDirectory=research/36-s9-classify/prompts research/22-judge/analyze.sh batch classify-edit --in research/36-s9-classify/results/b-stage2.in.jsonl --out research/36-s9-classify/results/b-stage2-hydrogen.out.jsonl --model gemini-4-hydrogen --scope s9-b-stage2 --concurrency 16

# Variant C (with structural facts)
Gemini__PromptDirectory=research/36-s9-classify/prompts research/22-judge/analyze.sh batch classify-edit-facts --in research/36-s9-classify/results/golden-dev.classify-facts.jsonl --out research/36-s9-classify/results/c-facts-hydrogen.out.jsonl --model gemini-4-hydrogen --scope s9-c-facts --concurrency 16

# Variant D (article-batched, <= 6 candidates per call)
Gemini__PromptDirectory=research/36-s9-classify/prompts research/22-judge/analyze.sh batch classify-edit-batch --in research/36-s9-classify/results/golden-dev.classify-batch.jsonl --out research/36-s9-classify/results/d-batch-hydrogen.out.jsonl --model gemini-4-hydrogen --scope s9-d-batch --concurrency 16

# Variant E (self-check refutation pass)
Gemini__PromptDirectory=research/36-s9-classify/prompts research/22-judge/analyze.sh batch refute-classification --in research/36-s9-classify/results/e-refute.in.jsonl --out research/36-s9-classify/results/e-refute-carbon.out.jsonl --model gemini-4-carbon --scope s9-e-refute --concurrency 16
```

### 3. Score variants A–E

Individual variants:

```bash
# Variant A (single-call runs)
python3 research/36-s9-classify/score.py research/36-s9-classify/results/a-flash.out.jsonl research/36-s9-classify/results/a-hydrogen.out.jsonl --inputs research/36-s9-classify/results/golden-dev.classify.jsonl

# Variant B (two-stage: stage 1 flash + stage 2 strong; accepts either b-stage2-hydrogen.out.jsonl or full a-hydrogen.out.jsonl)
python3 research/36-s9-classify/score.py --stage1 research/36-s9-classify/results/a-flash.out.jsonl --stage2 research/36-s9-classify/results/b-stage2-hydrogen.out.jsonl --inputs research/36-s9-classify/results/golden-dev.classify.jsonl

# Variant C (text-only vs structural facts)
python3 research/36-s9-classify/score.py research/36-s9-classify/results/a-hydrogen.out.jsonl research/36-s9-classify/results/c-facts-hydrogen.out.jsonl --inputs research/36-s9-classify/results/golden-dev.classify.jsonl

# Variant D (splits <page_id>:<n> batch rows back to items using --batch-inputs)
python3 research/36-s9-classify/score.py research/36-s9-classify/results/d-batch-hydrogen.out.jsonl --inputs research/36-s9-classify/results/golden-dev.classify.jsonl --batch-inputs research/36-s9-classify/results/golden-dev.classify-batch.jsonl

# Variant E (first-pass run + refute pass)
python3 research/36-s9-classify/score.py --refute research/36-s9-classify/results/e-refute-carbon.out.jsonl --refute-base research/36-s9-classify/results/a-hydrogen.out.jsonl --inputs research/36-s9-classify/results/golden-dev.classify.jsonl
```

Combined bake-off scorecard (writes both Markdown and JSON):

```bash
python3 research/36-s9-classify/score.py research/36-s9-classify/results/a-flash.out.jsonl research/36-s9-classify/results/a-hydrogen.out.jsonl research/36-s9-classify/results/c-facts-hydrogen.out.jsonl research/36-s9-classify/results/d-batch-hydrogen.out.jsonl --stage1 research/36-s9-classify/results/a-flash.out.jsonl --stage2 research/36-s9-classify/results/b-stage2-hydrogen.out.jsonl --refute research/36-s9-classify/results/e-refute-carbon.out.jsonl --refute-base research/36-s9-classify/results/a-hydrogen.out.jsonl --inputs research/36-s9-classify/results/golden-dev.classify.jsonl --batch-inputs research/36-s9-classify/results/golden-dev.classify-batch.jsonl --out-md research/36-s9-classify/results/scorecard.dev.md --out-json research/36-s9-classify/results/scorecard.dev.json
```

### 4. Offline unit tests

```bash
python3 -m unittest research/36-s9-classify/test_s9.py
```

---

## Input Variables & Blindness Rules

Every row in `results/golden-dev.classify.jsonl` contains `"_id"` plus the exact 9 `classify-edit` variables. Golden fields (`label`, `exclusion`, `category`, `description`, `why`, `notes`) are stripped when reading `golden.json`, and `split != "dev"` topics are never read.

| Variable | Construction |
|---|---|
| `article_title` | `corpus.article.title` (fallback: earliest inserting revision's `corpus.revision.title`). |
| `edit_timestamp` | Earliest inserting revision's UTC timestamp formatted ISO-8601 (`YYYY-MM-DDTHH:MM:SSZ`). |
| `editor_kind` | Across chronologically sorted `inserting_rev_ids`: `"anonymous"` (all IPs / `anonymous=1`), `"named"` (all registered accounts), or `"mixed"`. |
| `exposure` | Human-readable elapsed time from the **first** inserting revision to the **first** removing revision (e.g. `26 s`, `6 min`, `10 h 54 min`, `38 days`, `4.8 years`), or `"not reverted (still live in snapshot)"` when `removing_rev_ids` is empty. |
| `edit_summary` | Distinct non-empty `corpus.revision.comment` values across `inserting_rev_ids` in chronological order, masked via `mask_summary` and joined with ` &#124; `. |
| `revert_summary` | Distinct non-empty `corpus.revision.comment` values across `removing_rev_ids` in chronological order, masked via `mask_summary` and joined with ` &#124; `. |
| `removed_text` | Ordered union of distinct non-moved `op == "removed"` segment `text` values from `research.s3_diff` (`kind='edit'`) across `inserting_rev_ids`, newline-joined and capped at **2 000 chars** (`…`). |
| `inserted_text` | Ordered union of distinct non-moved `op == "added"` segment `text` values from `research.s3_diff` (`kind='edit'`) across `inserting_rev_ids`, newline-joined and capped at **4 000 chars** (`…`). |
| `context` | `<= 1 200` chars of the **first insertion's parent revision** (`old_rev_id` in `s3_diff`, else `parent_id` in `corpus.revision`) around the edit locus, clipped on whitespace boundaries with `…` when truncated. |
| `facts` (`--facts` only) | Privacy-safe structural signals from revision metadata + `research.s4_episode*`, `research.s5_revert_reason`, `research.s5_summary`, and `research.s6_editor_profile` (never usernames or IPs). |

### How `context` (≤ 1 200 chars) is located in the parent wikitext

`extract_context` locates the edit anchor inside the parent revision's `corpus.revision_text` using a 4-step cascade, then takes a balanced `<= 1 200`-character window snapped to word boundaries:

1. **Removed-segment match:** Exact substring match in `parent_text` for the first non-moved `removed` segment `text` from the earliest inserting diff.
2. **Prefix divergence (pure insertions):** When the edit only inserts text (`removed` fails to match) and the first inserting revision's text is cached, finds the first character offset where `parent_text` and `first_new_text` diverge.
3. **Section heading match:** Parses `/* Section */` from the first inserting edit summary and locates `== Section ==` in `parent_text`.
4. **Paragraph-index fallback:** Maps `old_paragraph` / `new_paragraph` from the first `s3_diff` segment onto blank-line-separated paragraphs in `parent_text` (or offset `0` if none).

### How `facts` (`--facts`) is built

`format_facts` emits a compact multi-line summary with pseudonyms (`Editor 1`, `Editor 2`, …) and zero raw usernames/IPs:
- **Insertion structure:** Inserting revision count (`re-insertions: k`), distinct inserting editors and breakdown (`named` vs `anonymous`), plus distinct reverting editors.
- **S5 summary signals:** `s5_revert_reason.removal_reasons` + `auto_family` on removing revisions, and `s5_summary.insert_flags` + `reasons` on inserting revisions.
- **S4 conflict overlap:** Distinct `s4_episode` overlaps (`kind`, `revert_count`, `named_participants`, `protection_hint`).
- **S6 editor profiles:** Per distinct named inserting editor (`Editor i`), corpus-local edit count, distinct pages, **leave-one-out** revert rate (`reverted_edits_by_others` excluding the current candidate's own reverted inserting edits to prevent target leakage on low-edit accounts), and `single_purpose` / `throwaway` / `bot` flags.

### Summary masking (`mask_summary`)

1. Flattens `[[Keskustelu käyttäjästä:…|keskustelu]]` talk links and replaces named/truncated `[[Käyttäjä:…]]` / `[[Toiminnot:Muokkaukset/…]]` links with `[editor]` while leaving complete IP targets intact for masking.
2. Masks all IPv4 (`/16` → `a.b.x.x`) and IPv6 (`/32` → `A:B:…`) addresses via `aot.mask_comment`, then protects those masked IP tokens so subsequent username/slot rules never split an IP on `.` or `:`.
3. Redacts MediaWiki temporary accounts (`~2025-…`), known revision editors, `(tehneet …)` lists, and Finnish/English revert-template slots (`jonka teki …`, `jonka on tehnyt …`, `Käyttäjän …`, `(tehnyt …)`, `käyttäjältä …`) to `[editor]`.

---

## Results (golden `2026-11-14.1`, dev split, 238 items; run 2026-11-14 by the orchestrator)

Full scorecards: `results/s9-scorecard.{AB,CD,E}.md` (+ `.json`); raw batch outputs `results/{A,C,D,E}.out.jsonl`.
Category accuracy is over the 235 items with a golden `category`. "Wrongly filtered" = golden contested/campaign items
that the graffiti filter (vandalism/test_or_nonsense, confidence ≥ 4) would drop.

| Variant | Prompt | Model(s) | Cat acc | Graffiti filtered | Contested/campaign wrongly filtered | Evidence verbatim | Calls/item | ms/item | $/item |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| A fast | `classify-edit` | `gemini-3.8-flash` | 66.0 % | 29/29 | **0**/64 | 99.4 % | 1.00 | 4 488 | 0.0024 |
| A | `classify-edit` | `gemini-4-hydrogen` | 67.7 % | 27/29 | **0**/64 | 99.7 % | 1.00 | 5 604 | 0.0107 |
| A strong | `classify-edit` | `gemini-4-carbon` | 66.8 % | 28/29 | **0**/64 | 100 % | 1.00 | 11 753 | 0.0086 |
| B | `classify-edit` | flash → carbon | 65.5 % | 29/29 | **0**/64 | 100 % | 1.78 | 13 568 | 0.0095 |
| C facts | `classify-edit-facts` | hydrogen / carbon | 66.0 / 67.7 % | 26/29 · 27/29 | **0**/64 | 99.9 % | 1.00 | 6 039 / 11 808 | 0.011 / 0.009 |
| D batch ≤ 6 | `classify-edit-batch` | hydrogen / carbon | 67.2 / 63.8 % | 26/29 · 27/29 | **0**/64 | 99.8 % | 0.28 | 3 016 / 5 499 | 0.0065 / 0.0062 |
| E refute | `classify-edit` + `refute-classification` | carbon + carbon | 66.8 % | 28/29 | **0**/64 | 100 % | 1.26 | 14 478 | 0.0107 |

**What S10 actually consumes** is `isClaim` from `TierRules` (category ∈ content_dispute, pov_push,
false_factual_claim, source_manipulation · confidence ≥ 3 · an evidence span verbatim in the inserted/removed text):

| Run | campaign | contested | reverted | not-incident |
|---|---:|---:|---:|---:|
| A `gemini-3.8-flash` | 1/1 | 52/63 (83 %) | 20/80 (25 %) | 7/94 (7 %) |
| A `gemini-4-hydrogen` | 1/1 | 55/63 (87 %) | 24/80 (30 %) | 10/94 (11 %) |
| A `gemini-4-carbon` | 1/1 | **58/63 (92 %)** | 26/80 (32 %) | 11/94 (12 %) |
| C `gemini-4-hydrogen` | 1/1 | 56/63 (89 %) | 24/80 (30 %) | 13/94 (14 %) |
| C `gemini-4-carbon` | 1/1 | 56/63 (89 %) | 27/80 (34 %) | 11/94 (12 %) |

**Reading.** The variants are within noise of each other on category accuracy (±2 pt on 235 items); most errors
are golden `other` items (largely non-incidents) predicted as `good_faith_error`, which has no effect on tiering.
- No variant ever filtered a golden contested/campaign item as graffiti, and flash filters all 29 graffiti items:
  **a flash triage stage is safe** and cheapest (B's saving is small here because only ~22 % of items are graffiti).
- **Structural facts (C) do not help**, and the refute pass (E) changes nothing for +26 % calls: drop both.
- **D (one call per article) matches A on hydrogen at 0.28 calls/item** — the cheapest option for the app if its
  claim texts hold up (not yet rated by the E3 judge); carbon degrades in batch mode.
- For S10 (#37) the assessment source is **A on `gemini-4-carbon`**: highest isClaim recall on contested (92 %).
  The 25–34 % isClaim rate on golden `reverted` items is what the dispute signals must keep at level 1.
