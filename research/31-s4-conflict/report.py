#!/usr/bin/env python3
"""P1.S4 (#31) report generator.

Queries `research.s4_*` and `research.s1_*` (plus masked edit summaries for the 30 sampled episodes),
computes all distributions, demo-topic episode tables & contestedness ranks, Top 30 contested pages,
anniversary-alignment statistics, and writes:
  - `results/report_data.json` (privacy-safe aggregate & demo/top-30 metrics backing `README.md`)
  - `results/sample_edit_war.md`, `results/sample_ping_pong.md`, `results/sample_burst.md`
    (10 seeded episodes per kind with fiwiki diff links, masked summaries, and 1-line English gists)

Privacy guarantee: never writes raw `user` or unmasked `comment` to any file. Participant counts
use `aot.user_display(u)` (collapsing IPs in the same /16 or /32 range per `docs/evidence-standard.md` §3),
and summaries pass through `aot.mask_comment(c)`.

Usage:
    python3 research/31-s4-conflict/report.py
"""
from __future__ import annotations

from collections import Counter, defaultdict
from datetime import date, datetime
import json
import pathlib
import random
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

NL = chr(10)

DEMO_TOPICS: list[tuple[str, list[str]]] = [
    (
        "Topic 1 · Finnish Civil War (1918)",
        [
            "Suomen sisällissota",
            "Carl Gustaf Emil Mannerheim",
            "Punakaarti",
            "Suojeluskunta",
            "Suomen sisällissodan nimet",
        ],
    ),
    (
        "Topic 2 · Continuation War, Finland-Germany, East Karelia & Waffen-SS",
        [
            "Jatkosota",
            "Risto Ryti",
            "Sotasyyllisyysoikeudenkäynti",
            "Itä-Karjalan keskitysleirit",
            "Suomalainen Waffen-SS-vapaaehtoispataljoona",
            "Naarajärven sotavankileiri",
        ],
    ),
    (
        "Topic 3 · Winter War, Shelling of Mainila & Moscow Peace",
        [
            "Talvisota",
            "Simo Häyhä",
            "Moskovan rauhansopimus (1940)",
            "Mainilan laukaukset",
        ],
    ),
    (
        "Topic 4 · Ceded Karelia, Vyborg & Greater Finland",
        [
            "Karjala",
            "Suur-Suomi",
            "Viipuri",
            "Karjala-kysymys",
            "Käkisalmi",
            "Luovutettu Karjala",
        ],
    ),
]

HISTORICAL_ANNIVERSARIES: list[tuple[str, int, int]] = [
    ("Civil War outbreak (Jan 27)", 1, 27),
    ("Moscow Peace / Winter War end (Mar 13)", 3, 13),
    ("Civil War end / Commemoration (May 15-16)", 5, 15),
    ("Continuation War outbreak (Jun 25)", 6, 25),
    ("Moscow Armistice (Sep 19)", 9, 19),
    ("Winter War outbreak (Nov 30)", 11, 30),
    ("Finnish Independence Day (Dec 6)", 12, 6),
]

_RE_SECTION = re.compile(r"^(?:/\*\s*(.*?)\s*\*/\s*)+")

_REASON_KEYWORDS: list[tuple[str, str]] = [
    (r"\bsuojasi sivun\b|\bpuolisuojat|\{\{suojattu", "page protection notice"),
    (r"\bmuokkaussota", "edit-war note"),
    (r"\bvandalism|\bsotk", "vandalism cleanup"),
    (r"\blähteetön|\blähde\b|\blähteet\b|\bviitte|\bviite\b", "sourcing/citation dispute"),
    (r"\bneutraal|\bpov\b|\bnäkökulma", "neutrality/POV dispute"),
    (r"\bkeskustelu|\bkonsensus", "talk-page / consensus reference"),
    (r"\beston kierto", "block-evasion note"),
    (r"\btyhjen|\bak:\s*sivu tyhjennettiin", "page/section blanking"),
    (r"\bohjaussivu|\buudelleenohjaus", "redirect change"),
    (r"\bluokka\b|hotcat", "category edit"),
    (r"\bkuva\b|\btiedosto", "image/file edit"),
    (r"\bkieliopp|\btyp[oa]|\boikoluk|\bkieli\b", "copyedit/spelling"),
    (r"\bpäivit|\btilasto|\bluku", "data/figure update"),
]


