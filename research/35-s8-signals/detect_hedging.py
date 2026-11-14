"""Detector for Finnish hedging, attribution flips, and single-word scare quotes (P1.S8, kind='attribution_flip')."""
from __future__ import annotations

import difflib
import re

KIND = "attribution_flip"

# Explicit doubt/claim hedges (strong enough even in a short standalone added/removed sentence)
_STRONG_HEDGE_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("vaitetysti", re.compile(r"\bväitetysti\b", re.IGNORECASE)),
    (
        "vaittaa",
        re.compile(
            r"\bväit(?:tää|ti|tivät|tänyt|täneen|tävät|ettiin|etty|etyn|etään)\b",
            re.IGNORECASE,
        ),
    ),
    ("muka", re.compile(r"\bmuka\b", re.IGNORECASE)),
    ("oletettavasti", re.compile(r"\boletet(?:tavasti|usti)\b", re.IGNORECASE)),
]

# Contextual discourse hedges/attributions (evaluated on in-paragraph edits so routine new sections aren't flagged)
_NON_GENITIVE_WORDS = {
    "kun",
    "niin",
    "kuin",
    "vain",
    "ihan",
    "varsin",
    "usein",
    "toisin",
    "samoin",
    "myöhemmin",
    "aiemmin",
    "jälkeen",
    "ennen",
    "silloin",
    "tällöin",
    "mukaan",
}
_DIRECTIONAL_BEFORE_MUKAAN = re.compile(
    r"(?:ott\w*|otett\w*|läht\w*|tul\w*|liitt\w*|vet\w*|pääs\w*|saat\w*|saan\w*|tuon\w*|jout\w*|lask\w*)$",
    re.IGNORECASE,
)
_AFTER_MUKAAN_IDIOM = re.compile(
    r"^\s*(?:lukien|luettuna|laskien|laskettuna|läht\w+|tul\w+|otett\w+|liitt\w+)\b",
    re.IGNORECASE,
)
_MUKAAN_RE = re.compile(r"\b([a-zäöåA-ZÄÖÅ-]{3,}n)\s+(mukaan(?:sa)?)\b")

_NIIN_SANOTTU_RE = re.compile(
    r"\bniin\s+(?:sanot\w+|kutsut\w+)\b|(?<!\w)n[sk]\.",
    re.IGNORECASE,
)
_TULKITTU_RE = re.compile(r"\btulkit(?:tu|tiin|aan)\b", re.IGNORECASE)

# Factual/proof verbs that form attribution flips when swapped with claim/hedge verbs ("osoitti/todisti" <-> "väitti")
_PROOF_VERB_RE = re.compile(
    r"\b(?:osoit(?:taa|ti|tivat|tanut|taneet|ettiin)|"
    r"todist(?:aa|i|ivat|anut|aneet|ettiin)|"
    r"myön(?:tää|si|sivät|tänyt|täneen|täneet|täneitä))\b",
    re.IGNORECASE,
)
_CLAIM_OR_MODAL_RE = re.compile(
    r"\b(?:väit(?:tää|ti|tivät|tänyt|täneen|tävät|ettiin|etty|etään)|"
    r"välitti\b.*\bkäsityksen|käsityksen|saattoi\s+olla|tulkit(?:tu|tiin|aan)|"
    r"kertoi|katsoi|epäil\w+|oletet(?:tavasti|usti)|väitetysti|muka)\b",
    re.IGNORECASE,
)

# Scare quotes around a single lowercase Finnish word (excludes proper nicknames like Simo "Simuna" Häyhä and XML attrs)
_SCARE_QUOTE_RE = re.compile(r'(?<![=\w])["”„«]([a-zäöå-]{3,})["”»](?![\w])')
_METALANG_BEFORE_QUOTE_RE = re.compile(
    r"\b(?:nimitys\w*|sana\w*|termi\w*|lempinim\w*|otsik\w*|lukee)\s*:?\s*$",
    re.IGNORECASE,
)


def _clean_ws(s: str) -> str:
    s = s.replace("&nbsp;", " ").replace("&#160;", " ").replace("\xad", "")
    return re.sub(r"\s+", " ", s).strip()


def _strip_wiki_for_prose(line: str) -> str:
    line = re.sub(r"<ref\b[^>]*>.*?</ref>", " ", line, flags=re.IGNORECASE | re.DOTALL)
    line = re.sub(r"<ref\b[^>]*/\s*>", " ", line, flags=re.IGNORECASE)
    line = re.sub(r"<!--.*?-->", " ", line, flags=re.DOTALL)
    line = re.sub(r"\[\[(?:File|Image|Kuva|Tiedosto):[^\]]*\]\]", " ", line, flags=re.IGNORECASE)
    line = re.sub(r"\[\[([^|\]]+)\|([^\]]+)\]\]", r"\2", line)
    line = re.sub(r"\[\[([^\]]+)\]\]", r"\1", line)
    line = line.replace("'''", "").replace("''", "")
    return _clean_ws(line)


