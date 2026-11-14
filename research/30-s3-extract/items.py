#!/usr/bin/env python3
"""P1.S3 (#30) approach A: build `research.s3_item` from `s1_revert` spans + precomputed `s3_diff`.

Drops and recreates `research.s3_item` with one row per non-excluded `s1_revert` on pages present in
`research.s3_diff` that have a `kind='span'` diff row. Gracefully counts and reports any missing or
errored `s3_diff` rows without crashing, and prints row counts per revert class.

    python3 research/30-s3-extract/items.py
"""
from __future__ import annotations

import argparse
import ipaddress
import json
import pathlib
import re
import sys
import time
from collections import Counter

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "lib"))
import aot  # noqa: E402

_NL = chr(10)
_DQ = chr(34)
_TEMP_RE = re.compile(r'''^~\d{4}-\d+-\d+$''')
_IPV4_RE = re.compile(r'''^\d{1,3}(\.\d{1,3}){3}$''')
_FRAG_JOIN_RE = re.compile(r'''\s+…\s+''')
_SENT_BOUNDARY_RE = re.compile(r'''([.!?]+)(?:['"”»)\]]*)\s+(?=['"“«(\[]*\S)''')
_PREV_TOK_SPLIT_RE = re.compile(r'''[\s(\["'“«/]+''')
_ABBREVS = frozenset({
    "ns", "esim", "mm", "ym", "yms", "ks", "kts", "vrt", "toim", "s", "ss",
    "nro", "n", "v", "vs", "klo", "prof", "tri", "evp", "ups", "kenr", "maj",
    "kapt", "luutn", "alil", "kers", "st", "dr", "mr", "mrs", "ms", "jr", "sr",
    "inc", "ltd", "co", "al", "et", "op", "cit", "ibid", "no", "vol", "fig",
    "ed", "eds", "trans", "ch", "p", "pp", "ca", "c", "e", "i",
})

MAX_WIKITEXT_CHARS = 20_000
PAGE_CHUNK_SIZE = 50
INSERT_BATCH_ROWS = 250
INSERT_BATCH_BYTES = 1_500_000


def editor_grouping_key(user: str | None) -> str:
    """Port of EditorDisplay.For(user).GroupingKey (EditorDisplay.cs). Never returns a raw IP."""
    if user is None or not user.strip():
        return "hidden"
    s = user.strip()
    if _TEMP_RE.match(s):
        return f"temp:{s}"
    if _IPV4_RE.match(s):
        try:
            b = ipaddress.IPv4Address(s).packed
            return f"ip4:{b[0]}.{b[1]}"
        except ipaddress.AddressValueError:
            pass
    if ":" in s:
        try:
            b = ipaddress.IPv6Address(s).packed
            g1 = (b[0] << 8) | b[1]
            g2 = (b[2] << 8) | b[3]
            return f"ip6:{g1:X}:{g2:X}"
        except ipaddress.AddressValueError:
            pass
    return f"user:{s}"


def is_edit_anonymous(user: str | None, anonymous_flag: int | str | None = None) -> bool:
    """True for IPv4, IPv6, and temporary accounts; False for named accounts and hidden editors."""
    if user is None or not user.strip():
        return False
    if anonymous_flag is not None and int(anonymous_flag) == 1:
        return True
    return aot.is_anonymous(user)


def count_sentences(prose: str | None) -> int:
    """Count sentences in cleaned Wikipedia prose (handling Finnish dates, initials, and abbreviations)."""
    if not prose:
        return 0
    total = 0
    for raw_line in prose.splitlines():
        line = _FRAG_JOIN_RE.sub(" ", raw_line)
        line = " ".join(line.split())
        if not line or not any(ch.isalnum() for ch in line):
            continue
        splits = 0
        for m in _SENT_BOUNDARY_RE.finditer(line):
            punct = m.group(1)
            after = line[m.end():].lstrip("“«([ '" + _DQ)
            if not after or not any(ch.isalnum() for ch in after):
                continue
            if "." in punct and "!" not in punct and "?" not in punct:
                first_ch = after[0]
                if first_ch.isalpha() and first_ch.islower():
                    continue
                before = line[:m.start()].rstrip("”»)] '" + _DQ)
                prev_tok = _PREV_TOK_SPLIT_RE.split(before)[-1] if before else ""
                prev_word = prev_tok.split(".")[-1]
                if prev_word.lower() in _ABBREVS:
                    continue
                if len(prev_word) == 1 and prev_word.isalpha() and prev_word.isupper():
                    continue
                if prev_word.isdigit() and len(prev_word) <= 3:
                    continue
            splits += 1
        total += splits + 1
    return total


