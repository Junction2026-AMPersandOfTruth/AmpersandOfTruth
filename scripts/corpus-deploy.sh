#!/usr/bin/env bash
# From a laptop with ROOT ssh access: push deploy/corpus to the VPS and run corpus-up.sh there.
# Usage: scripts/corpus-deploy.sh            (env: CORPUS_SSH_ROOT, CORPUS_SSH_KEY)
set -euo pipefail
cd "$(dirname "$0")/.."
HOST="${CORPUS_SSH_ROOT:-root@srv2158939.hstgr.cloud}"
SSH=(ssh -o IdentitiesOnly=yes)
[ -n "${CORPUS_SSH_KEY:-}" ] && SSH+=(-i "$CORPUS_SSH_KEY")
[ -z "${CORPUS_SSH_KEY:-}" ] && [ -f .ssh/ai_vps_access ] && SSH+=(-i .ssh/ai_vps_access)

"${SSH[@]}" "$HOST" 'mkdir -p /opt/junction/corpus'
# .env lives only on the VPS → excluded so it's never overwritten or copied back.
rsync -az --delete --exclude .env --exclude .conf-hash -e "${SSH[*]}" deploy/corpus/ "$HOST:/opt/junction/corpus/"
rsync -az -e "${SSH[*]}" scripts/corpus-up.sh "$HOST:/opt/junction/corpus/corpus-up.sh"
"${SSH[@]}" "$HOST" 'chown -R root:root /opt/junction/corpus && /opt/junction/corpus/corpus-up.sh'
