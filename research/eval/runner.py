#!/usr/bin/env python3
"""Eval runner (P1.E2, #21): run a variant over the golden set and score it. CONTRACT.md §6. Standard library only.

  research/eval/run retrieval --name r1-vector  -- python3 research/8-r1-vector/variant.py
  research/eval/run signals   --name s1-reverts -- python3 research/28-s1-revert/variant.py
  research/eval/run pipeline  --name p1-app     -- scripts/<pipeline variant>
  research/eval/run replay research/28-s1-revert/results/s1-reverts.dev.responses.jsonl --judgements j.jsonl

Options: --split dev|test  --out DIR  --golden PATH  --judgements F.jsonl  --reviews F.jsonl  --baseline SCORECARD.json
         --topics ID,ID (partial run, not comparable)  --limit N (signals, default 50)  --timeout S (120)  --jobs N (1)

Writes to --out (default research/eval/results/, git-ignored), as <name>.<split>.*:
  scorecard.md          the Markdown scorecard, also printed on stdout: paste it into the experiment's issue
  scorecard.json        the same as data. Diff two runs through their `metrics`
  unlabelled.jsonl      shown incidents with no golden item, for the E3 judge (signals and pipeline)
  responses.jsonl       every request and response, so `replay` can rescore without running the variant again
  log                   the variant's stderr for every request
The test split is sealed: --split test needs EVAL_ALLOW_TEST=1 (decision issues R6 and S11 only).
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import shlex
import signal
import subprocess
import sys
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import contract  # noqa: E402
from contract import LENIENT_RELEVANT, LEVELS, STRICT_RELEVANT, incident_key  # noqa: E402

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
DEFAULT_OUT = HERE / "results"
K = 20                      # retrieval: results scored per query; recall@20 is the widest cut
DEFAULT_LIMIT = 50          # signals: incidents scored per topic
PRECISION_AT = (10, 25)
RECALL_AT = (5, 10, 20)
STYLES = ("en", "fi", "broad", "sv")
RETRIEVAL_SCORED = ("topic", "control-quiet")   # kinds in the retrieval headline
OUTCOMES = ("campaign", "contested", "reverted", "not-incident", "duplicate", "unlabelled")
NAME_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._+-]*$")


@dataclass
class Run:
    """One request sent to the variant. `response` is None when the request failed; it then scores as empty."""
    id: str
    topic_id: str
    request: dict
    response: dict | None
    seconds: float
    failure: str | None = None


class UsageError(Exception):
    pass


# ── Small helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

def _mean(values: Iterable[float | None]) -> float | None:
    xs = [v for v in values if v is not None]
    return sum(xs) / len(xs) if xs else None


def _ratio(num: int, den: int) -> float | None:
    return num / den if den else None


def _ndcg(ranked: list[int], grades: dict[int, int], n: int) -> float:
    dcg = sum(grades.get(p, 0) / math.log2(i + 2) for i, p in enumerate(ranked[:n]))
    ideal = sorted(grades.values(), reverse=True)[:n]
    idcg = sum(g / math.log2(i + 2) for i, g in enumerate(ideal))
    return dcg / idcg if idcg else 0.0


def _short(text: str | None, n: int = 110) -> str:
    text = " ".join((text or "").split())
    return text if len(text) <= n else text[: n - 1] + "…"


def _now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def git_commit() -> str:
    try:
        head = subprocess.run(["git", "-C", str(REPO), "rev-parse", "--short=12", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
        dirty = subprocess.run(["git", "-C", str(REPO), "status", "--porcelain", "--untracked-files=no"], capture_output=True, text=True).stdout.strip()
        return head + ("-dirty" if dirty else "")
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def split_topics(golden: dict, split: str, only: list[str] | None = None) -> list[dict]:
    topics = [t for t in golden["topics"] if t["split"] == split]
    if only:
        known = {t["id"] for t in topics}
        unknown = [t for t in only if t not in known]
        if unknown:
            raise UsageError(f"--topics: {', '.join(unknown)} not in the {split} split")
        topics = [t for t in topics if t["id"] in only]
    return topics


# ── Requests and running the variant ─────────────────────────────────────────────────────────────────────────────────

def plan(kind: str, topics: list[dict], limit: int = DEFAULT_LIMIT) -> list[tuple[str, str, dict]]:
    """(request id, topic id, request) in run order. Signals: one per topic with articles. Otherwise one per query."""
    planned = []
    for t in topics:
        if kind == "signals":
            if t["articles"]:
                planned.append((t["id"], t["id"], contract.signals_request(t, limit)))
            continue
        for q in t["queries"]:
            req = contract.retrieval_request(q, K) if kind == "retrieval" else contract.pipeline_request(q, K, limit)
            planned.append((q["id"], t["id"], req))
    return planned


def _kill_group(proc: subprocess.Popen) -> None:
    """A timed-out variant must not keep querying the 2-vCPU VPS: kill its whole process group."""
    try:
        os.killpg(proc.pid, signal.SIGKILL)
    except (OSError, AttributeError):
        proc.kill()


def call_variant(command: list[str], request: dict, timeout: float) -> tuple[dict | None, float, str | None, str]:
    """(response, seconds, failure, stderr). One process per request, as CONTRACT.md §2 promises."""
    started = time.monotonic()
    try:
        proc = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                text=True, encoding="utf-8", errors="replace", start_new_session=True)
    except OSError as ex:
        return None, 0.0, f"cannot start the command: {ex}", ""
    try:
        out, err = proc.communicate(json.dumps(request, ensure_ascii=False), timeout=timeout)
    except subprocess.TimeoutExpired:
        _kill_group(proc)
        _, err = proc.communicate()
        return None, time.monotonic() - started, f"no answer within {timeout:g} s", err or ""
    seconds = time.monotonic() - started
    if proc.returncode != 0:
        return None, seconds, f"exit code {proc.returncode}", err
    try:
        return json.loads(out), seconds, None, err
    except json.JSONDecodeError as ex:
        return None, seconds, f"stdout is not one JSON document ({ex.msg}); logs belong on stderr", err


def check_response(kind: str, request: dict, response: dict) -> str | None:
    rep = contract.validate(f"{kind}-response", response, request=request)
    if rep.ok:
        return None
    more = f" (+{len(rep.errors) - 3} more)" if len(rep.errors) > 3 else ""
    return "response does not match the contract: " + "; ".join(rep.errors[:3]) + more


def run_variant(kind: str, command: list[str], planned: list[tuple[str, str, dict]], timeout: float, jobs: int,
                log_path: Path | None = None) -> list[Run]:
    def one(entry: tuple[str, str, dict]) -> tuple[Run, str]:
        rid, tid, req = entry
        response, seconds, failure, stderr = call_variant(command, req, timeout)
        if response is not None:
            failure = check_response(kind, req, response)
            if failure:
                response = None
        return Run(rid, tid, req, response, seconds, failure), stderr

    runs: list[Run] = []
    log = log_path.open("w", encoding="utf-8") if log_path else None
    try:
        with ThreadPoolExecutor(max_workers=max(1, jobs)) as pool:
            for n, (run, stderr) in enumerate(pool.map(one, planned), 1):
                status = f"FAILED: {run.failure}" if run.failure else "ok"
                print(f"[{n}/{len(planned)}] {run.id}: {run.seconds:.1f} s, {status}", file=sys.stderr)
                if log:
                    log.write(f"── {run.id} ({run.seconds:.1f} s, {status}) ──\n{stderr.rstrip()}\n")
                runs.append(run)
    finally:
        if log:
            log.close()
    return runs


# ── Scoring: retrieval ───────────────────────────────────────────────────────────────────────────────────────────────

def retrieval_row(topic: dict, query: dict, run: Run) -> dict:
    resp = run.response
    oos = bool(resp and resp["out_of_scope"])
    ranked = [] if resp is None or oos else [r["page_id"] for r in resp["results"][:K]]
    row: dict[str, Any] = {"query_id": query["id"], "topic_id": topic["id"], "kind": topic["kind"], "style": query["style"],
                           "out_of_scope": oos, "returned": len(ranked), "seconds": round(run.seconds, 2),
                           "failed": run.failure is not None}
    grades = {a["page_id"]: a["grade"] for a in topic["articles"]}
    if grades:
        found = set(ranked)
        for n in RECALL_AT:
            row[f"recall@{n}"] = len(set(ranked[:n]) & grades.keys()) / len(grades)
        row["ndcg@10"] = _ndcg(ranked, grades, 10)
        rank = {p: i for i, p in enumerate(ranked, 1)}
        row["articles"] = [{"page_id": a["page_id"], "grade": a["grade"], "title": a["title"], "rank": rank.get(a["page_id"])}
                           for a in sorted(topic["articles"], key=lambda a: (-a["grade"], a["page_id"]))]
        row["missed"] = [a["page_id"] for a in topic["articles"] if a["page_id"] not in found]
    return row


def retrieval_metrics(rows: list[dict]) -> tuple[dict, dict]:
    scored = [r for r in rows if r["kind"] in RETRIEVAL_SCORED and "recall@10" in r]

    def block(rs: list[dict]) -> dict:
        b: dict[str, Any] = {f"recall@{n}": _mean(r[f"recall@{n}"] for r in rs) for n in RECALL_AT}
        b["ndcg@10"] = _mean(r["ndcg@10"] for r in rs)
        return b

    metrics = block(scored)
    oos = [r for r in rows if r["kind"] == "control-out-of-corpus"]
    metrics["oos_detection_rate"] = _mean(1.0 if r["out_of_scope"] else 0.0 for r in oos)
    metrics["oos_false_alarm_rate"] = _mean(1.0 if r["out_of_scope"] else 0.0 for r in rows if r["kind"] != "control-out-of-corpus")
    by_style = {s: {"queries": len(rs), **block(rs)} for s in STYLES if (rs := [r for r in scored if r["style"] == s])}
    return metrics, by_style


def retrieval_examples(rows: list[dict], n: int = 5) -> tuple[list[dict], list[dict]]:
    scored = [r for r in rows if r["kind"] in RETRIEVAL_SCORED and "recall@10" in r]

    def ex(r: dict) -> dict:
        return {"query_id": r["query_id"], "style": r["style"], "recall@10": r["recall@10"], "ndcg@10": r["ndcg@10"],
                "missed": [a for a in r["articles"] if a["rank"] is None or a["rank"] > 10]}

    best = sorted((r for r in scored if r["ndcg@10"] > 0), key=lambda r: (-r["ndcg@10"], -r["recall@10"], r["query_id"]))[:n]
    worst = sorted((r for r in scored if r["ndcg@10"] < 1), key=lambda r: (r["recall@10"], r["ndcg@10"], r["query_id"]))[:n]
    return [ex(r) for r in best], [ex(r) for r in worst]


# ── Scoring: signals ─────────────────────────────────────────────────────────────────────────────────────────────────

class GoldenIndex:
    """The golden items of one split. Items of every topic in the split are match candidates (CONTRACT.md §6)."""

    def __init__(self, topics: list[dict]):
        self.items: dict[str, dict] = {}
        self.owner: dict[int, str] = {}
        for t in topics:
            for it in t["items"]:
                self.items[it["id"]] = dict(it, topic_id=t["id"])
                for rev in it["inserting_rev_ids"]:
                    self.owner[rev] = it["id"]

    def expected(self, topic: dict) -> list[str]:
        """Golden contested/campaign items on any of the topic's golden articles."""
        pages = {a["page_id"] for a in topic["articles"]}
        return sorted(i for i, it in self.items.items()
                      if it["label"] in STRICT_RELEVANT and {it["page_id"], *it.get("other_page_ids", [])} & pages)


