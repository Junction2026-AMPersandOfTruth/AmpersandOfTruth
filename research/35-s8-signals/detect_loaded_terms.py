"""Detector for paired neutral/loaded Finnish term substitutions (P1.S8, kind='loaded_term')."""
from __future__ import annotations

import difflib
import functools
import json
import pathlib
import re

KIND = "loaded_term"
PAIRS_PATH = pathlib.Path(__file__).resolve().with_name("loaded_pairs.json")

_WORD_OR_PUNCT_RE = re.compile(r"[^\W_]+(?:-[^\W_]+)*|\S", re.UNICODE)


def load_pairs(path: pathlib.Path | None = None) -> list[dict]:
    p = path or PAIRS_PATH
    return json.loads(p.read_text(encoding="utf-8"))


def word_stem_pattern(word: str) -> str:
    """Compile a Finnish citation-form word into an inflected stem regex fragment."""
    wl = word.lower().strip()
    if wl.isdigit():
        return re.escape(wl)
    if "-" in wl:
        parts = wl.split("-")
        return "-".join([re.escape(p) for p in parts[:-1]] + [word_stem_pattern(parts[-1])])

    # Irregular / derivational / compound families common in Finnish historical prose
    if wl in ("venäjä", "venäläinen"):
        return r"(?:venäjä[a-zäöå]*|venäläi(?:nen|s[a-zäöå]*))"
    if wl in ("saksalainen", "saksa"):
        return r"(?:saksa[a-zäöå]*|saksalai(?:nen|s[a-zäöå]*))"
    if wl == "ryssä":
        return r"(?:[a-zäöå]+-)?ryss[äi][a-zäöå]*"
    if wl == "natsi":
        return r"(?:[a-zäöå]+-)?natsi[a-zäöå-]*"
    if wl == "miehitysarmeija":
        return r"miehitys(?:armeij|jouk)[a-zäöå]*"
    if wl == "armeija":
        return r"armeij[a-zäöå]*"
    if wl == "sota":
        return r"so(?:ta|da)[a-zäöå]*"
    if wl.endswith("sota"):
        pref = re.escape(wl[:-4])
        if pref.endswith("s"):
            pref = pref + "+"
        return rf"{pref}o(?:ta|da)[a-zäöå]*"
    if wl == "suomi":
        return r"suom(?:i|e[a-zäöå]*)"
    if wl.endswith("suomi"):
        pref = re.escape(wl[:-5])
        return rf"{pref}suom(?:i|e[a-zäöå]*)"
    if wl == "vuoden":
        return r"vuoden"
    if wl == "joukot":
        return r"jouk(?:ot|ko[a-zäöå]*)"
    if wl == "alueet":
        return r"alue[a-zäöå]*"
    if wl == "senaatin":
        return r"(?:senaat|hallitu(?:s|ks))[a-zäöå]*"

    # Productive Finnish inflection classes (consonant gradation + case/number suffixes)
    if wl.endswith(("ttu", "tty", "tut", "tyt")) and len(wl) >= 6:
        return re.escape(wl[:-3]) + r"t?[a-zäöå]*"
    if wl.endswith("nen") and len(wl) >= 6:
        return re.escape(wl[:-3]) + r"(?:nen|s[a-zäöå]*)"
    if wl.endswith("set") and len(wl) >= 6:
        return re.escape(wl[:-3]) + r"(?:nen|s[a-zäöå]*)"
    if wl.endswith(("kki", "kit")) and len(wl) >= 5:
        return re.escape(wl[:-3]) + r"k[a-zäöå-]*"
    if wl.endswith("tti") and len(wl) >= 6:
        return re.escape(wl[:-2]) + r"t?[ie][a-zäöå]*"
    if wl.endswith("kaarti"):
        return re.escape(wl[:-1]) + r"[ie][a-zäöå]*"
    if wl.endswith("it") and len(wl) >= 6:
        return re.escape(wl[:-2]) + r"[ie][a-zäöå]*"
    if wl.endswith("i") and len(wl) >= 5:
        return re.escape(wl[:-1]) + r"[ie][a-zäöå]*"
    if wl.endswith(("us", "ys")) and len(wl) >= 5:
        return re.escape(wl[:-1]) + r"(?:s|ks[a-zäöå]+|st[a-zäöå]*)"
    if wl.endswith(("oida", "öidä")) and len(wl) >= 6:
        return re.escape(wl[:-2]) + r"[a-zäöå]*"
    if wl.endswith(("aa", "ää")) and len(wl) >= 5:
        return re.escape(wl[:-2]) + r"[a-zäöå]*"
    if wl.endswith(("a", "ä", "u", "y", "o", "ö")) and len(wl) >= 5:
        return re.escape(wl[:-1]) + r"[a-zäöå]*"
    return re.escape(wl) + r"[a-zäöå]*"


