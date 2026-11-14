#!/usr/bin/env python3
"""P1.S4 (#31): Precompute edit-war, ping-pong, and burst conflict episodes plus per-page
contestedness scores across all 19,742 corpus articles, built on `research.s1_*`.

Re-runnable (drops and recreates `research.s4_episode`, `research.s4_episode_rev`,
`research.s4_page_contest`, and cleans up `research.s4__*` staging tables).
Every SQL step runs sequentially under `SET SESSION max_statement_time = 85;` (< 90 s budget
on the shared 2-vCPU corpus VPS) and writes step timings + invariant checks to
`research/31-s4-conflict/results/build_timings.json`.

Usage:
    python3 research/31-s4-conflict/build.py
"""
from __future__ import annotations

from collections import Counter, defaultdict
from datetime import date, datetime, timedelta
import json
import math
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

# Dataset M ends at 2026-10-01 10:59:12 UTC; require >= 14 full days after a burst to evaluate
# the 14-day post-burst anonymous-edit drop proxy without right-censoring at the dump boundary.
CORPUS_END_MINUS_14D = date(2026, 9, 17)

EDIT_WAR_WINDOWS: list[tuple[str, int]] = [
    ("24h", 86_400),
    ("7d", 7 * 86_400),
    ("30d", 30 * 86_400),
]
DEFAULT_EDIT_WAR_WINDOW = "7d"

# Ping-pong thresholds (documented in README.md)
PING_PONG_MIN_BYTES = 101          # > 100 B (excludes 0 B blankings & <= 100 B redirect stubs, matching S1)
PING_PONG_MAX_GAP_EDITS = 3        # allow up to 3 unrelated non-null edits between recurring states
PING_PONG_MAX_STEP_SECONDS = 7 * 86_400  # max 7 days between consecutive recurring-state transitions
PING_PONG_MIN_HUMAN_USERS = 2      # exclude single-editor self-toggling and bot-only stretches

# Burst thresholds (documented in README.md)
BURST_MIN_HUMAN_EDITS = 10         # >= 10 non-null non-bot edits on calendar day (UTC)
BURST_MIN_HUMAN_EDITORS = 2        # >= 2 distinct non-bot editors (excludes 2,040 solo-drafting days)
BURST_MEDIAN_MULT = 5.0            # > 5x trailing 90-calendar-day median
BURST_SIGMA_MULT = 3.0             # > trailing 90-calendar-day (mean + 3*sigma)


def _run_step(timings: list[dict], step_name: str, fn) -> any:
    t0 = time.monotonic()
    res = fn()
    elapsed = round(time.monotonic() - t0, 2)
    if elapsed >= 90.0:
        raise RuntimeError(f"Step {step_name!r} exceeded 90 s limit ({elapsed:.2f} s)")
    aot.log(f"[s4 build] {step_name}: {elapsed:.2f} s")
    timings.append({"step": step_name, "seconds": elapsed})
    return res


def _detect_edit_wars(reverts_by_page: dict[int, list[dict]]) -> list[dict]:
    """Detect sliding-window edit_war episodes for 24h, 7d, and 30d windows.

    Eligible reverts: `s1_revert` rows with `class NOT IN ('excluded', 'bot', 'self_revert')`
    where a named non-bot account reverts >= 1 distinct named non-bot account (`A != B`).
    A sliding window `[t0, t0 + W]` qualifies when it contains >= 3 such reverts and at least
    one mutual named pair (`A -> B` and `B -> A` both occur inside the window).
    Overlapping windows (in timestamp or revision sequence) are merged per `(page_id, window)`.
    """
    episodes: list[dict] = []
    for w_name, w_sec in EDIT_WAR_WINDOWS:
        for pid in sorted(reverts_by_page.keys()):
            rlist = reverts_by_page[pid]
            if len(rlist) < 3:
                continue
            intervals: list[list] = []
            n = len(rlist)
            for i in range(n):
                t0 = rlist[i]["ts"]
                j = i
                while j + 1 < n and rlist[j + 1]["ts"] - t0 <= w_sec:
                    j += 1
                sub = rlist[i : j + 1]
                if len(sub) < 3:
                    continue
                edges = {(r["rev_user"], eu) for r in sub for eu in r["ed_named_users"]}
                if any((b, a) in edges for (a, b) in edges):
                    cutoff_ts = sub[0]["ts"] - w_sec
                    min_seq = min(
                        min((s for s, t in r["span_seqs"] if t >= cutoff_ts), default=r["r_seq"])
                        for r in sub
                    )
                    max_seq = max(r["r_seq"] for r in sub)
                    intervals.append([sub[0]["ts"], sub[-1]["ts"], min_seq, max_seq])
            if not intervals:
                continue
            intervals.sort(key=lambda x: (x[0], x[1], x[2], x[3]))
            merged: list[list] = []
            for s_ts, e_ts, s_seq, e_seq in intervals:
                if not merged or (s_ts > merged[-1][1] and s_seq > merged[-1][3]):
                    merged.append([s_ts, e_ts, s_seq, e_seq])
                else:
                    merged[-1][1] = max(merged[-1][1], e_ts)
                    merged[-1][2] = min(merged[-1][2], s_seq)
                    merged[-1][3] = max(merged[-1][3], e_seq)
            for _, _, s_seq, e_seq in merged:
                episodes.append({
                    "page_id": pid,
                    "kind": "edit_war",
                    "window": w_name,
                    "is_default": 1 if w_name == DEFAULT_EDIT_WAR_WINDOW else 0,
                    "start_seq": s_seq,
                    "end_seq": e_seq,
                    "states": None,
                    "state_rev_ids": None,
                    "burst_anon_drop": 0,
                })
    return episodes