def extract_inserted_wikitext(segments: list[dict], max_chars: int = MAX_WIKITEXT_CHARS) -> str:
    """Join non-moved added segments' raw `text` (grouping fragments by `new_paragraph`), capped at 20,000 chars."""
    paragraphs: list[str] = []
    cur_para: object = object()
    cur_parts: list[str] = []

    def flush() -> None:
        if cur_parts:
            paragraphs.append("".join(cur_parts))

    for seg in segments:
        if seg.get("op") != "added" or seg.get("moved", False):
            continue
        txt = seg.get("text")
        if not txt:
            continue
        np = seg.get("new_paragraph")
        if np is not None and np == cur_para and cur_parts:
            sep = "" if (txt.startswith("<ref") or txt[0] in ".,;:!?)") else " "
            cur_parts.append(sep + txt)
        else:
            flush()
            cur_para = np
            cur_parts = [txt]
    flush()
    joined = _NL.join(paragraphs)
    return joined[:max_chars]


def summarize_segment_kinds(segments: list[dict]) -> dict[str, dict[str, int]]:
    """Counts of non-moved `added` and `removed` segments per segment `kind`."""
    counts: dict[str, dict[str, int]] = {}
    for seg in segments:
        if seg.get("moved", False):
            continue
        op = seg.get("op")
        if op not in ("added", "removed"):
            continue
        kind = str(seg.get("kind") or "unknown")
        bucket = counts.setdefault(kind, {"added": 0, "removed": 0})
        bucket[op] += 1
    return {k: counts[k] for k in sorted(counts)}


def choose_inserting_revs(span_edits: list[dict], edit_diffs: dict[int, dict]) -> list[dict]:
    """Select reverted edits in the span whose edit diff added prose or refs, falling back to all span edits."""
    ordered = sorted(span_edits, key=lambda e: int(e["rev_id"]))
    filtered = []
    for e in ordered:
        d = edit_diffs.get(int(e["rev_id"]))
        if d is not None and (int(d.get("added_prose_chars") or 0) > 0 or int(d.get("added_refs") or 0) > 0):
            filtered.append(e)
    return filtered if filtered else ordered


def build_item_row(rev: dict, span_diff: dict, span_edits: list[dict], edit_diffs: dict[int, dict]) -> dict:
    """Build one `research.s3_item` row dictionary from S1 revert + S3 span/edit diffs."""
    chosen = choose_inserting_revs(span_edits, edit_diffs)
    ins_ids = [int(e["rev_id"]) for e in chosen]
    if not ins_ids:
        raise ValueError(f"revert {rev['reverting_rev_id']} has no non-null reverted edits")

    editors = sorted({editor_grouping_key(e.get("user")) for e in chosen})
    anon_only = 1 if all(is_edit_anonymous(e.get("user"), e.get("anonymous")) for e in chosen) else 0

    raw_segments = span_diff.get("segments")
    if isinstance(raw_segments, str):
        segments = json.loads(raw_segments) if raw_segments.strip() else []
    elif isinstance(raw_segments, list):
        segments = raw_segments
    else:
        segments = []

    inserted_prose = span_diff.get("added_prose") or ""
    removed_prose = span_diff.get("removed_prose") or ""
    inserted_wikitext = extract_inserted_wikitext(segments, MAX_WIKITEXT_CHARS)
    seg_kinds = summarize_segment_kinds(segments)

    reins_raw = rev.get("reinserted_rev_id")
    reinserted_rev_id = int(reins_raw) if reins_raw is not None and str(reins_raw) != "" else None
    exposure_seconds = max(0, int(rev["seconds_to_revert"]))

    return {
        "reverting_rev_id": int(rev["reverting_rev_id"]),
        "page_id": int(rev["page_id"]),
        "class": str(rev["class"]),
        "inserting_rev_ids": ",".join(str(r) for r in ins_ids),
        "inserting_editors": json.dumps(editors, ensure_ascii=False),
        "inserting_anonymous_only": anon_only,
        "removing_user_display": aot.user_display(rev.get("reverting_user")),
        "removal_summary": aot.mask_comment(rev.get("reverting_comment")),
        "inserted_prose": inserted_prose,
        "inserted_wikitext": inserted_wikitext,
        "removed_prose": removed_prose,
        "segment_kinds": json.dumps(seg_kinds, ensure_ascii=False, separators=(",", ":")),
        "inserted_at": str(rev["first_reverted_ts"]),
        "removed_at": str(rev["revert_ts"]),
        "exposure_seconds": exposure_seconds,
        "reinserted_rev_id": reinserted_rev_id,
        "prose_chars": int(span_diff.get("added_prose_chars") or 0),
        "sentence_count": count_sentences(inserted_prose),
        "refs_added": int(span_diff.get("added_refs") or 0),
        "refs_removed": int(span_diff.get("removed_refs") or 0),
        "is_substantive": int(span_diff.get("is_substantive") or 0),
        "is_blanking": int(span_diff.get("is_blanking") or 0),
    }


