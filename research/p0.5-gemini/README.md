# P0.5 (#16) — Gemini model comparison on real reverted Finnish edits

Question: which Gemini tier should classify edits and extract claims, and is any of them weak on Finnish?

| File | What |
|---|---|
| `extract_sample.py` | Builds `sample20.jsonl` from dataset M (`m-2026-10` parquet): single-edit identity reverts (sha1), word-level diff → inserted/removed text + context. Hand-picked for diversity (graffiti, contested history, good faith, sources, flagged summaries). Usernames replaced by `<user>`. |
| `sample20.jsonl` | The 20 edits (Finnish Wikipedia text, CC BY-SA 4.0 — attribution: fi.wikipedia.org contributors; diff links inside). |
| `revert_stats.txt` | Cheap revert statistics gathered while sampling (P0.6 does this properly). |
| `results/classify-edit-v1.jsonl` | 20 edits × 5 models through the app's own client (`classify-edit` prompt v1). |
| `compare.py` | Objective comparison → `REPORT.md` + `human-review.csv` (blank columns for the human check). |
| `blind.py`, `judge/` | Blinded outputs for an independent different-vendor judge (Claude) and its ratings; `mapping.json` un-blinds them. |

## Reproduce

```bash
# 1. sample (needs the parquet files; ~2 s from the hand-picked list)
gh release download m-2026-10 --repo MariaDB/mariadb-wikipedia-datasets -D /tmp/m -p '*.parquet'
python3 research/p0.5-gemini/extract_sample.py --data /tmp/m --out research/p0.5-gemini/sample20.jsonl   # needs duckdb

# 2. run all models (key: user-secrets Gemini:ApiKey or env Gemini__ApiKey). Cached + resumable: a rerun costs $0.
scripts/analyze.sh batch classify-edit --in research/p0.5-gemini/sample20.jsonl \
  --out research/p0.5-gemini/results/classify-edit-v1.jsonl \
  --model gemini-4-hydrogen,gemini-4-carbon,gemini-4-argon,gemini-3.8-flash,gemini-3.5-flash-lite \
  --scope p0.5-finnish-20 --concurrency 8

# 3. report
python3 research/p0.5-gemini/compare.py --sample research/p0.5-gemini/sample20.jsonl \
  --results research/p0.5-gemini/results/classify-edit-v1.jsonl --judge research/p0.5-gemini/judge-claude.json \
  --out research/p0.5-gemini/REPORT.md --review-csv research/p0.5-gemini/human-review.csv
```

Prompt variants without a rebuild: copy `src/AmpersandOfTruth.Web/Features/Analysis/Prompts/classify-edit.*` to a folder, edit, and run with
`Gemini__PromptDirectory=<folder>` (the content hash changes, so the cache won't serve old answers).
