# P1.E3 (#22) · Judge & Adversarial-Review Harness

Input builder, calibration scorer, and CONTRACT §5 converter for the E3 LLM judge (`research/eval/prompts/judge-incident.md`) and adversarial reviewer (`research/eval/prompts/adversarial-review.md`).

Standard library only (Python 3.10+), reading `corpus.*` and `research.*` through `research/lib/aot.py`.

## Files

| File | Role |
|---|---|
| `evidence.py` | `evidence.build(page_id, inserting_rev_ids, removing_rev_ids, other_page_ids=()) -> dict[str, str]` + `evidence.incident_key(ids) -> str`. Renders blind editor pseudonyms (`Editor <n>`), masked/scrubbed summaries, block-grouped wikitext diffs from `research.s3_diff`, and the 10 precomputed `history` signals from S1/S3/S5/S6. |
| `inputs.py` | Builds blind `.inputs.jsonl` files for golden-set calibration or variant `.unlabelled.jsonl` outputs, enforcing privacy checks (no raw usernames or IPs in `insertions`, `removals`, or `history`) and split hygiene (`dev` only unless `EVAL_ALLOW_TEST=1`). |
| `calibrate.py` | Scores `analyze.sh batch` outputs against golden `dev` labels per `(model, prompt_version)`: outcomes, exact agreement, per-label recall, 4×4 confusion matrix, binary agreement (`>= contested`), exclusion/category agreement, over/under-claim rates, confidence right vs wrong, verbatim evidence quote rate (`--inputs`), latency/tokens/cost, and up to 30 disagreements. |
| `tojsonl.py` | Converts `analyze.sh batch` outputs into CONTRACT.md §5 `judgements` or `reviews` JSONL files validated by `research/eval/validate.py`. |
| `test_judge.py` | Offline `unittest` suite covering editor grouping/numbering, summary/IP scrubbing, privacy assertions, `visible_for` & history signals, diff rendering/truncation, calibration metrics, and CONTRACT §5 conversion. |
| `analyze.sh` | Wrapper over `scripts/analyze.sh` configured with `research/eval/prompts`. |

## Commands

### 1. Build blind prompt inputs

Golden `dev` split (for judge calibration):

```bash
python3 research/22-judge/inputs.py golden --split dev --out research/22-judge/results/golden-dev.inputs.jsonl
```

Unlabelled incidents shown by a variant (`--with-flag` adds `flagged_as` = `tier` for `adversarial-review`):

```bash
python3 research/22-judge/inputs.py unlabelled research/28-s1-revert/results/s1-reverts.dev.unlabelled.jsonl --with-flag --out research/22-judge/results/s1-unlabelled.inputs.jsonl
```

### 2. Run Gemini batches (orchestrator, outside worker sandbox)

Judge calibration batch on golden `dev`:

```bash
research/22-judge/analyze.sh batch judge-incident --in research/22-judge/results/golden-dev.inputs.jsonl --out <out> --model <m> --scope e3-calibration --concurrency 16
```

Judge unlabelled variant incidents and run adversarial review:

```bash
research/22-judge/analyze.sh batch judge-incident --in research/22-judge/results/s1-unlabelled.inputs.jsonl --out <judge-out.jsonl> --model <m> --scope e3-s1 --concurrency 16
research/22-judge/analyze.sh batch adversarial-review --in research/22-judge/results/s1-unlabelled.inputs.jsonl --out <review-out.jsonl> --model <m> --scope e3-s1-review --concurrency 16
```

### 3. Score calibration against golden `dev`

```bash
python3 research/22-judge/calibrate.py <batch-out.jsonl>... --split dev --inputs research/22-judge/results/golden-dev.inputs.jsonl --json research/22-judge/results/calibration.json
```

### 4. Convert batch outputs to CONTRACT §5 JSONL & validate

```bash
python3 research/22-judge/tojsonl.py judgements <judge-out.jsonl> --out judgements.jsonl
python3 research/eval/validate.py judgements judgements.jsonl

python3 research/22-judge/tojsonl.py reviews <review-out.jsonl> --out reviews.jsonl
python3 research/eval/validate.py reviews reviews.jsonl
```

Then replay scoring without re-running the variant:

```bash
research/eval/run replay <variant.dev.responses.jsonl> --judgements judgements.jsonl --reviews reviews.jsonl
```

### 5. Run offline unit tests

```bash
python3 -m unittest discover -s research/22-judge -p "test_*.py" -v
```

## Calibration results (2026-11-14, golden `2026-11-14.1`, dev split, 238 items) — `results/calibration.v1.md`

`judge-incident@1` on the inputs from `inputs.py golden --split dev` (714 calls, ~$11). Target ≥ ~80 % exact agreement (#22).

| Model | Exact label agreement | ≥ contested vs below | contested recall | reverted recall | not-incident recall | Over / under-claim | Quotes verbatim | ms/item | $/item |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| **`gemini-4-carbon`** (job `Judge`) | **86.0 %** (203/236; 2 truncated) | 92.8 % | 92 % | 85 % | 83 % | 11.4 % / 2.5 % | 99.8 % | 19 600 | 0.018 |
| `gemini-4-argon` | 82.8 % | 89.1 % | | | | | | | |
| `gemini-4-hydrogen` | 80.7 % | 90.8 % | | | | | | | |

**Decision: `gemini-4-carbon` is the E3 judge** for unlabelled hits (`tojsonl.py judgements` →
`research/eval/run replay … --judgements`). Its errors lean to over-claiming (11 golden `reverted` items judged
`contested`, 15 golden `not-incident` judged `reverted`), so judged precision is slightly optimistic; the adversarial
reviewer (below) is the counterweight for judge-only hits.

**Adversarial reviewer, tried once on the baseline** (S1 top 10 per dev topic, 100 incidents claimed `reverted`,
`results/review.s1-top10.out.jsonl`): against golden labels it called **3/3 not-incidents `benign`**, upheld
**15/15 contested + 1/1 campaign**, and 14/16 reverted (1 benign, 1 unclear); unlabelled hits split 30 survive / 29 benign.
