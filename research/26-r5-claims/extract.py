#!/usr/bin/env python3
"""P1.R5 (#26, #94) Step 1: extract reverted claim passages from research.s1_revert via wikidiff.

    python3 research/26-r5-claims/extract.py [--pages FILE] [--limit N]

Writes to $AOT_CACHE_DIR/r5/ (default ~/.cache/aot-research/r5/, never committed):
  - passages.jsonl: {"passage_id", "page_id", "inserting_rev_ids", "removing_rev_id", "text"}
  - check.jsonl:    {"page_id", "input", "stored"} for 3 non-truncated fi articles (2,000-4,000 chars)
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "research" / "lib"))
import aot  # noqa: E402

NL = chr(10)
SIGNAL_CLASSES = ("dispute", "edit_war", "graffiti_cleanup", "other")
MIN_CHARS = 80
MAX_CHARS = 1500
PK_BATCH = 20
PK_SLEEP_S = 0.2
DIFF_CHUNK = 200

# Core articles of the top 4 demo topics (research/35-s8-signals/candidates.py).
CORE_PAGES = [
    1150, 7675, 9186, 250972, 440, 11398, 105157, 319344, 1256,
    24889, 52097, 8295, 6626, 24094, 1333, 19086, 12374, 9895,
]

_SENT_SPLIT = re.compile(r'''(?<=[.!?])(?:["'”’)\]])?\s+(?=[A-ZÅÄÖŠŽ0-9"'“‘(\[])''')
_CLAUSE_SPLIT = re.compile(r'''(?<=[.!?;:])\s+''')


def _cache_root() -> pathlib.Path:
    return pathlib.Path(os.environ.get("AOT_CACHE_DIR", pathlib.Path.home() / ".cache" / "aot-research"))


def _retry(fn, *args, **kwargs):
    for attempt in range(4):
        try:
            return fn(*args, **kwargs)
        except RuntimeError as ex:
            if attempt == 3:
                raise
            wait = 0.5 * (attempt + 1)
            aot.log(f"[r5 extract] transient SQL error ({ex}); retrying in {wait:.1f}s...")
            time.sleep(wait)


def _load_pages(spec: str) -> list[int]:
    p = pathlib.Path(spec)
    if not p.exists() and spec.strip().upper() == "CORE":
        return list(CORE_PAGES)
    raw = p.read_text(encoding="utf-8").strip()
    if raw.startswith("["):
        return [int(x) for x in json.loads(raw)]
    ids: list[int] = []
    for line in raw.splitlines():
        clean = line.split("#", 1)[0]
        ids.extend(int(m) for m in re.findall(r'''\d+''', clean))
    if not ids:
        raise ValueError(f"no page_ids found in {spec}")
    return list(dict.fromkeys(ids))


def split_passage(text: str, min_chars: int = MIN_CHARS, max_chars: int = MAX_CHARS) -> list[str]:
    """Normalize whitespace and split > max_chars passages at sentence boundaries."""
    norm = " ".join(text.split())
    if len(norm) < min_chars:
        return []
    if len(norm) <= max_chars:
        return [norm]

    def _split_oversized_unit(unit: str) -> list[str]:
        if len(unit) <= max_chars:
            return [unit]
        clauses = [c.strip() for c in _CLAUSE_SPLIT.split(unit) if c.strip()]
        if len(clauses) > 1 and max(len(c) for c in clauses) < len(unit):
            packed = _pack(clauses)
            out: list[str] = []
            for item in packed:
                out.extend(_split_words(item) if len(item) > max_chars else [item])
            return out
        return _split_words(unit)

    def _split_words(s: str) -> list[str]:
        words = s.split(" ")
        chunks: list[str] = []
        cur = ""
        for w in words:
            while len(w) > max_chars:
                if cur:
                    chunks.append(cur)
                    cur = ""
                chunks.append(w[:max_chars])
                w = w[max_chars:]
            cand = f"{cur} {w}" if cur else w
            if len(cand) <= max_chars:
                cur = cand
            else:
                if cur:
                    chunks.append(cur)
                cur = w
        if cur:
            chunks.append(cur)
        return chunks

    def _pack(units: list[str]) -> list[str]:
        packed_units: list[list[str]] = []
        cur: list[str] = []
        cur_len = 0
        for u in units:
            add_len = len(u) if not cur else cur_len + 1 + len(u)
            if cur and add_len > max_chars:
                packed_units.append(cur)
                cur = [u]
                cur_len = len(u)
            else:
                cur.append(u)
                cur_len = add_len
        if cur:
            packed_units.append(cur)
        # Rebalance a short trailing chunk by borrowing sentences from the previous chunk when possible.
        if len(packed_units) >= 2:
            last_str = " ".join(packed_units[-1])
            while len(last_str) < min_chars and len(packed_units[-2]) > 1:
                moved = packed_units[-2].pop()
                candidate = " ".join([moved, *packed_units[-1]])
                if len(candidate) > max_chars:
                    packed_units[-2].append(moved)
                    break
                packed_units[-1].insert(0, moved)
                last_str = candidate
        return [" ".join(g) for g in packed_units]

    raw_sentences = [s.strip() for s in _SENT_SPLIT.split(norm) if s.strip()]
    sentences: list[str] = []
    for s in raw_sentences:
        sentences.extend(_split_oversized_unit(s))
    chunks = _pack(sentences)
    return [c for c in chunks if len(c) >= min_chars]


def extract_passages_from_diff(diff: dict) -> list[str]:
    """Extract >=80-char added prose passages, merging adjacent added prose segments of the same paragraph."""
    if "error" in diff:
        return []
    stats = diff.get("stats") or {}
    if stats.get("is_blanking") or not stats.get("is_substantive"):
        return []

    paragraph_groups: list[str] = []
    cur_para: object = object()
    cur_parts: list[str] = []

    def _flush() -> None:
        nonlocal cur_para, cur_parts
        if cur_parts:
            paragraph_groups.append(" ".join(cur_parts))
            cur_parts = []
        cur_para = object()

    for seg in diff.get("segments") or []:
        if seg.get("moved"):
            continue
        op = seg.get("op")
        kind = seg.get("kind")
        if op == "removed":
            # A removed segment means existing text intervened between replacements in the paragraph.
            _flush()
            continue
        if op == "added" and kind == "prose":
            prose = " ".join((seg.get("prose") or "").split())
            if not prose:
                continue
            new_p = seg.get("new_paragraph")
            if new_p is not None and new_p == cur_para:
                cur_parts.append(prose)
            else:
                _flush()
                cur_para = new_p
                cur_parts = [prose]
        # Added non-prose segments (e.g. inline <ref> or template) within the same added paragraph do not flush.

    _flush()

    passages: list[str] = []
    for group in paragraph_groups:
        passages.extend(split_passage(group))
    return passages


def fetch_rev_texts_throttled(pairs: list[tuple[int, int]]) -> dict[int, str | None]:
    """Fetch (page_id, rev_id) texts via aot.rev_texts in PK batches of 20 with 0.2s sleep on cache misses."""
    uniq = list(dict.fromkeys((int(p), int(r)) for p, r in pairs))
    revtext_dir = _cache_root() / "revtext"
    cached = [pr for pr in uniq if (revtext_dir / f"{pr[1]}.txt").exists()]
    uncached = [pr for pr in uniq if not (revtext_dir / f"{pr[1]}.txt").exists()]

    out: dict[int, str | None] = {}
    if cached:
        out.update(aot.rev_texts(cached))
    total_batches = (len(uncached) + PK_BATCH - 1) // PK_BATCH
    t0 = time.monotonic()
    for b_idx, i in enumerate(range(0, len(uncached), PK_BATCH), 1):
        batch = uncached[i:i + PK_BATCH]
        out.update(_retry(aot.rev_texts, batch))
        if b_idx % 25 == 0 or b_idx == total_batches:
            elapsed = time.monotonic() - t0
            aot.log(f"[r5 extract] fetched {min(i + PK_BATCH, len(uncached))}/{len(uncached)} uncached rev_texts "
                    f"({b_idx}/{total_batches} batches, {elapsed:.1f}s)")
        if i + PK_BATCH < len(uncached):
            time.sleep(PK_SLEEP_S)
    return out


def fetch_inserting_revs(reverting_ids: list[int]) -> dict[int, list[int]]:
    out: dict[int, list[int]] = {}
    chunk_size = 2000
    for i in range(0, len(reverting_ids), chunk_size):
        chunk = reverting_ids[i:i + chunk_size]
        rows = _retry(
            aot.sql,
            f"SELECT reverting_rev_id, rev_id FROM research.s1_reverted_edit "
            f"WHERE reverting_rev_id IN ({aot.ints(chunk)}) ORDER BY reverting_rev_id ASC, rev_id ASC",
        )
        for r in rows:
            out.setdefault(int(r["reverting_rev_id"]), []).append(int(r["rev_id"]))
    return out


def write_check_jsonl(check_path: pathlib.Path) -> int:
    rows = _retry(
        aot.sql,
        """
        SELECT a.page_id, a.title, a.text, VEC_ToText(v.embedding) AS stored
        FROM corpus.article a
        JOIN corpus.article_vector v ON v.lang = a.lang AND v.page_id = a.page_id
        WHERE a.lang = 'fi'
          AND v.truncated = 0
          AND CHAR_LENGTH(a.text) BETWEEN 2000 AND 4000
        ORDER BY a.page_id ASC
        LIMIT 3
        """,
    )
    check_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = check_path.with_suffix(".jsonl.tmp")
    sep = NL + NL
    with tmp.open("w", encoding="utf-8") as f:
        for r in rows:
            rec = {
                "page_id": int(r["page_id"]),
                "input": r["title"] + sep + r["text"],
                "stored": json.loads(r["stored"]),
            }
            print(json.dumps(rec, ensure_ascii=False), file=f)
    tmp.replace(check_path)
    return len(rows)


def main() -> None:
    t_start = time.monotonic()
    default_dir = _cache_root() / "r5"
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--pages", metavar="FILE", help="restrict to page_ids in FILE (or 'CORE')")
    ap.add_argument("--limit", type=int, default=None, metavar="N", help="process at most N candidate reverts")
    ap.add_argument("--out", type=pathlib.Path, default=default_dir / "passages.jsonl", help="output passages.jsonl")
    ap.add_argument("--check-out", type=pathlib.Path, default=default_dir / "check.jsonl", help="output check.jsonl")
    ap.add_argument("--stats-out", type=pathlib.Path, default=None, help="optional JSON summary output path")
    args = ap.parse_args()

    where = [
        "class IN ('dispute','edit_war','graffiti_cleanup','other')",
        "net_bytes >= 80",
    ]
    if args.pages:
        page_ids = _load_pages(args.pages)
        where.append(f"page_id IN ({aot.ints(page_ids)})")
        aot.log(f"[r5 extract] restricting to {len(page_ids)} page(s) from {args.pages}")

    limit_sql = f" LIMIT {int(args.limit)}" if args.limit is not None else ""
    candidates = _retry(
        aot.sql,
        f"""
        SELECT page_id, reverting_rev_id, restored_rev_id, last_reverted_rev_id, class, net_bytes
        FROM research.s1_revert
        WHERE {' AND '.join(where)}
        ORDER BY page_id ASC, reverting_rev_id ASC{limit_sql}
        """,
    )
    aot.log(f"[r5 extract] {len(candidates)} candidate revert(s)")

    inserting_by_rev = fetch_inserting_revs([int(r["reverting_rev_id"]) for r in candidates]) if candidates else {}

    pairs: list[tuple[int, int]] = []
    for r in candidates:
        pid = int(r["page_id"])
        pairs.append((pid, int(r["restored_rev_id"])))
        pairs.append((pid, int(r["last_reverted_rev_id"])))
    texts = fetch_rev_texts_throttled(pairs) if pairs else {}

    by_class = {
        cls: {"reverts": 0, "diffs": 0, "substantive_diffs": 0, "passages": 0, "chars": 0}
        for cls in SIGNAL_CLASSES
    }
    diffable: list[dict] = []
    for r in candidates:
        cls = r["class"]
        by_class[cls]["reverts"] += 1
        old_t = texts.get(int(r["restored_rev_id"]))
        new_t = texts.get(int(r["last_reverted_rev_id"]))
        reverting_id = int(r["reverting_rev_id"])
        if old_t is None or new_t is None or not inserting_by_rev.get(reverting_id):
            continue
        diffable.append(r)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    tmp_out = args.out.with_suffix(".jsonl.tmp")
    wikidiff_cmd = [str(ROOT / "scripts" / "wikidiff.sh"), "batch"]

    total_chunks = (len(diffable) + DIFF_CHUNK - 1) // DIFF_CHUNK
    with tmp_out.open("w", encoding="utf-8") as out_f:
        for c_idx, i in enumerate(range(0, len(diffable), DIFF_CHUNK), 1):
            chunk = diffable[i:i + DIFF_CHUNK]
            payload = NL.join(
                json.dumps(
                    {
                        "id": str(r["reverting_rev_id"]),
                        "old": texts[int(r["restored_rev_id"])],
                        "new": texts[int(r["last_reverted_rev_id"])],
                    },
                    ensure_ascii=False,
                )
                for r in chunk
            )
            proc = subprocess.run(wikidiff_cmd, input=payload, capture_output=True, text=True, encoding="utf-8")
            if proc.returncode != 0:
                sys.exit(f"wikidiff failed (exit {proc.returncode}): {proc.stderr[-500:]}")
            diffs = [json.loads(line) for line in proc.stdout.splitlines() if line.strip()]
            if len(diffs) != len(chunk):
                sys.exit(f"wikidiff count mismatch: sent {len(chunk)}, got {len(diffs)}")

            for r, d in zip(chunk, diffs):
                cls = r["class"]
                if "error" in d:
                    continue
                by_class[cls]["diffs"] += 1
                st = d.get("stats") or {}
                if st.get("is_blanking") or not st.get("is_substantive"):
                    continue
                by_class[cls]["substantive_diffs"] += 1
                passages = extract_passages_from_diff(d)
                if not passages:
                    continue
                rev_id = int(r["reverting_rev_id"])
                page_id = int(r["page_id"])
                ins_ids = inserting_by_rev[rev_id]
                for idx, p_text in enumerate(passages):
                    rec = {
                        "passage_id": f"{rev_id}:{idx}",
                        "page_id": page_id,
                        "inserting_rev_ids": ins_ids,
                        "removing_rev_id": rev_id,
                        "text": p_text,
                    }
                    print(json.dumps(rec, ensure_ascii=False), file=out_f)
                    by_class[cls]["passages"] += 1
                    by_class[cls]["chars"] += len(p_text)

            if c_idx % 5 == 0 or c_idx == total_chunks:
                aot.log(f"[r5 extract] diffed {min(i + DIFF_CHUNK, len(diffable))}/{len(diffable)} pairs "
                        f"({c_idx}/{total_chunks} chunks)")

    tmp_out.replace(args.out)
    n_check = write_check_jsonl(args.check_out)
    elapsed_s = round(time.monotonic() - t_start, 2)

    totals = {
        k: sum(by_class[c][k] for c in SIGNAL_CLASSES)
        for k in ("reverts", "diffs", "substantive_diffs", "passages", "chars")
    }

    print(f"Wrote {totals['passages']} passages ({totals['chars']} chars) -> {args.out}")
    print(f"Wrote {n_check} check articles -> {args.check_out}")
    print(f"Elapsed: {elapsed_s:.1f}s")
    print()
    print(f"{'class':<18} {'reverts':>8} {'diffs':>8} {'subst_diffs':>12} {'passages':>10} {'chars':>12}")
    print("-" * 72)
    for cls in SIGNAL_CLASSES:
        st = by_class[cls]
        print(f"{cls:<18} {st['reverts']:>8} {st['diffs']:>8} {st['substantive_diffs']:>12} "
              f"{st['passages']:>10} {st['chars']:>12}")
    print("-" * 72)
    print(f"{'TOTAL':<18} {totals['reverts']:>8} {totals['diffs']:>8} {totals['substantive_diffs']:>12} "
          f"{totals['passages']:>10} {totals['chars']:>12}")

    if args.stats_out:
        args.stats_out.parent.mkdir(parents=True, exist_ok=True)
        args.stats_out.write_text(
            json.dumps({"elapsed_seconds": elapsed_s, "totals": totals, "by_class": by_class}, indent=2) + NL,
            encoding="utf-8",
        )


if __name__ == "__main__":
    main()
