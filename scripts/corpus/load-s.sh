#!/usr/bin/env bash
# Load Dataset S (s-2026-10: 29,460 current fi/sv/en articles, bge-m3 1024-d vectors, fi categories)
# into the corpus MariaDB using the upstream MariaDB/mariadb-vector-wikipedia-rag schema and loader (issue #13).
#
# Usage:
#   scripts/corpus/load-s.sh            # run locally (against VPS loopback if on VPS, else tunnel 127.0.0.1:3307)
#   scripts/corpus/load-s.sh --remote   # rsync loader to VPS and execute there over loopback (needs root SSH)
#
# Environment overrides:
#   CORPUS_DATA_DIR   where s-2026-10 files are downloaded (default: /opt/junction/corpus/data/s on VPS, else ./data/corpus/s)
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
  exec "${SSH[@]}" "$HOST" "/opt/junction/corpus/loader/load-s.sh $*"
fi

# Auto-detect VPS environment (/opt/junction/corpus/.env) vs laptop tunnel.
if [ -f /opt/junction/corpus/.env ]; then
  # shellcheck disable=SC1091
  set -a; . /opt/junction/corpus/.env; set +a
  : "${DB_HOST:=127.0.0.1}"
  : "${DB_PORT:=3306}"
  : "${DB_PASSWORD:=${CORPUS_RW_PASSWORD:-}}"
  : "${CORPUS_DATA_DIR:=/opt/junction/corpus/data/s}"
  : "${CORPUS_VENV:=/opt/junction/corpus/.venv}"
  : "${UPSTREAM_DIR:=/opt/junction/corpus/.upstream-rag}"
else
  : "${DB_HOST:=127.0.0.1}"
  : "${DB_PORT:=3307}"
  : "${CORPUS_DATA_DIR:=$ROOT/data/corpus/s}"
  : "${CORPUS_VENV:=$HERE/.venv}"
  : "${UPSTREAM_DIR:=$HERE/.upstream}"
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

# 1. Download and verify Dataset S (release s-2026-10 of MariaDB/mariadb-wikipedia-datasets)
DATASET_BASE="https://github.com/MariaDB/mariadb-wikipedia-datasets/releases/download/s-2026-10"
mkdir -p "$CORPUS_DATA_DIR"
echo "📥 Dataset S → $CORPUS_DATA_DIR"
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

# 2. Fetch pinned upstream schema + loader from MariaDB/mariadb-vector-wikipedia-rag (MIT)
RAG_COMMIT="9c2e729037bcf5fae9aa49bd17f087391448c7fe"
RAG_BASE="https://raw.githubusercontent.com/MariaDB/mariadb-vector-wikipedia-rag/$RAG_COMMIT"
mkdir -p "$UPSTREAM_DIR"
for f in LICENSE schema.sql common.py create_tables.py load.py; do
  if [ ! -f "$UPSTREAM_DIR/$f" ]; then
    curl -fsSL -o "$UPSTREAM_DIR/$f" "$RAG_BASE/$f"
  fi
done
ln -sfn "$CORPUS_DATA_DIR" "$UPSTREAM_DIR/data"

# 3. Prepare Python virtualenv (uses C `mariadb` connector when available, pure-Python `pymysql` fallback on laptops)
if [ ! -x "$CORPUS_VENV/bin/python3" ]; then
  python3 -m venv "$CORPUS_VENV"
fi
"$CORPUS_VENV/bin/pip" install -q pyarrow requests pymysql
if ! "$CORPUS_VENV/bin/python3" -c "import mariadb" 2>/dev/null; then
  "$CORPUS_VENV/bin/pip" install -q mariadb 2>/dev/null || true
fi