def compile_phrase_regex(phrase: str) -> re.Pattern[str]:
    words = phrase.strip().split()
    pattern = r"\b" + r"\s+".join(word_stem_pattern(w) for w in words) + r"\b"
    return re.compile(pattern, re.IGNORECASE)


@functools.lru_cache(maxsize=1)
def _default_compiled_pairs() -> list[tuple[dict, re.Pattern[str], re.Pattern[str]]]:
    return [(entry, compile_phrase_regex(entry["a"]), compile_phrase_regex(entry["b"])) for entry in load_pairs()]


def _compile_pairs(pairs: list[dict] | None) -> list[tuple[dict, re.Pattern[str], re.Pattern[str]]]:
    if pairs is None:
        return _default_compiled_pairs()
    return [(entry, compile_phrase_regex(entry["a"]), compile_phrase_regex(entry["b"])) for entry in pairs]


def _strip_wiki_for_prose(s: str) -> str:
    s = re.sub(r"<ref\b[^>]*>.*?</ref>", " ", s, flags=re.IGNORECASE | re.DOTALL)
    s = re.sub(r"<ref\b[^>]*/\s*>", " ", s, flags=re.IGNORECASE)
    s = re.sub(r"<!--.*?-->", " ", s, flags=re.DOTALL)
    s = re.sub(r"\[\[(?:File|Image|Kuva|Tiedosto):[^\]]*\]\]", " ", s, flags=re.IGNORECASE)
    s = re.sub(r"\[\[([^|\]]+)\|([^\]]+)\]\]", r"\2", s)
    s = re.sub(r"\[\[([^\]]+)\]\]", r"\1", s)
    # Strip inline single-line templates (e.g. {{k-ru|...}}, {{lähde}}) while preserving multi-line infobox lines
    s = re.sub(r"\{\{[^{}\n]*\}\}", " ", s)
    s = re.sub(r"</?(?:center|small|br)\b[^>]*>", " ", s, flags=re.IGNORECASE)
    s = s.replace("'''", "").replace("''", "").replace("&nbsp;", " ").replace("&#160;", " ").replace("\xad", "")
    s = re.sub(r"^=+\s*|\s*=+$", "", s)
    return re.sub(r"\s+", " ", s).strip()


def _format_tokens(tokens: list[str]) -> str:
    text = " ".join(tokens)
    text = re.sub(r"\s+([,.;:?!)\]])", r"\1", text)
    text = re.sub(r"([(\[])\s+", r"\1", text)
    return text.strip()


def _truncate(s: str, limit: int = 200) -> str:
    s = re.sub(r"\s+", " ", s).strip()
    if len(s) <= limit:
        return s
    return s[: limit - 3].rstrip() + "..."


