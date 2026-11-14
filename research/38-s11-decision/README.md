# P1.S11 (#38) · Signal decision: final numbers on the sealed test split

Golden `2026-11-14.1`, **test split** (language-politics, sami-prehistory, far-right, national-myths; 26 expected
contested/campaign items), limit 50, one run per variant by local Claude with `EVAL_ALLOW_TEST=1`, 0 failures.
Unlabelled hits (395 unique over all six runs) labelled by the E3 judge (`judge-incident@1` / `gemini-4-carbon`, 86 %
agreement on dev); the 180 it called contested/campaign challenged by the adversarial reviewer (a `benign` verdict
turns a judge-only hit into a miss). Decision metric: `strict_p@k_judged` (CONTRACT.md §6). Dev numbers: #11, #37.

| Variant | judged p@10 | judged p@25 | recall | level acc. | overclaim | dupl. | s/topic |
|---|---:|---:|---:|---:|---:|---:|---:|
| S1 identity reverts (naive baseline, #21) | 0.25 | 0.32 | 0.65 | 0.33 | 0.15 | 30 | 2.3 |
| S3-A revert-span items, S5-gated score (#30) | 0.62 | 0.54 | 0.62 | 0.33 | 0.07 | 15 | 2.0 |
| S7 claim clusters (#34) | 0.57 | 0.49 | 0.65 | 0.32 | 0.18 | 0 | 0.9 |
| S10-B: tiers + carbon assessment, old settings (#37) | 0.77 | 0.55 | 0.54 | 0.54 | 0.38 | 1 | 13.5 |
| S10-B2: + claim sides, passage edit war, topic relevance | 0.82 | 0.56 | 0.50 | 0.63 | 0.33 | 0 | 16.0 |
| **S10-B2-rel + MinCoordinationSignals 3 (chosen)** | 0.85 | 0.56 | 0.50 | 0.56 | 0.33 | 0 | 13.3 |

## Decision (local Claude, 2026-11-14): ship **S10-B2-rel with `MinCoordinationSignals` = 3**

**Pipeline stages** (research prototype → app, cloud lane):
1. Retrieval: R3-D (#27).
2. Candidates: S7 claim clusters over the content each identity revert took out (S1 spans + `wikidiff`), top 60 per
   topic by S10's additive score (re-insertions, accounts, articles, exposure, prose length).
3. Assessment: S9 `classify-edit` on `gemini-4-carbon` per candidate (category, confidence, quotes, `claim_en`).
4. Evidence: S5 removal reason, claim-side re-insertions and editors, passage-level edit war, dispute templates,
   source changes, kept-in-latest → `TierRules` (thresholds: defaults, except `MinCoordinationSignals` 3).
5. Ranking: tier first, then the additive score × cosine(bge-m3 topic, bge-m3 `claim_en`); cap 5 per article.

**Precomputed vs on demand.** Precomputed per corpus: S1 reverts, the `wikidiff` of reverted spans (`s3_diff`), S4/S5/S6
tables, S7 clusters. On demand per investigation (cached by content): the carbon assessment (~58 calls per topic,
≈ $0.50), two embeddings per incident, the R3 query expansion. Prototype time ≈ 13 s per topic for signals on top of
retrieval; the app path is #40's to measure.

**Why.** Best on the decision metric on the sealed test split: judged strict p@10 **0.85** vs 0.25 for the naive
baseline (+0.60) and 0.62 for the best untiered variant (S3-A); no duplicates; level accuracy 0.56 vs 0.33. Each
added piece helped on test as on dev: tiers (0.77), + sides/passage edit war/relevance (0.82), + coordination 3 (0.85).

**Known failure modes (for the methodology page, P2.10, and the pitch).**
- **Recall 0.50 on test** (0.60 dev): exclusions, the cap and the 60-candidate pool drop golden items — 6 of 9 in
  `sami-prehistory`. Untiered S3-A/S7 keep 0.62–0.65. Cheapest fix: a larger candidate pool (more assessment calls).
- **Over-claiming 0.33 on test** (0.16 dev), 9 of 27 matched items, 5 of them in `far-right`: three `campaign` calls on
  long disputes between regular editors (`multiple_editors` + `persistence` still reach three coordination signals),
  four golden `reverted` items read as contested (POV/content-dispute reading + re-insertion or a dispute template),
  two not-incidents shown at level 1. Next guard: count only new or single-purpose accounts (S6) towards coordination.
- Partial removals (S3-B finds 13/20 that identity reverts miss) are not in the shipped candidates yet.
- The test split is small (4 topics, 26 expected items): ±1 item moves recall by 0.04. The judge itself over-claims
  (11 % on dev), so judged precision is slightly optimistic; the reviewer removes the worst of it.

**Dropped.** Generative variants C (structural facts in the prompt) and E (refute pass) of S9 — no gain (#36).
Page-level edit-war signal — fired in every level-2+ over-claim on dev. S7 ranking by cluster size alone — favours
large vandalism clusters (judged p@10 0.57). R2/R4/R5 retrieval arms (#27).

**Not done (stop rule).** The red-team passes of #38 (sceptic per demo topic, miss-hunter, fairness check) and a
human read-through of the demo topics' top results — run them before the demo; the human spot check below covers the
decision itself.