def match(incidents: list[dict], index: GoldenIndex) -> list[dict]:
    """Rank-ordered outcomes. Match state is per topic: an item is matched at most once, by the highest-ranked incident."""
    matched: set[str] = set()
    outcomes = []
    for rank, inc in enumerate(incidents, 1):
        inserting = set(inc["inserting_rev_ids"])
        hits = {index.owner[r] for r in inserting if r in index.owner}
        o: dict[str, Any] = {"rank": rank, "key": incident_key(inc["inserting_rev_ids"]), "page_id": inc["page_id"],
                             "tier": inc["tier"], "outcome": "unlabelled", "item_id": None}
        if hits:
            free = [index.items[i] for i in hits if i not in matched]
            if free:
                best = min(free, key=lambda it: (-LEVELS[it["label"]], -len(inserting & set(it["inserting_rev_ids"])), it["id"]))
                matched.add(best["id"])
                o.update(outcome=best["label"], item_id=best["id"])
            else:
                o.update(outcome="duplicate", item_id=min(hits, key=lambda i: (-LEVELS[index.items[i]["label"]], i)))
        outcomes.append(o)
    return outcomes


def _judged_relevant(o: dict, judgements: dict[str, dict], reviews: dict[str, dict]) -> bool:
    """Golden label wins. An unlabelled hit takes the judge's label, unless a review calls it benign. Unjudged = miss."""
    if o["outcome"] != "unlabelled":
        return o["outcome"] in STRICT_RELEVANT
    j = judgements.get(o["key"])
    return bool(j) and j["label"] in STRICT_RELEVANT and reviews.get(o["key"], {}).get("verdict") != "benign"


