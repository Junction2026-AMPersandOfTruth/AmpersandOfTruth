"""P1.S10 (#37): Assemble `IncidentEvidence` and candidate metadata per S7 claim cluster.

Separates pure mapping helpers (unit-tested offline in `test_s10.py`) from polite PK-indexed MariaDB
loaders over `research.s7_cluster`, `s7_member`, `s3_diff`, `s1_revert`, `s4_episode*`,
`s5_revert_reason`, `s6_rev_author` + `s6_editor_profile`, `corpus.revision`, and `corpus.revision_text`.

Python 3.10+, standard library only.
"""
from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, replace
from datetime import datetime, timedelta, timezone
import importlib.util
import json
import pathlib
import re
import sys
import time
from typing import Any
from urllib.parse import urlparse

HERE = pathlib.Path(__file__).resolve().parent
RESEARCH_DIR = HERE.parent
sys.path.insert(0, str(RESEARCH_DIR / "lib"))
sys.path.insert(0, str(RESEARCH_DIR / "eval"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
import contract  # noqa: E402
from tiers import (  # noqa: E402
    ContentAssessment,
    ContentCategory,
    IncidentEvidence,
    RemovalReason,
)


def _load_s7_variant():
    spec = importlib.util.spec_from_file_location("s7_variant", RESEARCH_DIR / "34-s7-repeat" / "variant.py")
    if spec is None or spec.loader is None:
        raise RuntimeError("Cannot load research/34-s7-repeat/variant.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _load_s7_cluster():
    spec = importlib.util.spec_from_file_location("s7_cluster", RESEARCH_DIR / "34-s7-repeat" / "cluster.py")
    if spec is None or spec.loader is None:
        raise RuntimeError("Cannot load research/34-s7-repeat/cluster.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules["s7_cluster"] = mod
    spec.loader.exec_module(mod)
    return mod


_s7_variant = _load_s7_variant()
_s7_cluster = _load_s7_cluster()
parse_rev_list = _s7_variant.parse_rev_list
s7_normalise_text = _s7_cluster.normalise_text
s7_word_shingles = _s7_cluster.word_shingles
s7_jaccard = _s7_cluster.jaccard
s7_extract_sentences = _s7_cluster.extract_sentences
s7_sentence_hash = _s7_cluster.sentence_hash
S7_JACCARD_THRESHOLD: float = float(_s7_cluster.JACCARD_THRESHOLD)
S7_SHINGLE_WORDS: int = int(_s7_cluster.SHINGLE_WORDS)

NL = chr(10)

EDIT_WAR_SOURCES: frozenset[str] = frozenset({"s4", "passage"})

VALID_DISABLES: frozenset[str] = frozenset(
    {
        "removal_reason",
        "reinsertion",
        "edit_war",
        "dispute_template",
        "assessment",
    }
)

DISPUTE_TEMPLATE_RE = re.compile(
    r"(?i)\{\{\s*(neutraalius|lähteetön|lähde\?|korjattava|kiistanalainen|tarkista|puolueellinen)"
)

_SENTENCE_SPLIT_RE = re.compile("(?:[" + chr(13) + chr(10) + r"]+|\s*…\s*|(?<=[.!?])\s+)")
_URL_RE = re.compile(r"https?://[^\s<>\]|}'\x22]+", re.IGNORECASE)
_REF_NAME_RE = re.compile(r"<ref\b[^>]*?\bname\s*=\s*(?:\x22([^\x22]+)\x22|'([^']+)'|([^\s/>]+))", re.IGNORECASE)
_NESTED_REF_SPLIT_RE = re.compile(r"(?=<ref[\s>/])", re.IGNORECASE)
_ARCHIVE_UNWRAP_RE = re.compile(
    r"^https?://(?:web\.archive\.org/web/\d+[a-z_]*|archive\.(?:is|today|ph)/[^/]+)/(https?://.+)$",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class AssessmentMeta:
    model: str | None
    category: ContentCategory
    confidence: int
    has_verified_quote: bool
    quotes: tuple[str, ...]
    claim_en: str | None
    claim_fi: str | None
    what_changed_en: str | None
    narrative_en: str | None
    uncertainty_note: str | None
    historical_note_en: str | None
    input_tokens: int = 0
    output_tokens: int = 0
    cost_usd: float = 0.0
    from_cache: bool = False


@dataclass(frozen=True)
class CandidateCluster:
    cluster_id: int
    incident_key: str
    page_id: int
    other_page_ids: tuple[int, ...]
    member_page_ids: frozenset[int]
    inserting_rev_ids: tuple[int, ...]
    removing_rev_ids: tuple[int, ...]
    evidence: IncidentEvidence
    diff_source_manipulation: bool
    prose_chars: int
    total_exposure_seconds: int
    first_inserted_at: str
    last_removed_at: str
    inserted_raw_text: str
    inserted_prose_text: str
    articles: tuple[dict[str, Any], ...]
    assessment_meta: AssessmentMeta | None
    missing_s3_diffs: int = 0


# ── Pure helpers (offline testable) ──────────────────────────────────────────

def parse_utc(ts: str | datetime) -> datetime:
    if isinstance(ts, datetime):
        return ts.replace(tzinfo=timezone.utc) if ts.tzinfo is None else ts.astimezone(timezone.utc)
    raw = str(ts).strip()
    if raw.endswith("Z"):
        raw = raw[:-1]
    dt = datetime.fromisoformat(raw.replace(" ", "T"))
    return dt.replace(tzinfo=timezone.utc) if dt.tzinfo is None else dt.astimezone(timezone.utc)


def format_iso_z(ts: str | datetime) -> str:
    return parse_utc(ts).strftime("%Y-%m-%dT%H:%M:%SZ")


def to_removal_reason(categories: set[str] | frozenset[str] | list[str] | tuple[str, ...]) -> RemovalReason:
    """Map S5 `removal_reasons` tags using `RevertReasons.cs` priority order."""
    cats = {c.strip() for c in categories if c and c.strip()}
    if "factual_error" in cats:
        return RemovalReason.ACCURACY
    if "neutrality_pov" in cats:
        return RemovalReason.NEUTRALITY
    if "sourcing" in cats or "original_research" in cats:
        return RemovalReason.SOURCING
    if "vandalism_test" in cats:
        return RemovalReason.VANDALISM
    if cats:
        return RemovalReason.OTHER
    return RemovalReason.NONE


def _iter_segments(diff_row: dict[str, Any]) -> list[dict[str, Any]]:
    raw = diff_row.get("segments")
    if not raw:
        return []
    segs = json.loads(raw) if isinstance(raw, str) else raw
    return segs if isinstance(segs, list) else []


def has_dispute_template_added(diff_rows: list[dict[str, Any]]) -> bool:
    """True iff an added non-moved template segment matches `DISPUTE_TEMPLATE_RE`."""
    for row in diff_rows:
        if row.get("error"):
            continue
        for seg in _iter_segments(row):
            if seg.get("moved") or seg.get("op") != "added" or seg.get("kind") != "template":
                continue
            if DISPUTE_TEMPLATE_RE.search(seg.get("text") or ""):
                return True
    return False


def _norm_url(u: str) -> str:
    u = u.rstrip(".,;:!?)")
    m = _ARCHIVE_UNWRAP_RE.match(u)
    orig = m.group(1) if m else u
    return re.sub(r"^https?://(www\.)?", "", orig.lower()).rstrip("/")


def _parse_ref_block(raw: str) -> dict[str, Any]:
    m_name = _REF_NAME_RE.search(raw)
    name = ((m_name.group(1) or m_name.group(2) or m_name.group(3) or "").strip().lower()) if m_name else ""
    self_closing = bool(re.match(r"^\s*<ref\b[^>]*/>\s*$", raw, re.IGNORECASE | re.DOTALL))
    body = re.sub(r"^\s*<ref\b[^>]*>", "", raw, flags=re.IGNORECASE | re.DOTALL)
    body = re.sub(r"</ref\s*>\s*$", "", body, flags=re.IGNORECASE | re.DOTALL)
    body = re.sub(r"<!--.*?-->", " ", body, flags=re.DOTALL)
    core = " ".join(body.split())
    urls = {_norm_url(u) for u in _URL_RE.findall(body)}
    domains: set[str] = set()
    for u in _URL_RE.findall(body):
        m = _ARCHIVE_UNWRAP_RE.match(u.rstrip(".,;:!?)"))
        orig = m.group(1) if m else u.rstrip(".,;:!?)")
        try:
            dom = (urlparse(orig).hostname or "").lower().removeprefix("www.")
        except ValueError:
            dom = ""
        if dom and dom != "web.archive.org":
            domains.add(dom)
    is_empty = (not self_closing) and (not urls) and len(core) < 3
    return {
        "name": name,
        "self_closing": self_closing,
        "core": core,
        "urls": urls,
        "domains": domains,
        "is_empty": is_empty,
    }


def _same_ref_source(old_r: dict[str, Any], new_r: dict[str, Any]) -> bool:
    if old_r["name"] and old_r["name"] == new_r["name"] and (old_r["self_closing"] or new_r["self_closing"]):
        return True
    if len(old_r["core"]) >= 15 and (old_r["core"] in new_r["core"] or new_r["core"] in old_r["core"]):
        return True
    if old_r["urls"] and new_r["urls"] and (old_r["urls"] & new_r["urls"]):
        return True
    return False


def has_ref_manipulation(diff_rows: list[dict[str, Any]]) -> bool:
    """True iff a non-blanking member diff removes or replaces a `<ref>` while adding prose."""
    for row in diff_rows:
        if row.get("error") or int(row.get("is_blanking") or 0) == 1:
            continue
        segs = [s for s in _iter_segments(row) if not s.get("moved")]
        added_prose_chars = int(row.get("added_prose_chars") or 0)
        has_added_prose = added_prose_chars > 0 or any(
            s.get("op") == "added" and s.get("kind") == "prose" and (s.get("prose") or s.get("text") or "").strip()
            for s in segs
        )
        if not has_added_prose:
            continue

        rem_refs: list[tuple[dict[str, Any], dict[str, Any]]] = []
        add_refs: list[tuple[dict[str, Any], dict[str, Any]]] = []
        for seg in segs:
            if seg.get("kind") != "reference":
                continue
            parts = [p for p in _NESTED_REF_SPLIT_RE.split(seg.get("text") or "") if p.strip()] or [
                seg.get("text") or ""
            ]
            if seg.get("op") == "removed":
                for p in parts:
                    parsed = _parse_ref_block(p)
                    if not parsed["is_empty"]:
                        rem_refs.append((seg, parsed))
            elif seg.get("op") == "added":
                for p in parts:
                    parsed = _parse_ref_block(p)
                    if not parsed["is_empty"]:
                        add_refs.append((seg, parsed))

        if not rem_refs:
            continue

        used_add: set[int] = set()
        unmatched_rem: list[tuple[dict[str, Any], dict[str, Any]]] = []
        for rs, rinfo in rem_refs:
            matched = False
            for ai, (_, ainfo) in enumerate(add_refs):
                if ai in used_add:
                    continue
                if _same_ref_source(rinfo, ainfo):
                    used_add.add(ai)
                    matched = True
                    break
            if not matched:
                unmatched_rem.append((rs, rinfo))

        for rs, _ in unmatched_rem:
            # Ensure the paragraph remained or was edited (not an entire deleted unrelated paragraph)
            if rs.get("new_paragraph") is not None or not segs:
                return True
            op = rs.get("old_paragraph")
            if op is not None and any(
                s.get("op") == "added" and s.get("kind") == "prose" and s.get("old_paragraph") == op for s in segs
            ):
                return True
    return False


def longest_added_sentence(diff_rows: list[dict[str, Any]], min_chars: int = 25) -> str | None:
    """Return the longest added prose sentence (`>= min_chars`) across `diff_rows`, or `None`."""
    best = ""
    for row in diff_rows:
        if row.get("error"):
            continue
        prose = row.get("added_prose")
        if not prose:
            parts = [
                (s.get("prose") or "").strip()
                for s in _iter_segments(row)
                if not s.get("moved") and s.get("op") == "added" and s.get("kind") == "prose"
            ]
            prose = NL.join(p for p in parts if p)
        for chunk in _SENTENCE_SPLIT_RE.split(prose or ""):
            s = chunk.strip()
            if len(s) >= min_chars and len(s) > len(best):
                best = s
    return best or None


def extract_inserted_texts(diff_rows: list[dict[str, Any]]) -> tuple[str, str]:
    """Return `(inserted_raw_text, inserted_prose_text)` across `diff_rows` for quote verification & B+rel."""
    raw_parts: list[str] = []
    prose_parts: list[str] = []
    seen_raw: set[str] = set()
    seen_prose: set[str] = set()

    for row in diff_rows:
        if row.get("error"):
            continue
        segs = _iter_segments(row)
        if segs:
            for seg in segs:
                if seg.get("moved") or seg.get("op") != "added":
                    continue
                t = (seg.get("text") or "").strip()
                if t and t not in seen_raw:
                    seen_raw.add(t)
                    raw_parts.append(t)
                if seg.get("kind") in ("prose", "template"):
                    p = (seg.get("prose") or "").strip()
                    if p and p not in seen_prose:
                        seen_prose.add(p)
                        prose_parts.append(p)
        ap = (row.get("added_prose") or "").strip()
        if ap and ap not in seen_prose:
            seen_prose.add(ap)
            prose_parts.append(ap)

    raw_joined = NL.join(raw_parts)
    prose_joined = NL.join(prose_parts)
    if not raw_joined:
        raw_joined = prose_joined
    return raw_joined, prose_joined


def _clean_optional_str(val: Any) -> str | None:
    if val is None or not isinstance(val, str):
        return None
    s = aot.mask_comment(val).strip()
    return s if s else None


def parse_assessment_row(
    row: dict[str, Any] | None,
    inserted_raw_text: str,
    inserted_prose_text: str = "",
) -> tuple[ContentAssessment | None, AssessmentMeta | None]:
    """Parse one `classify-edit` batch output row and compute `HasVerifiedQuote`."""
    if not row or str(row.get("outcome") or "").lower() != "ok":
        return None, None
    val = row.get("value")
    if not isinstance(val, dict):
        return None, None

    cat_raw = str(val.get("category") or "").strip()
    try:
        cat = ContentCategory(cat_raw)
    except ValueError:
        return None, None

    conf_raw = int(val.get("confidence") or 0)
    conf = max(1, min(5, conf_raw)) if conf_raw > 0 else 1

    quotes = tuple(q.strip() for q in (val.get("evidence") or ()) if isinstance(q, str) and q.strip())
    has_verified_quote = any(
        len(q) >= 3 and (q in inserted_raw_text or (bool(inserted_prose_text) and q in inserted_prose_text))
        for q in quotes
    )

    ca = ContentAssessment(category=cat, confidence=conf, has_verified_quote=has_verified_quote)
    meta = AssessmentMeta(
        model=str(row.get("model")) if row.get("model") else None,
        category=cat,
        confidence=conf,
        has_verified_quote=has_verified_quote,
        quotes=quotes,
        claim_en=_clean_optional_str(val.get("claim_en")),
        claim_fi=_clean_optional_str(val.get("claim_fi")),
        what_changed_en=_clean_optional_str(val.get("what_changed_en")),
        narrative_en=_clean_optional_str(val.get("narrative_en")),
        uncertainty_note=_clean_optional_str(val.get("uncertainty_note")),
        historical_note_en=_clean_optional_str(val.get("historical_note_en")),
        input_tokens=max(0, int(row.get("input_tokens") or 0)),
        output_tokens=max(0, int(row.get("output_tokens") or 0)),
        cost_usd=max(0.0, float(row.get("cost_usd") or 0.0)),
        from_cache=bool(row.get("from_cache") or row.get("cache_hit")),
    )
    return ca, meta


def load_assessments(
    path_spec: str | pathlib.Path | None,
    model: str | None = None,
) -> dict[str, dict[str, Any]]:
    """Load `classify-edit` batch JSONL (`path` or `path:model`) into `{incident_key: row}`."""
    if path_spec is None:
        return {}
    raw_spec = str(path_spec)
    chosen_model = model
    file_path = pathlib.Path(raw_spec)
    if not file_path.exists() and ":" in raw_spec:
        lhs, rhs = raw_spec.rsplit(":", 1)
        if pathlib.Path(lhs).exists():
            file_path = pathlib.Path(lhs)
            chosen_model = chosen_model or rhs

    rows: list[dict[str, Any]] = []
    with file_path.open(encoding="utf-8") as f:
        for line in f:
            if line.strip():
                rows.append(json.loads(line))

    models = sorted({str(r.get("model")) for r in rows if r.get("model")})
    if chosen_model is None and len(models) > 1:
        if "gemini-4-carbon" in models:
            chosen_model = "gemini-4-carbon"
        else:
            raise ValueError(f"Multiple models in assessments file {models}; pass --assessment-model.")

    out: dict[str, dict[str, Any]] = {}
    for r in rows:
        if chosen_model and str(r.get("model") or "") != chosen_model:
            continue
        rid = r.get("id") if r.get("id") is not None else r.get("_id")
        if rid is not None:
            out[str(rid)] = r
    return out


def _compute_exposure_seconds(ordered_members: list[dict[str, Any]]) -> int:
    """Sum non-overlapping `[inserted_at, removed_at]` exposure windows per page across cluster members."""
    by_page: dict[int, list[tuple[datetime, datetime]]] = defaultdict(list)
    for m in ordered_members:
        t0 = parse_utc(m["inserted_at"])
        t1 = parse_utc(m["removed_at"])
        if t1 > t0:
            by_page[int(m["page_id"])].append((t0, t1))

    total = 0.0
    for intervals in by_page.values():
        intervals.sort(key=lambda iv: (iv[0], iv[1]))
        cur_s, cur_e = intervals[0]
        for s, e in intervals[1:]:
            if s <= cur_e:
                if e > cur_e:
                    cur_e = e
            else:
                total += (cur_e - cur_s).total_seconds()
                cur_s, cur_e = s, e
        total += (cur_e - cur_s).total_seconds()
    return max(0, int(round(total)))


def _extract_span_side_parts(diff_row: dict[str, Any] | None, op: str) -> list[str]:
    """Extract added (`op='added'`) or removed (`op='removed'`) text parts from an S3 span diff row."""
    if not diff_row or diff_row.get("error"):
        return []
    col = "added_prose" if op == "added" else "removed_prose"
    prose_col = (diff_row.get(col) or "").strip()
    seg_prose_parts: list[str] = []
    seg_ref_parts: list[str] = []
    for seg in _iter_segments(diff_row):
        if seg.get("moved") or seg.get("op") != op:
            continue
        kind = seg.get("kind")
        if kind in ("prose", "template"):
            val = (seg.get("prose") or "").strip()
            if val:
                seg_prose_parts.append(val)
        elif kind == "reference":
            val = (seg.get("text") or "").strip()
            if val:
                seg_ref_parts.append(val)
    if prose_col:
        return [prose_col, *seg_ref_parts]
    return [*seg_prose_parts, *seg_ref_parts]


def _unit_side_features(
    diff_row: dict[str, Any] | None,
) -> tuple[dict[str, Any], dict[str, Any]]:
    """Build normalised S7 shingle + distinctive sentence-hash features for `(added, removed)` of a span."""
    add_parts = _extract_span_side_parts(diff_row, "added")
    rem_parts = _extract_span_side_parts(diff_row, "removed")
    a_norm = s7_normalise_text(NL.join(add_parts))
    r_norm = s7_normalise_text(NL.join(rem_parts))
    a_shingles = s7_word_shingles(a_norm, S7_SHINGLE_WORDS)
    r_shingles = s7_word_shingles(r_norm, S7_SHINGLE_WORDS)
    a_sents = {s7_sentence_hash(s) for s in s7_extract_sentences(add_parts)}
    r_sents = {s7_sentence_hash(s) for s in s7_extract_sentences(rem_parts)}
    return (
        {"norm": a_norm, "shingles": a_shingles, "sents": a_sents - r_sents},
        {"norm": r_norm, "shingles": r_shingles, "sents": r_sents - a_sents},
    )


def _pair_side_alignment(
    u_a: dict[str, Any],
    u_r: dict[str, Any],
    ref_x: dict[str, Any],
    ref_y: dict[str, Any],
) -> tuple[bool, float]:
    """Check whether `(u_a, u_r)` aligns with `(ref_x, ref_y)` via S7 Jaccard >= 0.6 or sentence-hash overlap."""
    j_ax = (
        1.0
        if (u_a["norm"] and u_a["norm"] == ref_x["norm"] and u_a["norm"] != ref_y["norm"])
        else s7_jaccard(u_a["shingles"], ref_x["shingles"])
    )
    j_ry = (
        1.0
        if (u_r["norm"] and u_r["norm"] == ref_y["norm"] and u_r["norm"] != ref_x["norm"])
        else s7_jaccard(u_r["shingles"], ref_y["shingles"])
    )
    best_j = max(j_ax, j_ry)
    sent_hit = bool((u_a["sents"] & ref_x["sents"]) or (u_r["sents"] & ref_y["sents"]))
    matched = (best_j >= S7_JACCARD_THRESHOLD) or sent_hit
    strength = best_j + (0.5 if sent_hit else 0.0)
    return matched, strength


def orient_cluster_units(
    ordered_members: list[dict[str, Any]],
    span_diffs_by_rev: dict[int, dict[str, Any]],
) -> tuple[int, ...]:
    """Orient each chronologically ordered unit in a cluster as side `1` (earliest claim) or `2` (opposite side).

    A unit whose added text matches what an earlier side-1 unit removed (or what an earlier side-2 unit added),
    or whose removed text matches what an earlier side-1 unit added (or what an earlier side-2 unit removed) —
    under S7 text normalisation (word 5-shingle Jaccard >= 0.6 or distinctive sentence-hash overlap) — is placed
    on side `2` whenever its opposite-side match is stronger than any same-side match.
    """
    if not ordered_members:
        return ()
    if len(ordered_members) == 1:
        return (1,)

    feats = [
        _unit_side_features(span_diffs_by_rev.get(int(m["reverting_rev_id"])))
        for m in ordered_members
    ]
    sides: list[int] = [1]
    for i in range(1, len(ordered_members)):
        a_i, r_i = feats[i]
        opp_hit, same_hit = False, False
        opp_best, same_best = 0.0, 0.0
        for j in range(i):
            a_j, r_j = feats[j]
            x_j, y_j = (a_j, r_j) if sides[j] == 1 else (r_j, a_j)
            m_opp, s_opp = _pair_side_alignment(a_i, r_i, y_j, x_j)
            m_same, s_same = _pair_side_alignment(a_i, r_i, x_j, y_j)
            if m_opp:
                opp_hit = True
                if s_opp > opp_best:
                    opp_best = s_opp
            if m_same:
                same_hit = True
                if s_same > same_best:
                    same_best = s_same
        sides.append(2 if (opp_hit and (not same_hit or opp_best > same_best)) else 1)
    return tuple(sides)


def build_cluster_candidate(
    cluster_row: dict[str, Any],
    member_rows: list[dict[str, Any]],
    rev_by_id: dict[int, dict[str, Any]],
    span_diffs_by_rev: dict[int, dict[str, Any]],
    s1_class_by_rev: dict[int, str],
    s4_war_revs: set[int] | frozenset[int],
    s5_reasons_by_rev: dict[int, str],
    s6_bot_by_rev: dict[int, int | bool],
    kept_in_latest: bool = False,
    assessment_row: dict[str, Any] | None = None,
    titles_by_page: dict[int, str] | None = None,
    edit_diffs_by_rev: dict[int, dict[str, Any]] | None = None,
    sides: bool = True,
    edit_war_source: str = "passage",
) -> CandidateCluster:
    """Assemble one `CandidateCluster` and its `IncidentEvidence` from structured table rows."""
    if edit_war_source not in EDIT_WAR_SOURCES:
        raise ValueError(f"Unknown edit_war_source {edit_war_source!r}; valid: {sorted(EDIT_WAR_SOURCES)}")

    cid = int(cluster_row["cluster_id"])
    ordered = sorted(
        member_rows,
        key=lambda m: (str(m["inserted_at"]), int(m["reverting_rev_id"])),
    )
    if not ordered:
        raise ValueError(f"Cluster {cid} has no member rows.")

    unit_sides = orient_cluster_units(ordered, span_diffs_by_rev) if sides else (1,) * len(ordered)
    side1_units = [m for m, s in zip(ordered, unit_sides) if s == 1]
    side2_units = [m for m, s in zip(ordered, unit_sides) if s == 2]

    page_id = int(side1_units[0]["page_id"])
    other_page_ids_list: list[int] = []
    seen_pages: set[int] = {page_id}
    for m in side1_units[1:]:
        pid = int(m["page_id"])
        if pid not in seen_pages:
            seen_pages.add(pid)
            other_page_ids_list.append(pid)

    ins_set: set[int] = set()
    for m in side1_units:
        ins_set.update(parse_rev_list(m["inserting_rev_ids"]))
    inserting_rev_ids = tuple(sorted(ins_set))

    # Side-1 units are removed by their reverting_rev_id; side-2 units added the counter-text (removing side 1),
    # while side-2 reverting_rev_ids restored the side-1 claim and are NOT removals of the claim.
    side1_rem_revs = {int(m["reverting_rev_id"]) for m in side1_units}
    side2_rem_revs = {r for m in side2_units for r in parse_rev_list(m["inserting_rev_ids"])}
    claim_removing = sorted(side1_rem_revs | side2_rem_revs)
    rem_filtered = [r for r in claim_removing if r not in ins_set]
    if rem_filtered:
        removing_rev_ids = tuple(rem_filtered)
    else:
        all_non_ins = sorted(({int(m["reverting_rev_id"]) for m in ordered} | side2_rem_revs) - ins_set)
        removing_rev_ids = tuple([all_non_ins[-1]] if all_non_ins else [int(ordered[-1]["reverting_rev_id"])])

    inc_key = contract.incident_key(list(inserting_rev_ids))

    # Side-1 member span diffs (+ optional inserting edit diffs for exact quote matching)
    span_diffs: list[dict[str, Any]] = []
    missing_s3 = 0
    for m in side1_units:
        rr = int(m["reverting_rev_id"])
        sd = span_diffs_by_rev.get(rr)
        if sd is None or sd.get("error"):
            missing_s3 += 1
        else:
            span_diffs.append(sd)

    # 1. IsSubstantive & IsBlanking (any side-1 member span is_substantive=1 / all blanking)
    is_substantive = any(int(d.get("is_substantive") or 0) == 1 for d in span_diffs)
    is_blanking = len(span_diffs) > 0 and all(int(d.get("is_blanking") or 0) == 1 for d in span_diffs)

    # 2. Editor grouping keys -> RemovedByOtherAccount & DistinctInsertingEditors (side-1 inserters vs claim removers)
    ins_users: set[str] = set()
    for r in inserting_rev_ids:
        rm = rev_by_id.get(r)
        raw_u = (rm.get("user") if rm else None) or ""
        if raw_u.strip():
            ins_users.add(aot.user_display(raw_u))

    rem_users: set[str] = set()
    for r in removing_rev_ids:
        rm = rev_by_id.get(r)
        raw_u = (rm.get("user") if rm else None) or ""
        if raw_u.strip():
            rem_users.add(aot.user_display(raw_u))

    non_remover_inserters = ins_users - rem_users
    removed_by_other = (len(ins_users) == 0) or (len(non_remover_inserters) > 0) or (len(rem_users - ins_users) > 0)
    if non_remover_inserters:
        distinct_editors = len(non_remover_inserters)
    elif ins_users:
        distinct_editors = len(ins_users)
    else:
        distinct_editors = max(1, len({m.get("account_key") for m in side1_units if m.get("account_key")}))

    # 3. InsertedByBot (`s6_editor_profile.is_bot` or S1 class `bot`)
    all_ins_bot = len(inserting_rev_ids) > 0 and all(bool(s6_bot_by_rev.get(r)) for r in inserting_rev_ids)
    all_s1_bot = len(side1_rem_revs) > 0 and all(s1_class_by_rev.get(r) == "bot" for r in side1_rem_revs)
    inserted_by_bot = all_ins_bot or all_s1_bot

    # 4. RemovalReason from `s5_revert_reason.removal_reasons` of claim-removing revs
    reason_tags: set[str] = set()
    for r in claim_removing:
        raw_reasons = s5_reasons_by_rev.get(r) or ""
        for tag in raw_reasons.split(","):
            if tag.strip():
                reason_tags.add(tag.strip())
    removal_reason = to_removal_reason(reason_tags)

    # 5. ReinsertionCount & InsertionSpan (side-1 units only)
    first_rem_dt = min(parse_utc(m["removed_at"]) for m in side1_units)
    reinsertion_units = [m for m in side1_units if parse_utc(m["inserted_at"]) >= first_rem_dt]
    reinsertion_count = len(reinsertion_units)

    ins_dts: list[datetime] = [
        parse_utc(rev_by_id[r]["ts"]) for r in inserting_rev_ids if r in rev_by_id and rev_by_id[r].get("ts")
    ]
    if not ins_dts:
        ins_dts = [parse_utc(m["inserted_at"]) for m in side1_units]
    first_ins_dt = min(ins_dts)
    last_ins_dt = max(ins_dts)
    insertion_span = max(timedelta(0), last_ins_dt - first_ins_dt)

    last_rem_dt = max(
        [parse_utc(rev_by_id[r]["ts"]) for r in removing_rev_ids if r in rev_by_id and rev_by_id[r].get("ts")]
        or [parse_utc(m["removed_at"]) for m in side1_units]
    )

    # 6. InEditWar (`passage` = >=2 claim removals & >=1 re-insertion by different editor camps; `s4` = page episode)
    if edit_war_source == "s4":
        all_cluster_revs = set(inserting_rev_ids) | set(claim_removing)
        in_edit_war = any(r in s4_war_revs for r in all_cluster_revs)
    else:
        unmirrored_side2_removals = sum(
            1
            for m2 in side2_units
            if (m2_ins := set(parse_rev_list(m2["inserting_rev_ids"]))) and not (m2_ins & side1_rem_revs)
        )
        removal_count = len(side1_rem_revs) + unmirrored_side2_removals
        reinserter_users: set[str] = set()
        for m in reinsertion_units:
            for r in parse_rev_list(m["inserting_rev_ids"]):
                rm = rev_by_id.get(r)
                raw_u = (rm.get("user") if rm else None) or ""
                if raw_u.strip():
                    reinserter_users.add(aot.user_display(raw_u))
        different_camps = bool(reinserter_users - rem_users) and bool(rem_users - reinserter_users)
        in_edit_war = (removal_count >= 2) and (reinsertion_count >= 1) and different_camps

    # 7. DisputeTemplateAdded & diff-side SourceManipulation
    edit_diffs = (
        [edit_diffs_by_rev[r] for r in inserting_rev_ids if r in edit_diffs_by_rev and not edit_diffs_by_rev[r].get("error")]
        if edit_diffs_by_rev
        else []
    )
    all_member_diffs = span_diffs + edit_diffs
    dispute_template_added = has_dispute_template_added(all_member_diffs)
    diff_source_manip = has_ref_manipulation(span_diffs) or (bool(edit_diffs) and has_ref_manipulation(edit_diffs))

    # 8. Inserted texts & optional S9 assessment
    raw_span_text, prose_span_text = extract_inserted_texts(span_diffs)
    if edit_diffs:
        raw_edit_text, prose_edit_text = extract_inserted_texts(edit_diffs)
        inserted_raw_text = NL.join(x for x in (raw_edit_text, raw_span_text) if x)
        inserted_prose_text = NL.join(x for x in (prose_edit_text, prose_span_text) if x)
    else:
        inserted_raw_text, inserted_prose_text = raw_span_text, prose_span_text

    if not inserted_prose_text and cluster_row.get("sample_text"):
        inserted_prose_text = str(cluster_row["sample_text"])
    if not inserted_raw_text:
        inserted_raw_text = inserted_prose_text

    assessment, assessment_meta = parse_assessment_row(
        assessment_row,
        inserted_raw_text=inserted_raw_text,
        inserted_prose_text=inserted_prose_text,
    )

    source_manip = diff_source_manip or (
        assessment is not None and assessment.category == ContentCategory.SOURCE_MANIPULATION
    )

    distinct_articles = (
        len(seen_pages)
        if side2_units
        else max(1, int(cluster_row.get("n_pages") or len(seen_pages)))
    )

    ev = IncidentEvidence(
        is_substantive=is_substantive,
        removed_by_other_account=removed_by_other,
        inserted_by_bot=inserted_by_bot,
        is_blanking=is_blanking,
        kept_in_latest_revision=bool(kept_in_latest),
        assessment=assessment,
        removal_reason=removal_reason,
        reinsertion_count=reinsertion_count,
        in_edit_war=in_edit_war,
        dispute_template_added=dispute_template_added,
        distinct_inserting_editors=max(1, distinct_editors),
        distinct_articles=distinct_articles,
        insertion_span=insertion_span,
        source_manipulation=source_manip,
    )

    titles = titles_by_page or {}
    ordered_pages = [page_id, *other_page_ids_list]
    articles = tuple({"page_id": pid, "title_fi": titles.get(pid)} for pid in ordered_pages)

    side1_max_prose = max((int(d.get("added_prose_chars") or 0) for d in span_diffs), default=0)
    prose_chars = (
        side1_max_prose
        if (side2_units and side1_max_prose > 0)
        else max(int(cluster_row.get("prose_chars") or 0), side1_max_prose)
    )

    return CandidateCluster(
        cluster_id=cid,
        incident_key=inc_key,
        page_id=page_id,
        other_page_ids=tuple(other_page_ids_list),
        member_page_ids=frozenset(ordered_pages),
        inserting_rev_ids=inserting_rev_ids,
        removing_rev_ids=removing_rev_ids,
        evidence=ev,
        diff_source_manipulation=diff_source_manip,
        prose_chars=prose_chars,
        total_exposure_seconds=_compute_exposure_seconds(side1_units),
        first_inserted_at=format_iso_z(first_ins_dt),
        last_removed_at=format_iso_z(last_rem_dt),
        inserted_raw_text=inserted_raw_text,
        inserted_prose_text=inserted_prose_text,
        articles=articles,
        assessment_meta=assessment_meta,
        missing_s3_diffs=missing_s3,
    )


def apply_disables(
    cand: CandidateCluster,
    disabled: set[str] | frozenset[str] = frozenset(),
) -> tuple[IncidentEvidence, AssessmentMeta | None]:
    """Return `(evidence, assessment_meta)` with requested signals ablated for S11."""
    if not disabled:
        return cand.evidence, cand.assessment_meta
    unknown = set(disabled) - VALID_DISABLES
    if unknown:
        raise ValueError(f"Unknown --disable signal(s) {sorted(unknown)}; valid: {sorted(VALID_DISABLES)}")

    ev = cand.evidence
    meta = cand.assessment_meta
    updates: dict[str, Any] = {}

    if "removal_reason" in disabled:
        updates["removal_reason"] = RemovalReason.NONE
    if "reinsertion" in disabled:
        updates["reinsertion_count"] = 0
        updates["insertion_span"] = timedelta(0)
    if "edit_war" in disabled:
        updates["in_edit_war"] = False
    if "dispute_template" in disabled:
        updates["dispute_template_added"] = False
    if "assessment" in disabled:
        updates["assessment"] = None
        updates["source_manipulation"] = cand.diff_source_manipulation
        meta = None

    return (replace(ev, **updates) if updates else ev), meta


# ── Corpus queries (PK-indexed, sequential, tunnel-resilient) ────────────────

def sql_query(query: str) -> list[dict[str, Any]]:
    """Execute `aot.sql(query)` with one extra wait-and-retry if the SSH tunnel briefly resets."""
    try:
        return aot.sql(query)
    except RuntimeError as ex:
        msg = str(ex)
        if "Can't connect" in msg or "Lost connection" in msg or "ERROR 2002" in msg or "ERROR 2013" in msg:
            aot.log("tunnel reconnect wait (3s) before retry...")
            time.sleep(3.0)
            return aot.sql(query)
        raise


def check_kept_in_latest_one(page_id: int, latest_rev_id: int, sentence: str) -> bool:
    """Server-side `LOCATE` check in the page's latest revision (`corpus.revision_text` PK) for one incident."""
    if len(sentence) < 25:
        return False
    rows = sql_query(
        f"SELECT (LOCATE({aot.quote(sentence)}, text) > 0) AS kept "
        f"FROM corpus.revision_text "
        f"WHERE page_id = {int(page_id)} AND rev_id = {int(latest_rev_id)}"
    )
    return bool(rows and int(rows[0].get("kept") or 0) == 1)


def check_kept_in_latest_batch(
    probes: list[tuple[int, int, int, str]],
    batch_size: int = 150,
) -> dict[int, bool]:
    """Server-side `LOCATE` on `corpus.revision_text` PK `(page_id, rev_id)` without shipping revision text."""
    out: dict[int, bool] = {}
    valid = [(int(cid), int(pid), int(rid), needle) for cid, pid, rid, needle in probes if len(needle) >= 25]
    for start in range(0, len(valid), batch_size):
        chunk = valid[start : start + batch_size]
        selects = [
            f"SELECT {cid} AS cid, {pid} AS page_id, {rid} AS rev_id, {aot.quote(needle)} AS needle"
            for cid, pid, rid, needle in chunk
        ]
        union_sql = " UNION ALL ".join(selects)
        rows = sql_query(
            f"SELECT p.cid, (LOCATE(p.needle, rt.text) > 0) AS kept "
            f"FROM ({union_sql}) p "
            f"JOIN corpus.revision_text rt ON rt.page_id = p.page_id AND rt.rev_id = p.rev_id"
        )
        for r in rows:
            out[int(r["cid"])] = int(r.get("kept") or 0) == 1
    return out


def load_candidates_for_pages(
    page_ids: list[int],
    assessments_by_key: dict[str, dict[str, Any]] | None = None,
    method: str = "fuzzy",
    include_edit_diffs_for_assessed: bool = True,
    sides: bool = True,
    edit_war_source: str = "passage",
) -> tuple[list[CandidateCluster], dict[str, Any]]:
    """Load all S7 `method` clusters touching `page_ids` and compute `IncidentEvidence` for each."""
    if edit_war_source not in EDIT_WAR_SOURCES:
        raise ValueError(f"Unknown edit_war_source {edit_war_source!r}; valid: {sorted(EDIT_WAR_SOURCES)}")

    clean_pids = sorted({int(p) for p in page_ids if p is not None})
    if not clean_pids:
        return [], {"clusters": 0, "missing_s3_diffs": 0, "two_sided_clusters": 0}

    method_sql = aot.quote(method)
    pids_sql = aot.ints(clean_pids)

    cid_rows = sql_query(
        f"SELECT DISTINCT c.cluster_id "
        f"FROM research.s7_cluster c "
        f"JOIN research.s7_member m ON m.cluster_id = c.cluster_id "
        f"WHERE c.method = {method_sql} AND m.page_id IN ({pids_sql})"
    )
    if not cid_rows:
        return [], {"clusters": 0, "missing_s3_diffs": 0, "two_sided_clusters": 0}

    cids = sorted(int(r["cluster_id"]) for r in cid_rows)

    clusters_by_id: dict[int, dict[str, Any]] = {}
    members_by_cid: dict[int, list[dict[str, Any]]] = defaultdict(list)
    for start in range(0, len(cids), 800):
        batch = cids[start : start + 800]
        cids_sql = aot.ints(batch)
        for r in sql_query(
            f"SELECT cluster_id, n_units, n_pages, n_accounts, n_reinsertions, "
            f"first_ts, last_ts, span_days, max_gap_days, sample_text, campaign_signals, prose_chars "
            f"FROM research.s7_cluster WHERE cluster_id IN ({cids_sql})"
        ):
            clusters_by_id[int(r["cluster_id"])] = r
        for r in sql_query(
            f"SELECT cluster_id, page_id, reverting_rev_id, inserting_rev_ids, "
            f"inserted_at, removed_at, account_key "
            f"FROM research.s7_member "
            f"WHERE cluster_id IN ({cids_sql}) "
            f"ORDER BY cluster_id, inserted_at, reverting_rev_id"
        ):
            members_by_cid[int(r["cluster_id"])].append(r)

    all_member_pages: set[int] = set()
    all_ins_revs: set[int] = set()
    all_rem_revs: set[int] = set()

    for cid in cids:
        ms = members_by_cid.get(cid, [])
        for m in ms:
            all_member_pages.add(int(m["page_id"]))
            all_rem_revs.add(int(m["reverting_rev_id"]))
            all_ins_revs.update(parse_rev_list(m["inserting_rev_ids"]))

    sorted_pages = sorted(all_member_pages)
    sorted_ins = sorted(all_ins_revs)
    sorted_rem = sorted(all_rem_revs)
    sorted_all_revs = sorted(all_ins_revs | all_rem_revs)

    # Article titles + latest revision per page
    titles_by_page: dict[int, str] = {}
    latest_rev_by_page: dict[int, int] = {}
    if sorted_pages:
        for r in sql_query(
            f"SELECT page_id, title FROM corpus.article WHERE lang = 'fi' AND page_id IN ({aot.ints(sorted_pages)})"
        ):
            titles_by_page[int(r["page_id"])] = str(r["title"])
        for r in sql_query(
            f"SELECT page_id, MAX(rev_id) AS max_rev, MAX(title) AS fallback_title "
            f"FROM corpus.revision WHERE page_id IN ({aot.ints(sorted_pages)}) GROUP BY page_id"
        ):
            pid = int(r["page_id"])
            latest_rev_by_page[pid] = int(r["max_rev"])
            if pid not in titles_by_page and r.get("fallback_title"):
                titles_by_page[pid] = str(r["fallback_title"])

    # Revision metadata (user, ts) via composite PK prefix `page_id IN (...) AND rev_id IN (...)`
    rev_by_id: dict[int, dict[str, Any]] = {}
    if sorted_all_revs and sorted_pages:
        pages_sql = aot.ints(sorted_pages)
        for start in range(0, len(sorted_all_revs), 800):
            batch = sorted_all_revs[start : start + 800]
            for r in sql_query(
                f"SELECT page_id, rev_id, ts, user, anonymous "
                f"FROM corpus.revision "
                f"WHERE page_id IN ({pages_sql}) AND rev_id IN ({aot.ints(batch)})"
            ):
                rev_by_id[int(r["rev_id"])] = r

    # S3 span diffs (`kind = 'span'`, including `removed_prose` for side orientation)
    span_diffs_by_rev: dict[int, dict[str, Any]] = {}
    for start in range(0, len(sorted_rem), 250):
        batch = sorted_rem[start : start + 250]
        for r in sql_query(
            f"SELECT rev_id, page_id, added_prose, removed_prose, added_prose_chars, removed_prose_chars, "
            f"removed_refs, is_substantive, is_blanking, segments, error "
            f"FROM research.s3_diff "
            f"WHERE kind = 'span' AND rev_id IN ({aot.ints(batch)})"
        ):
            span_diffs_by_rev[int(r["rev_id"])] = r

    # Orient units per cluster so assessment lookup, edit-diff loading, and kept probes use side-1 units
    ordered_by_cid: dict[int, list[dict[str, Any]]] = {}
    side1_by_cid: dict[int, list[dict[str, Any]]] = {}
    arow_by_cid: dict[int, dict[str, Any] | None] = {}
    assessed_ins_revs: set[int] = set()
    two_sided_count = 0

    for cid in cids:
        ms = members_by_cid.get(cid, [])
        if not ms:
            continue
        ordered = sorted(ms, key=lambda m: (str(m["inserted_at"]), int(m["reverting_rev_id"])))
        ordered_by_cid[cid] = ordered
        u_sides = orient_cluster_units(ordered, span_diffs_by_rev) if sides else (1,) * len(ordered)
        if 2 in u_sides:
            two_sided_count += 1
        s1 = [m for m, s in zip(ordered, u_sides) if s == 1]
        side1_by_cid[cid] = s1

        arow: dict[str, Any] | None = None
        if assessments_by_key:
            s1_ins = sorted({r for m in s1 for r in parse_rev_list(m["inserting_rev_ids"])})
            s1_ikey = contract.incident_key(s1_ins) if s1_ins else ""
            full_ins = sorted({r for m in ordered for r in parse_rev_list(m["inserting_rev_ids"])})
            full_ikey = contract.incident_key(full_ins) if full_ins else ""
            arow = assessments_by_key.get(s1_ikey) or assessments_by_key.get(full_ikey)
            if arow is not None and include_edit_diffs_for_assessed:
                assessed_ins_revs.update(s1_ins)
        arow_by_cid[cid] = arow

    # Optional S3 edit diffs (`kind = 'edit'`) for candidates that have S9 assessments (verbatim quote parity with S9 inputs)
    edit_diffs_by_rev: dict[int, dict[str, Any]] = {}
    sorted_assessed_ins = sorted(assessed_ins_revs)
    for start in range(0, len(sorted_assessed_ins), 250):
        batch = sorted_assessed_ins[start : start + 250]
        for r in sql_query(
            f"SELECT rev_id, page_id, added_prose, added_prose_chars, removed_refs, "
            f"is_substantive, is_blanking, segments, error "
            f"FROM research.s3_diff "
            f"WHERE kind = 'edit' AND rev_id IN ({aot.ints(batch)})"
        ):
            edit_diffs_by_rev[int(r["rev_id"])] = r

    # S1 revert class
    s1_class_by_rev: dict[int, str] = {}
    for start in range(0, len(sorted_rem), 800):
        batch = sorted_rem[start : start + 800]
        for r in sql_query(
            f"SELECT reverting_rev_id, class FROM research.s1_revert WHERE reverting_rev_id IN ({aot.ints(batch)})"
        ):
            s1_class_by_rev[int(r["reverting_rev_id"])] = str(r.get("class") or "")

    # S4 conflict episodes (`edit_war`, `ping_pong`, default window) — queried only when edit_war_source == 's4'
    s4_war_revs: set[int] = set()
    if edit_war_source == "s4":
        for start in range(0, len(sorted_all_revs), 800):
            batch = sorted_all_revs[start : start + 800]
            for r in sql_query(
                f"SELECT DISTINCT er.rev_id "
                f"FROM research.s4_episode_rev er "
                f"JOIN research.s4_episode e USING (episode_id) "
                f"WHERE er.rev_id IN ({aot.ints(batch)}) "
                f"AND e.is_default = 1 AND e.kind IN ('edit_war', 'ping_pong')"
            ):
                s4_war_revs.add(int(r["rev_id"]))

    # S5 revert reasons
    s5_reasons_by_rev: dict[int, str] = {}
    for start in range(0, len(sorted_rem), 800):
        batch = sorted_rem[start : start + 800]
        for r in sql_query(
            f"SELECT reverting_rev_id, removal_reasons "
            f"FROM research.s5_revert_reason "
            f"WHERE reverting_rev_id IN ({aot.ints(batch)})"
        ):
            s5_reasons_by_rev[int(r["reverting_rev_id"])] = str(r.get("removal_reasons") or "")

    # S6 bot flag via PK-indexed `s6_rev_author` -> `s6_editor_profile`
    s6_bot_by_rev: dict[int, int] = {}
    for start in range(0, len(sorted_ins), 800):
        batch = sorted_ins[start : start + 800]
        for r in sql_query(
            f"SELECT STRAIGHT_JOIN ra.rev_id, ep.is_bot "
            f"FROM research.s6_rev_author ra USE INDEX (PRIMARY) "
            f"JOIN research.s6_editor_profile ep ON ep.user = ra.user "
            f"WHERE ra.rev_id IN ({aot.ints(batch)})"
        ):
            s6_bot_by_rev[int(r["rev_id"])] = int(r.get("is_bot") or 0)

    # Server-side KeptInLatestRevision (`LOCATE` of longest added sentence >= 25 chars in primary page's latest rev)
    kept_probes: list[tuple[int, int, int, str]] = []
    for cid in cids:
        s1 = side1_by_cid.get(cid)
        if not s1:
            continue
        primary_pid = int(s1[0]["page_id"])
        latest_rid = latest_rev_by_page.get(primary_pid)
        if latest_rid is None:
            continue
        sds = [
            span_diffs_by_rev[int(m["reverting_rev_id"])]
            for m in s1
            if int(m["reverting_rev_id"]) in span_diffs_by_rev
            and not span_diffs_by_rev[int(m["reverting_rev_id"])].get("error")
        ]
        needle = longest_added_sentence(sds, min_chars=25)
        if needle:
            kept_probes.append((cid, primary_pid, latest_rid, needle))

    kept_by_cid = check_kept_in_latest_batch(kept_probes)

    candidates: list[CandidateCluster] = []
    missing_s3_total = 0
    for cid in cids:
        crow = clusters_by_id.get(cid)
        ms = ordered_by_cid.get(cid)
        if not crow or not ms:
            continue
        arow = arow_by_cid.get(cid)
        cand = build_cluster_candidate(
            cluster_row=crow,
            member_rows=ms,
            rev_by_id=rev_by_id,
            span_diffs_by_rev=span_diffs_by_rev,
            s1_class_by_rev=s1_class_by_rev,
            s4_war_revs=s4_war_revs,
            s5_reasons_by_rev=s5_reasons_by_rev,
            s6_bot_by_rev=s6_bot_by_rev,
            kept_in_latest=kept_by_cid.get(cid, False),
            assessment_row=arow,
            titles_by_page=titles_by_page,
            edit_diffs_by_rev=edit_diffs_by_rev if arow else None,
            sides=sides,
            edit_war_source=edit_war_source,
        )
        missing_s3_total += cand.missing_s3_diffs
        candidates.append(cand)

    return candidates, {
        "clusters": len(candidates),
        "missing_s3_diffs": missing_s3_total,
        "two_sided_clusters": two_sided_count,
        "kept_probes": len(kept_probes),
        "kept_hits": sum(1 for v in kept_by_cid.values() if v),
    }
