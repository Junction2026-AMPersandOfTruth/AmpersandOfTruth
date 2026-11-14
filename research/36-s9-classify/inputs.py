#!/usr/bin/env python3
"""P1.S9 (#36): Build Gemini batch classification inputs on the golden dev split.

Modes:
  python3 research/36-s9-classify/inputs.py
      -> results/golden-dev.classify.jsonl       (variant A / B: _id + 9 classify-edit vars)
  python3 research/36-s9-classify/inputs.py --facts
      -> results/golden-dev.classify-facts.jsonl (variant C: adds `facts` variable)
  python3 research/36-s9-classify/inputs.py --batch-by-article
      -> results/golden-dev.classify-batch.jsonl (variant D: <= 6 items per article call, _id = <page_id>:<n>)
  python3 research/36-s9-classify/inputs.py --stage2-from <stage1-out.jsonl>
      -> results/golden-dev.stage2.jsonl         (optional offline helper for variant B stage 2)
  python3 research/36-s9-classify/inputs.py --refute-from <first-pass-out.jsonl>
      -> results/golden-dev.refute.jsonl         (offline helper for variant E second pass)

Context extraction method (`extract_context`, <= 1200 chars):
  1. Take the chronologically first inserting revision `r0`. Its parent is `s3_diff.old_rev_id`
     (previous non-null revision on the page; `None` if `r0` created the page), falling back to
     `corpus.revision.parent_id` if `r0` has no `s3_diff` row yet.
  2. Anchor the change inside `parent_text`:
     - If `r0` has a non-empty `removed` segment in `s3_diff`, locate its exact substring offset in `parent_text`.
     - Else if `r0`'s own wikitext is available (pure insertion), find the exact first character index where
       `parent_text` and `r0_text` diverge (`common_prefix_len`).
     - Else fall back to matching any `/* Section */` heading from `r0`'s edit comment (`== Section ==`),
       or proportional paragraph offset from `old_paragraph` / `new_paragraph`, or offset 0.
  3. Slice a window of at most 1200 characters of `parent_text` centered on the anchor, snapping inward to
     whitespace boundaries when trimmed and marking trimmed edges with `…` (total length strictly <= 1200).

Privacy & blindness guarantees:
  - Reads ONLY `split == "dev"` topics from `research/eval/golden.json` (never touches test split).
  - Never writes golden `label`, `exclusion`, `category`, `description`, `why`, or `notes`.
  - Masks all IPs via `aot.mask_comment` and scrubs usernames / temporary account IDs from summaries and facts.
"""
from __future__ import annotations

import argparse
import base64
from collections import defaultdict
from datetime import datetime, timezone
import json
import pathlib
import re
import sys
import zlib
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

NL = chr(10)
NUL = chr(0)
PARA_SPLIT_RE = re.compile(r"(?:^|" + NL + r"\s*" + NL + r")\S")

MAX_INSERTED_CHARS = 4000
MAX_REMOVED_CHARS = 2000
MAX_CONTEXT_CHARS = 1200
MAX_BATCH_ITEMS = 6

CLASSIFY_VARS = (
    "article_title",
    "edit_timestamp",
    "editor_kind",
    "exposure",
    "edit_summary",
    "revert_summary",
    "removed_text",
    "inserted_text",
    "context",
)

GRAFFITI_CATS = frozenset({"vandalism", "test_or_nonsense"})
DEFAULT_REFUTE_CATS = ("pov_push", "false_factual_claim", "source_manipulation")

_IPV4_EXACT = re.compile(r"^\d{1,3}(?:\.\d{1,3}){3}$")
_MASKED_IP_RE = re.compile(r"\b\d{1,3}\.\d{1,3}\.x\.x\b|(?<![0-9A-Fa-f:])[0-9A-Fa-f]{1,4}:[0-9A-Fa-f]{1,4}:…")
_TEMP_ACCOUNT_RE = re.compile(r"~\d{4}-\d+(?:-\d+)?")
_SECTION_COMMENT_RE = re.compile(r"/\*\s*([^*]+?)\s*\*/")
_USER_TALK_LINK_RE = re.compile(
    r"\[\[\s*(?:User[ _]talk|Keskustelu[ _]käyttäjästä|Käyttäjä[ _]keskustelu)\s*:([^|\]]+)(?:\|([^\]]*))?\]\]",
    re.IGNORECASE,
)
_USER_LINK_RE = re.compile(
    r"\[\[\s*(?:(?:User|Käyttäjä)\s*:|(?:Special|Toiminnot|Spesiaali)\s*:\s*(?:Contributions|Muokkaukset)/)"
    r"([^|\]]*?)(?:\|([^\]]*))?(?:\]\]|$)",
    re.IGNORECASE,
)
_TEHNEET_LIST_RE = re.compile(r"(\(tehneet )([^()\[\]<>]+?)(\))")
_REVERT_SLOT_RES = (
    re.compile(r"(\bdated [\d\-: ]+ by )((?!\[editor\])[^()\[\]<>:,;]+?)( using\b|$)"),
    re.compile(r"(\bjonka (?:on )?(?:teki|tehnyt) )((?!\[editor\])[^()\[\]<>:,;]+?)( \(|[,:;]|$)"),
    re.compile(
        r"(\b[Kk]äyttäjän )((?!\[editor\])[^()\[\]<>:,;]+?)"
        r"( \(keskustelu\)| tekemään versioon| tekemään muutokseen| muokkaukset\b| muokkaus\b| tekemä\b)"
    ),
    re.compile(r"(\((?:tehnyt|käyttäjältä) )((?!\[editor\])[^()\[\]<>:,;]+?)(\))"),
    re.compile(r"(\bkäyttäjältä )((?!\[editor\])[^()\[\]<>:,;]+?)(:| \(|$)"),
)


