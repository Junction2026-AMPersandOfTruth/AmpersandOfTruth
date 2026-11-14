"""Detector `unsourced_addition` (P1.S8 #35, group `refs`).

Flags substantive added prose (>= 80 chars) with no added reference or inline
source link, in a paragraph or section where the parent text already had
references (excluding bibliography/external-links appendix sections).
"""
from __future__ import annotations

import collections
import re

KIND = "unsourced_addition"
MIN_PROSE_CHARS = 80
MIN_NOVEL_WORD_CHARS = 60
_LF = chr(10)
_CR = chr(13)

_REF_OR_CITE_RE = re.compile(
    r"<ref[\s>/]|\{\{\s*(?:kirjaviite|verkkoviite|lehtiviite|jaksoviite|cite\s+\w+|ref|sfn|harvnb)\b",
    re.I,
)
_EXT_URL_RE = re.compile(r"https?://", re.I)
_HEADING_RE = re.compile(r"^(=+)\s*(.*?)\s*\1\s*$")
_INTERWIKI_RE = re.compile(r"[a-z]{2,3}(?:-[a-z0-9]+)*|simple|commons")

_APPENDIX_SECTIONS = frozenset({
    "lähteet",
    "viitteet",
    "lähdeviitteet",
    "kirjallisuutta",
    "kirjallisuus",
    "kirjat, aikakauslehdet",
    "aiheesta muualla",
    "katso myös",
    "muuta",
    "linkit",
    "ulkoiset linkit",
})


def _split_wiki_blocks(text: str) -> list[tuple[int, bool, str]]:
    """Mirror `WikitextBlocks.Split` so paragraph indices align with `wikidiff`."""
    s = (text or "").replace(_CR + _LF, _LF).replace(_CR, _LF)
    blocks: list[tuple[int, bool, str]] = []
    templates = 0
    tables = 0
    comment = False
    ref = False
    start = -1
    end = -1
    lines = 0
    single = False
    heading = False

    def is_open() -> bool:
        return templates > 0 or tables > 0 or comment or ref

    def close() -> None:
        nonlocal start, end
        if start < 0:
            return
        e = end
        while e > start and s[e - 1].isspace():
            e -= 1
        if e > start:
            blocks.append((len(blocks), heading, s[start:e]))
        start = -1

    def link_is_non_prose(inner: str) -> bool:
        stop = len(inner)
        for ch in ("|", "]"):
            idx = inner.find(ch)
            if 0 <= idx < stop:
                stop = idx
        target = inner[:stop].strip()
        colon = target.find(":")
        if colon <= 0:
            return False
        ns = target[:colon].strip().lower().replace("_", " ")
        if ns in ("luokka", "category", "tiedosto", "kuva", "file", "image"):
            return True
        return bool(_INTERWIKI_RE.fullmatch(ns))

    def classify(line: str) -> str:
        trimmed = line.rstrip()
        if len(trimmed) >= 3 and trimmed[0] == "=" and trimmed[-1] == "=":
            return "heading"
        if line and line[0] in ("*", "#", ":", ";"):
            return "single"
        if line.startswith(("{{", "{|", "<!--", "__", "----")):
            return "single"
        if line.startswith("[[") and link_is_non_prose(line[2:]):
            return "single"
        return "text"

    def feed(line: str) -> None:
        nonlocal templates, tables, comment, ref
        t = line.lstrip()
        if not comment and t.startswith("{|"):
            tables += 1
        elif not comment and t.startswith("|}") and tables > 0:
            tables -= 1
        k = 0
        n = len(line)
        while k < n:
            if comment:
                ci = line.find("-->", k)
                if ci < 0:
                    return
                comment = False
                k = ci + 3
                continue
            m = re.search(r"[<{}]", line[k:])
            if not m:
                return
            k += m.start()
            rest = line[k:]
            if rest.startswith("<!--"):
                comment = True
                k += 4
            elif rest.startswith("{{"):
                templates += 1
                k += 2
            elif rest.startswith("}}"):
                if templates > 0:
                    templates -= 1
                k += 2
            elif len(rest) > 4 and rest[:4].lower() == "<ref" and (rest[4] in (">", "/") or rest[4].isspace()):
                gt = rest.find(">")
                if gt < 0:
                    ref = True
                    return
                if rest[gt - 1] != "/":
                    ref = True
                k += gt + 1
            elif rest[:5].lower() == "</ref":
                ref = False
                k += 5
            else:
                k += 1

    pos = 0
    while True:
        nl = s.find(_LF, pos)
        le = len(s) if nl < 0 else nl
        line = s[pos:le]
        blank = line == "" or line.isspace()
        if start >= 0 and is_open():
            if blank and lines > 300:
                templates = tables = 0
                comment = ref = False
                close()
            else:
                feed(line)
                end = le
                lines += 1
                if single and not is_open():
                    close()
        elif blank:
            close()
        else:
            kind = classify(line)
            if kind != "text" or single:
                close()
            if start < 0:
                start = pos
                lines = 0
                single = kind != "text"
                heading = kind == "heading"
            feed(line)
            end = le
            lines += 1
            if single and not is_open():
                close()
        if nl < 0:
            break
        pos = nl + 1
    close()
    return blocks


