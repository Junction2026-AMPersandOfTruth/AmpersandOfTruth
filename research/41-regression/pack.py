#!/usr/bin/env python3
"""P1.P3 (#41): demo-pack recorder, before/after diff, and corpus evidence check.

Standard library Python 3.10+ plus `research/lib/aot.py` for corpus SQL.

Subcommands:
  python3 research/41-regression/pack.py record [--events-dir DIR] [--out PATH]
  python3 research/41-regression/pack.py diff   [--fixture PATH] [--events-dir DIR]
  python3 research/41-regression/pack.py check  [--events-dir DIR]
"""
from __future__ import annotations

import argparse
import datetime
import ipaddress
import json
import pathlib
import re
import os
import subprocess
import sys
import unicodedata
from dataclasses import dataclass, field
from typing import Any, Callable, Iterable

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

DEFAULT_TOPICS_PATH = HERE / "topics.json"
DEFAULT_EVENTS_DIR = HERE / "events"
DEFAULT_FIXTURE_PATH = (
    REPO_ROOT / "tests" / "AmpersandOfTruth.Tests" / "Features" / "Investigations" / "Fixtures" / "demo-pack.json"
)
DEFAULT_PIPELINE_CS = (
    REPO_ROOT / "src" / "AmpersandOfTruth.Web" / "Features" / "Investigations" / "Pipeline" / "InvestigationPipeline.cs"
)
INVESTIGATE_SH = REPO_ROOT / "scripts" / "investigate.sh"

DIFF_URL_RE = re.compile(r"^https://fi\.wikipedia\.org/w/index\.php\?diff=prev&oldid=(\d+)$")
PIPELINE_VERSION_RE = re.compile(r"\bpublic\s+const\s+int\s+Version\s*=\s*(\d+)\s*;")

# Mirrors EditorDisplay.cs / aot.py _C_IPV4 & _C_IPV6 for raw IP detection in text fields.
COMMENT_IPV4_RE = re.compile(r"(?<!\d\.)(?<!\d)\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d)")
COMMENT_IPV6_RE = re.compile(r"(?<![0-9A-Za-z])(?<![0-9A-Fa-f]:)(?<!::)[0-9A-Fa-f:]*:[0-9A-Fa-f:]*(?![0-9A-Za-z:])")

LEVEL_2_OR_ABOVE = frozenset({"contested", "campaign"})
LINK_KEYS = frozenset({"diff_ref", "url", "link", "href"})
LINK_SUFFIXES = ("_url", "_link", "_href", "_ref")


def topic_slug(topic: str) -> str:
    """Port of C# TopicSlug.From: NFC lowercase, letters/digits kept, other runs collapsed to '-'."""
    norm = unicodedata.normalize("NFC", topic.strip()).lower()
    out: list[str] = []
    dash = False
    for ch in norm:
        if ch.isalnum():
            if dash and out:
                out.append("-")
            out.append(ch)
            dash = False
        else:
            dash = True
    return "".join(out)


def _ascii_slug(topic: str) -> str:
    folded = topic_slug(topic).replace("ä", "a").replace("ö", "o").replace("å", "a")
    return re.sub(r"[^a-z0-9]+", "-", folded).strip("-")


def resolve_events_path(events_dir: pathlib.Path, topic: str) -> pathlib.Path:
    """Return the JSONL file path for `topic` inside `events_dir` (checking NFC, NFD, and ASCII-folded names)."""
    canonical = events_dir / f"{topic_slug(topic)}.jsonl"
    if canonical.exists():
        return canonical
    for candidate_name in (
        unicodedata.normalize("NFD", f"{topic_slug(topic)}.jsonl"),
        f"{_ascii_slug(topic)}.jsonl",
    ):
        alt = events_dir / candidate_name
        if alt.exists():
            return alt
    return canonical


def incident_key(inserting_rev_ids: Iterable[int]) -> str:
    """CONTRACT.md §1 incident identity: inserting rev ids ascending, joined with '+'."""
    return "+".join(str(int(r)) for r in sorted({int(x) for x in inserting_rev_ids}))


def read_git_commit(repo_root: pathlib.Path = REPO_ROOT) -> str:
    res = subprocess.run(
        ["git", "rev-parse", "--short", "HEAD"],
        cwd=repo_root,
        capture_output=True,
        text=True,
        check=True,
    )
    return res.stdout.strip()


def read_pipeline_version(pipeline_cs: pathlib.Path = DEFAULT_PIPELINE_CS) -> int:
    text = pipeline_cs.read_text(encoding="utf-8")
    m = PIPELINE_VERSION_RE.search(text)
    if not m:
        raise ValueError(f"Could not parse InvestigationPipeline.Version from {pipeline_cs}")
    return int(m.group(1))


