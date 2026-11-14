#!/usr/bin/env bash
# P3 web spread from the shell: the app's own tracer, Oxylabs client, caches and request caps (Features/WebSpread).
#
#   scripts/webspread.sh topic "Talvisota" [--rank N] [--events]   trace the topic's Nth-ranked incident (needs what the app needs)
#   scripts/webspread.sh claim [--events] < claim.json             trace a hand-written claim — only Oxylabs (+ Gemini) needed
#        e.g. scripts/webspread.sh claim < research/54-oxylabs-spike/example-claim.json
#
# Result JSON → stdout (every event as JSONL with --events), progress → stderr. Credentials: user-secrets
# Oxylabs:ApiKey, or Oxylabs:Username + Oxylabs:Password. Caches/ledger = src/AmpersandOfTruth.Web/app.db (--db to isolate).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cmd="${1:-}"; shift || true
case "$cmd" in
  topic) sub=webspread ;;
  claim) sub=webspread-claim ;;
  *) sed -n '2,10p' "$0" >&2; exit 2 ;;
esac
dotnet build "$ROOT/tools/AmpersandOfTruth.Investigate" -c Release -v quiet --nologo -clp:ErrorsOnly >&2
exec dotnet "$ROOT/tools/AmpersandOfTruth.Investigate/bin/Release/net10.0/AmpersandOfTruth.Investigate.dll" "$sub" "$@"
