#!/usr/bin/env python3
"""Extract a small evaluation sample of reverted Finnish Wikipedia edits (issue #16).

Data: dataset M (GitHub release m-2026-10 of MariaDB/mariadb-wikipedia-datasets),
versions-01..16.parquet, one row per revision, sorted by (timestamp, rev_id) per article.

Revert detection (identity revert): revision R_k whose sha1 equals the sha1 of an earlier
revision R_j of the same page with j < k-1 (most recent earlier occurrence). For the
sample we use single-edit reverts (j = k-2): reverted edit E = R_{k-1}, parent P = R_j,
reverting revision R_k.

Usage:
  python -I extract_sample.py --data /tmp/jx/data --out sample20.jsonl
      -> builds the sample from the hand-picked PICKS list below (fast: only those revs' texts).
  python -I extract_sample.py --data /tmp/jx/data --pool pool.jsonl --stats revert_stats.txt
      -> scans every single-edit revert, applies the skip rules and writes the candidate pool
         (with cheap selection features) that PICKS was hand-chosen from.

This script does NOT classify edits; selection_bucket is only a coarse diversity bucket.
"""
from __future__ import annotations

import argparse
import difflib
import glob
import json
import os
import re
import sys
from collections import Counter
from concurrent.futures import ProcessPoolExecutor
from datetime import datetime, timezone

import duckdb

# (reverted edit E.rev_id, selection bucket) -- hand-picked from the candidate pool for variety.
PICKS: list[tuple[int, str]] = [
    # graffiti / obscenity / nonsense
    (400519, "graffiti"),             # Suomen sisällissota, anon 2005: nonsense sentence
    (1580551, "graffiti"),            # Talvisota, anon 2006: obscenity inside a sentence
    (15144408, "graffiti"),           # Saamelaiset, anon 2015: slur in infobox name
    (18648732, "graffiti"),           # Lapin sota, registered 2020: obscene nonsense in lead link
    (23739766, "graffiti"),           # Simo Häyhä, temporary 2025: infobox nonsense
    # substantive changes on contested history / politics
    (2968411, "contested-history"),   # Talvisota, anon 2007: who attacked whom
    (4225630, "contested-history"),   # Talvisota, registered 2008: changed strength / casualty figures
    (1507690, "contested-history"),   # Karjalankannas, registered 2006: Karelian Isthmus status
    (13268224, "contested-history"),  # Talvisota, registered 2013: attribution -> evaluative wording
    (16351052, "contested-history"),  # Suomen sisällissota, anon 2017: "vapaussota" framing
    (18422484, "contested-history"),  # Jatkosota, anon 2019: war outcome in infobox
    (19544342, "contested-history"),  # Mannerheim, registered 2021: suomalainen -> suomenruotsalainen
    (22741016, "contested-history"),  # Metsäsaamelaiset, anon 2024: minority status wording
    # look like good-faith edits, reverted anyway
    (8960233, "good-faith"),          # Lapin sota, registered 2010: removed "Suomen voitto"
    (17089097, "good-faith"),         # Suomen sisällissota, anon 2018: sourced background detail
    (18357147, "good-faith"),         # Jääkäripataljoona 27, anon 2019: terminology, reverted as block evasion
    # touching <ref> / sources
    (15356378, "sources"),            # Mainilan laukaukset, registered 2015: is the YLE source valid
    (23898635, "sources"),            # Sumiainen, temporary 2026: joke <ref>
    # revert summary mentions POV / missing source
    (1412385, "summary-flagged"),     # Suojeluskunta, anon 2006: "erittäin POV"
    (23817012, "summary-flagged"),    # Suur-Suomi, registered 2026: "Lähteetöntä"
]

MAX_SPAN_CHARS = 1500
CONTEXT_CHARS = 400
MIN_INSERTED_PROSE = 15

# ---------------------------------------------------------------- revert detection