def load_topics(path: pathlib.Path = DEFAULT_TOPICS_PATH) -> list[dict[str, str]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise ValueError(f"{path}: expected a JSON array of topic specs")
    for entry in data:
        if entry.get("kind") not in ("demo", "out-of-scope", "quiet") or not entry.get("topic"):
            raise ValueError(f"{path}: invalid topic spec {entry!r}")
    return data


# ── Event stream parsing ─────────────────────────────────────────────────────────────────────────────────────────────

@dataclass
class ParsedRun:
    scope: dict[str, Any] | None
    articles: list[dict[str, Any]]
    candidates: int
    classifications: dict[str, dict[str, Any]]
    incidents: list[dict[str, Any]]
    degraded: int = 0
    unranked: int = 0
    search_fallback: bool = False


def _candidate_key(candidate: dict[str, Any]) -> str:
    if candidate.get("key"):
        return str(candidate["key"])
    page_id = candidate["article"]["page_id"]
    rev_id = candidate["revert"]["rev_id"]
    return f"{page_id}-{rev_id}"


def _normalize_article(raw: dict[str, Any]) -> dict[str, Any]:
    art: dict[str, Any] = {
        "lang": raw["lang"],
        "page_id": int(raw["page_id"]),
        "title": raw["title"],
        "distance": raw["distance"],
    }
    if raw.get("wikidata_id") is not None:
        art["wikidata_id"] = raw["wikidata_id"]
    if raw.get("via_lang") is not None:
        art["via_lang"] = raw["via_lang"]
    return art


def _rebuild_classification_from_incident(incident: dict[str, Any]) -> dict[str, Any] | None:
    assessment = incident.get("assessment")
    if assessment is None:
        return None
    return {
        "category": assessment["category"],
        "confidence": int(assessment["confidence"]),
        "what_changed_en": assessment.get("what_changed_en"),
        "evidence": list(assessment.get("quotes") or []),
        "claim_en": incident.get("claim_en"),
        "claim_fi": incident.get("claim_fi"),
        "narrative_en": incident.get("narrative"),
        "historical_note_en": assessment.get("historical_note_en"),
        "uncertainty_note": assessment.get("uncertainty_note"),
    }


def _graffiti_classification() -> dict[str, Any]:
    return {
        "category": "vandalism",
        "confidence": 5,
        "what_changed_en": None,
        "evidence": [],
        "claim_en": None,
        "claim_fi": None,
        "narrative_en": None,
        "historical_note_en": None,
        "uncertainty_note": None,
    }


def parse_events_jsonl(lines: Iterable[str], *, source: str = "<stream>") -> ParsedRun:
    scope: dict[str, Any] | None = None
    articles: list[dict[str, Any]] = []
    candidates_count = 0
    candidate_order: list[str] = []
    raw_classifications: dict[str, dict[str, Any]] = {}
    completed_incidents: list[dict[str, Any]] | None = None
    degraded = 0
    unranked = 0
    search_fallback = False

    for line_no, raw_line in enumerate(lines, start=1):
        stripped = raw_line.strip()
        if not stripped:
            continue
        try:
            ev = json.loads(stripped)
        except json.JSONDecodeError as exc:
            raise ValueError(f"{source}:{line_no}: invalid JSON: {exc}") from exc

        kind = ev.get("event")
        if kind == "articles_found":
            raw_scope = ev.get("scope")
            if raw_scope is not None:
                scope = {
                    "in_scope": bool(raw_scope["in_scope"]),
                    "reason": raw_scope.get("reason"),
                }
            else:
                scope = None
            articles = [_normalize_article(item) for item in (ev.get("items") or [])]
            search_fallback = bool(ev.get("search_fallback", False))
        elif kind == "revisions_scanned":
            cands = ev.get("candidates") or []
            candidates_count = len(cands)
            candidate_order = [_candidate_key(c) for c in cands]
        elif kind == "candidate_scored":
            cand = ev.get("candidate") or {}
            inc = ev.get("incident")
            exclusion = ev.get("exclusion")
            if inc is not None:
                key = str(inc.get("id") or _candidate_key(cand))
                rebuilt = _rebuild_classification_from_incident(inc)
                if rebuilt is not None:
                    raw_classifications[key] = rebuilt
            elif exclusion == "graffiti":
                key = _candidate_key(cand)
                raw_classifications[key] = _graffiti_classification()
        elif kind == "stage_failed":
            raise RuntimeError(
                f"{source}: pipeline stage '{ev.get('stage')}' failed ({ev.get('kind')}): {ev.get('error')}"
            )
        elif kind == "completed":
            completed_incidents = list(ev.get("incidents") or [])
            degraded = int(ev.get("degraded") or 0)
            unranked = int(ev.get("unranked") or 0)
        else:
            raise ValueError(f"{source}:{line_no}: unknown event {kind!r}")

    if completed_incidents is None:
        raise RuntimeError(f"{source}: incomplete event stream (missing 'completed' event)")

    # Order classifications deterministically: scan order first, then any remaining keys sorted.
    ordered_classifications: dict[str, dict[str, Any]] = {}
    for k in candidate_order:
        if k in raw_classifications:
            ordered_classifications[k] = raw_classifications[k]
    for k in sorted(raw_classifications):
        if k not in ordered_classifications:
            ordered_classifications[k] = raw_classifications[k]

    return ParsedRun(
        scope=scope,
        articles=articles,
        candidates=candidates_count,
        classifications=ordered_classifications,
        incidents=completed_incidents,
        degraded=degraded,
        unranked=unranked,
        search_fallback=search_fallback,
    )


def run_live_events(topic: str, save_dir: pathlib.Path | None = None) -> list[str]:
    # Always the real pipeline: the app's default `Investigations:Source` is `Fixtures` (HybridFixtureRunner replays the
    # P2 sample incidents), which would lock sample incidents into the regression pack.
    env = dict(os.environ, Investigations__Source="Pipeline")
    res = subprocess.run(
        [str(INVESTIGATE_SH), "run", topic, "--events"],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        env=env,
    )
    if res.returncode != 0:
        detail = (res.stderr or res.stdout or "").strip()
        raise RuntimeError(f"investigate.sh failed for {topic!r} (exit {res.returncode}): {detail}")
    raw_text = res.stdout
    if save_dir is not None:
        save_dir.mkdir(parents=True, exist_ok=True)
        out_file = save_dir / f"{topic_slug(topic)}.jsonl"
        out_file.write_text(raw_text, encoding="utf-8")
    return raw_text.splitlines()


def load_or_run_topic(
    topic: str,
    *,
    events_dir: pathlib.Path | None = None,
    save_live_dir: pathlib.Path | None = None,
) -> ParsedRun:
    if events_dir is not None:
        path = resolve_events_path(events_dir, topic)
        if not path.exists():
            raise FileNotFoundError(f"Missing events file for {topic!r}: {path}")
        lines = path.read_text(encoding="utf-8").splitlines()
        return parse_events_jsonl(lines, source=str(path))

    lines = run_live_events(topic, save_dir=save_live_dir)
    return parse_events_jsonl(lines, source=f"live:{topic}")


# ── Fixture building (record) ────────────────────────────────────────────────────────────────────────────────────────

def _fixture_incident_entry(rank: int, inc: dict[str, Any]) -> dict[str, Any]:
    ins_ids = [int(r) for r in inc["inserting_rev_ids"]]
    rem_ids = [int(r) for r in inc["removing_rev_ids"]]
    inc_id = inc.get("id") or f"{inc['page_id']}-{rem_ids[0]}"
    return {
        "rank": rank,
        "id": str(inc_id),
        "key": incident_key(ins_ids),
        "page_id": int(inc["page_id"]),
        "tier": inc["tier"],
        "score": inc["score"],
        "inserting_rev_ids": ins_ids,
        "removing_rev_ids": rem_ids,
        "claim_en": inc.get("claim_en"),
    }


def _build_expect(kind: str, scope: dict[str, Any] | None, all_incidents: list[dict[str, Any]]) -> dict[str, Any]:
    if kind == "demo":
        return {
            "no_new_campaign": True,
            "max_contested_or_above": None,
            "out_of_scope": None,
        }
    if kind == "quiet":
        level_2_plus = sum(1 for inc in all_incidents if inc.get("tier") in LEVEL_2_OR_ABOVE)
        return {
            "no_new_campaign": None,
            "max_contested_or_above": level_2_plus,
            "out_of_scope": None,
        }
    if kind == "out-of-scope":
        is_out = scope is not None and scope.get("in_scope") is False
        return {
            "no_new_campaign": None,
            "max_contested_or_above": None,
            "out_of_scope": is_out,
        }
    raise ValueError(f"Unknown topic kind {kind!r}")


def build_fixture(
    topics_spec: list[dict[str, str]],
    runs_by_topic: dict[str, ParsedRun],
    *,
    recorded_at: str,
    git_commit: str,
    pipeline_version: int,
    top_n: int = 10,
) -> dict[str, Any]:
    topic_entries: list[dict[str, Any]] = []
    for spec in topics_spec:
        topic = spec["topic"]
        kind = spec["kind"]
        run = runs_by_topic[topic]
        top_incidents = [
            _fixture_incident_entry(idx + 1, inc)
            for idx, inc in enumerate(run.incidents[:top_n])
        ]
        topic_entries.append(
            {
                "topic": topic,
                "kind": kind,
                "scope": run.scope,
                "articles": run.articles,
                "candidates": run.candidates,
                "classifications": run.classifications,
                "incidents": top_incidents,
                "expect": _build_expect(kind, run.scope, run.incidents),
            }
        )

    return {
        "version": 1,
        "recorded_at": recorded_at,
        "git_commit": git_commit,
        "pipeline_version": pipeline_version,
        "top_n": top_n,
        "topics": topic_entries,
    }


# ── Before/after diff ────────────────────────────────────────────────────────────────────────────────────────────────

@dataclass
class TopicDiff:
    topic: str
    kind: str
    added: list[dict[str, Any]] = field(default_factory=list)
    removed: list[dict[str, Any]] = field(default_factory=list)
    moved: list[tuple[dict[str, Any], int, int]] = field(default_factory=list)  # (cur_inc, old_rank, new_rank)
    tier_changes: list[tuple[str, str, str]] = field(default_factory=list)     # (id, old_tier, new_tier)
    new_campaigns: list[dict[str, Any]] = field(default_factory=list)
    scope_changed: bool = False
    old_scope: dict[str, Any] | None = None
    new_scope: dict[str, Any] | None = None
    articles_added: list[dict[str, Any]] = field(default_factory=list)
    articles_removed: list[dict[str, Any]] = field(default_factory=list)
    articles_reordered: bool = False
    old_articles_count: int = 0
    new_articles_count: int = 0
    old_candidates: int = 0
    new_candidates: int = 0
    broken_expectations: list[str] = field(default_factory=list)


@dataclass
class DiffReport:
    topics: list[TopicDiff]
    markdown: str

    @property
    def ok(self) -> bool:
        return not any(t.broken_expectations for t in self.topics)


def _scope_label(scope: dict[str, Any] | None) -> str:
    if scope is None:
        return "null"
    verdict = "in_scope" if scope.get("in_scope") else "out_of_scope"
    reason = scope.get("reason") or ""
    return f"{verdict} ({reason})" if reason else verdict


def diff_pack(fixture: dict[str, Any], runs_by_topic: dict[str, ParsedRun]) -> DiffReport:
    top_n = int(fixture.get("top_n") or 10)
    topic_diffs: list[TopicDiff] = []
    lines: list[str] = [
        "# Demo-Pack Regression Diff",
        "",
        f"Baseline commit `{fixture.get('git_commit', '?')}` · pipeline v{fixture.get('pipeline_version', '?')} · top_n={top_n}",
        "",
    ]

    for base_topic in fixture.get("topics") or []:
        t_name = base_topic["topic"]
        kind = base_topic["kind"]
        run = runs_by_topic[t_name]

        base_top = list(base_topic.get("incidents") or [])[:top_n]
        cur_top = [_fixture_incident_entry(idx + 1, inc) for idx, inc in enumerate(run.incidents[:top_n])]
        cur_all = [_fixture_incident_entry(idx + 1, inc) for idx, inc in enumerate(run.incidents)]

        base_by_id = {inc["id"]: inc for inc in base_top}
        cur_top_by_id = {inc["id"]: inc for inc in cur_top}
        cur_all_by_id = {inc["id"]: inc for inc in cur_all}

        added = [inc for inc in cur_top if inc["id"] not in base_by_id]
        removed = [inc for inc in base_top if inc["id"] not in cur_top_by_id]
        moved = [
            (inc, int(base_by_id[inc["id"]]["rank"]), int(inc["rank"]))
            for inc in cur_top
            if inc["id"] in base_by_id and int(base_by_id[inc["id"]]["rank"]) != int(inc["rank"])
        ]

        tier_changes: list[tuple[str, str, str]] = []
        seen_tier_ids: set[str] = set()
        for inc in cur_top:
            old = base_by_id.get(inc["id"])
            if old and old["tier"] != inc["tier"]:
                tier_changes.append((inc["id"], old["tier"], inc["tier"]))
                seen_tier_ids.add(inc["id"])
        for old in base_top:
            if old["id"] not in seen_tier_ids and old["id"] in cur_all_by_id:
                cur_inc = cur_all_by_id[old["id"]]
                if old["tier"] != cur_inc["tier"]:
                    tier_changes.append((old["id"], old["tier"], cur_inc["tier"]))

        base_campaign_ids = {inc["id"] for inc in base_top if inc.get("tier") == "campaign"}
        new_campaigns = [
            inc for inc in cur_all
            if inc.get("tier") == "campaign" and inc["id"] not in base_campaign_ids
        ]

        old_scope = base_topic.get("scope")
        new_scope = run.scope
        old_in_scope = None if old_scope is None else bool(old_scope.get("in_scope"))
        new_in_scope = None if new_scope is None else bool(new_scope.get("in_scope"))
        scope_changed = old_in_scope != new_in_scope

        base_arts = list(base_topic.get("articles") or [])
        cur_arts = list(run.articles)
        base_art_ids = [int(a["page_id"]) for a in base_arts]
        cur_art_ids = [int(a["page_id"]) for a in cur_arts]
        base_art_set = set(base_art_ids)
        cur_art_set = set(cur_art_ids)
        articles_added = [a for a in cur_arts if int(a["page_id"]) not in base_art_set]
        articles_removed = [a for a in base_arts if int(a["page_id"]) not in cur_art_set]
        common_old_order = [pid for pid in base_art_ids if pid in cur_art_set]
        common_new_order = [pid for pid in cur_art_ids if pid in base_art_set]
        articles_reordered = common_old_order != common_new_order

        old_candidates = int(base_topic.get("candidates") or 0)
        new_candidates = int(run.candidates)

        expect = base_topic.get("expect") or {}
        broken: list[str] = []
        if expect.get("no_new_campaign") is True and new_campaigns:
            ids_str = ", ".join(c["id"] for c in new_campaigns)
            broken.append(f"`expect.no_new_campaign` failed: {len(new_campaigns)} new campaign item(s) ({ids_str})")
        if expect.get("max_contested_or_above") is not None:
            max_allowed = int(expect["max_contested_or_above"])
            cur_l2 = sum(1 for inc in run.incidents if inc.get("tier") in LEVEL_2_OR_ABOVE)
            if cur_l2 > max_allowed:
                broken.append(
                    f"`expect.max_contested_or_above` failed: got {cur_l2} contested/campaign incident(s), max allowed {max_allowed}"
                )
        if expect.get("out_of_scope") is not None:
            expected_oos = bool(expect["out_of_scope"])
            actual_oos = new_scope is not None and new_scope.get("in_scope") is False
            if actual_oos != expected_oos:
                broken.append(
                    f"`expect.out_of_scope` failed: expected out_of_scope={expected_oos}, got {_scope_label(new_scope)}"
                )

        td = TopicDiff(
            topic=t_name,
            kind=kind,
            added=added,
            removed=removed,
            moved=moved,
            tier_changes=tier_changes,
            new_campaigns=new_campaigns,
            scope_changed=scope_changed,
            old_scope=old_scope,
            new_scope=new_scope,
            articles_added=articles_added,
            articles_removed=articles_removed,
            articles_reordered=articles_reordered,
            old_articles_count=len(base_arts),
            new_articles_count=len(cur_arts),
            old_candidates=old_candidates,
            new_candidates=new_candidates,
            broken_expectations=broken,
        )
        topic_diffs.append(td)

        status = "FAIL" if broken else "OK"
        lines.append(f"## {t_name} (`{kind}`) — {status}")
        lines.append("")
        if new_campaigns:
            lines.append("> **WARNING — NEW `campaign` ITEM(S) DETECTED:**")
            for c in new_campaigns:
                prev = base_by_id.get(c["id"])
                origin = f"was `{prev['tier']}`" if prev else "newly added"
                claim = c.get("claim_en") or "(no claim_en)"
                lines.append(f"> - **{c['id']}** (rank #{c['rank']}, {origin}): {claim}")
            lines.append("")

        if broken:
            for b in broken:
                lines.append(f"- **BROKEN EXPECTATION:** {b}")

        if scope_changed:
            lines.append(f"- **Scope verdict changed:** `{_scope_label(old_scope)}` → `{_scope_label(new_scope)}`")
        else:
            lines.append(f"- Scope verdict: `{_scope_label(new_scope)}` (unchanged)")

        cand_delta = new_candidates - old_candidates
        cand_note = "unchanged" if cand_delta == 0 else f"{cand_delta:+d}"
        lines.append(f"- Candidates: {old_candidates} → {new_candidates} ({cand_note})")

        if articles_added or articles_removed or articles_reordered:
            parts: list[str] = []
            if articles_added:
                parts.append("added " + ", ".join(f"{a['page_id']} ({a['title']})" for a in articles_added))
            if articles_removed:
                parts.append("removed " + ", ".join(f"{a['page_id']} ({a['title']})" for a in articles_removed))
            if articles_reordered:
                parts.append("reordered")
            lines.append(
                f"- Articles: {len(base_arts)} → {len(cur_arts)} ({'; '.join(parts)})"
            )
        else:
            lines.append(f"- Articles: {len(cur_arts)} (unchanged)")

        if not (added or removed or moved or tier_changes):
            lines.append(f"- Top-{top_n} incidents: unchanged ({len(cur_top)} shown)")
        else:
            for inc in added:
                lines.append(
                    f"- **Added in top {top_n}:** `{inc['id']}` at #{inc['rank']} (`{inc['tier']}`, score {inc['score']}) — {inc.get('claim_en') or '(no claim_en)'}"
                )
            for inc in removed:
                lines.append(
                    f"- **Removed from top {top_n}:** `{inc['id']}` (was #{inc['rank']}, `{inc['tier']}`, score {inc['score']}) — {inc.get('claim_en') or '(no claim_en)'}"
                )
            for inc, old_r, new_r in moved:
                lines.append(
                    f"- **Moved in top {top_n}:** `{inc['id']}` #{old_r} → #{new_r} (`{inc['tier']}`, score {inc['score']})"
                )
            for inc_id, old_t, new_t in tier_changes:
                lines.append(f"- **Tier changed:** `{inc_id}` `{old_t}` → `{new_t}`")
        lines.append("")

    overall = "FAILED" if any(t.broken_expectations for t in topic_diffs) else "PASSED"
    lines.append(f"**Overall expectations:** {overall}")
    return DiffReport(topics=topic_diffs, markdown="\n".join(lines) + "\n")


# ── Evidence check (corpus + links + privacy) ────────────────────────────────────────────────────────────────────────

@dataclass
class CheckFailure:
    topic: str
    incident_id: str
    field: str
    message: str


@dataclass
class TopicCheckSummary:
    topic: str
    incidents_checked: int
    failures: list[CheckFailure] = field(default_factory=list)


@dataclass
class CheckReport:
    topics: list[TopicCheckSummary]
    markdown: str

    @property
    def ok(self) -> bool:
        return not any(t.failures for t in self.topics)


def normalize_ts(value: str) -> str:
    """Normalize ISO-8601 ('2014-03-02T18:20:00Z') or MariaDB DATETIME ('2014-03-02 18:20:00') to UTC ISO-8601 Z."""
    s = value.strip()
    for fmt in ("%Y-%m-%dT%H:%M:%SZ", "%Y-%m-%d %H:%M:%S"):
        try:
            dt = datetime.datetime.strptime(s, fmt).replace(tzinfo=datetime.timezone.utc)
            return dt.strftime("%Y-%m-%dT%H:%M:%SZ")
        except ValueError:
            pass
    iso = s[:-1] + "+00:00" if s.endswith("Z") else s
    dt = datetime.datetime.fromisoformat(iso)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=datetime.timezone.utc)
    else:
        dt = dt.astimezone(datetime.timezone.utc)
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