def signals_row(unit_id: str, topic: dict, run: Run, index: GoldenIndex, limit: int,
                judgements: dict[str, dict] | None, reviews: dict[str, dict]) -> tuple[dict, list[dict]]:
    """Score one topic run (signals: per topic; pipeline: per query). Returns the row and its unlabelled lines."""
    resp = run.response
    incidents = [] if resp is None or resp.get("out_of_scope") else resp["incidents"][:limit]
    outcomes = match(incidents, index)
    expected = index.expected(topic)
    found = sorted({o["item_id"] for o in outcomes if o["item_id"] in expected and o["outcome"] != "duplicate"})
    row: dict[str, Any] = {"id": unit_id, "topic_id": topic["id"], "kind": topic["kind"], "shown": len(outcomes),
                           "seconds": round(run.seconds, 2), "failed": run.failure is not None,
                           "outcomes": dict(Counter(o["outcome"] for o in outcomes))}
    for k in PRECISION_AT:
        top = outcomes[:k]
        labelled = [o for o in top if o["outcome"] != "unlabelled"]
        row[f"strict_p@{k}"] = _ratio(sum(o["outcome"] in STRICT_RELEVANT for o in labelled), len(labelled))
        row[f"lenient_p@{k}"] = _ratio(sum(o["outcome"] in LENIENT_RELEVANT for o in labelled), len(labelled))
        row[f"unlabelled@{k}"] = len(top) - len(labelled)
        if judgements is not None:
            row[f"strict_p@{k}_judged"] = _ratio(sum(_judged_relevant(o, judgements, reviews) for o in top), len(top))
            row[f"unjudged@{k}"] = sum(o["outcome"] == "unlabelled" and o["key"] not in judgements for o in top)
    level_matched = [o for o in outcomes if o["outcome"] in LEVELS]
    row.update(expected=len(expected), found=found, missed=[i for i in expected if i not in found],
               matched=len(level_matched),
               level_correct=sum(o["tier"] == o["outcome"] for o in level_matched),
               overclaims=sum(LEVELS[o["tier"]] > LEVELS[o["outcome"]] for o in level_matched),
               duplicates=sum(o["outcome"] == "duplicate" for o in outcomes),
               shown_contested_or_above=sum(o["tier"] in STRICT_RELEVANT for o in outcomes),
               ranked=outcomes)
    unlabelled = []
    for inc, o in zip(incidents, outcomes):
        if o["outcome"] == "unlabelled":
            line = {"key": o["key"], "topic_id": topic["id"], "rank": o["rank"]}
            if unit_id != topic["id"]:
                line["query_id"] = unit_id
            line.update({f: inc[f] for f in ("page_id", "inserting_rev_ids", "removing_rev_ids", "tier", "score")})
            line["claim_en"] = inc.get("claim_en")
            unlabelled.append(line)
    return row, unlabelled