def _quantile(vals: list[float], q: float) -> float:
    if not vals:
        return 0.0
    s = sorted(vals)
    if len(s) == 1:
        return round(float(s[0]), 2)
    pos = q * (len(s) - 1)
    lo = int(pos)
    hi = min(lo + 1, len(s) - 1)
    frac = pos - lo
    return round(float(s[lo] * (1.0 - frac) + s[hi] * frac), 2)


def _dist_summary(vals: list[float]) -> dict:
    if not vals:
        return {"min": 0, "p25": 0, "p50": 0, "p75": 0, "p90": 0, "max": 0, "mean": 0}
    return {
        "min": round(min(vals), 2),
        "p25": _quantile(vals, 0.25),
        "p50": _quantile(vals, 0.50),
        "p75": _quantile(vals, 0.75),
        "p90": _quantile(vals, 0.90),
        "max": round(max(vals), 2),
        "mean": round(sum(vals) / len(vals), 2),
    }


def _masked_participant_count(raw_csv: str | None) -> int:
    if not raw_csv:
        return 0
    labels = {aot.user_display(u) for u in raw_csv.split(",") if u}
    return len(labels)


def _summarize_single_masked_comment(mc: str | None) -> str:
    """Turn one masked Finnish edit summary into a short, neutral English phrase (no editor names)."""
    if not mc or not mc.strip():
        return ""
    s = " ".join(mc.strip().splitlines())
    secs = re.findall(r"/\*\s*(.*?)\s*\*/", s)
    body = _RE_SECTION.sub("", s).strip()
    sec_tag = f"section '{secs[-1][:36]}'" if secs else ""

    if not body:
        return f"edits in {sec_tag}" if sec_tag else ""

    # Strip MediaWiki `(keskustelu)` user-talk link boilerplate before keyword matching
    body_no_utalk = re.sub(r"\(\s*keskustelu\s*\)", "", body)
    low = body_no_utalk.lower()
    tags: list[str] = []
    if re.search(r"vandalismi kumottiin", low):
        tags.append("auto-rollback (vandalism)")
    elif re.search(r"muokkaukset kumottiin ja (sivu|artikkeli) palautettiin", low):
        tags.append("auto-rollback")
    elif re.search(r"^kumottu muokkaus|muokkaus \d+ kumottiin", low):
        tags.append("undo")
    elif re.search(r"^hylättiin .*tekstimuuto", low):
        tags.append("FlaggedRevs reject")
    elif re.search(r"\b(rv|rvv|palautus|palautettu|kumottu)\b", low):
        tags.append("manual revert")

    for pat, label in _REASON_KEYWORDS:
        if re.search(pat, low):
            if label not in tags:
                tags.append(label)

    # Extract explicit custom rationale after colon in auto-undo/rollback if present
    custom = ""
    m = re.match(
        r"^(?:Käyttäjän .+? kumottiin ja (?:sivu|artikkeli) palautettiin [^:]+|"
        r"Kumottu muokkaus [^\)]+\)|"
        r"Hylättiin [^:]+ versio \d+[^:]*|"
        r"Käyttäjän .+? muokkaus \d+ kumottiin)\s*[:\-–]\s*(.+)$",
        body,
    )
    if m and m.group(1).strip():
        custom = m.group(1).strip()
    elif not tags:
        custom = body

    if custom:
        # Redact any residual `Käyttäjän X` or user talk mentions so gists focus purely on content/actions
        clean_custom = re.sub(r"\(\s*keskustelu\s*\)", "", custom).strip()
        if len(clean_custom) > 65:
            clean_custom = clean_custom[:62] + "..."
        if tags:
            phrase = f"{', '.join(tags)} ('{clean_custom}')"
        else:
            phrase = f"summary note '{clean_custom}'"
    else:
        phrase = ", ".join(tags)

    if sec_tag and sec_tag not in phrase:
        return f"{sec_tag}: {phrase}"
    return phrase


