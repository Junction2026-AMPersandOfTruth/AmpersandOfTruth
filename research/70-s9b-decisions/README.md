# P1.S9b (#70) · Gemini Decisions API (`gemini-decision-1`) on the golden dev split

Code by a Gemini worker (`gemini-4-carbon`); API probe, runs, block-handling fix and this write-up by the orchestrator
(local Claude), 2026-11-14. Golden `2026-11-14.1`, dev split, 238 items (all labels).

## The API (confirmed with real calls, no official reference page found)
`POST https://generativelanguage.googleapis.com/v1beta/decisions`, header `x-goog-api-key`, body
`{"model": "gemini-decision-1", "input": "<text>", "questions": {<key>: {"type": "noul"|"choice"|"score", "instructions": "…", "criteria": …}}}`.
`choice` criteria = `{option_id: description}`; `score` criteria = list of level descriptions, lowest first.
Answers: `noul` → `{noul: p, confidence}`; `choice` → `{choice, probabilities, confidence}`; `score` →
`{score (expected value), probabilities {"0".."n"}, legend, confidence}`. Usage reports input tokens only.
**Blocks:** HTTP 400 `{"error": {"code": "prohibited_content", "message": "Input blocked: … Prohibited Use policy …"}}`
for the whole request (1/238 here: Russian slurs against Finns — itself a vandalism signal). Measured latency
p50 0.6 s / p95 1.3 s for 10 questions and ~1.9k input tokens, not Google's 150–250 ms. Sources used to find the
shape: the launch post (blog.google, "Gemini Decisions API") and integration issues on GitHub (Arize phoenix #17916,
system_one_client #10); every field above was then verified against live responses (`results/probe.json`).

## Commands
```bash
source scripts/gemini/_common.sh && load_api_key          # GEMINI_API_KEY, never printed
python3 research/70-s9b-decisions/run.py --variant A       # inputs: research/36-s9-classify/results/golden-dev.classify.jsonl
python3 research/70-s9b-decisions/run.py --variant C       # + structural facts (golden-dev.classify-facts.jsonl)
python3 research/70-s9b-decisions/run.py --variant A --dry-run --out-dir /tmp/x   # request bodies only
python3 research/70-s9b-decisions/score.py                 # → results/s9b-scorecard.{md,json}
python3 -m unittest research/70-s9b-decisions/test_s9b.py
```
One request per item with 10 questions: 7 yes/no (`nonsense`, `factual_claim`, `changes_number_date_name`,
`political_or_national_pov`, `contradicts_replaced_text`, `alters_citation`, `removal_disputes_accuracy`), the
category `choice` (8 ids + `none_of_these`) and the evidence `level` `score` (docs/evidence-standard.md §1).
Responses are cached in `results/.cache/` (git-ignored).

## Results (`results/s9b-scorecard.md`)

| Run | Cat acc | Graffiti removed (29) | Contested/campaign lost (64) | Judge exact agreement | AUC incident / ≥ contested | p50 | $/item |
|---|---:|---:|---:|---:|---:|---:|---:|
| **Decisions A** | 58.7 % | 27 (93 %) at `nonsense ≥ 0.64` | **0** | **72.2 %** | **0.926 / 0.954** | 0.6 s | **$0.0002** |
| Decisions C (+ facts) | 58.7 % | 26 (90 %) | **0** | 66.2 % | 0.925 / 0.952 | 0.6 s | $0.0002 |
| S9 A `gemini-3.8-flash` | 66.0 % | 29 (100 %) | **0** | – | – | 4.2 s | $0.0024 |
| S9 A `gemini-4-hydrogen` | 67.7 % | 27 (93 %) | **0** | – | – | 5.6 s | $0.0107 |
| S9 A `gemini-4-carbon` | 66.8 % | 28 (97 %) | **0** | – | – | 9.8 s | $0.0086 |

- **Level as a ranking score is the standout:** P(level ≥ 2) separates golden contested/campaign items from the rest
  with AUC 0.954, Brier 0.096; the top bucket (P ≥ 0.8, 34 items) is 97 % contested. The `nonsense` threshold is
  chosen on dev (in-sample by construction); the AUCs need no threshold.
- **As a judge** (argmax level vs golden): 72 % exact, 96 % within one level; errors are mostly golden `reverted`
  items scored `contested` (23/80) — the same over-reading the generative classifier shows (~30 % isClaim on reverted).
- **Categories are weaker** than the generative models (59 % vs 66–68 %), and it gives no claim text or quotes.
- **Structural facts (C) hurt** the judge (66 % vs 72 %, overclaim 28 % vs 17 %): the facts make it read more into an edit.
- Finnish input works: no sign that Finnish is a weak point at this level of measurement.

## Recommendation for #38 (S11) and #37 (S10): **adopt, as a front stage and a ranking signal — not as the classifier**
- Triage: Decisions A `nonsense` filter (or `level` P(≥1) low) before any generative call — same safety as flash
  (0 contested lost), ~12× cheaper and ~7× faster; treat a `prohibited_content` block as graffiti evidence.
- Ranking: use P(level ≥ 2) as a score input in S10 (variant B within-tier order, or A's additive score).
- Keep the generative classifier (S9 A) for category, claim text and verified quotes — `TierRules` needs the quote.
- As an E3 judge it is a useful **second, differently built** judge next to `judge-incident`; disagreements go to the
  adversarial reviewer. If adopted in the app: `IDecisionModel` next to `IAnalysisModel` (plain REST, cache keyed by
  input + question-set hash, `prohibited_content` → blocked).
