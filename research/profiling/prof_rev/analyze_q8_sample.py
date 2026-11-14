#!/usr/bin/env python3
"""Analyze the deterministic 200-page latest-revision wikitext sample for Q8.

Fetches `corpus.revision_text` by `(page_id, rev_id)` PK only (200 rows from
`research.prof_rev_q8_sample`) via the `mariadb` CLI (`MYSQL_PWD` inherited from env).
"""
from __future__ import annotations

from collections import Counter
import re
import statistics
import subprocess

NL = chr(10)
TAB = chr(9)

SQL = """
SELECT s.page_id, s.rev_id, s.title, s.latest_bytes, HEX(rt.text) AS hex_text
FROM research.prof_rev_q8_sample s
JOIN corpus.revision_text rt
  ON rt.page_id = s.page_id AND rt.rev_id = s.rev_id
ORDER BY s.page_id;
"""

# `<ref` tags (`<ref>`, `<ref name=...>`, `<ref .../>`), excluding `<references...>`
REF_TAG_RE = re.compile(r"<ref(?![a-z])", re.IGNORECASE)
RAW_REF_RE = re.compile(r"<ref", re.IGNORECASE)

# Strip HTML comments and nowiki blocks when parsing templates
COMMENT_NOWIKI_RE = re.compile(r"<!--.*?-->|<nowiki>.*?</nowiki>", re.IGNORECASE | re.DOTALL)

# Match `{{TemplateName` at start of template call
TEMPLATE_RE = re.compile(r"\{\{\s*([^\{\}\|\x0a\x0d<>#]+)")

DISPUTE_PREFIXES = [
    ("{{Neutraalius", re.compile(r"\{\{\s*neutraalius", re.IGNORECASE)),
    ("{{Lähteetön", re.compile(r"\{\{\s*lähteetön", re.IGNORECASE)),
    ("{{Lähde?", re.compile(r"\{\{\s*lähde\?", re.IGNORECASE)),
    ("{{Korjattava", re.compile(r"\{\{\s*korjattava", re.IGNORECASE)),
    ("{{POV", re.compile(r"\{\{\s*pov(?![a-z])", re.IGNORECASE)),
    ("{{Kiistanalainen", re.compile(r"\{\{\s*kiistanalainen", re.IGNORECASE)),
]


def normalize_template_name(raw: str) -> str:
    name = raw.strip().replace("_", " ")
    name = re.sub(r"\s+", " ", name)
    for prefix in ("Malline:", "Template:"):
        if name.lower().startswith(prefix.lower()):
            name = name[len(prefix):].strip()
    if not name:
        return ""
    return name[0].upper() + name[1:]


