#!/usr/bin/env python3
"""P1.S1 (#28) class_v2: apply the independent review's 4 rule fixes as new columns
`class_v2` and `exclude_reason_v2` on `research.s1_revert` without touching `class` or `exclude_reason`.

Rules implemented:
  1. `dispute` -> keep only if (a) at least one reverted author is a named non-throwaway account
     with >= 5 corpus edits (`research.s6_editor_profile`) OR `reinserted_rev_id` is set by such an
     account, AND (b) `wikidiff` between `restored_rev_id` and `last_reverted_rev_id` has
     `stats.is_substantive` and `added_prose_chars + removed_prose_chars >= 30`.
     Otherwise -> `graffiti_cleanup` if all reverted authors are anon/throwaway (<5 edits or throwaway=1)
     and `seconds_to_revert < 86400`, else `other`.
  2. `excluded`: `blanking_collision` only when `r_bytes = 0`, or (`restored_bytes <= 100` AND
     > 365 days between restored revision and R). `wide_span` only when `reverted_count > 15` AND
     (`seconds_to_revert > 30 days` OR >= 3 distinct non-bot reverted users). Freed rows enter the
     normal cascade (`bot -> self_revert -> edit_war -> dispute -> graffiti_cleanup -> other`).
  3. `bot` reverter whose diff removes only `interwiki` segments (no other segments) -> `excluded`,
     `exclude_reason_v2 = 'interwiki_migration'`.
  4. `edit_war` via "span contains a revert": count only when that inner revert was made by one of R's
     reverted users (`ir.reverting_user <> r.reverting_user`) within 7 days of R, AND at least one of
     R's reverted authors is a named non-throwaway account; else fall through.

Usage:
    python3 research/28-s1-revert/class_v2.py
    python3 research/28-s1-revert/class_v2.py --check-only
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

CACHE_DIR = (
    pathlib.Path(os.environ.get("AOT_CACHE_DIR", pathlib.Path.home() / ".cache" / "aot-research"))
    / "s1-class-v2"
)
DIFF_CACHE_PATH = CACHE_DIR / "diff_outcomes.json"

CLASSES = [
    "excluded",
    "bot",
    "self_revert",
    "edit_war",
    "dispute",
    "graffiti_cleanup",
    "other",
]

EXPECTED_VERDICT_BY_CLASS: dict[str, str] = {
    "excluded": "collision_artifact",
    "bot": "bot_maintenance",
    "self_revert": "self_correction",
    "edit_war": "edit_war",
    "dispute": "content_dispute",
    "graffiti_cleanup": "graffiti",
    "other": "good_faith_fix",
}

STAGING_TABLES = (
    "research.s1_v2__seq",
    "research.s1_v2__base",
    "research.s1_v2__edges",
    "research.s1_v2__war48",
    "research.s1_v2__stage",
    "research.s1_v2__result",
)

NL = chr(10)
BS = chr(92)


def snapshot_v1_signature() -> dict[str, int]:
    """Capture a checksum of (`reverting_rev_id`, `class`, `exclude_reason`) to prove v1 is untouched."""
    row = aot.sql("""
        SET SESSION max_statement_time = 85;
        SELECT
          COUNT(*) AS row_count,
          COALESCE(BIT_XOR(CRC32(CONCAT_WS('|', reverting_rev_id, class, COALESCE(exclude_reason, '__NULL__')))), 0) AS xor_crc,
          COALESCE(SUM(CRC32(CONCAT_WS('|', reverting_rev_id, class, COALESCE(exclude_reason, '__NULL__')))), 0) AS sum_crc
        FROM research.s1_revert;
    """)[0]
    return {k: int(v) for k, v in row.items()}


def ensure_v2_columns() -> None:
    """Idempotently add `class_v2`, `exclude_reason_v2`, and `(page_id, class_v2)` index on `research.s1_revert`."""
    cols = {r["Field"] for r in aot.sql("SHOW COLUMNS FROM research.s1_revert")}
    idxs = {r["Key_name"] for r in aot.sql("SHOW INDEX FROM research.s1_revert")}

    alter_clauses: list[str] = []
    if "class_v2" not in cols:
        alter_clauses.append("ADD COLUMN class_v2 VARCHAR(20) CHARACTER SET ascii DEFAULT NULL")
    if "exclude_reason_v2" not in cols:
        alter_clauses.append("ADD COLUMN exclude_reason_v2 VARCHAR(24) CHARACTER SET ascii DEFAULT NULL")
    if "idx_page_class_v2" not in idxs:
        alter_clauses.append("ADD INDEX idx_page_class_v2 (page_id, class_v2)")

    if alter_clauses:
        sql_stmt = f"SET SESSION max_statement_time = 85;{NL}ALTER TABLE research.s1_revert " + ", ".join(alter_clauses) + ";"
        aot.log(f"[class_v2] altering research.s1_revert: {', '.join(alter_clauses)}")
        aot.sql(sql_stmt, timeout=90)


def drop_staging_tables() -> None:
    aot.sql(
        f"SET SESSION max_statement_time = 85;{NL}DROP TABLE IF EXISTS " + ", ".join(STAGING_TABLES) + ";",
        timeout=90,
    )


def build_sql_stage() -> None:
    """Build non-diff v2 cascade flags across all 46,049 reverts in `research.s1_v2__stage`."""
    steps: list[tuple[str, str]] = [
        (
            "1. Drop leftover staging tables",
            f"SET SESSION max_statement_time = 85;{NL}DROP TABLE IF EXISTS " + ", ".join(STAGING_TABLES) + ";",
        ),
        (
            "2. Build non-null revision sequences for revert-bearing pages",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_v2__seq (
              page_id INT UNSIGNED NOT NULL,
              seq INT UNSIGNED NOT NULL,
              rev_id INT UNSIGNED NOT NULL,
              ts DATETIME NOT NULL,
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              anonymous TINYINT(1) NOT NULL,
              bytes INT UNSIGNED NOT NULL,
              PRIMARY KEY (page_id, seq),
              KEY idx_page_rev (page_id, rev_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH rev_pages AS (
              SELECT DISTINCT page_id FROM research.s1_revert
            ),
            raw_ord AS (
              SELECT r.page_id, r.rev_id, r.ts, r.user, r.anonymous, r.bytes, r.sha1,
                     LAG(r.sha1) OVER (PARTITION BY r.page_id ORDER BY r.ts, r.rev_id) AS imm_prev_sha1
              FROM corpus.revision r
              JOIN rev_pages rp USING (page_id)
            )
            SELECT page_id,
                   CAST(ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS UNSIGNED) AS seq,
                   rev_id,
                   ts,
                   user,
                   anonymous,
                   bytes
            FROM raw_ord
            WHERE NOT (sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1);
            """,
        ),
        (
            "3. Aggregate Rule 2 exclusions and S6 editor-profile span flags per revert",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_v2__base (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              p_seq INT UNSIGNED NOT NULL,
              r_seq INT UNSIGNED NOT NULL,
              restored_rev_id INT UNSIGNED NOT NULL,
              last_reverted_rev_id INT UNSIGNED NOT NULL,
              reverting_user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              revert_ts DATETIME NOT NULL,
              seconds_to_revert BIGINT NOT NULL,
              excl_rule2 VARCHAR(24) CHARACTER SET ascii DEFAULT NULL,
              r_is_bot TINYINT(1) NOT NULL,
              is_bot TINYINT(1) NOT NULL,
              is_self TINYINT(1) NOT NULL,
              has_named_non_throwaway_rev_author TINYINT(1) NOT NULL,
              has_est_rev_author TINYINT(1) NOT NULL,
              has_est_reinserter TINYINT(1) NOT NULL,
              all_rev_authors_anon_or_throwaway TINYINT(1) NOT NULL,
              KEY idx_page_rseq (page_id, r_seq)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            SELECT
              rv.reverting_rev_id,
              rv.page_id,
              sp.seq AS p_seq,
              sr.seq AS r_seq,
              rv.restored_rev_id,
              rv.last_reverted_rev_id,
              rv.reverting_user,
              rv.revert_ts,
              rv.seconds_to_revert,
              CASE
                WHEN rv.r_bytes = 0
                  OR (sp.bytes <= 100 AND TIMESTAMPDIFF(SECOND, sp.ts, sr.ts) > 365 * 86400)
                  THEN 'blanking_collision'
                WHEN rv.reverted_count > 15 AND (
                  rv.seconds_to_revert > 30 * 86400
                  OR COUNT(DISTINCT CASE
                       WHEN NOT (rv.reverting_user IS NOT NULL AND se.user = rv.reverting_user) AND ep_e.is_bot = 0
                       THEN COALESCE(se.user, CONCAT('__null_rev_', se.rev_id))
                     END) >= 3
                )
                  THEN 'wide_span'
                ELSE NULL
              END AS excl_rule2,
              ep_r.is_bot AS r_is_bot,
              IF(ep_r.is_bot = 1 OR MIN(ep_e.is_bot) = 1, 1, 0) AS is_bot,
              IF(rv.reverting_user IS NOT NULL AND MIN(IF(se.user IS NOT NULL AND se.user = rv.reverting_user, 1, 0)) = 1, 1, 0) AS is_self,
              MAX(IF(
                NOT (rv.reverting_user IS NOT NULL AND se.user = rv.reverting_user)
                AND ep_e.kind = 'named' AND ep_e.throwaway = 0,
                1, 0
              )) AS has_named_non_throwaway_rev_author,
              MAX(IF(
                NOT (rv.reverting_user IS NOT NULL AND se.user = rv.reverting_user)
                AND ep_e.kind = 'named' AND ep_e.throwaway = 0 AND ep_e.edits >= 5,
                1, 0
              )) AS has_est_rev_author,
              IF(ep_reins.kind = 'named' AND ep_reins.throwaway = 0 AND ep_reins.edits >= 5, 1, 0) AS has_est_reinserter,
              MIN(CASE
                WHEN rv.reverting_user IS NOT NULL AND se.user = rv.reverting_user THEN 1
                WHEN ep_e.kind IN ('ipv4', 'ipv6', 'temporary') THEN 1
                WHEN ep_e.kind = 'named' AND (ep_e.throwaway = 1 OR ep_e.edits < 5) THEN 1
                ELSE 0
              END) AS all_rev_authors_anon_or_throwaway
            FROM research.s1_revert rv
            JOIN research.s1_v2__seq sp ON sp.page_id = rv.page_id AND sp.rev_id = rv.restored_rev_id
            JOIN research.s1_v2__seq sr ON sr.page_id = rv.page_id AND sr.rev_id = rv.reverting_rev_id
            JOIN research.s1_v2__seq se ON se.page_id = rv.page_id AND se.seq > sp.seq AND se.seq < sr.seq
            JOIN research.s6_editor_profile ep_r ON ep_r.user = COALESCE(rv.reverting_user, '') COLLATE utf8mb4_bin
            JOIN research.s6_editor_profile ep_e ON ep_e.user = COALESCE(se.user, '') COLLATE utf8mb4_bin
            LEFT JOIN research.s1_v2__seq sreins ON sreins.page_id = rv.page_id AND sreins.rev_id = rv.reinserted_rev_id
            LEFT JOIN research.s6_editor_profile ep_reins
              ON ep_reins.user = COALESCE(sreins.user, '') COLLATE utf8mb4_bin
             AND rv.reinserted_rev_id IS NOT NULL
            GROUP BY rv.reverting_rev_id, rv.page_id, sp.seq, sr.seq, rv.restored_rev_id, rv.last_reverted_rev_id,
                     rv.reverting_user, rv.revert_ts, rv.seconds_to_revert, rv.r_bytes, sp.bytes, sp.ts, sr.ts,
                     rv.reverted_count, ep_r.is_bot, ep_reins.kind, ep_reins.throwaway, ep_reins.edits;
            """,
        ),
        (
            "4. Compute 48-hour mutual-revert edit-war pairs (including freed Rule 2 reverts)",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_v2__edges (
              page_id INT UNSIGNED NOT NULL,
              u_lo VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              u_hi VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
              reverting_rev_id INT UNSIGNED NOT NULL,
              revert_ts DATETIME NOT NULL,
              dir TINYINT UNSIGNED NOT NULL,
              PRIMARY KEY (page_id, u_lo(120), u_hi(120), reverting_rev_id, dir),
              KEY idx_pair_ts (page_id, u_lo(120), u_hi(120), revert_ts)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin
            AS
            SELECT DISTINCT
              b.page_id,
              LEAST(b.reverting_user, se.user) AS u_lo,
              GREATEST(b.reverting_user, se.user) AS u_hi,
              b.reverting_rev_id,
              b.revert_ts,
              IF(b.reverting_user < se.user, 1, 2) AS dir
            FROM research.s1_v2__base b
            JOIN research.s1_v2__seq se ON se.page_id = b.page_id AND se.seq > b.p_seq AND se.seq < b.r_seq
            WHERE b.excl_rule2 IS NULL AND b.is_bot = 0 AND b.is_self = 0
              AND b.reverting_user IS NOT NULL AND se.user IS NOT NULL
              AND b.reverting_user <> se.user;

            CREATE TABLE research.s1_v2__war48 (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            WITH valid_windows AS (
              SELECT e1.page_id, e1.u_lo, e1.u_hi, e1.reverting_rev_id AS anchor_rev_id, e1.revert_ts AS anchor_ts
              FROM research.s1_v2__edges e1
              JOIN research.s1_v2__edges e2
                ON e2.page_id = e1.page_id AND e2.u_lo = e1.u_lo AND e2.u_hi = e1.u_hi
               AND e2.revert_ts >= e1.revert_ts
               AND TIMESTAMPDIFF(SECOND, e1.revert_ts, e2.revert_ts) <= 172800
              GROUP BY e1.page_id, e1.u_lo, e1.u_hi, e1.reverting_rev_id, e1.revert_ts
              HAVING COUNT(DISTINCT e2.reverting_rev_id) >= 3 AND COUNT(DISTINCT e2.dir) = 2
            )
            SELECT DISTINCT e2.reverting_rev_id
            FROM valid_windows vw
            JOIN research.s1_v2__edges e2
              ON e2.page_id = vw.page_id AND e2.u_lo = vw.u_lo AND e2.u_hi = vw.u_hi
             AND e2.revert_ts >= vw.anchor_ts
             AND TIMESTAMPDIFF(SECOND, vw.anchor_ts, e2.revert_ts) <= 172800;
            """,
        ),
        (
            "5. Build pre-diff stage table with Rule 4 inner-revert edit-war evaluation",
            """
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s1_v2__stage (
              reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              restored_rev_id INT UNSIGNED NOT NULL,
              last_reverted_rev_id INT UNSIGNED NOT NULL,
              seconds_to_revert BIGINT NOT NULL,
              excl_rule2 VARCHAR(24) CHARACTER SET ascii DEFAULT NULL,
              r_is_bot TINYINT(1) NOT NULL,
              is_bot TINYINT(1) NOT NULL,
              is_self TINYINT(1) NOT NULL,
              is_edit_war_v2 TINYINT(1) NOT NULL,
              passes_dispute_author TINYINT(1) NOT NULL,
              all_rev_authors_anon_or_throwaway TINYINT(1) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            SELECT
              b.reverting_rev_id,
              b.page_id,
              b.restored_rev_id,
              b.last_reverted_rev_id,
              b.seconds_to_revert,
              b.excl_rule2,
              b.r_is_bot,
              b.is_bot,
              b.is_self,
              IF(
                wp.reverting_rev_id IS NOT NULL
                OR (
                  b.has_named_non_throwaway_rev_author = 1
                  AND EXISTS (
                    SELECT 1 FROM research.s1_v2__base ir
                    WHERE ir.page_id = b.page_id
                      AND ir.r_seq > b.p_seq AND ir.r_seq < b.r_seq
                      AND ir.excl_rule2 IS NULL AND ir.is_bot = 0 AND ir.is_self = 0
                      AND ir.reverting_user IS NOT NULL
                      AND NOT (b.reverting_user IS NOT NULL AND ir.reverting_user = b.reverting_user)
                      AND TIMESTAMPDIFF(SECOND, ir.revert_ts, b.revert_ts) <= 7 * 86400
                  )
                ),
                1, 0
              ) AS is_edit_war_v2,
              IF(b.has_est_rev_author = 1 OR b.has_est_reinserter = 1, 1, 0) AS passes_dispute_author,
              b.all_rev_authors_anon_or_throwaway
            FROM research.s1_v2__base b
            LEFT JOIN research.s1_v2__war48 wp USING (reverting_rev_id);
            """,
        ),
    ]

    for step_name, query in steps:
        t0 = time.monotonic()
        aot.sql(query, timeout=90)
        elapsed = time.monotonic() - t0
        aot.log(f"[class_v2] {step_name}: {elapsed:.2f} s")