def _looks_like_ip(target: str) -> bool:
    t = target.strip()
    if _IPV4_EXACT.match(t):
        return True
    return ":" in t and aot.user_display(t).endswith("(anonymous)")


def mask_summary(comment: str | None, known_users: list[str | None] | tuple[str | None, ...] = ()) -> str:
    """Mask IPs via `aot.mask_comment` and redact usernames/temporary accounts in edit summaries."""
    if not comment or not comment.strip():
        return ""

    s = comment.replace(NUL, "")
    s = _USER_TALK_LINK_RE.sub(
        lambda m: m.group(2).strip() if m.group(2) and m.group(2).strip() else "keskustelu",
        s,
    )

    def _sub_user_link(m: re.Match[str]) -> str:
        raw_match = m.group(0)
        target = (m.group(1) or "").strip()
        if raw_match.endswith("]]") and _looks_like_ip(target):
            return target
        return "[editor]"

    s = _USER_LINK_RE.sub(_sub_user_link, s)
    s = aot.mask_comment(s)

    # Protect masked IPv4/IPv6 tokens so username/slot rules never split them on '.' or ':'.
    masked_ips: list[str] = []

    def _stash_ip(m: re.Match[str]) -> str:
        idx = len(masked_ips)
        masked_ips.append(m.group(0))
        return f"{NUL}#IP_{idx}_#{NUL}"

    s = _MASKED_IP_RE.sub(_stash_ip, s)
    s = _TEMP_ACCOUNT_RE.sub("[editor]", s)

    clean_users = sorted(
        {
            u.strip()
            for u in known_users
            if u and u.strip() and len(u.strip()) >= 2 and not _looks_like_ip(u) and not u.strip().startswith("~")
        },
        key=len,
        reverse=True,
    )
    for u in clean_users:
        for variant in dict.fromkeys((u, u.replace(" ", "_"))):
            pat = re.compile(r"(?<!\w)" + re.escape(variant) + r"(?=(?::n|'s|n)?(?!\w))")
            s = pat.sub("[editor]", s)

    def _sub_tehneet(m: re.Match[str]) -> str:
        parts = re.split(r"(\s*,\s*|\s+ja\s+)", m.group(2))
        rebuilt: list[str] = []
        for idx, part in enumerate(parts):
            if idx % 2 == 1:
                rebuilt.append(part)
                continue
            tok = part.strip()
            if not tok or "[editor]" in tok or f"{NUL}#IP_" in tok:
                rebuilt.append(part)
            else:
                rebuilt.append("[editor]")
        return m.group(1) + "".join(rebuilt) + m.group(3)

    s = _TEHNEET_LIST_RE.sub(_sub_tehneet, s)

    for slot_re in _REVERT_SLOT_RES:
        def _sub_slot(m: re.Match[str]) -> str:
            cand = m.group(2).strip()
            if f"{NUL}#IP_" in cand or "[editor]" in cand:
                return m.group(0)
            return m.group(1) + "[editor]" + m.group(3)

        s = slot_re.sub(_sub_slot, s)

    for idx, ip_tok in enumerate(masked_ips):
        s = s.replace(f"{NUL}#IP_{idx}_#{NUL}", ip_tok)

    return " ".join(s.split()).strip()


def join_summaries(comments: list[str | None], known_users: list[str | None] = ()) -> str:
    """Mask each summary and join distinct non-empty summaries in chronological order with ` | `."""
    masked = [mask_summary(c, known_users) for c in comments]
    unique_non_empty = list(dict.fromkeys(m for m in masked if m))
    return " | ".join(unique_non_empty)


def parse_db_timestamp(ts: str | datetime) -> datetime:
    """Parse MariaDB UTC `YYYY-MM-DD HH:MM:SS` or ISO-8601 string into timezone-aware UTC datetime."""
    if isinstance(ts, datetime):
        return ts.replace(tzinfo=timezone.utc) if ts.tzinfo is None else ts.astimezone(timezone.utc)
    raw = ts.strip()
    if raw.endswith("Z"):
        raw = raw[:-1]
    dt = datetime.fromisoformat(raw.replace(" ", "T"))
    return dt.replace(tzinfo=timezone.utc) if dt.tzinfo is None else dt.astimezone(timezone.utc)


def format_iso_utc(ts: str | datetime) -> str:
    return parse_db_timestamp(ts).strftime("%Y-%m-%dT%H:%M:%SZ")


def format_exposure(first_insert_ts: str | datetime, first_remove_ts: str | datetime | None) -> str:
    """Human-readable survival duration from first insertion to first removal."""
    if first_remove_ts is None:
        return "not reverted"
    seconds = max(0.0, (parse_db_timestamp(first_remove_ts) - parse_db_timestamp(first_insert_ts)).total_seconds())
    s = int(round(seconds))
    if s < 60:
        return f"{s} s"
    m = s // 60
    if m < 60:
        return f"{m} min"
    h, rem_m = divmod(m, 60)
    if h < 24:
        return f"{h} h {rem_m} min" if rem_m else f"{h} h"
    d = h // 24
    if d < 60:
        return "1 day" if d == 1 else f"{d} days"
    if d < 730:
        months = max(1, round(d / 30.44))
        return "1 month" if months == 1 else f"{months} months"
    return f"{d / 365.25:.1f} years"


