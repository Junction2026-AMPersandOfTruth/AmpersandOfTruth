#!/usr/bin/env bash
# Build research.hotspot_{headline,article,month,category} in MariaDB from build.sql.
# Uses standard repo env vars: CORPUS_HOST, CORPUS_PORT, CORPUS_USER, CORPUS_PW / MYSQL_PWD, CORPUS_DB.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST="${CORPUS_HOST:-127.0.0.1}"
PORT="${CORPUS_PORT:-3307}"
USER_NAME="${CORPUS_USER:-corpus_ro}"
DB_NAME="${CORPUS_DB:-corpus}"
export MYSQL_PWD="${CORPUS_PW:-${MYSQL_PWD:-}}"

echo "[66-hotspots] Building research.hotspot_* on ${HOST}:${PORT} (user=${USER_NAME})..." >&2
SECONDS=0

mariadb --protocol=TCP --default-character-set=utf8mb4 -h "${HOST}" -P "${PORT}" -u "${USER_NAME}" "${DB_NAME}" < "${SCRIPT_DIR}/build.sql"

echo "[66-hotspots] Done in ${SECONDS}s." >&2