def fetch_texts_throttled(pairs: list[tuple[int, int]]) -> dict[int, str | None]:
    """Fetch `(page_id, rev_id)` texts via `aot.rev_texts` in PK batches of 20 with `time.sleep(0.2)`
    between uncached DB batches (resumable via `~/.cache/aot-research/revtext`).
    """
    unique_pairs = list(dict.fromkeys((int(p), int(r)) for p, r in pairs))
    texts: dict[int, str | None] = {}
    db_batches = 0
    t0 = time.monotonic()
    total_batches = (len(unique_pairs) + 19) // 20

    for idx in range(0, len(unique_pairs), 20):
        chunk = unique_pairs[idx : idx + 20]
        prev_queries = aot.STATS["sql_queries"]
        chunk_out = aot.rev_texts(chunk)
        texts.update(chunk_out)
        if aot.STATS["sql_queries"] > prev_queries:
            db_batches += 1
            if db_batches % 50 == 0:
                elapsed = time.monotonic() - t0
                aot.log(
                    f"[class_v2] fetched {idx + len(chunk)}/{len(unique_pairs)} texts "
                    f"({db_batches} DB batches, {elapsed:.1f} s)"
                )
            if idx + 20 < len(unique_pairs):
                time.sleep(0.2)

    aot.log(
        f"[class_v2] loaded {len(texts)} revision texts ({db_batches}/{total_batches} uncached DB batches "
        f"in {time.monotonic() - t0:.1f} s)"
    )
    return texts


