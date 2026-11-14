#!/usr/bin/env bash
# Warm the caches before a demo and measure the #40 latency budget: every topic in the file runs twice
# (pass 1 fills the run cache, pass 2 must be warm). Markdown table on stdout — paste it on issue #40.
#   scripts/warm.sh [topics-file] [--cold]     default topics: scripts/demo-topics.txt; --cold clears cached runs first
# Run early, never during a demo: pass 1 hits the VPS (2 vCPUs) and Gemini.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FILE="${1:-$ROOT/scripts/demo-topics.txt}"
[[ "${1:-}" == --* ]] && FILE="$ROOT/scripts/demo-topics.txt"
ARGS=(); for a in "$@"; do [[ "$a" == --* ]] && ARGS+=("$a"); done
exec "$ROOT/scripts/investigate.sh" warm "$FILE" "${ARGS[@]}"
