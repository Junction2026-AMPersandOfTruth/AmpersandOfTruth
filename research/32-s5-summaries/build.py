"""Build `research.s5_summary` and `research.s5_revert_reason` (P1.S5, #32).

Usage:
    python3 research/32-s5-summaries/build.py              # build s5_summary + join s5_revert_reason
    python3 research/32-s5-summaries/build.py --join-only  # re-run only s5_revert_reason (e.g. after s1_revert lands)
    python3 research/32-s5-summaries/build.py --stats      # print masked coverage & distribution stats from built tables
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[0] / "lib"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
import lexicon  # noqa: E402

PAGE_CHUNK_SIZE = 2000
INSERT_BATCH_SIZE = 2000
STATEMENTS_PER_CONN = 5


def _sql_int_or_null(val: int | None) -> str:
    if val is None or val < 0:
        return "NULL"
    return str(int(val))


def _sql_str_or_null(val: str | None) -> str:
    if val is None:
        return "NULL"
    return aot.quote(val)


def _flush_batches(table: str, batches: list[list[str]]) -> None:
    if not batches:
        return
    stmts = [
        f"INSERT INTO {table} "
        "(page_id, rev_id, auto_family, auto_target_rev_id, has_reason_text, reasons, insert_flags) "
        f"VALUES {','.join(batch)};"
        for batch in batches
    ]
    aot.sql(chr(10).join(stmts), timeout=85)
    batches.clear()


def build_s5_summary() -> dict[str, int | float]:
    """Scan `corpus.revision` in page_id ranges and populate `research.s5_summary`."""
    t0 = time.time()
    page_rows = aot.sql("SELECT DISTINCT page_id FROM corpus.revision ORDER BY page_id", timeout=60)
    page_ids = [int(r["page_id"]) for r in page_rows]
    if not page_ids:
        raise RuntimeError("corpus.revision returned 0 pages")

    aot.sql("DROP TABLE IF EXISTS research.s5_summary_tmp", timeout=60)
    aot.sql(
        """
        CREATE TABLE research.s5_summary_tmp (
            page_id INT UNSIGNED NOT NULL,
            rev_id INT UNSIGNED NOT NULL,
            auto_family VARCHAR(32) NULL,
            auto_target_rev_id BIGINT UNSIGNED NULL,
            has_reason_text TINYINT(1) NOT NULL,
            reasons VARCHAR(255) NOT NULL,
            insert_flags VARCHAR(128) NOT NULL,
            PRIMARY KEY (rev_id),
            KEY idx_page_id (page_id),
            KEY idx_auto_family (auto_family)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
        """,
        timeout=60,
    )

    total_revs = 0
    written_rows = 0
    current_batch: list[str] = []
    pending_batches: list[list[str]] = []

    num_chunks = (len(page_ids) + PAGE_CHUNK_SIZE - 1) // PAGE_CHUNK_SIZE
    for chunk_idx in range(num_chunks):
        chunk = page_ids[chunk_idx * PAGE_CHUNK_SIZE : (chunk_idx + 1) * PAGE_CHUNK_SIZE]
        p_lo, p_hi = chunk[0], chunk[-1]
        qt0 = time.time()
        rows = aot.sql(
            f"""
            SELECT page_id, rev_id, minor, bytes, comment
            FROM corpus.revision
            WHERE page_id BETWEEN {p_lo} AND {p_hi}
            ORDER BY page_id, ts, rev_id
            """,
            timeout=85,
        )
        q_elapsed = time.time() - qt0

        prev_page_id: int | None = None
        prev_bytes: int = 0

        for r in rows:
            total_revs += 1
            pid = int(r["page_id"])
            rid = int(r["rev_id"])
            minor = int(r["minor"] or 0)
            cur_bytes = int(r["bytes"] or 0)
            comment = r["comment"]

            if pid != prev_page_id:
                delta = cur_bytes
                prev_page_id = pid
            else:
                delta = cur_bytes - prev_bytes
            prev_bytes = cur_bytes

            if comment is None or comment == "":
                continue

            auto = lexicon.parse_auto(comment)
            if auto is not None:
                fam = str(auto["family"])
                target_id = auto["target_rev_id"]
                rt = str(auto["reason_text"])
            else:
                fam = None
                target_id = None
                rt = lexicon.strip_section(comment)

            has_rt = 1 if rt else 0
            cats = sorted(lexicon.reasons(rt))
            flags = sorted(lexicon.insert_flags(comment, delta, bool(minor)))

            row_sql = (
                f"({pid},{rid},{_sql_str_or_null(fam)},"
                f"{_sql_int_or_null(target_id if isinstance(target_id, int) else None)},"
                f"{has_rt},{aot.quote(','.join(cats))},{aot.quote(','.join(flags))})"
            )
            current_batch.append(row_sql)
            written_rows += 1

            if len(current_batch) >= INSERT_BATCH_SIZE:
                pending_batches.append(current_batch)
                current_batch = []
                if len(pending_batches) >= STATEMENTS_PER_CONN:
                    _flush_batches("research.s5_summary_tmp", pending_batches)

        print(
            f"[s5_summary] chunk {chunk_idx + 1}/{num_chunks} "
            f"(page_id {p_lo}..{p_hi}): read {len(rows)} revs in {q_elapsed:.2f}s, "
            f"total written={written_rows}",
            file=sys.stderr,
        )

    if current_batch:
        pending_batches.append(current_batch)
    _flush_batches("research.s5_summary_tmp", pending_batches)

    aot.sql("DROP TABLE IF EXISTS research.s5_summary", timeout=60)
    aot.sql("RENAME TABLE research.s5_summary_tmp TO research.s5_summary", timeout=60)

    elapsed = round(time.time() - t0, 2)
    print(
        f"[s5_summary] complete: {written_rows} non-empty comment rows (of {total_revs} revisions) in {elapsed}s",
        file=sys.stderr,
    )
    return {"total_revisions": total_revs, "s5_summary_rows": written_rows, "elapsed_s": elapsed}


def build_s5_revert_reason() -> dict[str, str | int | float]:
    """Join `research.s5_summary` onto `research.s1_revert` (if present) else `research.prof_rev_reverts`."""
    t0 = time.time()
    has_s1 = len(aot.sql("SHOW TABLES IN research LIKE 's1_revert'")) > 0

    if has_s1:
        source_table = "s1_revert"
        s1_cols = {r["Field"] for r in aot.sql("SHOW COLUMNS FROM research.s1_revert")}
        if "page_id" in s1_cols:
            revert_src_sql = """
                SELECT reverting_rev_id, MIN(page_id) AS page_id
                FROM research.s1_revert
                GROUP BY reverting_rev_id
            """
        else:
            revert_src_sql = """
                SELECT s1.reverting_rev_id, rev.page_id
                FROM (SELECT DISTINCT reverting_rev_id FROM research.s1_revert) s1
                JOIN corpus.revision rev ON rev.rev_id = s1.reverting_rev_id
            """
    else:
        source_table = "prof_rev_reverts"
        revert_src_sql = """
            SELECT r_rev_id AS reverting_rev_id, MIN(page_id) AS page_id
            FROM research.prof_rev_reverts
            GROUP BY r_rev_id
        """

    aot.sql("DROP TABLE IF EXISTS research.s5_revert_reason_tmp", timeout=60)
    aot.sql(
        """
        CREATE TABLE research.s5_revert_reason_tmp (
            reverting_rev_id INT UNSIGNED NOT NULL,
            page_id INT UNSIGNED NOT NULL,
            removal_reasons VARCHAR(255) NOT NULL,
            auto_family VARCHAR(32) NULL,
            source_table VARCHAR(32) NOT NULL,
            PRIMARY KEY (reverting_rev_id),
            KEY idx_page_id (page_id),
            KEY idx_auto_family (auto_family)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
        """,
        timeout=60,
    )

    aot.sql(
        f"""
        INSERT INTO research.s5_revert_reason_tmp
            (reverting_rev_id, page_id, removal_reasons, auto_family, source_table)
        SELECT
            src.reverting_rev_id,
            src.page_id,
            COALESCE(s.reasons, ''),
            s.auto_family,
            {aot.quote(source_table)}
        FROM ({revert_src_sql}) src
        LEFT JOIN research.s5_summary s
            ON s.rev_id = src.reverting_rev_id
        """,
        timeout=85,
    )

    aot.sql("DROP TABLE IF EXISTS research.s5_revert_reason", timeout=60)
    aot.sql("RENAME TABLE research.s5_revert_reason_tmp TO research.s5_revert_reason", timeout=60)

    cnt = int(aot.sql("SELECT COUNT(*) AS n FROM research.s5_revert_reason")[0]["n"])
    elapsed = round(time.time() - t0, 2)
    print(
        f"[s5_revert_reason] complete: {cnt} rows joined from research.{source_table} in {elapsed}s",
        file=sys.stderr,
    )
    return {"source_table": source_table, "s5_revert_reason_rows": cnt, "elapsed_s": elapsed}


def collect_stats() -> dict[str, object]:
    """Query aggregate statistics over `s5_summary` and `s5_revert_reason` for README reporting."""
    summary_overview = aot.sql(
        """
        SELECT
            COUNT(*) AS total_rows,
            SUM(auto_family IS NOT NULL) AS auto_rows,
            SUM(has_reason_text = 1) AS has_reason_text_rows,
            SUM(reasons <> '') AS has_reasons_rows,
            SUM(insert_flags <> '') AS has_insert_flags_rows
        FROM research.s5_summary
        """
    )[0]

    revert_overview = aot.sql(
        """
        SELECT
            rr.source_table,
            COUNT(*) AS total_reverts,
            SUM(s.rev_id IS NOT NULL) AS non_empty_comment_reverts,
            SUM(COALESCE(s.has_reason_text, 0) = 1) AS has_reason_text_reverts,
            SUM(rr.removal_reasons <> '') AS with_reason_category,
            SUM(rr.auto_family IS NOT NULL) AS with_auto_family
        FROM research.s5_revert_reason rr
        LEFT JOIN research.s5_summary s ON s.rev_id = rr.reverting_rev_id
        GROUP BY rr.source_table
        """
    )[0]

    by_family = aot.sql(
        """
        SELECT
            COALESCE(rr.auto_family, '(manual_or_empty)') AS family,
            COUNT(*) AS reverts,
            SUM(COALESCE(s.has_reason_text, 0) = 1) AS with_reason_text,
            SUM(rr.removal_reasons <> '') AS with_reason_category
        FROM research.s5_revert_reason rr
        LEFT JOIN research.s5_summary s ON s.rev_id = rr.reverting_rev_id
        GROUP BY COALESCE(rr.auto_family, '(manual_or_empty)')
        ORDER BY reverts DESC
        """
    )

    reason_rows = aot.sql(
        "SELECT removal_reasons FROM research.s5_revert_reason WHERE removal_reasons <> ''"
    )
    reason_counts = {cat: 0 for cat in lexicon.REASON_CATEGORIES}
    for r in reason_rows:
        for cat in r["removal_reasons"].split(","):
            if cat in reason_counts:
                reason_counts[cat] += 1

    flag_rows = aot.sql(
        "SELECT insert_flags FROM research.s5_summary WHERE insert_flags <> ''"
    )
    flag_counts = {fl: 0 for fl in lexicon.INSERT_FLAGS}
    for r in flag_rows:
        for fl in r["insert_flags"].split(","):
            if fl in flag_counts:
                flag_counts[fl] += 1

    return {
        "s5_summary": summary_overview,
        "s5_revert_reason": revert_overview,
        "by_auto_family": by_family,
        "revert_reason_distribution": dict(
            sorted(reason_counts.items(), key=lambda kv: kv[1], reverse=True)
        ),
        "insert_flag_distribution_nonempty_comments": dict(
            sorted(flag_counts.items(), key=lambda kv: kv[1], reverse=True)
        ),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="Build P1.S5 summary tables in research.*")
    ap.add_argument(
        "--join-only",
        action="store_true",
        help="Rebuild only research.s5_revert_reason against s1_revert / prof_rev_reverts.",
    )
    ap.add_argument(
        "--stats",
        action="store_true",
        help="Print JSON statistics from the built research.s5_* tables without rebuilding.",
    )
    args = ap.parse_args()

    if args.stats:
        print(json.dumps(collect_stats(), indent=2, ensure_ascii=False))
        return 0

    result: dict[str, object] = {}
    if not args.join_only:
        result["summary_build"] = build_s5_summary()
    result["join_build"] = build_s5_revert_reason()
    result["stats"] = collect_stats()
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