def find_raw_ips(text: str) -> list[str]:
    """Return raw IPv4/IPv6 substrings in `text` (matching EditorDisplay.MaskComment rules)."""
    found: list[str] = []
    for m in COMMENT_IPV4_RE.finditer(text):
        cand = m.group(0)
        parts = cand.split(".")
        if len(parts) == 4 and all(p.isdigit() and 0 <= int(p) <= 255 for p in parts):
            try:
                ipaddress.IPv4Address(".".join(str(int(p)) for p in parts))
                found.append(cand)
            except ValueError:
                pass

    for m in COMMENT_IPV6_RE.finditer(text):
        cand = m.group(0)
        colon_count = cand.count(":")
        has_hex = any(ch in "0123456789abcdefABCDEF" for ch in cand)
        if colon_count < 2 or not has_hex:
            continue
        try:
            ipaddress.IPv6Address(cand)
            found.append(cand)
            continue
        except ValueError:
            pass
        if cand.endswith(":"):
            trimmed = cand[:-1]
            try:
                ipaddress.IPv6Address(trimmed)
                found.append(trimmed)
            except ValueError:
                pass
    return found


def _iter_string_fields(obj: Any, path: str = "") -> Iterable[tuple[str, str]]:
    if isinstance(obj, str):
        yield path, obj
    elif isinstance(obj, dict):
        for k, v in obj.items():
            sub = f"{path}.{k}" if path else str(k)
            yield from _iter_string_fields(v, sub)
    elif isinstance(obj, list):
        for idx, item in enumerate(obj):
            sub = f"{path}[{idx}]"
            yield from _iter_string_fields(item, sub)


