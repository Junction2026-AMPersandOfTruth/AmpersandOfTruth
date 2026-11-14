#!/usr/bin/env bash
# P3.1 spike (#54): probe what our Oxylabs credentials can do (search, date ranges, news, Bing, page fetch, dates).
# Credentials from env (OXYLABS_USERNAME + OXYLABS_PASSWORD / OXYLABS_API_KEY) or the app's user-secrets; never printed.
#   scripts/oxylabs-spike.sh [--query '"Mainilan laukaukset"'] [--from 2014-01-01 --to 2014-12-31] [--fetch 3]
# Writes research/54-oxylabs-spike/out/<timestamp>/report.md (+ raw JSON, git-ignored). Paste report.md on #54.
set -euo pipefail
exec python3 "$(cd "$(dirname "$0")/.." && pwd)/research/54-oxylabs-spike/spike.py" "$@"
