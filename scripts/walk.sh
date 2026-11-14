#!/usr/bin/env bash
# Walks the P2 demo flow in headless Chrome/Chromium at 1280x720 and 390x844, saving screenshots + report.md.
# Usage: scripts/walk.sh [--url http://localhost:PORT] [--out DIR]
set -euo pipefail

CALLER_PWD="$PWD"
cd "$(dirname "$0")/.."

URL=""
OUT=""

while [ $# -gt 0 ]; do
  case "$1" in
    --url)
      URL="${2:?missing value for --url}"
      shift 2
      ;;
    --url=*)
      URL="${1#--url=}"
      shift
      ;;
    --out)
      OUT="${2:?missing value for --out}"
      shift 2
      ;;
    --out=*)
      OUT="${1#--out=}"
      shift
      ;;
    -h|--help)
      echo "Usage: scripts/walk.sh [--url http://localhost:PORT] [--out DIR]"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      echo "Usage: scripts/walk.sh [--url http://localhost:PORT] [--out DIR]" >&2
      exit 1
      ;;
  esac
done

if [ -z "$OUT" ]; then
  OUT="research/review-rounds/$(date -u +%Y%m%d-%H%M)"
elif [[ "$OUT" != /* ]]; then
  OUT="$CALLER_PWD/$OUT"
fi

APP_PID=""
LOG=""
cleanup() {
  if [ -n "$APP_PID" ]; then
    kill "$APP_PID" 2>/dev/null || true
  fi
  if [ -n "$LOG" ] && [ -f "$LOG" ]; then
    rm -f "$LOG"
  fi
}
trap cleanup EXIT

if [ -z "$URL" ]; then
  PORT=${PORT:-$((5300 + RANDOM % 600))}
  LOG=$(mktemp -t ampersandoftruth-walk.XXXXXX)
  URL="http://localhost:$PORT"

  dotnet build --nologo -v quiet -clp:ErrorsOnly src/AmpersandOfTruth.Web
  ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --project src/AmpersandOfTruth.Web --no-launch-profile --urls "$URL" > "$LOG" 2>&1 &
  APP_PID=$!

  healthy=0
  for _ in $(seq 1 60); do
    if curl -sf "$URL/health" >/dev/null; then
      healthy=1
      break
    fi
    if ! kill -0 "$APP_PID" 2>/dev/null; then
      break
    fi
    sleep 0.5
  done

  if [ "$healthy" -ne 1 ]; then
    echo "❌ App failed to start at $URL" >&2
    echo "--- server log ---" >&2
    tail -40 "$LOG" >&2
    exit 1
  fi
fi

if [ ! -d scripts/walk/node_modules ]; then
  npm ci --prefix scripts/walk
fi

mkdir -p "$OUT"
node scripts/walk/walk.mjs --url "$URL" --out "$OUT"