def signals_metrics(rows: list[dict], judged: bool) -> dict:
    topic_rows = [r for r in rows if r["kind"] == "topic"]
    m: dict[str, Any] = {}
    for k in PRECISION_AT:
        m[f"strict_p@{k}"] = _mean(r[f"strict_p@{k}"] for r in topic_rows)
    for k in PRECISION_AT:
        m[f"lenient_p@{k}"] = _mean(r[f"lenient_p@{k}"] for r in topic_rows)
    for k in PRECISION_AT:
        m[f"unlabelled@{k}"] = _mean(r[f"unlabelled@{k}"] for r in topic_rows)
    if judged:
        for k in PRECISION_AT:
            m[f"strict_p@{k}_judged"] = _mean(r[f"strict_p@{k}_judged"] for r in topic_rows)
        for k in PRECISION_AT:
            m[f"unjudged@{k}"] = sum(r[f"unjudged@{k}"] for r in topic_rows)
    matched = sum(r["matched"] for r in rows)
    m["recall"] = _ratio(sum(len(r["found"]) for r in rows), sum(r["expected"] for r in rows))
    m["level_accuracy"] = _ratio(sum(r["level_correct"] for r in rows), matched)
    m["overclaim_rate"] = _ratio(sum(r["overclaims"] for r in rows), matched)
    m["duplicates"] = sum(r["duplicates"] for r in rows)
    quiet = [r for r in rows if r["kind"] == "control-quiet"]
    m["quiet_shown"] = sum(r["shown"] for r in quiet)
    m["quiet_contested_or_above"] = sum(r["shown_contested_or_above"] for r in quiet)
    oos = [r for r in rows if r["kind"] == "control-out-of-corpus"]
    if oos:
        m["oos_incidents_shown"] = sum(r["shown"] for r in oos)
    return m


def signals_examples(rows: list[dict], index: GoldenIndex, n: int = 5) -> tuple[list[dict], list[dict]]:
    """best = highest-ranked true positives. worst = missed contested/campaign items, then false positives,
    overclaims and duplicates by rank. Each item appears once."""
    def item(i: str) -> dict:
        it = index.items[i]
        return {"item_id": i, "label": it["label"], "exclusion": it.get("exclusion"), "page_id": it["page_id"],
                "description": it["description"]}

    shown = sorted(((r["id"], o) for r in rows for o in r["ranked"]), key=lambda x: (x[1]["rank"], x[0]))
    best = [{"kind": "true_positive", "unit": u, "rank": o["rank"], "tier": o["tier"], **item(o["item_id"])}
            for u, o in shown if o["outcome"] in STRICT_RELEVANT]
    missed = sorted({(i, r["id"]) for r in rows for i in r["missed"]},
                    key=lambda x: (-LEVELS[index.items[x[0]]["label"]], x[0], x[1]))
    worst = [{"kind": "missed", "unit": u, "rank": None, "tier": None, **item(i)} for i, u in missed]
    worst += [{"kind": "false_positive", "unit": u, "rank": o["rank"], "tier": o["tier"], **item(o["item_id"])}
              for u, o in shown if o["outcome"] == "not-incident"]
    worst += [{"kind": "overclaim", "unit": u, "rank": o["rank"], "tier": o["tier"], **item(o["item_id"])}
              for u, o in shown if o["outcome"] in LEVELS and o["outcome"] != "not-incident" and LEVELS[o["tier"]] > LEVELS[o["outcome"]]]
    worst += [{"kind": "duplicate", "unit": u, "rank": o["rank"], "tier": o["tier"], **item(o["item_id"])}
              for u, o in shown if o["outcome"] == "duplicate"]

    def first(entries: list[dict]) -> list[dict]:
        seen, out = set(), []
        for e in entries:
            if (e["kind"], e["item_id"]) not in seen:
                seen.add((e["kind"], e["item_id"]))
                out.append(e)
        return out[:n]
    return first(best), first(worst)


def challenges(rows: list[dict], index: GoldenIndex, reviews: dict[str, dict]) -> list[dict]:
    """Reviews of golden-labelled hits change no score; a person reads the ones that are not `survives`."""
    out = []
    for r in rows:
        for o in r["ranked"]:
            rev = reviews.get(o["key"])
            if rev and o["outcome"] in LEVELS and rev["verdict"] != "survives":
                out.append({"key": o["key"], "unit": r["id"], "rank": o["rank"], "item_id": o["item_id"],
                            "label": index.items[o["item_id"]]["label"], "verdict": rev["verdict"], "reason": rev["reason"],
                            "reviewer": rev["reviewer"]})
    return out


# ── Scorecard ────────────────────────────────────────────────────────────────────────────────────────────────────────

def cost_block(runs: list[Run], jobs: int) -> dict:
    totals = Counter()
    usd = 0.0
    for r in runs:
        c = (r.response or {}).get("cost") or {}
        for f in ("llm_calls", "input_tokens", "output_tokens", "sql_queries"):
            totals[f] += int(c.get(f) or 0)
        usd += float(c.get("cost_usd") or 0)
    secs = [r.seconds for r in runs]
    return {"requests": len(runs), "jobs": jobs, "seconds_total": round(sum(secs), 1),
            "seconds_per_request": round(_mean(secs) or 0.0, 2), "seconds_max": round(max(secs, default=0.0), 2),
            **{f: totals[f] for f in ("llm_calls", "input_tokens", "output_tokens", "sql_queries")},
            "cost_usd": round(usd, 4)}


