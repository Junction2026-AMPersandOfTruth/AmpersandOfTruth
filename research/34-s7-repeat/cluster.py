#!/usr/bin/env python3
"""P1.S7 (#34): Re-insertion and cross-article repetition clusters over removed content.

Builds `research.s7_cluster` and `research.s7_member` over golden pages from precomputed
`research.s3_diff` (kind='span') joined with `research.s1_revert` and `research.s1_reverted_edit`.

Keeps both:
  - `method = 'exact'`: normalised sentence SHA-1 links (>= 30 chars, boilerplate guard > 8 distinct pages)
  - `method = 'fuzzy'`: exact links + word 5-shingle MinHash (64 seeded permutations + LSH banding)
                        verified by exact true Jaccard >= 0.6

Standard library only; corpus queries go through `research/lib/aot.py` in small page batches.
"""
from __future__ import annotations

import argparse
import base64
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
import hashlib
import json
import pathlib
import random
import re
import sys
import time
import zlib

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

NL = chr(10)
MIN_PROSE_CHARS = 30
BOILERPLATE_MAX_PAGES = 8
SHINGLE_WORDS = 5
MINHASH_PERMS = 64
LSH_BANDS = 32
LSH_ROWS = 2  # 32 * 2 == 64 permutations: P(candidate | J=0.6) = 1 - (1 - 0.36)^32 = 99.99994%
MINHASH_SEED = 20261114
JACCARD_THRESHOLD = 0.6
MERSENNE_PRIME = (1 << 61) - 1
PAGE_BATCH_SIZE = 10
INSERT_BATCH_SIZE = 400

EXCLUDED_REVERT_CLASSES = ("excluded", "bot", "self_revert")

_RE_HTML_COMMENT = re.compile(r"<!--.*?-->", re.DOTALL)
_RE_SELF_TAG = re.compile(r"<[a-zA-Z][^>/]*/\s*>")
_RE_HTML_TAG = re.compile(r"</?[a-zA-Z][^>]*>")
_RE_USER_LINK = re.compile(r"\[\[\s*(?:user|käyttäjä|keskustelu käyttäjästä|user talk)\s*:[^\[\]]*\]\]", re.IGNORECASE)
_RE_WIKILINK = re.compile(r"\[\[(?:[^\[\]|]*\|)?([^\[\]]*)\]\]")
_RE_EXTLINK = re.compile(r"\[https?://([^\s\]]+)(?:\s+([^\]]*))?\]", re.IGNORECASE)
_RE_MARKUP = re.compile(r"\{\{|\}\}|\{\||\|\}|'''|''")
_RE_SENT_SPLIT = re.compile("(?:[" + chr(13) + chr(10) + r"]+|\s+…\s+|(?<=[.!?])\s+)")
_RE_NON_WORD = re.compile(r"[^\w\s]", re.UNICODE)
_RE_UNDERSCORE = re.compile(r"_+")


@dataclass
class Unit:
    """One removed-content item: an S1 revert span with >= 30 chars of extracted prose."""
    page_id: int
    reverting_rev_id: int
    inserting_rev_ids: list[int]
    inserted_at: str          # "YYYY-MM-DD HH:MM:SS" UTC
    removed_at: str           # "YYYY-MM-DD HH:MM:SS" UTC
    account_key: str          # 12-char SHA-1 of masked grouping key (never raw username/IP)
    prose_chars: int          # length of normalised extracted prose
    sample_text: str          # <= 300 chars, IP-masked and whitespace-collapsed
    sentences: list[str]      # normalised sentences (>= 30 chars each)
    norm_text: str            # full normalised unit text


@dataclass
class ClusterRecord:
    cluster_id: int
    method: str               # "exact" | "fuzzy"
    n_units: int
    n_pages: int
    n_accounts: int
    n_reinsertions: int
    first_ts: str
    last_ts: str
    span_days: float
    max_gap_days: float
    sample_text: str
    campaign_signals: str
    prose_chars: int
    members: list[Unit] = field(default_factory=list)