REVERT_SQL = """
WITH m AS (
    SELECT page_id, title, rev_id, timestamp, "user", anonymous, comment, sha1, bytes,
           row_number() OVER (PARTITION BY page_id ORDER BY timestamp, rev_id) AS pos
    FROM read_parquet($files)
), s AS (
    SELECT page_id, pos, sha1,
           lag(pos) OVER (PARTITION BY page_id, sha1 ORDER BY pos) AS prev_same
    FROM m WHERE sha1 IS NOT NULL
)
SELECT r.page_id, e.title,
       p.rev_id AS p_rev, e.rev_id AS e_rev, r.rev_id AS r_rev,
       p.timestamp::TIMESTAMP AS p_ts, e.timestamp::TIMESTAMP AS e_ts, r.timestamp::TIMESTAMP AS r_ts,
       p."user" AS p_user, e."user" AS e_user, r."user" AS r_user,
       e.anonymous AS e_anon, e.comment AS e_comment, r.comment AS r_comment,
       p.bytes AS p_bytes, e.bytes AS e_bytes
FROM s
JOIN m r ON r.page_id = s.page_id AND r.pos = s.pos
JOIN m e ON e.page_id = s.page_id AND e.pos = s.pos - 1
JOIN m p ON p.page_id = s.page_id AND p.pos = s.pos - 2
WHERE s.prev_same = s.pos - 2
"""

STATS_SQL = """
WITH m AS (
    SELECT page_id, anonymous, sha1,
           row_number() OVER (PARTITION BY page_id ORDER BY timestamp, rev_id) AS pos
    FROM read_parquet($files)
), s AS (
    SELECT *, lag(pos) OVER (PARTITION BY page_id, sha1 ORDER BY pos) AS prev_same
    FROM m WHERE sha1 IS NOT NULL
)
SELECT (SELECT count(*) FROM m) AS total,
       (SELECT count(*) FILTER (WHERE anonymous) FROM m) AS total_anon,
       count(*) FILTER (WHERE prev_same < pos - 1) AS identity_reverts,
       count(*) FILTER (WHERE prev_same = pos - 2) AS single_edit_reverts,
       count(*) FILTER (WHERE prev_same = pos - 1) AS null_edits
FROM s
"""


def connect() -> duckdb.DuckDBPyConnection:
    con = duckdb.connect()
    con.execute("SET TimeZone='UTC'")
    return con


def find_reverts(con, files: list[str], only_e: set[int] | None = None) -> list[dict]:
    rel = con.execute(REVERT_SQL, {"files": files})
    cols = [d[0] for d in rel.description]
    rows = [dict(zip(cols, r)) for r in rel.fetchall()]
    if only_e is not None:
        rows = [r for r in rows if r["e_rev"] in only_e]
    return rows


def fetch_texts(con, files: list[str], rev_ids: set[int]) -> dict[int, str]:
    if not rev_ids:
        return {}
    con.execute("CREATE OR REPLACE TEMP TABLE want(rev_id UINTEGER)")
    con.executemany("INSERT INTO want VALUES (?)", [(r,) for r in rev_ids])
    rel = con.execute(
        "SELECT v.rev_id, v.text FROM read_parquet($files) v SEMI JOIN want USING (rev_id)",
        {"files": files},
    )
    return {rid: (txt or "") for rid, txt in rel.fetchall()}


# ---------------------------------------------------------------- diff

TOKEN_RE = re.compile(r"\s+|\w+|[^\w\s]", re.UNICODE)
MERGE_GAP_CHARS = 12  # equal runs this short (e.g. one or two words) get merged into one span
DIFF_MARGIN = 400  # unchanged chars kept around the changed region (room for sentence widening)
EXPAND_IF_SHORTER = 40
EXPAND_MAX_TOKENS = 150


def _tokens(s: str) -> list[str]:
    return TOKEN_RE.findall(s)