def main() -> None:
    raw_out = subprocess.check_output(
        ["mariadb", "-h127.0.0.1", "-P3307", "-ucorpus_ro", "corpus", "--batch", "--raw", "-e", SQL],
        text=True,
    )
    lines = raw_out.splitlines()
    assert len(lines) == 201, f"Expected header + 200 rows, got {len(lines)}"

    ref_tag_counts: list[int] = []
    raw_ref_counts: list[int] = []
    dispute_pages: Counter[str] = Counter()
    dispute_occurrences: Counter[str] = Counter()
    dispute_examples: dict[str, list[str]] = {label: [] for label, _ in DISPUTE_PREFIXES}

    tpl_page_freq: Counter[str] = Counter()
    tpl_total_freq: Counter[str] = Counter()

    for line in lines[1:]:
        pid_s, rid_s, title, bytes_s, hex_txt = line.split(TAB, 4)
        txt = bytes.fromhex(hex_txt).decode("utf-8")

        c_tag = len(REF_TAG_RE.findall(txt))
        c_raw = len(RAW_REF_RE.findall(txt))
        ref_tag_counts.append(c_tag)
        raw_ref_counts.append(c_raw)

        clean_txt = COMMENT_NOWIKI_RE.sub("", txt)

        for label, pat in DISPUTE_PREFIXES:
            hits = pat.findall(clean_txt)
            if hits:
                dispute_pages[label] += 1
                dispute_occurrences[label] += len(hits)
                if len(dispute_examples[label]) < 5:
                    dispute_examples[label].append(f"{title} (page_id={pid_s}, hits={len(hits)})")

        seen_on_page: set[str] = set()
        for m in TEMPLATE_RE.finditer(clean_txt):
            tname = normalize_template_name(m.group(1))
            if not tname or tname.startswith(":"):
                continue
            if ":" in tname and tname.split(":", 1)[0].upper() in {
                "DEFAULTSORT", "DISPLAYTITLE", "SORTKEY", "SHORTDESC"
            }:
                continue
            tpl_total_freq[tname] += 1
            seen_on_page.add(tname)
        for tname in seen_on_page:
            tpl_page_freq[tname] += 1

    n = len(ref_tag_counts)
    pages_with_ref_tag = sum(1 for c in ref_tag_counts if c > 0)
    pages_with_raw_ref = sum(1 for c in raw_ref_counts if c > 0)

    print(f"Sample size: {n}")
    print(
        f"<ref> tag (excluding <references>): {pages_with_ref_tag}/{n} "
        f"({100.0 * pages_with_ref_tag / n:.2f}%), "
        f"median={statistics.median(ref_tag_counts):.1f}, "
        f"median_citing={statistics.median([c for c in ref_tag_counts if c > 0]):.1f}, "
        f"mean={statistics.mean(ref_tag_counts):.2f}, "
        f"p25={statistics.quantiles(ref_tag_counts, n=4)[0]:.1f}, "
        f"p75={statistics.quantiles(ref_tag_counts, n=4)[2]:.1f}, "
        f"max={max(ref_tag_counts)}"
    )
    print(
        f"Raw '<ref' substring (includes <references>): {pages_with_raw_ref}/{n} "
        f"({100.0 * pages_with_raw_ref / n:.2f}%), "
        f"median={statistics.median(raw_ref_counts):.1f}, "
        f"mean={statistics.mean(raw_ref_counts):.2f}, "
        f"max={max(raw_ref_counts)}"
    )

    print(NL + "Dispute template prefixes in 200-page sample:")
    for label, _ in DISPUTE_PREFIXES:
        pg = dispute_pages[label]
        occ = dispute_occurrences[label]
        ex = "; ".join(dispute_examples[label])
        print(f"  {label:18s}: {pg:3d} pages ({100.0 * pg / n:5.2f}%), {occ:3d} occurrences | {ex}")

    print(NL + "Top 30 templates in 200-page sample (by page count, tie-broken by total occurrences):")
    top30 = sorted(tpl_page_freq.keys(), key=lambda k: (-tpl_page_freq[k], -tpl_total_freq[k], k))[:30]
    for rank, tname in enumerate(top30, 1):
        pg = tpl_page_freq[tname]
        occ = tpl_total_freq[tname]
        print(f"  {rank:2d}. {{{{{tname}}}}} — {pg} pages ({100.0 * pg / n:.1f}%), {occ} total occurrences")

    print(NL + "All cleanup/dispute/maintenance-looking templates anywhere in the 200-page sample:")
    keywords = (
        "lähde", "lähtee", "viitteetön", "viite", "korjattava", "neutraalius", "pov",
        "kiistanalainen", "tarkistettava", "selvennä", "kenen", "päivitettävä",
        "vanhentunut", "tynkä", "käännös", "yhdistettävä", "merkittävyys", "mainos",
    )
    for tname, pg in sorted(tpl_page_freq.items(), key=lambda kv: (-kv[1], -tpl_total_freq[kv[0]], kv[0])):
        low = tname.lower()
        if any(kw in low for kw in keywords) and low not in {
            "viitteet", "verkkoviite", "lehtiviite", "kirjaviite", "karttaviite", "standardiviite"
        }:
            print(f"  {{{{{tname}}}}} — {pg} pages ({100.0 * pg / n:.1f}%), {tpl_total_freq[tname]} occurrences")


if __name__ == "__main__":
    main()
