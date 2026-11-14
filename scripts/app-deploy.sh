#!/usr/bin/env bash
# Deploy the web app to the team VPS (needs ROOT ssh, like corpus-deploy.sh): https://asot.tech
#   scripts/app-deploy.sh            publish the current checkout and (re)start the container
#   scripts/app-deploy.sh --logs     tail the container logs
# Secrets live only in /opt/junction/app/.env on the VPS (root-only). Created on first deploy from
# /opt/junction/corpus/.env (CORPUS_RO_PASSWORD) and, if present, GEMINI_API_KEY from ~/.gemini/.env on this laptop.
set -euo pipefail
cd "$(dirname "$0")/.."
HOST="${CORPUS_SSH_ROOT:-root@srv2158939.hstgr.cloud}"
SSH=(ssh -o IdentitiesOnly=yes)
[ -n "${CORPUS_SSH_KEY:-}" ] && SSH+=(-i "$CORPUS_SSH_KEY")
[ -z "${CORPUS_SSH_KEY:-}" ] && [ -f .ssh/ai_vps_access ] && SSH+=(-i .ssh/ai_vps_access)
[ -z "${CORPUS_SSH_KEY:-}" ] && [ ! -f .ssh/ai_vps_access ] && SSH=(ssh)
DIR=/opt/junction/app

if [ "${1:-}" = "--logs" ]; then exec "${SSH[@]}" "$HOST" "cd $DIR && docker compose logs -f --tail 100"; fi

OUT=$(mktemp -d -t aot-publish.XXXXXX); trap 'rm -rf "$OUT"' EXIT
echo "▶ publish $(git rev-parse --short HEAD) ($(git branch --show-current))"
dotnet publish src/AmpersandOfTruth.Web -c Release -o "$OUT" --nologo -v quiet -clp:ErrorsOnly
git rev-parse --short HEAD > "$OUT/REVISION"

"${SSH[@]}" "$HOST" "mkdir -p $DIR/publish"
rsync -az --delete -e "${SSH[*]}" "$OUT/" "$HOST:$DIR/publish/"
rsync -az -e "${SSH[*]}" deploy/app/docker-compose.yml "$HOST:$DIR/docker-compose.yml"

# First deploy only: write .env (never printed, never copied back).
if ! "${SSH[@]}" "$HOST" "test -f $DIR/.env"; then
  GEMINI_KEY=""
  [ -f "$HOME/.gemini/.env" ] && GEMINI_KEY="$(grep -E '^GEMINI_API_KEY=' "$HOME/.gemini/.env" | tail -1 | cut -d= -f2- | tr -d "\"' ")"
  printf 'Gemini__ApiKey=%s\n' "$GEMINI_KEY" | "${SSH[@]}" "$HOST" "umask 077 && cat > $DIR/.env.gemini && \
    pw=\$(sed -n 's/^CORPUS_RO_PASSWORD=//p' /opt/junction/corpus/.env | tr -d \"\\\"'\") && \
    { echo \"Corpus__Password=\$pw\"; cat $DIR/.env.gemini; } > $DIR/.env && rm $DIR/.env.gemini && chmod 600 $DIR/.env"
  echo "▶ created $DIR/.env on the VPS"
fi

"${SSH[@]}" "$HOST" "cd $DIR && docker compose up -d --force-recreate --remove-orphans >/dev/null && sleep 8 && \
  docker exec aot-web sh -c 'cat /app/REVISION' && docker compose ps --format '{{.Name}} {{.Status}}'"
for i in $(seq 1 30); do
  code=$(curl -s -o /dev/null -w '%{http_code}' https://asot.tech/health || true)
  [ "$code" = "200" ] && { echo "✅ https://asot.tech is up"; exit 0; }
  sleep 2
done
echo "❌ /health did not return 200 (last: $code) — scripts/app-deploy.sh --logs"; exit 1
