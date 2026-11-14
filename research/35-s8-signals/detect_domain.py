"""Detector `source_domain` (P1.S8 #35, group `refs`).

Extracts newly added URLs/domains from reference and external-link additions,
checks them against `domains.json`, and provides `score_candidate_domains` to
measure per-domain revert rates across the S8 candidate set.
"""
from __future__ import annotations

import collections
import json
import pathlib
import re
from urllib.parse import urlparse

KIND = "source_domain"
DOMAINS_PATH = pathlib.Path(__file__).resolve().parent / "domains.json"
VALID_CATEGORIES = frozenset({"state_media", "known_disinfo", "blog_forum", "self_published"})

_URL_RE = re.compile(r"https?://[^\s<>\]|}'\x22]+", re.I)
_ARCHIVE_UNWRAP_RE = re.compile(
    r"^https?://(?:web\.archive\.org/web/\d+[a-z_]*|archive\.(?:is|today|ph)/[^/]+)/(https?://.+)$",
    re.I,
)

_CATEGORY_LABELS = {
    "state_media": "state-controlled media",
    "known_disinfo": "known low-reliability / disinformation site",
    "blog_forum": "blog or discussion forum",
    "self_published": "self-published or user-generated site",
}


def load_domains(path: pathlib.Path = DOMAINS_PATH) -> dict[str, dict]:
    entries = json.loads(path.read_text(encoding="utf-8"))
    by_domain: dict[str, dict] = {}
    for entry in entries:
        dom = entry["domain"].strip().lower().removeprefix("www.")
        cat = entry["category"]
        if cat not in VALID_CATEGORIES:
            raise ValueError(f"invalid domain category {cat!r} for {dom!r}")
        by_domain[dom] = {"domain": dom, "category": cat, "source": entry["source"]}
    return by_domain


_CURATED_DOMAINS = load_domains()


def _extract_urls_and_domains(text: str) -> list[tuple[str, str]]:
    active = re.sub(r"<!--.*?-->", " ", text or "", flags=re.S)
    results: list[tuple[str, str]] = []
    for raw_url in _URL_RE.findall(active):
        u = raw_url.rstrip(".,;:!?)")
        m_arch = _ARCHIVE_UNWRAP_RE.match(u)
        u_orig = m_arch.group(1) if m_arch else u
        try:
            host = (urlparse(u_orig).hostname or "").lower().removeprefix("www.")
        except ValueError:
            host = ""
        if host and host != "web.archive.org":
            results.append((u, host))
    return results


def match_curated_domain(host: str, curated: dict[str, dict] | None = None) -> dict | None:
    table = _CURATED_DOMAINS if curated is None else curated
    h = host.strip().lower().removeprefix("www.")
    if h in table:
        return table[h]
    parts = h.split(".")
    for i in range(1, len(parts) - 1):
        suffix = ".".join(parts[i:])
        if suffix in table:
            return table[suffix]
    return None


def extract_added_domains(cand: dict, *, refs_only: bool = False) -> list[tuple[str, str, dict]]:
    """Return `(domain, url, segment)` for domains newly added in `cand`."""
    diff = cand.get("diff") or {}
    segs = [s for s in (diff.get("segments") or []) if not s.get("moved")]

    removed_domains: set[str] = set()
    for s in segs:
        if s.get("op") != "removed":
            continue
        if refs_only and s.get("kind") != "reference":
            continue
        for _, host in _extract_urls_and_domains(s.get("text") or ""):
            removed_domains.add(host)

    added: list[tuple[str, str, dict]] = []
    seen_hosts: set[str] = set()
    for s in segs:
        if s.get("op") != "added":
            continue
        if refs_only and s.get("kind") != "reference":
            continue
        for url, host in _extract_urls_and_domains(s.get("text") or ""):
            if host in removed_domains or host in seen_hosts:
                continue
            seen_hosts.add(host)
            added.append((host, url, s))
    return added


def detect(cand: dict) -> list[dict]:
    diff = cand.get("diff") or {}
    stats = diff.get("stats") or {}
    if stats.get("is_blanking"):
        return []

    signals: list[dict] = []
    for host, url, seg in extract_added_domains(cand, refs_only=False):
        matched = match_curated_domain(host)
        if matched is None:
            continue
        cat_desc = _CATEGORY_LABELS.get(matched["category"], matched["category"])
        label = f"Added source from {cat_desc}: {host}"
        seg_text = seg.get("text") or url
        pos = seg_text.find(url)
        if pos >= 0 and len(seg_text) > 200:
            start = max(0, pos - 20)
            evidence = seg_text[start : start + 200]
        else:
            evidence = seg_text[:200]
        signals.append({"kind": KIND, "label": label, "evidence": evidence})
    return signals


def score_candidate_domains(candidates: list[dict], *, refs_only: bool = False) -> list[dict]:
    """Compute data-driven revert rate for every newly added domain across `candidates`."""
    counts: dict[str, dict] = collections.defaultdict(
        lambda: {"reverted": 0, "control": 0, "rev_ids": [], "in_ref_seg": 0}
    )
    for cand in candidates:
        grp = cand.get("group")
        if grp not in ("reverted", "control"):
            continue
        for host, _, seg in extract_added_domains(cand, refs_only=refs_only):
            entry = counts[host]
            entry[grp] += 1
            entry["rev_ids"].append(cand.get("rev_id"))
            if seg.get("kind") == "reference":
                entry["in_ref_seg"] += 1

    rows: list[dict] = []
    for host, d in counts.items():
        total = d["reverted"] + d["control"]
        curated = match_curated_domain(host)
        rows.append({
            "domain": host,
            "total_additions": total,
            "reverted_additions": d["reverted"],
            "control_additions": d["control"],
            "reverted_rate": round(d["reverted"] / total, 4) if total else 0.0,
            "in_reference_segments": d["in_ref_seg"],
            "curated_category": curated["category"] if curated else None,
            "rev_ids": d["rev_ids"],
        })
    rows.sort(key=lambda x: (-x["total_additions"], -x["reverted_rate"], x["domain"]))
    return rows