def score(kind: str, golden: dict, split: str, runs: list[Run], *, limit: int = DEFAULT_LIMIT,
          judgements: dict[str, dict] | None = None, reviews: dict[str, dict] | None = None, jobs: int = 1) -> dict:
    """Everything in the scorecard that depends on the golden set and the responses (no run metadata)."""
    topics = {t["id"]: t for t in golden["topics"] if t["split"] == split}
    queries = {q["id"]: (t, q) for t in topics.values() for q in t["queries"]}
    reviews = reviews or {}
    index = GoldenIndex(list(topics.values()))
    card: dict[str, Any] = {}
    if kind in ("retrieval", "pipeline"):
        qrows = [retrieval_row(*queries[r.id], r) for r in runs]
        metrics, by_style = retrieval_metrics(qrows)
        card.update(metrics=metrics, by_style=by_style)
    if kind == "retrieval":
        card["per_topic"] = qrows
        card["best"], card["worst"] = retrieval_examples(qrows)
    else:
        rows, unlabelled = [], []
        for r in runs:
            row, lines = signals_row(r.id, topics[r.topic_id], r, index, limit, judgements, reviews)
            rows.append(row)
            unlabelled += lines
        card.setdefault("metrics", {}).update(signals_metrics(rows, judgements is not None))
        card["per_topic"] = rows
        if kind == "pipeline":
            card["per_query"] = qrows
            card["worst_queries"] = retrieval_examples(qrows)[1]
        card["best"], card["worst"] = signals_examples(rows, index)
        card["unlabelled"] = unlabelled
        if reviews:
            card["challenges"] = challenges(rows, index, reviews)
    cost = cost_block(runs, jobs)
    card["metrics"].update(seconds_per_topic=cost["seconds_per_request"], llm_calls=cost["llm_calls"],
                           input_tokens=cost["input_tokens"], output_tokens=cost["output_tokens"])
    card["failures"] = [{"id": r.id, "reason": r.failure, "seconds": round(r.seconds, 2)} for r in runs if r.failure]
    card["cost"] = cost
    return card


def scorecard(kind: str, name: str, golden: dict, split: str, runs: list[Run], *, command: str, git: str, ran_at: str,
              limit: int, partial: bool, judgements: dict | None = None, reviews: dict | None = None,
              sources: dict | None = None, jobs: int = 1) -> dict:
    head = {"contract": contract.CONTRACT, "kind": kind, "variant": name, "split": split,
            "golden_version": golden["version"], "dataset": golden["dataset"], "git_commit": git, "ran_at": ran_at,
            "command": command, "limit": limit if kind != "retrieval" else None, "k": K, "partial": partial,
            "topics": len({r.topic_id for r in runs}), "requests": len(runs), **(sources or {})}
    return {**head, **score(kind, golden, split, runs, limit=limit, judgements=judgements, reviews=reviews, jobs=jobs)}


# ── Markdown ─────────────────────────────────────────────────────────────────────────────────────────────────────────

def _fmt(v: Any) -> str:
    if v is None:
        return "–"
    if isinstance(v, bool):
        return "yes" if v else "no"
    if isinstance(v, int):
        return f"{v:,}"
    if isinstance(v, float):
        return f"{v:.2f}"
    return str(v)


def _delta(v: Any, b: Any) -> str:
    if isinstance(v, bool) or isinstance(b, bool) or not isinstance(v, (int, float)) or not isinstance(b, (int, float)):
        return ""
    d = v - b
    if isinstance(v, int) and isinstance(b, int):
        return f"{d:+,}" if d else "0"
    return f"{d:+.2f}" if abs(d) >= 0.005 else "0.00"


def comparable(card: dict, baseline: dict) -> str | None:
    """None when the two scorecards can be compared, else why not."""
    kinds = {card["kind"], baseline.get("kind")}
    if len(kinds) > 1 and "pipeline" not in kinds:
        return f"the baseline is a {baseline.get('kind')} run"
    for f in ("split", "golden_version"):
        if baseline.get(f) != card[f]:
            return f"{f} differs ({baseline.get(f)} vs {card[f]})"
    if baseline.get("partial") or card["partial"]:
        return "one of the runs is partial (--topics)"
    return None