def _extract_ping_pong_from_window(wlist: list[dict]) -> list[dict]:
    """Extract maximal interleaved SHA-1 alternation stretches from a candidate revision window."""
    counts = Counter(x["sha1"] for x in wlist if x["sha1"] is not None)
    rec = {h for h, c in counts.items() if c >= 2}
    if len(rec) < 2:
        return []
    pos = [i for i, x in enumerate(wlist) if x["sha1"] in rec]
    if len(pos) < 4:
        return []

    raw_found: list[tuple[int, int, int, list[int]]] = []
    queue = [pos]
    while queue:
        sub = queue.pop(0)
        if len(sub) < 4:
            continue
        split_at = None
        split_k = None
        for k in range(len(sub) - 1):
            i1, i2 = sub[k], sub[k + 1]
            if (i2 - i1 - 1) > PING_PONG_MAX_GAP_EDITS:
                split_at = k + 1
                split_k = k
                break
            if k >= 1 and (wlist[i2]["ts"] - wlist[i1]["ts"]) > PING_PONG_MAX_STEP_SECONDS:
                split_at = k + 1
                split_k = k
                break
            if k == 0 and i2 > i1 + 1 and (wlist[i2]["ts"] - wlist[i1 + 1]["ts"]) > PING_PONG_MAX_STEP_SECONDS:
                split_at = k + 1
                split_k = k
                break

        if split_at is not None and split_k is not None:
            i1, i2 = sub[split_k], sub[split_k + 1]
            # If split occurred on time gap at k >= 1, sub[split_k] may still serve as the pre-state
            # baseline immediately preceding sub[split_k + 1].
            if (
                split_k >= 1
                and (i2 - i1 - 1) <= PING_PONG_MAX_GAP_EDITS
                and (i2 == i1 + 1 or (wlist[i2]["ts"] - wlist[i1 + 1]["ts"]) <= PING_PONG_MAX_STEP_SECONDS)
            ):
                if len(sub[: split_k + 1]) >= 4:
                    queue.append(sub[: split_k + 1])
                if len(sub[split_k:]) >= 4:
                    queue.append(sub[split_k:])
            else:
                if len(sub[:split_at]) >= 4:
                    queue.append(sub[:split_at])
                if len(sub[split_at:]) >= 4:
                    queue.append(sub[split_at:])
            continue

        collapsed: list[str] = []
        for idx in sub:
            h = wlist[idx]["sha1"]
            if not collapsed or collapsed[-1] != h:
                collapsed.append(h)
        alt_counts = Counter(collapsed)
        valid = {h for h, c in alt_counts.items() if c >= 2}
        if len(valid) < 2:
            continue
        filt = [idx for idx in sub if wlist[idx]["sha1"] in valid]
        if len(filt) < len(sub):
            queue.append(filt)
            continue

        # Active edits begin right after the baseline pre-state `filt[0]`.
        act_start = filt[0] + 1
        act_end = filt[-1]
        stretch = wlist[act_start : act_end + 1]
        human_users = {aot.user_display(x["user"]) for x in stretch if x["user"] and not x["bot"]}
        if len(human_users) < PING_PONG_MIN_HUMAN_USERS:
            continue

        # One representative rev_id per alternating state (prefer revisions inside the active stretch).
        rep: dict[str, int] = {}
        for idx in filt[1:] + [filt[0]]:
            h = wlist[idx]["sha1"]
            if h in valid and h not in rep:
                rep[h] = wlist[idx]["rev_id"]
        raw_found.append((wlist[act_start]["seq"], wlist[act_end]["seq"], len(valid), list(rep.values())))

    if not raw_found:
        return []

    # Merge any overlapping stretches on the same page into maximal episodes.
    raw_found.sort(key=lambda x: (x[0], x[1]))
    merged: list[list] = []
    for s_seq, e_seq, n_states, rep_ids in raw_found:
        if not merged or s_seq > merged[-1][1]:
            merged.append([s_seq, e_seq, n_states, list(rep_ids)])
        else:
            merged[-1][1] = max(merged[-1][1], e_seq)
            for rid in rep_ids:
                if rid not in merged[-1][3]:
                    merged[-1][3].append(rid)
            merged[-1][2] = max(merged[-1][2], len(merged[-1][3]))

    pid = wlist[0]["page_id"]
    return [
        {
            "page_id": pid,
            "kind": "ping_pong",
            "window": None,
            "is_default": 1,
            "start_seq": s_seq,
            "end_seq": e_seq,
            "states": n_states,
            "state_rev_ids": ",".join(str(rid) for rid in rep_ids),
            "burst_anon_drop": 0,
        }
        for s_seq, e_seq, n_states, rep_ids in merged
    ]


