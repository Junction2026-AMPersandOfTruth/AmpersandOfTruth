#!/usr/bin/env bash
# scripts/analyze.sh with the Gemini workers' key (~/.gemini/.env, never printed) and the eval prompts
# (research/eval/prompts) — for E3 judge/review batches and other research LLM runs.
#   research/22-judge/analyze.sh batch judge-incident --in x.jsonl --out y.jsonl --model gemini-4-carbon --scope e3
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
source scripts/gemini/_common.sh
load_api_key
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
export Gemini__ApiKey="$GEMINI_API_KEY"
export Gemini__PromptDirectory="${Gemini__PromptDirectory:-$ROOT/research/eval/prompts}"
exec scripts/analyze.sh "$@"