def render_markdown(card: dict, baseline: dict | None = None, files: dict[str, str] | None = None) -> str:
    kind, out = card["kind"], []
    title = f"### Eval scorecard · `{card['variant']}` · {kind} · {card['split']} split"
    out += [title, ""]
    when = card["ran_at"].replace("T", " ").replace("Z", " UTC")
    fails = len(card["failures"])
    out.append(f"Golden `{card['golden_version']}` (`{card['dataset']}`) · {card['topics']} topics, {card['requests']} requests"
               + (f" · limit {card['limit']}" if card.get("limit") else "")
               + f" · commit `{card['git_commit']}` · {when} · " + (f"**{fails} failed**" if fails else "0 failed"))
    if card["partial"]:
        out.append("\n> **Partial run (`--topics`).** Not comparable with other scorecards.")
    if card.get("golden_version", "").startswith("example"):
        out.append("\n> **Synthetic golden set (`examples/`).** These numbers mean nothing.")
    why_not = comparable(card, baseline) if baseline else None
    if baseline and why_not:
        out.append(f"\n> Baseline `{baseline.get('variant')}` not compared: {why_not}.")
    show_delta = bool(baseline) and not why_not
    out.append("")
    out.append("| Metric | Value |" + (f" Δ vs `{baseline['variant']}` |" if show_delta else ""))
    out.append("|---|---:|" + ("---:|" if show_delta else ""))
    for key, v in card["metrics"].items():
        line = f"| {key} | {_fmt(v)} |"
        if show_delta:
            line += f" {_delta(v, baseline['metrics'].get(key))} |"
        out.append(line)

    if card.get("by_style"):
        out += ["", "**By query style**", "", "| Style | Queries | recall@5 | recall@10 | recall@20 | ndcg@10 |", "|---|---:|---:|---:|---:|---:|"]
        for s, b in card["by_style"].items():
            out.append(f"| {s} | {b['queries']} | {_fmt(b['recall@5'])} | {_fmt(b['recall@10'])} | {_fmt(b['recall@20'])} | {_fmt(b['ndcg@10'])} |")

    if kind == "retrieval":
        out += _md_retrieval_examples(card["best"], card["worst"])
        out += ["", "<details><summary>Per query</summary>", "", "| Query | Style | Kind | recall@10 | ndcg@10 | Out of scope | s |",
                "|---|---|---|---:|---:|---|---:|"]
        for r in card["per_topic"]:
            out.append(f"| `{r['query_id']}` | {r['style']} | {r['kind']} | {_fmt(r.get('recall@10'))} | {_fmt(r.get('ndcg@10'))} | "
                       f"{_fmt(r['out_of_scope'])} | {r['seconds']:.1f} |")
        out += ["", "</details>"]
    else:
        judged = "strict_p@10_judged" in card["metrics"]
        unit = "Query" if kind == "pipeline" else "Topic"
        out += ["", f"**Per {unit.lower()}** (outcomes of the shown incidents)", "",
                f"| {unit} | Kind | Shown | campaign | contested | reverted | not-incident | duplicate | unlabelled | strict p@10 |"
                + (" judged p@10 |" if judged else "") + " Found / expected | s |",
                "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|" + ("---:|" if judged else "") + "---:|---:|"]
        for r in card["per_topic"]:
            c = r["outcomes"]
            out.append(f"| `{r['id']}` | {r['kind']} | {r['shown']} | " + " | ".join(str(c.get(o, 0)) for o in OUTCOMES)
                       + f" | {_fmt(r['strict_p@10'])} |" + (f" {_fmt(r['strict_p@10_judged'])} |" if judged else "")
                       + f" {len(r['found'])} / {r['expected']} | {r['seconds']:.1f} |")
        out += ["", "**5 best** (highest-ranked true positives)", ""]
        out += [f"{i}. `{e['unit']}` #{e['rank']} → `{e['item_id']}` {e['label']}, shown as {e['tier']}: {_short(e['description'])}"
                for i, e in enumerate(card["best"], 1)] or ["None."]
        out += ["", "**5 worst** (misses first, then false positives, overclaims and duplicates by rank)", ""]
        out += [f"{i}. {_md_worst(e)}" for i, e in enumerate(card["worst"], 1)] or ["None."]
        if kind == "pipeline":
            out += _md_retrieval_examples([], card["worst_queries"])
        n = len(card["unlabelled"])
        target = (files or {}).get("unlabelled", "<name>.<split>.unlabelled.jsonl")
        out += ["", f"**Unlabelled:** {n} shown incident(s) have no golden item"
                + (f" → `{target}` for the E3 judge." if n else ".")]
        if judged:
            out.append(f"Judged precision counts an unjudged hit as a miss: {card['metrics']['unjudged@10']} unjudged in the top 10, "
                       f"{card['metrics']['unjudged@25']} in the top 25 (all topics).")
        if card.get("challenges"):
            out += ["", f"**Challenges** ({len(card['challenges'])} reviews dispute a golden label; no score changed)", ""]
            out += [f"- `{c['item_id']}` ({c['label']}): {c['verdict']}. {_short(c['reason'], 160)}" for c in card["challenges"]]

    out += ["", "**Failures:** " + ("none." if not card["failures"] else "")]
    out += [f"- `{f['id']}`: {f['reason']}" for f in card["failures"][:20]]
    if len(card["failures"]) > 20:
        out.append(f"- … and {len(card['failures']) - 20} more (see the scorecard JSON)")
    c = card["cost"]
    out += ["", f"**Cost:** {c['seconds_per_request']:.1f} s per request (max {c['seconds_max']:.1f} s, {c['jobs']} at a time), "
            f"{c['llm_calls']:,} LLM calls, {c['input_tokens']:,} in / {c['output_tokens']:,} out tokens"
            + (f", ${c['cost_usd']:.2f}" if c["cost_usd"] else "") + "."]
    out += ["", "<details><summary>Command</summary>", "", "```", card["command"], "```", "", "</details>"]
    return "\n".join(out) + "\n"


def _md_retrieval_examples(best: list[dict], worst: list[dict]) -> list[str]:
    def missed(e: dict) -> str:
        if not e["missed"]:
            return "nothing missed in the top 10"
        return "missed " + ", ".join(f"{a['title']} (grade {a['grade']}" + (f", rank {a['rank']})" if a["rank"] else ")") for a in e["missed"][:5])
    out = []
    if best:
        out += ["", "**5 best queries**", ""]
        out += [f"{i}. `{e['query_id']}`: ndcg@10 {_fmt(e['ndcg@10'])}, recall@10 {_fmt(e['recall@10'])}; {missed(e)}" for i, e in enumerate(best, 1)]
    out += ["", "**5 worst queries**", ""]
    out += [f"{i}. `{e['query_id']}`: recall@10 {_fmt(e['recall@10'])}, ndcg@10 {_fmt(e['ndcg@10'])}; {missed(e)}" for i, e in enumerate(worst, 1)] or ["None."]
    return out


