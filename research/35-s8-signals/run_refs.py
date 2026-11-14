#!/usr/bin/env python3
"""Run P1.S8 (#35) `refs` detectors on the shared 300-candidate S8 dataset.

    python3 research/35-s8-signals/run_refs.py [--candidates ~/.cache/aot-research/s8/candidates.jsonl]

Writes `research/35-s8-signals/results/refs.jsonl` (`rev_id, group, signals` — no wikitext)
and prints per-detector hit counts, rates, lifts, and data-driven domain revert scores.
"""
from __future__ import annotations

import argparse
import collections
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import detect_citation_removal  # noqa: E402
import detect_citation_swap  # noqa: E402
import detect_domain  # noqa: E402
import detect_templates  # noqa: E402
import detect_unsourced_assertion  # noqa: E402

GROUP = "refs"
LF = chr(10)
DEFAULT_CANDIDATES = pathlib.Path.home() / ".cache" / "aot-research" / "s8" / "candidates.jsonl"
OUT_JSONL = HERE / "results" / f"{GROUP}.jsonl"

DETECTORS = [
    (detect_citation_removal.KIND, detect_citation_removal.detect),
    (detect_citation_swap.KIND, detect_citation_swap.detect),
    (detect_unsourced_assertion.KIND, detect_unsourced_assertion.detect),
    (detect_domain.KIND, detect_domain.detect),
    (detect_templates.KIND, detect_templates.detect),
]


def run_all(candidates: list[dict]) -> list[dict]:
    results: list[dict] = []
    for cand in candidates:
        signals: list[dict] = []
        for _, fn in DETECTORS:
            for sig in fn(cand):
                signals.append({
                    "kind": sig["kind"],
                    "label": sig["label"],
                    "evidence": sig["evidence"][:200],
                })
        results.append({
            "rev_id": int(cand["rev_id"]),
            "group": cand["group"],
            "signals": signals,
        })
    return results


def summarize(candidates: list[dict], results: list[dict]) -> dict:
    totals = collections.Counter(c["group"] for c in candidates)
    by_kind: dict[str, dict[str, int]] = {
        kind: {"reverted": 0, "control": 0, "non_graffiti_reverted": 0}
        for kind, _ in DETECTORS
    }
    non_graffiti_total = sum(
        1 for c in candidates if c["group"] == "reverted" and c.get("s1_class") != "graffiti_cleanup"
    )
    any_hit = {"reverted": 0, "control": 0, "non_graffiti_reverted": 0}

    for cand, res in zip(candidates, results):
        grp = res["group"]
        kinds_present = {s["kind"] for s in res["signals"]}
        if kinds_present:
            any_hit[grp] += 1
            if grp == "reverted" and cand.get("s1_class") != "graffiti_cleanup":
                any_hit["non_graffiti_reverted"] += 1
        for k in kinds_present:
            by_kind[k][grp] += 1
            if grp == "reverted" and cand.get("s1_class") != "graffiti_cleanup":
                by_kind[k]["non_graffiti_reverted"] += 1

    return {
        "totals": dict(totals),
        "non_graffiti_reverted_total": non_graffiti_total,
        "by_kind": by_kind,
        "any_hit": any_hit,
        "domain_scores_all": detect_domain.score_candidate_domains(candidates, refs_only=False),
        "domain_scores_ref_only": detect_domain.score_candidate_domains(candidates, refs_only=True),
    }


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--candidates", type=pathlib.Path, default=DEFAULT_CANDIDATES)
    ap.add_argument("--out", type=pathlib.Path, default=OUT_JSONL)
    args = ap.parse_args(argv)

    with args.candidates.open(encoding="utf-8") as f:
        candidates = [json.loads(line) for line in f if line.strip()]

    results = run_all(candidates)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8") as f:
        for row in results:
            f.write(json.dumps(row, ensure_ascii=False) + LF)

    summary = summarize(candidates, results)
    n_rev = summary["totals"].get("reverted", 0)
    n_ctl = summary["totals"].get("control", 0)
    n_ng = summary["non_graffiti_reverted_total"]

    print(f"Wrote {len(results)} rows to {args.out}")
    print(f"Candidates: reverted={n_rev} (non-graffiti={n_ng}), control={n_ctl}" + LF)
    print(f"{'detector':24s} {'reverted':>14s} {'control':>14s} {'lift':>8s} {'non-graffiti':>15s}")
    print("-" * 79)
    for kind, _ in DETECTORS:
        c = summary["by_kind"][kind]
        r_rate = c["reverted"] / n_rev if n_rev else 0.0
        k_rate = c["control"] / n_ctl if n_ctl else 0.0
        ng_rate = c["non_graffiti_reverted"] / n_ng if n_ng else 0.0
        lift = f"{r_rate / k_rate:.2f}x" if k_rate > 0 else "inf"
        print(
            f"{kind:24s} {c['reverted']:3d} ({r_rate:5.1%})   {c['control']:3d} ({k_rate:5.1%})   "
            f"{lift:>8s} {c['non_graffiti_reverted']:3d}/{n_ng} ({ng_rate:5.1%})"
        )
    a = summary["any_hit"]
    ar = a["reverted"] / n_rev if n_rev else 0.0
    ac = a["control"] / n_ctl if n_ctl else 0.0
    ang = a["non_graffiti_reverted"] / n_ng if n_ng else 0.0
    alift = f"{ar / ac:.2f}x" if ac > 0 else "inf"
    print("-" * 79)
    print(
        f"{'ANY refs signal':24s} {a['reverted']:3d} ({ar:5.1%})   {a['control']:3d} ({ac:5.1%})   "
        f"{alift:>8s} {a['non_graffiti_reverted']:3d}/{n_ng} ({ang:5.1%})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
