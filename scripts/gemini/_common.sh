# Shared helpers for scripts/gemini/*. Source, don't execute.
set -euo pipefail

GEMINI_CLI_VERSION="0.68.0"

# Main checkout, even when called from inside a worker worktree.
MAIN_REPO="$(cd "$(git rev-parse --git-common-dir)/.." && pwd)"
WT_ROOT="${GEMINI_WT_ROOT:-$(dirname "$MAIN_REPO")/$(basename "$MAIN_REPO").worktrees}"
TASKS_DIR="$MAIN_REPO/.tasks"

die() { echo "❌ $*" >&2; exit 1; }

require_id() {
  [[ "${1:-}" =~ ^[a-z0-9][a-z0-9-]*$ ]] || die "task id must be kebab-case, got '${1:-}'"
}

task_paths() {
  ID="$1"
  TASK="$TASKS_DIR/$ID"
  WT="$WT_ROOT/$ID"
  BR="gemini/$ID"
}

# Resolve the gemini executable: $GEMINI_BIN > gemini on PATH > pinned npx.
gemini_cmd() {
  if [ -n "${GEMINI_BIN:-}" ]; then GEMINI=("$GEMINI_BIN")
  elif command -v gemini >/dev/null 2>&1; then GEMINI=(gemini)
  else GEMINI=(npx -y "@google/gemini-cli@$GEMINI_CLI_VERSION")
  fi
}

# Load GEMINI_API_KEY from ~/.gemini/.env if not already exported. Never printed.
load_api_key() {
  [ -n "${GEMINI_BIN:-}" ] && return 0   # stub binary in tests needs no key
  if [ -z "${GEMINI_API_KEY:-}" ] && [ -f "$HOME/.gemini/.env" ]; then
    GEMINI_API_KEY="$(grep -E '^GEMINI_API_KEY=' "$HOME/.gemini/.env" | tail -1 | cut -d= -f2- | tr -d "\"' ")"
    export GEMINI_API_KEY
  fi
  [ -n "${GEMINI_API_KEY:-}" ] || die "GEMINI_API_KEY not set. Put 'GEMINI_API_KEY=...' in ~/.gemini/.env (outside the repo)."
}
