#!/usr/bin/env python3
"""Build or incrementally refresh the local golden-slice MariaDB (127.0.0.1:3308) from the VPS tunnel (127.0.0.1:3307).

Usage:
    research/slice/slice.sh up
    python3 research/slice/export.py [--pages FILE] [--batch-size 200] [--pause 0.05]

Default page set (when --pages is omitted) is the union of:
  (a) research/eval/golden.json articles (if the file exists),
  (b) CORE demo articles in research/35-s8-signals/candidates.py,
  (c) fi top-20 of R1 (research/8-r1-vector/variant.py) for every query in research/lib/probe_queries.json.

What is copied:
  - Exact SHOW CREATE TABLE DDL for corpus.{article,article_vector,article_category,category_link,revision,revision_text}
    plus any existing research.{s1_*,s4_*,s5_*} tables.
  - corpus.article_vector WHOLE (~29k rows, VEC_ToText out -> VEC_FromText in) so retrieval ranks against full corpus.
  - corpus.article WHOLE (~29k rows), with `text` populated ONLY for slice pages + their en/sv wikidata siblings
    (empty string '' elsewhere).
  - corpus.article_category for slice pages + their en/sv siblings; corpus.category_link whole.
  - corpus.revision and corpus.revision_text for all fi slice pages (revision_text by page in PK order, <= 200 rows
    per query, one query at a time, short pause between queries, resumable).
  - research.{s1_*,s4_*,s5_*} rows for slice pages (and corpus-wide tables without page_id such as s1_user_stats).
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "research" / "lib"))
import aot  # noqa: E402

BS = chr(92)
NL = chr(10)
CR = chr(13)
TAB = chr(9)
NUL = chr(0)

CORPUS_TABLES = (
    "article",
    "article_vector",
    "article_category",
    "category_link",
    "revision",
    "revision_text",
)

SLICE_HOST = "127.0.0.1"
SLICE_PORT = "3308"
ENV_FILE = pathlib.Path.home() / ".config" / "aot-slice" / "env"


def log(msg: str) -> None:
    print(msg, file=sys.stderr, flush=True)


def sql_quote(s: str | None) -> str:
    if s is None:
        return "NULL"
    out = (
        s.replace(BS, BS + BS)
        .replace("'", "''")
        .replace(NUL, BS + "0")
        .replace(NL, BS + "n")
        .replace(CR, BS + "r")
    )
    return "'" + out + "'"


def unescape_cell(v: str, nullable: bool = True) -> str | None:
    if nullable and v == "NULL":
        return None
    if BS not in v:
        return v
    return BS.join(
        part.replace(BS + "n", NL).replace(BS + "t", TAB).replace(BS + "0", NUL)
        for part in v.split(BS + BS)
    )


def read_slice_root_pw() -> str:
    if not ENV_FILE.exists():
        raise SystemExit(f"Missing {ENV_FILE}; run 'research/slice/slice.sh up' first.")
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        if line.startswith("MARIADB_ROOT_PASSWORD="):
            pw = line.split("=", 1)[1].strip()
            if pw:
                return pw
    raise SystemExit(f"MARIADB_ROOT_PASSWORD not found in {ENV_FILE}; run 'research/slice/slice.sh up'.")


class DbEndpoint:
    def __init__(self, host: str, port: str, user: str, password: str, compress: bool = False):
        self.host = host
        self.port = port
        self.user = user
        self.password = password
        self.compress = compress

    def _env(self) -> dict[str, str]:
        env = dict(os.environ)
        env["MYSQL_PWD"] = self.password
        env.pop("CORPUS_SLICE", None)
        return env

    def query(self, sql_text: str, db: str = "corpus", timeout: int = 120, nullable_all: bool = True) -> list[dict]:
        cmd = ["mariadb", "--batch", "--default-character-set=utf8mb4"]
        if self.compress:
            cmd.append("--compress")
        cmd += ["-h", self.host, "-P", self.port, "-u", self.user, db]
        p = subprocess.run(cmd, input=sql_text, capture_output=True, text=True, env=self._env(), timeout=timeout)
        if p.returncode != 0:
            raise RuntimeError(f"mariadb ({self.host}:{self.port}/{db}) failed: {p.stderr.strip()[:500]}")
        lines = p.stdout.split(NL)
        rows: list[dict] = []
        header: list[str] | None = None
        for line in lines:
            if line == "":
                continue
            cells = line.split(TAB)
            if header is None or (len(cells) == len(header) and cells == header):
                header = cells
                rows = []
                continue
            rows.append({h: unescape_cell(c, nullable=nullable_all) for h, c in zip(header, cells)})
        return rows

    def query_raw_lines(self, sql_text: str, db: str = "corpus", timeout: int = 120) -> list[str]:
        cmd = ["mariadb", "--batch", "--skip-column-names", "--default-character-set=utf8mb4"]
        if self.compress:
            cmd.append("--compress")
        cmd += ["-h", self.host, "-P", self.port, "-u", self.user, db]
        p = subprocess.run(cmd, input=sql_text, capture_output=True, text=True, env=self._env(), timeout=timeout)
        if p.returncode != 0:
            raise RuntimeError(f"mariadb ({self.host}:{self.port}/{db}) failed: {p.stderr.strip()[:500]}")
        return [ln for ln in p.stdout.split(NL) if ln != ""]

    def execute(self, sql_text: str, db: str = "corpus", timeout: int = 300) -> None:
        cmd = [
            "mariadb",
            "--default-character-set=utf8mb4",
            "--max-allowed-packet=268435456",
            "-h", self.host,
            "-P", self.port,
            "-u", self.user,
            db,
        ]
        p = subprocess.run(cmd, input=sql_text, capture_output=True, text=True, env=self._env(), timeout=timeout)
        if p.returncode != 0:
            raise RuntimeError(f"mariadb exec ({self.host}:{self.port}/{db}) failed: {p.stderr.strip()[:500]}")


def dump_and_load(
    src: DbEndpoint,
    dst: DbEndpoint,
    schema: str,
    table: str,
    where: str | None = None,
    replace: bool = False,
    timeout: int = 300,
) -> None:
    dump_cmd = [
        "mariadb-dump",
        "--compress",
        "--default-character-set=utf8mb4",
        "--no-create-info",
        "--skip-triggers",
        "--skip-lock-tables",
        "--compact",
        "--quick",
        "--net-buffer-length=16777216",
        "--max-allowed-packet=268435456",
        "--replace" if replace else "--insert-ignore",
        "-h", src.host,
        "-P", src.port,
        "-u", src.user,
        schema,
        table,
    ]
    if where:
        dump_cmd.append(f"--where={where}")

    load_cmd = [
        "mariadb",
        "--default-character-set=utf8mb4",
        "--max-allowed-packet=268435456",
        "-h", dst.host,
        "-P", dst.port,
        "-u", dst.user,
        schema,
    ]

    p_dump = subprocess.Popen(dump_cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=src._env())
    assert p_dump.stdout is not None
    p_load = subprocess.Popen(load_cmd, stdin=p_dump.stdout, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=dst._env())
    p_dump.stdout.close()

    _, load_err = p_load.communicate(timeout=timeout)
    _, dump_err = p_dump.communicate(timeout=timeout)
    if p_dump.returncode != 0:
        raise RuntimeError(f"mariadb-dump {schema}.{table} failed: {dump_err.decode('utf-8', 'replace').strip()[:500]}")
    if p_load.returncode != 0:
        raise RuntimeError(f"mariadb load {schema}.{table} failed: {load_err.decode('utf-8', 'replace').strip()[:500]}")


def parse_pages_file(path: pathlib.Path) -> set[int]:
    pages: set[int] = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        raw = line.split("#", 1)[0].strip()
        if not raw:
            continue
        first = raw.replace(",", " ").split()[0].strip()
        if first.lower() == "page_id":
            continue
        pages.add(int(first))
    return pages


def load_core_pages() -> set[int]:
    cand_path = ROOT / "research" / "35-s8-signals" / "candidates.py"
    spec = importlib.util.spec_from_file_location("_s8_candidates", cand_path)
    assert spec and spec.loader
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return {int(x) for x in mod.CORE}


def load_golden_pages() -> set[int]:
    golden_path = ROOT / "research" / "eval" / "golden.json"
    if not golden_path.exists():
        return set()
    data = json.loads(golden_path.read_text(encoding="utf-8"))
    out: set[int] = set()
    for topic in data.get("topics", []):
        for art in topic.get("articles", []):
            out.add(int(art["page_id"]))
        for item in topic.get("items", []):
            out.add(int(item["page_id"]))
            for op in item.get("other_page_ids") or []:
                out.add(int(op))
    return out


def load_probe_r1_pages(src: DbEndpoint) -> set[int]:
    probes_path = ROOT / "research" / "lib" / "probe_queries.json"
    r1_path = ROOT / "research" / "8-r1-vector" / "variant.py"
    probes = json.loads(probes_path.read_text(encoding="utf-8"))["queries"]
    env = dict(os.environ)
    env.pop("CORPUS_SLICE", None)
    env["CORPUS_HOST"] = src.host
    env["CORPUS_PORT"] = src.port
    env["CORPUS_USER"] = src.user
    env["CORPUS_PW"] = src.password

    pages: set[int] = set()
    for q in probes:
        req = {"contract": 1, "kind": "retrieval", "query_id": q["id"], "text": q["text"], "k": 20}
        p = subprocess.run(
            [sys.executable, str(r1_path)],
            input=json.dumps(req),
            capture_output=True,
            text=True,
            env=env,
            timeout=90,
        )
        if p.returncode != 0:
            raise RuntimeError(f"R1 failed on probe {q['id']}: {p.stderr.strip()[:400]}")
        resp = json.loads(p.stdout)
        for hit in resp.get("results", [])[:20]:
            pages.add(int(hit["page_id"]))
    return pages


def resolve_page_set(args: argparse.Namespace, src: DbEndpoint) -> set[int]:
    if args.pages:
        p = pathlib.Path(args.pages)
        pages = parse_pages_file(p)
        log(f"[pages] loaded {len(pages)} fi page_ids from {p}")
        return pages

    golden = load_golden_pages()
    core = load_core_pages()
    probe_top = load_probe_r1_pages(src)
    union = golden | core | probe_top
    log(
        f"[pages] default union = {len(union)} fi pages "
        f"(golden.json={len(golden)}, s8 CORE={len(core)}, R1 probe top-20={len(probe_top)})"
    )
    return union


def copy_ddl(src: DbEndpoint, dst: DbEndpoint) -> list[str]:
    for tbl in CORPUS_TABLES:
        row = src.query(f"SHOW CREATE TABLE corpus.`{tbl}`", db="corpus")[0]
        ddl = row["Create Table"]
        assert ddl.startswith("CREATE TABLE "), f"unexpected DDL for corpus.{tbl}"
        ddl_if = "CREATE TABLE IF NOT EXISTS " + ddl[len("CREATE TABLE "):] + ";"
        dst.execute(ddl_if, db="corpus")

    res_rows = src.query("SHOW TABLES IN research", db="research")
    research_tables: list[str] = []
    for r in res_rows:
        tname = next(iter(r.values()))
        if tname and tname.startswith(("s1_", "s4_", "s5_")) and not tname.startswith(("s1__", "s4__", "s5__")):
            research_tables.append(tname)
    research_tables.sort()

    copied_research: list[str] = []
    for tbl in research_tables:
        try:
            row = src.query(f"SHOW CREATE TABLE research.`{tbl}`", db="research")[0]
        except RuntimeError as ex:
            log(f"[ddl] skipping vanished research.{tbl}: {ex}")
            continue
        ddl = row["Create Table"]
        ddl_if = "CREATE TABLE IF NOT EXISTS " + ddl[len("CREATE TABLE "):] + ";"
        dst.execute(ddl_if, db="research")
        copied_research.append(tbl)

    log(f"[ddl] synced {len(CORPUS_TABLES)} corpus tables and {len(copied_research)} research tables: {', '.join(copied_research)}")
    return copied_research


def resolve_siblings(src: DbEndpoint, fi_pages: set[int]) -> set[tuple[str, int]]:
    lang_pages: set[tuple[str, int]] = {("fi", pid) for pid in fi_pages}
    if not fi_pages:
        return lang_pages
    wrows = src.query(
        f"SELECT DISTINCT wikidata_id FROM corpus.article "
        f"WHERE lang='fi' AND page_id IN ({aot.ints(sorted(fi_pages))}) "
        f"AND wikidata_id IS NOT NULL AND wikidata_id <> ''"
    )
    qids = sorted({r["wikidata_id"] for r in wrows if r["wikidata_id"]})
    if qids:
        for i in range(0, len(qids), 200):
            chunk = qids[i:i + 200]
            in_sql = ",".join(sql_quote(q) for q in chunk)
            srows = src.query(f"SELECT lang, page_id FROM corpus.article WHERE wikidata_id IN ({in_sql})")
            for r in srows:
                lang_pages.add((r["lang"], int(r["page_id"])))
    by_lang: dict[str, int] = {}
    for lg, _ in lang_pages:
        by_lang[lg] = by_lang.get(lg, 0) + 1
    log(f"[siblings] slice (lang, page_id) entries = {len(lang_pages)} ({by_lang})")
    return lang_pages


def sync_category_link(src: DbEndpoint, dst: DbEndpoint) -> None:
    t0 = time.monotonic()
    vps_n = int(src.query("SELECT COUNT(*) AS n FROM corpus.category_link")[0]["n"])
    loc_n = int(dst.query("SELECT COUNT(*) AS n FROM corpus.category_link")[0]["n"])
    if loc_n >= vps_n:
        log(f"[category_link] up to date ({loc_n} rows)")
        return
    dump_and_load(src, dst, "corpus", "category_link")
    loc_after = int(dst.query("SELECT COUNT(*) AS n FROM corpus.category_link")[0]["n"])
    log(f"[category_link] synced {loc_after} rows in {time.monotonic() - t0:.1f}s")


def sync_article_vector(src: DbEndpoint, dst: DbEndpoint) -> None:
    t0 = time.monotonic()
    vps_n = int(src.query("SELECT COUNT(*) AS n FROM corpus.article_vector")[0]["n"])
    loc_n = int(dst.query("SELECT COUNT(*) AS n FROM corpus.article_vector")[0]["n"])
    if loc_n >= vps_n:
        log(f"[article_vector] up to date ({loc_n} vectors)")
        return

    have = {
        (r["lang"], int(r["page_id"]))
        for r in dst.query("SELECT lang, page_id FROM corpus.article_vector")
    }
    all_keys = [
        (r["lang"], int(r["page_id"]))
        for r in src.query("SELECT lang, page_id FROM corpus.article_vector ORDER BY lang, page_id")
    ]
    todo = [k for k in all_keys if k not in have]
    log(f"[article_vector] copying {len(todo)}/{len(all_keys)} vectors via VEC_ToText -> VEC_FromText ...")

    chunk_size = 500
    copied = 0
    for i in range(0, len(todo), chunk_size):
        chunk = todo[i:i + chunk_size]
        pairs_sql = ",".join(f"({sql_quote(lg)},{pid})" for lg, pid in chunk)
        lines = src.query_raw_lines(
            f"SELECT lang, page_id, model, truncated, VEC_ToText(embedding) "
            f"FROM corpus.article_vector WHERE (lang, page_id) IN ({pairs_sql}) ORDER BY lang, page_id",
            timeout=120,
        )
        values: list[str] = []
        for ln in lines:
            lg_s, pid_s, model_s, trunc_s, vec_s = ln.split(TAB, 4)
            model_u = unescape_cell(model_s, nullable=False) or ""
            vec_clean = vec_s.strip()
            if not (vec_clean.startswith("[") and vec_clean.endswith("]")):
                raise RuntimeError(f"Unexpected VEC_ToText payload for ({lg_s}, {pid_s}): {vec_clean[:60]!r}")
            values.append(
                f"({sql_quote(lg_s)},{int(pid_s)},{sql_quote(model_u)},{int(trunc_s)},VEC_FromText({sql_quote(vec_clean)}))"
            )
        if values:
            stmt = (
                "INSERT IGNORE INTO corpus.article_vector (lang, page_id, model, truncated, embedding) VALUES "
                + ",".join(values)
                + ";"
            )
            dst.execute(stmt, db="corpus", timeout=180)
            copied += len(values)
        if (i // chunk_size + 1) % 10 == 0 or copied == len(todo):
            log(f"[article_vector]   {copied}/{len(todo)} vectors ({time.monotonic() - t0:.1f}s)")

    loc_after = int(dst.query("SELECT COUNT(*) AS n FROM corpus.article_vector")[0]["n"])
    log(f"[article_vector] ready: {loc_after} vectors in {time.monotonic() - t0:.1f}s")


def sync_article(src: DbEndpoint, dst: DbEndpoint, slice_lang_pages: set[tuple[str, int]]) -> None:
    t0 = time.monotonic()
    vps_n = int(src.query("SELECT COUNT(*) AS n FROM corpus.article")[0]["n"])
    loc_n = int(dst.query("SELECT COUNT(*) AS n FROM corpus.article")[0]["n"])

    if loc_n < vps_n:
        rows = src.query(
            "SELECT lang, page_id, rev_id, wikidata_id, title, url, date_modified "
            "FROM corpus.article ORDER BY lang, page_id"
        )
        batch_sz = 2000
        for i in range(0, len(rows), batch_sz):
            chunk = rows[i:i + batch_sz]
            vals = [
                f"({sql_quote(r['lang'])},{int(r['page_id'])},{int(r['rev_id'])},"
                f"{sql_quote(r['wikidata_id'])},{sql_quote(r['title'])},{sql_quote(r['url'])},"
                f"{sql_quote(r['date_modified'])},'')"
                for r in chunk
            ]
            stmt = (
                "INSERT IGNORE INTO corpus.article "
                "(lang, page_id, rev_id, wikidata_id, title, url, date_modified, text) VALUES "
                + ",".join(vals)
                + ";"
            )
            dst.execute(stmt, db="corpus")
        log(f"[article] metadata synced for all {len(rows)} rows (non-slice text='')")

    # Populate full `text` for slice pages + en/sv siblings where missing locally.
    sorted_lp = sorted(slice_lang_pages)
    need_text: list[tuple[str, int]] = []
    for i in range(0, len(sorted_lp), 300):
        chunk = sorted_lp[i:i + 300]
        pairs_sql = ",".join(f"({sql_quote(lg)},{pid})" for lg, pid in chunk)
        vps_nonempty = {
            (r["lang"], int(r["page_id"]))
            for r in src.query(
                f"SELECT lang, page_id FROM corpus.article WHERE (lang, page_id) IN ({pairs_sql}) AND CHAR_LENGTH(text) > 0"
            )
        }
        loc_nonempty = {
            (r["lang"], int(r["page_id"]))
            for r in dst.query(
                f"SELECT lang, page_id FROM corpus.article WHERE (lang, page_id) IN ({pairs_sql}) AND CHAR_LENGTH(text) > 0"
            )
        }
        need_text.extend(sorted(vps_nonempty - loc_nonempty))

    if not need_text:
        log(f"[article] slice texts already present ({len(sorted_lp)} slice+sibling rows checked)")
        return

    for i in range(0, len(need_text), 100):
        chunk = need_text[i:i + 100]
        pairs_sql = ",".join(f"({sql_quote(lg)},{pid})" for lg, pid in chunk)
        dump_and_load(src, dst, "corpus", "article", where=f"(lang, page_id) IN ({pairs_sql})", replace=True)

    log(f"[article] populated text for {len(need_text)} slice+sibling articles in {time.monotonic() - t0:.1f}s")


def sync_article_category(src: DbEndpoint, dst: DbEndpoint, slice_lang_pages: set[tuple[str, int]]) -> None:
    if not slice_lang_pages:
        return
    t0 = time.monotonic()
    sorted_lp = sorted(slice_lang_pages)
    missing_lp: list[tuple[str, int]] = []
    for i in range(0, len(sorted_lp), 300):
        chunk = sorted_lp[i:i + 300]
        pairs_sql = ",".join(f"({sql_quote(lg)},{pid})" for lg, pid in chunk)
        vps_c = {
            (r["lang"], int(r["page_id"])): int(r["n"])
            for r in src.query(
                f"SELECT lang, page_id, COUNT(*) AS n FROM corpus.article_category "
                f"WHERE (lang, page_id) IN ({pairs_sql}) GROUP BY lang, page_id"
            )
        }
        loc_c = {
            (r["lang"], int(r["page_id"])): int(r["n"])
            for r in dst.query(
                f"SELECT lang, page_id, COUNT(*) AS n FROM corpus.article_category "
                f"WHERE (lang, page_id) IN ({pairs_sql}) GROUP BY lang, page_id"
            )
        }
        for k, cnt in vps_c.items():
            if loc_c.get(k, 0) < cnt:
                missing_lp.append(k)

    if not missing_lp:
        tot = int(dst.query("SELECT COUNT(*) AS n FROM corpus.article_category")[0]["n"])
        log(f"[article_category] up to date ({tot} rows)")
        return

    for i in range(0, len(missing_lp), 200):
        chunk = missing_lp[i:i + 200]
        pairs_sql = ",".join(f"({sql_quote(lg)},{pid})" for lg, pid in chunk)
        dump_and_load(src, dst, "corpus", "article_category", where=f"(lang, page_id) IN ({pairs_sql})")

    tot = int(dst.query("SELECT COUNT(*) AS n FROM corpus.article_category")[0]["n"])
    log(f"[article_category] synced {tot} rows ({len(missing_lp)} pages updated) in {time.monotonic() - t0:.1f}s")


def sync_revision(src: DbEndpoint, dst: DbEndpoint, fi_pages: set[int]) -> dict[int, int]:
    if not fi_pages:
        return {}
    t0 = time.monotonic()
    pages_sql = aot.ints(sorted(fi_pages))
    vps_counts = {
        int(r["page_id"]): int(r["n"])
        for r in src.query(f"SELECT page_id, COUNT(*) AS n FROM corpus.revision WHERE page_id IN ({pages_sql}) GROUP BY page_id")
    }
    loc_counts = {
        int(r["page_id"]): int(r["n"])
        for r in dst.query(f"SELECT page_id, COUNT(*) AS n FROM corpus.revision WHERE page_id IN ({pages_sql}) GROUP BY page_id")
    }
    todo_pages = sorted(pid for pid, n in vps_counts.items() if loc_counts.get(pid, 0) < n)
    if not todo_pages:
        log(f"[revision] up to date ({sum(vps_counts.values())} rows across {len(vps_counts)} pages)")
        return vps_counts

    for i in range(0, len(todo_pages), 80):
        chunk = todo_pages[i:i + 80]
        dump_and_load(src, dst, "corpus", "revision", where=f"page_id IN ({aot.ints(chunk)})")

    tot = int(dst.query(f"SELECT COUNT(*) AS n FROM corpus.revision WHERE page_id IN ({pages_sql})")[0]["n"])
    log(f"[revision] synced {tot} rows ({len(todo_pages)} pages updated) in {time.monotonic() - t0:.1f}s")
    return vps_counts


def _insert_rev_text_lines(dst: DbEndpoint, lines: list[str]) -> tuple[int, int, int]:
    """Insert raw batch lines (page_id TAB rev_id TAB escaped_text) into local revision_text.

    Returns (row_count, max_rev_id, utf8_bytes).
    """
    if not lines:
        return 0, 0, 0
    vals: list[str] = []
    max_rev = 0
    raw_bytes = 0
    for ln in lines:
        p_s, r_s, txt_esc = ln.split(TAB, 2)
        pid = int(p_s)
        rid = int(r_s)
        if rid > max_rev:
            max_rev = rid
        txt = unescape_cell(txt_esc, nullable=False) or ""
        raw_bytes += len(txt.encode("utf-8"))
        vals.append(f"({pid},{rid},{sql_quote(txt)})")
    stmt = "INSERT IGNORE INTO corpus.revision_text (page_id, rev_id, text) VALUES " + ",".join(vals) + ";"
    dst.execute(stmt, db="corpus", timeout=180)
    return len(vals), max_rev, raw_bytes


def sync_revision_text(
    src: DbEndpoint,
    dst: DbEndpoint,
    fi_pages: set[int],
    expected_counts: dict[int, int],
    batch_size: int,
    pause_s: float,
) -> dict[str, float | int]:
    t0 = time.monotonic()
    sorted_pages = sorted(fi_pages)
    if not sorted_pages:
        return {"copied_rows": 0, "skipped_pages": 0, "queries": 0, "seconds": 0.0, "raw_mb": 0.0}

    pages_sql = aot.ints(sorted_pages)
    loc_stats = {
        int(r["page_id"]): (int(r["n"]), int(r["max_rev"]))
        for r in dst.query(
            f"SELECT page_id, COUNT(*) AS n, COALESCE(MAX(rev_id), 0) AS max_rev "
            f"FROM corpus.revision_text WHERE page_id IN ({pages_sql}) GROUP BY page_id"
        )
    }

    skipped_pages = 0
    copied_pages = 0
    copied_rows = 0
    queries = 0
    raw_bytes_total = 0
    todo_pages = [pid for pid in sorted_pages if loc_stats.get(pid, (0, 0))[0] < expected_counts.get(pid, 0)]
    skipped_pages = len(sorted_pages) - len(todo_pages)

    if not todo_pages:
        tot_rows = sum(expected_counts.get(pid, 0) for pid in sorted_pages)
        log(f"[revision_text] all {len(sorted_pages)} pages ({tot_rows} rows) already complete; nothing to fetch")
        return {
            "copied_rows": 0,
            "skipped_pages": skipped_pages,
            "queries": 0,
            "seconds": round(time.monotonic() - t0, 2),
            "raw_mb": 0.0,
        }

    todo_expected_rows = sum(expected_counts[pid] - loc_stats.get(pid, (0, 0))[0] for pid in todo_pages)
    log(
        f"[revision_text] {len(todo_pages)}/{len(sorted_pages)} pages need text "
        f"(~{todo_expected_rows} rows, batch_size={batch_size}, pause={pause_s}s; {skipped_pages} pages already complete)"
    )

    last_report = time.monotonic()
    for idx, pid in enumerate(todo_pages, 1):
        exp = expected_counts.get(pid, 0)
        have, cursor = loc_stats.get(pid, (0, 0))

        while True:
            if queries > 0 and pause_s > 0:
                time.sleep(pause_s)
            lines = src.query_raw_lines(
                f"SELECT page_id, rev_id, text FROM corpus.revision_text "
                f"WHERE page_id = {pid} AND rev_id > {cursor} "
                f"ORDER BY page_id, rev_id LIMIT {batch_size}",
                timeout=90,
            )
            queries += 1
            if not lines:
                break
            n_ins, cursor, b_ins = _insert_rev_text_lines(dst, lines)
            copied_rows += n_ins
            raw_bytes_total += b_ins
            now_inner = time.monotonic()
            if now_inner - last_report >= 15.0:
                elapsed = now_inner - t0
                mb = raw_bytes_total / 1048576
                log(
                    f"[revision_text]   page {idx}/{len(todo_pages)} (id={pid}) | "
                    f"rows +{copied_rows}/{todo_expected_rows} | {mb:.1f} MB raw | "
                    f"{queries} queries | {elapsed:.1f}s"
                )
                last_report = now_inner
            if len(lines) < batch_size:
                break

        # Verify completeness for this page; fill any gap below initial max_rev if an earlier run was interrupted weirdly.
        now_cnt = int(dst.query(f"SELECT COUNT(*) AS n FROM corpus.revision_text WHERE page_id = {pid}")[0]["n"])
        if now_cnt < exp:
            missing_revs = [
                int(r["rev_id"])
                for r in dst.query(
                    f"SELECT r.rev_id FROM corpus.revision r "
                    f"LEFT JOIN corpus.revision_text t USING (page_id, rev_id) "
                    f"WHERE r.page_id = {pid} AND t.rev_id IS NULL ORDER BY r.rev_id"
                )
            ]
            for j in range(0, len(missing_revs), batch_size):
                if pause_s > 0:
                    time.sleep(pause_s)
                rchunk = missing_revs[j:j + batch_size]
                lines = src.query_raw_lines(
                    f"SELECT page_id, rev_id, text FROM corpus.revision_text "
                    f"WHERE page_id = {pid} AND rev_id IN ({aot.ints(rchunk)}) "
                    f"ORDER BY page_id, rev_id",
                    timeout=90,
                )
                queries += 1
                n_ins, _, b_ins = _insert_rev_text_lines(dst, lines)
                copied_rows += n_ins
                raw_bytes_total += b_ins

        copied_pages += 1
        now = time.monotonic()
        if now - last_report >= 10.0 or idx == len(todo_pages):
            elapsed = now - t0
            mb = raw_bytes_total / 1048576
            log(
                f"[revision_text]   pages {idx}/{len(todo_pages)} | "
                f"rows +{copied_rows}/{todo_expected_rows} | {mb:.1f} MB raw | "
                f"{queries} queries | {elapsed:.1f}s"
            )
            last_report = now

    elapsed = time.monotonic() - t0
    raw_mb = raw_bytes_total / 1048576
    log(
        f"[revision_text] done: +{copied_rows} rows across {copied_pages} pages "
        f"({skipped_pages} skipped), {raw_mb:.1f} MB raw in {elapsed:.1f}s ({queries} queries)"
    )
    return {
        "copied_rows": copied_rows,
        "skipped_pages": skipped_pages,
        "queries": queries,
        "seconds": round(elapsed, 2),
        "raw_mb": round(raw_mb, 2),
    }


def sync_research_tables(src: DbEndpoint, dst: DbEndpoint, tables: list[str], fi_pages: set[int]) -> None:
    if not tables:
        return
    pages_sql = aot.ints(sorted(fi_pages))
    for tbl in tables:
        t0 = time.monotonic()
        cols = {r["Field"] for r in dst.query(f"SHOW COLUMNS FROM research.`{tbl}`", db="research")}
        try:
            if "page_id" in cols:
                if not fi_pages:
                    continue
                vps_c = {
                    int(r["page_id"]): int(r["n"])
                    for r in src.query(
                        f"SELECT page_id, COUNT(*) AS n FROM research.`{tbl}` "
                        f"WHERE page_id IN ({pages_sql}) GROUP BY page_id",
                        db="research",
                    )
                }
                loc_c = {
                    int(r["page_id"]): int(r["n"])
                    for r in dst.query(
                        f"SELECT page_id, COUNT(*) AS n FROM research.`{tbl}` "
                        f"WHERE page_id IN ({pages_sql}) GROUP BY page_id",
                        db="research",
                    )
                }
                todo = sorted(pid for pid, n in vps_c.items() if loc_c.get(pid, 0) < n)
                if not todo:
                    log(f"[research.{tbl}] up to date ({sum(vps_c.values())} rows)")
                    continue
                for i in range(0, len(todo), 100):
                    chunk = todo[i:i + 100]
                    dump_and_load(src, dst, "research", tbl, where=f"page_id IN ({aot.ints(chunk)})")
                tot = int(
                    dst.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}` WHERE page_id IN ({pages_sql})", db="research")[0]["n"]
                )
                log(f"[research.{tbl}] synced {tot} slice rows in {time.monotonic() - t0:.1f}s")
            elif "episode_id" in cols and "s4_episode" in tables:
                if not fi_pages:
                    continue
                where_ep = f"episode_id IN (SELECT episode_id FROM research.s4_episode WHERE page_id IN ({pages_sql}))"
                vps_n = int(src.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}` WHERE {where_ep}", db="research")[0]["n"])
                loc_n = int(dst.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}` WHERE {where_ep}", db="research")[0]["n"])
                if loc_n >= vps_n:
                    log(f"[research.{tbl}] up to date ({loc_n} slice episode rows)")
                    continue
                dump_and_load(src, dst, "research", tbl, where=where_ep)
                loc_after = int(dst.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}` WHERE {where_ep}", db="research")[0]["n"])
                log(f"[research.{tbl}] synced {loc_after} slice episode rows in {time.monotonic() - t0:.1f}s")
            else:
                vps_n = int(src.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}`", db="research")[0]["n"])
                loc_n = int(dst.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}`", db="research")[0]["n"])
                if loc_n >= vps_n:
                    log(f"[research.{tbl}] up to date ({loc_n} rows)")
                    continue
                dump_and_load(src, dst, "research", tbl, replace=True)
                loc_after = int(dst.query(f"SELECT COUNT(*) AS n FROM research.`{tbl}`", db="research")[0]["n"])
                log(f"[research.{tbl}] synced {loc_after} rows in {time.monotonic() - t0:.1f}s")
        except RuntimeError as ex:
            log(f"[research.{tbl}] skipped (table changed or unavailable on VPS): {ex}")


def analyze_and_report(dst: DbEndpoint, research_tables: list[str], total_s: float, revtext_stats: dict) -> None:
    tables_to_analyze = [t for t in CORPUS_TABLES if t != "revision_text" or revtext_stats.get("copied_rows", 0) > 0]
    analyze_stmts = [f"ANALYZE TABLE corpus.`{t}`;" for t in tables_to_analyze]
    analyze_stmts += [f"ANALYZE TABLE research.`{t}`;" for t in research_tables]
    dst.execute(NL.join(analyze_stmts), db="corpus")

    size_rows = dst.query(
        """
        SELECT table_schema AS db,
               table_name AS tbl,
               ROUND(data_length / 1048576, 2) AS data_mb,
               ROUND(index_length / 1048576, 2) AS idx_mb,
               ROUND((data_length + index_length) / 1048576, 2) AS total_mb
        FROM information_schema.tables
        WHERE table_schema IN ('corpus', 'research')
        ORDER BY table_schema, table_name
        """
    )

    print()
    print("| schema.table | exact_rows | data_mb | index_mb | total_mb |")
    print("|---|---:|---:|---:|---:|")
    grand_mb = 0.0
    for r in size_rows:
        db = r["db"]
        tbl = r["tbl"]
        exact_n = int(dst.query(f"SELECT COUNT(*) AS n FROM `{db}`.`{tbl}`", db=db)[0]["n"])
        tot_mb = float(r["total_mb"])
        grand_mb += tot_mb
        print(f"| `{db}.{tbl}` | {exact_n:,} | {r['data_mb']} | {r['idx_mb']} | {r['total_mb']} |")
    print(f"| **TOTAL** | | | | **{grand_mb:.2f}** |")
    print()
    print(
        f"Export finished in {total_s:.1f}s "
        f"(revision_text: +{revtext_stats['copied_rows']:,} rows, "
        f"{revtext_stats['raw_mb']} MB uncompressed, {revtext_stats['queries']} queries, "
        f"{revtext_stats['seconds']}s; {revtext_stats['skipped_pages']} pages skipped)."
    )


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pages", help="File with one fi page_id per line (overrides default union)")
    ap.add_argument("--batch-size", type=int, default=200, help="Max revision_text rows per query (<= 200, default 200)")
    ap.add_argument("--pause", type=float, default=0.05, help="Pause in seconds between revision_text queries (default 0.05)")
    args = ap.parse_args()

    if args.batch_size < 1 or args.batch_size > 200:
        ap.error("--batch-size must be between 1 and 200 (per VPS load guardrails)")
    if args.pause < 0:
        ap.error("--pause must be >= 0")

    vps_pw = os.environ.get("CORPUS_PW") or os.environ.get("MYSQL_PWD") or ""
    if not vps_pw:
        raise SystemExit("CORPUS_PW (or MYSQL_PWD) must be set to read from the VPS tunnel.")
    slice_root_pw = read_slice_root_pw()

    src = DbEndpoint(
        host=os.environ.get("CORPUS_HOST", "127.0.0.1"),
        port=os.environ.get("CORPUS_PORT", "3307"),
        user=os.environ.get("CORPUS_USER", "corpus_ro"),
        password=vps_pw,
        compress=True,
    )
    dst = DbEndpoint(
        host=SLICE_HOST,
        port=SLICE_PORT,
        user="root",
        password=slice_root_pw,
        compress=False,
    )

    # Sanity-check connectivity on both ends before starting.
    try:
        dst.query("SELECT 1")
    except Exception as ex:
        raise SystemExit(f"Cannot reach local slice on {SLICE_HOST}:{SLICE_PORT} ({ex}). Run 'research/slice/slice.sh up' first.")
    try:
        src.query("SELECT 1")
    except Exception as ex:
        raise SystemExit(f"Cannot reach VPS tunnel on {src.host}:{src.port} ({ex}). Run 'scripts/corpus-tunnel.sh' first.")

    t_start = time.monotonic()
    research_tables = copy_ddl(src, dst)
    fi_pages = resolve_page_set(args, src)
    slice_lang_pages = resolve_siblings(src, fi_pages)

    sync_category_link(src, dst)
    sync_article_vector(src, dst)
    sync_article(src, dst, slice_lang_pages)
    sync_article_category(src, dst, slice_lang_pages)
    expected_rev_counts = sync_revision(src, dst, fi_pages)
    revtext_stats = sync_revision_text(src, dst, fi_pages, expected_rev_counts, args.batch_size, args.pause)
    sync_research_tables(src, dst, research_tables, fi_pages)

    total_s = time.monotonic() - t_start
    analyze_and_report(dst, research_tables, total_s, revtext_stats)


if __name__ == "__main__":
    main()
