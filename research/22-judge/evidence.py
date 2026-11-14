"""Build blind prompt variables for P1.E3 judge and adversarial review (#22).

Turns an incident `(page_id, inserting_rev_ids, removing_rev_ids, other_page_ids=())` into the five prompt
variables consumed by `research/eval/prompts/judge-incident.md` and `adversarial-review.md`:
`article_title`, `insertions`, `diff`, `removals`, `history`.

Standard library only (Python 3.10+), reading `corpus.*` and `research.*` through `research/lib/aot.py`.
"""
from __future__ import annotations

import json
import pathlib
import re
import sys
from datetime import datetime, timedelta, timezone
from typing import Any, Iterable

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

DIFF_LINE_CAP = 600
DIFF_TOTAL_CAP = 6000
DIFF_TRUNCATED_SUFFIX = "… (truncated)"
NL = chr(10)
TAB = chr(9)

BOT_NAME_RE = re.compile(r"(?i)bot\b|bot$")
DISPUTE_TEMPLATE_RE = re.compile(
    r"(?i)\{\{\s*(neutraalius|lähteetön|lähde\?|korjattava|kiistanalainen|tarkista|puolueellinen)"
)
_WIKILINK_RE = re.compile(r"\[\[([^\[\]|]*)(?:\|([^\[\]]*))?\]\]")
_REF_RE = re.compile(r"<ref[^>/]*/>|<ref[^>]*>.*?</ref>", re.DOTALL | re.IGNORECASE)
_USER_WIKILINK_RE = re.compile(
    r"\[\[:?(?:(?:Special:Contributions|Toiminnot:(?:Muokkaukset|Contributions))/([^|\]#]+)"
    r"|(?:User|Käyttäjä):([^|\]#]+))(?:[#|][^\]]*)?\]\]",
    re.IGNORECASE,
)
_TRUNC_USER_WIKILINK_RE = re.compile(
    r"\[\[:?(?:(?:Special:Contributions|Toiminnot:(?:Muokkaukset|Contributions))/([^|\]#]*)"
    r"|(?:User|Käyttäjä):([^|\]#]*)).*$",
    re.IGNORECASE,
)
_PLAIN_TEMPLATE_USER_RE = re.compile(
    r"(?i)(\bkäyttäjän\s+)(?!Editor\s+\d+\b)(?!\[editor\])(?![0-9]+\.[0-9]+\.x\.x)(?![0-9A-F]+:[0-9A-F]+:…)"
    r"(.+?)(\s+(?:tekemään\s+(?:versioon|muutokseen)|muokkaukseen|versioon|tekemä\s+muokkaus)\b)"
)
_PLAIN_TAIL_USER_RE = re.compile(
    r"(?i)(\b(?:viimeisimpään\s+käyttäjän|estetyn\s+käyttäjän|käyttäjä|käyttäjältä|jonka\s+on\s+tehnyt|jonka\s+teki|rv\s+per)\s+)"
    r"(?!Editor\s+\d+\b)(?!\[editor\])(?![0-9]+\.[0-9]+\.x\.x)(?![0-9A-F]+:[0-9A-F]+:…)([^\s,;:()\[\]\".]+)"
)
_PLAIN_BLOCK_EVASION_RE = re.compile(
    r"(?i)(\beston\s+kiertoa\s*\(\s*)(?!Editor\s+\d+\b)(?!\[editor\])([^)]+?)(\s*\))"
)
_PLAIN_NS_USER_RE = re.compile(r"(?i)\b((?:Käyttäjä|User):)(?!Editor\s+\d+\b)(?!\[editor\])([^\s,;:()\[\]\"]+)")
_PLAIN_EN_BY_RE = re.compile(
    r"(?i)(\b(?:version|revision|edits)\s+by\s+)"
    r"(?!Editor\s+\d+\b)(?!\[editor\])(?![0-9]+\.[0-9]+\.x\.x)(?![0-9A-F]+:[0-9A-F]+:…)([^\s,;:()\[\]\"]+)"
)


def incident_key(ids: Iterable[int]) -> str:
    """CONTRACT.md §1: inserting rev ids, ascending, joined with '+'."""
    return "+".join(str(int(r)) for r in sorted({int(x) for x in ids}))