def _episode_english_gist(kind: str, ep: dict, rev_rows: list[dict]) -> str:
    """Build a concise one-line English gist for a sampled episode from its masked edit summaries."""
    phrases: list[str] = []
    seen: set[str] = set()
    # Prioritize reverting / non-empty custom summaries first, then section/general summaries
    ordered = sorted(
        rev_rows,
        key=lambda r: (
            0 if (r.get("is_revert") == "1" and r.get("comment")) else 1,
            int(r["rev_id"]),
        ),
    )
    for r in ordered:
        mc = aot.mask_comment(r.get("comment"))
        p = _summarize_single_masked_comment(mc)
        if p and p not in seen:
            seen.add(p)
            phrases.append(p)
        if len(phrases) >= 3:
            break

    rc = int(ep["revert_count"])
    ec = int(ep["edit_count"])
    mp = ep["masked_participants"]
    prot = " + protection hint" if int(ep["protection_hint"]) == 1 else ""

    if not phrases:
        return f"{ec} edits ({rc} reverts) across {mp} masked participants{prot}; all edit summaries empty."
    joined = "; ".join(phrases)
    if len(joined) > 155:
        joined = joined[:152] + "..."
    return f"{ec} edits ({rc} reverts, {mp} participants{prot}) — {joined}"


def _md_code(text: str | None) -> str:
    if not text:
        return "*(empty)*"
    clean = " ".join(text.splitlines()).replace("`", "'")
    if len(clean) > 140:
        clean = clean[:137] + "..."
    return f"`{clean}`"