class UnionFind:
    """Disjoint-set forest with path compression and deterministic smallest-root union."""

    def __init__(self, n: int) -> None:
        self.parent = list(range(n))

    def find(self, x: int) -> int:
        while self.parent[x] != x:
            self.parent[x] = self.parent[self.parent[x]]
            x = self.parent[x]
        return x

    def union(self, a: int, b: int) -> bool:
        ra, rb = self.find(a), self.find(b)
        if ra == rb:
            return False
        if ra > rb:
            ra, rb = rb, ra
        self.parent[rb] = ra
        return True

    def components(self) -> list[list[int]]:
        groups: dict[int, list[int]] = defaultdict(list)
        for i in range(len(self.parent)):
            groups[self.find(i)].append(i)
        return [groups[k] for k in sorted(groups)]


# ── Text extraction & normalisation ──────────────────────────────────────────

def extract_unit_parts(segments: str | list[dict] | None, fallback_added_prose: str | None = None) -> list[str]:
    """Extract visible `prose` of added `prose`/`template` segments + raw `text` of added `reference` segments."""
    if segments is not None:
        segs = json.loads(segments) if isinstance(segments, str) else segments
        parts: list[str] = []
        for s in segs:
            if s.get("op") != "added" or s.get("moved"):
                continue
            kind = s.get("kind")
            if kind in ("prose", "template"):
                val = (s.get("prose") or "").strip()
                if val:
                    parts.append(val)
            elif kind == "reference":
                val = (s.get("text") or "").strip()
                if val:
                    parts.append(val)
        return parts
    if fallback_added_prose and fallback_added_prose.strip():
        return [fallback_added_prose.strip()]
    return []


def strip_wiki_markup(text: str) -> str:
    """Flatten wiki markup and HTML/ref tags while preserving inner prose and citation strings."""
    if not text:
        return ""
    s = _RE_HTML_COMMENT.sub(" ", text)
    s = _RE_USER_LINK.sub(" ", s)
    s = _RE_SELF_TAG.sub(" ", s)
    s = _RE_HTML_TAG.sub(" ", s)
    # Handle nested wikilinks (e.g. [[File:...|thumb|text with [[link]]]]) in 2 passes
    for _ in range(2):
        s = _RE_WIKILINK.sub(r" \1 ", s)
    s = _RE_EXTLINK.sub(r" \1 \2 ", s)
    s = _RE_MARKUP.sub(" ", s)
    return s


def normalise_text(text: str) -> str:
    """Lowercase, strip wiki markup and punctuation, collapse spaces."""
    s = strip_wiki_markup(text).lower()
    s = _RE_NON_WORD.sub(" ", s)
    s = _RE_UNDERSCORE.sub(" ", s)
    return " ".join(s.split())


def extract_sentences(parts: list[str], min_chars: int = MIN_PROSE_CHARS) -> list[str]:
    """Split extracted unit parts into normalised sentences >= `min_chars` characters."""
    out: list[str] = []
    seen: set[str] = set()
    for part in parts:
        cleaned = strip_wiki_markup(part)
        for chunk in _RE_SENT_SPLIT.split(cleaned):
            norm = normalise_text(chunk)
            if len(norm) >= min_chars and norm not in seen:
                seen.add(norm)
                out.append(norm)
    if not out:
        full = normalise_text(NL.join(parts))
        if len(full) >= min_chars:
            out.append(full)
    return out


def sentence_hash(norm_sentence: str) -> str:
    """16-hex-char SHA-1 of a normalised sentence."""
    return hashlib.sha1(norm_sentence.encode("utf-8")).hexdigest()[:16]


def make_sample_text(parts: list[str], max_chars: int = 300) -> str:
    """Build safe display sample_text <= 300 chars with wiki markup flattened and IPs masked."""
    raw = " ".join(strip_wiki_markup(NL.join(parts)).split())
    masked = " ".join(aot.mask_comment(raw).split())
    if len(masked) <= max_chars:
        return masked
    return masked[: max_chars - 1].rstrip() + "…"


