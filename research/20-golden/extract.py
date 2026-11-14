# /// script
# dependencies = ["pymysql"]
# ///
"""P1.E1 (#20): build one labelling pack per golden topic → research/20-golden/packs/<topic>.json (git-ignored).

A pack is everything a labeller may read: candidate articles for the retrieval grades, and candidate incidents with
readable diffs and the community signals of docs/evidence-standard.md §1. Labellers never query the corpus (the VPS has
2 vCPUs); this script runs serially and fetches revision_text by primary key only.

Privacy (evidence-standard §3): editors become per-topic pseudonyms "E<n>" + kind; IPs keep only the masked /16 or /32
range; usernames inside edit summaries are replaced by pseudonyms and IPs masked. No raw username leaves this script.

    research/20-golden/golden.sh extract.py --topic winter-war          # one topic
    research/20-golden/golden.sh extract.py --all                       # every topic in topics.json
    research/20-golden/golden.sh extract.py --stats --pages 1256,8295   # run counts only, no text fetches
"""
from __future__ import annotations

import argparse
import difflib
import ipaddress
import json
import math
import os
import random
import re
import sys
import urllib.request
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

from q import connect

HERE = Path(__file__).resolve().parent
TOPICS = HERE / "topics.json"
PACKS = HERE / "packs"

N_LIKELY, N_LOOKALIKE, N_PARTIAL, MAX_PER_PAGE = 16, 12, 5, 6
DIFF_CAP = 9000
DISPUTE_TEMPLATES = re.compile(r"\{\{\s*(neutraalius|pov|lähteetön|lähde\?|lähde tarkentuu|korjattava|kiistanalainen|"
                               r"tarkistettava|epäselvä|näkökulma|puolueettomuus|tyylikorjattava)", re.I)
DISPUTE_SUMMARY = re.compile(r"pov|neutra|puolueet|lähtee?t|lähdetön|lähteetön|lähde|spekul|propagand|valhe|väite|"
                             r"kiistanal|mielipi|asiaton|tutkimus|ei pidä paikkaansa|virheelli|harhaanjoht|tendenssi|"
                             r"salaliit|disinfo|misinfo|totuu", re.I)
BOT = re.compile(r"bot(\b|$)|^bot", re.I)
TEMP = re.compile(r"^~\d{4}-\d+-\d+$")
WIKILINK = re.compile(r"\[\[([^\[\]|]*)(?:\|([^\[\]]*))?\]\]")
IPV4_IN_TEXT = re.compile(r"(?<!\d)(?<!\d\.)\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d)")
# names after "by user X" phrases, for accounts not in the page history (renamed, or mentioned from elsewhere)
NAME_AFTER = re.compile(r"((?:[Kk]äyttäjä|[Uu]ser|[Kk]eskustelu käyttäjästä|[Uu]ser talk)\s*:\s*|"
                        r"(?:jonka teki|käyttäjän|käyttäjä|tehnyt|tehneet|tekemään|by)\s+)([^\s|\]\[(),.:;]+)")
IPV6_IN_TEXT = re.compile(r"(?<![0-9A-Za-z])(?<![0-9A-Fa-f]:)(?<!::)[0-9A-Fa-f:]*:[0-9A-Fa-f:]*(?![0-9A-Za-z:])")