def _is_link_field(field_path: str) -> bool:
    leaf = field_path.rsplit(".", 1)[-1]
    leaf = re.sub(r"\[\d+\]$", "", leaf)
    return leaf in LINK_KEYS or leaf.endswith(LINK_SUFFIXES)


def _fetch_revisions_cached(
    pairs: set[tuple[int, int]],
    cache: dict[tuple[int, int], str | None],
    sql_fn: Callable[[str], list[dict[str, Any]]],
) -> None:
    missing = sorted(p for p in pairs if p not in cache)
    for start in range(0, len(missing), 50):
        chunk = missing[start : start + 50]
        cond = " OR ".join(f"(page_id = {int(p)} AND rev_id = {int(r)})" for p, r in chunk)
        rows = sql_fn(f"SELECT page_id, rev_id, ts FROM corpus.revision WHERE {cond}")
        for row in rows:
            key = (int(row["page_id"]), int(row["rev_id"]))
            cache[key] = str(row["ts"]) if row.get("ts") is not None else None
        for p in chunk:
            cache.setdefault(p, None)


def _fetch_rev_texts_cached(
    pairs: set[tuple[int, int]],
    cache: dict[tuple[int, int], str | None],
    sql_fn: Callable[[str], list[dict[str, Any]]],
) -> None:
    missing = sorted(p for p in pairs if p not in cache)
    for start in range(0, len(missing), 20):
        chunk = missing[start : start + 20]
        cond = " OR ".join(f"(page_id = {int(p)} AND rev_id = {int(r)})" for p, r in chunk)
        rows = sql_fn(f"SELECT page_id, rev_id, text FROM corpus.revision_text WHERE {cond}")
        for row in rows:
            key = (int(row["page_id"]), int(row["rev_id"]))
            cache[key] = row.get("text") if row.get("text") is not None else ""
        for p in chunk:
            cache.setdefault(p, None)