def make_account_key(user: str | None) -> str:
    """Short SHA-1 of the masked editor grouping key (`aot.user_display`), never the raw name/IP."""
    grouped = aot.user_display(user)
    return hashlib.sha1(grouped.encode("utf-8")).hexdigest()[:12]


# ── Shingling, MinHash & LSH ─────────────────────────────────────────────────

def word_shingles(norm_str: str, k: int = SHINGLE_WORDS) -> set[str]:
    """Set of k-word shingles from already-normalised text."""
    words = norm_str.split()
    if len(words) < k:
        return set()
    return {" ".join(words[i : i + k]) for i in range(len(words) - k + 1)}


def jaccard(a: set[str], b: set[str]) -> float:
    if not a or not b:
        return 0.0
    inter = len(a & b)
    if inter == 0:
        return 0.0
    return inter / (len(a) + len(b) - inter)


class MinHashLSH:
    """Pure-Python seeded 64-permutation MinHash + LSH banding + exact Jaccard verification."""

    def __init__(
        self,
        num_perm: int = MINHASH_PERMS,
        bands: int = LSH_BANDS,
        seed: int = MINHASH_SEED,
    ) -> None:
        if num_perm % bands != 0:
            raise ValueError(f"num_perm ({num_perm}) must be divisible by bands ({bands})")
        self.num_perm = num_perm
        self.bands = bands
        self.rows = num_perm // bands
        rng = random.Random(seed)
        self._coeffs: list[tuple[int, int]] = [
            (rng.randint(1, MERSENNE_PRIME - 1), rng.randint(0, MERSENNE_PRIME - 1))
            for _ in range(num_perm)
        ]
        self._shingle_hash_cache: dict[str, int] = {}

    def _base_hash(self, shingle: str) -> int:
        h = self._shingle_hash_cache.get(shingle)
        if h is None:
            digest = hashlib.sha1(shingle.encode("utf-8")).digest()
            h = int.from_bytes(digest[:8], "little") & MERSENNE_PRIME
            self._shingle_hash_cache[shingle] = h
        return h

    def signature(self, shingles: set[str]) -> tuple[int, ...]:
        if not shingles:
            return ()
        base_hashes = [self._base_hash(s) for s in shingles]
        p = MERSENNE_PRIME
        sig: list[int] = []
        for a, b in self._coeffs:
            m = p
            for x in base_hashes:
                v = (a * x + b) % p
                if v < m:
                    m = v
            sig.append(m)
        return tuple(sig)

    def find_fuzzy_links(
        self,
        shingle_sets: list[set[str]],
        threshold: float = JACCARD_THRESHOLD,
    ) -> tuple[list[tuple[int, int, float]], int]:
        """Return `(verified_links, candidate_pair_count)` for items with true Jaccard >= threshold."""
        buckets: dict[tuple[int, tuple[int, ...]], list[int]] = defaultdict(list)
        r = self.rows
        for idx, sh in enumerate(shingle_sets):
            if not sh:
                continue
            sig = self.signature(sh)
            for b in range(self.bands):
                start = b * r
                buckets[(b, sig[start : start + r])].append(idx)

        candidate_pairs: set[tuple[int, int]] = set()
        for members in buckets.values():
            if len(members) < 2:
                continue
            for i in range(len(members)):
                u = members[i]
                for j in range(i + 1, len(members)):
                    v = members[j]
                    candidate_pairs.add((u, v) if u < v else (v, u))

        verified: list[tuple[int, int, float]] = []
        for u, v in sorted(candidate_pairs):
            sim = jaccard(shingle_sets[u], shingle_sets[v])
            if sim >= threshold:
                verified.append((u, v, sim))
        return verified, len(candidate_pairs)


# ── Clustering & Campaign Signals ────────────────────────────────────────────

def _parse_ts(ts_str: str) -> datetime:
    return datetime.strptime(ts_str[:19], "%Y-%m-%d %H:%M:%S")


