#!/usr/bin/env bash
# Manage the local golden-slice MariaDB 12.3 container (aot-slice on 127.0.0.1:3308).
#
#   research/slice/slice.sh up       start container idempotently and provision corpus/research + corpus_ro
#   research/slice/slice.sh stop     stop container (keeps volume aot-slice-data)
#   research/slice/slice.sh status   show container status, port check, and table row counts/sizes
#
# Root credentials live in ~/.config/aot-slice/env (chmod 600, never printed or committed).
# User corpus_ro gets the password from $CORPUS_PW (or $MYSQL_PWD if CORPUS_PW is unset).
set -euo pipefail

IMAGE="docker.io/library/mariadb:12.3"
CONTAINER="aot-slice"
VOLUME="aot-slice-data"
HOST="127.0.0.1"
PORT="3308"
CONF_DIR="${HOME}/.config/aot-slice"
ENV_FILE="${CONF_DIR}/env"

ensure_env_file() {
  mkdir -p -m 700 "${CONF_DIR}"
  chmod 700 "${CONF_DIR}"
  if [ ! -f "${ENV_FILE}" ]; then
    umask 077
    python3 - "${ENV_FILE}" <<'PY'
import pathlib, secrets, sys
p = pathlib.Path(sys.argv[1])
p.write_text("MARIADB_ROOT_PASSWORD=" + secrets.token_hex(24) + chr(10), encoding="utf-8")
p.chmod(0o600)
PY
    echo "generated ${ENV_FILE} (mode 600)"
  else
    chmod 600 "${ENV_FILE}"
  fi
}

cmd_up() {
  if [ -z "${CORPUS_PW:-}" ] && [ -z "${MYSQL_PWD:-}" ]; then
    echo "ERROR: CORPUS_PW (or MYSQL_PWD) must be set in the environment to configure corpus_ro." >&2
    exit 1
  fi

  ensure_env_file

  if ! podman volume exists "${VOLUME}" 2>/dev/null; then
    podman volume create "${VOLUME}" >/dev/null
    echo "created volume ${VOLUME}"
  fi

  if ! podman image exists "${IMAGE}" 2>/dev/null; then
    echo "pulling ${IMAGE} ..."
    podman pull "${IMAGE}"
  fi

  if ! podman container exists "${CONTAINER}" 2>/dev/null; then
    RUN_ARGS=(
      run -d
      --name "${CONTAINER}"
      -p "${HOST}:${PORT}:3306"
      -v "${VOLUME}:/var/lib/mysql"
      --env-file "${ENV_FILE}"
      -e MARIADB_DATABASE=corpus
      -e MARIADB_AUTO_UPGRADE=1
      "${IMAGE}"
      --character-set-server=utf8mb4
      --mhnsw-ef-search=80
      --mhnsw-max-cache-size=512M
      --innodb-buffer-pool-size=1G
      --max-allowed-packet=256M
      --innodb-flush-log-at-trx-commit=2
      --skip-name-resolve=ON
    )
    podman "${RUN_ARGS[@]}" >/dev/null
    echo "created and started container ${CONTAINER} on ${HOST}:${PORT}"
  else
    running="$(podman inspect -f '{{.State.Running}}' "${CONTAINER}" 2>/dev/null || echo false)"
    if [ "${running}" != "true" ]; then
      podman start "${CONTAINER}" >/dev/null
      echo "started container ${CONTAINER} on ${HOST}:${PORT}"
    else
      echo "container ${CONTAINER} already running on ${HOST}:${PORT}"
    fi
  fi

  echo -n "waiting for MariaDB in ${CONTAINER} to be ready"
  ready=0
  for _ in $(seq 1 60); do
    if podman exec "${CONTAINER}" healthcheck.sh --connect --innodb_initialized >/dev/null 2>&1; then
      ready=1
      break
    fi
    echo -n "."
    sleep 1
  done
  echo
  if [ "${ready}" -ne 1 ]; then
    echo "ERROR: ${CONTAINER} did not become ready in time" >&2
    podman logs --tail 40 "${CONTAINER}" >&2 || true
    exit 1
  fi

  # Provision databases, root@% (port is loopback-only 127.0.0.1:3308), and corpus_ro@%.
  # Passwords never touch argv or stdout.
  python3 - "${ENV_FILE}" "${CONTAINER}" "${HOST}" "${PORT}" <<'PY'
import os, pathlib, subprocess, sys

env_file, container, host, port = sys.argv[1:5]
root_pw = ""
for line in pathlib.Path(env_file).read_text(encoding="utf-8").splitlines():
    if line.startswith("MARIADB_ROOT_PASSWORD="):
        root_pw = line.split("=", 1)[1].strip()
        break
if not root_pw:
    raise SystemExit("MARIADB_ROOT_PASSWORD missing in " + env_file)

ro_pw = os.environ.get("CORPUS_PW") or os.environ.get("MYSQL_PWD") or ""
if not ro_pw:
    raise SystemExit("CORPUS_PW / MYSQL_PWD is empty")

bs = chr(92)
def sql_quote(s: str) -> str:
    return "'" + s.replace(bs, bs + bs).replace("'", "''") + "'"

ddl = f"""
CREATE DATABASE IF NOT EXISTS corpus CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS research CHARACTER SET utf8mb4;

CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY {sql_quote(root_pw)};
ALTER USER 'root'@'%' IDENTIFIED BY {sql_quote(root_pw)};
GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION;

CREATE USER IF NOT EXISTS 'corpus_ro'@'%' IDENTIFIED BY {sql_quote(ro_pw)};
ALTER USER 'corpus_ro'@'%' IDENTIFIED BY {sql_quote(ro_pw)};
GRANT SELECT ON corpus.* TO 'corpus_ro'@'%';
GRANT ALL ON research.* TO 'corpus_ro'@'%';
FLUSH PRIVILEGES;
"""

runner = 'MYSQL_PWD="$MARIADB_ROOT_PASSWORD" mariadb --default-character-set=utf8mb4 -uroot'
p = subprocess.run(
    ["podman", "exec", "-i", container, "bash", "-c", runner],
    input=ddl,
    capture_output=True,
    text=True,
)
if p.returncode != 0:
    raise SystemExit(f"provisioning failed: {p.stderr.strip()[:400]}")

env = dict(os.environ)
env["MYSQL_PWD"] = ro_pw
chk = subprocess.run(
    ["mariadb", "--batch", "--skip-column-names", "-h", host, "-P", port, "-u", "corpus_ro", "corpus",
     "-e", "SELECT CONCAT('ready: MariaDB ', VERSION(), ' | ef_search=', @@mhnsw_ef_search, ' | mhnsw_cache=', @@mhnsw_max_cache_size DIV 1048576, 'M')"],
    capture_output=True,
    text=True,
    env=env,
)
if chk.returncode != 0:
    raise SystemExit(f"host TCP check failed: {chk.stderr.strip()[:400]}")
print(chk.stdout.strip())
PY
}