def parse_ts(ts: str | datetime) -> datetime:
    if isinstance(ts, datetime):
        return ts if ts.tzinfo is not None else ts.replace(tzinfo=timezone.utc)
    s = ts.strip()
    if s.endswith("Z"):
        return datetime.strptime(s, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    return datetime.strptime(s, "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc)


def format_iso(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def strip_wikitext_inline(text: str) -> str:
    """Collapse wikilinks and formatting in revision wikitext so `s3_diff` `prose` spans match when kept."""
    out = _REF_RE.sub(" ", text)
    for _ in range(3):
        out = _WIKILINK_RE.sub(lambda m: m.group(2) if (m.group(2) or "").strip() else m.group(1), out)
    out = re.sub(r"'{2,5}", "", out)
    out = re.sub(f"[ {TAB}]+", " ", out)
    return out


def editor_group_and_kind(user: str | None) -> tuple[str, str]:
    """Return `(grouping_key, 'named'|'anonymous')`.

    Named accounts group by exact trimmed name; anonymous IPs group by `aot.user_display(user)` (masked /16 or /32
    prefix), so addresses in one masked subnet share one Editor label and count as one editor.
    """
    if user is None or not user.strip():
        return ("hidden", "anonymous")
    u = user.strip()
    disp = aot.user_display(u)
    if aot.is_anonymous(u):
        return (f"anon:{disp}", "anonymous")
    return (f"named:{u}", "named")


def assign_editors(
    ins_revs: list[dict[str, Any]], rem_revs: list[dict[str, Any]]
) -> tuple[dict[str, tuple[str, str]], dict[str, str]]:
    """Assign `Editor <n>` by first appearance (insertions oldest-first, then removals oldest-first).

    Returns:
      - `group_to_editor`: `grouping_key -> ("Editor <n>", "named"|"anonymous")`
      - `name_to_label`: raw non-IP account name (and underscore form) -> `"Editor <n>"`
    """
    group_to_editor: dict[str, tuple[str, str]] = {}
    name_to_label: dict[str, str] = {}
    for rev in [*ins_revs, *rem_revs]:
        user = rev.get("user")
        gkey, kind = editor_group_and_kind(user)
        if gkey not in group_to_editor:
            label = f"Editor {len(group_to_editor) + 1}"
            group_to_editor[gkey] = (label, kind)
        else:
            label = group_to_editor[gkey][0]
        if user and user.strip():
            u = user.strip()
            disp = aot.user_display(u)
            if not disp.endswith("(anonymous)"):
                name_to_label[u] = label
                if " " in u:
                    name_to_label[u.replace(" ", "_")] = label
    return group_to_editor, name_to_label


def format_summary(
    comment: str | None,
    name_to_label: dict[str, str],
    third_party_users: Iterable[str] = (),
) -> str:
    """Mask IPs/wikilinks via `aot.mask_comment`, replace involved account names with `Editor <n>`, and scrub third-party user mentions."""
    if comment is None or not comment.strip():
        return ""

    def _replace_user_link(m: re.Match[str]) -> str:
        raw_u = (m.group(1) or m.group(2) or "").strip()
        norm_u = raw_u.replace("_", " ")
        if raw_u in name_to_label:
            return name_to_label[raw_u]
        if norm_u in name_to_label:
            return name_to_label[norm_u]
        if norm_u and aot.user_display(norm_u).endswith("(anonymous)"):
            return m.group(0)
        return "[editor]"

    pre = _USER_WIKILINK_RE.sub(_replace_user_link, comment)
    pre = _TRUNC_USER_WIKILINK_RE.sub(_replace_user_link, pre)
    masked = aot.mask_comment(pre)
    single_line = " ".join(masked.replace(TAB, " ").splitlines()).strip()
    single_line = re.sub(r" {2,}", " ", single_line)
    if not single_line:
        return single_line
    if name_to_label:
        keys = sorted((k for k in name_to_label if k), key=len, reverse=True)
        if keys:
            pattern = re.compile("|".join(re.escape(k) for k in keys))
            single_line = pattern.sub(lambda m: name_to_label[m.group(0)], single_line)
    extra = sorted(
        {u.strip() for u in third_party_users if u and len(u.strip()) >= 3 and u.strip() not in name_to_label},
        key=len,
        reverse=True,
    )
    if extra:
        tp_re = re.compile(r"(?<!\w)(?:" + "|".join(re.escape(u) for u in extra) + r")(?!\w)")
        single_line = tp_re.sub("[editor]", single_line)
    single_line = _PLAIN_TEMPLATE_USER_RE.sub(r"\1[editor]\3", single_line)
    single_line = _PLAIN_TAIL_USER_RE.sub(r"\1[editor]", single_line)
    single_line = _PLAIN_BLOCK_EVASION_RE.sub(r"\1[editor]\3", single_line)
    single_line = _PLAIN_NS_USER_RE.sub(r"\1[editor]", single_line)
    single_line = _PLAIN_EN_BY_RE.sub(r"\1[editor]", single_line)
    return single_line


def render_segments_for_rev(rev_id: int, s3_row: dict[str, Any] | None) -> tuple[list[str], bool]:
    """Render one inserting revision's diff lines from its `s3_diff` row. Returns `(lines, is_missing)`."""
    if s3_row is None or s3_row.get("error") or s3_row.get("segments") is None:
        return ([f"(diff not available for rev {rev_id})"], True)

    raw_segments = s3_row["segments"]
    segments: list[dict[str, Any]] = json.loads(raw_segments) if isinstance(raw_segments, str) else list(raw_segments)
    active = [s for s in segments if not s.get("moved")]
    if not active:
        return (["(whitespace or block move only)"], False)

    blocks: dict[tuple[Any, ...], list[dict[str, Any]]] = {}
    for idx, seg in enumerate(active):
        old_p = seg.get("old_paragraph")
        new_p = seg.get("new_paragraph")
        if old_p is not None and new_p is not None:
            bkey: tuple[Any, ...] = ("both", old_p, new_p)
        elif old_p is not None:
            bkey = ("old", old_p)
        elif new_p is not None:
            bkey = ("new", new_p)
        else:
            bkey = ("unindexed", idx)
        blocks.setdefault(bkey, []).append(seg)

    lines: list[str] = []
    for bsegs in blocks.values():
        ops = {s.get("op") for s in bsegs}
        if ops == {"added"}:
            parts = [" ".join((s.get("text") or "").splitlines()) for s in bsegs]
            line = "+ " + " ".join(p for p in parts if p)
        elif ops == {"removed"}:
            parts = [" ".join((s.get("text") or "").splitlines()) for s in bsegs]
            line = "- " + " ".join(p for p in parts if p)
        else:
            parts = []
            for s in bsegs:
                t = " ".join((s.get("text") or "").splitlines())
                if not t:
                    continue
                if s.get("op") == "removed":
                    parts.append(f"[-{t}-]")
                else:
                    parts.append(f"{{+{t}+}}")
            line = "~ " + " ".join(parts)
        if len(line) > DIFF_LINE_CAP:
            line = line[: DIFF_LINE_CAP - 1] + "…"
        lines.append(line)
    return (lines, False)


def render_diff(ins_rev_ids: list[int], s3_by_rev: dict[int, dict[str, Any] | None]) -> tuple[str, int]:
    """Render `diff` across inserting revisions (oldest first). Returns `(diff_text, missing_diff_count)`."""
    multi = len(ins_rev_ids) > 1
    all_lines: list[str] = []
    missing = 0
    for rid in ins_rev_ids:
        if multi:
            all_lines.append(f"@@ rev {rid}")
        rev_lines, is_missing = render_segments_for_rev(rid, s3_by_rev.get(rid))
        if is_missing:
            missing += 1
        all_lines.extend(rev_lines)
    full = NL.join(all_lines)
    if len(full) > DIFF_TOTAL_CAP:
        full = full[: DIFF_TOTAL_CAP - len(DIFF_TRUNCATED_SUFFIX)] + DIFF_TRUNCATED_SUFFIX
    return (full, missing)


def compute_visible_for(ins_revs: list[dict[str, Any]], rem_revs: list[dict[str, Any]]) -> str:
    """Sum over each insertion -> the next removal after it (merging overlapping spans on consecutive edits)."""
    intervals: list[tuple[datetime, datetime]] = []
    for ins in ins_revs:
        ins_key = (ins["dt"], ins["rev_id"])
        next_rem = next((r for r in rem_revs if (r["dt"], r["rev_id"]) > ins_key), None)
        if next_rem is not None and next_rem["dt"] >= ins["dt"]:
            intervals.append((ins["dt"], next_rem["dt"]))

    intervals.sort(key=lambda iv: (iv[0], iv[1]))
    merged: list[tuple[datetime, datetime]] = []
    for start, end in intervals:
        if merged and start <= merged[-1][1]:
            if end > merged[-1][1]:
                merged[-1] = (merged[-1][0], end)
        else:
            merged.append((start, end))

    total_seconds = max(0, int(sum((end - start).total_seconds() for start, end in merged)))
    days, rem = divmod(total_seconds, 86400)
    hours, rem = divmod(rem, 3600)
    minutes = rem // 60
    if days > 0:
        return f"{days} days, {hours} hours"
    return f"{hours} hours, {minutes} minutes"


def longest_added_prose_segment(ins_rev_ids: list[int], s3_by_rev: dict[int, dict[str, Any] | None]) -> str:
    """Return the longest non-moved added `prose` segment >= 20 chars across `ins_rev_ids`, or '' if none."""
    best = ""
    for rid in ins_rev_ids:
        row = s3_by_rev.get(rid)
        if not row or row.get("error") or not row.get("segments"):
            continue
        segs = json.loads(row["segments"]) if isinstance(row["segments"], str) else row["segments"]
        for s in segs:
            if s.get("op") == "added" and not s.get("moved") and s.get("kind") == "prose":
                prose = s.get("prose") or ""
                if len(prose) >= 20 and len(prose) > len(best):
                    best = prose
    return best


def has_dispute_template_in_diffs(
    rev_ids: Iterable[int], s3_by_rev: dict[int, dict[str, Any] | None]
) -> bool:
    """True if any added template segment in `rev_ids` matches `DISPUTE_TEMPLATE_RE`."""
    for rid in rev_ids:
        row = s3_by_rev.get(rid)
        if not row or row.get("error") or not row.get("segments"):
            continue
        segs = json.loads(row["segments"]) if isinstance(row["segments"], str) else row["segments"]
        for s in segs:
            if s.get("op") == "added" and not s.get("moved") and s.get("kind") == "template":
                if DISPUTE_TEMPLATE_RE.search(s.get("text") or ""):
                    return True
    return False


class CorpusStore:
    """Indexed, per-page cached reader for `corpus.*` and `research.*` (or offline synthetic fixtures in tests)."""

    def __init__(self, *, offline: bool = False) -> None:
        self.offline = offline
        self.titles: dict[int, str] = {}
        self.revisions: dict[int, dict[str, Any]] = {}
        self.prev_bytes: dict[int, int] = {}
        self.s3_edits: dict[int, dict[str, Any] | None] = {}
        self.page_s1_reverts: dict[int, list[datetime]] = {}
        self.bot_users: dict[str, bool] = {}
        self.removal_reasons: dict[int, set[str]] = {}
        self.page_users: dict[int, set[str]] = {}
        self.latest_rev_id: dict[int, int | None] = {}
        self.latest_texts: dict[int, tuple[str, str]] = {}  # page_id -> (raw_wikitext, stripped_wikitext)

    @staticmethod
    def _chunks(items: list[Any], size: int) -> Iterable[list[Any]]:
        for i in range(0, len(items), size):
            yield items[i : i + size]

    def ensure_titles(self, page_ids: Iterable[int]) -> None:
        todo = sorted({int(p) for p in page_ids if int(p) not in self.titles})
        if not todo or self.offline:
            return
        for batch in self._chunks(todo, 200):
            for r in aot.sql(
                f"SELECT page_id, title FROM corpus.article WHERE lang='fi' AND page_id IN ({aot.ints(batch)})"
            ):
                self.titles[int(r["page_id"])] = r["title"] or ""
        still_missing = [p for p in todo if p not in self.titles]
        for batch in self._chunks(still_missing, 200):
            for r in aot.sql(
                f"SELECT page_id, MAX(title) AS title FROM corpus.revision WHERE page_id IN ({aot.ints(batch)}) GROUP BY page_id"
            ):
                self.titles[int(r["page_id"])] = r["title"] or ""
        for p in todo:
            self.titles.setdefault(p, f"Page {p}")

    def ensure_s3_edits(self, rev_ids: Iterable[int]) -> None:
        todo = sorted({int(r) for r in rev_ids if int(r) not in self.s3_edits})
        if not todo or self.offline:
            return
        for batch in self._chunks(todo, 400):
            found: set[int] = set()
            for r in aot.sql(
                "SELECT rev_id, page_id, old_rev_id, new_rev_id, error, segments "
                f"FROM research.s3_diff WHERE kind='edit' AND rev_id IN ({aot.ints(batch)})"
            ):
                rid = int(r["rev_id"])
                found.add(rid)
                segs = json.loads(r["segments"]) if r.get("segments") else []
                self.s3_edits[rid] = {
                    "rev_id": rid,
                    "page_id": int(r["page_id"]),
                    "old_rev_id": int(r["old_rev_id"]) if r.get("old_rev_id") is not None else None,
                    "new_rev_id": int(r["new_rev_id"]) if r.get("new_rev_id") is not None else rid,
                    "error": r.get("error"),
                    "segments": segs,
                }
            for rid in batch:
                if rid not in found:
                    self.s3_edits[rid] = None

    def ensure_revisions(self, page_ids: Iterable[int], rev_ids: Iterable[int]) -> None:
        pages = sorted({int(p) for p in page_ids})
        todo = sorted({int(r) for r in rev_ids if int(r) not in self.revisions})
        if not pages or not todo or self.offline:
            return
        p_in = aot.ints(pages)
        for batch in self._chunks(todo, 400):
            for r in aot.sql(
                "SELECT page_id, rev_id, parent_id, title, ts, user, anonymous, comment, bytes "
                f"FROM corpus.revision WHERE page_id IN ({p_in}) AND rev_id IN ({aot.ints(batch)})"
            ):
                rid = int(r["rev_id"])
                b = int(r["bytes"] or 0)
                self.revisions[rid] = {
                    "page_id": int(r["page_id"]),
                    "rev_id": rid,
                    "parent_id": int(r["parent_id"]) if r.get("parent_id") else None,
                    "title": r.get("title") or "",
                    "ts": r["ts"],
                    "dt": parse_ts(r["ts"]),
                    "user": r.get("user"),
                    "anonymous": int(r.get("anonymous") or 0),
                    "comment": r.get("comment"),
                    "bytes": b,
                }
                self.prev_bytes[rid] = b

        prev_needed: set[int] = set()
        prev_pages: set[int] = set(pages)
        for rid in rev_ids:
            rev = self.revisions.get(int(rid))
            if not rev:
                continue
            prev_pages.add(rev["page_id"])
            s3 = self.s3_edits.get(int(rid))
            prev_id = s3["old_rev_id"] if s3 is not None else rev["parent_id"]
            if prev_id and prev_id not in self.prev_bytes:
                prev_needed.add(int(prev_id))

        if prev_needed:
            pp_in = aot.ints(sorted(prev_pages))
            for batch in self._chunks(sorted(prev_needed), 400):
                for r in aot.sql(
                    f"SELECT rev_id, bytes FROM corpus.revision WHERE page_id IN ({pp_in}) AND rev_id IN ({aot.ints(batch)})"
                ):
                    self.prev_bytes[int(r["rev_id"])] = int(r["bytes"] or 0)
            for pid in prev_needed:
                self.prev_bytes.setdefault(pid, 0)

    def ensure_s1_reverts(self, page_ids: Iterable[int]) -> None:
        todo = sorted({int(p) for p in page_ids if int(p) not in self.page_s1_reverts})
        if not todo or self.offline:
            return
        for batch in self._chunks(todo, 200):
            for p in batch:
                self.page_s1_reverts.setdefault(p, [])
            for r in aot.sql(
                "SELECT page_id, revert_ts FROM research.s1_revert "
                f"WHERE page_id IN ({aot.ints(batch)}) AND class NOT IN ('excluded', 'bot')"
            ):
                self.page_s1_reverts[int(r["page_id"])].append(parse_ts(r["revert_ts"]))

    def ensure_bots(self, users: Iterable[str | None]) -> None:
        todo = sorted({u.strip() for u in users if u and u.strip() and u.strip() not in self.bot_users})
        if not todo or self.offline:
            return
        for batch in self._chunks(todo, 300):
            in_sql = ",".join(aot.quote(u) for u in batch)
            for r in aot.sql(f"SELECT user, is_bot FROM research.s6_editor_profile WHERE user IN ({in_sql})"):
                if r.get("user") is not None:
                    self.bot_users[r["user"]] = bool(int(r.get("is_bot") or 0))
            for u in batch:
                self.bot_users.setdefault(u, False)

    def ensure_removal_reasons(self, rem_rev_ids: Iterable[int]) -> None:
        todo = sorted({int(r) for r in rem_rev_ids if int(r) not in self.removal_reasons})
        if not todo or self.offline:
            return
        for batch in self._chunks(todo, 400):
            for rid in batch:
                self.removal_reasons.setdefault(rid, set())
            in_sql = aot.ints(batch)
            for r in aot.sql(
                f"SELECT reverting_rev_id, removal_reasons FROM research.s5_revert_reason WHERE reverting_rev_id IN ({in_sql})"
            ):
                rid = int(r["reverting_rev_id"])
                for tok in (r.get("removal_reasons") or "").split(","):
                    if tok.strip():
                        self.removal_reasons[rid].add(tok.strip())
            for r in aot.sql(f"SELECT rev_id, reasons FROM research.s5_summary WHERE rev_id IN ({in_sql})"):
                rid = int(r["rev_id"])
                for tok in (r.get("reasons") or "").split(","):
                    if tok.strip():
                        self.removal_reasons[rid].add(tok.strip())

    def ensure_page_users(self, page_ids: Iterable[int]) -> None:
        todo = sorted({int(p) for p in page_ids if int(p) not in self.page_users})
        if not todo or self.offline:
            return
        for batch in self._chunks(todo, 100):
            for p in batch:
                self.page_users.setdefault(p, set())
            for r in aot.sql(
                "SELECT DISTINCT page_id, user FROM corpus.revision "
                f"WHERE page_id IN ({aot.ints(batch)}) AND anonymous = 0 AND user IS NOT NULL"
            ):
                u = (r.get("user") or "").strip()
                if len(u) >= 3:
                    self.page_users[int(r["page_id"])].add(u)

    def ensure_latest_texts(self, page_ids: Iterable[int]) -> None:
        todo = sorted({int(p) for p in page_ids if int(p) not in self.latest_texts})
        if not todo or self.offline:
            return
        need_rev = [p for p in todo if p not in self.latest_rev_id]
        for batch in self._chunks(need_rev, 200):
            p_in = aot.ints(batch)
            for r in aot.sql(
                "SELECT r.page_id, MAX(r.rev_id) AS rev_id FROM corpus.revision r "
                f"JOIN (SELECT page_id, MAX(ts) AS max_ts FROM corpus.revision WHERE page_id IN ({p_in}) GROUP BY page_id) m "
                "ON r.page_id = m.page_id AND r.ts = m.max_ts GROUP BY r.page_id"
            ):
                self.latest_rev_id[int(r["page_id"])] = int(r["rev_id"])
            for p in batch:
                self.latest_rev_id.setdefault(p, None)

        pairs = [(p, self.latest_rev_id[p]) for p in todo if self.latest_rev_id.get(p) is not None]
        for p in todo:
            if self.latest_rev_id.get(p) is None:
                self.latest_texts[p] = ("", "")
        for batch in self._chunks(pairs, 12):
            tuples_sql = ",".join(f"({int(p)},{int(r)})" for p, r in batch)
            for row in aot.sql(
                f"SELECT page_id, text FROM corpus.revision_text WHERE (page_id, rev_id) IN ({tuples_sql})"
            ):
                pid = int(row["page_id"])
                raw = row.get("text") or ""
                self.latest_texts[pid] = (raw, strip_wikitext_inline(raw))
            for p, _ in batch:
                self.latest_texts.setdefault(p, ("", ""))

    def prefetch(self, incidents: Iterable[dict[str, Any]]) -> None:
        """Batch-load corpus/research rows for a list of incidents using indexed queries only."""
        inc_list = list(incidents)
        if not inc_list or self.offline:
            return
        primary_pages = {int(it["page_id"]) for it in inc_list}
        all_pages = set(primary_pages)
        ins_revs: set[int] = set()
        rem_revs: set[int] = set()
        for it in inc_list:
            for op in it.get("other_page_ids") or ():
                all_pages.add(int(op))
            ins_revs.update(int(r) for r in it["inserting_rev_ids"])
            rem_revs.update(int(r) for r in it["removing_rev_ids"])

        all_revs = ins_revs | rem_revs
        self.ensure_titles(primary_pages)
        self.ensure_s3_edits(all_revs)
        self.ensure_revisions(all_pages, all_revs)
        self.ensure_page_users(all_pages)
        self.ensure_s1_reverts(primary_pages)
        ins_users = [self.revisions[r]["user"] for r in ins_revs if r in self.revisions]
        self.ensure_bots(ins_users)
        self.ensure_removal_reasons(rem_revs)

        pages_needing_latest = {
            int(it["page_id"])
            for it in inc_list
            if longest_added_prose_segment([int(r) for r in it["inserting_rev_ids"]], self.s3_edits)
        }
        if pages_needing_latest:
            self.ensure_latest_texts(pages_needing_latest)


DEFAULT_STORE = CorpusStore()


def build_with_meta(
    page_id: int,
    inserting_rev_ids: Iterable[int],
    removing_rev_ids: Iterable[int],
    other_page_ids: Iterable[int] = (),
    *,
    store: CorpusStore | None = None,
) -> tuple[dict[str, str], dict[str, Any]]:
    """Build the five prompt variables plus metadata (`missing_diffs`, `involved_users`)."""
    st = store if store is not None else DEFAULT_STORE
    pid = int(page_id)
    ins_ids = [int(r) for r in inserting_rev_ids]
    rem_ids = [int(r) for r in removing_rev_ids]
    other_pids = [int(p) for p in (other_page_ids or ())]
    all_pages = {pid, *other_pids}
    all_rev_ids = set(ins_ids) | set(rem_ids)

    st.ensure_titles([pid])
    st.ensure_s3_edits(all_rev_ids)
    st.ensure_revisions(all_pages, all_rev_ids)
    st.ensure_page_users(all_pages)
    st.ensure_s1_reverts([pid])

    ins_revs = sorted(
        (st.revisions[r] for r in set(ins_ids) if r in st.revisions),
        key=lambda r: (r["dt"], r["rev_id"]),
    )
    rem_revs = sorted(
        (st.revisions[r] for r in set(rem_ids) if r in st.revisions),
        key=lambda r: (r["dt"], r["rev_id"]),
    )
    st.ensure_bots([r.get("user") for r in ins_revs])
    st.ensure_removal_reasons([r["rev_id"] for r in rem_revs])

    group_to_editor, name_to_label = assign_editors(ins_revs, rem_revs)
    article_title = st.titles.get(pid, f"Page {pid}")
    third_party: set[str] = set()
    for p in all_pages:
        for u in st.page_users.get(p, ()):
            if u not in name_to_label and u not in article_title:
                third_party.add(u)

    def _rev_line(rev: dict[str, Any]) -> str:
        rid = rev["rev_id"]
        gkey, _ = editor_group_and_kind(rev.get("user"))
        ed_label, ed_kind = group_to_editor[gkey]
        s3 = st.s3_edits.get(rid)
        if s3 is not None:
            prev_id = s3.get("old_rev_id")
        else:
            prev_id = rev.get("parent_id")
        prev_b = st.prev_bytes.get(prev_id, 0) if prev_id else 0
        delta = int(rev["bytes"]) - int(prev_b)
        summary = format_summary(rev.get("comment"), name_to_label, third_party)
        return f'{rid} · {format_iso(rev["dt"])} · {ed_label} ({ed_kind}) · {delta:+d} B · summary: "{summary}"'

    insertions_str = NL.join(_rev_line(r) for r in ins_revs)
    removals_str = NL.join(_rev_line(r) for r in rem_revs)

    ordered_ins_ids = [r["rev_id"] for r in ins_revs] or sorted(set(ins_ids))
    diff_str, missing_diffs = render_diff(ordered_ins_ids, st.s3_edits)

    # Computed history signals (strict 10-key order)
    visible_for = compute_visible_for(ins_revs, rem_revs)

    if rem_revs:
        first_rem_key = (rem_revs[0]["dt"], rem_revs[0]["rev_id"])
        put_backs = sum(1 for r in ins_revs if (r["dt"], r["rev_id"]) > first_rem_key)
    else:
        put_backs = 0

    ins_group_keys = {editor_group_and_kind(r.get("user"))[0] for r in ins_revs}
    distinct_ins_editors = len(ins_group_keys)
    articles_count = len(all_pages)

    reverts_3d = 0
    if ins_revs:
        t0 = ins_revs[0]["dt"]
        t_end = t0 + timedelta(days=3)
        reverts_3d = sum(1 for rts in st.page_s1_reverts.get(pid, []) if t0 <= rts <= t_end)

    longest_prose = longest_added_prose_segment(ordered_ins_ids, st.s3_edits)
    still_in_latest = False
    if longest_prose:
        st.ensure_latest_texts([pid])
        raw_latest, stripped_latest = st.latest_texts.get(pid, ("", ""))
        still_in_latest = (longest_prose in raw_latest) or (longest_prose in stripped_latest)

    bot_like = any(
        bool(BOT_NAME_RE.search(r.get("user") or "")) or st.bot_users.get((r.get("user") or "").strip(), False)
        for r in ins_revs
    )

    ins_raw_users = {(r.get("user") or "").strip() for r in ins_revs if (r.get("user") or "").strip()}
    rem_raw_users = {(r.get("user") or "").strip() for r in rem_revs if (r.get("user") or "").strip()}
    same_account = bool(ins_raw_users) and bool(rem_raw_users) and len(ins_raw_users) == 1 and ins_raw_users == rem_raw_users

    dispute_tmpl = has_dispute_template_in_diffs(
        [*ordered_ins_ids, *(r["rev_id"] for r in rem_revs)], st.s3_edits
    )

    reasons: set[str] = set()
    for r in rem_revs:
        reasons.update(st.removal_reasons.get(r["rev_id"], set()))
    reasons_str = ", ".join(sorted(reasons)) if reasons else "none"

    def _b(v: bool) -> str:
        return "true" if v else "false"

    history_lines = [
        f"visible_for: {visible_for}",
        f"times_put_back_after_removal: {put_backs}",
        f"distinct_inserting_editors: {distinct_ins_editors}",
        f"articles_with_same_content: {articles_count}",
        f"identity_reverts_on_page_within_3_days: {reverts_3d}",
        f"added_text_still_in_latest_revision: {_b(still_in_latest)}",
        f"inserter_bot_like_name: {_b(bot_like)}",
        f"same_account_removed_it: {_b(same_account)}",
        f"dispute_templates_in_diff: {_b(dispute_tmpl)}",
        f"removal_reasons_from_summaries: {reasons_str}",
    ]

    vars_dict = {
        "article_title": st.titles.get(pid, f"Page {pid}"),
        "insertions": insertions_str,
        "diff": diff_str,
        "removals": removals_str,
        "history": NL.join(history_lines),
    }
    involved_users = [r.get("user") for r in [*ins_revs, *rem_revs] if r.get("user")]
    meta = {"missing_diffs": missing_diffs, "involved_users": involved_users}
    return vars_dict, meta


def build(
    page_id: int,
    inserting_rev_ids: Iterable[int],
    removing_rev_ids: Iterable[int],
    other_page_ids: Iterable[int] = (),
    *,
    store: CorpusStore | None = None,
) -> dict[str, str]:
    """Return the five prompt variables: `article_title`, `insertions`, `diff`, `removals`, `history`."""
    vars_dict, _ = build_with_meta(
        page_id, inserting_rev_ids, removing_rev_ids, other_page_ids, store=store
    )
    return vars_dict