def compute_campaign_signals(n_units: int, n_pages: int, n_accounts: int, span_days: float) -> list[str]:
    """Which campaign signals hold: accounts (>=2), articles (>=2), persistence (>=3 insertions & >=7d span)."""
    signals: list[str] = []
    if n_accounts >= 2:
        signals.append("accounts")
    if n_pages >= 2:
        signals.append("articles")
    if n_units >= 3 and span_days >= 7.0:
        signals.append("persistence")
    return signals


def build_clusters_from_uf(
    units: list[Unit],
    uf: UnionFind,
    method: str,
    start_cluster_id: int = 1,
) -> list[ClusterRecord]:
    """Materialise sorted ClusterRecord objects from UnionFind connected components."""
    comps = uf.components()
    raw_clusters: list[tuple[str, int, list[Unit]]] = []
    for idxs in comps:
        members = sorted(
            (units[i] for i in idxs),
            key=lambda u: (u.inserted_at, u.reverting_rev_id),
        )
        raw_clusters.append((members[0].inserted_at, members[0].reverting_rev_id, members))

    # Deterministic numbering: larger clusters first, then chronological
    raw_clusters.sort(key=lambda item: (-len(item[2]), item[0], item[1]))

    records: list[ClusterRecord] = []
    for offset, (_, _, members) in enumerate(raw_clusters):
        cid = start_cluster_id + offset
        n_units = len(members)
        n_pages = len({m.page_id for m in members})
        n_accounts = len({m.account_key for m in members})
        n_reinsertions = n_units - 1
        first_ts = members[0].inserted_at
        last_ts = members[-1].inserted_at
        t_first = _parse_ts(first_ts)
        t_last = _parse_ts(last_ts)
        span_days = max(0.0, (t_last - t_first).total_seconds() / 86400.0)

        # Quiet gap before a re-addition (sleeper pattern): gap from latest removal so far to next insertion
        max_gap_days = 0.0
        if n_units >= 2:
            latest_removed = _parse_ts(members[0].removed_at)
            for m in members[1:]:
                ins_dt = _parse_ts(m.inserted_at)
                gap_d = max(0.0, (ins_dt - latest_removed).total_seconds() / 86400.0)
                if gap_d > max_gap_days:
                    max_gap_days = gap_d
                rem_dt = _parse_ts(m.removed_at)
                if rem_dt > latest_removed:
                    latest_removed = rem_dt

        sample_text = members[0].sample_text
        prose_chars = max(m.prose_chars for m in members)
        sigs = compute_campaign_signals(n_units, n_pages, n_accounts, span_days)

        records.append(
            ClusterRecord(
                cluster_id=cid,
                method=method,
                n_units=n_units,
                n_pages=n_pages,
                n_accounts=n_accounts,
                n_reinsertions=n_reinsertions,
                first_ts=first_ts,
                last_ts=last_ts,
                span_days=round(span_days, 4),
                max_gap_days=round(max_gap_days, 4),
                sample_text=sample_text,
                campaign_signals=",".join(sigs),
                prose_chars=prose_chars,
                members=members,
            )
        )
    return records


