#!/usr/bin/env bash
# Load Dataset M (m-2026-10: 862,076 Wikipedia edit-history revisions across 19,742 fi articles)
# into `revision` (narrow metadata) + `revision_text` (`MEDIUMTEXT COMPRESSED`) in the corpus DB (issue #14).
#
# Usage:
#   scripts/corpus/load-m.sh                           # full load (862,076 rows)
#   scripts/corpus/load-m.sh --articles 200            # subset: top 200 most-edited articles
#   scripts/corpus/load-m.sh --pages golden_pages.txt  # subset: specific page_ids (combinable with --articles N)
#   scripts/corpus/load-m.sh --remote [args...]        # rsync loader to VPS and execute there over loopback
#
# Environment overrides:
#   CORPUS_DATA_DIR   where m-2026-10 files are downloaded (default: /opt/junction/corpus/data/m on VPS, else ./data/corpus/m)
#   CORPUS_VENV       Python virtualenv path
#   DB_HOST, DB_PORT, DB_USER, DB_PASSWORD, DB_NAME
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

if [ "${1:-}" = "--remote" ]; then
  shift
  HOST="${CORPUS_SSH_ROOT:-root@srv2158939.hstgr.cloud}"
  SSH=(ssh -o IdentitiesOnly=yes)
  [ -n "${CORPUS_SSH_KEY:-}" ] && SSH+=(-i "$CORPUS_SSH_KEY")
  [ -z "${CORPUS_SSH_KEY:-}" ] && [ -f "$ROOT/.ssh/ai_vps_access" ] && SSH+=(-i "$ROOT/.ssh/ai_vps_access")
  "${SSH[@]}" "$HOST" 'mkdir -p /opt/junction/corpus/loader'
  rsync -az -e "${SSH[*]}" "$HERE/" "$HOST:/opt/junction/corpus/loader/"
  exec "${SSH[@]}" "$HOST" "/opt/junction/corpus/loader/load-m.sh $*"
fi

if [ -f /opt/junction/corpus/.env ]; then
  # shellcheck disable=SC1091
  set -a; . /opt/junction/corpus/.env; set +a
  : "${DB_HOST:=127.0.0.1}"
  : "${DB_PORT:=3306}"
  : "${DB_PASSWORD:=${CORPUS_RW_PASSWORD:-}}"
  : "${CORPUS_DATA_DIR:=/opt/junction/corpus/data/m}"
  : "${CORPUS_VENV:=/opt/junction/corpus/.venv}"
else
  : "${DB_HOST:=127.0.0.1}"
  : "${DB_PORT:=3307}"
  : "${CORPUS_DATA_DIR:=$ROOT/data/corpus/m}"
  : "${CORPUS_VENV:=$HERE/.venv}"
fi
: "${DB_USER:=corpus_rw}"
: "${DB_NAME:=corpus}"
: "${DB_PASSWORD:?Set DB_PASSWORD (corpus_rw password from /opt/junction/corpus/.env on the VPS)}"

export DB_HOST DB_PORT DB_USER DB_PASSWORD DB_NAME

check_sha256() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c "$@"
  else
    shasum -a 256 -c "$@"
  fi
}

verify_single_sha256() {
  local sum="$1" file="$2"
  if command -v sha256sum >/dev/null 2>&1; then
    echo "$sum  $file" | sha256sum -c --status - 2>/dev/null
  else
    echo "$sum  $file" | shasum -a 256 -c --status - 2>/dev/null
  fi
}

# 1. Download and verify Dataset M (release m-2026-10 of MariaDB/mariadb-wikipedia-datasets)
DATASET_BASE="https://github.com/MariaDB/mariadb-wikipedia-datasets/releases/download/m-2026-10"
mkdir -p "$CORPUS_DATA_DIR"
echo "📥 Dataset M → $CORPUS_DATA_DIR"
(
  cd "$CORPUS_DATA_DIR"
  curl -fsSLO "$DATASET_BASE/SHA256SUMS"
  while read -r sum file; do
    [ -z "$sum" ] && continue
    if [ -f "$file" ] && verify_single_sha256 "$sum" "$file"; then
      continue
    fi
    echo "   downloading $file ..."
    curl -fsSL -o "$file.part" "$DATASET_BASE/$file" && mv "$file.part" "$file"
  done < SHA256SUMS
  check_sha256 SHA256SUMS
)

# 2. Prepare Python virtualenv
if [ ! -x "$CORPUS_VENV/bin/python3" ]; then
  python3 -m venv "$CORPUS_VENV"
fi
"$CORPUS_VENV/bin/pip" install -q pyarrow pymysql

# 3. Stream Parquet files into revision + revision_text
exec "$CORPUS_VENV/bin/python3" "$HERE/load_m.py" --data-dir "$CORPUS_DATA_DIR" "$@"