def _clip_span(text: str, start: int, end: int, radius: int = 55) -> str:
    left = max(0, start - radius)
    right = min(len(text), end + radius)
    if left > 0:
        space = text.find(" ", left)
        if 0 <= space < start:
            left = space + 1
    if right < len(text):
        space = text.rfind(" ", end, right)
        if space > end:
            right = space
    span = text[left:right].strip()
    if left > 0:
        span = "…" + span
    if right < len(text):
        span = span + "…"
    return span


def _truncate(ev: str, limit: int = 200) -> str:
    ev = _clean_ws(ev)
    if len(ev) <= limit:
        return ev
    return ev[: limit - 3].rstrip() + "..."


def _find_mukaan_matches(text: str) -> list[re.Match[str]]:
    out: list[re.Match[str]] = []
    for m in _MUKAAN_RE.finditer(text):
        gen_word = m.group(1).lower()
        if gen_word in _NON_GENITIVE_WORDS or _DIRECTIONAL_BEFORE_MUKAAN.search(gen_word):
            continue
        prefix = text[max(0, m.start() - 25) : m.start()].strip()
        if prefix and _DIRECTIONAL_BEFORE_MUKAAN.search(prefix):
            continue
        suffix = text[m.end() : m.end() + 25]
        if _AFTER_MUKAAN_IDIOM.search(suffix):
            continue
        out.append(m)
    return out


def _find_scare_quotes(text: str) -> list[re.Match[str]]:
    out: list[re.Match[str]] = []
    for m in _SCARE_QUOTE_RE.finditer(text):
        before = text[max(0, m.start() - 30) : m.start()]
        if _METALANG_BEFORE_QUOTE_RE.search(before):
            continue
        out.append(m)
    return out


def _word_root(w: str) -> str:
    wl = w.lower()
    return wl[: max(4, int(len(wl) * 0.65))]


def _extract_hedges(text: str, in_paragraph: bool) -> list[tuple[str, str, str]]:
    """Return list of (equiv_key, matched_surface, context_span) for hedges in `text`."""
    items: list[tuple[str, str, str]] = []
    for cat, rx in _STRONG_HEDGE_PATTERNS:
        for m in rx.finditer(text):
            items.append((cat, m.group(0), _clip_span(text, m.start(), m.end())))

    for m in _find_scare_quotes(text):
        word = m.group(1)
        items.append((f"scare:{_word_root(word)}", m.group(0), _clip_span(text, m.start(), m.end())))

    if in_paragraph:
        for m in _NIIN_SANOTTU_RE.finditer(text):
            items.append(("niin_sanottu", m.group(0), _clip_span(text, m.start(), m.end())))
        for m in _TULKITTU_RE.finditer(text):
            items.append(("tulkittu", m.group(0), _clip_span(text, m.start(), m.end())))
        for m in _find_mukaan_matches(text):
            gen_key = f"mukaan:{_word_root(m.group(1))}"
            items.append((gen_key, m.group(0), _clip_span(text, m.start(), m.end())))
    return items


def _enrich_short_span(span: str, surface: str, prose_lines: list[str]) -> str:
    if len(span) >= 18 or not prose_lines:
        return span
    for line in prose_lines:
        m = re.search(rf"(?<!\w){re.escape(surface)}(?!\w)", line)
        if m:
            return _clip_span(line, m.start(), m.end(), radius=45)
    return span