def cluster_units(
    units: list[Unit],
    boilerplate_max_pages: int = BOILERPLATE_MAX_PAGES,
    jaccard_threshold: float = JACCARD_THRESHOLD,
    seed: int = MINHASH_SEED,
) -> tuple[list[ClusterRecord], list[ClusterRecord], dict]:
    """Run exact-only and exact+fuzzy clustering over `units`, returning `(exact_clusters, fuzzy_clusters, stats)`."""
    n = len(units)
    t0 = time.monotonic()

    hash_to_units: dict[str, list[int]] = defaultdict(list)
    hash_to_pages: dict[str, set[int]] = defaultdict(set)
    hash_to_sample: dict[str, str] = {}

    for idx, u in enumerate(units):
        for s in set(u.sentences):
            h = sentence_hash(s)
            hash_to_units[h].append(idx)
            hash_to_pages[h].add(u.page_id)
            if h not in hash_to_sample:
                hash_to_sample[h] = aot.mask_comment(s)[:200]

    boilerplate_hashes: set[str] = {
        h for h, pages in hash_to_pages.items() if len(pages) > boilerplate_max_pages
    }

    uf_exact = UnionFind(n)
    exact_pair_links = 0
    exact_unions = 0
    for h, idxs in hash_to_units.items():
        if h in boilerplate_hashes or len(idxs) < 2:
            continue
        first = idxs[0]
        exact_pair_links += len(idxs) * (len(idxs) - 1) // 2
        for other in idxs[1:]:
            if uf_exact.union(first, other):
                exact_unions += 1

    exact_seconds = time.monotonic() - t0

    # Copy exact UF state as the base of exact+fuzzy clustering
    t_fuzzy0 = time.monotonic()
    uf_fuzzy = UnionFind(n)
    uf_fuzzy.parent = list(uf_exact.parent)

    # Strip boilerplate sentences before shingling so >8-page boilerplate never links via Jaccard either
    shingle_sets: list[set[str]] = []
    for u in units:
        if boilerplate_hashes:
            kept_sents = [s for s in u.sentences if sentence_hash(s) not in boilerplate_hashes]
            text_for_fuzzy = " ".join(kept_sents) if kept_sents else ""
        else:
            text_for_fuzzy = u.norm_text
        shingle_sets.append(word_shingles(text_for_fuzzy, SHINGLE_WORDS))

    lsh = MinHashLSH(num_perm=MINHASH_PERMS, bands=LSH_BANDS, seed=seed)
    verified_links, candidate_pairs = lsh.find_fuzzy_links(shingle_sets, threshold=jaccard_threshold)

    fuzzy_extra_unions = 0
    for u_idx, v_idx, _sim in verified_links:
        if uf_fuzzy.union(u_idx, v_idx):
            fuzzy_extra_unions += 1

    fuzzy_seconds = time.monotonic() - t_fuzzy0

    exact_clusters = build_clusters_from_uf(units, uf_exact, method="exact", start_cluster_id=1)
    fuzzy_clusters = build_clusters_from_uf(
        units,
        uf_fuzzy,
        method="fuzzy",
        start_cluster_id=len(exact_clusters) + 1,
    )

    # Top sentence hashes by distinct page count (boilerplate >8 pages + runners-up for inspection)
    ranked_hashes = sorted(
        hash_to_pages.keys(),
        key=lambda h: (-len(hash_to_pages[h]), -len(hash_to_units[h]), h),
    )
    boilerplate_list = [
        {
            "hash": h,
            "n_pages": len(hash_to_pages[h]),
            "n_units": len(hash_to_units[h]),
            "blocked": h in boilerplate_hashes,
            "sample": hash_to_sample[h],
        }
        for h in ranked_hashes[:25]
        if len(hash_to_pages[h]) >= 2
    ]

    stats = {
        "n_units": n,
        "unique_sentence_hashes": len(hash_to_units),
        "boilerplate_hashes_blocked": len(boilerplate_hashes),
        "boilerplate_max_pages": boilerplate_max_pages,
        "exact_seconds": round(exact_seconds, 4),
        "exact_pair_links": exact_pair_links,
        "exact_unions": exact_unions,
        "exact_clusters_total": len(exact_clusters),
        "exact_clusters_multi": sum(1 for c in exact_clusters if c.n_units >= 2),
        "fuzzy_shingled_units": sum(1 for s in shingle_sets if s),
        "fuzzy_unique_shingles": len(lsh._shingle_hash_cache),
        "fuzzy_seconds": round(fuzzy_seconds, 4),
        "fuzzy_lsh_candidate_pairs": candidate_pairs,
        "fuzzy_verified_pairs": len(verified_links),
        "fuzzy_extra_unions": fuzzy_extra_unions,
        "fuzzy_clusters_total": len(fuzzy_clusters),
        "fuzzy_clusters_multi": sum(1 for c in fuzzy_clusters if c.n_units >= 2),
        "top_sentence_hashes": boilerplate_list,
    }
    return exact_clusters, fuzzy_clusters, stats