def _map_sections(
    blocks: list[tuple[int, bool, str]],
) -> tuple[dict[int, int], dict[int, tuple[str, str]], dict[int, list[int]]]:
    top_h = "<lead>"
    cur_h = "<lead>"
    sec_id = 0
    sec_blocks: dict[int, list[int]] = collections.defaultdict(list)
    sec_titles: dict[int, tuple[str, str]] = {0: ("<lead>", "<lead>")}
    blk_sec: dict[int, int] = {}
    for idx, is_h, txt in blocks:
        if is_h:
            m = _HEADING_RE.match(txt.strip())
            if m:
                lvl = len(m.group(1))
                title = re.sub(r"\[\[([^|\]]+\|)?([^\]]+)\]\]", r"\2", m.group(2)).strip().lower()
                if lvl <= 2:
                    top_h = title
                cur_h = title
                sec_id += 1
                sec_titles[sec_id] = (top_h, cur_h)
        blk_sec[idx] = sec_id
        sec_blocks[sec_id].append(idx)
    return blk_sec, sec_titles, sec_blocks


def detect(cand: dict) -> list[dict]:
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []

    segs = [s for s in (diff.get("segments") or []) if not s.get("moved")]
    if not segs:
        return []

    old_text = cand.get("old") or ""
    new_text = cand.get("new") or ""
    ob = _split_wiki_blocks(old_text)
    nb = _split_wiki_blocks(new_text)
    o_blk_sec, o_sec_titles, o_sec_blocks = _map_sections(ob)
    n_blk_sec, n_sec_titles, _ = _map_sections(nb)

    old_sec_has_ref: dict[int, bool] = {}
    old_title_has_ref: dict[str, bool] = collections.defaultdict(bool)
    for sid, bidxs in o_sec_blocks.items():
        has_r = any(bool(_REF_OR_CITE_RE.search(ob[bi][2])) for bi in bidxs)
        old_sec_has_ref[sid] = has_r
        if has_r:
            _, cur_t = o_sec_titles[sid]
            old_title_has_ref[cur_t] = True

    by_np: dict[int, dict] = collections.defaultdict(
        lambda: {"add_prose": [], "rem_prose": [], "add_ref": 0, "old_ps": set()}
    )
    sec_added_ref: set[int] = set()

    for s in segs:
        np = s.get("new_paragraph")
        if np is None:
            continue
        nsid = n_blk_sec.get(np)
        stext = s.get("text") or ""
        if s.get("op") == "added":
            if s.get("kind") == "reference" or _REF_OR_CITE_RE.search(stext) or _EXT_URL_RE.search(stext):
                by_np[np]["add_ref"] += 1
                if nsid is not None:
                    sec_added_ref.add(nsid)
            if s.get("kind") == "prose":
                by_np[np]["add_prose"].append(s)
            if s.get("old_paragraph") is not None:
                by_np[np]["old_ps"].add(s["old_paragraph"])
        elif s.get("op") == "removed":
            if s.get("kind") == "prose":
                by_np[np]["rem_prose"].append(s)
            if s.get("old_paragraph") is not None:
                by_np[np]["old_ps"].add(s["old_paragraph"])

    signals: list[dict] = []
    for np, info in sorted(by_np.items()):
        if not info["add_prose"] or info["add_ref"] > 0:
            continue
        if np < len(nb):
            nsid = n_blk_sec[np]
            top_t, cur_t = n_sec_titles[nsid]
            if top_t in _APPENDIX_SECTIONS or cur_t in _APPENDIX_SECTIONS:
                continue
            if nb[np][1] or nb[np][2].lstrip().startswith(("{|", "{{")):
                continue
            if nsid in sec_added_ref:
                continue

        added_chars = sum(len(s.get("prose") or "") for s in info["add_prose"])
        if added_chars < MIN_PROSE_CHARS:
            continue

        add_words = re.findall(r"\w{3,}", " ".join((s.get("prose") or "").lower() for s in info["add_prose"]))
        rem_word_set = set(
            re.findall(r"\w{3,}", " ".join((s.get("prose") or "").lower() for s in info["rem_prose"]))
        )
        novel_chars = sum(len(w) + 1 for w in add_words if w not in rem_word_set)
        if novel_chars < MIN_NOVEL_WORD_CHARS:
            continue

        parent_had_refs = False
        if info["old_ps"] and ob:
            for op in info["old_ps"]:
                if op < len(ob) and _REF_OR_CITE_RE.search(ob[op][2]):
                    parent_had_refs = True
                osid = o_blk_sec.get(op)
                if osid is not None and old_sec_has_ref.get(osid):
                    parent_had_refs = True
        elif np < len(nb) and ob:
            nsid = n_blk_sec[np]
            _, cur_t = n_sec_titles[nsid]
            if old_title_has_ref.get(cur_t):
                parent_had_refs = True

        if not parent_had_refs:
            continue

        best_seg = max(info["add_prose"], key=lambda s: len(s.get("prose") or ""))
        evidence = (best_seg.get("text") or best_seg.get("prose") or "")[:200]
        label = f"Added {added_chars} chars of unsourced prose in a referenced section"
        signals.append({"kind": KIND, "label": label, "evidence": evidence})

    return signals