def iso(ts: datetime) -> str:
    return ts.replace(tzinfo=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def human(seconds: float) -> str:
    s = int(seconds)
    if s < 120: return f"{s} s"
    if s < 7200: return f"{s / 60:.0f} min"
    if s < 172800: return f"{s / 3600:.1f} h"
    return f"{s / 86400:.1f} days"


# ── editors ─────────────────────────────────────────────────────────────────────────────────────────────────────────
class Editors:
    """Per-topic pseudonyms. kind ∈ named|anonymous|temporary|hidden; anonymous carries the masked range (counts as one editor)."""

    def __init__(self):
        self.ids: dict[str, str] = {}
        self.info: dict[str, dict] = {}

    @staticmethod
    def classify(user: str | None) -> tuple[str, str | None]:
        if not user or not user.strip():
            return "hidden", None
        u = user.strip()
        if TEMP.match(u):
            return "temporary", None
        try:
            ip = ipaddress.ip_address(u)
            if ip.version == 4:
                a, b = u.split(".")[:2]
                return "anonymous", f"{a}.{b}.x.x"
            g = ip.exploded.split(":")
            return "anonymous", f"{g[0].lstrip('0') or '0'}:{g[1].lstrip('0') or '0'}:…".upper()
        except ValueError:
            return "named", None

    def of(self, user: str | None) -> str:
        key = user or ""
        if key not in self.ids:
            pid = f"E{len(self.ids) + 1}"
            kind, rng = self.classify(user)
            self.ids[key] = pid
            self.info[pid] = {"kind": kind, **({"masked_range": rng} if rng else {}),
                              **({"bot_like_name": True} if kind == "named" and BOT.search(user or "") else {})}
        return self.ids[key]

    def group(self, pid: str) -> str:
        """Level-3 counting key: anonymous editors in one masked range are one editor."""
        i = self.info[pid]
        return f"range:{i['masked_range']}" if i.get("masked_range") else pid

    def mask_comment(self, comment: str | None, known_users: set[str]) -> str:
        if not comment:
            return ""
        text = WIKILINK.sub(lambda m: m.group(2) if (m.group(2) or "").strip() else m.group(1), comment)
        for u in sorted((u for u in known_users if u and len(u) >= 3 and self.classify(u)[0] in ("named", "temporary")),
                        key=len, reverse=True):
            if u in text:
                text = text.replace(u, self.of(u))
        text = IPV4_IN_TEXT.sub(lambda m: self._mask_ip(m.group(0)), text)
        text = NAME_AFTER.sub(lambda m: m.group(0) if re.fullmatch(r"E\d+|\d[\d.]*|[0-9A-Fa-f:]+", m.group(2))
                              else m.group(1) + "[editor]", text)
        text = IPV6_IN_TEXT.sub(lambda m: self._mask_ip(m.group(0)) if m.group(0).count(":") >= 2 else m.group(0), text)
        return text

    def mask_ips(self, text: str) -> str:
        """Article text (diffs) may carry signatures with IPs: mask them; names in article prose are content and stay."""
        text = IPV4_IN_TEXT.sub(lambda m: self._mask_ip(m.group(0)), text)
        return IPV6_IN_TEXT.sub(lambda m: self._mask_ip(m.group(0)) if m.group(0).count(":") >= 2 else m.group(0), text)

    def _mask_ip(self, s: str) -> str:
        try:
            ipaddress.ip_address(s)
            return self.of(s)  # pseudonym; its masked range is in the editors table
        except ValueError:
            return s


# ── diff rendering ──────────────────────────────────────────────────────────────────────────────────────────────────
def _trim(s: str, n: int) -> str:
    return s if len(s) <= n else s[: n // 2] + " … " + s[-n // 2:]


def word_diff(a: str, b: str) -> str:
    ta, tb = re.findall(r"\s+|[^\s]+", a), re.findall(r"\s+|[^\s]+", b)
    out = []
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, ta, tb, autojunk=False).get_opcodes():
        if op == "equal":
            out.append(_trim("".join(ta[i1:i2]), 240))
        else:
            if i2 > i1: out.append("[-" + "".join(ta[i1:i2]) + "-]")
            if j2 > j1: out.append("{+" + "".join(tb[j1:j2]) + "+}")
    return "".join(out)


def render_diff(old: str, new: str, deleted_lines: list | None = None) -> tuple[str, str, str]:
    """→ (readable diff, added text, removed text). Lines are wikitext paragraphs; changed pairs get a word diff.
    `deleted_lines`, if given, collects paragraphs removed outright (not edited in place)."""
    a, b = old.splitlines(), new.splitlines()
    out, added, removed = [], [], []
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes():
        if op == "equal":
            continue
        if op == "replace" and (i2 - i1) == (j2 - j1):
            for x, y in zip(a[i1:i2], b[j1:j2]):
                out.append("~ " + word_diff(x, y))
                sm = difflib.SequenceMatcher(None, x, y, autojunk=False)
                for o, p1, p2, q1, q2 in sm.get_opcodes():
                    if o in ("replace", "insert"): added.append(y[q1:q2])
                    if o in ("replace", "delete"): removed.append(x[p1:p2])
            continue
        for x in a[i1:i2]:
            if x.strip():
                out.append("- " + _trim(x, 700)); removed.append(x)
                if deleted_lines is not None: deleted_lines.append(x)
        for y in b[j1:j2]:
            if y.strip(): out.append("+ " + _trim(y, 3000)); added.append(y)
    d = "\n".join(out)
    if len(d) > DIFF_CAP:
        d = d[:DIFF_CAP] + f"\n… [diff truncated, {len(d) - DIFF_CAP} more chars]"
    return d, "\n".join(added), "\n".join(removed)


def fingerprint(text: str) -> set[str]:
    words = re.findall(r"\w+", text.lower())
    return {" ".join(words[i:i + 5]) for i in range(max(0, len(words) - 4))}


def jaccard(x: set, y: set) -> float:
    return len(x & y) / len(x | y) if x and y else 0.0


# ── corpus access ───────────────────────────────────────────────────────────────────────────────────────────────────
class Corpus:
    def __init__(self):
        self.c = connect()
        self.text_fetches = 0
        self._text: dict[tuple[int, int], str] = {}

    def rows(self, sql: str, args=()) -> list[dict]:
        with self.c.cursor() as cur:
            cur.execute(sql, args)
            cols = [d[0] for d in cur.description]
            return [dict(zip(cols, r)) for r in cur.fetchall()]

    def revisions(self, page_id: int) -> list[dict]:
        return self.rows("SELECT rev_id, parent_id, ts, `user`, comment, sha1, bytes, minor FROM revision "
                         "WHERE page_id=%s ORDER BY ts, rev_id", (page_id,))

    def text(self, page_id: int, rev_id: int) -> str:
        k = (page_id, rev_id)
        if k not in self._text:
            r = self.rows("SELECT text FROM revision_text WHERE page_id=%s AND rev_id=%s", k)
            self.text_fetches += 1
            self._text[k] = r[0]["text"] if r else ""
            if isinstance(self._text[k], bytes):
                self._text[k] = self._text[k].decode("utf-8", "replace")
        return self._text[k]

    def titles(self, page_ids) -> dict[int, str]:
        ids = list(page_ids)
        if not ids: return {}
        ph = ",".join(["%s"] * len(ids))
        r = self.rows(f"SELECT page_id, title FROM article WHERE lang='fi' AND page_id IN ({ph})", ids)
        got = {x["page_id"]: x["title"] for x in r}
        missing = [i for i in ids if i not in got]
        if missing:  # pages with history but no article row: take the revision title
            ph = ",".join(["%s"] * len(missing))
            for x in self.rows(f"SELECT page_id, MAX(title) title FROM revision WHERE page_id IN ({ph}) GROUP BY page_id", missing):
                got[x["page_id"]] = x["title"]
        return got


def embed(text: str) -> list[float] | None:
    for url in (os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434"), "http://127.0.0.1:11435"):
        try:
            req = urllib.request.Request(url + "/api/embed", data=json.dumps({"model": "bge-m3", "input": text}).encode(),
                                         headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.load(r)["embeddings"][0]
        except Exception:
            continue
    return None


def article_candidates(db: Corpus, topic: dict) -> list[dict]:
    """Union of vector hits (en/fi phrasings, folded to fi via wikidata_id), FULLTEXT hits and the seeds."""
    found: dict[int, dict] = {}

    def add(pid, title, how):
        e = found.setdefault(pid, {"page_id": pid, "title": title, "found_by": []})
        if how not in e["found_by"]: e["found_by"].append(how)

    for q in topic.get("probe_queries", []):
        v = embed(q)
        if v is None:
            print("  ! embedding unavailable, skipping vector probe", file=sys.stderr); break
        for r in db.rows("""SELECT a.lang, a.page_id, a.title, a.wikidata_id, h.d FROM (
                              SELECT lang, page_id, VEC_DISTANCE_COSINE(embedding, VEC_FromText(%s)) d
                              FROM article_vector ORDER BY d LIMIT 30) h JOIN article a USING (lang, page_id) ORDER BY h.d""",
                         (json.dumps(v),)):
            pid, title = r["page_id"], r["title"]
            if r["lang"] != "fi":
                fi = db.rows("SELECT page_id, title FROM article WHERE lang='fi' AND wikidata_id=%s", (r["wikidata_id"],)) if r["wikidata_id"] else []
                if not fi: continue
                pid, title = fi[0]["page_id"], fi[0]["title"]
            add(pid, title, f"vector:{q[:40]}")
    for kw in topic.get("fulltext", []):
        for r in db.rows("SELECT page_id, title FROM article WHERE lang='fi' AND MATCH(title, text) AGAINST (%s IN BOOLEAN MODE) LIMIT 20", (kw,)):
            add(r["page_id"], r["title"], f"fulltext:{kw}")
    for s in topic["seed_pages"]:
        add(s["page_id"], s.get("title", ""), "seed")
    ids = list(found)
    ph = ",".join(["%s"] * len(ids))
    leads = {r["page_id"]: r["lead"] for r in db.rows(
        f"SELECT page_id, LEFT(text, 260) lead FROM article WHERE lang='fi' AND page_id IN ({ph})", ids)}
    for pid, e in found.items():
        e["lead"] = re.sub(r"\s+", " ", leads.get(pid, "") or "")
    return sorted(found.values(), key=lambda e: ("seed" not in e["found_by"], -len(e["found_by"])))


# ── revert runs ─────────────────────────────────────────────────────────────────────────────────────────────────────
def runs_for_page(revs: list[dict]) -> tuple[list[dict], list[dict]]:
    """Identity reverts, null revisions dropped first (CONTRACT §1). → (non-null revisions, runs)."""
    m, prev = [], None
    for r in revs:
        if not (r["sha1"] and prev and r["sha1"] == prev):
            m.append(r)
        prev = r["sha1"]
    last: dict[str, int] = {}
    runs = []
    for k, r in enumerate(m):
        s = r["sha1"]
        if s and s in last and last[s] < k - 1:
            runs.append({"j": last[s], "k": k})
        if s: last[s] = k
    # exact re-insertion chains: a later revision recreating a reverted version re-adds that content
    sha_pos = defaultdict(list)
    for i, r in enumerate(m):
        if r["sha1"]: sha_pos[r["sha1"]].append(i)
    parent = list(range(len(runs)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x

    covering = defaultdict(list)  # position → runs whose reverted range contains it
    for ri, run in enumerate(runs):
        for p in range(run["j"] + 1, run["k"]):
            covering[p].append(ri)
    for ri, run in enumerate(runs):
        s = m[run["k"] - 1]["sha1"]
        for p in sha_pos.get(s, []):
            if p > run["k"]:
                for rj in covering.get(p, []):
                    parent[find(rj)] = find(ri)
    for ri, run in enumerate(runs):
        run["chain"] = find(ri)
    return m, runs


def page_runs(db: Corpus, page_id: int, title: str, ed: Editors) -> tuple[list[dict], dict, list[dict]]:
    revs = db.revisions(page_id)
    m, runs = runs_for_page(revs)
    users = {r["user"] for r in revs if r["user"]}
    revert_ts = [m[r["k"]]["ts"] for r in runs]
    removed_positions = {p for r in runs for p in range(r["j"] + 1, r["k"])}
    undone_by: dict[int, int] = {}
    for r in runs:
        for p in range(r["j"] + 1, r["k"]):
            undone_by.setdefault(p, m[r["k"]]["rev_id"])
    revert_positions = {r["k"] for r in runs}
    out = []
    for i, run in enumerate(runs):
        j, k = run["j"], run["k"]
        edits = m[j + 1:k]
        R, P, L = m[k], m[j], m[k - 1]
        ins_users = [e["user"] for e in edits]
        net = L["bytes"] - P["bytes"]
        survival = (R["ts"] - edits[0]["ts"]).total_seconds()
        rems = {R["user"]}
        self_removal = len(set(ins_users)) == 1 and ins_users[0] == R["user"]
        bot = any(BOT.search(u or "") for u in ins_users)
        blanking = P["bytes"] > 400 and L["bytes"] < 0.3 * P["bytes"]
        near = sum(1 for t in revert_ts if abs((t - R["ts"]).total_seconds()) <= 3 * 86400) - 1
        out.append({
            "page_id": page_id, "title": title, "chain_key": f"{page_id}:{run['chain']}",
            "restored_rev_id": P["rev_id"], "removing_rev_id": R["rev_id"],
            "edits": [{"rev_id": e["rev_id"], "parent_rev_id": e["parent_id"], "ts": iso(e["ts"]), "editor": ed.of(e["user"]),
                       "bytes_delta": e["bytes"] - (m[idx - 1]["bytes"] if idx > 0 else 0),
                       "summary": ed.mask_comment(e["comment"], users)}
                      for idx, e in zip(range(j + 1, k), edits)],
            "remover": ed.of(R["user"]), "removed_at": iso(R["ts"]), "removal_summary": ed.mask_comment(R["comment"], users),
            "net_bytes": net, "survival_s": survival, "survival": human(survival),
            "flags": {"self_removal": self_removal, "bot_inserter": bot, "blanking": blanking,
                      "removal_was_itself_undone": k in removed_positions,
                      **({"removal_undone_by_rev_id": undone_by[k]} if k in undone_by else {}),
                      "first_undone_edit_is_itself_a_revert": (j + 1) in revert_positions,
                      "identity_reverts_on_page_within_3_days": near},
            "_users": set(ins_users) | rems,
        })
    pstats = {"page_id": page_id, "title": title, "revisions": len(revs), "null_revisions": len(revs) - len(m),
              "identity_reverts": len(runs),
              "substantive_runs": sum(1 for r in out if abs(r["net_bytes"]) >= 50 and not r["flags"]["self_removal"]
                                      and not r["flags"]["bot_inserter"] and not r["flags"]["blanking"]),
              "latest_rev_id": revs[-1]["rev_id"] if revs else None}
    # partial removals: big non-revert deletions whose summary is about accuracy/neutrality/sourcing
    run_revs = {m[r["k"]]["rev_id"] for r in runs} | {m[p]["rev_id"] for p in removed_positions}
    partial = []
    for idx in range(1, len(m)):
        r = m[idx]
        drop = m[idx - 1]["bytes"] - r["bytes"]
        why = re.sub(r"/\*.*?\*/", "", r["comment"] or "")
        if r["rev_id"] in run_revs or drop < 150 or not DISPUTE_SUMMARY.search(why):
            continue
        if BOT.search(r["user"] or ""):
            continue
        partial.append({"pos": idx, "rev": r, "parent": m[idx - 1], "drop": drop, "m": m, "users": users})
    return out, pstats, partial


def attribute_insertion(db: Corpus, page_id: int, m: list[dict], upto: int, needle: str) -> int | None:
    """Binary search: first non-null revision before `upto` whose text contains `needle` (≈ the inserting revision)."""
    lo, hi = 0, upto - 1
    if needle not in db.text(page_id, m[hi]["rev_id"]):
        return None
    while lo < hi:
        mid = (lo + hi) // 2
        if needle in db.text(page_id, m[mid]["rev_id"]):
            hi = mid
        else:
            lo = mid + 1
    return lo


def build_partial(db: Corpus, p: dict, ed: Editors) -> dict | None:
    m, idx, R, parent = p["m"], p["pos"], p["rev"], p["parent"]
    deleted: list[str] = []
    diff, _added, _removed = render_diff(db.text(R["_page"], parent["rev_id"]), db.text(R["_page"], R["rev_id"]), deleted)
    lines = sorted((x for x in deleted if len(x.strip()) >= 40 and not x.lstrip().startswith(("|", "{{", "[[Luokka", "<ref"))),
                   key=len, reverse=True)
    if not lines:
        return None
    ins = idiff = None
    for line in lines[:3]:
        needle = line[:200]
        cand = attribute_insertion(db, R["_page"], m, idx, needle)
        if cand is None or cand == 0:
            continue
        d, iadded, _ = render_diff(db.text(R["_page"], m[cand - 1]["rev_id"]), db.text(R["_page"], m[cand]["rev_id"]))
        grew = m[cand]["bytes"] - m[cand - 1]["bytes"]
        if needle[:60] in iadded and grew >= 0.3 * len(needle.encode()):
            ins, idiff = cand, d
            break
    if ins is None:
        return None  # the first revision containing the text did not add it (reformatting, moves): unattributable
    I = m[ins]
    return {
        "page_id": R["_page"], "title": R["_title"], "chain_key": None, "restored_rev_id": None,
        "removing_rev_id": R["rev_id"],
        "edits": [{"rev_id": I["rev_id"], "parent_rev_id": I["parent_id"], "ts": iso(I["ts"]), "editor": ed.of(I["user"]),
                   "bytes_delta": I["bytes"] - m[ins - 1]["bytes"], "summary": ed.mask_comment(I["comment"], p["users"]),
                   "note": "inserting revision found by text search for the longest removed paragraph; check it"}],
        "remover": ed.of(R["user"]), "removed_at": iso(R["ts"]), "removal_summary": ed.mask_comment(R["comment"], p["users"]),
        "net_bytes": -p["drop"], "survival_s": (R["ts"] - I["ts"]).total_seconds(), "survival": human((R["ts"] - I["ts"]).total_seconds()),
        "flags": {"self_removal": I["user"] == R["user"], "bot_inserter": bool(BOT.search(I["user"] or "")), "blanking": False},
        "diff_of_removal": diff, "insertion_diff": idiff, "_users": {I["user"], R["user"]},
    }


def interest(r: dict) -> float:
    f = r["flags"]
    if f["self_removal"] or f["bot_inserter"] or f["blanking"] or r["net_bytes"] < 50:
        return -1
    return math.log10(max(r["net_bytes"], 1)) * math.log10(max(r["survival_s"], 60) / 60 + 1) * \
        (1.5 if r.get("_chain_size", 1) > 1 else 1) * (1.3 if r.get("_editor_runs", 1) > 1 else 1)


def build_pack(db: Corpus, topic: dict) -> dict:
    ed = Editors()
    titles = db.titles(s["page_id"] for s in topic["seed_pages"])
    all_runs, stats, partials = [], [], []
    for s in topic["seed_pages"]:
        pid = s["page_id"]
        rs, st, pa = page_runs(db, pid, titles.get(pid, s.get("title", "")), ed)
        for p in pa:
            p["rev"]["_page"], p["rev"]["_title"] = pid, titles.get(pid, "")
        all_runs += rs; stats.append(st); partials += pa
        print(f"  {titles.get(pid, pid)}: {st['revisions']} revs, {st['identity_reverts']} reverts, "
              f"{st['substantive_runs']} substantive, {len(pa)} partial-removal candidates", file=sys.stderr)
    chain_size = defaultdict(int)
    for r in all_runs: chain_size[r["chain_key"]] += 1
    editor_runs = defaultdict(int)
    for r in all_runs:
        for e in {x["editor"] for x in r["edits"]}: editor_runs[e] += 1
    for r in all_runs:
        r["_chain_size"] = chain_size[r["chain_key"]]
        r["_editor_runs"] = max(editor_runs[x["editor"]] for x in r["edits"])

    rng = random.Random(20)
    forced = set(topic.get("must_include_rev_ids", []))
    picked: list[dict] = []
    per_page = defaultdict(int)

    def take(r, bucket):
        if any(r is x for x in picked): return
        r["bucket"] = bucket; picked.append(r); per_page[r["page_id"]] += 1

    for r in all_runs:
        if forced & {e["rev_id"] for e in r["edits"]} | ({r["removing_rev_id"]} & forced):
            take(r, "likely")
    for r in sorted(all_runs, key=interest, reverse=True):
        if sum(1 for x in picked if x["bucket"] == "likely") >= N_LIKELY or interest(r) <= 0: break
        if per_page[r["page_id"]] < MAX_PER_PAGE: take(r, "likely")
    # pull in the rest of each picked chain (re-insertions belong to one item). Chains > 6 runs are a stable version
    # restored over and over (edit wars, repeated vandalism), not one piece of content: don't pull those.
    for r in list(picked):
        if chain_size[r["chain_key"]] <= 6:
            for x in all_runs:
                if x["chain_key"] == r["chain_key"]: take(x, r["bucket"])
    look = {
        "short-lived substantive": [r for r in all_runs if r["net_bytes"] >= 50 and r["survival_s"] < 600 and not r["flags"]["self_removal"]],
        "self-removal": [r for r in all_runs if r["flags"]["self_removal"] and abs(r["net_bytes"]) >= 30],
        "bot": [r for r in all_runs if r["flags"]["bot_inserter"]],
        "blanking": [r for r in all_runs if r["flags"]["blanking"]],
        "small": [r for r in all_runs if 0 < abs(r["net_bytes"]) < 50 and not r["flags"]["self_removal"]],
        "removal undone": [r for r in all_runs if r["flags"]["removal_was_itself_undone"]],
    }
    quota = {"short-lived substantive": 4, "self-removal": 2, "bot": 1, "blanking": 1, "small": 2, "removal undone": 2}
    for kind, pool in look.items():
        pool = [r for r in pool if not any(r is x for x in picked)]
        for r in rng.sample(pool, min(quota[kind], len(pool))):
            take(r, f"look-alike:{kind}")

    print(f"  fetching texts for {len(picked)} runs …", file=sys.stderr)
    latest = {s["page_id"]: db.text(s["page_id"], s["latest_rev_id"]) for s in stats if s["latest_rev_id"]}
    cands = []
    for r in picked:
        diff, added, removed = render_diff(db.text(r["page_id"], r["restored_rev_id"]), db.text(r["page_id"], r["edits"][-1]["rev_id"]))
        r["diff"] = ed.mask_ips(diff)
        r["_fp"] = fingerprint(added)
        long_added = [x for x in added.splitlines() if len(x.strip()) >= 30]
        r["flags"]["added_text_in_latest_revision"] = bool(long_added) and all(x in latest.get(r["page_id"], "") for x in long_added[:3])
        tmpl = sorted({t.group(1).lower() for t in DISPUTE_TEMPLATES.finditer(added + "\n" + removed)})
        if tmpl: r["flags"]["dispute_templates_in_diff"] = tmpl
        cands.append(r)
    for p in sorted(partials, key=lambda p: -p["drop"])[: N_PARTIAL * 2]:
        if sum(1 for c in cands if c.get("bucket") == "partial-removal") >= N_PARTIAL: break
        c = build_partial(db, p, ed)
        if c:
            c["bucket"] = "partial-removal"; c["diff"] = ed.mask_ips(c.pop("diff_of_removal")); c["insertion_diff"] = ed.mask_ips(c["insertion_diff"]); c["_fp"] = fingerprint(c["diff"]); cands.append(c)
    for a in cands:
        sim = [b for b in cands if b is not a and jaccard(a["_fp"], b["_fp"]) >= 0.4]
        a["similar_to"] = [f"c{cands.index(b) + 1:02d}" for b in sim]
    out = []
    for i, c in enumerate(cands):
        c["cid"] = f"c{i + 1:02d}"
        c["inserting_editors"] = sorted({e["editor"] for e in c["edits"]}, key=lambda x: int(x[1:]))
        c["distinct_inserting_editor_groups"] = len({ed.group(e) for e in c["inserting_editors"]})
        c["same_chain_candidates"] = [f"c{j + 1:02d}" for j, x in enumerate(cands) if x is not c and c.get("chain_key") and x.get("chain_key") == c["chain_key"]]
        c["editor_reverted_runs_in_topic"] = c.pop("_editor_runs", None)
        c["links"] = {"removal": f"https://fi.wikipedia.org/w/index.php?diff=prev&oldid={c['removing_rev_id']}",
                      "insertion": [f"https://fi.wikipedia.org/w/index.php?diff=prev&oldid={e['rev_id']}" for e in c["edits"]]}
        for k in ("_users", "_fp", "_chain_size", "chain_key"): c.pop(k, None)
        out.append({k: c[k] for k in ("cid", "bucket", "page_id", "title", "edits", "inserting_editors",
                                       "distinct_inserting_editor_groups", "remover", "removed_at", "removal_summary",
                                       "restored_rev_id", "removing_rev_id", "net_bytes", "survival", "flags",
                                       "same_chain_candidates", "similar_to", "editor_reverted_runs_in_topic", "links", "diff",
                                       "insertion_diff")
                    if k in c})
    return {
        "topic": {k: topic[k] for k in topic if k not in ("fulltext",)},
        "article_candidates": [a for a in article_candidates(db, topic) if a["page_id"] not in set(topic.get("exclude_pages", []))],
        "page_stats": stats,
        "editors": {k: v for k, v in ed.info.items()
                    if re.search(rf"\b{k}\b", json.dumps(out, ensure_ascii=False))},
        "candidates": out,
        "text_fetches": db.text_fetches,
        "built_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--topic", action="append", default=[])
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--stats", action="store_true", help="run counts for --pages, no text fetches")
    ap.add_argument("--pages", default="")
    a = ap.parse_args()
    db = Corpus()
    if a.stats:
        ed = Editors()
        ids = [int(x) for x in a.pages.split(",") if x]
        t = db.titles(ids)
        print("page_id\ttitle\trevisions\tidentity_reverts\tsubstantive_runs\tpartial_removal_candidates")
        for pid in ids:
            _, st, pa = page_runs(db, pid, t.get(pid, ""), ed)
            print(f"{pid}\t{st['title']}\t{st['revisions']}\t{st['identity_reverts']}\t{st['substantive_runs']}\t{len(pa)}")
        return 0
    topics = json.loads(TOPICS.read_text())["topics"]
    want = [t for t in topics if a.all or t["id"] in a.topic]
    if not want:
        print("no topic selected (--topic <id> | --all)", file=sys.stderr); return 2
    PACKS.mkdir(exist_ok=True)
    for t in want:  # articles with items must not sit under both splits (CONTRACT §4): fence off the other split's seeds
        own = {s["page_id"] for s in t["seed_pages"]}
        t["exclude_pages"] = sorted({s["page_id"] for o in topics if o["split"] != t["split"] for s in o["seed_pages"]} - own)
    for t in want:
        if t["kind"] == "control-out-of-corpus":
            continue
        print(f"▶ {t['id']}", file=sys.stderr)
        db.text_fetches = 0
        pack = build_pack(db, t)
        (PACKS / f"{t['id']}.json").write_text(json.dumps(pack, ensure_ascii=False, indent=1))
        print(f"  ✓ {len(pack['candidates'])} candidates, {len(pack['article_candidates'])} article candidates, "
              f"{pack['text_fetches']} text fetches → packs/{t['id']}.json", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
