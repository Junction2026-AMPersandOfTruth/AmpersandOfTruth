#!/usr/bin/env bash
# Boots the real app, curls key routes, shuts down. Use after UI/routing changes.
# Usage: scripts/smoke.sh [/route ...]   (defaults: / /notes /health)
set -euo pipefail
cd "$(dirname "$0")/.."
PORT=${PORT:-$((5300 + RANDOM % 600))}   # random → parallel worktrees don't collide
LOG=$(mktemp -t ampersandoftruth-smoke.XXXXXX)
if [ $# -gt 0 ]; then ROUTES=("$@"); else ROUTES=(/ /notes /health); fi

dotnet build --nologo -v quiet -clp:ErrorsOnly src/AmpersandOfTruth.Web
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --project src/AmpersandOfTruth.Web --no-launch-profile --urls "http://localhost:$PORT" > "$LOG" 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do curl -sf "http://localhost:$PORT/health" >/dev/null && break; sleep 0.5; done

fail=0
for r in "${ROUTES[@]}"; do
  code=$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT$r")
  if [ "$code" = "200" ]; then echo "✅ $code $r"; else echo "❌ $code $r"; fail=1; fi
done
[ $fail -eq 0 ] || { echo "--- server log ---"; tail -40 "$LOG"; exit 1; }