def detect(cand: dict, pairs: list[dict] | None = None) -> list[dict]:
    """Return 'loaded_term' signal dicts for paired neutral/loaded term substitutions."""
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []

    compiled = _compile_pairs(pairs)
    signals: list[dict] = []
    seen_hits: set[tuple[str, str, str]] = set()

    def _check_window(
        core_old: str,
        core_new: str,
        ctx_old: str,
        ctx_new: str,
    ) -> None:
        if not core_old.strip() or not core_new.strip():
            return
        for entry, ra, rb in compiled:
            ma_o, mb_o = ra.search(ctx_old), rb.search(ctx_old)
            ma_n, mb_n = ra.search(ctx_new), rb.search(ctx_new)
            # Require a true swap: side A in old (not B) and side B in new (not A), or vice versa,
            # AND require at least part of the swapped expression to lie inside the changed core tokens.
            a_to_b = bool(ma_o and not mb_o and mb_n and not ma_n)
            b_to_a = bool(mb_o and not ma_o and ma_n and not mb_n)
            if not (a_to_b or b_to_a):
                continue

            old_m = ma_o if a_to_b else mb_o
            new_m = mb_n if a_to_b else ma_n
            assert old_m is not None and new_m is not None
            if old_m.group(0).lower() == new_m.group(0).lower():
                continue
            # Ensure the matched words actually overlap the replaced core rather than unchanged surrounding context.
            old_words = set(re.findall(r"[a-zäöå0-9-]+", old_m.group(0).lower()))
            new_words = set(re.findall(r"[a-zäöå0-9-]+", new_m.group(0).lower()))
            core_words = set(re.findall(r"[a-zäöå0-9-]+", f"{core_old} {core_new}".lower()))
            if not ((old_words | new_words) & core_words):
                continue

            src_term = entry["a"] if a_to_b else entry["b"]
            dst_term = entry["b"] if a_to_b else entry["a"]
            label = f"Paired term swap: {src_term} → {dst_term}"
            evidence = _truncate(f"{ctx_old} → {ctx_new}")
            dedup_key = (entry["a"], entry["b"], f"{old_m.group(0).lower()}->{new_m.group(0).lower()}")
            if dedup_key in seen_hits:
                continue
            seen_hits.add(dedup_key)
            signals.append({"kind": KIND, "label": label, "evidence": evidence})

    old_wt = cand.get("old")
    new_wt = cand.get("new")
    if isinstance(old_wt, str) and isinstance(new_wt, str) and old_wt and new_wt:
        old_lines = [_strip_wiki_for_prose(x) for x in old_wt.splitlines()]
        new_lines = [_strip_wiki_for_prose(x) for x in new_wt.splitlines()]
        sm = difflib.SequenceMatcher(None, old_lines, new_lines, autojunk=False)
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag != "replace":
                continue
            chunk_o = [x for x in old_lines[i1:i2] if x]
            chunk_n = [x for x in new_lines[j1:j2] if x]
            pairs_to_diff: list[tuple[str, str]] = []
            if len(chunk_o) != len(chunk_n) and chunk_o and chunk_n:
                # Pair lines that clearly rewrite each other (e.g., first paragraph edited while trailing list deleted)
                used_n: set[int] = set()
                for lo in chunk_o:
                    best_j, best_r = -1, 0.0
                    for idx_n, ln in enumerate(chunk_n):
                        if idx_n in used_n:
                            continue
                        r = difflib.SequenceMatcher(None, lo, ln, autojunk=False).ratio()
                        if r > best_r:
                            best_j, best_r = idx_n, r
                    if best_j >= 0 and best_r >= 0.45:
                        used_n.add(best_j)
                        pairs_to_diff.append((lo, chunk_n[best_j]))
            if not pairs_to_diff:
                pairs_to_diff = [(" ".join(chunk_o), " ".join(chunk_n))]

            for text_o, text_n in pairs_to_diff:
                ot = _WORD_OR_PUNCT_RE.findall(text_o)
                nt = _WORD_OR_PUNCT_RE.findall(text_n)
                tsm = difflib.SequenceMatcher(None, ot, nt, autojunk=False)
                before_count = len(signals)
                for ttag, ti1, ti2, tj1, tj2 in tsm.get_opcodes():
                    if ttag != "replace":
                        continue
                    core_o = _format_tokens(ot[ti1:ti2])
                    core_n = _format_tokens(nt[tj1:tj2])
                    ctx_o = _format_tokens(ot[max(0, ti1 - 3) : min(len(ot), ti2 + 3)])
                    ctx_n = _format_tokens(nt[max(0, tj1 - 3) : min(len(nt), tj2 + 3)])
                    _check_window(core_o, core_n, ctx_o, ctx_n)
                # Fallback for single-paragraph rewrites where clause reordering splits delete/insert opcodes
                if len(signals) == before_count and len(text_o) <= 600 and len(text_n) <= 600:
                    for entry, ra, rb in compiled:
                        ma_o, mb_o = ra.search(text_o), rb.search(text_o)
                        ma_n, mb_n = ra.search(text_n), rb.search(text_n)
                        a_to_b = bool(ma_o and not mb_o and mb_n and not ma_n)
                        b_to_a = bool(mb_o and not ma_o and ma_n and not mb_n)
                        if not (a_to_b or b_to_a):
                            continue
                        om = ma_o if a_to_b else mb_o
                        nm = mb_n if a_to_b else ma_n
                        assert om is not None and nm is not None
                        lo_idx = max(0, om.start() - 30)
                        if lo_idx > 0 and " " in text_o[lo_idx : om.start()]:
                            lo_idx = text_o.find(" ", lo_idx, om.start()) + 1
                        ro_idx = min(len(text_o), om.end() + 30)
                        if ro_idx < len(text_o) and " " in text_o[om.end() : ro_idx]:
                            ro_idx = text_o.rfind(" ", om.end(), ro_idx)
                        ln_idx = max(0, nm.start() - 30)
                        if ln_idx > 0 and " " in text_n[ln_idx : nm.start()]:
                            ln_idx = text_n.find(" ", ln_idx, nm.start()) + 1
                        rn_idx = min(len(text_n), nm.end() + 30)
                        if rn_idx < len(text_n) and " " in text_n[nm.end() : rn_idx]:
                            rn_idx = text_n.rfind(" ", nm.end(), rn_idx)
                        co = text_o[lo_idx:ro_idx].strip()
                        cn = text_n[ln_idx:rn_idx].strip()
                        _check_window(om.group(0), nm.group(0), co, cn)
        return signals

    # Fallback when only cand["diff"]["segments"] is provided (e.g., synthetic unit tests without full wikitext).
    segments = [s for s in diff.get("segments", []) if not s.get("moved")]
    by_block: dict[tuple[int | None, int | None], list[dict]] = {}
    for seg in segments:
        if seg.get("kind") not in ("prose", "template"):
            continue
        txt = _strip_wiki_for_prose(seg.get("prose") or seg.get("text") or "")
        if not txt:
            continue
        key = (seg.get("old_paragraph"), seg.get("new_paragraph"))
        by_block.setdefault(key, []).append({**seg, "_txt": txt})

    for bsegs in by_block.values():
        rem_txt = " ".join(s["_txt"] for s in bsegs if s.get("op") == "removed")
        add_txt = " ".join(s["_txt"] for s in bsegs if s.get("op") == "added")
        if not rem_txt or not add_txt:
            continue
        ot = _WORD_OR_PUNCT_RE.findall(rem_txt)
        nt = _WORD_OR_PUNCT_RE.findall(add_txt)
        tsm = difflib.SequenceMatcher(None, ot, nt, autojunk=False)
        for ttag, ti1, ti2, tj1, tj2 in tsm.get_opcodes():
            if ttag != "replace":
                continue
            core_o = _format_tokens(ot[ti1:ti2])
            core_n = _format_tokens(nt[tj1:tj2])
            ctx_o = _format_tokens(ot[max(0, ti1 - 3) : min(len(ot), ti2 + 3)])
            ctx_n = _format_tokens(nt[max(0, tj1 - 3) : min(len(nt), tj2 + 3)])
            _check_window(core_o, core_n, ctx_o, ctx_n)

    return signals