def _parent_text(page_id: int, rev_id: int, sql_fn: Callable[[str], list[dict[str, Any]]],
                 cache: dict[tuple[int, int], str]) -> str:
    """Wikitext of the revision just before `rev_id` on its page ('' if none)."""
    if (page_id, rev_id) not in cache:
        rows = sql_fn(
            f"SELECT t.text FROM revision_text t JOIN (SELECT rev_id FROM revision WHERE page_id={int(page_id)} "
            f"AND (ts, rev_id) < (SELECT ts, rev_id FROM revision WHERE page_id={int(page_id)} AND rev_id={int(rev_id)}) "
            f"ORDER BY ts DESC, rev_id DESC LIMIT 1) p ON t.rev_id = p.rev_id WHERE t.page_id={int(page_id)}")
        cache[(page_id, rev_id)] = str((rows[0].get("text") if rows else "") or "")
    return cache[(page_id, rev_id)]


def check_pack(
    demo_runs: list[tuple[str, ParsedRun]],
    *,
    sql_fn: Callable[[str], list[dict[str, Any]]] = aot.sql,
) -> CheckReport:
    rev_meta_cache: dict[tuple[int, int], str | None] = {}
    rev_text_cache: dict[tuple[int, int], str | None] = {}

    # Collect required corpus lookups across all shown incidents of demo topics.
    needed_revs: set[tuple[int, int]] = set()
    needed_texts: set[tuple[int, int]] = set()

    for _, run in demo_runs:
        for inc in run.incidents:
            default_pid = int(inc["page_id"])
            rev_to_page: dict[int, int] = {}
            for e in inc.get("edits") or []:
                rid = int(e["rev_id"])
                epid = int(e.get("page_id") or default_pid)
                rev_to_page[rid] = epid
                needed_revs.add((epid, rid))
            assessment = inc.get("assessment")
            if assessment and (assessment.get("quotes") or []):
                for rid in inc.get("inserting_rev_ids") or []:
                    ir = int(rid)
                    needed_texts.add((rev_to_page.get(ir, default_pid), ir))

    if needed_revs:
        _fetch_revisions_cached(needed_revs, rev_meta_cache, sql_fn)
    if needed_texts:
        _fetch_rev_texts_cached(needed_texts, rev_text_cache, sql_fn)

    summaries: list[TopicCheckSummary] = []
    lines: list[str] = ["# Demo-Pack Evidence Check", ""]
    summary_quotes: list[tuple[str, str, str]] = []  # quotes found verbatim in an edit summary, not in article text
    context_quotes: list[tuple[str, str, str]] = []  # quotes found verbatim in the text before the edit (removed text / context)
    parent_text_cache: dict[tuple[int, int], str] = {}

    for topic, run in demo_runs:
        failures: list[CheckFailure] = []
        for idx, inc in enumerate(run.incidents):
            default_pid = int(inc["page_id"])
            inc_id = str(inc.get("id") or f"{default_pid}-#{idx + 1}")
            ins_revs = [int(r) for r in (inc.get("inserting_rev_ids") or [])]
            rem_revs = [int(r) for r in (inc.get("removing_rev_ids") or [])]
            incident_revs = set(ins_revs) | set(rem_revs)

            rev_to_page: dict[int, int] = {}
            edits = inc.get("edits") or []
            for e in edits:
                rev_to_page[int(e["rev_id"])] = int(e.get("page_id") or default_pid)

            # (a) Every assessment.quotes string occurs verbatim in wikitext of >= 1 inserting_rev_ids
            assessment = inc.get("assessment")
            if assessment is not None:
                quotes = assessment.get("quotes") or []
                for q_idx, quote in enumerate(quotes):
                    field_name = f"assessment.quotes[{q_idx}]"
                    if not isinstance(quote, str) or not quote:
                        failures.append(CheckFailure(topic, inc_id, field_name, "empty or non-string quote"))
                        continue
                    norm_quote = quote.replace("\r\n", "\n").replace("\r", "\n")
                    matched = False
                    for r in ins_revs:
                        raw_wt = rev_text_cache.get((rev_to_page.get(r, default_pid), r)) or ""
                        if quote in raw_wt or norm_quote in raw_wt.replace("\r\n", "\n").replace("\r", "\n"):
                            matched = True
                            break
                    if not matched:
                        # The classifier may also quote its other inputs: the editors' and reverters' edit summaries.
                        # A verbatim summary quote is legitimate evidence (shown as a summary, never as article text).
                        squash = lambda t: " ".join(t.split())
                        edit_summaries = [str(e.get("summary") or "") for e in edits if isinstance(e, dict)]
                        if any(squash(quote) and squash(quote) in squash(sm) for sm in edit_summaries):
                            summary_quotes.append((topic, inc_id, field_name))
                            matched = True
                    if not matched and ins_revs:
                        # ... or the text before the edit (what it removed, or the article context the model was shown).
                        parent_text = _parent_text(default_pid, min(ins_revs), sql_fn, parent_text_cache)
                        if squash(quote) and squash(quote) in squash(parent_text):
                            context_quotes.append((topic, inc_id, field_name))
                            matched = True
                    if not matched:
                        failures.append(
                            CheckFailure(
                                topic,
                                inc_id,
                                field_name,
                                f"quote not found verbatim in any inserting_rev_ids {ins_revs}, the incident's edit summaries or the text before the edit",
                            )
                        )

            # (b) Every edits[].rev_id exists in corpus.revision on that page_id with stated timestamp
            checked_link_paths: set[str] = set()
            for e_idx, edit in enumerate(edits):
                erid = int(edit["rev_id"])
                epid = int(edit.get("page_id") or default_pid)
                db_ts = rev_meta_cache.get((epid, erid))
                if db_ts is None:
                    failures.append(
                        CheckFailure(
                            topic,
                            inc_id,
                            f"edits[{e_idx}].rev_id",
                            f"rev_id {erid} on page_id {epid} not found in corpus.revision",
                        )
                    )
                else:
                    stated_ts = edit.get("timestamp")
                    if not isinstance(stated_ts, str) or not stated_ts.strip():
                        failures.append(
                            CheckFailure(topic, inc_id, f"edits[{e_idx}].timestamp", "missing timestamp")
                        )
                    else:
                        try:
                            norm_stated = normalize_ts(stated_ts)
                            norm_db = normalize_ts(db_ts)
                        except ValueError:
                            norm_stated, norm_db = stated_ts, db_ts
                        if norm_stated != norm_db:
                            failures.append(
                                CheckFailure(
                                    topic,
                                    inc_id,
                                    f"edits[{e_idx}].timestamp",
                                    f"timestamp mismatch for rev {erid}: stated {stated_ts!r}, corpus {norm_db!r}",
                                )
                            )

                # (c) edits[].diff_ref link format and rev ownership
                diff_field = f"edits[{e_idx}].diff_ref"
                checked_link_paths.add(diff_field)
                diff_ref = edit.get("diff_ref")
                m = DIFF_URL_RE.match(diff_ref) if isinstance(diff_ref, str) else None
                if not m:
                    failures.append(
                        CheckFailure(
                            topic,
                            inc_id,
                            diff_field,
                            f"invalid diff link {diff_ref!r} (expected https://fi.wikipedia.org/w/index.php?diff=prev&oldid=<rev_id>)",
                        )
                    )
                else:
                    link_rev = int(m.group(1))
                    if link_rev != erid or link_rev not in incident_revs:
                        failures.append(
                            CheckFailure(
                                topic,
                                inc_id,
                                diff_field,
                                f"diff link oldid={link_rev} does not match edit rev_id={erid} / incident revs",
                            )
                        )

            # (c cont.) Any other link/ref field in the incident, plus (d) raw IP check on every text field
            for field_path, text_val in _iter_string_fields(inc):
                if field_path not in checked_link_paths and _is_link_field(field_path):
                    m = DIFF_URL_RE.match(text_val)
                    if not m or int(m.group(1)) not in incident_revs:
                        failures.append(
                            CheckFailure(
                                topic,
                                inc_id,
                                field_path,
                                f"link {text_val!r} is not https://fi.wikipedia.org/w/index.php?diff=prev&oldid=<rev_id> for an incident rev",
                            )
                        )

                # (d) No raw IPv4/IPv6 address in any text field
                raw_ips = find_raw_ips(text_val)
                if raw_ips:
                    masked = ", ".join(aot.user_display(ip) for ip in raw_ips)
                    failures.append(
                        CheckFailure(
                            topic,
                            inc_id,
                            field_path,
                            f"unmasked IP address(es) found (masked: {masked})",
                        )
                    )

        summary = TopicCheckSummary(topic=topic, incidents_checked=len(run.incidents), failures=failures)
        summaries.append(summary)

        status = "PASS" if not failures else f"FAIL ({len(failures)} issue(s))"
        lines.append(f"## {topic} — {status}")
        lines.append("")
        lines.append(f"- Incidents checked: {len(run.incidents)}")
        if not failures:
            lines.append("- Failures: none")
        else:
            for f in failures:
                lines.append(f"- **FAIL** `{f.incident_id}` · `{f.field}`: {f.message}")
        lines.append("")

    total_checked = sum(s.incidents_checked for s in summaries)
    total_failures = sum(len(s.failures) for s in summaries)
    overall = "PASSED" if total_failures == 0 else f"FAILED ({total_failures} failure(s))"
    lines.append(f"- Quotes matched verbatim in an edit summary (not article text): {len(summary_quotes)}")
    lines.append(f"- Quotes matched verbatim in the text before the edit (removed text or context): {len(context_quotes)}")
    lines.append("")
    lines.append(f"**Summary:** {total_checked} incident(s) checked across {len(summaries)} demo topic(s) — {overall}")
    return CheckReport(topics=summaries, markdown="\n".join(lines) + "\n")


