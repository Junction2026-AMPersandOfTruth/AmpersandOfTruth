#!/usr/bin/env bash
# Runs ON THE VPS. Starts/updates the corpus MariaDB and (re)provisions schemas + users idempotently.
# Usually invoked by scripts/corpus-deploy.sh from a laptop; safe to re-run any time.
#   - First run generates ./.env (root + corpus_ro + corpus_rw passwords, chmod 600). Never commit it.
#   - Prints nothing secret. Read passwords on the VPS:  grep CORPUS_ /opt/junction/corpus/.env
set -euo pipefail

# Compose dir: next to this script when deployed (/opt/junction/corpus), else the repo's deploy/corpus.
HERE="$(cd "$(dirname "$0")" && pwd)"
if [ -n "${CORPUS_DIR:-}" ]; then DIR="$CORPUS_DIR"
elif [ -f "$HERE/docker-compose.yml" ]; then DIR="$HERE"
else DIR="$HERE/../deploy/corpus"; fi
cd "$DIR"

if [ ! -f .env ]; then
  umask 077
  {
    echo "MARIADB_ROOT_PASSWORD=$(openssl rand -hex 24)"
    echo "CORPUS_RO_PASSWORD=$(openssl rand -hex 24)"
    echo "CORPUS_RW_PASSWORD=$(openssl rand -hex 24)"
  } > .env
  echo "🔑 generated $DIR/.env"
fi
chmod 600 .env
set -a; . ./.env; set +a

docker compose up -d --remove-orphans

# conf.d is a bind mount → `up` won't restart on a .cnf edit. Restart when its hash changed since last run.
CONF_HASH=$(cat conf.d/*.cnf | sha256sum | cut -c1-16)
if [ -f .conf-hash ] && [ "$(cat .conf-hash)" != "$CONF_HASH" ]; then
  echo "♻️  conf.d changed → restarting MariaDB"; docker compose restart mariadb
fi
echo "$CONF_HASH" > .conf-hash

echo -n "⏳ waiting for MariaDB to be healthy"
for _ in $(seq 1 90); do
  status=$(docker inspect -f '{{.State.Health.Status}}' corpus-mariadb 2>/dev/null || echo starting)
  [ "$status" = healthy ] && break
  echo -n .; sleep 2
done
echo
[ "$status" = healthy ] || { echo "❌ not healthy ($status)"; docker compose logs --tail 50 mariadb; exit 1; }

# Passwords are hex → safe to inline in SQL. Fed via stdin so they never show up in `ps`.
docker exec -i -e MYSQL_PWD="$MARIADB_ROOT_PASSWORD" corpus-mariadb mariadb -uroot <<SQL
CREATE DATABASE IF NOT EXISTS corpus   CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS research CHARACTER SET utf8mb4;

-- corpus_ro: the app + experiment agents. Read the shared corpus; scratch tables only in \`research\`.
CREATE USER IF NOT EXISTS 'corpus_ro'@'%' IDENTIFIED BY '$CORPUS_RO_PASSWORD';
ALTER USER 'corpus_ro'@'%' IDENTIFIED BY '$CORPUS_RO_PASSWORD';
GRANT SELECT ON corpus.*   TO 'corpus_ro'@'%';
GRANT ALL    ON research.* TO 'corpus_ro'@'%';

-- corpus_rw: loaders and precompute jobs (P0.2, P0.3, P1.S1).
CREATE USER IF NOT EXISTS 'corpus_rw'@'%' IDENTIFIED BY '$CORPUS_RW_PASSWORD';
ALTER USER 'corpus_rw'@'%' IDENTIFIED BY '$CORPUS_RW_PASSWORD';
GRANT ALL ON corpus.*   TO 'corpus_rw'@'%';
GRANT ALL ON research.* TO 'corpus_rw'@'%';
FLUSH PRIVILEGES;
SQL

docker exec -e MYSQL_PWD="$MARIADB_ROOT_PASSWORD" corpus-mariadb mariadb -uroot -N -e "
  SELECT CONCAT('✅ ', VERSION(),
    ' | buffer_pool=', @@innodb_buffer_pool_size DIV 1048576, 'M',
    ' | mhnsw_cache=', @@mhnsw_max_cache_size DIV 1048576, 'M',
    ' | ef_search=', @@mhnsw_ef_search,
    ' | max_packet=', @@max_allowed_packet DIV 1048576, 'M');"
docker stats --no-stream --format '   mem: {{.MemUsage}}' corpus-mariadb
