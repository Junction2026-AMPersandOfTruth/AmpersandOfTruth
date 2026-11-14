#!/usr/bin/env bash
# Wrapper: load the Gemini workers' key (never printed) and run one-shot blind re-labelling for one topic.
#   research/20-golden/gemini_blind_api.sh <topic> [model]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE/../.."
source scripts/gemini/_common.sh
load_api_key
exec python3 "$HERE/gemini_blind_api.py" "$@"