def classify_editor_kind(ins_revs: list[dict[str, Any]]) -> str:
    """Return `named`, `anonymous`, or `mixed` across an item's inserting revisions."""
    kinds: set[str] = set()
    for r in ins_revs:
        anon_flag = r.get("anonymous")
        if anon_flag is not None:
            is_anon = bool(int(anon_flag))
        else:
            is_anon = aot.is_anonymous(r.get("user"))
        kinds.add("anonymous" if is_anon else "named")
    if len(kinds) > 1:
        return "mixed"
    return next(iter(kinds), "anonymous")


def cap_text(text: str, limit: int) -> str:
    if len(text) <= limit:
        return text
    if limit <= 1:
        return text[:limit]
    return text[: limit - 1].rstrip() + "…"


def union_diff_texts(
    diff_rows_in_order: list[dict[str, Any]],
    max_inserted: int = MAX_INSERTED_CHARS,
    max_removed: int = MAX_REMOVED_CHARS,
) -> tuple[str, str]:
    """Ordered union of non-moved `added` and `removed` segment `text` across inserting revisions."""
    added: list[str] = []
    removed: list[str] = []
    seen_add: set[str] = set()
    seen_rem: set[str] = set()

    for row in diff_rows_in_order:
        segs_raw = row.get("segments")
        if segs_raw is None:
            continue
        segs = json.loads(segs_raw) if isinstance(segs_raw, str) else segs_raw
        for seg in segs or ():
            if seg.get("moved"):
                continue
            txt = (seg.get("text") or "").strip()
            if not txt:
                continue
            op = seg.get("op")
            if op == "added" and txt not in seen_add:
                seen_add.add(txt)
                added.append(txt)
            elif op == "removed" and txt not in seen_rem:
                seen_rem.add(txt)
                removed.append(txt)

    ins_joined = cap_text(NL.join(added), max_inserted)
    rem_joined = cap_text(NL.join(removed), max_removed)
    return ins_joined, rem_joined


def common_prefix_len(a: str, b: str) -> int:
    n = min(len(a), len(b))
    i = 0
    while i < n and a[i] == b[i]:
        i += 1
    return i


