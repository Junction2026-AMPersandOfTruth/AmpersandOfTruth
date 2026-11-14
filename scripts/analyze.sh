#!/usr/bin/env bash
# Run LLM prompts from the shell / research scripts through the app's OWN client, prompts, cache and ledger (issue #16).
#
#   scripts/analyze.sh prompts
#   scripts/analyze.sh run classify-edit --vars edit.json [--model gemini-4-carbon] [--no-cache|--refresh]
#   scripts/analyze.sh batch classify-edit --in items.jsonl --out results.jsonl \
#       [--model gemini-4-hydrogen,gemini-4-carbon] [--scope my-experiment] [--concurrency 6] [--limit 5]
#   scripts/analyze.sh usage [--since 2026-11-14] [--scope my-experiment]
#   scripts/analyze.sh cache-clear [--prompt classify-edit]
#
# JSON → stdout, logs → stderr. Cache/ledger = src/Junction.Web/app.db (shared with the app; --db <path> to isolate).
# Batch output is appended and resumable: re-running skips (id, model) pairs already done. Pass --model explicitly when resuming.
# Key: user-secrets Gemini:ApiKey, or env Gemini__ApiKey. Prompt variants without a rebuild: env Gemini__PromptDirectory=<dir>.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet build "$ROOT/tools/Junction.Analyze" -c Release -v quiet --nologo -clp:ErrorsOnly >&2
exec dotnet "$ROOT/tools/Junction.Analyze/bin/Release/net10.0/Junction.Analyze.dll" "$@"
