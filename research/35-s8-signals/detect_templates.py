"""Detector `maintenance_template` (P1.S8 #35, group `refs`).

Flags Finnish Wikipedia dispute/sourcing/maintenance templates added or removed
(e.g. `{{Neutraalius}}`, `{{Lähteetön}}`, `{{Lähde}}`, `{{Lähde?}}`,
`{{Lähde tarkemmin}}`, `{{Kiistanalainen}}`, `{{Kenen mukaan}}`, `{{Selvennä}}`,
`{{Tarkista}}`, `{{Korjattava/...}}`). Removing such a template without other
changes to that section is flagged as the strong variant in `label`.
"""
from __future__ import annotations

import re

KIND = "maintenance_template"
_LF = chr(10)
_CR = chr(13)

MAINTENANCE_TEMPLATES = frozenset({
    "neutraalius",
    "pov",
    "npov",
    "puolueellinen",
    "kiistanalainen",
    "ristiriita",
    "lähteetön",
    "lähteetön osio",
    "viitteetön",
    "lähde",
    "lähde?",
    "lähde tarkemmin",
    "parempi lähde",
    "tarkista",
    "tarkistettava",
    "tarkenna",
    "kenen mukaan",
    "selvennä",
    "korjattava",
})
MAINTENANCE_PREFIXES = ("korjattava/", "lähteetön/", "lähde/")

_TPL_SPAN_RE = re.compile(r"\{\{\s*([^|}<>\x0a]+)[^{}]*\}\}", re.S)
_INTERWIKI_RE = re.compile(r"[a-z]{2,3}(?:-[a-z0-9]+)*|simple|commons")


def _find_templates(text: str) -> list[tuple[str, str, str]]:
    found: list[tuple[str, str, str]] = []
    for m in _TPL_SPAN_RE.finditer(text or ""):
        raw_name = m.group(1).strip().replace("_", " ")
        norm_name = " ".join(raw_name.lower().split())
        if norm_name in MAINTENANCE_TEMPLATES or norm_name.startswith(MAINTENANCE_PREFIXES):
            found.append((norm_name, raw_name, m.group(0)))
    return found


def _split_blocks_with_sections(text: str) -> tuple[list[tuple[int, bool, str]], dict[int, int]]:
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

    sec_id = 0
    blk_sec: dict[int, int] = {}
    for idx, is_h, _ in blocks:
        if is_h:
            sec_id += 1
        blk_sec[idx] = sec_id
    return blocks, blk_sec


def detect(cand: dict) -> list[dict]:
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []

    segs = [s for s in (diff.get("segments") or []) if not s.get("moved")]
    if not segs:
        return []

    ob, o_sec = _split_blocks_with_sections(cand.get("old") or "")
    _, n_sec = _split_blocks_with_sections(cand.get("new") or "")

    rem_t: list[tuple[int, dict, str, str, str]] = []
    add_t: list[tuple[int, dict, str, str, str]] = []
    for si, seg in enumerate(segs):
        for norm_name, raw_name, full_tpl in _find_templates(seg.get("text") or ""):
            if seg.get("op") == "removed":
                rem_t.append((si, seg, norm_name, raw_name, full_tpl))
            elif seg.get("op") == "added":
                add_t.append((si, seg, norm_name, raw_name, full_tpl))

    if not rem_t and not add_t:
        return []

    used_r: set[int] = set()
    used_a: set[int] = set()
    for ri, (_, rs, rnl, _, rfull) in enumerate(rem_t):
        norm_rf = " ".join(rfull.lower().split())
        for ai, (_, as_, anl, _, afull) in enumerate(add_t):
            if ai in used_a:
                continue
            norm_af = " ".join(afull.lower().split())
            if norm_rf == norm_af or (
                rnl == anl
                and rs.get("new_paragraph") is not None
                and rs.get("new_paragraph") == as_.get("new_paragraph")
            ):
                used_r.add(ri)
                used_a.add(ai)
                break

    signals: list[dict] = []

    for ri, (si, rs, _, raw_name, rfull) in enumerate(rem_t):
        if ri in used_r:
            continue
        op = rs.get("old_paragraph")
        np = rs.get("new_paragraph")

        if np is None and op is not None and op < len(ob):
            block_text = ob[op][2].strip()
            if not block_text.startswith("{{"):
                continue

        osid = o_sec.get(op) if op is not None else None
        nsid = n_sec.get(np) if np is not None else None
        other_in_sec = False
        for osi, other in enumerate(segs):
            if osi == si:
                continue
            oop = other.get("old_paragraph")
            onp = other.get("new_paragraph")
            if (
                (osid is not None and oop is not None and o_sec.get(oop) == osid)
                or (nsid is not None and onp is not None and n_sec.get(onp) == nsid)
                or (osid is None and nsid is None and len(segs) > 1)
            ):
                other_in_sec = True
                break

        if not other_in_sec:
            label = f"Strong: removed maintenance template {{{{{raw_name}}}}} without other section changes"
        else:
            label = f"Removed maintenance template {{{{{raw_name}}}}}"
        signals.append({"kind": KIND, "label": label, "evidence": rfull[:200]})

    for ai, (_, _, _, raw_name, afull) in enumerate(add_t):
        if ai in used_a:
            continue
        label = f"Added maintenance template {{{{{raw_name}}}}}"
        signals.append({"kind": KIND, "label": label, "evidence": afull[:200]})

    return signals