# ── Corpus loading & DB persistence ──────────────────────────────────────────

def golden_page_ids(golden_path: pathlib.Path) -> list[int]:
    """All golden article page_ids (never reading test-split items) unioned with distinct pages in `s3_diff`."""
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    pages: set[int] = set()
    for t in doc["topics"]:
        for a in t["articles"]:
            pages.add(int(a["page_id"]))
        if t["split"] == "dev":
            for it in t["items"]:
                pages.add(int(it["page_id"]))
                for op in it.get("other_page_ids") or []:
                    pages.add(int(op))
    for r in aot.sql("SELECT DISTINCT page_id FROM research.s3_diff"):
        if r["page_id"] is not None:
            pages.add(int(r["page_id"]))
    return sorted(pages)


def _decode_compressed_segments(seg_b64: str) -> str:
    raw = base64.b64decode(seg_b64)
    if len(raw) <= 4:
        return "[]"
    return zlib.decompress(raw[4:]).decode("utf-8", "replace")


def load_units_from_corpus(page_ids: list[int]) -> tuple[list[Unit], dict]:
    """Fetch S1 revert spans + S3 diffs + reverted edits across `page_ids` in polite small batches."""
    units: list[Unit] = []
    counts = {
        "pages_requested": len(page_ids),
        "candidate_spans": 0,
        "missing_s3_diff": 0,
        "error_s3_diff": 0,
        "short_prose_skipped": 0,
        "no_reverted_edits_skipped": 0,
        "kept_units": 0,
    }

    excl_csv = ",".join(aot.quote(c) for c in EXCLUDED_REVERT_CLASSES)
    t0 = time.monotonic()

    for start in range(0, len(page_ids), PAGE_BATCH_SIZE):
        batch_pages = page_ids[start : start + PAGE_BATCH_SIZE]
        pages_csv = aot.ints(batch_pages)
        rows = aot.sql(f"""
            SELECT r.page_id,
                   r.reverting_rev_id,
                   r.first_reverted_ts,
                   r.revert_ts,
                   d.rev_id AS diff_rev_id,
                   d.added_prose_chars,
                   d.added_refs,
                   d.error,
                   CASE
                     WHEN d.rev_id IS NOT NULL
                          AND d.error IS NULL
                          AND (d.added_prose_chars IS NULL OR d.added_prose_chars >= {MIN_PROSE_CHARS} OR COALESCE(d.added_refs, 0) > 0)
                     THEN TO_BASE64(COMPRESS(d.segments))
                     ELSE NULL
                   END AS seg_b64,
                   CASE
                     WHEN d.rev_id IS NOT NULL AND d.error IS NULL AND d.segments IS NULL
                     THEN d.added_prose
                     ELSE NULL
                   END AS fallback_prose
            FROM research.s1_revert r
            LEFT JOIN research.s3_diff d
              ON d.kind = 'span' AND d.rev_id = r.reverting_rev_id
            WHERE r.page_id IN ({pages_csv})
              AND r.class NOT IN ({excl_csv})
            ORDER BY r.page_id ASC, r.reverting_rev_id ASC
        """)

        pending: list[tuple[dict, list[str], str, list[str]]] = []
        for r in rows:
            counts["candidate_spans"] += 1
            if r["diff_rev_id"] is None:
                counts["missing_s3_diff"] += 1
                continue
            if r["error"]:
                counts["error_s3_diff"] += 1
                continue
            if r["seg_b64"] is None and r["fallback_prose"] is None:
                counts["short_prose_skipped"] += 1
                continue

            segs_str = _decode_compressed_segments(r["seg_b64"]) if r["seg_b64"] else None
            parts = extract_unit_parts(segs_str, r["fallback_prose"])
            norm_full = normalise_text(NL.join(parts))
            if len(norm_full) < MIN_PROSE_CHARS:
                counts["short_prose_skipped"] += 1
                continue

            sents = extract_sentences(parts, MIN_PROSE_CHARS)
            pending.append((r, parts, norm_full, sents))

        if not pending:
            continue

        rev_ids_csv = aot.ints(int(item[0]["reverting_rev_id"]) for item in pending)
        edit_rows = aot.sql(f"""
            SELECT reverting_rev_id, rev_id, user, ts, bytes_delta
            FROM research.s1_reverted_edit
            WHERE reverting_rev_id IN ({rev_ids_csv})
            ORDER BY reverting_rev_id ASC, ts ASC, rev_id ASC
        """)
        edits_by_span: dict[int, list[dict]] = defaultdict(list)
        for e in edit_rows:
            edits_by_span[int(e["reverting_rev_id"])].append(e)

        for r, parts, norm_full, sents in pending:
            r_rev_id = int(r["reverting_rev_id"])
            span_edits = edits_by_span.get(r_rev_id)
            if not span_edits:
                counts["no_reverted_edits_skipped"] += 1
                continue

            ins_revs = sorted({int(e["rev_id"]) for e in span_edits})
            inserted_at = span_edits[0]["ts"]
            removed_at = r["revert_ts"]

            # Primary editor of the span: largest positive bytes added (tie-break earliest edit)
            pos_bytes_by_key: dict[str, int] = defaultdict(int)
            first_order: dict[str, int] = {}
            for order_idx, e in enumerate(span_edits):
                k = make_account_key(e["user"])
                first_order.setdefault(k, order_idx)
                pos_bytes_by_key[k] += max(0, int(e["bytes_delta"] or 0))
            best_acct = min(
                first_order.keys(),
                key=lambda k: (-pos_bytes_by_key[k], first_order[k]),
            )

            units.append(
                Unit(
                    page_id=int(r["page_id"]),
                    reverting_rev_id=r_rev_id,
                    inserting_rev_ids=ins_revs,
                    inserted_at=inserted_at,
                    removed_at=removed_at,
                    account_key=best_acct,
                    prose_chars=len(norm_full),
                    sample_text=make_sample_text(parts, 300),
                    sentences=sents,
                    norm_text=norm_full,
                )
            )
            counts["kept_units"] += 1

    counts["load_seconds"] = round(time.monotonic() - t0, 3)
    return units, counts