def collect_distributions_and_overlaps() -> dict:
    rows = aot.sql("""
        SET SESSION max_statement_time = 85;
        SELECT episode_id, page_id, kind, `window`, is_default,
               start_ts, end_ts,
               TIMESTAMPDIFF(SECOND, start_ts, end_ts) AS dur_sec,
               revert_count, named_participants, anon_participants, participant_users,
               states, state_rev_ids, first_rev_id, last_rev_id, edit_count, protection_hint, score
        FROM research.s4_episode
        ORDER BY episode_id ASC
    """)

    groups: dict[str, list[dict]] = defaultdict(list)
    for r in rows:
        key = f"edit_war:{r['window']}" if r["kind"] == "edit_war" else r["kind"]
        r["masked_participants"] = _masked_participant_count(r["participant_users"])
        groups[key].append(r)

    summary_by_group: dict[str, dict] = {}
    ordered_keys = ["edit_war:24h", "edit_war:7d", "edit_war:30d", "ping_pong", "burst"]
    for k in ordered_keys:
        g = groups.get(k, [])
        edits = [int(x["edit_count"]) for x in g]
        dur_hours = [int(x["dur_sec"]) / 3600.0 for x in g]
        reverts = [int(x["revert_count"]) for x in g]
        named_p = [int(x["named_participants"]) for x in g]
        anon_p = [int(x["anon_participants"]) for x in g]
        masked_p = [int(x["masked_participants"]) for x in g]
        states_dist = dict(sorted(Counter(int(x["states"]) for x in g if x["states"] is not None).items()))
        summary_by_group[k] = {
            "episodes": len(g),
            "distinct_pages": len({int(x["page_id"]) for x in g}),
            "total_edits": sum(edits),
            "total_reverts": sum(reverts),
            "episodes_with_reverts": sum(1 for v in reverts if v > 0),
            "protection_hint_count": sum(int(x["protection_hint"]) for x in g),
            "protection_hint_pct": round(100.0 * sum(int(x["protection_hint"]) for x in g) / len(g), 2) if g else 0.0,
            "edit_count_dist": _dist_summary(edits),
            "duration_hours_dist": _dist_summary(dur_hours),
            "revert_count_dist": _dist_summary(reverts),
            "named_participants_dist": _dist_summary(named_p),
            "anon_participants_dist": _dist_summary(anon_p),
            "masked_participants_dist": _dist_summary(masked_p),
            "states_counts": states_dist,
        }

    # Cross-kind revision overlap among default episodes (`edit_war:7d`, `ping_pong`, `burst`)
    overlap = aot.sql("""
        SET SESSION max_statement_time = 85;
        WITH def_rev AS (
          SELECT er.rev_id,
                 MAX(e.kind = 'edit_war') AS in_ew,
                 MAX(e.kind = 'ping_pong') AS in_pp,
                 MAX(e.kind = 'burst') AS in_burst
          FROM research.s4_episode_rev er
          JOIN research.s4_episode e ON e.episode_id = er.episode_id
          WHERE e.is_default = 1
          GROUP BY er.rev_id
        )
        SELECT COUNT(*) AS distinct_default_episode_revs,
               SUM(in_ew = 1) AS revs_in_ew7d,
               SUM(in_pp = 1) AS revs_in_ping_pong,
               SUM(in_burst = 1) AS revs_in_burst,
               SUM(in_ew = 1 AND in_pp = 1) AS revs_ew_and_pp,
               SUM(in_ew = 1 AND in_burst = 1) AS revs_ew_and_burst,
               SUM(in_pp = 1 AND in_burst = 1) AS revs_pp_and_burst,
               SUM(in_ew = 1 AND in_pp = 1 AND in_burst = 1) AS revs_all_three
        FROM def_rev
    """)[0]

    # Coverage of S1 candidate reverted edits (`s1_reverted_edit` joined with `s1_revert` signal classes)
    s1_cov = aot.sql("""
        SET SESSION max_statement_time = 85;
        WITH cand AS (
          SELECT re.rev_id, rv.class
          FROM research.s1_reverted_edit re
          JOIN research.s1_revert rv USING (reverting_rev_id)
          WHERE rv.class NOT IN ('excluded', 'bot', 'self_revert')
        ),
        in_def AS (
          SELECT DISTINCT er.rev_id
          FROM research.s4_episode_rev er
          JOIN research.s4_episode e USING (episode_id)
          WHERE e.is_default = 1
        )
        SELECT c.class,
               COUNT(*) AS total_reverted_edits,
               SUM(d.rev_id IS NOT NULL) AS inside_s4_episode,
               ROUND(100.0 * SUM(d.rev_id IS NOT NULL) / COUNT(*), 2) AS pct_inside
        FROM cand c
        LEFT JOIN in_def d USING (rev_id)
        GROUP BY c.class
        ORDER BY FIELD(c.class, 'edit_war', 'dispute', 'graffiti_cleanup', 'other')
    """)

    return {
        "by_group": summary_by_group,
        "default_revision_overlap": {k: int(v) for k, v in overlap.items()},
        "s1_reverted_edit_coverage": s1_cov,
        "_cached_rows": groups,
    }