DDL = """
CREATE TABLE research.s3_item (
  reverting_rev_id         INT UNSIGNED NOT NULL PRIMARY KEY,
  page_id                  INT UNSIGNED NOT NULL,
  class                    VARCHAR(32)  NOT NULL,
  inserting_rev_ids        MEDIUMTEXT   NOT NULL,
  inserting_editors        MEDIUMTEXT   NOT NULL,
  inserting_anonymous_only TINYINT(1)   NOT NULL,
  removing_user_display    VARCHAR(300) NOT NULL,
  removal_summary          MEDIUMTEXT   NOT NULL,
  inserted_prose           MEDIUMTEXT   NOT NULL,
  inserted_wikitext        MEDIUMTEXT   NOT NULL,
  removed_prose            MEDIUMTEXT   NOT NULL,
  segment_kinds            MEDIUMTEXT   NOT NULL,
  inserted_at              DATETIME     NOT NULL,
  removed_at               DATETIME     NOT NULL,
  exposure_seconds         BIGINT       NOT NULL,
  reinserted_rev_id        INT UNSIGNED NULL,
  prose_chars              INT          NOT NULL,
  sentence_count           INT          NOT NULL,
  refs_added               INT          NOT NULL,
  refs_removed             INT          NOT NULL,
  is_substantive           TINYINT(1)   NOT NULL,
  is_blanking              TINYINT(1)   NOT NULL,
  KEY idx_page_filter (page_id, is_substantive, is_blanking, class),
  KEY idx_class (class)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
"""

COLUMNS = (
    "reverting_rev_id",
    "page_id",
    "class",
    "inserting_rev_ids",
    "inserting_editors",
    "inserting_anonymous_only",
    "removing_user_display",
    "removal_summary",
    "inserted_prose",
    "inserted_wikitext",
    "removed_prose",
    "segment_kinds",
    "inserted_at",
    "removed_at",
    "exposure_seconds",
    "reinserted_rev_id",
    "prose_chars",
    "sentence_count",
    "refs_added",
    "refs_removed",
    "is_substantive",
    "is_blanking",
)


def _row_sql_tuple(row: dict) -> str:
    vals = []
    for col in COLUMNS:
        v = row[col]
        if v is None:
            vals.append("NULL")
        elif isinstance(v, int):
            vals.append(str(v))
        else:
            vals.append(aot.quote(str(v)))
    return "(" + ",".join(vals) + ")"


def _insert_rows(rows: list[dict]) -> None:
    if not rows:
        return
    cols_sql = ",".join(f"`{c}`" for c in COLUMNS)
    prefix = f"INSERT INTO research.s3_item ({cols_sql}) VALUES "
    batch: list[str] = []
    batch_bytes = len(prefix)
    for row in rows:
        tup = _row_sql_tuple(row)
        tup_bytes = len(tup.encode("utf-8")) + 1
        if batch and (len(batch) >= INSERT_BATCH_ROWS or batch_bytes + tup_bytes > INSERT_BATCH_BYTES):
            aot.sql(prefix + ",".join(batch))
            batch = []
            batch_bytes = len(prefix)
        batch.append(tup)
        batch_bytes += tup_bytes
    if batch:
        aot.sql(prefix + ",".join(batch))


