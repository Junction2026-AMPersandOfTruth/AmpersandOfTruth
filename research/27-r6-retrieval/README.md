# P1.R6 (#27) · Retrieval decision: scorecards on the sealed test split

Golden `2026-11-14.1`. Test split = language-politics, sami-prehistory, far-right, national-myths + moon-landing-hoax
(out-of-corpus). Run once, by local Claude, with `EVAL_ALLOW_TEST=1`, using exactly the dev commands of each variant
(`command` in each dev scorecard JSON). Files: `results/<name>.test.{scorecard.md,scorecard.json,responses.jsonl}`.

| Variant | test recall@10 | test recall@20 | test nDCG@10 | OOS detected | false alarms | s/query | LLM calls | dev recall@20 | dev nDCG@10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| R1 vector (baseline, #8) | 0.27 | 0.39 | 0.57 | 0.00 | 0.00 | 1.0 | 0 | 0.39 | 0.54 |
| R3-E LLM expand + scope (#24) | 0.32 | 0.45 | 0.66 | 1.00 | 0.00 | 17.1 | 19 | 0.53 | 0.69 |
| R3-D LLM expand, no scope gate | 0.32 | 0.45 | 0.66 | 0.00 | 0.00 | 12.6 | 19 | 0.53 | 0.69 |
| R3-B LLM multi-query | 0.28 | 0.43 | 0.56 | 0.00 | 0.00 | 9.2 | 19 | 0.53 | 0.66 |
| R2-C vector + FULLTEXT RRF (#23) | 0.25 | 0.34 | 0.51 | 0.00 | 0.00 | 1.4 | 0 | 0.33 | 0.46 |
| R4-D category re-rank (#25) | 0.24 | 0.37 | 0.57 | 0.00 | 0.00 | 0.7 | 0 | 0.36 | 0.41 |
| R5 hybrid claim index (#26) | 0.21 | 0.33 | 0.50 | 0.00 | 0.00 | 1.8 | 0 | 0.34 | 0.44 |

nDCG@10 by query style on test:

| Variant | en | fi | broad | sv |
|---|---:|---:|---:|---:|
| r1-vector | 0.64 | 0.62 | 0.38 | 0.66 |
| r3-e | 0.69 | 0.70 | 0.51 | 0.74 |
| r3-d | 0.69 | 0.70 | 0.51 | 0.74 |
| r3-b | 0.59 | 0.62 | 0.39 | 0.64 |
| r2-c | 0.60 | 0.71 | 0.22 | 0.51 |
| r4-d | 0.64 | 0.68 | 0.40 | 0.58 |
| r5-hybrid | 0.57 | 0.57 | 0.35 | 0.52 |

## Provenance
All 14 scorecards (7 dev, 7 test) use golden `2026-11-14.1`, dataset `m-2026-10`, 0 failures, and the identical
variant command per row (dev: commits `ebfdadc`/`f4bb085`, test: `f985802`). Nothing was retuned on test.

## Decision (local Claude, 2026-11-14): **R3-D retrieval, with R3-E's scope verdict as a non-blocking warning**

**Pipeline.** One cached `expand-topic` call per distinct topic text (`research/24-r3-query/prompts/expand-topic.md`, job
`QueryExpand` → `gemini-4-hydrogen`, temperature 0) returns `in_scope`, `topic_fi`, Finnish title guesses and expansion
queries. Mode D: bge-m3 vector search (all languages, folded to the fi sibling through `wikidata_id`) for the raw text,
`topic_fi`, the title guesses and the expansions, fused with reciprocal rank fusion (k = 60), top 20.
`mhnsw_ef_search` = 80 (server default). Code: `research/24-r3-query/variant.py --mode D` (E = D + `out_of_scope = !in_scope`).

**Why.**
- R3-D/E are best on the sealed test split on every headline metric: recall@20 0.45 (R1 0.39), nDCG@10 0.66 (R1 0.57),
  and they tie each other exactly (E only adds the scope flag). The largest gain is on vague `broad` phrasings (nDCG
  0.51 vs 0.38), which is what a real user types.
- "Simplest within 2–3 points of the best recall@20": R3-B (0.43) qualifies on recall@20 but is 10 points below on
  nDCG@10 (0.56, below R1) and needs the same LLM call, so it is not simpler in stages — it drops the title-guess arm.
  We take D because the ranking quality is what the user sees. Every variant without an LLM call is ≥ 6 points below
  on recall@20.
- The scope verdict comes from the same LLM call. It caught every out-of-corpus control (dev 2/2, test 1/1) with no
  false alarms on either split, but 3 controls are far too few to *block* a search: show it as a warning
  ("this topic may be outside the corpus") and still show results.

**Rejected.** R2-C keyword + vector RRF: below R1 on recall@20 on both splits; it is the best on precise Finnish
queries (`fi` nDCG 0.71 on test) but collapses on `broad` (0.22) and `sv` (0.51). R4-D category re-rank: below R1 on
dev (nDCG 0.41), equal on test (0.57) — unstable, not worth a stage. R5 claim index: below R1 on both — it finds
*claims*, not *topic articles*; it belongs in the signal stage (#26 says the same).

**Known weak spots.**
- **Latency (measured, research prototype through the tunnel):** R3-D 10–13 s per query, R3-E 17 s, R1 1–3 s. R3 runs
  4–8 embeddings + SQL searches per query; the expansion itself is cached by topic text, so the LLM call is not the
  whole cost. E is 5–7 s slower than D on both splits for a reason not yet understood (the scope verdict should come
  from the same cached call) — check in the port. The app path (pooled connections, embedding cache, parallel arms)
  is unmeasured; #40 must time it. Demo topics must be warmed (`warm.sh`).
- **Failure mode:** if `expand-topic` fails or times out (outcome ≠ ok), fall back to R1 for that query and say so.
- **In-sample tuning on dev:** R3's recall@20 lead over R1 shrinks from +0.14 (dev) to +0.06 (test), and R3-B's nDCG
  lead turns negative (+0.12 → −0.01); the middle of the ranking reshuffles on test (R4-D rises, R3-B falls). Only the
  top (R3-D/E) is stable. Test has 4 topics and 19 queries: treat the margins as indicative.
- recall@20 = 0.45: about half of the golden articles (many grade-1 "related") are not in the top 20; the signal
  stage only sees what retrieval returns.

**Build (cloud lane, #27 "Build").** Port mode D (+ the scope verdict as a warning) into `TopicSearch`
(`Features/Investigations/`): the `expand-topic` prompt into `Features/*/Prompts/`, the multi-query RRF, the fold, R1 as
the fallback. Score it through the eval adapter against **`research/24-r3-query/results/r3-d.dev.scorecard.json`**
(must match within tolerance on dev); the test split is used once more, only for the final production check.

## Review
An independent Gemini reviewer (`gemini-4-carbon`, saw only this README and the scorecard headers) checked the first
draft: it found a false "ranking unchanged" claim, an unexplained E-vs-D latency gap stated as "free", a wrong
FULLTEXT explanation, and the wrong scorecard for the build parity check. All four are fixed above; its request to
keep the scope gate non-blocking is adopted. Remaining before final: a human spot check of 10 random R3-D results
(#22 protocol) and ~20 borderline in/out-of-scope queries.