def collect_top_30_and_demo_topics(cached_groups: dict[str, list[dict]]) -> dict:
    # Rank all 19,742 pages deterministically by contested_score DESC (`RANK()` gives shared rank on ties)
    all_ranked = aot.sql("""
        SET SESSION max_statement_time = 85;
        SELECT c.page_id,
               p.title,
               p.revision_count,
               p.edit_war_count AS s1_edit_war_reverts,
               p.dispute_count AS s1_dispute_reverts,
               c.episodes_edit_war,
               c.episodes_edit_war_24h,
               c.episodes_edit_war_30d,
               c.episodes_ping_pong,
               c.episodes_burst,
               c.protected_episodes,
               c.contested_score,
               c.peak_month,
               RANK() OVER (ORDER BY c.contested_score DESC) AS score_rank
        FROM research.s4_page_contest c
        JOIN research.s1_page_stats p USING (page_id)
        ORDER BY c.contested_score DESC, c.page_id ASC
    """)

    top_30 = all_ranked[:30]
    by_title = {r["title"]: r for r in all_ranked}
    positive_pages = sum(1 for r in all_ranked if float(r["contested_score"]) > 0.0)
    pages_with_any_default_ep = sum(
        1
        for r in all_ranked
        if (int(r["episodes_edit_war"]) + int(r["episodes_ping_pong"]) + int(r["episodes_burst"])) > 0
    )

    # Index default episodes (`edit_war:7d`, `ping_pong`, `burst`) by page_id — privacy-safe fields ONLY!
    default_eps_by_page: dict[int, list[dict]] = defaultdict(list)
    for k in ("edit_war:7d", "ping_pong", "burst"):
        for ep in cached_groups.get(k, []):
            default_eps_by_page[int(ep["page_id"])].append({
                "episode_id": int(ep["episode_id"]),
                "kind": ep["kind"],
                "window": ep["window"],
                "start_date": ep["start_ts"][:10],
                "end_date": ep["end_ts"][:10],
                "start_ts": ep["start_ts"],
                "end_ts": ep["end_ts"],
                "edit_count": int(ep["edit_count"]),
                "revert_count": int(ep["revert_count"]),
                "masked_participants": int(ep["masked_participants"]),
                "named_participants": int(ep["named_participants"]),
                "anon_participants": int(ep["anon_participants"]),
                "states": int(ep["states"]) if ep["states"] is not None else None,
                "protection_hint": int(ep["protection_hint"]),
                "first_rev_id": int(ep["first_rev_id"]),
                "last_rev_id": int(ep["last_rev_id"]),
                "score": float(ep["score"]),
            })

    for pid in default_eps_by_page:
        default_eps_by_page[pid].sort(key=lambda x: (x["start_ts"], x["episode_id"]))

    demo_report: list[dict] = []
    demo_pids: dict[int, str] = {}
    for topic_name, titles in DEMO_TOPICS:
        art_entries: list[dict] = []
        for t in titles:
            row = by_title[t]
            pid = int(row["page_id"])
            demo_pids[pid] = t
            art_entries.append({
                "page_id": pid,
                "title": t,
                "contested_score": float(row["contested_score"]),
                "rank_of_19742": int(row["score_rank"]),
                "peak_month": row["peak_month"],
                "revision_count": int(row["revision_count"]),
                "episodes_edit_war_24h": int(row["episodes_edit_war_24h"]),
                "episodes_edit_war_7d": int(row["episodes_edit_war"]),
                "episodes_edit_war_30d": int(row["episodes_edit_war_30d"]),
                "episodes_ping_pong": int(row["episodes_ping_pong"]),
                "episodes_burst": int(row["episodes_burst"]),
                "protected_episodes": int(row["protected_episodes"]),
                "s1_edit_war_reverts": int(row["s1_edit_war_reverts"]),
                "s1_dispute_reverts": int(row["s1_dispute_reverts"]),
                "episodes": default_eps_by_page.get(pid, []),
            })
        demo_report.append({"topic": topic_name, "articles": art_entries})

    # Anniversary alignment check across all burst episodes on the 21 demo core articles
    mmdd_per_page: dict[tuple[int, str], list[str]] = defaultdict(list)
    demo_bursts: list[tuple[str, date]] = []
    for pid, title in demo_pids.items():
        for ep in default_eps_by_page.get(pid, []):
            if ep["kind"] != "burst":
                continue
            y, m, dd = map(int, ep["start_date"].split("-"))
            d_obj = date(y, m, dd)
            mmdd = ep["start_date"][5:]
            mmdd_per_page[(pid, mmdd)].append(ep["start_date"][:4])
            demo_bursts.append((title, d_obj))

    exact_repeats = [
        {"title": demo_pids[pid], "mm_dd": mmdd, "years": sorted(set(yrs))}
        for (pid, mmdd), yrs in sorted(mmdd_per_page.items())
        if len(set(yrs)) >= 2
    ]

    anniv_hits: list[dict] = []
    for label, am, ad in HISTORICAL_ANNIVERSARIES:
        matches: list[str] = []
        for title, bd in demo_bursts:
            try:
                target = date(bd.year, am, ad)
            except ValueError:
                continue
            if abs((bd - target).days) <= 3:
                matches.append(f"{title} ({bd.isoformat()})")
        anniv_hits.append({"anniversary": label, "within_pm3d_count": len(matches), "examples": matches})

    return {
        "total_pages": len(all_ranked),
        "positive_contested_score_pages": positive_pages,
        "pages_with_any_default_episode": pages_with_any_default_ep,
        "top_30": top_30,
        "demo_topics": demo_report,
        "anniversary_alignment": {
            "demo_burst_episodes_total": len(demo_bursts),
            "exact_same_page_mmdd_repeats": exact_repeats,
            "historical_anniversary_pm3d": anniv_hits,
        },
    }