def _md_worst(e: dict) -> str:
    what = e["label"] + (f" ({e['exclusion']})" if e.get("exclusion") else "")
    if e["kind"] == "missed":
        return f"**missed** `{e['item_id']}` {what} on page {e['page_id']} (`{e['unit']}`): {_short(e['description'])}"
    label = {"false_positive": "false positive", "overclaim": "overclaim", "duplicate": "duplicate"}[e["kind"]]
    again = "already matched above: " if e["kind"] == "duplicate" else ""
    return f"**{label}** `{e['unit']}` #{e['rank']} shown as {e['tier']} → {again}`{e['item_id']}` {what}: {_short(e['description'])}"


# ── Files ────────────────────────────────────────────────────────────────────────────────────────────────────────────

def load_lines(path: str, shape: str) -> dict[str, dict]:
    """A judgements or reviews file, validated, as {incident key: line}."""
    by_key: dict[str, dict] = {}
    errors = []
    for n, line in enumerate(Path(path).read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            value = json.loads(line)
        except json.JSONDecodeError as ex:
            errors.append(f"line {n}: not JSON ({ex.msg})")
            continue
        rep = contract.validate(shape, value)
        errors += [f"line {n}: {e}" for e in rep.errors]
        if rep.ok:
            if value["key"] in by_key:
                errors.append(f"line {n}: key '{value['key']}' appears twice")
            by_key[value["key"]] = value
    if errors:
        raise UsageError(f"{path} is not a valid {shape} file:\n  " + "\n  ".join(errors[:10]))
    return by_key


def save_responses(path: Path, header: dict, runs: list[Run]) -> None:
    with path.open("w", encoding="utf-8") as f:
        f.write(json.dumps({"header": header}, ensure_ascii=False) + "\n")
        for r in runs:
            resp = {k: v for k, v in r.response.items() if k != "debug"} if r.response else None
            f.write(json.dumps({"id": r.id, "topic_id": r.topic_id, "request": r.request, "response": resp,
                                "seconds": round(r.seconds, 3), "failure": r.failure}, ensure_ascii=False) + "\n")


def load_responses(path: str) -> tuple[dict, dict[str, dict]]:
    lines = [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines() if line.strip()]
    if not lines or "header" not in lines[0]:
        raise UsageError(f"{path} is not a responses file written by the runner")
    return lines[0]["header"], {line["id"]: line for line in lines[1:]}


def write_outputs(card: dict, out: Path, baseline: dict | None) -> str:
    out.mkdir(parents=True, exist_ok=True)
    stem = f"{card['variant']}.{card['split']}"
    files = {"scorecard": str(out / f"{stem}.scorecard.json"), "unlabelled": str(out / f"{stem}.unlabelled.jsonl")}
    files = {k: os.path.relpath(v, REPO) if v.startswith(str(REPO)) else v for k, v in files.items()}
    md = render_markdown(card, baseline, files)
    (out / f"{stem}.scorecard.json").write_text(json.dumps(card, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    (out / f"{stem}.scorecard.md").write_text(md, encoding="utf-8")
    if "unlabelled" in card:
        with (out / f"{stem}.unlabelled.jsonl").open("w", encoding="utf-8") as f:
            for line in card["unlabelled"]:
                f.write(json.dumps(line, ensure_ascii=False) + "\n")
    return md


# ── CLI ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

def _load_golden(path: str) -> dict:
    p = Path(path)
    if not p.exists():
        raise UsageError(f"no golden set at {path}. It lands with P1.E1 (#20); until then pass --golden "
                         "research/eval/examples/golden.example.json to try the runner on synthetic data")
    doc, rep = contract.load_golden(p)
    if not rep.ok:
        raise UsageError(f"{path} is not a valid golden set (run validate.py golden):\n  " + "\n  ".join(rep.errors[:10]))
    if rep.warnings:
        print(f"golden {doc['version']}: {len(rep.warnings)} warning(s); `validate.py golden` lists them", file=sys.stderr)
    return doc


def _guard_split(split: str) -> None:
    if split == "test" and os.environ.get("EVAL_ALLOW_TEST") != "1":
        raise UsageError("the test split is sealed. Only the decision issues (R6 #27, S11 #38) score on it, with EVAL_ALLOW_TEST=1")


def main(argv: list[str]) -> int:
    command: list[str] = []
    if "--" in argv:
        cut = argv.index("--")
        argv, command = argv[:cut], argv[cut + 1:]
    p = argparse.ArgumentParser(prog="research/eval/run", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("kind", choices=[*("retrieval", "signals", "pipeline"), "replay"])
    p.add_argument("responses", nargs="?", help="replay: a <name>.<split>.responses.jsonl file")
    p.add_argument("--name", help="variant name, used in file names (e.g. s1-reverts)")
    p.add_argument("--split", choices=["dev", "test"], default="dev")
    p.add_argument("--golden", default=str(HERE / "golden.json"))
    p.add_argument("--out", help="output directory (default research/eval/results; replay: next to the responses file)")
    p.add_argument("--judgements", help="E3 judgements .jsonl: adds strict_p@k_judged")
    p.add_argument("--reviews", help="E3 adversarial reviews .jsonl")
    p.add_argument("--baseline", help="a scorecard.json to compare with (adds a Δ column)")
    p.add_argument("--topics", help="comma-separated topic ids: a partial run for development, not comparable")
    p.add_argument("--limit", type=int, default=DEFAULT_LIMIT, help="incidents scored per topic (default 50)")
    p.add_argument("--timeout", type=float, default=120.0, help="seconds per request (default 120)")
    p.add_argument("--jobs", type=int, default=1, help="requests in parallel (default 1; the VPS has 2 vCPUs)")
    args = p.parse_args(argv)

    try:
        judgements = load_lines(args.judgements, "judgements") if args.judgements else None
        reviews = load_lines(args.reviews, "reviews") if args.reviews else None
        baseline = json.loads(Path(args.baseline).read_text(encoding="utf-8")) if args.baseline else None

        if args.kind == "replay":
            if not args.responses or command:
                raise UsageError("usage: run replay <name>.<split>.responses.jsonl [--judgements f] [--reviews f] [--baseline f]")
            header, saved = load_responses(args.responses)
            kind, split, limit = header["kind"], header["split"], header.get("limit") or DEFAULT_LIMIT
            _guard_split(split)
            golden = _load_golden(args.golden)
            if golden["version"] != header["golden_version"]:
                print(f"warning: the run used golden {header['golden_version']}, scoring against {golden['version']}", file=sys.stderr)
            topics = split_topics(golden, split, header.get("topics_filter"))
            runs = []
            for rid, tid, req in plan(kind, topics, limit):
                s = saved.get(rid)
                if s is None:
                    runs.append(Run(rid, tid, req, None, 0.0, "not in the replayed run"))
                elif s["request"] != req:
                    runs.append(Run(rid, tid, req, None, s["seconds"], "the request changed since the run (golden set edited?)"))
                else:
                    runs.append(Run(rid, tid, req, s["response"], s["seconds"], s["failure"]))
            name = args.name or header["variant"]
            out = Path(args.out) if args.out else Path(args.responses).resolve().parent
            card = scorecard(kind, name, golden, split, runs, command=header["command"], git=header["git_commit"],
                             ran_at=header["ran_at"], limit=limit, partial=bool(header.get("topics_filter")),
                             judgements=judgements, reviews=reviews, jobs=header.get("jobs", 1),
                             sources=_sources(args, replayed_from=args.responses))
        else:
            if not command or not args.name:
                raise UsageError(f"usage: run {args.kind} --name <name> [options] -- <variant command ...>")
            if not NAME_PATTERN.match(args.name):
                raise UsageError(f"--name '{args.name}': use lowercase letters, digits, '.', '_', '+' and '-'")
            _guard_split(args.split)
            golden = _load_golden(args.golden)
            only = [t.strip() for t in args.topics.split(",") if t.strip()] if args.topics else None
            topics = split_topics(golden, args.split, only)
            planned = plan(args.kind, topics, args.limit)
            if not planned:
                raise UsageError(f"nothing to run: the {args.split} split has no {args.kind} requests")
            out = Path(args.out) if args.out else DEFAULT_OUT
            out.mkdir(parents=True, exist_ok=True)
            stem = f"{args.name}.{args.split}"
            git, ran_at, cmd = git_commit(), _now(), "research/eval/run " + shlex.join(_runner_args(argv) + ["--", *command])
            print(f"{args.kind} variant '{args.name}': {len(planned)} request(s) on the {args.split} split of golden {golden['version']}", file=sys.stderr)
            runs = run_variant(args.kind, command, planned, args.timeout, args.jobs, out / f"{stem}.log")
            header = {"contract": contract.CONTRACT, "kind": args.kind, "variant": args.name, "split": args.split,
                      "golden_version": golden["version"], "dataset": golden["dataset"], "git_commit": git, "ran_at": ran_at,
                      "command": cmd, "limit": args.limit, "jobs": args.jobs, "topics_filter": only}
            save_responses(out / f"{stem}.responses.jsonl", header, runs)
            card = scorecard(args.kind, args.name, golden, args.split, runs, command=cmd, git=git, ran_at=ran_at,
                             limit=args.limit, partial=bool(only), judgements=judgements, reviews=reviews, jobs=args.jobs,
                             sources=_sources(args))
        md = write_outputs(card, out, baseline)
    except UsageError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 2
    print(md)
    print(f"wrote {out}/{card['variant']}.{card['split']}.scorecard.json (+ .md"
          + (", .unlabelled.jsonl" if "unlabelled" in card else "") + ")", file=sys.stderr)
    return 1 if card["failures"] and len(card["failures"]) == card["requests"] else 0


def _runner_args(argv: list[str]) -> list[str]:
    """The runner's own arguments minus the output-only ones, for the scorecard's `command` field."""
    keep, skip = [], False
    for a in argv:
        if skip:
            skip = False
            continue
        if a in ("--out", "--baseline", "--judgements", "--reviews"):
            skip = True
            continue
        if a.split("=")[0] in ("--out", "--baseline", "--judgements", "--reviews"):
            continue
        keep.append(a)
    return keep


def _sources(args: argparse.Namespace, replayed_from: str | None = None) -> dict:
    s = {}
    if args.judgements:
        s["judgements_file"] = args.judgements
    if args.reviews:
        s["reviews_file"] = args.reviews
    if args.baseline:
        s["baseline_file"] = args.baseline
    if replayed_from:
        s["replayed_from"] = replayed_from
        s["replayed_at"] = _now()
    return s


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