def extract_context(
    parent_text: str,
    first_diff_segments: list[dict[str, Any]] | None = None,
    first_new_text: str | None = None,
    first_comment: str | None = None,
    max_chars: int = MAX_CONTEXT_CHARS,
) -> str:
    """Slice <= `max_chars` of `parent_text` centered around the first insertion's change location."""
    if not parent_text or max_chars <= 0:
        return ""
    if len(parent_text) <= max_chars:
        return parent_text

    anchor_start: int | None = None
    anchor_end: int | None = None

    # 1. Exact removed segment match inside parent_text.
    for seg in first_diff_segments or ():
        if seg.get("moved") or seg.get("op") != "removed":
            continue
        needle = (seg.get("text") or "").strip()
        if needle:
            pos = parent_text.find(needle)
            if pos >= 0:
                anchor_start = pos
                anchor_end = min(len(parent_text), pos + min(len(needle), 400))
                break

    # 2. Pure insertion: exact divergence point between parent_text and r0's text.
    if anchor_start is None and first_new_text is not None:
        pos = min(len(parent_text), common_prefix_len(parent_text, first_new_text))
        anchor_start = anchor_end = pos

    # 3. Fallback: section heading from /* Section */ comment.
    if anchor_start is None and first_comment:
        m = _SECTION_COMMENT_RE.search(first_comment)
        if m:
            sec = m.group(1).strip()
            if sec:
                sec_re = re.compile(r"^=+\s*" + re.escape(sec) + r"\s*=+", re.MULTILINE)
                hm = sec_re.search(parent_text)
                if hm:
                    anchor_start = anchor_end = hm.start()

    # 4. Fallback: paragraph index proportion from first diff segment.
    if anchor_start is None and first_diff_segments:
        blocks = [m.start() for m in PARA_SPLIT_RE.finditer(parent_text)]
        for seg in first_diff_segments:
            p_idx = seg.get("old_paragraph")
            if p_idx is None:
                p_idx = seg.get("new_paragraph")
            if isinstance(p_idx, int) and blocks:
                clamped = max(0, min(p_idx, len(blocks) - 1))
                anchor_start = anchor_end = blocks[clamped]
                break

    if anchor_start is None:
        anchor_start = anchor_end = 0
    assert anchor_end is not None

    mid = (anchor_start + anchor_end) // 2
    # Reserve up to 2 chars for leading/trailing ellipsis so output length never exceeds max_chars.
    inner_budget = max(1, max_chars - 2)
    start = max(0, min(mid - inner_budget // 2, len(parent_text) - inner_budget))
    end = min(len(parent_text), start + inner_budget)

    if start > 0:
        sp = parent_text.find(" ", start, min(end, start + 60))
        if sp != -1:
            start = sp + 1
    if end < len(parent_text):
        sp = parent_text.rfind(" ", max(start, end - 60), end)
        if sp != -1 and sp > start:
            end = sp

    prefix = "…" if start > 0 else ""
    suffix = "…" if end < len(parent_text) else ""
    out = prefix + parent_text[start:end].strip() + suffix
    return cap_text(out, max_chars)


def format_facts(
    ins_revs: list[dict[str, Any]],
    rem_revs: list[dict[str, Any]],
    s5_revert_by_rev: dict[int, dict[str, Any]],
    s5_summary_by_rev: dict[int, dict[str, Any]],
    s4_episodes_by_rev: dict[int, list[dict[str, Any]]],
    s6_by_rev: dict[int, dict[str, Any]],
) -> str:
    """Format privacy-safe structural facts (S1/S4/S5/S6) with pseudonymous editors and zero names/IPs."""
    ins_displays: list[str] = []
    ins_kinds: dict[str, str] = {}
    ins_rev_counts: dict[str, int] = defaultdict(int)
    for r in ins_revs:
        disp = aot.user_display(r.get("user"))
        is_anon = bool(int(r["anonymous"])) if r.get("anonymous") is not None else aot.is_anonymous(r.get("user"))
        if disp not in ins_kinds:
            ins_displays.append(disp)
            ins_kinds[disp] = "anonymous" if is_anon else "named"
        ins_rev_counts[disp] += 1

    rem_displays = {aot.user_display(r.get("user")) for r in rem_revs}
    self_removal = bool(rem_displays & set(ins_displays))

    n_named = sum(1 for d in ins_displays if ins_kinds[d] == "named")
    n_anon = sum(1 for d in ins_displays if ins_kinds[d] == "anonymous")
    reinsertions = max(0, len(ins_revs) - 1)

    lines = [
        f"Insertions: {len(ins_revs)} (re-insertions: {reinsertions}) by {len(ins_displays)} distinct editor(s) "
        f"({n_named} named, {n_anon} anonymous); removals: {len(rem_revs)} "
        f"(self-removal overlap: {'yes' if self_removal else 'no'})."
    ]

    removal_reasons: list[str] = []
    auto_families: list[str] = []
    for r in rem_revs:
        rid = int(r["rev_id"])
        rr = s5_revert_by_rev.get(rid)
        if rr:
            for tag in (rr.get("removal_reasons") or "").split(","):
                if tag and tag not in removal_reasons:
                    removal_reasons.append(tag)
            fam = rr.get("auto_family")
            if fam and fam not in auto_families:
                auto_families.append(fam)
        sm = s5_summary_by_rev.get(rid)
        if sm:
            for tag in (sm.get("reasons") or "").split(","):
                if tag and tag not in removal_reasons:
                    removal_reasons.append(tag)
            fam = sm.get("auto_family")
            if fam and fam not in auto_families:
                auto_families.append(fam)

    insert_flags: list[str] = []
    for r in ins_revs:
        rid = int(r["rev_id"])
        sm = s5_summary_by_rev.get(rid)
        if sm:
            for fl in (sm.get("insert_flags") or "").split(","):
                if fl and fl not in insert_flags:
                    insert_flags.append(fl)

    lines.append(
        "Edit-summary signals (S5): "
        f"removal_reasons={','.join(removal_reasons) if removal_reasons else 'none'}; "
        f"revert_tool={','.join(auto_families) if auto_families else 'manual_or_none'}; "
        f"insert_flags={','.join(insert_flags) if insert_flags else 'none'}."
    )

    seen_ep: dict[int, dict[str, Any]] = {}
    for r in (*ins_revs, *rem_revs):
        rid = int(r["rev_id"])
        for ep in s4_episodes_by_rev.get(rid, ()):
            seen_ep[int(ep["episode_id"])] = ep

    if seen_ep:
        ep_parts = []
        for ep in sorted(seen_ep.values(), key=lambda x: int(x["episode_id"])):
            prot = "yes" if int(ep.get("protection_hint") or 0) else "no"
            ep_parts.append(
                f"{ep['kind']} (reverts={int(ep.get('revert_count') or 0)}, "
                f"named_participants={int(ep.get('named_participants') or 0)}, protection_hint={prot})"
            )
        lines.append("Conflict-episode overlap (S4): " + "; ".join(ep_parts) + ".")
    else:
        lines.append("Conflict-episode overlap (S4): none.")

    prof_by_disp: dict[str, dict[str, Any]] = {}
    for r in ins_revs:
        rid = int(r["rev_id"])
        disp = aot.user_display(r.get("user"))
        if disp not in prof_by_disp and rid in s6_by_rev:
            prof_by_disp[disp] = s6_by_rev[rid]

    if prof_by_disp:
        ed_parts = []
        for idx, disp in enumerate(ins_displays, 1):
            p = prof_by_disp.get(disp)
            if not p:
                continue
            edits = int(p.get("edits") or 0)
            pages = int(p.get("distinct_pages") or 0)
            rev_others = int(p.get("reverted_edits_by_others") or 0)
            this_count = ins_rev_counts[disp]
            other_edits = max(0, edits - this_count)
            if other_edits > 0:
                this_rev_others = 0 if self_removal else min(this_count, rev_others)
                loo_rate = max(0, rev_others - this_rev_others) / other_edits
                prior_str = f"{loo_rate:.0%} over {other_edits} other edits"
            else:
                prior_str = "no other corpus edits"
            ed_parts.append(
                f"Editor {idx} ({ins_kinds[disp]}): corpus_edits={edits}, pages={pages}, "
                f"single_purpose={'yes' if int(p.get('single_purpose') or 0) else 'no'}, "
                f"throwaway={'yes' if int(p.get('throwaway') or 0) else 'no'}, "
                f"bot={'yes' if int(p.get('is_bot') or 0) else 'no'}, "
                f"other_revert_rate={prior_str}"
            )
        if ed_parts:
            lines.append("Inserting editor patterns (S6, dataset-scoped, no names): " + "; ".join(ed_parts) + ".")

    return NL.join(lines)


def assemble_item_record(
    item: dict[str, Any],
    article_title: str,
    rev_by_id: dict[int, dict[str, Any]],
    s3_by_rev: dict[int, dict[str, Any]],
    texts_by_rev: dict[int, str],
    include_facts: bool = False,
    facts_text: str | None = None,
) -> tuple[dict[str, str], dict[str, Any]]:
    """Assemble one `classify-edit` JSONL record and return `(record, diagnostics)`."""
    ins_ids = [int(r) for r in item["inserting_rev_ids"]]
    rem_ids = [int(r) for r in item.get("removing_rev_ids") or ()]

    ins_revs = sorted(
        (rev_by_id[r] for r in ins_ids if r in rev_by_id),
        key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
    )
    rem_revs = sorted(
        (rev_by_id[r] for r in rem_ids if r in rev_by_id),
        key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
    )
    if not ins_revs:
        raise ValueError(f"Item {item['id']} has no inserting revisions in rev_by_id.")

    first_ins = ins_revs[0]
    first_rem = rem_revs[0] if rem_revs else None

    first_ins_id = int(first_ins["rev_id"])
    first_s3 = s3_by_rev.get(first_ins_id)
    if first_s3 is not None:
        parent_rev_id = int(first_s3["old_rev_id"]) if first_s3.get("old_rev_id") is not None else None
    else:
        parent_rev_id = int(first_ins["parent_id"]) if first_ins.get("parent_id") is not None else None

    known_users: list[str | None] = [r.get("user") for r in (*ins_revs, *rem_revs)]
    if parent_rev_id is not None and parent_rev_id in rev_by_id:
        known_users.append(rev_by_id[parent_rev_id].get("user"))

    ordered_ins_ids = [int(r["rev_id"]) for r in ins_revs]
    missing_diff_revs = [
        rid for rid in ordered_ins_ids if rid not in s3_by_rev or bool(s3_by_rev[rid].get("error"))
    ]
    diff_rows = [s3_by_rev[rid] for rid in ordered_ins_ids if rid in s3_by_rev and not s3_by_rev[rid].get("error")]
    inserted_text, removed_text = union_diff_texts(diff_rows)

    first_segs: list[dict[str, Any]] | None = None
    if first_s3 and first_s3.get("segments") is not None and not first_s3.get("error"):
        raw_segs = first_s3["segments"]
        first_segs = json.loads(raw_segs) if isinstance(raw_segs, str) else raw_segs

    parent_text = texts_by_rev.get(parent_rev_id, "") if parent_rev_id is not None else ""
    first_new_text = texts_by_rev.get(first_ins_id)
    context = extract_context(
        parent_text=parent_text,
        first_diff_segments=first_segs,
        first_new_text=first_new_text,
        first_comment=first_ins.get("comment"),
    )

    rec: dict[str, str] = {
        "_id": str(item["id"]),
        "article_title": article_title,
        "edit_timestamp": format_iso_utc(first_ins["ts"]),
        "editor_kind": classify_editor_kind(ins_revs),
        "exposure": format_exposure(first_ins["ts"], first_rem["ts"] if first_rem else None),
        "edit_summary": join_summaries([r.get("comment") for r in ins_revs], known_users),
        "revert_summary": join_summaries([r.get("comment") for r in rem_revs], known_users),
        "removed_text": removed_text,
        "inserted_text": inserted_text,
        "context": context,
    }
    if include_facts:
        rec["facts"] = facts_text or ""

    diag = {
        "id": item["id"],
        "page_id": int(item["page_id"]),
        "missing_diff_revs": missing_diff_revs,
        "lacked_diff": len(missing_diff_revs) > 0,
    }
    return rec, diag


def format_candidate_block(rec: dict[str, str]) -> str:
    """Format one candidate record inside `classify-edit-batch`'s `{{data:candidates}}` payload."""
    lines = [
        f"=== Candidate {rec['_id']} ===",
        f"candidate_id: {rec['_id']}",
        f"edit_timestamp: {rec['edit_timestamp']}",
        f"editor_kind: {rec['editor_kind']}",
        f"exposure: {rec['exposure']}",
        f"edit_summary: {rec['edit_summary'] or '(empty)'}",
        f"revert_summary: {rec['revert_summary'] or '(empty)'}",
        "removed_text:",
        rec["removed_text"] or "(empty)",
        "inserted_text:",
        rec["inserted_text"] or "(empty)",
        "context:",
        rec["context"] or "(empty)",
    ]
    return NL.join(lines)


def batch_by_article(
    records_with_pages: list[tuple[int, dict[str, str]]],
    max_items: int = MAX_BATCH_ITEMS,
) -> list[dict[str, str]]:
    """Group item records by `page_id` into chunks of `<= max_items`, keyed `<page_id>:<n>` (1-based)."""
    by_page: dict[int, list[dict[str, str]]] = {}
    page_order: list[int] = []
    for page_id, rec in records_with_pages:
        pid = int(page_id)
        if pid not in by_page:
            by_page[pid] = []
            page_order.append(pid)
        by_page[pid].append(rec)

    sep = NL + NL
    out: list[dict[str, str]] = []
    for pid in page_order:
        items = by_page[pid]
        for idx, start in enumerate(range(0, len(items), max_items), 1):
            chunk = items[start : start + max_items]
            out.append(
                {
                    "_id": f"{pid}:{idx}",
                    "article_title": chunk[0]["article_title"],
                    "candidate_count": str(len(chunk)),
                    "candidate_ids": ", ".join(r["_id"] for r in chunk),
                    "candidates": sep.join(format_candidate_block(r) for r in chunk),
                }
            )
    return out


def is_graffiti_filtered(row: dict[str, Any] | None) -> bool:
    if not row or row.get("outcome") != "ok":
        return False
    val = row.get("value") or {}
    return val.get("category") in GRAFFITI_CATS and int(val.get("confidence") or 0) >= 4


def _select_model_rows(rows: list[dict[str, Any]], model: str | None) -> dict[str, dict[str, Any]]:
    models = sorted({r.get("model", "") for r in rows if r.get("model")})
    chosen = model
    if chosen is None:
        if len(models) > 1:
            raise ValueError(f"Multiple models in batch output {models}; specify model explicitly.")
        chosen = models[0] if models else None
    out: dict[str, dict[str, Any]] = {}
    for r in rows:
        if chosen is None or r.get("model") == chosen:
            out[str(r["id"])] = r
    return out


def build_stage2_inputs(
    base_records: list[dict[str, str]],
    stage1_rows: list[dict[str, Any]],
    model: str | None = None,
) -> list[dict[str, str]]:
    """Return base records NOT filtered by stage 1 (`vandalism | test_or_nonsense` at `confidence >= 4`)."""
    by_id = _select_model_rows(stage1_rows, model)
    return [rec for rec in base_records if not is_graffiti_filtered(by_id.get(rec["_id"]))]


def build_refute_inputs(
    base_records: list[dict[str, str]],
    first_pass_rows: list[dict[str, Any]],
    model: str | None = None,
    categories: set[str] | None = None,
) -> list[dict[str, str]]:
    """Build inputs for `refute-classification` (variant E) from base inputs + first-pass batch results."""
    by_id = _select_model_rows(first_pass_rows, model)
    target_cats = set(DEFAULT_REFUTE_CATS) if categories is None else categories
    out: list[dict[str, str]] = []
    for rec in base_records:
        r1 = by_id.get(rec["_id"])
        if not r1 or r1.get("outcome") != "ok":
            continue
        val = r1.get("value") or {}
        cat = val.get("category")
        if not cat:
            continue
        if "all" not in target_cats and cat not in target_cats:
            continue
        ref_rec = {k: rec[k] for k in ("_id", *CLASSIFY_VARS)}
        ref_rec["first_category"] = str(cat)
        ref_rec["first_confidence"] = str(val.get("confidence") or "")
        ref_rec["first_what_changed_en"] = str(val.get("what_changed_en") or "")
        ref_rec["first_claim_en"] = str(val.get("claim_en") or "(no explicit claim extracted)")
        out.append(ref_rec)
    return out


def load_dev_golden_items(golden_path: pathlib.Path) -> tuple[list[dict[str, Any]], dict[int, str]]:
    """Load ONLY `split == 'dev'` items and topic-article fallback titles from `golden.json`."""
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    items: list[dict[str, Any]] = []
    fallback_titles: dict[int, str] = {}
    for topic in doc.get("topics") or ():
        if topic.get("split") != "dev":
            continue
        for art in topic.get("articles") or ():
            fallback_titles.setdefault(int(art["page_id"]), str(art["title"]))
        for it in topic.get("items") or ():
            items.append(
                {
                    "id": it["id"],
                    "page_id": int(it["page_id"]),
                    "other_page_ids": [int(p) for p in it.get("other_page_ids") or ()],
                    "inserting_rev_ids": [int(r) for r in it["inserting_rev_ids"]],
                    "removing_rev_ids": [int(r) for r in it.get("removing_rev_ids") or ()],
                }
            )
    return items, fallback_titles


def _fetch_compressed_texts(page_id: int, rev_ids: list[int]) -> dict[int, str]:
    out: dict[int, str] = {}
    if not rev_ids:
        return out
    for i in range(0, len(rev_ids), 30):
        chunk = rev_ids[i : i + 30]
        rows = aot.sql(
            f"SELECT rev_id, TO_BASE64(COMPRESS(text)) AS bz "
            f"FROM corpus.revision_text "
            f"WHERE page_id = {int(page_id)} AND rev_id IN ({aot.ints(chunk)})"
        )
        for r in rows:
            rid = int(r["rev_id"])
            bz = (r.get("bz") or "").replace(NL, "").strip()
            if not bz:
                out[rid] = ""
                continue
            raw = base64.b64decode(bz)
            out[rid] = zlib.decompress(raw[4:]).decode("utf-8", "replace") if len(raw) > 4 else ""
    return out


def generate_from_corpus(
    golden_path: pathlib.Path,
    include_facts: bool = False,
) -> tuple[list[tuple[int, dict[str, str]]], dict[str, Any]]:
    """Fetch dev golden item diffs/contexts/signals sequentially from MariaDB and assemble records."""
    items, fallback_titles = load_dev_golden_items(golden_path)
    page_ids = sorted({it["page_id"] for it in items} | {p for it in items for p in it["other_page_ids"]})
    ins_ids = sorted({r for it in items for r in it["inserting_rev_ids"]})
    rem_ids = sorted({r for it in items for r in it["removing_rev_ids"]})
    all_rev_ids = sorted(set(ins_ids) | set(rem_ids))

    titles_by_page = dict(fallback_titles)
    if page_ids:
        for r in aot.sql(
            f"SELECT page_id, title FROM corpus.article WHERE lang = 'fi' AND page_id IN ({aot.ints(page_ids)})"
        ):
            titles_by_page[int(r["page_id"])] = str(r["title"])

    rev_by_id: dict[int, dict[str, Any]] = {}
    if all_rev_ids and page_ids:
        for start in range(0, len(all_rev_ids), 900):
            batch = all_rev_ids[start : start + 900]
            rows = aot.sql(
                f"SELECT page_id, rev_id, parent_id, title, ts, user, anonymous, comment "
                f"FROM corpus.revision "
                f"WHERE page_id IN ({aot.ints(page_ids)}) AND rev_id IN ({aot.ints(batch)})"
            )
            for r in rows:
                rid = int(r["rev_id"])
                rev_by_id[rid] = r
                titles_by_page.setdefault(int(r["page_id"]), str(r["title"]))

    s3_by_rev: dict[int, dict[str, Any]] = {}
    if ins_ids:
        for start in range(0, len(ins_ids), 400):
            batch = ins_ids[start : start + 400]
            rows = aot.sql(
                f"SELECT rev_id, page_id, old_rev_id, new_rev_id, segments, error "
                f"FROM research.s3_diff "
                f"WHERE kind = 'edit' AND rev_id IN ({aot.ints(batch)})"
            )
            for r in rows:
                s3_by_rev[int(r["rev_id"])] = r

    parent_ids: set[int] = set()
    need_texts_by_page: dict[int, set[int]] = defaultdict(set)
    for it in items:
        item_ins = sorted(
            (rev_by_id[r] for r in it["inserting_rev_ids"] if r in rev_by_id),
            key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
        )
        if not item_ins:
            continue
        first_ins = item_ins[0]
        first_rid = int(first_ins["rev_id"])
        first_page = int(first_ins["page_id"])
        s3_first = s3_by_rev.get(first_rid)
        if s3_first is not None:
            p_rid = int(s3_first["old_rev_id"]) if s3_first.get("old_rev_id") is not None else None
        else:
            p_rid = int(first_ins["parent_id"]) if first_ins.get("parent_id") is not None else None

        if p_rid is not None:
            parent_ids.add(p_rid)
            need_texts_by_page[first_page].add(p_rid)
            has_removed_seg = False
            if s3_first and s3_first.get("segments") and not s3_first.get("error"):
                for seg in json.loads(s3_first["segments"]):
                    if not seg.get("moved") and seg.get("op") == "removed" and (seg.get("text") or "").strip():
                        has_removed_seg = True
                        break
            if not has_removed_seg:
                need_texts_by_page[first_page].add(first_rid)

    extra_parents = sorted(parent_ids - set(rev_by_id))
    if extra_parents and page_ids:
        for start in range(0, len(extra_parents), 900):
            batch = extra_parents[start : start + 900]
            for r in aot.sql(
                f"SELECT page_id, rev_id, parent_id, title, ts, user, anonymous, comment "
                f"FROM corpus.revision "
                f"WHERE page_id IN ({aot.ints(page_ids)}) AND rev_id IN ({aot.ints(batch)})"
            ):
                rev_by_id[int(r["rev_id"])] = r

    texts_by_rev: dict[int, str] = {}
    for pid in sorted(need_texts_by_page):
        texts_by_rev.update(_fetch_compressed_texts(pid, sorted(need_texts_by_page[pid])))

    s5_revert_by_rev: dict[int, dict[str, Any]] = {}
    s5_summary_by_rev: dict[int, dict[str, Any]] = {}
    s4_episodes_by_rev: dict[int, list[dict[str, Any]]] = defaultdict(list)
    s6_by_rev: dict[int, dict[str, Any]] = {}

    if include_facts:
        if rem_ids:
            for r in aot.sql(
                f"SELECT reverting_rev_id, removal_reasons, auto_family "
                f"FROM research.s5_revert_reason "
                f"WHERE reverting_rev_id IN ({aot.ints(rem_ids)})"
            ):
                s5_revert_by_rev[int(r["reverting_rev_id"])] = r
        if all_rev_ids:
            for r in aot.sql(
                f"SELECT rev_id, auto_family, reasons, insert_flags "
                f"FROM research.s5_summary "
                f"WHERE rev_id IN ({aot.ints(all_rev_ids)})"
            ):
                s5_summary_by_rev[int(r["rev_id"])] = r
            for r in aot.sql(
                f"SELECT er.rev_id, e.episode_id, e.kind, e.revert_count, e.named_participants, e.protection_hint "
                f"FROM research.s4_episode_rev er "
                f"JOIN research.s4_episode e USING (episode_id) "
                f"WHERE er.rev_id IN ({aot.ints(all_rev_ids)}) AND e.is_default = 1"
            ):
                s4_episodes_by_rev[int(r["rev_id"])].append(r)
        if ins_ids:
            for r in aot.sql(
                f"SELECT STRAIGHT_JOIN ra.rev_id, ep.kind, ep.is_bot, ep.edits, ep.distinct_pages, "
                f"ep.reverted_edits_by_others, ep.single_purpose, ep.throwaway "
                f"FROM research.s6_rev_author ra USE INDEX (PRIMARY) "
                f"JOIN research.s6_editor_profile ep ON ep.user = ra.user "
                f"WHERE ra.rev_id IN ({aot.ints(ins_ids)})"
            ):
                s6_by_rev[int(r["rev_id"])] = r

    records_with_pages: list[tuple[int, dict[str, str]]] = []
    lacked_diff_items: list[str] = []
    missing_diff_revs_all: set[int] = set()

    for it in items:
        pid = it["page_id"]
        ins_revs_sorted = sorted(
            (rev_by_id[r] for r in it["inserting_rev_ids"] if r in rev_by_id),
            key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
        )
        rem_revs_sorted = sorted(
            (rev_by_id[r] for r in it["removing_rev_ids"] if r in rev_by_id),
            key=lambda r: (parse_db_timestamp(r["ts"]), int(r["rev_id"])),
        )
        facts_str = (
            format_facts(
                ins_revs=ins_revs_sorted,
                rem_revs=rem_revs_sorted,
                s5_revert_by_rev=s5_revert_by_rev,
                s5_summary_by_rev=s5_summary_by_rev,
                s4_episodes_by_rev=s4_episodes_by_rev,
                s6_by_rev=s6_by_rev,
            )
            if include_facts
            else None
        )
        rec, diag = assemble_item_record(
            item=it,
            article_title=titles_by_page.get(pid, f"page:{pid}"),
            rev_by_id=rev_by_id,
            s3_by_rev=s3_by_rev,
            texts_by_rev=texts_by_rev,
            include_facts=include_facts,
            facts_text=facts_str,
        )
        records_with_pages.append((pid, rec))
        if diag["lacked_diff"]:
            lacked_diff_items.append(diag["id"])
            missing_diff_revs_all.update(diag["missing_diff_revs"])

    stats = {
        "dev_items": len(items),
        "total_inserting_revs": len(ins_ids),
        "lacked_diff_items": len(lacked_diff_items),
        "lacked_diff_item_ids": lacked_diff_items,
        "missing_diff_revs": len(missing_diff_revs_all),
        "sql_queries": aot.STATS["sql_queries"],
        "sql_seconds": round(aot.STATS["sql_seconds"], 2),
    }
    return records_with_pages, stats


def load_jsonl(path: pathlib.Path) -> list[dict[str, Any]]:
    with path.open(encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def write_jsonl(path: pathlib.Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + NL)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Build P1.S9 LLM classification JSONL inputs on the golden dev split.")
    ap.add_argument(
        "--golden",
        type=pathlib.Path,
        default=REPO_ROOT / "research/eval/golden.json",
        help="Path to golden.json (only split='dev' topics are read).",
    )
    ap.add_argument("--facts", action="store_true", help="Add structural `facts` variable (variant C).")
    ap.add_argument(
        "--batch-by-article",
        action="store_true",
        help="Pack <= 6 dev items per article into `classify-edit-batch` rows (variant D).",
    )
    ap.add_argument(
        "--stage2-from",
        type=pathlib.Path,
        help="Offline helper (variant B): emit base items not filtered by stage-1 batch output.",
    )
    ap.add_argument("--stage1-model", help="Model name in `--stage2-from` when the file has multiple models.")
    ap.add_argument(
        "--refute-from",
        type=pathlib.Path,
        help="Offline helper (variant E): emit `refute-classification` inputs from first-pass batch output.",
    )
    ap.add_argument("--refute-model", help="Model name in `--refute-from` when the file has multiple models.")
    ap.add_argument(
        "--refute-categories",
        default=",".join(DEFAULT_REFUTE_CATS),
        help="Comma-separated first-pass categories to send to refute-classification, or 'all'.",
    )
    ap.add_argument(
        "--base-inputs",
        type=pathlib.Path,
        default=HERE / "results/golden-dev.classify.jsonl",
        help="Base single-item classify JSONL for `--stage2-from` / `--refute-from`.",
    )
    ap.add_argument("--out", type=pathlib.Path, help="Output JSONL path (defaults per mode under results/).")
    args = ap.parse_args(argv)

    if args.stage2_from:
        base_rows = load_jsonl(args.base_inputs)
        s1_rows = load_jsonl(args.stage2_from)
        out_rows = build_stage2_inputs(base_rows, s1_rows, model=args.stage1_model)
        out_path = args.out or (HERE / "results/golden-dev.stage2.jsonl")
        write_jsonl(out_path, out_rows)
        aot.log(f"wrote {len(out_rows)}/{len(base_rows)} unfiltered stage-2 items -> {out_path}")
        return 0

    if args.refute_from:
        base_rows = load_jsonl(args.base_inputs)
        fp_rows = load_jsonl(args.refute_from)
        cats = {c.strip() for c in args.refute_categories.split(",") if c.strip()}
        out_rows = build_refute_inputs(base_rows, fp_rows, model=args.refute_model, categories=cats)
        out_path = args.out or (HERE / "results/golden-dev.refute.jsonl")
        write_jsonl(out_path, out_rows)
        aot.log(f"wrote {len(out_rows)}/{len(base_rows)} refute-classification items -> {out_path}")
        return 0

    records_with_pages, stats = generate_from_corpus(args.golden, include_facts=args.facts)
    if args.batch_by_article:
        out_rows = batch_by_article(records_with_pages, max_items=MAX_BATCH_ITEMS)
        default_out = HERE / "results/golden-dev.classify-batch.jsonl"
    else:
        out_rows = [rec for _, rec in records_with_pages]
        default_out = HERE / (
            "results/golden-dev.classify-facts.jsonl" if args.facts else "results/golden-dev.classify.jsonl"
        )

    out_path = args.out or default_out
    write_jsonl(out_path, out_rows)
    aot.log(
        f"wrote {len(out_rows)} rows ({stats['dev_items']} dev items; "
        f"{stats['lacked_diff_items']} items / {stats['missing_diff_revs']} revs lacked s3_diff; "
        f"{stats['sql_queries']} SQL queries in {stats['sql_seconds']} s) -> {out_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