def sample_episodes_and_write_markdown(cached_groups: dict[str, list[dict]], out_dir: pathlib.Path) -> dict[str, list[dict]]:
    """Deterministically sample 10 episodes per kind (`edit_war` default 7d, `ping_pong`, `burst`),
    fetch masked revision summaries via `(page_id, rev_id)` PK index, and generate English gists.
    """
    title_map = {
        int(r["page_id"]): r["title"]
        for r in aot.sql("SELECT page_id, title FROM research.s1_page_stats")
    }

    kind_sources = [
        ("edit_war", cached_groups["edit_war:7d"]),
        ("ping_pong", cached_groups["ping_pong"]),
        ("burst", cached_groups["burst"]),
    ]

    sampled_summary: dict[str, list[dict]] = {}

    for kind, pool in kind_sources:
        rng = random.Random(f"s4-31-{kind}")
        chosen = rng.sample(pool, min(10, len(pool)))
        chosen.sort(key=lambda x: int(x["episode_id"]))

        ep_ids_csv = aot.ints(int(x["episode_id"]) for x in chosen)
        rev_rows = aot.sql(f"""
            SET SESSION max_statement_time = 85;
            SELECT er.episode_id,
                   cr.rev_id,
                   cr.ts,
                   cr.comment,
                   IF(rv.reverting_rev_id IS NOT NULL, 1, 0) AS is_revert
            FROM research.s4_episode_rev er
            JOIN research.s4_episode e ON e.episode_id = er.episode_id
            JOIN corpus.revision cr ON cr.page_id = e.page_id AND cr.rev_id = er.rev_id
            LEFT JOIN research.s1_revert rv
              ON rv.reverting_rev_id = cr.rev_id
             AND rv.class NOT IN ('excluded', 'bot', 'self_revert')
            WHERE er.episode_id IN ({ep_ids_csv})
            ORDER BY er.episode_id, cr.ts, cr.rev_id
        """)
        revs_by_ep: dict[int, list[dict]] = defaultdict(list)
        for r in rev_rows:
            revs_by_ep[int(r["episode_id"])].append(r)

        md_lines = [
            f"# Sampled `{kind}` episodes (n = {len(chosen)}, seed = `'s4-31-{kind}'`)",
            "",
            "All edit summaries are masked with `aot.mask_comment`; participant counts use `aot.user_display`.",
            "",
        ]

        items_out: list[dict] = []
        for idx, ep in enumerate(chosen, 1):
            eid = int(ep["episode_id"])
            pid = int(ep["page_id"])
            title = title_map.get(pid, f"page_{pid}")
            erows = revs_by_ep.get(eid, [])
            gist = _episode_english_gist(kind, ep, erows)

            # Pick primary diff link: first clean revert inside episode if present, else last_rev_id
            revert_revs = [int(r["rev_id"]) for r in erows if r["is_revert"] == "1"]
            key_diff_rev = revert_revs[0] if revert_revs else int(ep["last_rev_id"])
            first_rev = int(ep["first_rev_id"])
            last_rev = int(ep["last_rev_id"])

            # Masked summaries (up to 3 non-empty) for markdown inspection
            masked_samples = []
            for r in erows:
                mc = aot.mask_comment(r["comment"])
                if mc and mc.strip():
                    masked_samples.append((int(r["rev_id"]), mc.strip()))
                if len(masked_samples) >= 3:
                    break

            extra_state_links = ""
            if kind == "ping_pong" and ep.get("state_rev_ids"):
                s_ids = [int(x) for x in str(ep["state_rev_ids"]).split(",") if x]
                extra_state_links = " · state reps: " + ", ".join(
                    f"[{sid}](https://fi.wikipedia.org/w/index.php?diff={sid})" for sid in s_ids
                )

            md_lines.extend([
                f"### {idx}. {title} (`episode_id={eid}`, `page_id={pid}`)",
                f"- **Window / span:** `{ep['start_ts']}` → `{ep['end_ts']}` UTC "
                f"(`edit_count={ep['edit_count']}`, `revert_count={ep['revert_count']}`, "
                f"`masked_participants={ep['masked_participants']}` "
                f"[named={ep['named_participants']}, raw_anon={ep['anon_participants']}], "
                f"`protection_hint={ep['protection_hint']}`, `score={ep['score']}`)",
                f"- **Diff links:** key [diff={key_diff_rev}](https://fi.wikipedia.org/w/index.php?diff={key_diff_rev}) "
                f"· first [diff={first_rev}](https://fi.wikipedia.org/w/index.php?diff={first_rev}) "
                f"· last [diff={last_rev}](https://fi.wikipedia.org/w/index.php?diff={last_rev}){extra_state_links}",
                f"- **English gist:** {gist}",
            ])
            if masked_samples:
                md_lines.append(
                    "- **Sample masked summaries:** "
                    + " | ".join(f"`{rid}`: {_md_code(mc)}" for rid, mc in masked_samples)
                )
            else:
                md_lines.append("- **Sample masked summaries:** *(all edit summaries in episode empty)*")
            md_lines.append("")

            items_out.append({
                "episode_id": eid,
                "page_id": pid,
                "title": title,
                "start_ts": ep["start_ts"],
                "end_ts": ep["end_ts"],
                "edit_count": int(ep["edit_count"]),
                "revert_count": int(ep["revert_count"]),
                "masked_participants": int(ep["masked_participants"]),
                "states": int(ep["states"]) if ep["states"] is not None else None,
                "state_rev_ids": ep.get("state_rev_ids"),
                "protection_hint": int(ep["protection_hint"]),
                "score": float(ep["score"]),
                "key_diff_rev_id": key_diff_rev,
                "first_rev_id": first_rev,
                "last_rev_id": last_rev,
                "english_gist": gist,
            })

        (out_dir / f"sample_{kind}.md").write_text(NL.join(md_lines), encoding="utf-8")
        aot.log(f"[s4 report] wrote results/sample_{kind}.md ({len(chosen)} episodes)")
        sampled_summary[kind] = items_out

    return sampled_summary


def main() -> None:
    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)

    dist_data = collect_distributions_and_overlaps()
    cached_groups = dist_data.pop("_cached_rows")
    ranking_data = collect_top_30_and_demo_topics(cached_groups)
    sampled = sample_episodes_and_write_markdown(cached_groups, out_dir)

    report_payload: dict = {
        "distributions_and_overlaps": dist_data,
        "rankings_and_demo": ranking_data,
        "sampled_episodes": sampled,
    }
    timings_path = out_dir / "build_timings.json"
    if timings_path.exists():
        report_payload["build_timings"] = json.loads(timings_path.read_text(encoding="utf-8"))

    (out_dir / "report_data.json").write_text(
        json.dumps(report_payload, indent=2, ensure_ascii=False) + NL,
        encoding="utf-8",
    )
    aot.log("[s4 report] wrote results/report_data.json")


if __name__ == "__main__":
    main()