# ── CLI ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

def cmd_record(args: argparse.Namespace) -> int:
    topics_spec = load_topics(pathlib.Path(args.topics))
    events_dir = pathlib.Path(args.events_dir) if args.events_dir else None
    save_live_dir = None if events_dir is not None else pathlib.Path(args.save_events)

    runs_by_topic: dict[str, ParsedRun] = {}
    for entry in topics_spec:
        topic = entry["topic"]
        run = load_or_run_topic(topic, events_dir=events_dir, save_live_dir=save_live_dir)
        if run.degraded > 0 or run.unranked > 0 or run.search_fallback:
            print(
                f"warning: {topic!r} had degraded={run.degraded}, unranked={run.unranked}, search_fallback={run.search_fallback}",
                file=sys.stderr,
            )
        runs_by_topic[topic] = run

    now_utc = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    fixture = build_fixture(
        topics_spec,
        runs_by_topic,
        recorded_at=now_utc,
        git_commit=read_git_commit(),
        pipeline_version=read_pipeline_version(),
        top_n=args.top_n,
    )

    out_path = pathlib.Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(fixture, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {out_path} ({len(topics_spec)} topics, pipeline_version={fixture['pipeline_version']}, commit={fixture['git_commit']})", file=sys.stderr)
    return 0


def cmd_diff(args: argparse.Namespace) -> int:
    fixture_path = pathlib.Path(args.fixture)
    fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
    events_dir = pathlib.Path(args.events_dir) if args.events_dir else None

    runs_by_topic: dict[str, ParsedRun] = {}
    for base_topic in fixture.get("topics") or []:
        topic = base_topic["topic"]
        runs_by_topic[topic] = load_or_run_topic(topic, events_dir=events_dir, save_live_dir=None)

    report = diff_pack(fixture, runs_by_topic)
    sys.stdout.write(report.markdown)
    return 0 if report.ok else 1


def cmd_check(args: argparse.Namespace) -> int:
    topics_spec = load_topics(pathlib.Path(args.topics))
    events_dir = pathlib.Path(args.events_dir) if args.events_dir else None

    demo_runs: list[tuple[str, ParsedRun]] = []
    for entry in topics_spec:
        if entry["kind"] != "demo":
            continue
        topic = entry["topic"]
        run = load_or_run_topic(topic, events_dir=events_dir, save_live_dir=None)
        demo_runs.append((topic, run))

    report = check_pack(demo_runs, sql_fn=aot.sql)
    sys.stdout.write(report.markdown)
    return 0 if report.ok else 1


def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)

    p_rec = sub.add_parser("record", help="Record pipeline output for the 5 pack topics into demo-pack.json")
    p_rec.add_argument("--events-dir", default=None, help="Read pre-saved <slug>.jsonl event streams from DIR instead of running dotnet")
    p_rec.add_argument("--save-events", default=str(DEFAULT_EVENTS_DIR), help="Directory to save raw <slug>.jsonl streams when running live")
    p_rec.add_argument("--out", default=str(DEFAULT_FIXTURE_PATH), help="Output fixture JSON path")
    p_rec.add_argument("--topics", default=str(DEFAULT_TOPICS_PATH), help="Path to topics.json")
    p_rec.add_argument("--top-n", type=int, default=10, help="Number of top incidents per topic to store (default: 10)")
    p_rec.set_defaults(func=cmd_record)

    p_diff = sub.add_parser("diff", help="Compare current pipeline output against demo-pack.json")
    p_diff.add_argument("--fixture", default=str(DEFAULT_FIXTURE_PATH), help="Baseline demo-pack.json path")
    p_diff.add_argument("--events-dir", default=None, help="Read <slug>.jsonl event streams from DIR instead of running dotnet")
    p_diff.set_defaults(func=cmd_diff)

    p_chk = sub.add_parser("check", help="Check corpus evidence, Wikipedia diff links, and IP masking on demo topics")
    p_chk.add_argument("--events-dir", default=None, help="Read <slug>.jsonl event streams from DIR instead of running dotnet")
    p_chk.add_argument("--topics", default=str(DEFAULT_TOPICS_PATH), help="Path to topics.json")
    p_chk.set_defaults(func=cmd_check)

    return ap


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return int(args.func(args))


if __name__ == "__main__":
    sys.exit(main())