DDL_DROP_CREATE = """
DROP TABLE IF EXISTS research.s7_member;
DROP TABLE IF EXISTS research.s7_cluster;

CREATE TABLE research.s7_cluster (
  cluster_id INT UNSIGNED NOT NULL,
  method ENUM('exact','fuzzy') NOT NULL,
  n_units INT UNSIGNED NOT NULL,
  n_pages INT UNSIGNED NOT NULL,
  n_accounts INT UNSIGNED NOT NULL,
  n_reinsertions INT UNSIGNED NOT NULL,
  first_ts DATETIME NOT NULL,
  last_ts DATETIME NOT NULL,
  span_days DOUBLE NOT NULL,
  max_gap_days DOUBLE NOT NULL,
  sample_text VARCHAR(300) NOT NULL,
  campaign_signals VARCHAR(64) NOT NULL,
  prose_chars INT UNSIGNED NOT NULL,
  PRIMARY KEY (cluster_id),
  KEY idx_method (method)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE research.s7_member (
  cluster_id INT UNSIGNED NOT NULL,
  page_id INT UNSIGNED NOT NULL,
  reverting_rev_id INT UNSIGNED NOT NULL,
  inserting_rev_ids MEDIUMTEXT NOT NULL,
  inserted_at DATETIME NOT NULL,
  removed_at DATETIME NOT NULL,
  account_key VARCHAR(16) NOT NULL,
  PRIMARY KEY (cluster_id, reverting_rev_id),
  KEY idx_page_cluster (page_id, cluster_id),
  KEY idx_rev (reverting_rev_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
"""


