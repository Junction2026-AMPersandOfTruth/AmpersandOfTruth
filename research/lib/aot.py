"""Shared helpers for P1 research variants (Codex lane, epic #3). Python 3.10+, standard library only.

    import sys, pathlib; sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
    import aot

Corpus access goes through the `mariadb` CLI (no pip installs), configured by the contract env vars:
CORPUS_HOST (127.0.0.1), CORPUS_PORT (3307 = scripts/corpus-tunnel.sh), CORPUS_USER (corpus_ro), CORPUS_PW, CORPUS_DB.
If CORPUS_PW is unset, MYSQL_PWD is used. Embeddings: OLLAMA_URL (default :11434), then :11435 (VPS via the tunnel).
Everything logs to stderr; nothing here prints to stdout.
"""
from __future__ import annotations

import hashlib
import ipaddress
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

STATS = {"sql_queries": 0, "sql_seconds": 0.0, "embed_calls": 0, "embed_seconds": 0.0}


def log(*args) -> None:
    print(*args, file=sys.stderr)


# ── SQL ──────────────────────────────────────────────────────────────────────

def _unescape(v: str):
    if v == "NULL":
        return None
    if "\\" not in v:
        return v
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t", "0": "\0", "\\": "\\"}.get(m.group(1), m.group(1)), v)


def sql(query: str, timeout: int = 110) -> list[dict]:
    """Run one statement (or a ;-separated script) and return the LAST result set as dicts of strings/None.

    Never interpolate untrusted text without `quote()`. Numbers come back as strings; convert yourself.
    """
    env = dict(os.environ)
    env["MYSQL_PWD"] = os.environ.get("CORPUS_PW") or os.environ.get("MYSQL_PWD", "")
    cmd = ["mariadb", "--batch", "--default-character-set=utf8mb4",
           "-h", os.environ.get("CORPUS_HOST", "127.0.0.1"), "-P", os.environ.get("CORPUS_PORT", "3307"),
           "-u", os.environ.get("CORPUS_USER", "corpus_ro"), os.environ.get("CORPUS_DB", "corpus")]
    t0 = time.monotonic()
    for attempt in range(4):  # the tunnel/VPS drops connections under load (ERROR 2002/2003/2013/2026): retry those only
        p = subprocess.run(cmd, input=query, capture_output=True, text=True, env=env, timeout=timeout)
        if p.returncode == 0 or not re.search(r"ERROR 20(02|03|13|26)|Lost connection|TLS/SSL", p.stderr):
            break
        log(f"  mariadb connection error, retry {attempt + 1}: {p.stderr.strip()[:120]}")
        time.sleep(1 + 2 * attempt)
    STATS["sql_queries"] += 1
    STATS["sql_seconds"] += time.monotonic() - t0
    if p.returncode != 0:
        raise RuntimeError(f"mariadb failed: {p.stderr.strip()[:500]}")
    lines = p.stdout.split("\n")
    # With several result sets the CLI prints them back to back; keep the last header + rows.
    rows: list[dict] = []
    header: list[str] | None = None
    for line in lines:
        if line == "":
            continue
        cells = line.split("\t")
        if header is None or (len(cells) == len(header) and cells == header):
            header = cells
            rows = []
            continue
        rows.append({h: _unescape(c) for h, c in zip(header, cells)})
    return rows


def quote(s: str) -> str:
    """SQL string literal for MariaDB (utf8mb4)."""
    return "'" + s.replace("\\", "\\\\").replace("'", "''").replace("\0", "") + "'"


def ints(xs) -> str:
    """Comma-separated integer list for IN (...). Raises on non-integers."""
    return ",".join(str(int(x)) for x in xs) or "NULL"


# ── Embeddings (bge-m3 via Ollama, cached on disk) ───────────────────────────

_CACHE = Path(os.environ.get("AOT_CACHE_DIR", Path.home() / ".cache" / "aot-research")) / "embeddings"