def load_diff_cache() -> dict[str, dict]:
    if DIFF_CACHE_PATH.exists():
        try:
            return json.loads(DIFF_CACHE_PATH.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return {}
    return {}


def save_diff_cache(cache: dict[str, dict]) -> None:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    tmp_path = DIFF_CACHE_PATH.with_suffix(".tmp")
    tmp_path.write_text(json.dumps(cache, ensure_ascii=False, sort_keys=True) + NL, encoding="utf-8")
    tmp_path.replace(DIFF_CACHE_PATH)


def evaluate_diffs(stage_rows: list[dict]) -> dict[str, dict]:
    """Evaluate `wikidiff` in chunks of 200 for Rule 3 (`bot` reverters) and Rule 1b (`dispute` candidates)."""
    cache = load_diff_cache()

    # Determine which rows need diffs and are not yet in `diff_outcomes.json`
    tasks: list[tuple[str, str, int, int, int]] = []  # (key, mode, page_id, old_rev_id, new_rev_id)
    for r in stage_rows:
        if r["excl_rule2"] is not None:
            continue
        pid = int(r["page_id"])
        rest_id = int(r["restored_rev_id"])
        last_id = int(r["last_reverted_rev_id"])
        is_bot = int(r["is_bot"]) == 1
        r_is_bot = int(r["r_is_bot"]) == 1
        is_self = int(r["is_self"]) == 1
        is_ew = int(r["is_edit_war_v2"]) == 1
        passes_author = int(r["passes_dispute_author"]) == 1

        if is_bot and r_is_bot:
            # Reverter R's own diff is `last_reverted -> reverting` (= `restored` by SHA-1 identity).
            key = f"bot:{last_id}:{rest_id}"
            if key not in cache:
                tasks.append((key, "bot", pid, last_id, rest_id))
        elif not is_bot and not is_self and not is_ew and passes_author:
            # Span diff between `restored_rev_id` and `last_reverted_rev_id`.
            key = f"disp:{rest_id}:{last_id}"
            if key not in cache:
                tasks.append((key, "disp", pid, rest_id, last_id))

    if not tasks:
        aot.log(f"[class_v2] all required diff outcomes already cached ({len(cache)} entries)")
        return cache

    # De-duplicate tasks by key
    dedup_tasks: list[tuple[str, str, int, int, int]] = []
    seen_keys: set[str] = set()
    for t in tasks:
        if t[0] not in seen_keys:
            seen_keys.add(t[0])
            dedup_tasks.append(t)

    text_pairs: list[tuple[int, int]] = []
    for _, _, pid, old_id, new_id in dedup_tasks:
        text_pairs.append((pid, old_id))
        text_pairs.append((pid, new_id))

    texts = fetch_texts_throttled(text_pairs)

    t0 = time.monotonic()
    total_chunks = (len(dedup_tasks) + 199) // 200
    for c_idx in range(0, len(dedup_tasks), 200):
        chunk = dedup_tasks[c_idx : c_idx + 200]
        batch_lines: list[str] = []
        mode_by_key: dict[str, str] = {}
        for key, mode, _, old_id, new_id in chunk:
            mode_by_key[key] = mode
            batch_lines.append(
                json.dumps(
                    {"id": key, "old": texts.get(old_id), "new": texts.get(new_id)},
                    ensure_ascii=False,
                )
            )

        proc = subprocess.run(
            [str(REPO / "scripts/wikidiff.sh"), "batch"],
            input=NL.join(batch_lines) + NL,
            capture_output=True,
            text=True,
            cwd=str(REPO),
            check=True,
        )

        for line in proc.stdout.splitlines():
            if not line.strip():
                continue
            d = json.loads(line)
            key = str(d["id"])
            mode = mode_by_key[key]
            if "error" in d:
                cache[key] = {"error": True}
            elif mode == "bot":
                segs = d.get("segments") or []
                only_iw = bool(segs) and all(
                    s.get("op") == "removed" and s.get("kind") == "interwiki" and not s.get("moved")
                    for s in segs
                )
                cache[key] = {"only_removes_interwiki": only_iw}
            else:
                st = d.get("stats") or {}
                prose_chars = int(st.get("added_prose_chars", 0)) + int(st.get("removed_prose_chars", 0))
                cache[key] = {
                    "is_substantive": bool(st.get("is_substantive")),
                    "prose_chars": prose_chars,
                }

        save_diff_cache(cache)
        chunk_num = c_idx // 200 + 1
        if chunk_num % 5 == 0 or chunk_num == total_chunks:
            aot.log(
                f"[class_v2] wikidiff chunk {chunk_num}/{total_chunks} done "
                f"({c_idx + len(chunk)}/{len(dedup_tasks)} pairs, {time.monotonic() - t0:.1f} s)"
            )

    return cache


def classify_all(stage_rows: list[dict], diff_cache: dict[str, dict]) -> list[tuple[int, str, str | None]]:
    """Apply the full v2 cascade to all 46,049 stage rows."""
    results: list[tuple[int, str, str | None]] = []
    for r in stage_rows:
        rid = int(r["reverting_rev_id"])
        rest_id = int(r["restored_rev_id"])
        last_id = int(r["last_reverted_rev_id"])
        sec = int(r["seconds_to_revert"])
        excl_r2 = r["excl_rule2"]
        is_bot = int(r["is_bot"]) == 1
        r_is_bot = int(r["r_is_bot"]) == 1
        is_self = int(r["is_self"]) == 1
        is_ew = int(r["is_edit_war_v2"]) == 1
        passes_author = int(r["passes_dispute_author"]) == 1
        all_anon_or_th = int(r["all_rev_authors_anon_or_throwaway"]) == 1

        if excl_r2 is not None:
            results.append((rid, "excluded", excl_r2))
            continue

        if is_bot:
            if r_is_bot:
                bot_info = diff_cache.get(f"bot:{last_id}:{rest_id}", {})
                if bot_info.get("only_removes_interwiki") is True:
                    results.append((rid, "excluded", "interwiki_migration"))
                    continue
            results.append((rid, "bot", None))
            continue

        if is_self:
            results.append((rid, "self_revert", None))
            continue

        if is_ew:
            results.append((rid, "edit_war", None))
            continue

        if passes_author:
            disp_info = diff_cache.get(f"disp:{rest_id}:{last_id}", {})
            if disp_info.get("is_substantive") is True and int(disp_info.get("prose_chars", 0)) >= 30:
                results.append((rid, "dispute", None))
                continue

        if all_anon_or_th and sec < 86400:
            results.append((rid, "graffiti_cleanup", None))
        else:
            results.append((rid, "other", None))

    return results


def write_results_to_s1_revert(results: list[tuple[int, str, str | None]]) -> None:
    """Bulk-load `(reverting_rev_id, class_v2, exclude_reason_v2)` and PK-join UPDATE `research.s1_revert`."""
    t0 = time.monotonic()
    aot.sql("""
        SET SESSION max_statement_time = 85;
        DROP TABLE IF EXISTS research.s1_v2__result;
        CREATE TABLE research.s1_v2__result (
          reverting_rev_id INT UNSIGNED NOT NULL PRIMARY KEY,
          class_v2 VARCHAR(20) CHARACTER SET ascii NOT NULL,
          exclude_reason_v2 VARCHAR(24) CHARACTER SET ascii DEFAULT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    """, timeout=90)

    for i in range(0, len(results), 2000):
        chunk = results[i : i + 2000]
        vals = ",".join(
            f"({rid},{aot.quote(c2)},{'NULL' if ex2 is None else aot.quote(ex2)})"
            for rid, c2, ex2 in chunk
        )
        aot.sql(
            f"SET SESSION max_statement_time = 85;{NL}"
            f"INSERT INTO research.s1_v2__result (reverting_rev_id, class_v2, exclude_reason_v2) VALUES {vals};",
            timeout=90,
        )

    aot.sql("""
        SET SESSION max_statement_time = 85;
        UPDATE research.s1_revert rv
        JOIN research.s1_v2__result v2 USING (reverting_rev_id)
        SET rv.class_v2 = v2.class_v2,
            rv.exclude_reason_v2 = v2.exclude_reason_v2;
    """, timeout=90)
    aot.log(f"[class_v2] updated {len(results)} rows in research.s1_revert in {time.monotonic() - t0:.2f} s")


def verify_v2_invariants(before_sig: dict[str, int]) -> None:
    after_sig = snapshot_v1_signature()
    if after_sig != before_sig:
        raise RuntimeError(f"v1 signature changed! before={before_sig} vs after={after_sig}")

    inv = aot.sql("""
        SET SESSION max_statement_time = 85;
        SELECT
          SUM(class_v2 IS NULL) AS null_class_v2,
          SUM((class_v2 = 'excluded') <> (exclude_reason_v2 IS NOT NULL)) AS bad_excl_pairing
        FROM research.s1_revert;
    """)[0]
    assert int(inv["null_class_v2"]) == 0, f"Found NULL class_v2 rows: {inv}"
    assert int(inv["bad_excl_pairing"]) == 0, f"exclude_reason_v2 pairing mismatch: {inv}"
    aot.log(f"[class_v2] verified v1 columns untouched ({after_sig}) and 0 NULL class_v2 rows")


def print_check_report() -> None:
    """Print corpus-wide v1 -> v2 transition table, exclusion breakdown, and 140-row review agreement."""
    trans_rows = aot.sql("""
        SET SESSION max_statement_time = 85;
        SELECT class AS c1, class_v2 AS c2, COUNT(*) AS cnt
        FROM research.s1_revert
        GROUP BY class, class_v2;
    """)
    mat: dict[tuple[str, str], int] = {(r["c1"], r["c2"]): int(r["cnt"]) for r in trans_rows}

    print("## Corpus-wide Transition Table (`class` v1 → `class_v2`)" + NL)
    header = f"| v1 {BS} v2 | " + " | ".join(f"`{c}`" for c in CLASSES) + " | v1 Total |"
    sep = "|---|" + "|".join("---:" for _ in CLASSES) + "|---:|"
    print(header)
    print(sep)
    col_totals = {c: 0 for c in CLASSES}
    grand = 0
    for c1 in CLASSES:
        row_tot = 0
        cells: list[str] = []
        for c2 in CLASSES:
            v = mat.get((c1, c2), 0)
            row_tot += v
            col_totals[c2] += v
            grand += v
            cells.append(f"**{v}**" if c1 == c2 else str(v))
        print(f"| `{c1}` | " + " | ".join(cells) + f" | **{row_tot}** |")
    print("| **v2 Total** | " + " | ".join(f"**{col_totals[c]}**" for c in CLASSES) + f" | **{grand}** |" + NL)

    excl_rows = aot.sql("""
        SET SESSION max_statement_time = 85;
        SELECT COALESCE(exclude_reason, '(none)') AS ex1,
               COALESCE(exclude_reason_v2, '(none)') AS ex2,
               COUNT(*) AS cnt
        FROM research.s1_revert
        WHERE exclude_reason IS NOT NULL OR exclude_reason_v2 IS NOT NULL
        GROUP BY ex1, ex2
        ORDER BY ex1, ex2;
    """)
    print("## Exclusion Reason Breakdown (v1 vs v2)" + NL)
    print("| `exclude_reason` (v1) | `exclude_reason_v2` (v2) | Reverts |")
    print("|---|---|---:|")
    for r in excl_rows:
        print(f"| `{r['ex1']}` | `{r['ex2']}` | {r['cnt']} |")
    print()

    # Re-score the 140 reviewed rows from review/verdicts.jsonl
    verdicts_path = HERE / "review" / "verdicts.jsonl"
    verdict_recs = [
        json.loads(line)
        for line in verdicts_path.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    rids = [int(rec["reverting_rev_id"]) for rec in verdict_recs]
    db_rows = {
        int(r["reverting_rev_id"]): r
        for r in aot.sql(
            f"SELECT reverting_rev_id, class, class_v2, exclude_reason_v2 "
            f"FROM research.s1_revert WHERE reverting_rev_id IN ({aot.ints(rids)})"
        )
    }

    print("## 140 Reviewed Reverts: v1 vs `class_v2` Agreement" + NL)
    print("### A. By Predicted Class Bucket (Precision per Class)" + NL)
    print("| Class | Expected verdict | v1 n | v1 agrees | v1 rate | v2 n | v2 agrees | v2 rate |")
    print("|---|---|---:|---:|---:|---:|---:|---:|")
    tot_v1_ag = 0
    tot_v2_ag = 0
    for cls in CLASSES:
        exp = EXPECTED_VERDICT_BY_CLASS[cls]
        v1_sub = [rec for rec in verdict_recs if db_rows[int(rec["reverting_rev_id"])]["class"] == cls]
        v2_sub = [rec for rec in verdict_recs if db_rows[int(rec["reverting_rev_id"])]["class_v2"] == cls]
        ag1 = sum(1 for rec in v1_sub if rec["verdict"] == exp)
        ag2 = sum(1 for rec in v2_sub if rec["verdict"] == exp)
        tot_v1_ag += ag1
        tot_v2_ag += ag2
        r1 = f"{100.0 * ag1 / len(v1_sub):.1f}%" if v1_sub else "—"
        r2 = f"{100.0 * ag2 / len(v2_sub):.1f}%" if v2_sub else "—"
        print(f"| `{cls}` | `{exp}` | {len(v1_sub)} | {ag1} | {r1} | {len(v2_sub)} | {ag2} | {r2} |")
    n_all = len(verdict_recs)
    print(
        f"| **Total** | — | **{n_all}** | **{tot_v1_ag}** | **{100.0 * tot_v1_ag / n_all:.1f}%** "
        f"| **{n_all}** | **{tot_v2_ag}** | **{100.0 * tot_v2_ag / n_all:.1f}%** |" + NL
    )

    print("### B. By Original v1 Sampled Stratum (20 per v1 Class)" + NL)
    print("| Sampled v1 stratum | n | v1 agrees | v2 agrees | Net delta |")
    print("|---|---:|---:|---:|---:|")
    for cls in CLASSES:
        sub = [rec for rec in verdict_recs if rec["s1_class"] == cls]
        ag1 = sum(1 for rec in sub if rec["verdict"] == EXPECTED_VERDICT_BY_CLASS[rec["s1_class"]])
        ag2 = sum(
            1
            for rec in sub
            if rec["verdict"] == EXPECTED_VERDICT_BY_CLASS[db_rows[int(rec["reverting_rev_id"])]["class_v2"]]
        )
        print(f"| `{cls}` | {len(sub)} | {ag1} / {len(sub)} | {ag2} / {len(sub)} | {ag2 - ag1:+d} |")
    print(f"| **Total** | **{n_all}** | **{tot_v1_ag} / {n_all}** | **{tot_v2_ag} / {n_all}** | **{tot_v2_ag - tot_v1_ag:+d}** |")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true", help="Print transition and review tables from existing class_v2")
    args = parser.parse_args()

    if args.check_only:
        print_check_report()
        return

    t_start = time.monotonic()
    before_sig = snapshot_v1_signature()
    ensure_v2_columns()
    try:
        build_sql_stage()
        stage_rows = aot.sql("""
            SET SESSION max_statement_time = 85;
            SELECT reverting_rev_id, page_id, restored_rev_id, last_reverted_rev_id,
                   seconds_to_revert, excl_rule2, r_is_bot, is_bot, is_self,
                   is_edit_war_v2, passes_dispute_author, all_rev_authors_anon_or_throwaway
            FROM research.s1_v2__stage
            ORDER BY reverting_rev_id ASC;
        """, timeout=90)
        diff_cache = evaluate_diffs(stage_rows)
        results = classify_all(stage_rows, diff_cache)
        write_results_to_s1_revert(results)
    finally:
        drop_staging_tables()

    verify_v2_invariants(before_sig)
    aot.log(f"[class_v2] ALL DONE in {time.monotonic() - t_start:.2f} s")
    print_check_report()


if __name__ == "__main__":
    main()
