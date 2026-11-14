"""Detector `citation_swapped` (P1.S8 #35, group `refs`).

Flags `<ref>` citations replaced in the same paragraph by a different source
(not routine citation-template formatting, Wayback archive wrapping, or named-ref
shortening), reporting `old → new` domain/title.
"""
from __future__ import annotations

import re
from urllib.parse import urlparse

KIND = "citation_swapped"
_LF = chr(10)
_HEADING_BREAK = _LF + "=="

_URL_RE = re.compile(r"https?://[^\s<>\]|}'\x22]+", re.I)
_REF_NAME_RE = re.compile(r"<ref\b[^>]*?\bname\s*=\s*(?:\x22([^\x22]+)\x22|'([^']+)'|([^\s/>]+))", re.I)
_NESTED_REF_SPLIT_RE = re.compile(r"(?=<ref[\s>/])", re.I)
_ARCHIVE_UNWRAP_RE = re.compile(
    r"^https?://(?:web\.archive\.org/web/\d+[a-z_]*|archive\.(?:is|today|ph)/[^/]+)/(https?://.+)$",
    re.I,
)
_TITLE_PARAM_RE = re.compile(r"\|\s*(?:nimeke|otsikko|title)\s*=\s*([^|}\x0a]+)", re.I)
_PARAM_BOILERPLATE_RE = re.compile(
    r"\b(?:osoite|url|nimeke|otsikko|title|tekijä|author|julkaisu|julkaisija|ajankohta|vuosi|"
    r"sivu|sivut|viitattu|luettu|kieli|ietf-kielikoodi|arkisto|arkistoitu|tiedostomuoto|selite|"
    r"tunniste|isbn|issn|kappale|luku|numero|vuosikerta|julkaisupaikka)\s*=",
    re.I,
)
_CITE_TPL_RE = re.compile(
    r"\{\{\s*(?:verkkoviite|kirjaviite|lehtiviite|jaksoviite|cite\s+\w+|wayback)",
    re.I,
)
_STOP_TOKENS = frozenset({"http", "https", "www", "html", "htm", "pdf", "sivu", "sivut", "painos", "toim"})


def _split_unclosed_refs(raw: str) -> list[str]:
    parts = [p for p in _NESTED_REF_SPLIT_RE.split(raw) if p.strip()]
    return parts if parts else [raw]


def _clean_display(text: str, max_len: int = 45) -> str:
    text = re.sub(r"\[\[([^|\]]+\|)?([^\]]+)\]\]", r"\2", text)
    text = re.sub(r"[{}|\[\]'\x22]+", " ", text)
    text = " ".join(text.split()).strip(" ,.;:-")
    if len(text) > max_len:
        return text[: max_len - 1].rstrip() + "…"
    return text


def _parse_ref(raw: str) -> dict:
    m_name = _REF_NAME_RE.search(raw)
    name = ((m_name.group(1) or m_name.group(2) or m_name.group(3) or "").strip().lower()) if m_name else ""
    self_closing = bool(re.match(r"^\s*<ref\b[^>]*/>\s*$", raw, re.I | re.S))
    body = re.sub(r"^\s*<ref\b[^>]*>", "", raw, flags=re.I | re.S)
    body = re.sub(r"</ref\s*>\s*$", "", body, flags=re.I | re.S)
    swallowed_heading = _HEADING_BREAK in body
    if swallowed_heading:
        body = body.split(_HEADING_BREAK, 1)[0]
    active = re.sub(r"<!--.*?-->", " ", body, flags=re.S)

    urls: list[str] = []
    domains: list[str] = []
    for u in _URL_RE.findall(active):
        u = u.rstrip(".,;:!?)")
        m_arch = _ARCHIVE_UNWRAP_RE.match(u)
        u_orig = m_arch.group(1) if m_arch else u
        try:
            dom = (urlparse(u_orig).hostname or "").lower().removeprefix("www.")
        except ValueError:
            dom = ""
        urls.append(u_orig)
        if dom and dom != "web.archive.org":
            domains.append(dom)
    domains = list(dict.fromkeys(domains))

    m_title = _TITLE_PARAM_RE.search(active)
    title = _clean_display(m_title.group(1), 45) if m_title else ""
    if not title:
        m_ext = re.search(r"\[https?://[^\s\]]+\s+([^\]]+)\]", active)
        if m_ext:
            title = _clean_display(m_ext.group(1), 45)

    cleaned = _CITE_TPL_RE.sub(" ", active)
    cleaned = _PARAM_BOILERPLATE_RE.sub(" ", cleaned)
    cleaned = re.sub(r"https?://\S+", " ", cleaned)
    tokens = [
        w for w in re.findall(r"[0-9a-zA-ZäöåÄÖÅšžŠŽ]{2,}", cleaned.lower())
        if w not in _STOP_TOKENS
    ]
    if not title:
        title = _clean_display(cleaned, 45)

    has_alpha = any(any(ch.isalpha() for ch in w) for w in tokens)
    is_empty = (not self_closing) and (not domains) and (not has_alpha)
    if domains and title:
        desc = f"{domains[0]} ({title})"
    elif domains:
        desc = domains[0]
    elif title:
        desc = title
    elif name:
        desc = f"ref:{name}"
    else:
        desc = "citation"

    return {
        "raw": raw,
        "name": name,
        "self_closing": self_closing,
        "swallowed_heading": swallowed_heading,
        "urls": urls,
        "domains": domains,
        "title": title,
        "token_set": set(tokens),
        "is_empty": is_empty,
        "desc": desc,
        "body": active,
    }