def _detect_bursts(daily_by_page: dict[int, list[dict]]) -> tuple[list[dict], dict]:
    """Detect daily activity bursts per page and compare 5x-median vs 3-sigma rules."""
    episodes: list[dict] = []
    cmp_stats = {
        "days_5x_median_ge10": 0,
        "days_3sigma_ge10": 0,
        "days_both_ge10": 0,
        "days_5x_median_having_positive_median": 0,
        "episodes_5x_median_ge10": 0,
        "episodes_3sigma_ge10": 0,
    }

    for pid in sorted(daily_by_page.keys()):
        dlist = daily_by_page[pid]
        dmap = {x["day"]: x for x in dlist}
        med_days: list[dict] = []
        sig_days: list[dict] = []

        for x in dlist:
            c = x["human_edits"]
            if c < BURST_MIN_HUMAN_EDITS or x["human_editors"] < BURST_MIN_HUMAN_EDITORS:
                continue
            d = x["day"]
            vals = [
                dmap[d - timedelta(days=k)]["human_edits"] if (d - timedelta(days=k)) in dmap else 0
                for k in range(1, 91)
            ]
            vs = sorted(vals)
            med = 0.5 * (vs[44] + vs[45])
            mean = sum(vals) / 90.0
            sd = math.sqrt(sum((v - mean) ** 2 for v in vals) / 90.0)

            ok_med = c > BURST_MEDIAN_MULT * med
            ok_sig = c > mean + BURST_SIGMA_MULT * sd
            if ok_med:
                cmp_stats["days_5x_median_ge10"] += 1
                if med > 0:
                    cmp_stats["days_5x_median_having_positive_median"] += 1
                med_days.append(x)
            if ok_sig:
                cmp_stats["days_3sigma_ge10"] += 1
                sig_days.append(x)
            if ok_med and ok_sig:
                cmp_stats["days_both_ge10"] += 1

        def _merge_consecutive(xs: list[dict]) -> list[list[dict]]:
            if not xs:
                return []
            out = [[xs[0]]]
            for item in xs[1:]:
                if (item["day"] - out[-1][-1]["day"]).days == 1:
                    out[-1].append(item)
                else:
                    out.append([item])
            return out

        cmp_stats["episodes_5x_median_ge10"] += len(_merge_consecutive(med_days))
        merged_sig = _merge_consecutive(sig_days)
        cmp_stats["episodes_3sigma_ge10"] += len(merged_sig)

        for grp in merged_sig:
            end_d = grp[-1]["day"]
            anon_in = sum(g["anon_edits"] for g in grp)
            anon_next_14d = sum(
                dmap[end_d + timedelta(days=k)]["anon_edits"]
                for k in range(1, 15)
                if (end_d + timedelta(days=k)) in dmap
            )
            named_next_14d = sum(
                dmap[end_d + timedelta(days=k)]["named_edits"]
                for k in range(1, 15)
                if (end_d + timedelta(days=k)) in dmap
            )
            anon_drop = (
                1
                if (end_d <= CORPUS_END_MINUS_14D and anon_in >= 2 and anon_next_14d == 0 and named_next_14d >= 2)
                else 0
            )
            episodes.append({
                "page_id": pid,
                "kind": "burst",
                "window": None,
                "is_default": 1,
                "start_seq": grp[0]["min_seq"],
                "end_seq": grp[-1]["max_seq"],
                "states": None,
                "state_rev_ids": None,
                "burst_anon_drop": anon_drop,
            })

    return episodes, cmp_stats


def verify_invariants() -> dict:
    """Assert acceptance criteria directly on `research.s4_*`."""
    row = aot.sql("""
        SET SESSION max_statement_time = 85;
        WITH raw_nulls AS (
          SELECT page_id, rev_id
          FROM (
            SELECT page_id, rev_id, sha1,
                   LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
            FROM corpus.revision
          ) ro
          WHERE sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1
        )
        SELECT
          (SELECT COUNT(*) FROM research.s4_episode) AS episode_rows,
          (SELECT COUNT(*) FROM research.s4_episode WHERE is_default = 1) AS default_episode_rows,
          (SELECT COUNT(*) FROM research.s4_episode_rev) AS episode_rev_rows,
          (SELECT COUNT(*) FROM research.s4_page_contest) AS page_contest_rows,
          (SELECT COUNT(*)
           FROM research.s4_episode_rev er
           JOIN research.s4_episode e ON e.episode_id = er.episode_id
           LEFT JOIN corpus.revision cr ON cr.page_id = e.page_id AND cr.rev_id = er.rev_id
           WHERE cr.rev_id IS NULL
          ) AS missing_in_corpus_revision,
          (SELECT COUNT(*)
           FROM raw_nulls rn
           JOIN research.s4_episode_rev er ON er.rev_id = rn.rev_id
          ) AS null_revs_in_episode_rev,
          (SELECT COUNT(*)
           FROM research.s4_episode e
           LEFT JOIN (
             SELECT episode_id, COUNT(*) AS cnt FROM research.s4_episode_rev GROUP BY episode_id
           ) rc ON rc.episode_id = e.episode_id
           WHERE COALESCE(rc.cnt, 0) <> e.edit_count OR e.edit_count = 0
          ) AS edit_count_mismatch
    """)[0]
    counts = {k: int(v) for k, v in row.items()}
    assert counts["missing_in_corpus_revision"] == 0, f"orphan rev_id in s4_episode_rev: {counts}"
    assert counts["null_revs_in_episode_rev"] == 0, f"null revision in s4_episode_rev: {counts}"
    assert counts["edit_count_mismatch"] == 0, f"edit_count != s4_episode_rev rows: {counts}"
    assert counts["page_contest_rows"] == 19742, f"expected 19,742 pages in s4_page_contest: {counts}"
    return counts


