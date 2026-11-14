"""Detector for in-place numeral changes in prose/infobox segments (P1.S8, kind='number_changed')."""
from __future__ import annotations

import difflib
import re

KIND = "number_changed"

# Single Finnish number: full numeric date (14.12.1939), space/NBSP thousand-grouped number (25 000, 25 000,5),
# or plain integer/decimal (250, 3,5, 1918).
_SINGLE_NUM = (
    r"(?:\d{1,2}\.\d{1,2}\.\d{2,4}"
    r"|\d{1,3}(?:[ \u00a0\u202f\u2009]\d{3})+(?:[.,]\d+)?"
    r"|\d+(?:[.,]\d+)?)"
)
_NUM_EXPR = rf"{_SINGLE_NUM}(?:\s*(?:–|—|-)\s*{_SINGLE_NUM})?"
_NUM_RE = re.compile(_NUM_EXPR)
_APPROX_RE = re.compile(
    r"\b(?:noin|n\.|yli|alle|jopa|lähes|arviolta|enintään|vähintään|runsas|runsaat|vajaa|vajaat)(?=\s)",
    re.IGNORECASE,
)
_TOKEN_RE = re.compile(rf"{_NUM_EXPR}|[^\W\d_]+|\S", re.UNICODE)

_YEAR_CTX_RE = re.compile(
    r"\b(?:vuonna|vuoden|vuoteen|vuodesta|vuosina|luvulla|luvun|"
    r"tammikuu\w*|helmikuu\w*|maaliskuu\w*|huhtikuu\w*|toukokuu\w*|kesäkuu\w*|"
    r"heinäkuu\w*|elokuu\w*|syyskuu\w*|lokakuu\w*|marraskuu\w*|joulukuu\w*)\b",
    re.IGNORECASE,
)
_QTY_UNIT_RE = re.compile(
    r"(?:%|prosent\w*|henke\w*|hengen|miest\w*|miehen|ihmist\w*|sotil\w*|kaatun\w*|"
    r"haavoitt\w*|vangit\w*|vank\w*|uhri\w*|asuk\w*|suomalais\w*|venäläis\w*|"
    r"naiskaart\w*|kiväär\w*|tykk\w*|divisioon\w*|euro\w*|markk\w*|miljard\w*|miljoon\w*|km\b)",
    re.IGNORECASE,
)