def _same_source(old_r: dict, new_r: dict) -> bool:
    if old_r["name"] and old_r["name"] == new_r["name"] and (old_r["self_closing"] or new_r["self_closing"]):
        return True
    old_core = " ".join(old_r["body"].split())
    new_core = " ".join(new_r["body"].split())
    if len(old_core) >= 15 and old_core in new_core:
        return True
    norm_u = lambda u: re.sub(r"^https?://(www\.)?", "", u.lower()).rstrip("/")
    old_us = {norm_u(u) for u in old_r["urls"]}
    new_us = {norm_u(u) for u in new_r["urls"]}
    if old_us and new_us and (old_us & new_us):
        return True
    ot, nt = old_r["token_set"], new_r["token_set"]
    if old_r["domains"] and new_r["domains"] and (set(old_r["domains"]) & set(new_r["domains"])):
        if ot and nt and (len(ot & nt) / min(len(ot), len(nt)) >= 0.6):
            return True
    if not old_r["domains"] and not new_r["domains"] and ot and nt:
        if ot <= nt or nt <= ot:
            return True
        if len(ot & nt) / len(ot | nt) >= 0.85:
            return True
    return False


def detect(cand: dict) -> list[dict]:
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []

    segs = [s for s in (diff.get("segments") or []) if not s.get("moved")]
    old_text = cand.get("old") or ""
    old_names = {
        (m.group(1) or m.group(2) or m.group(3) or "").strip().lower()
        for m in _REF_NAME_RE.finditer(old_text)
    }
    old_names.discard("")

    rem_refs: list[tuple[dict, dict]] = []
    add_refs: list[tuple[dict, dict]] = []
    for seg in segs:
        if seg.get("kind") != "reference":
            continue
        if seg.get("op") == "removed":
            for part in _split_unclosed_refs(seg.get("text") or ""):
                rem_refs.append((seg, _parse_ref(part)))
        elif seg.get("op") == "added":
            for part in _split_unclosed_refs(seg.get("text") or ""):
                add_refs.append((seg, _parse_ref(part)))

    if not rem_refs or not add_refs:
        return []

    used_rem: set[int] = set()
    used_add: set[int] = set()

    for same_para_only in (True, False):
        for ri, (rs, rinfo) in enumerate(rem_refs):
            if ri in used_rem:
                continue
            if rinfo["is_empty"]:
                used_rem.add(ri)
                continue
            for ai, (as_, ainfo) in enumerate(add_refs):
                if ai in used_add:
                    continue
                same_p = (
                    rs.get("new_paragraph") is not None
                    and rs.get("new_paragraph") == as_.get("new_paragraph")
                )
                if same_para_only and not same_p:
                    continue
                if _same_source(rinfo, ainfo):
                    used_rem.add(ri)
                    used_add.add(ai)
                    break

    signals: list[dict] = []
    seen_pairs: set[tuple[str, str]] = set()
    for ri, (rs, rinfo) in enumerate(rem_refs):
        np = rs.get("new_paragraph")
        if ri in used_rem or np is None:
            continue
        for ai, (as_, ainfo) in enumerate(add_refs):
            if ai in used_add or ainfo["is_empty"]:
                continue
            if ainfo["self_closing"] and ainfo["name"] in old_names:
                continue
            if as_.get("new_paragraph") != np:
                continue
            if ainfo["swallowed_heading"] and rinfo["domains"] and not ainfo["domains"]:
                continue
            used_rem.add(ri)
            used_add.add(ai)
            pair_key = (rinfo["desc"], ainfo["desc"])
            if pair_key in seen_pairs:
                break
            seen_pairs.add(pair_key)
            label = f"Citation replaced: {rinfo['desc']} → {ainfo['desc']}"
            evidence = ainfo["raw"][:200]
            signals.append({"kind": KIND, "label": label, "evidence": evidence})
            break

    return signals