cmd_stop() {
  if ! podman container exists "${CONTAINER}" 2>/dev/null; then
    echo "container ${CONTAINER} does not exist"
    return 0
  fi
  running="$(podman inspect -f '{{.State.Running}}' "${CONTAINER}" 2>/dev/null || echo false)"
  if [ "${running}" = "true" ]; then
    podman stop "${CONTAINER}" >/dev/null
    echo "stopped ${CONTAINER}"
  else
    echo "${CONTAINER} is already stopped"
  fi
}

cmd_status() {
  if ! podman container exists "${CONTAINER}" 2>/dev/null; then
    echo "status: missing (run 'research/slice/slice.sh up')"
    exit 1
  fi
  state="$(podman inspect -f '{{.State.Status}}' "${CONTAINER}" 2>/dev/null || echo unknown)"
  echo "container: ${CONTAINER} (${state}) on ${HOST}:${PORT} (volume ${VOLUME})"
  if [ "${state}" != "running" ]; then
    exit 1
  fi
  pw="${CORPUS_PW:-${MYSQL_PWD:-}}"
  if [ -z "${pw}" ]; then
    echo "note: set CORPUS_PW to query table counts from host"
    exit 0
  fi
  MYSQL_PWD="${pw}" mariadb --table -h "${HOST}" -P "${PORT}" -u corpus_ro --default-character-set=utf8mb4 -e "
    SELECT table_schema AS db,
           table_name AS tbl,
           table_rows AS approx_rows,
           ROUND((data_length + index_length) / 1048576, 2) AS size_mb
    FROM information_schema.tables
    WHERE table_schema IN ('corpus', 'research')
    ORDER BY table_schema, table_name;
  "
}

case "${1:-}" in
  up|start)
    cmd_up
    ;;
  stop|down)
    cmd_stop
    ;;
  status)
    cmd_status
    ;;
  *)
    sed -n '2,9p' "$0" >&2
    exit 2
    ;;
esac
