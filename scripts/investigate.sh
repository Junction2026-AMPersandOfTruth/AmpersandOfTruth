#!/usr/bin/env bash
# Run the app's investigation pipeline (#39) from the shell, with the app's own services, config and caches (#40).
#
#   scripts/investigate.sh run "Talvisota" [--refresh] [--events]   JSON summary (or every event as JSONL)
#   scripts/investigate.sh warm scripts/demo-topics.txt [--cold]    each topic twice → Markdown cold/warm timing table
#   scripts/investigate.sh variant < request.json                   eval contract §2 (retrieval | signals | pipeline)
#   scripts/investigate.sh clear-runs                               drop cached runs (diffs + LLM replies stay cached)
#
# Results → stdout, logs → stderr. Needs the corpus tunnel (scripts/corpus-tunnel.sh), Ollama (or a cached topic) and
# Gemini:ApiKey — exactly like the app. Caches/ledger = src/AmpersandOfTruth.Web/app.db (--db <path> to isolate).
# Eval: research/eval/run pipeline --name p1-app --out research/39-pipeline/results -- scripts/investigate.sh variant
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet build "$ROOT/tools/AmpersandOfTruth.Investigate" -c Release -v quiet --nologo -clp:ErrorsOnly >&2
exec dotnet "$ROOT/tools/AmpersandOfTruth.Investigate/bin/Release/net10.0/AmpersandOfTruth.Investigate.dll" "$@"