def _clean_text(s: str) -> str:
    s = (
        s.replace("&nbsp;", " ")
        .replace("&#160;", " ")
        .replace("&thinsp;", " ")
        .replace("&ndash;", "–")
        .replace("&mdash;", "—")
    )
    s = re.sub(r"[\u00a0\u202f\u2009]+", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def _strip_wiki_for_prose(line: str) -> str:
    line = re.sub(r"<ref\b[^>]*>.*?</ref>", " ", line, flags=re.IGNORECASE | re.DOTALL)
    line = re.sub(r"<ref\b[^>]*/\s*>", " ", line, flags=re.IGNORECASE)
    line = re.sub(r"<!--.*?-->", " ", line, flags=re.DOTALL)
    line = re.sub(r"\[\[(?:File|Image|Kuva|Tiedosto):[^\]]*\]\]", " ", line, flags=re.IGNORECASE)
    line = re.sub(r"\[\[([^|\]]+)\|([^\]]+)\]\]", r"\2", line)
    line = re.sub(r"\[\[([^\]]+)\]\]", r"\1", line)
    line = line.replace("'''", "").replace("''", "")
    return _clean_text(line)


def _norm_num(num_str: str) -> str:
    s = _clean_text(num_str)
    s = re.sub(r"\s*(?:–|—|-)\s*", "-", s)
    s = re.sub(r"(?<=\d) (?=\d{3}\b)", "", s)
    s = s.replace(",", ".")
    return s


def _has_new_reference(diff: dict) -> bool:
    segments = [s for s in diff.get("segments", []) if not s.get("moved")]
    added_refs = [s for s in segments if s.get("op") == "added" and s.get("kind") == "reference"]
    if not added_refs:
        has_any_ref_seg = any(s.get("kind") == "reference" for s in segments)
        if not has_any_ref_seg:
            return int(diff.get("stats", {}).get("added_refs", 0) or 0) > 0
        return False

    removed_pool = " ".join(
        (s.get("prose") or s.get("text") or "")
        for s in segments
        if s.get("op") == "removed" and s.get("kind") in ("reference", "prose")
    )
    pool_tokens = set(re.findall(r"[a-zäöå0-9]{3,}", removed_pool.lower()))
    for ref in added_refs:
        raw = ref.get("prose") or ref.get("text") or ""
        ref_tokens = set(re.findall(r"[a-zäöå0-9]{3,}", raw.lower()))
        if ref_tokens - pool_tokens:
            return True
    return False


def _is_year_token(s: str) -> bool:
    s = s.strip()
    if not re.fullmatch(r"\d{4}", s):
        return False
    val = int(s)
    return 1000 <= val <= 2099


def _is_year_change(old_num: str, new_num: str, context: str = "", raw_text: str = "") -> bool:
    if _QTY_UNIT_RE.search(context):
        return False

    old_parts = re.split(r"\s*(?:–|—|-)\s*", old_num.strip())
    new_parts = re.split(r"\s*(?:–|—|-)\s*", new_num.strip())

    # Do not treat space-grouped thousands ("1 500 -> 2000") or decimals ("3,5 -> 4,5") as years.
    if any(" " in p or "," in p for p in old_parts + new_parts):
        return False

    # Full dotted calendar dates (14.12.1939 -> 14.12.1940) where year part changed.
    if re.fullmatch(r"\d{1,2}\.\d{1,2}\.\d{4}", old_num) and re.fullmatch(r"\d{1,2}\.\d{1,2}\.\d{4}", new_num):
        if old_num.rsplit(".", 1)[-1] != new_num.rsplit(".", 1)[-1]:
            return True

    if all(_is_year_token(p) for p in old_parts) and all(_is_year_token(p) for p in new_parts):
        return True

    # Typo repair on a 4-digit year such as "18 -> 1918" when year context or [[1918]] wikilink is present.
    if len(old_parts) == 1 and len(new_parts) == 1:
        a, b = old_parts[0], new_parts[0]
        if (_is_year_token(b) and a.isdigit() and len(a) == 2 and b.endswith(a)) or (
            _is_year_token(a) and b.isdigit() and len(b) == 2 and a.endswith(b)
        ):
            if _YEAR_CTX_RE.search(context) or "[[" in raw_text:
                return True

    return False


def _format_label(is_year: bool, has_new_ref: bool) -> str:
    kind_word = "Year" if is_year else "Quantity"
    if has_new_ref:
        return f"{kind_word} changed (with new source)"
    return f"{kind_word} changed without a new source"


def _strip_approx(s: str) -> str:
    return _clean_text(_APPROX_RE.sub("", s))


def _segment_num_diffs(rem_seg: dict, add_seg: dict) -> list[tuple[str, str, str, str]]:
    raw_rem = rem_seg.get("prose") or rem_seg.get("text") or ""
    raw_add = add_seg.get("prose") or add_seg.get("text") or ""
    pa = _strip_approx(_clean_text(raw_rem))
    pb = _strip_approx(_clean_text(raw_add))
    if not pa or not pb:
        return []
    nums_a = _NUM_RE.findall(pa)
    nums_b = _NUM_RE.findall(pb)
    if not nums_a or len(nums_a) != len(nums_b):
        return []
    skel_a = _clean_text(_NUM_RE.sub("<N>", pa))
    skel_b = _clean_text(_NUM_RE.sub("<N>", pb))
    if skel_a != skel_b:
        return []
    raw_both = f"{rem_seg.get('text', '')} {add_seg.get('text', '')}"
    ctx = f"{_clean_text(raw_rem)} {_clean_text(raw_add)}"
    out: list[tuple[str, str, str, str]] = []
    for x, y in zip(nums_a, nums_b):
        cx, cy = _clean_text(x), _clean_text(y)
        if _norm_num(cx) != _norm_num(cy):
            out.append((cx, cy, ctx, raw_both))
    return out


def _expand_from_wikitext(old_text: str, new_text: str, seg_diffs: list[tuple[str, str, str, str]]) -> list[tuple[str, str, str, str]]:
    """Reconstruct full thousand-separated numbers / ranges when wikidiff split on '&nbsp;000' or '-'."""
    old_lines = [_strip_wiki_for_prose(line) for line in old_text.splitlines()]
    new_lines = [_strip_wiki_for_prose(line) for line in new_text.splitlines()]
    sm = difflib.SequenceMatcher(None, old_lines, new_lines, autojunk=False)

    line_num_swaps: list[tuple[str, str, str]] = []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag != "replace":
            continue
        ot = _TOKEN_RE.findall(" ".join(old_lines[i1:i2]))
        nt = _TOKEN_RE.findall(" ".join(new_lines[j1:j2]))
        tsm = difflib.SequenceMatcher(None, ot, nt, autojunk=False)
        for ttag, ti1, ti2, tj1, tj2 in tsm.get_opcodes():
            if ttag != "replace":
                continue
            osub = _strip_approx(_clean_text(" ".join(ot[ti1:ti2])))
            nsub = _strip_approx(_clean_text(" ".join(nt[tj1:tj2])))
            if _NUM_RE.fullmatch(osub) and _NUM_RE.fullmatch(nsub) and _norm_num(osub) != _norm_num(nsub):
                ctx = _clean_text(
                    " ".join(ot[max(0, ti1 - 3) : min(len(ot), ti2 + 4)])
                    + " "
                    + " ".join(nt[max(0, tj1 - 3) : min(len(nt), tj2 + 4)])
                )
                line_num_swaps.append((osub, nsub, ctx))

    if not line_num_swaps:
        return seg_diffs

    used_expanded: set[int] = set()
    result: list[tuple[str, str, str, str]] = []
    for sx, sy, sctx, sraw in seg_diffs:
        matched_idx = None
        for idx, (lx, ly, lctx) in enumerate(line_num_swaps):
            # Match exact or prefix/component of split thousand/range ("15" in "15 000", "3" in "3 000–4 000")
            x_parts = re.findall(r"\d+(?:[.,]\d+)?", lx.replace(" ", ""))
            y_parts = re.findall(r"\d+(?:[.,]\d+)?", ly.replace(" ", ""))
            if (lx == sx and ly == sy) or (
                sx in lx.split() or sx in re.split(r"[\s–—-]+", lx) or sx in x_parts
            ) and (
                sy in ly.split() or sy in re.split(r"[\s–—-]+", ly) or sy in y_parts
            ):
                matched_idx = idx
                break
        if matched_idx is not None:
            if matched_idx not in used_expanded:
                used_expanded.add(matched_idx)
                lx, ly, lctx = line_num_swaps[matched_idx]
                result.append((lx, ly, lctx, sraw))
        else:
            result.append((sx, sy, sctx, sraw))
    return result


def detect(cand: dict) -> list[dict]:
    """Return 'number_changed' signal dicts for in-place numeral edits in prose/infobox segments."""
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []

    segments = [s for s in diff.get("segments", []) if not s.get("moved")]
    # Keep prose and infobox/template segments, skipping empty-prose markup tokens that separate link edits.
    cand_segs = [
        s
        for s in segments
        if s.get("kind") in ("prose", "template")
        and ((s.get("prose") or "").strip() or (s.get("text") or "").strip())
    ]

    seg_diffs: list[tuple[str, str, str, str]] = []
    i = 0
    while i < len(cand_segs) - 1:
        a, b = cand_segs[i], cand_segs[i + 1]
        same_block = (
            a.get("old_paragraph") == b.get("old_paragraph")
            and a.get("new_paragraph") == b.get("new_paragraph")
        )
        in_place = a.get("old_paragraph") is not None or b.get("new_paragraph") is not None
        # At least one side must have visible prose (avoids inline layout template params like {{cols|75}} -> {{cols|65}}).
        has_visible_prose = bool((a.get("prose") or "").strip() or (b.get("prose") or "").strip())
        if {a.get("op"), b.get("op")} == {"removed", "added"} and same_block and in_place and has_visible_prose:
            rem = a if a.get("op") == "removed" else b
            add = b if a.get("op") == "removed" else a
            # Require both old_paragraph and new_paragraph when paragraph indices are present
            if (rem.get("old_paragraph") is None) != (rem.get("new_paragraph") is None):
                i += 1
                continue
            pair_hits = _segment_num_diffs(rem, add)
            if pair_hits:
                seg_diffs.extend(pair_hits)
                i += 2
                continue
        i += 1

    if not seg_diffs:
        return []

    old_wt = cand.get("old")
    new_wt = cand.get("new")
    if isinstance(old_wt, str) and isinstance(new_wt, str) and old_wt and new_wt:
        final_diffs = _expand_from_wikitext(old_wt, new_wt, seg_diffs)
    else:
        final_diffs = seg_diffs

    has_new_ref = _has_new_reference(diff)
    signals: list[dict] = []
    seen_evidence: set[str] = set()
    for old_num, new_num, ctx, raw_text in final_diffs:
        evidence = f"{old_num} → {new_num}"
        if len(evidence) > 200:
            evidence = evidence[:197] + "..."
        if evidence in seen_evidence:
            continue
        seen_evidence.add(evidence)
        is_year = _is_year_change(old_num, new_num, ctx, raw_text)
        signals.append(
            {
                "kind": KIND,
                "label": _format_label(is_year=is_year, has_new_ref=has_new_ref),
                "evidence": evidence,
            }
        )
    return signals