def embed(text: str) -> list[float]:
    """bge-m3 1024-d unit vector for `text`. Cached by whitespace-normalised text (case kept), like the app."""
    norm = " ".join(text.split())
    key = hashlib.sha256(("bge-m3\n" + norm).encode()).hexdigest()
    f = _CACHE / f"{key}.json"
    if f.exists():
        return json.loads(f.read_text())
    urls = [os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434"), "http://127.0.0.1:11435"]
    last = None
    for url in dict.fromkeys(urls):
        try:
            t0 = time.monotonic()
            req = urllib.request.Request(url.rstrip("/") + "/api/embed", method="POST",
                                         data=json.dumps({"model": "bge-m3", "input": norm}).encode(),
                                         headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=60) as r:
                vec = json.load(r)["embeddings"][0]
            STATS["embed_calls"] += 1
            STATS["embed_seconds"] += time.monotonic() - t0
            _CACHE.mkdir(parents=True, exist_ok=True)
            f.write_text(json.dumps(vec))
            return vec
        except Exception as e:  # try the next endpoint
            last = e
    raise RuntimeError(f"no Ollama endpoint could embed with bge-m3: {last}")


def vec_literal(vec: list[float]) -> str:
    return "VEC_FromText('[" + ",".join(f"{x:.7g}" for x in vec) + "]')"


# ── Retrieval primitives ─────────────────────────────────────────────────────

def nearest(vec: list[float], k: int, langs: tuple[str, ...] | None = None) -> list[dict]:
    """Top-k article vectors by cosine distance, best first: [{lang, page_id, title, wikidata_id, distance}].

    Top-k runs in a subquery directly on article_vector so the HNSW index is used, then joins article.
    A `langs` filter is applied AFTER the index scan (over-fetches 4×k), because a WHERE inside kills the index.
    """
    fetch = k if not langs else k * 4
    rows = sql(f"""
        SELECT a.lang, a.page_id, a.title, a.wikidata_id, v.distance
        FROM (SELECT lang, page_id, VEC_DISTANCE_COSINE(embedding, {vec_literal(vec)}) AS distance
              FROM article_vector ORDER BY distance LIMIT {int(fetch)}) v
        JOIN article a USING (lang, page_id)
        ORDER BY v.distance""")
    out = [{"lang": r["lang"], "page_id": int(r["page_id"]), "title": r["title"],
            "wikidata_id": r["wikidata_id"], "distance": float(r["distance"])} for r in rows]
    if langs:
        out = [r for r in out if r["lang"] in langs]
    return out[:k]


def fi_siblings(wikidata_ids) -> dict[str, int]:
    """wikidata_id → fi page_id, for the ids that have a Finnish article."""
    ids = [w for w in dict.fromkeys(wikidata_ids) if w]
    if not ids:
        return {}
    rows = sql("SELECT wikidata_id, page_id FROM article WHERE lang='fi' AND wikidata_id IN ("
               + ",".join(quote(w) for w in ids) + ")")
    return {r["wikidata_id"]: int(r["page_id"]) for r in rows}


def fold_to_fi(hits: list[dict]) -> list[dict]:
    """Fold en/sv hits onto their fi sibling (by wikidata_id), keep the best rank per fi page_id.

    Returns [{page_id, distance, via_lang, title}] best first. Hits with no fi sibling are dropped.
    """
    sib = fi_siblings(h["wikidata_id"] for h in hits if h["lang"] != "fi")
    seen: dict[int, dict] = {}
    for h in hits:
        pid = h["page_id"] if h["lang"] == "fi" else sib.get(h["wikidata_id"])
        if pid is None or pid in seen:
            continue
        seen[pid] = {"page_id": pid, "distance": h["distance"], "via_lang": h["lang"], "title": h["title"]}
    return list(seen.values())


def cost() -> dict:
    """The contract's optional `cost` object for this process."""
    return {"llm_calls": 0, "sql_queries": STATS["sql_queries"]}


# ── Editor display (port of Features/Evidence/EditorDisplay.cs; keep in sync) ─

_TEMP = re.compile(r"^~\d{4}-\d+-\d+$")
_IPV4 = re.compile(r"^\d{1,3}(\.\d{1,3}){3}$")
_WIKILINK = re.compile(r"\[\[([^\[\]|]*)(?:\|([^\[\]]*))?\]\]")
_C_IPV4 = re.compile(r"(?<!\d\.)(?<!\d)\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d)")
_C_IPV6 = re.compile(r"(?<![0-9A-Za-z])(?<![0-9A-Fa-f]:)(?<!::)[0-9A-Fa-f:]*:[0-9A-Fa-f:]*(?![0-9A-Za-z:])")


def user_display(user: str | None) -> str:
    """EditorDisplay.For(user).PlainText — the only form of a user name allowed in output files."""
    if user is None or not user.strip():
        return "Hidden editor"
    u = user.strip()
    if _TEMP.match(u):
        return f"{u} (temporary account)"
    if _IPV4.match(u):
        try:
            ip = ipaddress.IPv4Address(u)
            b = ip.packed
            return f"{b[0]}.{b[1]}.x.x (anonymous)"
        except ValueError:
            pass
    if ":" in u:
        try:
            ip = ipaddress.IPv6Address(u)
            b = ip.packed
            return f"{(b[0] << 8 | b[1]):X}:{(b[2] << 8 | b[3]):X}:… (anonymous)"
        except ValueError:
            pass
    return u


def is_anonymous(user: str | None) -> bool:
    return user_display(user).endswith(("(anonymous)", "(temporary account)"))


def mask_comment(comment: str | None) -> str:
    """EditorDisplay.MaskComment — wikilinks flattened, IPs masked. Use for every summary written to a file."""
    if comment is None or not comment.strip():
        return ""
    text = _WIKILINK.sub(lambda m: m.group(2) if m.group(2) and m.group(2).strip() else m.group(1), comment)
    text = _C_IPV4.sub(lambda m: user_display(m.group(0)).removesuffix(" (anonymous)")
                       if user_display(m.group(0)).endswith("(anonymous)") else m.group(0), text)

    def v6(m: re.Match) -> str:
        c = m.group(0)
        if c.count(":") < 2 or not re.search(r"[0-9A-Fa-f]", c):
            return c
        d = user_display(c)
        if d.endswith("(anonymous)"):
            return d.removesuffix(" (anonymous)")
        if c.endswith(":"):
            d = user_display(c[:-1])
            if d.endswith("(anonymous)"):
                return d.removesuffix(" (anonymous)") + ":"
        return c
    return _C_IPV6.sub(v6, text)


# ── Revision wikitext (PK fetch only, cached on disk) ────────────────────────

def rev_texts(pairs) -> dict[int, str | None]:
    """{rev_id: wikitext} for (page_id, rev_id) pairs, fetched by PRIMARY KEY in batches of 20 and cached on disk.

    Never use this to scan: revision_text is 5.7 GB on a 2-vCPU server. None = no text row (suppressed revision).
    """
    d = _CACHE.parent / "revtext"
    out: dict[int, str | None] = {}
    todo = []
    for page_id, rev_id in dict.fromkeys((int(p), int(r)) for p, r in pairs):
        f = d / f"{rev_id}.txt"
        if f.exists():
            out[rev_id] = f.read_text(encoding="utf-8")
        else:
            todo.append((page_id, rev_id))
    d.mkdir(parents=True, exist_ok=True)
    for i in range(0, len(todo), 20):
        chunk = todo[i:i + 20]
        cond = " OR ".join(f"(page_id={p} AND rev_id={r})" for p, r in chunk)
        for row in sql(f"SELECT rev_id, text FROM revision_text WHERE {cond}"):
            rid = int(row["rev_id"])
            out[rid] = row["text"] or ""
            (d / f"{rid}.txt").write_text(out[rid], encoding="utf-8")
    for _, r in todo:
        out.setdefault(r, None)
    return out