def build_s3_items(page_filter: list[int] | None = None) -> dict:
    """Drop + recreate `research.s3_item` for all pages present in `research.s3_diff`."""
    t0 = time.monotonic()
    s3_pages = sorted(int(r["page_id"]) for r in aot.sql("SELECT DISTINCT page_id FROM research.s3_diff"))
    if page_filter:
        allowed = set(page_filter)
        s3_pages = [p for p in s3_pages if p in allowed]

    aot.sql("DROP TABLE IF EXISTS research.s3_item")
    aot.sql(DDL)

    class_counts: Counter[str] = Counter()
    missing_span = 0
    errored_span = 0
    missing_edit_diff = 0
    fallback_spans = 0
    total_items = 0

    for start in range(0, len(s3_pages), PAGE_CHUNK_SIZE):
        chunk_pages = s3_pages[start:start + PAGE_CHUNK_SIZE]
        pages_csv = aot.ints(chunk_pages)

        reverts = aot.sql(f"""
            SELECT page_id, reverting_rev_id, restored_rev_id, first_reverted_rev_id, last_reverted_rev_id,
                   reverting_user, revert_ts, first_reverted_ts, seconds_to_revert, reinserted_rev_id,
                   class, reverting_comment
            FROM research.s1_revert
            WHERE page_id IN ({pages_csv})
              AND class <> 'excluded'
            ORDER BY reverting_rev_id ASC
        """)
        if not reverts:
            continue

        rev_ids = [int(r["reverting_rev_id"]) for r in reverts]
        span_rows = aot.sql(f"""
            SELECT rev_id, added_prose, removed_prose, added_prose_chars, removed_prose_chars,
                   added_refs, removed_refs, is_substantive, is_blanking, segments, error
            FROM research.s3_diff
            WHERE kind = 'span'
              AND rev_id IN ({aot.ints(rev_ids)})
        """)
        spans_by_rev = {int(r["rev_id"]): r for r in span_rows}

        reverted_edits = aot.sql(f"""
            SELECT page_id, rev_id, reverting_rev_id, user, anonymous, ts
            FROM research.s1_reverted_edit
            WHERE reverting_rev_id IN ({aot.ints(rev_ids)})
            ORDER BY reverting_rev_id ASC, rev_id ASC
        """)
        edits_by_revert: dict[int, list[dict]] = {}
        all_edit_rev_ids: set[int] = set()
        for e in reverted_edits:
            rr = int(e["reverting_rev_id"])
            edits_by_revert.setdefault(rr, []).append(e)
            all_edit_rev_ids.add(int(e["rev_id"]))

        edit_diffs: dict[int, dict] = {}
        edit_id_list = sorted(all_edit_rev_ids)
        for es in range(0, len(edit_id_list), 1000):
            sub_ids = edit_id_list[es:es + 1000]
            for d in aot.sql(f"""
                SELECT rev_id, added_prose_chars, added_refs, error
                FROM research.s3_diff
                WHERE kind = 'edit'
                  AND rev_id IN ({aot.ints(sub_ids)})
            """):
                if not d.get("error"):
                    edit_diffs[int(d["rev_id"])] = d

        out_rows: list[dict] = []
        for rev in reverts:
            rr = int(rev["reverting_rev_id"])
            span = spans_by_rev.get(rr)
            if span is None:
                missing_span += 1
                continue
            if span.get("error"):
                errored_span += 1
                continue

            span_edits = edits_by_revert.get(rr, [])
            if not span_edits:
                # Defensive fallback if s1_reverted_edit ever lacked owned edits: non-null span revisions.
                pid = int(rev["page_id"])
                fr = int(rev["first_reverted_rev_id"])
                lr = int(rev["last_reverted_rev_id"])
                span_edits = aot.sql(f"""
                    SELECT page_id, rev_id, user, anonymous, ts
                    FROM research.s1_reverted_edit
                    WHERE page_id = {pid} AND rev_id >= {fr} AND rev_id <= {lr}
                    ORDER BY rev_id ASC
                """)
            if not span_edits:
                continue

            for e in span_edits:
                if int(e["rev_id"]) not in edit_diffs:
                    missing_edit_diff += 1

            has_adding = any(
                (int(edit_diffs[int(e["rev_id"])].get("added_prose_chars") or 0) > 0
                 or int(edit_diffs[int(e["rev_id"])].get("added_refs") or 0) > 0)
                for e in span_edits
                if int(e["rev_id"]) in edit_diffs
            )
            if not has_adding:
                fallback_spans += 1

            item_row = build_item_row(rev, span, span_edits, edit_diffs)
            out_rows.append(item_row)
            class_counts[item_row["class"]] += 1

        _insert_rows(out_rows)
        total_items += len(out_rows)
        aot.log(f"[{min(start + PAGE_CHUNK_SIZE, len(s3_pages))}/{len(s3_pages)} pages] +{len(out_rows)} items (total {total_items})")

    elapsed = time.monotonic() - t0
    return {
        "pages_in_s3_diff": len(s3_pages),
        "total_items": total_items,
        "class_counts": dict(sorted(class_counts.items())),
        "missing_span_rows": missing_span,
        "errored_span_rows": errored_span,
        "missing_edit_diff_rows": missing_edit_diff,
        "fallback_all_span_revs": fallback_spans,
        "elapsed_seconds": round(elapsed, 2),
    }


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--pages", help="Optional comma-separated page_ids filter (for debugging only)")
    args = ap.parse_args(argv)
    page_filter = [int(x) for x in args.pages.split(",") if x.strip()] if args.pages else None

    stats = build_s3_items(page_filter=page_filter)
    print(f"Built research.s3_item across {stats['pages_in_s3_diff']} s3_diff page(s): "
          f"{stats['total_items']} item(s) in {stats['elapsed_seconds']:.1f} s")
    print("Row counts per class:")
    for cls, n in stats["class_counts"].items():
        print(f"  {cls:18s} {n:6d}")
    print(f"Missing s3_diff span rows: {stats['missing_span_rows']}, "
          f"errored spans: {stats['errored_span_rows']}, "
          f"missing edit diffs: {stats['missing_edit_diff_rows']}, "
          f"fallback-to-all-span-revs: {stats['fallback_all_span_revs']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