def main() -> None:
    timings: list[dict] = []
    total_start = time.monotonic()

    # Step 1: Drop staging and target tables
    _run_step(
        timings,
        "1. Drop previous s4 staging & target tables",
        lambda: aot.sql("""
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS
              research.s4__seq,
              research.s4__daily,
              research.s4__pp_win,
              research.s4__ep_spans,
              research.s4__prot_revs,
              research.s4_episode_rev,
              research.s4_episode,
              research.s4_page_contest;
        """),
    )

    # Step 2: Materialize non-null revision sequence per page joined with S1 bot flags
    _run_step(
        timings,
        "2. Materialize non-null revision sequence (s4__seq) with S1 bot flags",
        lambda: aot.sql("""
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s4__seq (
              page_id INT UNSIGNED NOT NULL,
              seq INT UNSIGNED NOT NULL,
              rev_id INT UNSIGNED NOT NULL,
              ts DATETIME NOT NULL,
              day DATE NOT NULL,
              user VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              anonymous TINYINT(1) NOT NULL,
              is_bot TINYINT(1) NOT NULL,
              sha1 CHAR(31) CHARACTER SET ascii DEFAULT NULL,
              bytes INT UNSIGNED NOT NULL,
              PRIMARY KEY (page_id, seq),
              UNIQUE KEY idx_rev (rev_id),
              KEY idx_page_ts (page_id, ts)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH raw_ord AS (
              SELECT page_id, rev_id, ts, user, anonymous, bytes, sha1,
                     LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
              FROM corpus.revision
            )
            SELECT ro.page_id,
                   CAST(ROW_NUMBER() OVER (PARTITION BY ro.page_id ORDER BY ro.ts, ro.rev_id) AS UNSIGNED) AS seq,
                   ro.rev_id,
                   ro.ts,
                   DATE(ro.ts) AS day,
                   ro.user COLLATE utf8mb4_bin AS user,
                   ro.anonymous,
                   COALESCE(us.is_bot, 0) AS is_bot,
                   ro.sha1,
                   ro.bytes
            FROM raw_ord ro
            LEFT JOIN research.s1_user_stats us ON us.user = ro.user COLLATE utf8mb4_bin
            WHERE NOT (ro.sha1 IS NOT NULL AND ro.imm_prev_sha1 IS NOT NULL AND ro.sha1 = ro.imm_prev_sha1);
        """),
    )

    # Step 3: Fetch eligible S1 reverts & detect edit_war episodes across 24h, 7d, 30d
    def step_edit_war() -> list[dict]:
        rows = aot.sql("""
            SET SESSION max_statement_time = 85;
            SELECT r.page_id, r.reverting_rev_id, r.revert_ts, r.reverting_user, r.reverting_anonymous,
                   sr.is_bot AS rev_is_bot, sr.seq AS r_seq, sp.seq AS p_seq,
                   se.seq AS ed_seq, se.ts AS ed_ts, se.user AS ed_user, se.anonymous AS ed_anon, se.is_bot AS ed_is_bot
            FROM research.s1_revert r
            JOIN research.s4__seq sr ON sr.rev_id = r.reverting_rev_id
            JOIN research.s4__seq sp ON sp.rev_id = r.restored_rev_id
            JOIN research.s4__seq se ON se.page_id = r.page_id AND se.seq > sp.seq AND se.seq < sr.seq
            WHERE r.class NOT IN ('excluded', 'bot', 'self_revert')
            ORDER BY r.page_id, r.revert_ts, r.reverting_rev_id
        """)
        by_page_dict: dict[int, dict[int, dict]] = {}
        for row in rows:
            pid = int(row["page_id"])
            rid = int(row["reverting_rev_id"])
            pdict = by_page_dict.setdefault(pid, {})
            if rid not in pdict:
                dt = datetime.strptime(row["revert_ts"], "%Y-%m-%d %H:%M:%S")
                pdict[rid] = {
                    "rid": rid,
                    "r_seq": int(row["r_seq"]),
                    "ts": int(dt.timestamp()),
                    "rev_user": row["reverting_user"],
                    "rev_named": (
                        row["reverting_anonymous"] == "0"
                        and row["reverting_user"] is not None
                        and row["rev_is_bot"] != "1"
                    ),
                    "ed_named_users": set(),
                    "span_seqs": [],
                }
            rec = pdict[rid]
            ed_dt = datetime.strptime(row["ed_ts"], "%Y-%m-%d %H:%M:%S")
            rec["span_seqs"].append((int(row["ed_seq"]), int(ed_dt.timestamp())))
            if (
                row["ed_user"]
                and row["ed_anon"] == "0"
                and row["ed_is_bot"] != "1"
                and row["ed_user"] != rec["rev_user"]
            ):
                rec["ed_named_users"].add(row["ed_user"])

        named_revs_by_page = {
            pid: sorted(
                [r for r in pdict.values() if r["rev_named"] and len(r["ed_named_users"]) > 0],
                key=lambda x: (x["ts"], x["rid"]),
            )
            for pid, pdict in by_page_dict.items()
        }
        return _detect_edit_wars(named_revs_by_page)

    ew_episodes = _run_step(timings, "3. Detect edit_war episodes (24h, 7d, 30d) from s1_revert", step_edit_war)

    # Step 4: Detect ping_pong episodes using interleaved recurring-SHA1 window filtering
    def step_ping_pong() -> list[dict]:
        pair_rows = aot.sql(f"""
            SET SESSION max_statement_time = 85;
            WITH rep_sha AS (
              SELECT page_id, seq, sha1,
                     LAG(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq) AS prev_same_seq
              FROM research.s4__seq
              WHERE sha1 IS NOT NULL AND bytes >= {PING_PONG_MIN_BYTES}
            )
            SELECT page_id, prev_same_seq AS s_lo, seq AS s_hi, sha1
            FROM rep_sha
            WHERE prev_same_seq IS NOT NULL
              AND (seq - prev_same_seq) <= 15
            ORDER BY page_id, s_lo, s_hi
        """)
        win_by_page: dict[int, list[list]] = defaultdict(list)
        for r in pair_rows:
            pid = int(r["page_id"])
            lo, hi, h = int(r["s_lo"]), int(r["s_hi"]), r["sha1"]
            if not win_by_page[pid] or lo > win_by_page[pid][-1][1] + PING_PONG_MAX_GAP_EDITS + 1:
                win_by_page[pid].append([lo, hi, {h: [lo, hi]}])
            else:
                win_by_page[pid][-1][1] = max(win_by_page[pid][-1][1], hi)
                bounds = win_by_page[pid][-1][2].setdefault(h, [lo, hi])
                bounds[0] = min(bounds[0], lo)
                bounds[1] = max(bounds[1], hi)

        # Keep only windows where >= 2 distinct sha1 states have overlapping [first, last] intervals.
        cand_windows: list[tuple[int, int, int]] = []
        for pid, ws in win_by_page.items():
            for lo, hi, hmap in ws:
                if len(hmap) < 2:
                    continue
                ivs = sorted(hmap.values(), key=lambda x: (x[0], x[1]))
                has_interleave = False
                max_end = ivs[0][1]
                for iv_lo, iv_hi in ivs[1:]:
                    if iv_lo < max_end:
                        has_interleave = True
                        break
                    if iv_hi > max_end:
                        max_end = iv_hi
                if has_interleave:
                    cand_windows.append((pid, lo, hi))

        if not cand_windows:
            return []

        vals_sql = ",".join(f"({pid},{lo},{hi})" for pid, lo, hi in cand_windows)
        win_revs = aot.sql(f"""
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS research.s4__pp_win;
            CREATE TABLE research.s4__pp_win (
              win_id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              s_lo INT UNSIGNED NOT NULL,
              s_hi INT UNSIGNED NOT NULL,
              KEY idx_page (page_id, s_lo, s_hi)
            ) ENGINE=InnoDB;
            INSERT INTO research.s4__pp_win (page_id, s_lo, s_hi) VALUES {vals_sql};
            SELECT w.win_id, s.page_id, s.seq, s.rev_id, s.ts, s.user, s.anonymous, s.is_bot, s.sha1, s.bytes
            FROM research.s4__pp_win w
            JOIN research.s4__seq s ON s.page_id = w.page_id AND s.seq BETWEEN w.s_lo AND w.s_hi
            ORDER BY w.win_id, s.seq;
        """)
        by_win: dict[int, list[dict]] = defaultdict(list)
        for r in win_revs:
            dt = datetime.strptime(r["ts"], "%Y-%m-%d %H:%M:%S")
            by_win[int(r["win_id"])].append({
                "page_id": int(r["page_id"]),
                "seq": int(r["seq"]),
                "rev_id": int(r["rev_id"]),
                "ts": int(dt.timestamp()),
                "user": r["user"],
                "bot": r["is_bot"] == "1",
                "sha1": r["sha1"] if int(r["bytes"]) >= PING_PONG_MIN_BYTES else None,
            })
        out: list[dict] = []
        for wid in sorted(by_win.keys()):
            out.extend(_extract_ping_pong_from_window(by_win[wid]))
        return out

    pp_episodes = _run_step(timings, "4. Detect ping_pong episodes from interleaved SHA-1 states", step_ping_pong)

    # Step 5: Detect burst episodes and compare 5x trailing 90d median vs 3-sigma
    def step_burst() -> tuple[list[dict], dict]:
        daily_rows = aot.sql(fr"""
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS research.s4__daily;
            CREATE TABLE research.s4__daily (
              page_id INT UNSIGNED NOT NULL,
              day DATE NOT NULL,
              human_edits INT UNSIGNED NOT NULL,
              anon_edits INT UNSIGNED NOT NULL,
              named_edits INT UNSIGNED NOT NULL,
              human_editors INT UNSIGNED NOT NULL,
              min_seq INT UNSIGNED NOT NULL,
              max_seq INT UNSIGNED NOT NULL,
              PRIMARY KEY (page_id, day),
              KEY idx_spikes (human_edits, human_editors, page_id, day)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            SELECT page_id, day,
                   CAST(SUM(is_bot = 0) AS UNSIGNED) AS human_edits,
                   CAST(SUM(anonymous = 1) AS UNSIGNED) AS anon_edits,
                   CAST(SUM(anonymous = 0 AND is_bot = 0) AS UNSIGNED) AS named_edits,
                   CAST(COUNT(DISTINCT CASE
                     WHEN is_bot = 0 AND user IS NOT NULL THEN
                       CASE
                         WHEN anonymous = 1 AND user REGEXP '^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$' THEN SUBSTRING_INDEX(user, '.', 2)
                         WHEN anonymous = 1 AND INSTR(user, ':') > 0 THEN LOWER(SUBSTRING_INDEX(user, ':', 2))
                         ELSE user
                       END
                   END) AS UNSIGNED) AS human_editors,
                   MIN(seq) AS min_seq,
                   MAX(seq) AS max_seq
            FROM research.s4__seq
            GROUP BY page_id, day;

            SELECT d.page_id, d.day, d.human_edits, d.anon_edits, d.named_edits, d.human_editors, d.min_seq, d.max_seq
            FROM research.s4__daily d
            WHERE EXISTS (
              SELECT 1 FROM research.s4__daily sp
              WHERE sp.page_id = d.page_id
                AND sp.human_edits >= {BURST_MIN_HUMAN_EDITS}
                AND sp.human_editors >= {BURST_MIN_HUMAN_EDITORS}
                AND d.day BETWEEN DATE_SUB(sp.day, INTERVAL 90 DAY) AND DATE_ADD(sp.day, INTERVAL 15 DAY)
            )
            ORDER BY d.page_id, d.day;
        """)
        daily_by_page: dict[int, list[dict]] = defaultdict(list)
        for r in daily_rows:
            y, m, dd = map(int, r["day"].split("-"))
            daily_by_page[int(r["page_id"])].append({
                "day": date(y, m, dd),
                "human_edits": int(r["human_edits"]),
                "anon_edits": int(r["anon_edits"]),
                "named_edits": int(r["named_edits"]),
                "human_editors": int(r["human_editors"]),
                "min_seq": int(r["min_seq"]),
                "max_seq": int(r["max_seq"]),
            })
        return _detect_bursts(daily_by_page)

    burst_episodes, burst_cmp = _run_step(
        timings, "5. Detect burst episodes (trailing 90d median & 3-sigma)", step_burst
    )

    # Step 6: Stage all episode spans and build `s4_episode` + `s4_episode_rev` in SQL
    def step_materialize_episodes() -> None:
        all_eps = ew_episodes + pp_episodes + burst_episodes
        kind_rank = {"edit_war": 1, "ping_pong": 2, "burst": 3}
        win_rank = {"24h": 1, "7d": 2, "30d": 3, None: 0}
        all_eps.sort(key=lambda e: (kind_rank[e["kind"]], win_rank[e["window"]], e["page_id"], e["start_seq"], e["end_seq"]))

        aot.sql(r"""
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS research.s4__ep_spans, research.s4__prot_revs;
            CREATE TABLE research.s4__ep_spans (
              episode_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              kind VARCHAR(16) CHARACTER SET ascii NOT NULL,
              `window` VARCHAR(8) CHARACTER SET ascii DEFAULT NULL,
              is_default TINYINT(1) NOT NULL,
              start_seq INT UNSIGNED NOT NULL,
              end_seq INT UNSIGNED NOT NULL,
              states INT UNSIGNED DEFAULT NULL,
              state_rev_ids VARCHAR(500) CHARACTER SET ascii DEFAULT NULL,
              burst_anon_drop TINYINT(1) NOT NULL,
              KEY idx_page_seqs (page_id, start_seq, end_seq)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            CREATE TABLE research.s4__prot_revs (
              page_id INT UNSIGNED NOT NULL,
              ts DATETIME NOT NULL,
              KEY idx_page_ts (page_id, ts)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            SELECT page_id, ts
            FROM corpus.revision
            WHERE comment IS NOT NULL
              AND REGEXP_REPLACE(comment, '^(/\\*.*?\\*/\\s*)+', '') COLLATE utf8mb4_uca1400_as_ci
                  REGEXP '(^|[^[:alpha:]])(suojas|suojattu|puolisuoja)'
              AND NOT (
                REGEXP_REPLACE(comment, '^(/\\*.*?\\*/\\s*)+', '') COLLATE utf8mb4_uca1400_as_ci
                  REGEXP '(^-suojattu|ei enää ole suojattu|poisti sivun .* suojauksen|poistettu suojaus)'
              );
        """)

        batch_size = 1500
        for b0 in range(0, len(all_eps), batch_size):
            chunk = all_eps[b0 : b0 + batch_size]
            vals = []
            for idx, ep in enumerate(chunk, start=b0 + 1):
                w_sql = aot.quote(ep["window"]) if ep["window"] is not None else "NULL"
                st_sql = str(ep["states"]) if ep["states"] is not None else "NULL"
                sr_sql = aot.quote(ep["state_rev_ids"]) if ep["state_rev_ids"] is not None else "NULL"
                vals.append(
                    f"({idx},{ep['page_id']},{aot.quote(ep['kind'])},{w_sql},{ep['is_default']},"
                    f"{ep['start_seq']},{ep['end_seq']},{st_sql},{sr_sql},{ep['burst_anon_drop']})"
                )
            aot.sql(
                "SET SESSION max_statement_time = 85; "
                "INSERT INTO research.s4__ep_spans "
                "(episode_id, page_id, kind, `window`, is_default, start_seq, end_seq, states, state_rev_ids, burst_anon_drop) "
                "VALUES " + ",".join(vals)
            )

    _run_step(timings, "6. Stage episode spans & index protection log revisions", step_materialize_episodes)

    # Step 7: Build research.s4_episode_rev
    _run_step(
        timings,
        "7. Build research.s4_episode_rev (all non-null revisions inside episodes)",
        lambda: aot.sql("""
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s4_episode_rev (
              episode_id INT UNSIGNED NOT NULL,
              rev_id INT UNSIGNED NOT NULL,
              PRIMARY KEY (episode_id, rev_id),
              KEY idx_rev_id (rev_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            AS
            SELECT es.episode_id, s.rev_id
            FROM research.s4__ep_spans es
            JOIN research.s4__seq s
              ON s.page_id = es.page_id
             AND s.seq BETWEEN es.start_seq AND es.end_seq;
        """),
    )

    # Step 8: Build research.s4_episode with participant aggregates, revert counts, protection_hint & score
    def step_build_s4_episode() -> None:
        aot.sql("""
            SET SESSION max_statement_time = 85;
            SET SESSION group_concat_max_len = 65536;
            CREATE TABLE research.s4_episode (
              episode_id INT UNSIGNED NOT NULL PRIMARY KEY,
              page_id INT UNSIGNED NOT NULL,
              kind VARCHAR(16) CHARACTER SET ascii NOT NULL,
              start_ts DATETIME NOT NULL,
              end_ts DATETIME NOT NULL,
              `window` VARCHAR(8) CHARACTER SET ascii DEFAULT NULL,
              is_default TINYINT(1) NOT NULL,
              revert_count INT UNSIGNED NOT NULL,
              named_participants INT UNSIGNED NOT NULL,
              anon_participants INT UNSIGNED NOT NULL,
              participant_users TEXT CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
              states INT UNSIGNED DEFAULT NULL,
              state_rev_ids VARCHAR(500) CHARACTER SET ascii DEFAULT NULL,
              first_rev_id INT UNSIGNED NOT NULL,
              last_rev_id INT UNSIGNED NOT NULL,
              edit_count INT UNSIGNED NOT NULL,
              protection_hint TINYINT(1) NOT NULL,
              score DOUBLE NOT NULL,
              KEY idx_page_id (page_id),
              KEY idx_kind (kind),
              KEY idx_page_default (page_id, is_default)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH span_stats AS (
              SELECT es.episode_id,
                     CAST(COUNT(*) AS UNSIGNED) AS edit_count,
                     CAST(COUNT(DISTINCT CASE WHEN s.anonymous = 0 AND s.is_bot = 0 AND s.user IS NOT NULL THEN s.user END) AS UNSIGNED) AS named_participants,
                     CAST(COUNT(DISTINCT CASE WHEN s.anonymous = 1 AND s.user IS NOT NULL THEN s.user END) AS UNSIGNED) AS anon_participants,
                     GROUP_CONCAT(DISTINCT CASE WHEN s.is_bot = 0 AND s.user IS NOT NULL THEN s.user END ORDER BY s.user SEPARATOR ',') AS participant_users,
                     CAST(SUM(rv.reverting_rev_id IS NOT NULL) AS UNSIGNED) AS revert_count
              FROM research.s4__ep_spans es
              JOIN research.s4__seq s
                ON s.page_id = es.page_id AND s.seq BETWEEN es.start_seq AND es.end_seq
              LEFT JOIN research.s1_revert rv
                ON rv.reverting_rev_id = s.rev_id
               AND rv.class NOT IN ('excluded', 'bot', 'self_revert')
              GROUP BY es.episode_id
            ),
            assembled AS (
              SELECT es.episode_id,
                     es.page_id,
                     es.kind,
                     s_first.ts AS start_ts,
                     s_last.ts AS end_ts,
                     es.`window`,
                     es.is_default,
                     st.revert_count,
                     st.named_participants,
                     st.anon_participants,
                     st.participant_users,
                     es.states,
                     es.state_rev_ids,
                     s_first.rev_id AS first_rev_id,
                     s_last.rev_id AS last_rev_id,
                     st.edit_count,
                     IF(
                       es.burst_anon_drop = 1
                       OR EXISTS (
                         SELECT 1 FROM research.s4__prot_revs pr
                         WHERE pr.page_id = es.page_id
                           AND pr.ts BETWEEN DATE_SUB(s_first.ts, INTERVAL 1 HOUR) AND DATE_ADD(s_last.ts, INTERVAL 72 HOUR)
                       ),
                       1, 0
                     ) AS protection_hint
              FROM research.s4__ep_spans es
              JOIN span_stats st USING (episode_id)
              JOIN research.s4__seq s_first ON s_first.page_id = es.page_id AND s_first.seq = es.start_seq
              JOIN research.s4__seq s_last ON s_last.page_id = es.page_id AND s_last.seq = es.end_seq
            )
            SELECT episode_id,
                   page_id,
                   kind,
                   start_ts,
                   end_ts,
                   `window`,
                   is_default,
                   revert_count,
                   named_participants,
                   anon_participants,
                   participant_users,
                   states,
                   state_rev_ids,
                   first_rev_id,
                   last_rev_id,
                   edit_count,
                   protection_hint,
                   ROUND(
                     CASE
                       WHEN kind = 'edit_war' THEN
                         3.0 + 1.5 * LN(1 + revert_count) + 0.8 * LN(1 + named_participants) + IF(protection_hint = 1, 1.0, 0.0)
                       WHEN kind = 'ping_pong' THEN
                         2.0 + 1.2 * LN(1 + revert_count) + 0.6 * LN(1 + named_participants) + 0.5 * GREATEST(COALESCE(states, 2) - 1, 1) + IF(protection_hint = 1, 1.0, 0.0)
                       ELSE
                         1.0 + 0.6 * LN(1 + edit_count) + 1.2 * LN(1 + revert_count) + 0.4 * LN(1 + named_participants) + IF(protection_hint = 1, 1.0, 0.0)
                     END,
                     4
                   ) AS score
            FROM assembled;
        """)

    _run_step(timings, "8. Build research.s4_episode (participants, reverts, protection_hint, score)", step_build_s4_episode)

    # Step 9: Build research.s4_page_contest across all 19,742 corpus pages
    _run_step(
        timings,
        "9. Build research.s4_page_contest (contested_score & peak_month for 19,742 pages)",
        lambda: aot.sql("""
            SET SESSION max_statement_time = 85;
            CREATE TABLE research.s4_page_contest (
              page_id INT UNSIGNED NOT NULL PRIMARY KEY,
              episodes_edit_war INT UNSIGNED NOT NULL,
              episodes_ping_pong INT UNSIGNED NOT NULL,
              episodes_burst INT UNSIGNED NOT NULL,
              contested_score DOUBLE NOT NULL,
              peak_month CHAR(7) CHARACTER SET ascii DEFAULT NULL,
              episodes_edit_war_24h INT UNSIGNED NOT NULL,
              episodes_edit_war_30d INT UNSIGNED NOT NULL,
              protected_episodes INT UNSIGNED NOT NULL,
              KEY idx_contested_score (contested_score)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_as_ci
            AS
            WITH ep_agg AS (
              SELECT page_id,
                     CAST(SUM(kind = 'edit_war' AND is_default = 1) AS UNSIGNED) AS episodes_edit_war,
                     CAST(SUM(kind = 'edit_war' AND `window` = '24h') AS UNSIGNED) AS episodes_edit_war_24h,
                     CAST(SUM(kind = 'edit_war' AND `window` = '30d') AS UNSIGNED) AS episodes_edit_war_30d,
                     CAST(SUM(kind = 'ping_pong') AS UNSIGNED) AS episodes_ping_pong,
                     CAST(SUM(kind = 'burst') AS UNSIGNED) AS episodes_burst,
                     CAST(SUM(CASE WHEN kind = 'edit_war' AND is_default = 1 THEN revert_count ELSE 0 END) AS UNSIGNED) AS ew_revert_sum,
                     CAST(SUM(kind = 'burst' AND revert_count > 0) AS UNSIGNED) AS reverted_bursts,
                     CAST(SUM(kind = 'burst' AND revert_count = 0) AS UNSIGNED) AS peaceful_bursts,
                     CAST(SUM(is_default = 1 AND protection_hint = 1) AS UNSIGNED) AS protected_episodes
              FROM research.s4_episode
              GROUP BY page_id
            ),
            month_events AS (
              SELECT page_id,
                     DATE_FORMAT(start_ts, '%Y-%m') AS ym,
                     CASE
                       WHEN kind = 'edit_war' THEN 3.0 * (1 + revert_count)
                       WHEN kind = 'ping_pong' THEN 2.0 * (1 + revert_count)
                       WHEN kind = 'burst' AND revert_count > 0 THEN 1.5 * (1 + revert_count)
                       ELSE 0.25
                     END AS w,
                     revert_count AS rc
              FROM research.s4_episode
              WHERE is_default = 1
              UNION ALL
              SELECT page_id,
                     DATE_FORMAT(revert_ts, '%Y-%m') AS ym,
                     IF(class = 'edit_war', 1.5, 0.75) AS w,
                     1 AS rc
              FROM research.s1_revert
              WHERE class IN ('edit_war', 'dispute')
            ),
            month_ranked AS (
              SELECT page_id, ym,
                     ROW_NUMBER() OVER (PARTITION BY page_id ORDER BY SUM(w) DESC, SUM(rc) DESC, ym DESC) AS rn
              FROM month_events
              GROUP BY page_id, ym
            )
            SELECT p.page_id,
                   COALESCE(ea.episodes_edit_war, 0) AS episodes_edit_war,
                   COALESCE(ea.episodes_ping_pong, 0) AS episodes_ping_pong,
                   COALESCE(ea.episodes_burst, 0) AS episodes_burst,
                   ROUND(
                     4.0 * LN(1 + COALESCE(ea.episodes_edit_war, 0))
                     + 1.5 * LN(1 + COALESCE(ea.ew_revert_sum, 0))
                     + 2.2 * LN(1 + COALESCE(ea.episodes_ping_pong, 0))
                     + 1.2 * LN(1 + COALESCE(ea.reverted_bursts, 0))
                     + 0.3 * LN(1 + COALESCE(ea.peaceful_bursts, 0))
                     + 1.5 * LN(1 + COALESCE(ea.protected_episodes, 0))
                     + 1.2 * LN(1 + p.edit_war_count + 0.5 * p.dispute_count),
                     4
                   ) AS contested_score,
                   mr.ym AS peak_month,
                   COALESCE(ea.episodes_edit_war_24h, 0) AS episodes_edit_war_24h,
                   COALESCE(ea.episodes_edit_war_30d, 0) AS episodes_edit_war_30d,
                   COALESCE(ea.protected_episodes, 0) AS protected_episodes
            FROM research.s1_page_stats p
            LEFT JOIN ep_agg ea ON ea.page_id = p.page_id
            LEFT JOIN month_ranked mr ON mr.page_id = p.page_id AND mr.rn = 1;
        """),
    )

    # Step 10: Drop staging tables
    _run_step(
        timings,
        "10. Clean up s4__* staging tables",
        lambda: aot.sql("""
            SET SESSION max_statement_time = 85;
            DROP TABLE IF EXISTS
              research.s4__seq,
              research.s4__daily,
              research.s4__pp_win,
              research.s4__ep_spans,
              research.s4__prot_revs;
        """),
    )

    # Step 11: Verify acceptance invariants
    invariants = _run_step(timings, "11. Verify invariants (corpus rev existence, zero null revs, counts)", verify_invariants)

    total_s = round(time.monotonic() - total_start, 2)
    aot.log(f"[s4 build] ALL DONE in {total_s:.2f} s — invariants: {invariants}")

    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "build_timings.json").write_text(
        json.dumps(
            {
                "total_seconds": total_s,
                "steps": timings,
                "invariants": invariants,
                "burst_threshold_comparison": burst_cmp,
            },
            indent=2,
        )
        + chr(10),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