# 4. Run upstream create_tables.py + load.py --vectors
echo "🚀 Loading Dataset S into $DB_USER@$DB_HOST:$DB_PORT/$DB_NAME ..."
START_TS=$(date +%s)
(
  cd "$UPSTREAM_DIR"
  "$CORPUS_VENV/bin/python3" - <<'PY'
import os, runpy, sys

# If C `mariadb` module isn't compiled on this machine (e.g. macOS laptop without mariadb-connector-c),
# register a thin PyMySQL DB-API shim translating '?' placeholders to '%s' so upstream scripts run unchanged.
try:
    import mariadb  # noqa: F401
except ImportError:
    import types, pymysql

    class _CursorWrapper:
        def __init__(self, cur): self._cur = cur
        def execute(self, sql, params=None):
            if params is not None:
                return self._cur.execute(sql.replace("?", "%s"), params)
            return self._cur.execute(sql)
        def executemany(self, sql, seq):
            return self._cur.executemany(sql.replace("?", "%s"), seq)
        def __getattr__(self, name): return getattr(self._cur, name)

    class _ConnWrapper:
        def __init__(self, con): self._con = con
        def cursor(self): return _CursorWrapper(self._con.cursor())
        def __getattr__(self, name): return getattr(self._con, name)

    mod = types.ModuleType("mariadb")
    def _connect(**kw):
        kw = dict(kw)
        kw.setdefault("charset", "utf8mb4")
        return _ConnWrapper(pymysql.connect(**kw))
    mod.connect = _connect
    sys.modules["mariadb"] = mod

sys.argv = ["create_tables.py"]
runpy.run_path("create_tables.py", run_name="__main__")

sys.argv = ["load.py", "--vectors"]
runpy.run_path("load.py", run_name="__main__")
PY
)
ELAPSED=$(( $(date +%s) - START_TS ))

# 5. Verify row counts and nearest-neighbour sanity check
"$CORPUS_VENV/bin/python3" - <<PY
import os, pymysql

con = pymysql.connect(
    host=os.environ["DB_HOST"], port=int(os.environ["DB_PORT"]),
    user=os.environ["DB_USER"], password=os.environ["DB_PASSWORD"],
    database=os.environ["DB_NAME"], charset="utf8mb4"
)
cur = con.cursor()

expected = {"fi": 19285, "sv": 4921, "en": 5254}
for tbl in ("article", "article_vector"):
    cur.execute(f"SELECT lang, COUNT(*) FROM {tbl} GROUP BY lang")
    got = dict(cur.fetchall())
    assert got == expected, f"{tbl} count mismatch: got {got}, expected {expected}"

cur.execute("SELECT COUNT(*) FROM category_link")
cat_links = cur.fetchone()[0]
assert cat_links == 1760, f"category_link count mismatch: {cat_links} != 1760"

cur.execute("SELECT lang, COUNT(*) FROM article_category GROUP BY lang")
art_cats = dict(cur.fetchall())

# Sanity check: nearest neighbours for Talvisota (Winter War) using its own vector
cur.execute("""
    SELECT a.lang, a.page_id, a.title, v.distance
    FROM (
        SELECT lang, page_id,
               VEC_DISTANCE_COSINE(embedding, (
                   SELECT v0.embedding FROM article_vector v0
                   JOIN article a0 USING (lang, page_id)
                   WHERE a0.lang = 'fi' AND a0.title = 'Talvisota'
                   LIMIT 1
               )) AS distance
        FROM article_vector
        ORDER BY distance
        LIMIT 8
    ) AS v
    JOIN article a USING (lang, page_id)
    ORDER BY v.distance
""")
neighbours = cur.fetchall()
assert neighbours and neighbours[0][2] == "Talvisota" and neighbours[0][3] < 1e-5, f"Unexpected top neighbour: {neighbours}"

cur.execute("""
    SELECT table_name,
           ROUND(data_length/1048576, 2),
           ROUND(index_length/1048576, 2),
           ROUND((data_length+index_length)/1048576, 2)
    FROM information_schema.tables
    WHERE table_schema = %s AND table_name IN ('article', 'article_vector', 'category_link', 'article_category')
    ORDER BY table_name
""", (os.environ["DB_NAME"],))
sizes = cur.fetchall()

print()
print(f"✅ Dataset S verified in ${ELAPSED}s")
print(f"   article / article_vector: {expected} (total {sum(expected.values()):,})")
print(f"   category_link: {cat_links:,} | article_category: {art_cats}")
print("   Table sizes (MB): " + ", ".join(f"{t}={tot} (data {d}, idx {i})" for t, d, i, tot in sizes))
print("   Sanity top-8 neighbours for 'Talvisota' (fi):")
for lang, pid, title, dist in neighbours:
    print(f"     [{lang}] {title} (page_id={pid}, dist={dist:.4f})")
con.close()
PY
