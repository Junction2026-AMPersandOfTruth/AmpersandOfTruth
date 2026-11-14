#!/usr/bin/env bash
# From a laptop with ROOT ssh access: let a teammate's public key open the corpus tunnel.
# The key is restricted on the VPS to forwarding 127.0.0.1:3306 only — no shell, no other ports.
# Usage: scripts/corpus-grant-tunnel.sh path/to/teammate.pub     (env: CORPUS_SSH_ROOT, CORPUS_SSH_KEY)
# Revoke: delete the line from /home/corpus-tunnel/.ssh/authorized_keys on the VPS.
set -euo pipefail
cd "$(dirname "$0")/.."
PUB="${1:?usage: $0 <public-key-file>}"
HOST="${CORPUS_SSH_ROOT:-root@srv2158939.hstgr.cloud}"
SSH=(ssh -o IdentitiesOnly=yes)
[ -n "${CORPUS_SSH_KEY:-}" ] && SSH+=(-i "$CORPUS_SSH_KEY")
[ -z "${CORPUS_SSH_KEY:-}" ] && [ -f .ssh/ai_vps_access ] && SSH+=(-i .ssh/ai_vps_access)

KEYLINE="restrict,port-forwarding,permitopen=\"127.0.0.1:3306\",command=\"/usr/sbin/nologin\" $(cat "$PUB")"

"${SSH[@]}" "$HOST" bash -s <<EOF
set -euo pipefail
id corpus-tunnel >/dev/null 2>&1 || useradd --create-home --shell /usr/sbin/nologin corpus-tunnel
install -d -m 700 -o corpus-tunnel -g corpus-tunnel /home/corpus-tunnel/.ssh
F=/home/corpus-tunnel/.ssh/authorized_keys
touch \$F && chown corpus-tunnel:corpus-tunnel \$F && chmod 600 \$F
grep -qF '$(awk '{print $2}' "$PUB")' \$F || echo '$KEYLINE' >> \$F
echo "✅ corpus-tunnel now has \$(wc -l < \$F) key(s)"
EOF