def _trim_common(a: str, b: str) -> tuple[int, int]:
    """Common prefix / suffix lengths (chars), backed off to a whitespace boundary."""
    n = min(len(a), len(b))
    pre = 0
    while pre < n and a[pre] == b[pre]:
        pre += 1
    while pre > 0 and not a[pre - 1].isspace():
        pre -= 1
    suf = 0
    while suf < n - pre and a[len(a) - 1 - suf] == b[len(b) - 1 - suf]:
        suf += 1
    while suf > 0 and not a[len(a) - suf].isspace():
        suf -= 1
    return pre, suf


def _is_boundary(toks: list[str], k: int) -> bool:
    """Token k ends a line or a sentence."""
    t = toks[k]
    return "\n" in t or (t in ".!?" and (k + 1 >= len(toks) or toks[k + 1].isspace()))


def diff_spans(a: str, b: str) -> tuple[list[str], list[str], list[str], list[str], int]:
    """Word-level diff a -> b merged into contiguous spans.

    Spans separated by <= MERGE_GAP_CHARS of unchanged text are merged. Replacement spans whose
    shorter side has < EXPAND_IF_SHORTER prose chars (changed numbers, swapped words, mid-word
    graffiti) are widened to the enclosing sentence/line on both sides so a reader sees the whole
    old vs new sentence. Pure insertions/deletions are kept word-level.

    Returns (inserted_raw, removed_raw, inserted, removed, char offset in `a` of the first change).
    """
    pre, suf = _trim_common(a, b)
    # keep a margin of unchanged text around the change so short replacements can be widened
    pre = max(0, pre - DIFF_MARGIN)
    while pre > 0 and not a[pre - 1].isspace():
        pre -= 1
    suf = max(0, suf - DIFF_MARGIN)
    while suf > 0 and not a[len(a) - suf].isspace():
        suf -= 1
    a_mid, b_mid = a[pre:len(a) - suf], b[pre:len(b) - suf]
    ta, tb = _tokens(a_mid), _tokens(b_mid)
    if len(ta) * len(tb) > 25_000_000:  # huge rewrite: fall back to line diff granularity
        ta, tb = a_mid.splitlines(keepends=True), b_mid.splitlines(keepends=True)
    a_off = [0]
    for t in ta:
        a_off.append(a_off[-1] + len(t))
    ops = difflib.SequenceMatcher(None, ta, tb, autojunk=False).get_opcodes()

    groups: list[list] = []  # [i1, i2, j1, j2, has_ins, has_del]
    pending_equal = None
    for tag, i1, i2, j1, j2 in ops:
        if tag == "equal":
            pending_equal = (i1, i2)
            continue
        has_ins, has_del = j2 > j1, i2 > i1
        if groups and (pending_equal is None or
                       len("".join(ta[pending_equal[0]:pending_equal[1]])) <= MERGE_GAP_CHARS):
            g = groups[-1]
            g[1], g[3] = i2, j2
            g[4] |= has_ins
            g[5] |= has_del
        else:
            groups.append([i1, i2, j1, j2, has_ins, has_del])
        pending_equal = None

    def texts(gs):
        ins = [s for g in gs if g[4] for s in ["".join(tb[g[2]:g[3]]).strip()] if s]
        rem = [s for g in gs if g[5] for s in ["".join(ta[g[0]:g[1]]).strip()] if s]
        return ins, rem

    ins_raw, rem_raw = texts(groups)

    widened: list[list] = []
    for n, (i1, i2, j1, j2, has_ins, has_del) in enumerate(groups):
        if has_ins and has_del and min(len(prose("".join(ta[i1:i2]))),
                                       len(prose("".join(tb[j1:j2])))) < EXPAND_IF_SHORTER:
            # tokens between groups are an equal run, so a and b shift by the same amount
            lo = widened[-1][1] if widened else 0
            k = i1
            while k > lo and i1 - k < EXPAND_MAX_TOKENS and not _is_boundary(ta, k - 1):
                k -= 1
            i1, j1 = k, j1 - (i1 - k)
            hi = groups[n + 1][0] if n + 1 < len(groups) else len(ta)
            k = i2
            while k < hi and k - i2 < EXPAND_MAX_TOKENS and not _is_boundary(ta, k):
                k += 1
            if k < hi and ta[k] in ".!?":
                k += 1
            i2, j2 = k, j2 + (k - i2)
        if widened and i1 <= widened[-1][1]:
            w = widened[-1]
            w[1], w[3] = max(w[1], i2), max(w[3], j2)
            w[4] |= has_ins
            w[5] |= has_del
        else:
            widened.append([i1, i2, j1, j2, has_ins, has_del])

    ins, rem = texts(widened)
    first = pre + (a_off[groups[0][0]] if groups else 0)
    return ins_raw, rem_raw, ins, rem, first


