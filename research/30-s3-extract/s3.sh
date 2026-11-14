#!/usr/bin/env bash
# Runs a research/30-s3-extract script with corpus credentials from user-secrets (never echoed).
#   research/30-s3-extract/s3.sh diffcache.py --pages 1256
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
if [ -z "${CORPUS_PW:-}" ]; then
  CORPUS_PW="$(dotnet user-secrets list --project "$ROOT/src/AmpersandOfTruth.Web" 2>/dev/null | sed -n 's/^Corpus:Password = //p')"
  export CORPUS_PW
fi
[ -n "$CORPUS_PW" ] || { echo "Corpus:Password not in user-secrets (see CLAUDE.md › Corpus)" >&2; exit 1; }
script="$1"; shift
exec uv run --quiet "$HERE/$script" "$@"
