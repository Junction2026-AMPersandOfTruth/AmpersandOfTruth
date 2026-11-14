#!/usr/bin/env bash
# Runs a research/20-golden script with corpus credentials from user-secrets (never echoed).
#   research/20-golden/golden.sh q.py "SELECT 1"
#   research/20-golden/golden.sh extract.py --topic civil-war-1918
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
if [ -z "${CORPUS_PW:-}" ]; then
  CORPUS_PW="$(dotnet user-secrets list --project "$ROOT/src/AmpersandOfTruth.Web" 2>/dev/null | sed -n 's/^Corpus:Password = //p')"
  export CORPUS_PW
fi
[ -n "$CORPUS_PW" ] || { echo "Corpus:Password not in user-secrets (see CLAUDE.md › Corpus)" >&2; exit 1; }
script="$1"; shift
exec uv run --quiet "$HERE/$script" "$@"