def join_spans(spans: list[str]) -> str:
    s = "\n…\n".join(spans)
    return s if len(s) <= MAX_SPAN_CHARS else s[:MAX_SPAN_CHARS - 1] + "…"


def context_around(text: str, pos: int, width: int = CONTEXT_CHARS) -> str:
    start = max(0, min(pos - width // 2, len(text) - width))  # near either edge: shift, keep width
    end = min(len(text), start + width)
    if start > 0:
        sp = text.find(" ", start, pos)
        start = sp + 1 if sp != -1 else start
    if end < len(text):
        sp = text.rfind(" ", pos, end)
        end = sp if sp != -1 else end
    return ("…" if start > 0 else "") + text[start:end].strip() + ("…" if end < len(text) else "")


# ---------------------------------------------------------------- filters / features

INTERWIKI_RE = re.compile(r"\[\[[a-z]{2,3}(?:-[a-z]{2,8})*:[^\]]*\]\]")
MEDIA_CAT_RE = re.compile(r"\[\[\s*(?:Luokka|Category|Tiedosto|File|Kuva|Image)\s*:[^\]]*\]\]", re.I)


def prose(s: str) -> str:
    """Strip markup that the skip rules don't count as content (templates, categories, interwiki)."""
    s = re.sub(r"<!--.*?(?:-->|$)", " ", s, flags=re.S)
    for _ in range(4):
        s = re.sub(r"\{\{[^{}]*\}\}", " ", s)
    s = MEDIA_CAT_RE.sub(" ", s)
    s = INTERWIKI_RE.sub(" ", s)
    s = re.sub(r"<[^>]*>", " ", s)
    s = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", r"\1", s)
    s = re.sub(r"'{2,}|={2,}|[\[\]{}|*#]", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def is_blanking(p_text: str, e_text: str) -> bool:
    return len(e_text.strip()) == 0 or (len(p_text) > 300 and len(e_text.strip()) < 0.02 * len(p_text))


# ---------------------------------------------------------------- privacy / formatting

USER_LINK_RE = re.compile(
    r"\[\[\s*(?:(?:User|Käyttäjä|User[ _]talk|Keskustelu[ _]käyttäjästä|Käyttäjä[ _]keskustelu)\s*:|"
    r"(?:Special|Toiminnot|Spesiaali)\s*:\s*(?:Contributions|Muokkaukset)/)[^\]]*(?:\]\]|$)",
    re.I,
)
TALK_RE = re.compile(r"\[\[\s*(?:User[ _]talk|Keskustelu[ _]käyttäjästä|Käyttäjä[ _]keskustelu)\s*:", re.I)
IP_RE = re.compile(r"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b(?:[0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}\b")
TEMP_RE = re.compile(r"~\d{4}-\d+-\d+")


def redact(summary: str | None, users: list[str | None]) -> str:
    """Remove editor names / IPs from an edit summary (revert summaries name both editors)."""
    if not summary:
        return ""
    s = USER_LINK_RE.sub(lambda m: "keskustelu" if TALK_RE.match(m.group(0)) else "<user>", summary)
    for u in sorted({u for u in users if u and len(u) >= 2}, key=len, reverse=True):
        s = s.replace(u, "<user>").replace(u.replace(" ", "_"), "<user>")
    s = TEMP_RE.sub("<user>", s)
    s = IP_RE.sub("<user>", s)
    for pat in NAMED_SUMMARY_RES:  # backstop: plain-text names in standard revert summaries
        s = pat.sub(lambda m: m.group(1) + "<user>" + m.group(3), s)
    return s


_NAME = r"((?!<user>)[^()\[\]<>:,;]+?)"
NAMED_SUMMARY_RES = [
    re.compile(r"(\bdated [\d\-: ]+ by )" + _NAME + r"( using\b|$)"),
    re.compile(r"(\bjonka (?:on )?(?:teki|tehnyt) )" + _NAME + r"( \(|[,:;]|$)"),
    re.compile(r"(\b[Kk]äyttäjän )" + _NAME + r"( tekemään versioon| muokkaukset kumottiin)"),
    re.compile(r"(\((?:tehnyt|käyttäjältä) )" + _NAME + r"(\))"),
    re.compile(r"(\bkäyttäjältä )" + _NAME + r"(:|$)"),
]


def editor_kind(user: str | None, anon: bool) -> str:
    if user and user.startswith("~"):
        return "temporary"
    return "anonymous" if anon else "registered"


def fmt_exposure(seconds: float) -> str:
    s = int(seconds)
    if s < 60:
        return f"{s} s"
    m = s // 60
    if m < 60:
        return f"{m} min"
    h, m = divmod(m, 60)
    if h < 24:
        return f"{h} h {m} min" if m else f"{h} h"
    d = h // 24
    if d < 60:
        return f"{d} day" if d == 1 else f"{d} days"
    if d < 730:
        return f"{round(d / 30.44)} months"
    return f"{d / 365.25:.1f} years"


def iso(ts: datetime) -> str:
    """Timestamps come back naive in UTC (session TimeZone=UTC, cast to TIMESTAMP)."""
    return ts.replace(tzinfo=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------------------------------------------------------------- record building

TOPIC_RE = re.compile(
    r"sisällisso|1918|punai|valkoi|talvisot|jatkosot|karjal|neuvostoli|venäjä|venäläi|mannerheim|"
    r"jääkäri|lapin sod|ruotsinkiel|kielipoliti|saamelai|suomettum|kekkonen|ryty|tanner|"
    r"sotasyyllis|vankileiri|terrori|kaatui|kaatuneit|kuoli|uhri|sotilai|hyökkä|miehity|rauha|"
    r"fasis|kommunis|äärioike|natsi|saksa",
    re.I,
)
OBSCENE_RE = re.compile(
    r"vittu|vitun|paska|perse|kulli|kyrpä|homo|huora|kakka|pieru|pillu|lol\b|haha|hähä|jee\b|moi\b|"
    r"rakastan|on tyhmä|on paras|nussi|runkk|neekeri|mulkku|tissi|penis|sexy|fuck|shit|gay|suck",
    re.I,
)
# "kumottu"/"hylättiin" are left out: they open every standard undo/review summary
FLAG_RE = re.compile(r"vandal|\bpov|neutraal|lähtee|lähde|lähdett|lähtet|sotku|töhr|roska|"
                     r"spam|mainos|tutkimus|eston kiert|asiaton|pila|hölynpöly", re.I)
ROLLBACK_RE = re.compile(r"muokkaukset kumottiin ja sivu palautettiin")


def build_record(meta: dict, p_text: str, e_text: str, bucket: str | None = None) -> tuple[dict, tuple]:
    ins_raw, rem_raw, ins, rem, first = diff_spans(p_text, e_text)
    users = [meta["p_user"], meta["e_user"], meta["r_user"]]
    rec = {
        "_id": f"rev-{meta['e_rev']}",
        "article_title": meta["title"],
        "edit_timestamp": iso(meta["e_ts"]),
        "editor_kind": editor_kind(meta["e_user"], bool(meta["e_anon"])),
        "edit_summary": redact(meta["e_comment"], users),
        "revert_summary": redact(meta["r_comment"], users),
        "exposure": fmt_exposure((meta["r_ts"] - meta["e_ts"]).total_seconds()),
        "inserted_text": join_spans(ins),
        "removed_text": join_spans(rem),
        "context": context_around(p_text, first),
        "diff_url": f"https://fi.wikipedia.org/w/index.php?diff={meta['e_rev']}&oldid={meta['p_rev']}",
        "page_id": int(meta["page_id"]),
        "rev_id": int(meta["e_rev"]),
        "parent_rev_id": int(meta["p_rev"]),
        "revert_rev_id": int(meta["r_rev"]),
        "selection_bucket": bucket or "",
    }
    return rec, (ins_raw, rem_raw, ins)


def skip_reason(p_text: str, e_text: str, diff: tuple) -> str | None:
    ins_raw, rem_raw, ins = diff
    if is_blanking(p_text, e_text):
        return "blanking"
    ins_prose = prose("\n".join(ins_raw))
    rem_prose = prose("\n".join(rem_raw))
    if not ins_prose and not rem_prose:
        return "markup-only"  # whitespace / templates / categories / interwiki
    if len(ins_prose) < MIN_INSERTED_PROSE:
        # exception: changed numbers/dates are short but substantive; judge them on the widened sentence
        numeric = re.search(r"\d", ins_prose) and re.search(r"\d", rem_prose)
        if not (numeric and len(prose("\n".join(ins))) >= MIN_INSERTED_PROSE):
            return "short-insert"
        return None
    return None


def scan_file(path: str) -> tuple[list[dict], Counter]:
    con = connect()
    metas = find_reverts(con, [path])
    texts = fetch_texts(con, [path], {m["p_rev"] for m in metas} | {m["e_rev"] for m in metas})
    out, reasons = [], Counter()
    for m in metas:
        p_text, e_text = texts.get(m["p_rev"], ""), texts.get(m["e_rev"], "")
        rec, diff = build_record(m, p_text, e_text)
        reasons["editor:" + rec["editor_kind"]] += 1
        reason = skip_reason(p_text, e_text, diff)
        reasons[reason or "kept"] += 1
        if reason:
            continue
        ins_all, rem_all = "\n".join(diff[0]), "\n".join(diff[1])
        rec["_f"] = {  # cheap selection features (pool only, not part of the sample schema)
            "year": m["e_ts"].year,
            "ins_prose": len(prose(ins_all)),
            "rem_prose": len(prose(rem_all)),
            "ref": ("<ref" in ins_all.lower()) or ("<ref" in rem_all.lower()),
            "digits": bool(re.search(r"\d", ins_all)) and bool(re.search(r"\d", rem_all)),
            "topic": bool(TOPIC_RE.search(m["title"] + " " + ins_all + " " + rem_all)),
            "obscene": bool(OBSCENE_RE.search(ins_all)),
            "rollback": bool(ROLLBACK_RE.search(m["r_comment"] or "")),
            "flag": bool(FLAG_RE.search(rec["revert_summary"])) and not ROLLBACK_RE.search(m["r_comment"] or ""),
            "page_replaced": len(e_text) < 0.3 * len(p_text) and len(p_text) > 1000,
            "self_revert": m["e_user"] == m["r_user"],
            "e_is_revert": bool(re.search(r"kumo|hylä|palaut|revert|\brv", m["e_comment"] or "", re.I)),
        }
        out.append(rec)
    return out, reasons


# ---------------------------------------------------------------- main

def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", required=True, help="folder with versions-*.parquet")
    ap.add_argument("--out", help="sample JSONL to write from PICKS")
    ap.add_argument("--pool", help="scan all single-edit reverts and write the candidate pool JSONL")
    ap.add_argument("--stats", help="write revert stats text file (with --pool)")
    ap.add_argument("--jobs", type=int, default=min(8, os.cpu_count() or 1))
    args = ap.parse_args()
    files = sorted(glob.glob(os.path.join(args.data, "versions-*.parquet")))
    if not files:
        sys.exit(f"no versions-*.parquet in {args.data}")

    if args.pool:
        pool, reasons = [], Counter()
        with ProcessPoolExecutor(args.jobs) as ex:
            for recs, rs in ex.map(scan_file, files):
                pool.extend(recs)
                reasons.update(rs)
        with open(args.pool, "w", encoding="utf-8") as f:
            for r in pool:
                f.write(json.dumps(r, ensure_ascii=False) + "\n")
        print(f"pool: {len(pool)} candidates -> {args.pool}; skip reasons: {dict(reasons)}")
        if args.stats:
            con = connect()
            st = dict(zip(["total", "total_anon", "identity", "single", "null_edits"],
                          con.execute(STATS_SQL, {"files": files}).fetchone()))
            kinds = Counter(r["editor_kind"] for r in pool)
            with open(args.stats, "w", encoding="utf-8") as f:
                f.write(f"files scanned: {len(files)}\n")
                f.write(f"total revisions scanned: {st['total']}\n")
                f.write(f"anonymous revisions (IP or temp account): {st['total_anon']} "
                        f"({st['total_anon'] / st['total']:.1%})\n")
                f.write(f"identity reverts (sha1 == sha1 of R_j, j < k-1): {st['identity']}\n")
                f.write(f"single-edit identity reverts (j = k-2): {st['single']}\n")
                f.write(f"null edits (sha1 == previous revision, not counted as reverts): {st['null_edits']}\n")
                ek = {k[7:]: v for k, v in reasons.items() if k.startswith("editor:")}
                n_single = sum(ek.values())
                f.write(f"single-edit reverted edits by editor kind: {ek} (anonymous+temporary share "
                        f"{(ek.get('anonymous', 0) + ek.get('temporary', 0)) / max(1, n_single):.1%})\n")
                f.write("single-edit reverts by skip rule: "
                        f"{ {k: v for k, v in reasons.items() if not k.startswith('editor:')} }\n")
                f.write(f"candidate pool after skip rules: {len(pool)}; editor kinds: {dict(kinds)} "
                        f"(anonymous+temporary share {(kinds['anonymous'] + kinds['temporary']) / max(1, len(pool)):.1%})\n")
                feats = Counter(k for r in pool for k, v in r["_f"].items() if v is True)
                f.write("pool keyword-heuristic flags (overlapping, crude regexes, NOT labels): "
                        f"{dict(sorted(feats.items()))}\n")
                f.write("  obscene=swear/graffiti word in inserted text; ref=<ref> in diff; digits=numbers on both "
                        "sides; topic=history/politics keyword; flag=non-rollback revert summary with "
                        "vandal/POV/lähde/... keyword; rollback=one-click rollback summary; e_is_revert=E's own "
                        "summary looks like a revert (edit war); self_revert=same user made E and R_k\n")
                years = Counter(r["_f"]["year"] // 5 * 5 for r in pool)
                f.write(f"pool by 5-year bin of E: {dict(sorted(years.items()))}\n")
        return

    if not args.out:
        sys.exit("need --out (sample from PICKS) or --pool (scan)")
    if not PICKS:
        sys.exit("PICKS is empty")
    want = {rid for rid, _ in PICKS}
    buckets = dict(PICKS)
    con = connect()
    metas = {m["e_rev"]: m for m in find_reverts(con, files, only_e=want)}
    missing = want - set(metas)
    if missing:
        sys.exit(f"picked rev ids are not single-edit reverted edits: {sorted(missing)}")
    texts = fetch_texts(con, files, {m["p_rev"] for m in metas.values()} | set(metas))
    with open(args.out, "w", encoding="utf-8") as f:
        for rid, _ in PICKS:
            m = metas[rid]
            rec, _ = build_record(m, texts[m["p_rev"]], texts[rid], buckets[rid])
            f.write(json.dumps(rec, ensure_ascii=False) + "\n")
    print(f"wrote {len(PICKS)} records -> {args.out}")


if __name__ == "__main__":
    main()
