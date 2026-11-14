#!/usr/bin/env bash
# On a laptop: forward localhost:3307 → the corpus MariaDB on the VPS loopback (127.0.0.1:3306),
# and localhost:11435 → the VPS Ollama/bge-m3 (127.0.0.1:11434, issue #15).
# The app's defaults (ConnectionStrings:Corpus, Embedding:FallbackOllamaUrl) point at these, so this is all you need.
#
#   scripts/corpus-tunnel.sh          start in the background (idempotent), return immediately
#   scripts/corpus-tunnel.sh --fg     run in the foreground, auto-reconnect (Ctrl-C to stop)
#   scripts/corpus-tunnel.sh --status / --stop
#
# Env: CORPUS_SSH (default corpus-tunnel@srv2158939.hstgr.cloud — a forward-only account; ask for your
#      public key to be added via scripts/corpus-grant-tunnel.sh), CORPUS_SSH_KEY (identity file),
#      CORPUS_LOCAL_PORT (default 3307), CORPUS_OLLAMA_LOCAL_PORT (default 11435; 0 = don't forward Ollama).
set -euo pipefail
cd "$(dirname "$0")/.."
TARGET="${CORPUS_SSH:-corpus-tunnel@srv2158939.hstgr.cloud}"
PORT="${CORPUS_LOCAL_PORT:-3307}"
OLLAMA_PORT="${CORPUS_OLLAMA_LOCAL_PORT:-11435}"
SOCK="${TMPDIR:-/tmp}/junction-corpus-tunnel-$PORT.sock"

SSH=(ssh -N -L "127.0.0.1:$PORT:127.0.0.1:3306"
     -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3
     -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10)
KEY="${CORPUS_SSH_KEY:-}"
[ -z "$KEY" ] && [ -f .ssh/ai_vps_access ] && KEY=.ssh/ai_vps_access
[ -n "$KEY" ] && SSH+=(-i "$KEY" -o IdentitiesOnly=yes)
# Keys granted before Ollama existed may only permitopen 3306: the forward still starts, but Ollama requests
# fail and the app falls back/degrades. Fix: re-run scripts/corpus-grant-tunnel.sh for that key.
[ "$OLLAMA_PORT" != 0 ] && SSH+=(-L "127.0.0.1:$OLLAMA_PORT:127.0.0.1:11434")

port_open() { nc -z 127.0.0.1 "$PORT" 2>/dev/null; }

case "${1:-}" in
  --stop)
    ssh -S "$SOCK" -O exit "$TARGET" 2>/dev/null && echo "🛑 tunnel stopped" || echo "no tunnel running (via $SOCK)"
    ;;
  --status)
    if port_open; then echo "✅ 127.0.0.1:$PORT is open"; else echo "❌ nothing on 127.0.0.1:$PORT"; exit 1; fi
    if [ "$OLLAMA_PORT" != 0 ]; then
      if curl -sf -m 5 "http://127.0.0.1:$OLLAMA_PORT/api/version" >/dev/null 2>&1; then echo "✅ Ollama answers on 127.0.0.1:$OLLAMA_PORT"
      else echo "⚠️  no Ollama on 127.0.0.1:$OLLAMA_PORT (old tunnel? --stop and restart; old key? re-grant it)"; fi
    fi
    ;;
  --fg)
    echo "🔌 127.0.0.1:$PORT → $TARGET:3306 (foreground, auto-reconnect)"
    while true; do "${SSH[@]}" "$TARGET" || true; echo "↻ tunnel dropped, reconnecting in 3s"; sleep 3; done
    ;;
  "")
    if port_open; then echo "✅ already open: 127.0.0.1:$PORT"; exit 0; fi
    "${SSH[@]}" -f -M -S "$SOCK" "$TARGET"
    for _ in $(seq 1 20); do port_open && break; sleep 0.25; done
    port_open && echo "✅ tunnel up: 127.0.0.1:$PORT → $TARGET:3306  (stop: $0 --stop)" \
              || { echo "❌ tunnel did not come up"; exit 1; }
    ;;
  *) sed -n '2,13p' "$0"; exit 2 ;;
esac