def write_tables(clusters: list[ClusterRecord]) -> float:
    """Drop + recreate `research.s7_cluster` and `research.s7_member` and bulk-insert all records."""
    t0 = time.monotonic()
    aot.sql(DDL_DROP_CREATE)

    sep = "," + NL
    for i in range(0, len(clusters), INSERT_BATCH_SIZE):
        chunk = clusters[i : i + INSERT_BATCH_SIZE]
        vals = sep.join(
            f"({c.cluster_id}, {aot.quote(c.method)}, {c.n_units}, {c.n_pages}, {c.n_accounts}, "
            f"{c.n_reinsertions}, {aot.quote(c.first_ts)}, {aot.quote(c.last_ts)}, "
            f"{c.span_days:.4f}, {c.max_gap_days:.4f}, {aot.quote(c.sample_text)}, "
            f"{aot.quote(c.campaign_signals)}, {c.prose_chars})"
            for c in chunk
        )
        aot.sql(
            "INSERT INTO research.s7_cluster "
            "(cluster_id, method, n_units, n_pages, n_accounts, n_reinsertions, first_ts, last_ts, "
            "span_days, max_gap_days, sample_text, campaign_signals, prose_chars) VALUES" + NL + vals
        )

    member_rows: list[tuple[int, Unit]] = [
        (c.cluster_id, m) for c in clusters for m in c.members
    ]
    for i in range(0, len(member_rows), INSERT_BATCH_SIZE):
        chunk = member_rows[i : i + INSERT_BATCH_SIZE]
        vals = sep.join(
            f"({cid}, {m.page_id}, {m.reverting_rev_id}, "
            f"{aot.quote(json.dumps(m.inserting_rev_ids, separators=(',', ':')))}, "
            f"{aot.quote(m.inserted_at)}, {aot.quote(m.removed_at)}, {aot.quote(m.account_key)})"
            for cid, m in chunk
        )
        aot.sql(
            "INSERT INTO research.s7_member "
            "(cluster_id, page_id, reverting_rev_id, inserting_rev_ids, inserted_at, removed_at, account_key) "
            "VALUES" + NL + vals
        )

    return round(time.monotonic() - t0, 3)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--golden", default=str(ROOT / "research/eval/golden.json"))
    ap.add_argument("--pages", help="Optional comma-separated page_ids override")
    args = ap.parse_args()

    t_start = time.monotonic()
    s3_pages = int(aot.sql("SELECT COUNT(DISTINCT page_id) AS n FROM research.s3_diff")[0]["n"])
    pages = (
        sorted({int(x) for x in args.pages.split(",") if x.strip()})
        if args.pages
        else golden_page_ids(pathlib.Path(args.golden))
    )
    aot.log(f"[s7] clustering {len(pages)} golden page(s) (s3_diff currently has {s3_pages} distinct pages)...")

    units, load_counts = load_units_from_corpus(pages)
    aot.log(
        f"[s7] loaded {load_counts['kept_units']} units (>= {MIN_PROSE_CHARS} prose chars) "
        f"from {load_counts['candidate_spans']} candidate spans "
        f"(missing_s3_diff={load_counts['missing_s3_diff']}, short={load_counts['short_prose_skipped']}) "
        f"in {load_counts['load_seconds']}s"
    )

    exact_clusters, fuzzy_clusters, cluster_stats = cluster_units(units)
    all_clusters = exact_clusters + fuzzy_clusters
    write_s = write_tables(all_clusters)
    total_s = round(time.monotonic() - t_start, 3)

    summary = {
        "s3_diff_distinct_pages": s3_pages,
        "load": load_counts,
        "clustering": cluster_stats,
        "write_seconds": write_s,
        "total_seconds": total_s,
    }
    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "build_stats.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + NL,
        encoding="utf-8",
    )

    print(
        f"s7_cluster built in {total_s:.2f}s (s3_diff pages={s3_pages}, missing_s3_diff={load_counts['missing_s3_diff']}): "
        f"units={len(units)}, "
        f"exact={len(exact_clusters)} clusters ({cluster_stats['exact_clusters_multi']} multi-unit), "
        f"fuzzy={len(fuzzy_clusters)} clusters ({cluster_stats['fuzzy_clusters_multi']} multi-unit, "
        f"+{cluster_stats['fuzzy_extra_unions']} fuzzy merges)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
