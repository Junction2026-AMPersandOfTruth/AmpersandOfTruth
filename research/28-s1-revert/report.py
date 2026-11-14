#!/usr/bin/env python3
"""P1.S1 (#28) report generator.

Queries `research.s1_*` (and `results/build_timings.json`), writes:
  - `results/sample_<class>.md` (20 seeded rows per class, masked via `aot`, with diff links and short EN summary gists)
  - `results/report_data.json` (aggregate metrics backing `README.md`)

Usage:
    python3 research/28-s1-revert/report.py
"""
from __future__ import annotations

import json
import pathlib
import random
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

CLASSES = [
    "excluded",
    "bot",
    "self_revert",
    "edit_war",
    "dispute",
    "graffiti_cleanup",
    "other",
]

FAMILY_NAMES = {
    "1": "1: Rollback",
    "2": "2: Undo (modern)",
    "3": "3: FlaggedRevs reject",
    "4": "4: Undo (legacy)",
    "5": "5: EN gadget / Popups",
    "6": "6: Rollback (2004-06)",
}

DEMO_TOPICS = [
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


# ── Concise English gist of masked Finnish edit summaries ────────────────────────────────────────────────────────────

_RE_SECTION = re.compile(r"^(?:/\*\s*(.*?)\s*\*/\s*)+")

_EXACT_REASON_MAP = {
    "rv": "rv (revert)",
    "palautus": "revert",
    "kumottu": "undone",
    "oli jo": "already present",
    "lähde puuttuu": "missing source",
    "lähde puuttuu.": "missing source",
    "lähteetön": "unsourced",
    "ei viitteitä": "no citations",
    "ei ollut virhe": "was not an error",
    "tarpeeton lisäys": "unnecessary addition",
    "rv, ei": "revert, no",
    "rv, selkeämpi näin": "revert, clearer this way",
    "rv, eston kiertoa (wpk)": "revert, block evasion (WPK)",
    "rv per keskustelu": "revert per talk page discussion",
    "ei liity noihin": "unrelated to those items",
    "linkki kuntoon": "fix link",
    "linkki toimii edelleen.": "link still works",
    "linkki toimii edelleen": "link still works",
    "markkinointilinkki pois": "remove promotional link",
    "removed false information": "Removed false information (EN summary)",
    "täysin perustelemattomia kumouksia": "unjustified reverts",
    "svg-tiedosto png:n tilalle.": "replace PNG file with SVG",
    "helvetin älykääpiöitä taas": "frustrated profanity remark",
    "(mobileundo v2.0.0)": "mobileUndo gadget tag",
}


def _translate_reason_fragment(frag: str) -> str:
    s = frag.strip()
    if not s:
        return ""
    low = s.lower()
    if low in _EXACT_REASON_MAP:
        return _EXACT_REASON_MAP[low]

    # Specific recurring Finnish summary clauses
    if low.startswith("ak: sivu tyhjennettiin"):
        return "Auto-summary: page was blanked"
    m = re.match(r"(?i)^ak:\s*(?:uudelleenohjaus|ohjaus)\s+sivulle\s+(.+)$", s)
    if m:
        return f"Auto-summary: redirect to '{m.group(1)}'"
    m = re.match(r"(?i)^poistettu\s+:?luokka:([^;]+);\s*lisätty\s+:?luokka:(.+?)\s+hotcat-työkalulla\s*(.*)$", s)
    if m:
        tail = f" ({_translate_reason_fragment(m.group(3))})" if m.group(3).strip() else ""
        return f"HotCat: replaced category '{m.group(1).strip()}' with '{m.group(2).strip()}'{tail}"
    m = re.match(r"(?i)^poistettu\s+:?luokka:(.+?)\s+hotcat-työkalulla$", s)
    if m:
        return f"HotCat: removed category '{m.group(1).strip()}'"
    if low.startswith("luokitus per keskustelu"):
        return "categorization per category talk page"
    m = re.match(r"(?i)^(?:r[0-9.]+\)\s*\()?botti\s+(muokkasi|lisäsi|poisti):\s*(.+)$", s)
    if m:
        verb = {"muokkasi": "modified", "lisäsi": "added", "poisti": "removed"}[m.group(1).lower()]
        return f"Interwiki bot {verb} link: {m.group(2)}"
    m = re.match(r"(?i)^botti poisti (\d+) wikidatan sivulle (\S+) siirrettyä kielilinkkiä$", s)
    if m:
        return f"Bot removed {m.group(1)} interwiki link(s) migrated to Wikidata ({m.group(2)})"
    if "suojaan artikkelin kunnes sivun kohdalla ollaan saatu aikaan sopu" in low:
        return "Revert to January version and protect page pending consensus"
    if "pois punaiset nimet" in low:
        return "Remove red-linked names added without notable entries"
    if "monta versiota taaksepäin" in low and "lähteet mukaan" in low:
        return "Revert several versions back; asks for sources and notes chronological anachronism (1957 vs 2000s)"
    if "suomi-venäjä-seura on perustettu 16.10.1944" in low:
        return "Cites society website stating founding date 16 Oct 1944 (same organization)"
    if "kaikki tiedot ovat myös artikkelissa karjalan tasavalta" in low:
        return "all information is already in article 'Karjalan tasavalta'"
    if "ennen omaan tutkimukseen perustuvia muokkauksia" in low:
        return "Revert to version before original-research edits; see talk page"
    if "palautetaan laurikaisen viimeisiä muutoksia edeltäneeseen tilaan" in low:
        return "Revert before recent structural/list changes; links RfC (Kommenttipyyntö)"
    if "teksti pitäisi kirjoittaa artikkelimuotoon eikä listana" in low:
        return "text should be written as prose rather than a list"
    if "kaksi mallinetta, tiedot jo artikkelissa ja väkiluku oli vuodelta 2004" in low:
        return "Does not fit: duplicate infobox template, info already in text, 2004 population figure"
    if "20 vuotta vanha uutislähetys lähteenä nykyiselle asuinpaikalle" in low:
        return "Revert: questions using a 20-year-old newscast as source for current residence"
    if "henkilöluokka mieluummin ohjaussivuun" in low:
        return "put person category on the redirect page instead"
    if "wikipedia ei ole mikään juorupalsta" in low:
        return "Wikipedia is not a gossip column"
    if "artikkelin mukaan hattulasta" in low:
        return "according to the article, from Hattula"
    if 'käytetään mieluummin "puhdasta" kuvaa' in low:
        return "prefer unannotated image; identify people in the caption instead"
    if "per kahvihuonekeskustelu järjestysnumerointi pois" in low:
        return "revert per Village Pump discussion: remove ordinal numbering"
    if "vastaakohan merkittyä lähdettä" in low:
        return "questions whether edit matches the cited source"
    if "lähteen kanssa takaisin, artikkelissa ei mainintaa haukiputaasta" in low:
        return "bring back only with a source; cited article does not mention Haukipudas"
    if "lisäät summaan noin 3 x enemmän ilman lähdettä" in low:
        return "questions multiplying figure ~3x without a source"

    # Generic keyword fallback (concise, literal)
    parts: list[str] = []
    if re.search(r"\b(rv|rvv|palautus|palautettu|kumottu)\b", low):
        parts.append("revert")
    if "vandalism" in low or "sotk" in low:
        parts.append("vandalism cleanup")
    if "lähteet" in low or "lähde" in low or "viitte" in low:
        parts.append("sourcing note")
    if "keskustelu" in low:
        parts.append("refers to talk page")
    if "luokka" in low:
        parts.append("category change")
    if "kuva" in low or "tiedosto" in low:
        parts.append("image/file change")
    if "linkki" in low:
        parts.append("link change")
    if parts:
        return "; ".join(parts)
    return f"Finnish note: {s[:80]}"


def english_summary_gist(masked_comment: str | None) -> str:
    """Short, literal English gist of what a masked Finnish revert summary says."""
    if masked_comment is None or not masked_comment.strip():
        return "(empty summary)"
    s = masked_comment.strip()

    sec_names = re.findall(r"/\*\s*(.*?)\s*\*/", s)
    body = _RE_SECTION.sub("", s).strip()
    sec_prefix = f"Section '{sec_names[-1]}'" if sec_names else ""
    if not body:
        return f"{sec_prefix} only (no text)" if sec_prefix else "(empty summary)"

    # 1. MediaWiki Rollback
    m = re.match(
        r"^Käyttäjän (.+?) (muokkaukset|vandalismi) kumottiin ja (?:sivu|artikkeli) palautettiin "
        r"(?:viimeisimpään )?käyttäjän (.+?) tekemä(?:än versioon(?: \d+)?\.?)?(?::\s*(.*))?$",
        body,
    )
    if m:
        kind = "vandalism rollback" if m.group(2) == "vandalismi" else "rollback"
        reason = _translate_reason_fragment(m.group(4) or "")
        core = f"Auto-{kind} of {m.group(1)} to version by {m.group(3)}"
        return f"{core}; {reason}" if reason else core

    # 2. Modern MediaWiki Undo
    m = re.match(
        r"^Kumottu muokkaus (?:#|\[\[Special:Diff/|\[\[Toiminnot:ero/)?(\d+), jonka teki (.+?)"
        r"\s*\(keskustelu\)\s*(?:[:\-–]\s*)?(.*)$",
        body,
    )
    if not m:
        m = re.match(
            r"^Kumottu muokkaus (?:#|\[\[Special:Diff/|\[\[Toiminnot:ero/)?(\d+), jonka teki ([^:\-–]+?)"
            r"(?:\s*[:\-–]\s*(.*))?$",
            body,
        )
    if m:
        raw_tail = m.group(3) or ""
        reason = _translate_reason_fragment(raw_tail)
        core = f"Auto-undo of rev {m.group(1)} by {m.group(2).strip()}"
        return f"{core}; {reason}" if reason else core

    # 3. FlaggedRevs Reject
    m = re.match(
        r"^Hylättiin [^\(]+\((?:tehnyt|käyttäjältä) (.+?)\) ja palautettiin versio (\d+)"
        r"(?:,? jonka on tehnyt | käyttäjältä )([^:]+?)(?::\s*(.*))?$",
        body,
    )
    if m:
        reason = _translate_reason_fragment(m.group(4) or "")
        core = f"FlaggedRevs reject of edit(s) by {m.group(1)} -> restored rev {m.group(2)} ({m.group(3).strip()})"
        return f"{core}; {reason}" if reason else core

    # 4. Legacy MediaWiki Undo
    m = re.match(r"^Käyttäjän (.+?) muokkaus (\d+) kumottiin(?::\s*(.*))?$", body)
    if m:
        reason = _translate_reason_fragment(m.group(3) or "")
        core = f"Legacy undo of rev {m.group(2)} by {m.group(1)}"
        return f"{core}; {reason}" if reason else core

    # 5. Popups (EN or FI localized)
    m = re.match(r"^Palautti sivun käyttäjän (.+?) [\d\-: ]+tekemään versioon (\d+) popupilla$", body)
    if m:
        return f"Navigation Popups revert to rev {m.group(2)} by {m.group(1)}"
    if re.search(r"(?i)Revert(ed)? to (the )?revision|Reverted edits by", body):
        return f"Gadget revert summary: {body[:90]}"

    # 6. Legacy 2004-2006 Rollback
    m = re.match(r"^Palautett(?:iin|u) viimeiseen (.+?) tekemään muutokseen\.?$", body)
    if m:
        return f"Legacy rollback to last edit by {m.group(1)}"

    gist = _translate_reason_fragment(body)
    if sec_prefix:
        return f"{sec_prefix}: {gist}"
    return gist


def _fmt_dur(seconds: int) -> str:
    if seconds < 60:
        return f"{seconds}s"
    if seconds < 3600:
        return f"{seconds / 60:.1f}m"
    if seconds < 86400:
        return f"{seconds / 3600:.1f}h"
    return f"{seconds / 86400:.1f}d"


def _md_escape(text: str | None) -> str:
    if text is None or text == "":
        return "*(empty)*"
    clean = " ".join(text.splitlines()).replace("`", "'")
    return f"`{clean}`"


def write_class_samples(out_dir: pathlib.Path) -> dict[str, int]:
    sample_counts: dict[str, int] = {}
    for cls in CLASSES:
        rows = aot.sql(f"""
            SELECT r.page_id,
                   p.title,
                   r.reverting_rev_id,
                   r.restored_rev_id,
                   r.first_reverted_rev_id,
                   r.last_reverted_rev_id,
                   r.reverted_count,
                   r.owned_reverted_count,
                   r.reverting_user,
                   r.reverted_user_count,
                   r.reverted_users_anonymous_only,
                   r.revert_ts,
                   r.seconds_to_revert,
                   r.r_bytes,
                   r.net_bytes,
                   r.comment_family,
                   r.reinserted_rev_id,
                   r.exclude_reason,
                   r.reverting_comment
            FROM research.s1_revert r
            JOIN research.s1_page_stats p ON p.page_id = r.page_id
            WHERE r.class = '{cls}'
            ORDER BY r.reverting_rev_id ASC
        """)
        rng = random.Random(f"s1-28-{cls}")
        sampled = rng.sample(rows, min(20, len(rows)))
        sampled.sort(key=lambda r: int(r["reverting_rev_id"]))
        sample_counts[cls] = len(sampled)

        page_ids_csv = ",".join(str(int(r["page_id"])) for r in sampled)
        page_revs_all = aot.sql(f"""
            SELECT page_id, rev_id, user, comment, sha1,
                   LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS prev_sha1
            FROM corpus.revision
            WHERE page_id IN ({page_ids_csv})
            ORDER BY page_id, ts, rev_id
        """)
        revs_by_page: dict[int, list[dict]] = {}
        for pr in page_revs_all:
            revs_by_page.setdefault(int(pr["page_id"]), []).append(pr)

        lines = [
            f"# Sampled `{cls}` reverts (n = {len(sampled)}, seed = `'s1-28-{cls}'`)",
            "",
            "All usernames and edit summaries are masked via `aot.user_display` / `aot.mask_comment`.",
            "",
        ]

        for idx, r in enumerate(sampled, 1):
            page_id = int(r["page_id"])
            r_rev_id = int(r["reverting_rev_id"])
            p_rev_id = int(r["restored_rev_id"])
            last_rev_id = int(r["last_reverted_rev_id"])

            in_span = False
            span_non_null: list[dict] = []
            for sr in revs_by_page.get(page_id, []):
                rid = int(sr["rev_id"])
                if rid == p_rev_id:
                    in_span = True
                    continue
                if rid == r_rev_id:
                    break
                if in_span:
                    if sr["sha1"] is not None and sr["prev_sha1"] is not None and sr["sha1"] == sr["prev_sha1"]:
                        continue
                    span_non_null.append(sr)

            span_users_masked: list[str] = []
            for sr in span_non_null:
                ud = aot.user_display(sr["user"])
                if ud not in span_users_masked:
                    span_users_masked.append(ud)

            last_edit_comment = aot.mask_comment(span_non_null[-1]["comment"]) if span_non_null else None
            rev_user_masked = aot.user_display(r["reverting_user"])
            rev_comment_masked = aot.mask_comment(r["reverting_comment"])
            gist = english_summary_gist(rev_comment_masked)

            net_b = int(r["net_bytes"])
            net_str = f"{net_b:+d} B"
            sec = int(r["seconds_to_revert"])
            fam = FAMILY_NAMES.get(str(r["comment_family"]), "none") if r["comment_family"] is not None else "none"

            meta_bits = [
                f"`revert_ts`: {r['revert_ts']} UTC",
                f"`span`: {r['reverted_count']} edit(s) in {_fmt_dur(sec)} ({sec}s)",
                f"`r_bytes`: {r['r_bytes']} B (`net_bytes` vs restored: {net_str})",
                f"`comment_family`: {fam}",
            ]
            if r["exclude_reason"]:
                meta_bits.append(f"`exclude_reason`: `{r['exclude_reason']}`")
            if r["reinserted_rev_id"]:
                meta_bits.append(f"`reinserted_rev_id`: [{r['reinserted_rev_id']}](https://fi.wikipedia.org/w/index.php?diff={r['reinserted_rev_id']})")

            lines.extend([
                f"### {idx}. {r['title']} (`page_id={page_id}`)",
                f"- **Diffs:** revert [diff={r_rev_id}](https://fi.wikipedia.org/w/index.php?diff={r_rev_id}) "
                f"· last reverted edit [diff={last_rev_id}](https://fi.wikipedia.org/w/index.php?diff={last_rev_id}) "
                f"(restores `rev_id={p_rev_id}`)",
                f"- **Metrics:** {' · '.join(meta_bits)}",
                f"- **Editors (masked):** `{rev_user_masked}` reverted {', '.join(f'`{u}`' for u in span_users_masked)}",
                f"- **Revert summary (masked):** {_md_escape(rev_comment_masked)}",
                f"- **Last reverted edit summary (masked):** {_md_escape(last_edit_comment)}",
                f"- **English gist of revert summary:** {gist}",
                "",
            ])

        (out_dir / f"sample_{cls}.md").write_text(chr(10).join(lines), encoding="utf-8")
        aot.log(f"[report] wrote results/sample_{cls}.md ({len(sampled)} rows)")

    return sample_counts


def collect_metrics() -> dict:
    by_class = aot.sql("""
        SELECT r.class,
               COUNT(*) AS reverts,
               ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM research.s1_revert), 2) AS pct_reverts,
               SUM(r.owned_reverted_count) AS reverted_edits,
               SUM(r.reverted_count) AS raw_span_edits,
               SUM(r.comment_family IS NOT NULL) AS auto_summary_reverts,
               ROUND(100.0 * SUM(r.comment_family IS NOT NULL) / COUNT(*), 2) AS auto_summary_pct,
               SUM(r.comment_family = 1) AS fam_1_rollback,
               SUM(r.comment_family = 2) AS fam_2_undo_modern,
               SUM(r.comment_family = 3) AS fam_3_flaggedrevs,
               SUM(r.comment_family = 4) AS fam_4_undo_legacy,
               SUM(r.comment_family = 5) AS fam_5_en_gadget,
               SUM(r.comment_family = 6) AS fam_6_rollback_old,
               SUM(r.reinserted_rev_id IS NOT NULL) AS reinserted_count
        FROM research.s1_revert r
        GROUP BY r.class
        ORDER BY FIELD(r.class, 'excluded', 'bot', 'self_revert', 'edit_war', 'dispute', 'graffiti_cleanup', 'other')
    """)

    by_year_reverts = aot.sql("""
        SELECT YEAR(revert_ts) AS yr,
               COUNT(*) AS total_reverts,
               SUM(owned_reverted_count) AS owned_reverted_edits,
               SUM(class = 'excluded') AS excluded,
               SUM(class = 'bot') AS bot,
               SUM(class = 'self_revert') AS self_revert,
               SUM(class = 'edit_war') AS edit_war,
               SUM(class = 'dispute') AS dispute,
               SUM(class = 'graffiti_cleanup') AS graffiti_cleanup,
               SUM(class = 'other') AS other
        FROM research.s1_revert
        GROUP BY YEAR(revert_ts)
        ORDER BY yr ASC
    """)

    by_year_edits = aot.sql("""
        SELECT YEAR(re.ts) AS yr,
               COUNT(*) AS reverted_edits_by_edit_year
        FROM research.s1_reverted_edit re
        GROUP BY YEAR(re.ts)
        ORDER BY yr ASC
    """)
    edits_year_map = {int(r["yr"]): int(r["reverted_edits_by_edit_year"]) for r in by_year_edits}
    for row in by_year_reverts:
        row["reverted_edits_in_edit_year"] = edits_year_map.get(int(row["yr"]), 0)

    excl_breakdown = aot.sql("""
        SELECT exclude_reason,
               SUM(r_bytes = 0) AS r_bytes_zero,
               SUM(r_bytes > 0 AND r_bytes <= 100) AS r_bytes_1_to_100,
               SUM(r_bytes > 100) AS r_bytes_gt_100,
               COUNT(*) AS reverts_removed,
               SUM(reverted_count) AS raw_span_slots
        FROM research.s1_revert
        WHERE class = 'excluded'
        GROUP BY exclude_reason
        ORDER BY FIELD(exclude_reason, 'blanking_collision', 'wide_span')
    """)

    # Exact count of how many canonical non-null reverted edits would exist without exclude_reason vs with it
    reconciliation = aot.sql("""
        WITH raw_ord AS (
          SELECT page_id, rev_id, ts, sha1,
                 LAG(sha1) OVER (PARTITION BY page_id ORDER BY ts, rev_id) AS imm_prev_sha1
          FROM corpus.revision
        )
        SELECT
          (SELECT COUNT(*) FROM corpus.revision) AS total_revisions,
          SUM(sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1) AS dropped_null_revisions,
          SUM(NOT (sha1 IS NOT NULL AND imm_prev_sha1 IS NOT NULL AND sha1 = imm_prev_sha1)) AS non_null_revisions,
          (SELECT COUNT(*) FROM research.s1_revert) AS s1_revert_total,
          (SELECT COUNT(*) FROM research.s1_reverted_edit) AS s1_reverted_edit_total
        FROM raw_ord
    """)[0]

    # Compare restored_rev_id shift vs prof_rev_reverts if prof_rev_reverts exists
    prof_cmp = aot.sql("""
        SELECT COUNT(*) AS matched_reverts,
               SUM(s.restored_rev_id <> p.p_rev_id) AS restored_shifted_past_null_rev,
               SUM(s.reverted_count <> p.run_len) AS span_shortened_by_null_revs
        FROM research.s1_revert s
        JOIN research.prof_rev_reverts p ON p.page_id = s.page_id AND p.r_rev_id = s.reverting_rev_id
    """)[0]

    demo_rows: list[dict] = []
    for topic_label, titles in DEMO_TOPICS:
        quoted = ",".join("'" + t.replace("'", "''") + "'" for t in titles)
        rows = aot.sql(f"""
            SELECT page_id, title, revision_count,
                   excluded_count, bot_count, self_revert_count,
                   edit_war_count, dispute_count, graffiti_cleanup_count, other_count,
                   total, signal_total, last_5y_signal_total
            FROM research.s1_page_stats
            WHERE BINARY title IN ({quoted})
            ORDER BY FIELD(BINARY title, {quoted})
        """)
        demo_rows.append({"topic": topic_label, "articles": rows})

    return {
        "reconciliation": reconciliation,
        "prof_comparison": prof_cmp,
        "by_class": by_class,
        "by_year": by_year_reverts,
        "exclude_breakdown": excl_breakdown,
        "demo_topics": demo_rows,
    }


def main() -> None:
    out_dir = HERE / "results"
    out_dir.mkdir(parents=True, exist_ok=True)

    metrics = collect_metrics()
    sample_counts = write_class_samples(out_dir)
    metrics["sample_counts"] = sample_counts

    timings_path = out_dir / "build_timings.json"
    if timings_path.exists():
        metrics["build_timings"] = json.loads(timings_path.read_text(encoding="utf-8"))

    (out_dir / "report_data.json").write_text(
        json.dumps(metrics, indent=2, ensure_ascii=False) + chr(10),
        encoding="utf-8",
    )
    aot.log("[report] wrote results/report_data.json")


if __name__ == "__main__":
    main()
