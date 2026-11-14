#!/usr/bin/env python3
"""P1.S3 (#30) Approach B: content-lifetime tracking via server-side LOCATE binary search.

Tracks substantive prose additions (`s3_diff` kind='edit', `added_prose_chars >= 40`) forward
through a page's non-null revision history using up to 3 verbatim sentence probes (>= 25 chars)
and server-side `LOCATE(<probe>, text)` queries against `corpus.revision_text` (capped at 40
queries per candidate). Populates `research.s3_lifetime` and writes `results/s3b-recovery.{md,json}`.

Usage:
    python3 research/30-s3-extract/lifetime.py --golden-dev --sample 300
    python3 research/30-s3-extract/lifetime.py --golden-dev
    python3 research/30-s3-extract/lifetime.py --sample 300
    python3 research/30-s3-extract/lifetime.py --revs 12345,67890
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime
import json
import os
from pathlib import Path
import random
import re
import statistics
import subprocess
import sys
import time
from typing import Callable, Sequence

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

NL = chr(10)
CR = chr(13)
NUL = chr(0)
BS = chr(92)

MIN_ADDED_PROSE_CHARS = 40
MIN_PROBE_CHARS = 25
MAX_PROBES = 3
MAX_QUERIES_PER_CANDIDATE = 40
DEFAULT_SAMPLE_SIZE = 100
DEFAULT_SEED = "s3-lifetime-30"

SENTENCE_SPLIT_RE = re.compile(f"[.!?{CR}{NL}]+| … ")

TABLE_COLUMNS = (
    "rev_id",
    "page_id",
    "probe_count",
    "removing_rev_id",
    "removed_at",
    "inserted_at",
    "exposure_seconds",
    "removal_kind",
    "removing_user_display",
    "removal_summary",
    "reappeared",
    "reappeared_rev_id",
    "still_present_latest",
    "queries",
    "seconds",
)

DDL_CREATE = """
CREATE TABLE IF NOT EXISTS research.s3_lifetime (
  rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
  page_id INT UNSIGNED NOT NULL,
  probe_count TINYINT UNSIGNED NOT NULL,
  removing_rev_id INT UNSIGNED NULL,
  removed_at DATETIME NULL,
  inserted_at DATETIME NOT NULL,
  exposure_seconds BIGINT NULL,
  removal_kind VARCHAR(20) CHARACTER SET ascii NULL,
  removing_user_display VARCHAR(255) NULL,
  removal_summary VARCHAR(1000) NULL,
  reappeared TINYINT NOT NULL,
  reappeared_rev_id INT UNSIGNED NULL,
  still_present_latest TINYINT NOT NULL,
  queries INT UNSIGNED NOT NULL,
  seconds DOUBLE NOT NULL,
  KEY idx_page_id (page_id),
  KEY idx_removing_rev_id (removing_rev_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci
"""


# ── Pure offline-testable logic ──────────────────────────────────────────────


def select_probes(
    segments: Sequence[dict] | None,
    *,
    min_chars: int = MIN_PROBE_CHARS,
    max_probes: int = MAX_PROBES,
) -> list[str]:
    """Extract up to `max_probes` longest verbatim sentence probes (>= `min_chars`) from added prose segments.

    Splits each added non-moved `prose` segment's `prose` on `. ! ?`, newlines, and ` … `,
    retaining sentences of `>= min_chars` that also occur verbatim in the segment's raw `text`
    (so every returned probe is guaranteed to be an exact substring of the stored wikitext).
    """
    if not segments or max_probes <= 0:
        return []
    seen: set[str] = set()
    candidates: list[tuple[int, str]] = []
    for seg in segments:
        if seg.get("op") != "added" or seg.get("kind") != "prose" or seg.get("moved"):
            continue
        raw_text = seg.get("text") or ""
        prose = seg.get("prose") or ""
        if not raw_text or not prose:
            continue
        for part in SENTENCE_SPLIT_RE.split(prose):
            sent = part.strip()
            if len(sent) < min_chars:
                continue
            pos = raw_text.find(sent)
            if pos == -1:
                continue
            probe = raw_text[pos : pos + len(sent)]
            if probe not in seen:
                seen.add(probe)
                candidates.append((len(candidates), probe))
    candidates.sort(key=lambda item: (-len(item[1]), item[0]))
    return [probe for _, probe in candidates[:max_probes]]


def drop_null_revisions(revs: Sequence[dict]) -> list[dict]:
    """Order revisions by `(ts, rev_id)` and drop null revisions (`sha1` equal to immediate predecessor's)."""
    ordered = sorted(revs, key=lambda r: (str(r["ts"]), int(r["rev_id"])))
    out: list[dict] = []
    prev_sha1: str | None = None
    for rev in ordered:
        sha1 = rev.get("sha1")
        if sha1 is not None and prev_sha1 is not None and sha1 == prev_sha1:
            prev_sha1 = sha1
            continue
        out.append(rev)
        prev_sha1 = sha1
    return out


def revisions_after(
    non_null_revs: Sequence[dict],
    candidate_rev_id: int,
    candidate_ts: str | None = None,
) -> list[dict]:
    """Return the non-null revisions strictly after `candidate_rev_id`."""
    for idx, rev in enumerate(non_null_revs):
        if int(rev["rev_id"]) == int(candidate_rev_id):
            return list(non_null_revs[idx + 1 :])
    if candidate_ts is not None:
        key = (str(candidate_ts), int(candidate_rev_id))
        return [r for r in non_null_revs if (str(r["ts"]), int(r["rev_id"])) > key]
    return []


def probes_present(locate_positions: Sequence[int]) -> bool:
    """Return True when at least half of the probes are found (`LOCATE > 0`)."""
    if not locate_positions:
        raise ValueError("locate_positions must not be empty")
    found = sum(1 for pos in locate_positions if int(pos) > 0)
    return found * 2 >= len(locate_positions)


@dataclass(frozen=True)
class TrackingResult:
    removing_rev_id: int | None
    reappeared: bool
    reappeared_rev_id: int | None
    still_present_latest: bool
    queries: int
    boundary_verified: bool


def track_revisions(
    after_rev_ids: Sequence[int],
    is_present: Callable[[int], bool],
    *,
    max_queries: int = MAX_QUERIES_PER_CANDIDATE,
) -> TrackingResult:
    """Binary-search first absent revision, verify boundary, check latest, and binary-search reappearance."""
    n = len(after_rev_ids)
    if n == 0:
        return TrackingResult(
            removing_rev_id=None,
            reappeared=False,
            reappeared_rev_id=None,
            still_present_latest=True,
            queries=0,
            boundary_verified=True,
        )

    cache: dict[int, bool] = {}
    queries = 0

    def check(idx: int) -> bool | None:
        nonlocal queries
        if idx in cache:
            return cache[idx]
        if queries >= max_queries:
            return None
        queries += 1
        val = bool(is_present(int(after_rev_ids[idx])))
        cache[idx] = val
        return val

    # 1. Binary search for first absent revision assuming monotone presence (True ... True, False ... False).
    lo, hi = 0, n
    while lo < hi:
        mid = (lo + hi) // 2
        present = check(mid)
        if present is None:
            break
        if present:
            lo = mid + 1
        else:
            hi = mid

    if hi == n:
        # Never observed absent; if binary search finished normally, n - 1 is cached as True.
        latest_present = cache.get(n - 1)
        if latest_present is None:
            checked_latest = check(n - 1)
            latest_present = True if checked_latest is None else checked_latest
        return TrackingResult(
            removing_rev_id=None,
            reappeared=False,
            reappeared_rev_id=None,
            still_present_latest=bool(latest_present),
            queries=queries,
            boundary_verified=True,
        )

    cand_idx = hi

    # 2. Verify boundary: present just before `cand_idx`, absent at `cand_idx`.
    # If query cap halted binary search early, step back if `cand_idx - 1` happens to be absent too.
    while cand_idx > 0:
        prev_val = check(cand_idx - 1)
        if prev_val is False:
            cand_idx -= 1
            continue
        break
    before_ok = True if cand_idx == 0 else (cache.get(cand_idx - 1) is True)
    at_ok = check(cand_idx) is False
    boundary_verified = before_ok and at_ok

    # 3. Check latest revision: if present again, mark `reappeared=1` and binary-search first reappearance.
    latest_val = check(n - 1)
    still_present_latest = bool(latest_val) if latest_val is not None else False
    if not still_present_latest:
        return TrackingResult(
            removing_rev_id=int(after_rev_ids[cand_idx]),
            reappeared=False,
            reappeared_rev_id=None,
            still_present_latest=False,
            queries=queries,
            boundary_verified=boundary_verified,
        )

    r_lo, r_hi = cand_idx + 1, n - 1
    while r_lo < r_hi:
        mid = (r_lo + r_hi) // 2
        present = check(mid)
        if present is None:
            break
        if present:
            r_hi = mid
        else:
            r_lo = mid + 1

    return TrackingResult(
        removing_rev_id=int(after_rev_ids[cand_idx]),
        reappeared=True,
        reappeared_rev_id=int(after_rev_ids[r_hi]),
        still_present_latest=True,
        queries=queries,
        boundary_verified=boundary_verified,
    )


# ── SQL Helpers & Persistent Single-Connection Session ───────────────────────


def sql_literal(val: str | int | float | None) -> str:
    """Single-line SQL literal safe for persistent mariadb batch mode."""
    if val is None:
        return "NULL"
    if isinstance(val, bool):
        return "1" if val else "0"
    if isinstance(val, int):
        return str(val)
    if isinstance(val, float):
        return repr(val)
    escaped = (
        str(val)
        .replace(BS, BS + BS)
        .replace("'", "''")
        .replace(NUL, "")
        .replace(NL, BS + "n")
        .replace(CR, BS + "r")
    )
    return f"'{escaped}'"


def build_locate_scalar_sql(page_id: int, rev_id: int, probes: Sequence[str]) -> str:
    """Single-row scalar SQL returning comma-separated 1-based LOCATE positions on the server."""
    loc_exprs = ", ".join(f"LOCATE({sql_literal(p)}, text)" for p in probes)
    return (
        f"SELECT COALESCE((SELECT CONCAT_WS(',', {loc_exprs}) "
        f"FROM corpus.revision_text WHERE page_id={int(page_id)} AND rev_id={int(rev_id)}), 'MISSING');"
    )


class PersistentMariaDb:
    """Single sequential `mariadb` CLI session avoiding per-query TCP/TLS handshake overhead."""

    def __init__(self) -> None:
        self.proc: subprocess.Popen[str] | None = None

    def _open(self) -> subprocess.Popen[str]:
        if self.proc is not None and self.proc.poll() is None:
            return self.proc
        self.close()
        env = dict(os.environ)
        env["MYSQL_PWD"] = os.environ.get("CORPUS_PW") or os.environ.get("MYSQL_PWD", "")
        cmd = [
            "mariadb",
            "--batch",
            "--skip-column-names",
            "--unbuffered",
            "--default-character-set=utf8mb4",
            "-A",
            "--ssl=0",
            "-h",
            os.environ.get("CORPUS_HOST", "127.0.0.1"),
            "-P",
            os.environ.get("CORPUS_PORT", "3307"),
            "-u",
            os.environ.get("CORPUS_USER", "corpus_ro"),
            os.environ.get("CORPUS_DB", "corpus"),
        ]
        self.proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            env=env,
            bufsize=1,
        )
        return self.proc

    def scalar_line(self, single_row_sql: str) -> str:
        """Execute a single-statement query guaranteed to return exactly 1 row (1 line)."""
        stmt = single_row_sql.strip().rstrip(";") + ";" + NL
        t0 = time.monotonic()
        last_err = ""
        for attempt in range(5):
            proc = self._open()
            try:
                assert proc.stdin is not None and proc.stdout is not None
                proc.stdin.write(stmt)
                proc.stdin.flush()
                line = proc.stdout.readline()
                if line != "":
                    aot.STATS["sql_queries"] += 1
                    aot.STATS["sql_seconds"] += time.monotonic() - t0
                    return line.rstrip(CR + NL)
                err = proc.stderr.read() if proc.stderr is not None else ""
                last_err = err.strip()[:300]
            except Exception as exc:
                last_err = str(exc)
            aot.log(f"  persistent mariadb dropped, reconnecting (attempt {attempt + 1}): {last_err}")
            self.close()
            time.sleep(1 + 2 * attempt)
        raise RuntimeError(f"persistent mariadb query failed after retries: {last_err}")

    def exec_write(self, write_sql: str) -> None:
        """Execute a DML/DDL statement followed by a 1-row ack query over the persistent session."""
        stmt = write_sql.strip().rstrip(";") + "; SELECT 'OK';" + NL
        res = self.scalar_line(stmt)
        if res != "OK":
            raise RuntimeError(f"unexpected write ack from mariadb: {res!r}")

    def locate_probes(self, page_id: int, rev_id: int, probes: Sequence[str]) -> list[int]:
        raw = self.scalar_line(build_locate_scalar_sql(page_id, rev_id, probes))
        if not raw or raw == "MISSING":
            return [0] * len(probes)
        parts = raw.split(",")
        if len(parts) != len(probes):
            raise RuntimeError(f"unexpected LOCATE response {raw!r} for {len(probes)} probes")
        return [int(x) for x in parts]

    def close(self) -> None:
        if self.proc is None:
            return
        try:
            if self.proc.stdin:
                self.proc.stdin.close()
        except Exception:
            pass
        try:
            self.proc.wait(timeout=3)
        except Exception:
            try:
                self.proc.kill()
            except Exception:
                pass
        self.proc = None


# ── Golden Dev & Corpus Metadata Loaders ─────────────────────────────────────


def load_golden_dev() -> tuple[list[dict], set[int], set[int]]:
    """Load dev-split topics ONLY (never reads test-split items)."""
    raw = json.loads((ROOT / "research/eval/golden.json").read_text(encoding="utf-8"))
    dev_topics = [t for t in raw["topics"] if t.get("split") == "dev"]
    dev_pages: set[int] = set()
    golden_ins_revs: set[int] = set()
    for topic in dev_topics:
        for art in topic.get("articles", []):
            dev_pages.add(int(art["page_id"]))
        for item in topic.get("items", []):
            dev_pages.add(int(item["page_id"]))
            for op in item.get("other_page_ids", []):
                dev_pages.add(int(op))
            for r in item.get("inserting_rev_ids", []):
                golden_ins_revs.add(int(r))
    return dev_topics, dev_pages, golden_ins_revs


def parse_dt(ts_str: str) -> datetime:
    return datetime.strptime(ts_str, "%Y-%m-%d %H:%M:%S")


def ensure_table(session: PersistentMariaDb, *, drop: bool) -> None:
    if drop:
        session.exec_write("DROP TABLE IF EXISTS research.s3_lifetime")
        session.exec_write(DDL_CREATE)
        return
    existing = aot.sql(
        "SELECT COLUMN_NAME FROM information_schema.COLUMNS "
        "WHERE TABLE_SCHEMA='research' AND TABLE_NAME='s3_lifetime' ORDER BY ORDINAL_POSITION"
    )
    cols = tuple(r["COLUMN_NAME"] for r in existing)
    if cols and cols != TABLE_COLUMNS:
        aot.log(f"  s3_lifetime schema mismatch ({cols}), recreating")
        session.exec_write("DROP TABLE IF EXISTS research.s3_lifetime")
    session.exec_write(DDL_CREATE)


def flush_lifetime_rows(session: PersistentMariaDb, batch: list[dict]) -> None:
    if not batch:
        return
    values_sql = []
    for row in batch:
        vals = ", ".join(sql_literal(row[col]) for col in TABLE_COLUMNS)
        values_sql.append(f"({vals})")
    col_list = ", ".join(TABLE_COLUMNS)
    session.exec_write(f"REPLACE INTO research.s3_lifetime ({col_list}) VALUES " + ", ".join(values_sql))
    batch.clear()


def fetch_s3_diff_by_revs(rev_ids: Sequence[int], chunk_size: int = 200) -> dict[int, dict]:
    out: dict[int, dict] = {}
    ordered = sorted({int(x) for x in rev_ids})
    for i in range(0, len(ordered), chunk_size):
        chunk = ordered[i : i + chunk_size]
        rows = aot.sql(
            "SELECT rev_id, page_id, added_prose_chars, segments, error "
            f"FROM research.s3_diff WHERE kind='edit' AND rev_id IN ({aot.ints(chunk)})"
        )
        for r in rows:
            out[int(r["rev_id"])] = r
    return out


def preload_page_revisions(
    page_ids: Sequence[int],
    page_cache: dict[int, tuple[list[dict], dict[int, dict]]],
    chunk_size: int = 40,
) -> None:
    missing = sorted({int(p) for p in page_ids if int(p) not in page_cache})
    for i in range(0, len(missing), chunk_size):
        chunk = missing[i : i + chunk_size]
        rows = aot.sql(
            "SELECT page_id, rev_id, ts, user, comment, sha1, bytes "
            f"FROM corpus.revision WHERE page_id IN ({aot.ints(chunk)}) ORDER BY page_id, ts, rev_id"
        )
        grouped: dict[int, list[dict]] = {p: [] for p in chunk}
        for r in rows:
            grouped[int(r["page_id"])].append(r)
        for p, raw_revs in grouped.items():
            non_null = drop_null_revisions(raw_revs)
            by_rev = {int(r["rev_id"]): r for r in raw_revs}
            page_cache[p] = (non_null, by_rev)


# ── Main Tracking Runner ─────────────────────────────────────────────────────


def track_candidate_set(
    session: PersistentMariaDb,
    diff_rows: Sequence[dict],
    s1_reverting_revs: set[int],
    page_cache: dict[int, tuple[list[dict], dict[int, dict]]],
    *,
    label: str,
    max_seconds: float | None = None,
) -> dict:
    """Track a list of `s3_diff` candidate rows, caching page revision metadata per page."""
    # Group by page_id so each page's revision list is fetched once and kept warm in InnoDB cache.
    ordered_rows = sorted(diff_rows, key=lambda r: (int(r["page_id"]), int(r["rev_id"])))
    skipped_no_probe = 0
    tracked_count = 0
    total_queries = 0
    total_seconds = 0.0
    pending_writes: list[dict] = []
    run_start = time.monotonic()
    stopped_early = False

    # Prepare probes up front to report accurate candidate / skip counts immediately.
    runnable: list[tuple[dict, list[str]]] = []
    for row in ordered_rows:
        segs = json.loads(row["segments"]) if row.get("segments") else []
        probes = select_probes(segs)
        if not probes:
            skipped_no_probe += 1
            continue
        runnable.append((row, probes))

    aot.log(
        f"[{label}] {len(ordered_rows)} candidates -> {len(runnable)} with usable probes, "
        f"{skipped_no_probe} skipped (no usable probe)"
    )

    preload_page_revisions([int(row["page_id"]) for row, _ in runnable], page_cache)

    for idx, (row, probes) in enumerate(runnable, 1):
        if max_seconds is not None and (time.monotonic() - run_start) >= max_seconds:
            aot.log(
                f"  [{label}] reached max_seconds={max_seconds:.0f}s at {idx - 1}/{len(runnable)} runnable candidates; stopping cleanly"
            )
            stopped_early = True
            break

        page_id = int(row["page_id"])
        rev_id = int(row["rev_id"])

        non_null_revs, rev_meta = page_cache[page_id]
        ins_meta = rev_meta.get(rev_id)
        if ins_meta is None:
            aot.log(f"  warning: rev {rev_id} missing from corpus.revision on page {page_id}; skipped")
            continue

        after_revs = revisions_after(non_null_revs, rev_id, ins_meta["ts"])
        after_ids = [int(r["rev_id"]) for r in after_revs]

        t0 = time.monotonic()
        res = track_revisions(
            after_ids,
            lambda target_rev_id: probes_present(session.locate_probes(page_id, target_rev_id, probes)),
            max_queries=MAX_QUERIES_PER_CANDIDATE,
        )
        elapsed = time.monotonic() - t0

        inserted_at = ins_meta["ts"]
        if res.removing_rev_id is not None:
            rem_meta = rev_meta[res.removing_rev_id]
            removed_at = rem_meta["ts"]
            exposure_seconds = int((parse_dt(removed_at) - parse_dt(inserted_at)).total_seconds())
            removal_kind = "identity_revert" if res.removing_rev_id in s1_reverting_revs else "partial"
            removing_user_display = aot.user_display(rem_meta.get("user"))
            removal_summary = aot.mask_comment(rem_meta.get("comment"))
        else:
            removed_at = None
            exposure_seconds = None
            removal_kind = None
            removing_user_display = None
            removal_summary = None

        pending_writes.append(
            {
                "rev_id": rev_id,
                "page_id": page_id,
                "probe_count": len(probes),
                "removing_rev_id": res.removing_rev_id,
                "removed_at": removed_at,
                "inserted_at": inserted_at,
                "exposure_seconds": exposure_seconds,
                "removal_kind": removal_kind,
                "removing_user_display": removing_user_display,
                "removal_summary": removal_summary,
                "reappeared": 1 if res.reappeared else 0,
                "reappeared_rev_id": res.reappeared_rev_id,
                "still_present_latest": 1 if res.still_present_latest else 0,
                "queries": res.queries,
                "seconds": round(elapsed, 4),
            }
        )
        tracked_count += 1
        total_queries += res.queries
        total_seconds += elapsed

        if len(pending_writes) >= 10:
            flush_lifetime_rows(session, pending_writes)
        if idx % 10 == 0 or idx == len(runnable):
            avg_q = total_queries / tracked_count if tracked_count else 0.0
            avg_s = total_seconds / tracked_count if tracked_count else 0.0
            aot.log(
                f"  [{label} {idx}/{len(runnable)}] page={page_id} rev={rev_id} "
                f"avg_queries={avg_q:.1f} avg_sec={avg_s:.2f}s total_sec={total_seconds:.1f}s"
            )

    flush_lifetime_rows(session, pending_writes)
    return {
        "candidates_ge40": len(ordered_rows),
        "skipped_no_probe": skipped_no_probe,
        "tracked": tracked_count,
        "stopped_early": stopped_early,
        "queries": total_queries,
        "seconds": round(total_seconds, 3),
    }


# ── Report Generation (`results/s3b-recovery.{md,json}`) ─────────────────────


def _pct(num: int, den: int) -> float:
    return round((100.0 * num / den), 2) if den else 0.0


def _fmt_dur(seconds: float | int | None) -> str:
    if seconds is None:
        return "—"
    s = float(seconds)
    if abs(s) < 90:
        return f"{s:.0f} s"
    if abs(s) < 5400:
        return f"{s / 60:.1f} min"
    if abs(s) < 172800:
        return f"{s / 3600:.1f} h"
    return f"{s / 86400:.1f} d"


def _quantiles_summary(vals: Sequence[float]) -> dict:
    if not vals:
        return {"count": 0, "mean": None, "median": None, "p90": None, "max": None}
    sv = sorted(float(x) for x in vals)
    p90_idx = min(len(sv) - 1, int(round(0.90 * (len(sv) - 1))))
    return {
        "count": len(sv),
        "mean": round(statistics.fmean(sv), 3),
        "median": round(statistics.median(sv), 3),
        "p90": round(sv[p90_idx], 3),
        "max": round(sv[-1], 3),
    }


def generate_report(*, sample_size: int = DEFAULT_SAMPLE_SIZE, seed: str = DEFAULT_SEED) -> dict:
    """Compute recovery, exposure window, kept-item, sample partial-removal, and cost metrics."""
    dev_topics, dev_pages, golden_ins_revs = load_golden_dev()

    # S1 reverting revisions: Approach A uses `class <> 'excluded'`; also record all `s1_revert` rows for transparency.
    s1_rows = aot.sql("SELECT reverting_rev_id, class, exclude_reason FROM research.s1_revert")
    s1_non_excluded = {int(r["reverting_rev_id"]) for r in s1_rows if r["class"] != "excluded"}
    s1_all = {int(r["reverting_rev_id"]): (r["class"], r["exclude_reason"]) for r in s1_rows}

    # Fetch all lifetime rows currently in `research.s3_lifetime`.
    lt_rows_raw = aot.sql("SELECT * FROM research.s3_lifetime")
    lifetime_by_rev: dict[int, dict] = {}
    for r in lt_rows_raw:
        lifetime_by_rev[int(r["rev_id"])] = {
            "rev_id": int(r["rev_id"]),
            "page_id": int(r["page_id"]),
            "probe_count": int(r["probe_count"]),
            "removing_rev_id": int(r["removing_rev_id"]) if r["removing_rev_id"] is not None else None,
            "removed_at": r["removed_at"],
            "inserted_at": r["inserted_at"],
            "exposure_seconds": int(r["exposure_seconds"]) if r["exposure_seconds"] is not None else None,
            "removal_kind": r["removal_kind"],
            "removing_user_display": r["removing_user_display"],
            "removal_summary": r["removal_summary"],
            "reappeared": int(r["reappeared"]),
            "reappeared_rev_id": int(r["reappeared_rev_id"]) if r["reappeared_rev_id"] is not None else None,
            "still_present_latest": int(r["still_present_latest"]),
            "queries": int(r["queries"]),
            "seconds": float(r["seconds"]),
        }

    # Golden inserting & removing revisions metadata (timestamps from corpus.revision + s3_diff status).
    all_golden_revs: set[int] = set(golden_ins_revs)
    dev_items: list[dict] = []
    for topic in dev_topics:
        for item in topic.get("items", []):
            dev_items.append({**item, "topic_id": topic["id"]})
            for r in item.get("removing_rev_ids", []):
                all_golden_revs.add(int(r))

    rev_ts_map: dict[int, str] = {}
    ordered_grevs = sorted(all_golden_revs)
    for i in range(0, len(ordered_grevs), 400):
        chunk = ordered_grevs[i : i + 400]
        for r in aot.sql(f"SELECT rev_id, ts FROM corpus.revision WHERE rev_id IN ({aot.ints(chunk)})"):
            rev_ts_map[int(r["rev_id"])] = r["ts"]

    golden_diff_map = fetch_s3_diff_by_revs(sorted(golden_ins_revs))

    # Golden inserting rev accounting.
    golden_missing_s3_diff = 0
    golden_lt40 = 0
    golden_ge40 = 0
    golden_skipped_no_probe = 0
    golden_tracked = 0
    golden_probes_by_rev: dict[int, list[str]] = {}
    for rev_id in sorted(golden_ins_revs):
        d = golden_diff_map.get(rev_id)
        if d is None or d.get("error"):
            golden_missing_s3_diff += 1
            continue
        chars = int(d["added_prose_chars"] or 0)
        if chars < MIN_ADDED_PROSE_CHARS:
            golden_lt40 += 1
            continue
        golden_ge40 += 1
        segs = json.loads(d["segments"]) if d.get("segments") else []
        probes = select_probes(segs)
        golden_probes_by_rev[rev_id] = probes
        if not probes:
            golden_skipped_no_probe += 1
        elif rev_id in lifetime_by_rev:
            golden_tracked += 1

    # 1. Item-level recovery on dev golden items with label in (reverted, contested, campaign).
    positive_labels = ("reverted", "contested", "campaign")
    pos_items = [it for it in dev_items if it["label"] in positive_labels]

    def eval_item(it: dict) -> dict:
        ins_ids = [int(x) for x in it["inserting_rev_ids"]]
        rem_ids = [int(x) for x in it["removing_rev_ids"]]
        rem_set = set(rem_ids)
        is_s1 = any(r in s1_non_excluded for r in rem_ids)
        is_s1_any_incl_excluded = any(r in s1_all for r in rem_ids)

        ge40_revs = [
            r for r in ins_ids
            if r in golden_diff_map
            and not golden_diff_map[r].get("error")
            and int(golden_diff_map[r]["added_prose_chars"] or 0) >= MIN_ADDED_PROSE_CHARS
        ]
        probed_revs = [r for r in ge40_revs if golden_probes_by_rev.get(r)]
        tracked_rows = [lifetime_by_rev[r] for r in probed_revs if r in lifetime_by_rev]
        removed_rows = [row for row in tracked_rows if row["removing_rev_id"] is not None]
        right_rows = [row for row in removed_rows if row["removing_rev_id"] in rem_set]

        if right_rows:
            outcome = "right_removal"
        elif not ge40_revs:
            outcome = "below_40_chars"
        elif not probed_revs:
            outcome = "no_usable_probe"
        elif not tracked_rows:
            outcome = "not_tracked"
        elif not removed_rows:
            outcome = "no_removal_found"
        else:
            outcome = "other_removal_rev"

        # Exposure windows: golden = first inserting rev -> first removing rev.
        ins_ts_list = [parse_dt(rev_ts_map[r]) for r in ins_ids if r in rev_ts_map]
        rem_ts_list = [parse_dt(rev_ts_map[r]) for r in rem_ids if r in rev_ts_map]
        golden_window_s = (
            int((min(rem_ts_list) - min(ins_ts_list)).total_seconds())
            if ins_ts_list and rem_ts_list
            else None
        )
        # B's window: earliest tracked inserting revision of the item that B saw removed.
        b_window_s = None
        b_right_window_s = None
        if removed_rows:
            earliest_rem_row = min(removed_rows, key=lambda row: (row["inserted_at"], row["rev_id"]))
            b_window_s = earliest_rem_row["exposure_seconds"]
        if right_rows:
            earliest_right_row = min(right_rows, key=lambda row: (row["inserted_at"], row["rev_id"]))
            b_right_window_s = earliest_right_row["exposure_seconds"]

        return {
            "id": it["id"],
            "label": it["label"],
            "page_id": int(it["page_id"]),
            "is_s1_removal": is_s1,
            "is_s1_including_excluded": is_s1_any_incl_excluded,
            "inserting_count": len(ins_ids),
            "ge40_count": len(ge40_revs),
            "probed_count": len(probed_revs),
            "tracked_count": len(tracked_rows),
            "right_removal": bool(right_rows),
            "any_removal": bool(removed_rows),
            "outcome": outcome,
            "b_removing_rev_ids": [row["removing_rev_id"] for row in removed_rows],
            "golden_removing_rev_ids": rem_ids,
            "golden_window_seconds": golden_window_s,
            "b_window_seconds": b_window_s,
            "b_right_window_seconds": b_right_window_s,
        }

    evaluated_pos = [eval_item(it) for it in pos_items]

    def summarize_group(items_subset: list[dict]) -> dict:
        total = len(items_subset)
        trackable = [x for x in items_subset if x["tracked_count"] > 0]
        right = [x for x in items_subset if x["right_removal"]]
        any_rem = [x for x in items_subset if x["any_removal"]]
        by_outcome: dict[str, int] = {}
        for x in items_subset:
            by_outcome[x["outcome"]] = by_outcome.get(x["outcome"], 0) + 1
        return {
            "items": total,
            "trackable_items": len(trackable),
            "right_removal": len(right),
            "right_share_of_all_pct": _pct(len(right), total),
            "right_share_of_trackable_pct": _pct(len(right), len(trackable)),
            "any_removal": len(any_rem),
            "outcomes": by_outcome,
        }

    recovery_summary = {
        "all_positive": summarize_group(evaluated_pos),
        "s1_removal": summarize_group([x for x in evaluated_pos if x["is_s1_removal"]]),
        "non_s1_removal": summarize_group([x for x in evaluated_pos if not x["is_s1_removal"]]),
        "non_s1_strict_not_in_s1_table": summarize_group(
            [x for x in evaluated_pos if not x["is_s1_including_excluded"]]
        ),
        "by_label": {
            lbl: {
                "all": summarize_group([x for x in evaluated_pos if x["label"] == lbl]),
                "s1_removal": summarize_group(
                    [x for x in evaluated_pos if x["label"] == lbl and x["is_s1_removal"]]
                ),
                "non_s1_removal": summarize_group(
                    [x for x in evaluated_pos if x["label"] == lbl and not x["is_s1_removal"]]
                ),
            }
            for lbl in positive_labels
        },
        "contested_and_campaign": {
            "all": summarize_group([x for x in evaluated_pos if x["label"] in ("contested", "campaign")]),
            "s1_removal": summarize_group(
                [x for x in evaluated_pos if x["label"] in ("contested", "campaign") and x["is_s1_removal"]]
            ),
            "non_s1_removal": summarize_group(
                [x for x in evaluated_pos if x["label"] in ("contested", "campaign") and not x["is_s1_removal"]]
            ),
        },
        "non_s1_item_details": [x for x in evaluated_pos if not x["is_s1_removal"]],
    }

    # 2. Removal window comparison (B vs golden first-insert -> first-removal).
    both_windows = [
        x for x in evaluated_pos
        if x["b_window_seconds"] is not None and x["golden_window_seconds"] is not None
    ]
    right_windows = [
        x for x in evaluated_pos
        if x["b_right_window_seconds"] is not None and x["golden_window_seconds"] is not None
    ]

    def window_stats(pairs: list[tuple[int, int]]) -> dict:
        if not pairs:
            return {"count": 0}
        diffs = [b - g for b, g in pairs]
        abs_diffs = [abs(d) for d in diffs]
        exact = sum(1 for d in diffs if d == 0)
        within_60s = sum(1 for d in abs_diffs if d <= 60)
        within_1h = sum(1 for d in abs_diffs if d <= 3600)
        return {
            "count": len(pairs),
            "exact_match_count": exact,
            "exact_match_pct": _pct(exact, len(pairs)),
            "within_60s_count": within_60s,
            "within_60s_pct": _pct(within_60s, len(pairs)),
            "within_1h_count": within_1h,
            "within_1h_pct": _pct(within_1h, len(pairs)),
            "golden_median_seconds": round(statistics.median(g for _, g in pairs), 1),
            "b_median_seconds": round(statistics.median(b for b, _ in pairs), 1),
            "signed_diff_median_seconds": round(statistics.median(diffs), 1),
            "abs_diff_median_seconds": round(statistics.median(abs_diffs), 1),
            "abs_diff_p90_seconds": _quantiles_summary(abs_diffs)["p90"],
        }

    window_comparison = {
        "all_removed_items": window_stats(
            [(int(x["b_window_seconds"]), int(x["golden_window_seconds"])) for x in both_windows]
        ),
        "right_removal_items": window_stats(
            [(int(x["b_right_window_seconds"]), int(x["golden_window_seconds"])) for x in right_windows]
        ),
    }

    # 3. Not-incident `kept` items: whether B says still present.
    kept_items_raw = [
        it for it in dev_items if it["label"] == "not-incident" and it.get("exclusion") == "kept"
    ]
    kept_details = []
    for it in kept_items_raw:
        ins_ids = [int(x) for x in it["inserting_rev_ids"]]
        t_rows = [lifetime_by_rev[r] for r in ins_ids if r in lifetime_by_rev]
        still_present_revs = [r["rev_id"] for r in t_rows if r["still_present_latest"] == 1]
        reappeared_revs = [r["rev_id"] for r in t_rows if r["reappeared"] == 1]
        never_removed_revs = [r["rev_id"] for r in t_rows if r["removing_rev_id"] is None]
        kept_details.append(
            {
                "id": it["id"],
                "page_id": int(it["page_id"]),
                "inserting_rev_ids": ins_ids,
                "tracked_rev_ids": [r["rev_id"] for r in t_rows],
                "still_present_rev_ids": still_present_revs,
                "reappeared_rev_ids": reappeared_revs,
                "never_removed_rev_ids": never_removed_revs,
                "any_still_present_latest": len(still_present_revs) > 0,
                "all_tracked_still_present_latest": len(t_rows) > 0 and len(still_present_revs) == len(t_rows),
            }
        )
    kept_summary = {
        "items": len(kept_details),
        "trackable_items": sum(1 for x in kept_details if x["tracked_rev_ids"]),
        "any_still_present_count": sum(1 for x in kept_details if x["any_still_present_latest"]),
        "all_tracked_still_present_count": sum(
            1 for x in kept_details if x["all_tracked_still_present_latest"]
        ),
        "details": kept_details,
    }

    # 4. Sample analysis (`rev_id` in the deterministic seeded sample).
    other_pool_rows = aot.sql(
        "SELECT rev_id FROM research.s3_diff "
        f"WHERE kind='edit' AND error IS NULL AND added_prose_chars >= {MIN_ADDED_PROSE_CHARS} "
        f"AND page_id IN ({aot.ints(sorted(dev_pages))}) "
        f"AND rev_id NOT IN ({aot.ints(sorted(golden_ins_revs))}) "
        "ORDER BY rev_id"
    )
    other_pool_ids = [int(r["rev_id"]) for r in other_pool_rows]
    rng = random.Random(seed)
    sampled_ids = sorted(rng.sample(other_pool_ids, min(sample_size, len(other_pool_ids))))
    sampled_id_set = set(sampled_ids)
    sample_lt_rows = sorted(
        (r for rev_id, r in lifetime_by_rev.items() if rev_id in sampled_id_set),
        key=lambda r: r["rev_id"],
    )
    sampled_diff_map = fetch_s3_diff_by_revs(sampled_ids)
    sampled_skipped_no_probe = 0
    for sid in sampled_ids:
        d = sampled_diff_map.get(sid)
        segs = json.loads(d["segments"]) if d and d.get("segments") else []
        if not select_probes(segs):
            sampled_skipped_no_probe += 1

    sample_removed = [r for r in sample_lt_rows if r["removing_rev_id"] is not None]
    sample_partial = [r for r in sample_removed if r["removal_kind"] == "partial"]
    sample_identity = [r for r in sample_removed if r["removal_kind"] == "identity_revert"]
    sample_still_present_unremoved = [r for r in sample_lt_rows if r["removing_rev_id"] is None]
    sample_partial_stay_gone = [r for r in sample_partial if r["still_present_latest"] == 0]

    # Pick 5 deterministic masked examples from `sample_partial`.
    ex_rng = random.Random(seed + "-examples")
    example_pool = list(sample_partial)
    ex_chosen = ex_rng.sample(example_pool, min(5, len(example_pool)))
    ex_chosen.sort(key=lambda r: r["rev_id"])

    ex_pages = sorted({r["page_id"] for r in ex_chosen})
    title_by_page: dict[int, str] = {}
    if ex_pages:
        for r in aot.sql(
            f"SELECT page_id, title FROM corpus.article WHERE lang='fi' AND page_id IN ({aot.ints(ex_pages)})"
        ):
            title_by_page[int(r["page_id"])] = r["title"]
        missing_t = [p for p in ex_pages if p not in title_by_page]
        if missing_t:
            for r in aot.sql(
                f"SELECT page_id, MAX(title) AS title FROM corpus.revision WHERE page_id IN ({aot.ints(missing_t)}) GROUP BY page_id"
            ):
                title_by_page[int(r["page_id"])] = r["title"]

    ex_diffs = fetch_s3_diff_by_revs([r["rev_id"] for r in ex_chosen])
    partial_examples = []
    for r in ex_chosen:
        d = ex_diffs.get(r["rev_id"])
        segs = json.loads(d["segments"]) if d and d.get("segments") else []
        probes = select_probes(segs)
        primary_probe = aot.mask_comment(probes[0]) if probes else ""
        partial_examples.append(
            {
                "page_id": r["page_id"],
                "article_title": title_by_page.get(r["page_id"], f"page:{r['page_id']}"),
                "inserting_rev_id": r["rev_id"],
                "removing_rev_id": r["removing_rev_id"],
                "probe": primary_probe,
                "probe_count": r["probe_count"],
                "exposure_seconds": r["exposure_seconds"],
                "reappeared": bool(r["reappeared"]),
                "still_present_latest": bool(r["still_present_latest"]),
                "removal_summary": r["removal_summary"] or "",
            }
        )

    sample_summary = {
        "pool_ge40_non_golden": len(other_pool_ids),
        "sampled_candidates": len(sampled_ids),
        "skipped_no_probe": sampled_skipped_no_probe,
        "tracked": len(sample_lt_rows),
        "removed_total": len(sample_removed),
        "never_removed": len(sample_still_present_unremoved),
        "identity_revert_count": len(sample_identity),
        "partial_removal_count": len(sample_partial),
        "partial_share_of_tracked_pct": _pct(len(sample_partial), len(sample_lt_rows)),
        "partial_share_of_removed_pct": _pct(len(sample_partial), len(sample_removed)),
        "partial_still_absent_latest_count": len(sample_partial_stay_gone),
        "examples": partial_examples,
    }

    # 5. Cost & Topic Extrapolation (`corpus.revision` byte deltas >= +200 on one topic's pages).
    all_lt = list(lifetime_by_rev.values())
    golden_lt = [r for rev_id, r in lifetime_by_rev.items() if rev_id in golden_ins_revs]

    cost_stats = {
        "all_tracked": {
            "candidates": len(all_lt),
            "queries": _quantiles_summary([r["queries"] for r in all_lt]),
            "seconds": _quantiles_summary([r["seconds"] for r in all_lt]),
            "total_queries": sum(r["queries"] for r in all_lt),
            "total_seconds": round(sum(r["seconds"] for r in all_lt), 2),
        },
        "golden_dev": {
            "candidates_ge40": golden_ge40,
            "skipped_no_probe": golden_skipped_no_probe,
            "tracked": len(golden_lt),
            "queries": _quantiles_summary([r["queries"] for r in golden_lt]),
            "seconds": _quantiles_summary([r["seconds"] for r in golden_lt]),
        },
        "sample": {
            "candidates_ge40": len(sampled_ids),
            "skipped_no_probe": sampled_skipped_no_probe,
            "tracked": len(sample_lt_rows),
            "queries": _quantiles_summary([r["queries"] for r in sample_lt_rows]),
            "seconds": _quantiles_summary([r["seconds"] for r in sample_lt_rows]),
        },
    }

    # Count non-null revisions with byte delta >= +200 per dev page, then aggregate per dev topic.
    delta_rows = aot.sql(
        f"""
        WITH raw_ord AS (
          SELECT page_id, rev_id, ts, bytes, sha1,
                 LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
          FROM corpus.revision
          WHERE page_id IN ({aot.ints(sorted(dev_pages))})
        ),
        non_null AS (
          SELECT page_id, rev_id, bytes,
                 LAG(bytes) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_bytes
          FROM raw_ord
          WHERE NOT (sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1)
        )
        SELECT page_id,
               COUNT(*) AS non_null_revs,
               SUM(CAST(bytes AS SIGNED) - CAST(COALESCE(prev_bytes, 0) AS SIGNED) >= 200) AS delta_ge_200
        FROM non_null
        GROUP BY page_id
        """
    )
    delta_by_page = {
        int(r["page_id"]): {
            "non_null_revs": int(r["non_null_revs"]),
            "delta_ge_200": int(r["delta_ge_200"] or 0),
        }
        for r in delta_rows
    }

    mean_q = cost_stats["sample"]["queries"]["mean"] or cost_stats["all_tracked"]["queries"]["mean"] or 0.0
    mean_s = cost_stats["sample"]["seconds"]["mean"] or cost_stats["all_tracked"]["seconds"]["mean"] or 0.0
    probed_rate = (
        (len(sampled_ids) - sampled_skipped_no_probe) / len(sampled_ids)
        if sampled_ids
        else ((golden_ge40 - golden_skipped_no_probe) / golden_ge40 if golden_ge40 else 1.0)
    )

    per_topic_extrapolation = []
    for topic in dev_topics:
        if topic.get("kind") != "topic":
            continue
        t_pages = sorted({int(a["page_id"]) for a in topic.get("articles", [])})
        additions_200 = sum(delta_by_page.get(p, {}).get("delta_ge_200", 0) for p in t_pages)
        non_null_total = sum(delta_by_page.get(p, {}).get("non_null_revs", 0) for p in t_pages)
        est_probed = round(additions_200 * probed_rate)
        per_topic_extrapolation.append(
            {
                "topic_id": topic["id"],
                "article_count": len(t_pages),
                "non_null_revisions": non_null_total,
                "byte_delta_ge_200_count": additions_200,
                "est_queries_if_all_tracked": round(additions_200 * mean_q),
                "est_seconds_if_all_tracked": round(additions_200 * mean_s, 1),
                "est_probed_candidates": est_probed,
                "est_queries_probed": round(est_probed * mean_q),
                "est_seconds_probed": round(est_probed * mean_s, 1),
            }
        )

    median_additions = (
        round(statistics.median(t["byte_delta_ge_200_count"] for t in per_topic_extrapolation))
        if per_topic_extrapolation
        else 0
    )
    mean_additions = (
        round(statistics.fmean(t["byte_delta_ge_200_count"] for t in per_topic_extrapolation), 1)
        if per_topic_extrapolation
        else 0.0
    )

    report_data = {
        "issue": 30,
        "approach": "B (sentence-probe content-lifetime tracking)",
        "split": "dev",
        "parameters": {
            "min_added_prose_chars": MIN_ADDED_PROSE_CHARS,
            "min_probe_chars": MIN_PROBE_CHARS,
            "max_probes": MAX_PROBES,
            "max_queries_per_candidate": MAX_QUERIES_PER_CANDIDATE,
            "sample_size": sample_size,
            "seed": seed,
        },
        "golden_candidates": {
            "distinct_inserting_revs": len(golden_ins_revs),
            "missing_s3_diff": golden_missing_s3_diff,
            "below_40_prose_chars": golden_lt40,
            "eligible_ge40": golden_ge40,
            "skipped_no_usable_probe": golden_skipped_no_probe,
            "tracked_in_table": golden_tracked,
        },
        "recovery": recovery_summary,
        "exposure_windows": window_comparison,
        "kept_items": kept_summary,
        "sample": sample_summary,
        "cost": {
            **cost_stats,
            "probe_yield_rate": round(probed_rate, 4),
            "topic_extrapolation": {
                "mean_byte_delta_ge_200_per_topic": mean_additions,
                "median_byte_delta_ge_200_per_topic": median_additions,
                "est_mean_topic_queries_all": round(mean_additions * mean_q),
                "est_mean_topic_seconds_all": round(mean_additions * mean_s, 1),
                "est_mean_topic_queries_probed": round(mean_additions * probed_rate * mean_q),
                "est_mean_topic_seconds_probed": round(mean_additions * probed_rate * mean_s, 1),
                "per_topic": per_topic_extrapolation,
            },
        },
    }

    md = render_markdown_report(report_data)
    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "s3b-recovery.json").write_text(
        json.dumps(report_data, indent=2, ensure_ascii=False) + NL,
        encoding="utf-8",
    )
    (out_dir / "s3b-recovery.md").write_text(md, encoding="utf-8")
    aot.log(f"[report] wrote {out_dir / 's3b-recovery.md'} and s3b-recovery.json")
    return report_data


def render_markdown_report(data: dict) -> str:
    gc = data["golden_candidates"]
    rec = data["recovery"]
    win = data["exposure_windows"]
    kept = data["kept_items"]
    smp = data["sample"]
    cost = data["cost"]
    ext = cost["topic_extrapolation"]

    lines: list[str] = [
        "# P1.S3 Approach B (`s3_lifetime`) — Recovery, Partial Removals & Cost Report",
        "",
        "Evaluated on `golden.json` **dev** split only (`s3_diff` `kind='edit'`, `added_prose_chars >= 40`,",
        "up to 3 verbatim sentence probes of `>= 25` chars, server-side `LOCATE`, `>= half` presence rule, max 40 queries/candidate).",
        "",
        "## 1. Candidate Accounting",
        "",
        "| Set | Pool | `< 40` prose chars | Eligible (`>= 40`) | Skipped (no usable probe) | Tracked in `s3_lifetime` |",
        "|---|---:|---:|---:|---:|---:|",
        f"| Golden dev inserting revs (all labels) | {gc['distinct_inserting_revs']} *(missing diff: {gc['missing_s3_diff']})* | {gc['below_40_prose_chars']} | {gc['eligible_ge40']} | {gc['skipped_no_usable_probe']} | {gc['tracked_in_table']} |",
        f"| Seeded non-golden sample (`--sample {data['parameters']['sample_size']}`) | {smp['pool_ge40_non_golden']} (`>= 40`) | — | {smp['sampled_candidates']} | {smp['skipped_no_probe']} | {smp['tracked']} |",
        "",
        "## 2. Golden Dev Recovery (`reverted` / `contested` / `campaign`)",
        "",
        "An item is counted as **right removal** when Approach B tracks at least one of its inserting revisions",
        "and finds `removing_rev_id` inside the item's `removing_rev_ids`.",
        "`S1 removal` = at least one `removing_rev_id` is a non-excluded `s1_revert.reverting_rev_id` (what Approach A can see).",
        "",
        "| Group | Golden items | Trackable items | Right removal | % of all items | % of trackable | Any removal found | Outcomes breakdown |",
        "|---|---:|---:|---:|---:|---:|---:|---|",
    ]

    def fmt_outcomes(outcomes: dict[str, int]) -> str:
        order = (
            "right_removal",
            "other_removal_rev",
            "no_removal_found",
            "no_usable_probe",
            "below_40_chars",
            "not_tracked",
        )
        parts = [f"`{k}`={outcomes[k]}" for k in order if outcomes.get(k)]
        return ", ".join(parts) or "—"

    rows_spec = [
        ("**All (`reverted`+`contested`+`campaign`)**", rec["all_positive"]),
        ("— S1 identity-revert removal (Approach A visible)", rec["s1_removal"]),
        ("— **Non-S1 removal (Approach A blind)**", rec["non_s1_removal"]),
        ("— Non-S1 *(excl. 2 `wide_span` S1 rows)*", rec["non_s1_strict_not_in_s1_table"]),
        ("**`contested` + `campaign` (strict TPs)**", rec["contested_and_campaign"]["all"]),
        ("— `contested`+`campaign` S1 removal", rec["contested_and_campaign"]["s1_removal"]),
        ("— **`contested`+`campaign` Non-S1 removal**", rec["contested_and_campaign"]["non_s1_removal"]),
        ("`reverted` (all)", rec["by_label"]["reverted"]["all"]),
        ("— `reverted` S1 removal", rec["by_label"]["reverted"]["s1_removal"]),
        ("— `reverted` Non-S1 removal", rec["by_label"]["reverted"]["non_s1_removal"]),
        ("`contested` (all)", rec["by_label"]["contested"]["all"]),
        ("— `contested` S1 removal", rec["by_label"]["contested"]["s1_removal"]),
        ("— `contested` Non-S1 removal", rec["by_label"]["contested"]["non_s1_removal"]),
        ("`campaign` (all)", rec["by_label"]["campaign"]["all"]),
    ]
    for title, g in rows_spec:
        lines.append(
            f"| {title} | {g['items']} | {g['trackable_items']} | {g['right_removal']} | "
            f"{g['right_share_of_all_pct']:.1f}% | {g['right_share_of_trackable_pct']:.1f}% | "
            f"{g['any_removal']} | {fmt_outcomes(g['outcomes'])} |"
        )

    lines.extend(
        [
            "",
            "### Non-S1 Golden Items Detail (Items Approach A Cannot See)",
            "",
            "| Item | Label | Note | Golden `removing_rev_ids` | B `removing_rev_ids` | Outcome |",
            "|---|---|---|---|---|---|",
        ]
    )
    for item in rec["non_s1_item_details"]:
        note = "S1 `wide_span` (>15)" if item["is_s1_including_excluded"] else "partial / non-SHA1"
        g_rems = ", ".join(map(str, item["golden_removing_rev_ids"]))
        b_rems = ", ".join(map(str, item["b_removing_rev_ids"])) or "—"
        lines.append(f"| `{item['id']}` | `{item['label']}` | {note} | `{g_rems}` | `{b_rems}` | `{item['outcome']}` |")

    lines.extend(
        [
            "",
            "## 3. Exposure Windows: Approach B vs Golden (`first-insert → first-removal`)",
            "",
            "| Subset | Items | Exact (`0 s`) | Within `±60 s` | Within `±1 h` | Golden median | B median | Median `B - golden` | Median `|B - golden|` | P90 `|B - golden|` |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for title, w in [
        ("Items with right removal", win["right_removal_items"]),
        ("All items where B found a removal", win["all_removed_items"]),
    ]:
        if not w.get("count"):
            lines.append(f"| {title} | 0 | — | — | — | — | — | — | — | — |")
            continue
        lines.append(
            f"| {title} | {w['count']} | {w['exact_match_count']} ({w['exact_match_pct']:.1f}%) | "
            f"{w['within_60s_count']} ({w['within_60s_pct']:.1f}%) | {w['within_1h_count']} ({w['within_1h_pct']:.1f}%) | "
            f"{_fmt_dur(w['golden_median_seconds'])} | {_fmt_dur(w['b_median_seconds'])} | "
            f"{_fmt_dur(w['signed_diff_median_seconds'])} | {_fmt_dur(w['abs_diff_median_seconds'])} | "
            f"{_fmt_dur(w['abs_diff_p90_seconds'])} |"
        )

    lines.extend(
        [
            "",
            "## 4. Not-Incident `kept` Items (`exclusion = 'kept'`)",
            "",
            f"- Dev `kept` items: **{kept['items']}** (trackable: **{kept['trackable_items']}**).",
            f"- Items with `>= 1` inserting rev having `still_present_latest = 1`: **{kept['any_still_present_count']} / {kept['items']}**.",
            f"- Items where **every** tracked inserting rev has `still_present_latest = 1`: **{kept['all_tracked_still_present_count']} / {kept['items']}**.",
            "",
            "| Item | `page_id` | Inserting revs | Tracked revs | `still_present_latest = 1` revs | `reappeared = 1` revs | Never-removed revs |",
            "|---|---:|---|---|---|---|---|",
        ]
    )
    for kd in kept["details"]:
        lines.append(
            f"| `{kd['id']}` | {kd['page_id']} | `{kd['inserting_rev_ids']}` | `{kd['tracked_rev_ids']}` | "
            f"`{kd['still_present_rev_ids']}` | `{kd['reappeared_rev_ids']}` | `{kd['never_removed_rev_ids']}` |"
        )

    lines.extend(
        [
            "",
            "## 5. Seeded Non-Golden Sample (`--sample`)",
            "",
            f"- Sampled `s3_diff` `kind='edit'` (`added_prose_chars >= 40`) candidates: **{smp['sampled_candidates']}** "
            f"(from pool of **{smp['pool_ge40_non_golden']}** on dev pages).",
            f"- Skipped (no usable probe): **{smp['skipped_no_probe']}**; tracked: **{smp['tracked']}**.",
            f"- Never removed (`removing_rev_id IS NULL`): **{smp['never_removed']}**.",
            f"- Removed: **{smp['removed_total']}** — `identity_revert`: **{smp['identity_revert_count']}**, "
            f"**`partial` (invisible to Approach A)**: **{smp['partial_removal_count']}** "
            f"(**{smp['partial_share_of_tracked_pct']:.1f}%** of tracked, **{smp['partial_share_of_removed_pct']:.1f}%** of removed; "
            f"**{smp['partial_still_absent_latest_count']}** remain absent in latest revision).",
            "",
            "### 5 Masked Examples of `partial` Removals from the Sample",
            "",
        ]
    )
    for idx, ex in enumerate(smp["examples"], 1):
        diff_url = f"https://fi.wikipedia.org/w/index.php?diff=prev&oldid={ex['removing_rev_id']}"
        ins_url = f"https://fi.wikipedia.org/w/index.php?diff=prev&oldid={ex['inserting_rev_id']}"
        summary_disp = ex["removal_summary"] if ex["removal_summary"] else "*(empty summary)*"
        lines.extend(
            [
                f"#### Example {idx} · *{ex['article_title']}* (`page_id={ex['page_id']}`)",
                f"- **Inserting rev:** [{ex['inserting_rev_id']}]({ins_url}) · **Removing rev:** [{ex['removing_rev_id']}]({diff_url}) "
                f"after {_fmt_dur(ex['exposure_seconds'])} "
                f"(`reappeared={int(ex['reappeared'])}`, `still_present_latest={int(ex['still_present_latest'])}`)",
                f"- **Probe (`1/{ex['probe_count']}`):** `{ex['probe']}`",
                f"- **Masked removal summary:** {summary_disp}",
                "",
            ]
        )

    q_all = cost["all_tracked"]["queries"]
    s_all = cost["all_tracked"]["seconds"]
    q_smp = cost["sample"]["queries"]
    s_smp = cost["sample"]["seconds"]
    q_gld = cost["golden_dev"]["queries"]
    s_gld = cost["golden_dev"]["seconds"]

    lines.extend(
        [
            "## 6. Cost & Extrapolation to All Substantive Additions on a Topic",
            "",
            "| Set | Tracked candidates | Mean queries | Median queries | P90 queries | Max queries | Mean seconds | Median seconds | P90 seconds | Total seconds |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
            f"| Golden dev | {q_gld['count']} | {q_gld['mean'] or 0:.2f} | {q_gld['median'] or 0:.1f} | {q_gld['p90'] or 0:.1f} | {q_gld['max'] or 0:.0f} | "
            f"{s_gld['mean'] or 0:.2f} s | {s_gld['median'] or 0:.2f} s | {s_gld['p90'] or 0:.2f} s | {sum_or_zero(s_gld, cost['golden_dev']):.1f} s |",
            f"| Non-golden sample | {q_smp['count']} | {q_smp['mean'] or 0:.2f} | {q_smp['median'] or 0:.1f} | {q_smp['p90'] or 0:.1f} | {q_smp['max'] or 0:.0f} | "
            f"{s_smp['mean'] or 0:.2f} s | {s_smp['median'] or 0:.2f} s | {s_smp['p90'] or 0:.2f} s | {sum_or_zero(s_smp, cost['sample']):.1f} s |",
            f"| **Combined** | **{q_all['count']}** | **{q_all['mean'] or 0:.2f}** | **{q_all['median'] or 0:.1f}** | **{q_all['p90'] or 0:.1f}** | **{q_all['max'] or 0:.0f}** | "
            f"**{s_all['mean'] or 0:.2f} s** | **{s_all['median'] or 0:.2f} s** | **{s_all['p90'] or 0:.2f} s** | **{cost['all_tracked']['total_seconds']:.1f} s** |",
            "",
            "### Extrapolation to One Topic's Pages (`corpus.revision` non-null `bytes_delta >= +200`)",
            "",
            f"- Mean substantive additions (`bytes_delta >= +200`) per dev topic: **{ext['mean_byte_delta_ge_200_per_topic']}** "
            f"(median: **{ext['median_byte_delta_ge_200_per_topic']}**).",
            f"- If every `bytes_delta >= +200` revision were tracked sequentially over the SSH tunnel: "
            f"**~{ext['est_mean_topic_queries_all']} SQL queries** (**~{ext['est_mean_topic_seconds_all']:.0f} s** / "
            f"**{ext['est_mean_topic_seconds_all'] / 60:.1f} min** per topic); applying the empirical usable-probe yield "
            f"(`{cost['probe_yield_rate'] * 100:.1f}%`): **~{ext['est_mean_topic_queries_probed']} SQL queries** "
            f"(**~{ext['est_mean_topic_seconds_probed']:.0f} s** / **{ext['est_mean_topic_seconds_probed'] / 60:.1f} min** per topic).",
            "",
            "| Dev topic | Articles | Non-null revs | `bytes_delta >= +200` | Est. queries (all) | Est. wall time (all) | Est. probed (`"
            + f"{cost['probe_yield_rate'] * 100:.0f}%"
            + "`) | Est. queries (probed) | Est. wall time (probed) |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for tp in ext["per_topic"]:
        lines.append(
            f"| `{tp['topic_id']}` | {tp['article_count']} | {tp['non_null_revisions']} | {tp['byte_delta_ge_200_count']} | "
            f"{tp['est_queries_if_all_tracked']} | {_fmt_dur(tp['est_seconds_if_all_tracked'])} | "
            f"{tp['est_probed_candidates']} | {tp['est_queries_probed']} | {_fmt_dur(tp['est_seconds_probed'])} |"
        )
    lines.append("")
    return NL.join(lines)


def sum_or_zero(sec_stats: dict, group_dict: dict) -> float:
    if not sec_stats.get("count") or sec_stats.get("mean") is None:
        return 0.0
    return float(sec_stats["count"]) * float(sec_stats["mean"])


# ── CLI Entrypoint ───────────────────────────────────────────────────────────


def main(argv: Sequence[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="P1.S3 Approach B: sentence-probe content-lifetime tracker")
    ap.add_argument("--golden-dev", action="store_true", help="track every dev golden item inserting revision (>=40 prose chars)")
    ap.add_argument("--sample", type=int, metavar="N", help="track a deterministic seeded sample of N non-golden candidates on dev pages")
    ap.add_argument("--revs", help="comma-separated rev_ids for ad hoc tracking")
    ap.add_argument("--seed", default=DEFAULT_SEED, help=f"RNG seed for --sample (default: {DEFAULT_SEED})")
    ap.add_argument("--max-seconds", type=float, default=None, help="optional wall-clock cap (seconds) per candidate set")
    ap.add_argument("--drop", action="store_true", help="force DROP + CREATE of research.s3_lifetime")
    ap.add_argument("--report", action="store_true", help="regenerate results/s3b-recovery.{md,json} from research.s3_lifetime")
    ap.add_argument("--no-report", action="store_true", help="skip report generation after --golden-dev / --sample")
    args = ap.parse_args(argv)

    if not (args.golden_dev or args.sample is not None or args.revs or args.report):
        ap.error("specify at least one of --golden-dev, --sample N, --revs a,b, or --report")

    session = PersistentMariaDb()
    try:
        if args.golden_dev or args.sample is not None or args.revs:
            # Drop + recreate unconditionally when `--drop` is passed or when both `--golden-dev` and `--sample` run together.
            drop_all = bool(args.drop or (args.golden_dev and args.sample is not None and not args.revs))
            ensure_table(session, drop=drop_all)

            s1_reverting_revs = {
                int(r["reverting_rev_id"])
                for r in aot.sql("SELECT reverting_rev_id FROM research.s1_revert WHERE class <> 'excluded'")
            }
            page_cache: dict[int, tuple[list[dict], dict[int, dict]]] = {}
            _, dev_pages, golden_ins_revs = load_golden_dev()

            existing_revs = (
                set()
                if drop_all
                else {int(r["rev_id"]) for r in aot.sql("SELECT rev_id FROM research.s3_lifetime")}
            )

            if args.golden_dev:
                g_diff_map = fetch_s3_diff_by_revs(sorted(golden_ins_revs))
                missing = [r for r in sorted(golden_ins_revs) if r not in g_diff_map or g_diff_map[r].get("error")]
                if missing:
                    aot.log(f"[golden-dev] note: {len(missing)} inserting revs missing/errored in s3_diff (skipped)")
                g_candidates = [
                    row
                    for rev_id, row in g_diff_map.items()
                    if not row.get("error")
                    and int(row["page_id"]) in dev_pages
                    and int(row["added_prose_chars"] or 0) >= MIN_ADDED_PROSE_CHARS
                    and rev_id not in existing_revs
                ]
                track_candidate_set(
                    session,
                    g_candidates,
                    s1_reverting_revs,
                    page_cache,
                    label="golden-dev",
                    max_seconds=args.max_seconds,
                )

            if args.sample is not None:
                pool_rows = aot.sql(
                    "SELECT rev_id FROM research.s3_diff "
                    f"WHERE kind='edit' AND error IS NULL AND added_prose_chars >= {MIN_ADDED_PROSE_CHARS} "
                    f"AND page_id IN ({aot.ints(sorted(dev_pages))}) "
                    f"AND rev_id NOT IN ({aot.ints(sorted(golden_ins_revs))}) "
                    "ORDER BY rev_id"
                )
                pool_ids = [int(r["rev_id"]) for r in pool_rows]
                rng = random.Random(args.seed)
                chosen_ids = sorted(rng.sample(pool_ids, min(args.sample, len(pool_ids))))
                if not drop_all:
                    keep_ids = sorted(golden_ins_revs | set(chosen_ids))
                    if keep_ids:
                        session.exec_write(
                            f"DELETE FROM research.s3_lifetime WHERE rev_id NOT IN ({aot.ints(keep_ids)})"
                        )
                todo_ids = [rid for rid in chosen_ids if rid not in existing_revs]
                s_diff_map = fetch_s3_diff_by_revs(todo_ids) if todo_ids else {}
                s_candidates = [s_diff_map[rid] for rid in todo_ids if rid in s_diff_map]
                track_candidate_set(
                    session,
                    s_candidates,
                    s1_reverting_revs,
                    page_cache,
                    label=f"sample-{args.sample}",
                    max_seconds=args.max_seconds,
                )

            if args.revs:
                ad_hoc_ids = [int(x.strip()) for x in args.revs.split(",") if x.strip()]
                r_diff_map = fetch_s3_diff_by_revs(ad_hoc_ids)
                missing_adhoc = [r for r in ad_hoc_ids if r not in r_diff_map or r_diff_map[r].get("error")]
                if missing_adhoc:
                    aot.log(f"[revs] missing/errored in s3_diff: {missing_adhoc}")
                r_candidates = [r_diff_map[r] for r in ad_hoc_ids if r in r_diff_map and not r_diff_map[r].get("error")]
                track_candidate_set(
                    session,
                    r_candidates,
                    s1_reverting_revs,
                    page_cache,
                    label="revs",
                    max_seconds=args.max_seconds,
                )

        should_report = args.report or ((args.golden_dev or args.sample is not None) and not args.no_report)
        if should_report:
            generate_report(
                sample_size=args.sample if args.sample is not None else DEFAULT_SAMPLE_SIZE,
                seed=args.seed,
            )
    finally:
        session.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