def detect(cand: dict) -> list[dict]:
    """Return 'attribution_flip' signal dicts (marked low weight) for hedging/attribution edits."""
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []
    if int(stats.get("removed_prose_chars", 0) or 0) > 2500:
        return []

    old_wt = cand.get("old")
    new_wt = cand.get("new")
    has_wt = isinstance(old_wt, str) and isinstance(new_wt, str) and bool(old_wt) and bool(new_wt)
    changed_old_lines: list[str] = []
    changed_new_lines: list[str] = []
    if has_wt:
        old_all = [_strip_wiki_for_prose(x) for x in old_wt.splitlines()]
        new_all = [_strip_wiki_for_prose(x) for x in new_wt.splitlines()]
        sm_lines = difflib.SequenceMatcher(None, old_all, new_all, autojunk=False)
        for tag, i1, i2, j1, j2 in sm_lines.get_opcodes():
            if tag in ("replace", "delete"):
                changed_old_lines.extend(old_all[i1:i2])
            if tag in ("replace", "insert"):
                changed_new_lines.extend(new_all[j1:j2])

    segments = [s for s in diff.get("segments", []) if not s.get("moved")]
    signals: list[dict] = []
    seen_evidence: set[str] = set()

    def _emit(label: str, evidence: str) -> None:
        ev = _truncate(evidence)
        if ev and ev not in seen_evidence:
            seen_evidence.add(ev)
            signals.append({"kind": KIND, "label": label, "evidence": ev})

    by_block: dict[tuple[int | None, int | None], list[dict]] = {}
    all_rem_keys: set[str] = set()
    all_add_keys: set[str] = set()

    for seg in segments:
        if seg.get("kind") not in ("prose", "template"):
            continue
        prose = _clean_ws(seg.get("prose") or "")
        if not prose:
            continue
        key = (seg.get("old_paragraph"), seg.get("new_paragraph"))
        by_block.setdefault(key, []).append({**seg, "_clean_prose": prose})
        # Compute cancellation keys across all non-moved prose segments (including split paragraphs)
        for k, _, _ in _extract_hedges(prose, in_paragraph=True):
            if seg.get("op") == "removed":
                all_rem_keys.add(k)
            elif seg.get("op") == "added":
                all_add_keys.add(k)

    cancelled_keys = all_rem_keys & all_add_keys
    used_scare_roots: set[str] = set()

    for key, bsegs in by_block.items():
        old_p, new_p = key
        in_para = (old_p is not None and new_p is not None) or (old_p is None and new_p is None)
        rem_segs = [s["_clean_prose"] for s in bsegs if s.get("op") == "removed"]
        add_segs = [s["_clean_prose"] for s in bsegs if s.get("op") == "added"]
        rem_joined = " ".join(rem_segs)
        add_joined = " ".join(add_segs)

        rem_items: list[tuple[str, str, str]] = []
        if in_para or len(rem_joined) <= 300:
            for seg_txt in rem_segs:
                rem_items.extend(_extract_hedges(seg_txt, in_paragraph=in_para))

        add_items: list[tuple[str, str, str]] = []
        if in_para or len(add_joined) <= 300:
            for seg_txt in add_segs:
                add_items.extend(_extract_hedges(seg_txt, in_paragraph=in_para))

        # 1. Check factual/proof verb <-> claim/hedge verb flip ("osoitti/todisti" <-> "väitti").
        if in_para and rem_joined and add_joined:
            rem_proof = _PROOF_VERB_RE.search(rem_joined)
            add_proof = _PROOF_VERB_RE.search(add_joined)
            rem_claim = _CLAIM_OR_MODAL_RE.search(rem_joined)
            add_claim = _CLAIM_OR_MODAL_RE.search(add_joined)
            if (rem_proof and not add_proof and add_claim) or (add_proof and not rem_proof and rem_claim):
                left_m = rem_proof or rem_claim
                right_m = add_proof or add_claim
                assert left_m is not None and right_m is not None
                left_span = _clip_span(rem_joined, left_m.start(), left_m.end(), radius=40)
                right_span = _clip_span(add_joined, right_m.start(), right_m.end(), radius=40)
                _emit("Attribution verb flipped (low weight)", f"{left_span} → {right_span}")
                if rem_claim and add_proof:
                    rem_items = [it for it in rem_items if it[1].lower() != rem_claim.group(0).lower()]
                if add_claim and rem_proof:
                    add_items = [it for it in add_items if it[1].lower() != add_claim.group(0).lower()]

        # 2. Emit remaining uncancelled removed and added hedges / scare quotes.
        for k, surf, span in rem_items:
            if k in cancelled_keys:
                continue
            rich_span = _enrich_short_span(span, surf, changed_old_lines)
            if k.startswith("scare:"):
                used_scare_roots.add(k.split(":", 1)[1])
                _emit("Scare quotes removed around word (low weight)", f"- {rich_span}")
            else:
                _emit("Hedge or attribution removed (low weight)", f"- {rich_span}")

        for k, surf, span in add_items:
            if k in cancelled_keys:
                continue
            rich_span = _enrich_short_span(span, surf, changed_new_lines)
            if k.startswith("scare:"):
                used_scare_roots.add(k.split(":", 1)[1])
                _emit("Scare quotes added around word (low weight)", f"+ {rich_span}")
            else:
                _emit("Hedge or attribution added (low weight)", f"+ {rich_span}")

    # 3. Catch scare quotes added/removed around an unchanged word in old/new wikitext
    # (wikidiff emits only markup '"' segments when the word itself did not change).
    if has_wt:
        old_lines = [_strip_wiki_for_prose(x) for x in old_wt.splitlines()]
        new_lines = [_strip_wiki_for_prose(x) for x in new_wt.splitlines()]
        sm = difflib.SequenceMatcher(None, old_lines, new_lines, autojunk=False)
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag != "replace":
                continue
            ot = " ".join(old_lines[i1:i2])
            nt = " ".join(new_lines[j1:j2])
            for m in _find_scare_quotes(nt):
                w = m.group(1)
                root = _word_root(w)
                if root in used_scare_roots:
                    continue
                if re.search(rf"\b{re.escape(w)}\b", ot) and not re.search(rf'["”„«]{re.escape(w)}["”»]', ot):
                    used_scare_roots.add(root)
                    span = _clip_span(nt, m.start(), m.end(), radius=45)
                    _emit("Scare quotes added around word (low weight)", f'{w} → "{w}" ({span})')
            for m in _find_scare_quotes(ot):
                w = m.group(1)
                root = _word_root(w)
                if root in used_scare_roots:
                    continue
                if re.search(rf"\b{re.escape(w)}\b", nt) and not re.search(rf'["”„«]{re.escape(w)}["”»]', nt):
                    used_scare_roots.add(root)
                    span = _clip_span(ot, m.start(), m.end(), radius=45)
                    _emit("Scare quotes removed around word (low weight)", f'"{w}" → {w} ({span})')

    return signals
