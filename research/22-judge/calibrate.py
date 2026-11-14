#!/usr/bin/env python3
"""Score judge calibration against golden-set labels (#22).

Usage:
  python3 research/22-judge/calibrate.py <batch-out.jsonl>... --split dev [--inputs results/golden-dev.inputs.jsonl] [--json out.json]
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import sys
from collections import Counter
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))

import evidence  # noqa: E402

NL = chr(10)
LABELS = ("campaign", "contested", "reverted", "not-incident")
LEVEL = {"not-incident": 0, "reverted": 1, "contested": 2, "campaign": 3}
INPUT_FIELDS = ("article_title", "insertions", "diff", "removals", "history")


def resolve_existing_path(raw_path: str) -> pathlib.Path:
    p = pathlib.Path(raw_path)
    if p.exists():
        return p
    alt = HERE / p
    if alt.exists():
        return alt
    return p


def _mean(xs: list[float]) -> float | None:
    return sum(xs) / len(xs) if xs else None


def _pct(num: int, den: int) -> str:
    if den == 0:
        return "–"
    return f"{num}/{den} ({100.0 * num / den:.1f}%)"


def _fmt_float(v: float | None, digits: int = 2) -> str:
    return "–" if v is None else f"{v:.{digits}f}"


def load_golden_split(golden_path: pathlib.Path, split: str) -> tuple[dict[str, dict[str, Any]], dict[str, str]]:
    if split == "test" and os.environ.get("EVAL_ALLOW_TEST") != "1":
        raise PermissionError("test split is sealed (set EVAL_ALLOW_TEST=1 only in final decision issues)")
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    by_id: dict[str, dict[str, Any]] = {}
    key_to_id: dict[str, str] = {}
    for topic in doc.get("topics", []):
        if topic.get("split") != split:
            continue
        for item in topic.get("items", []):
            iid = item["id"]
            by_id[iid] = item
            key_to_id[evidence.incident_key(item["inserting_rev_ids"])] = iid
    return by_id, key_to_id


def load_inputs_map(inputs_path: pathlib.Path | None) -> dict[str, str]:
    """Return map from `_id` and `key` to concatenated prompt input text for verbatim evidence checking."""
    if inputs_path is None:
        return {}
    mapping: dict[str, str] = {}
    for line in inputs_path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        obj = json.loads(line)
        blob = NL.join(str(obj.get(f) or "") for f in INPUT_FIELDS)
        if obj.get("_id"):
            mapping[str(obj["_id"])] = blob
        if obj.get("key"):
            mapping[str(obj["key"])] = blob
    return mapping


def score_group(
    model: str,
    prompt_version: str,
    rows: list[dict[str, Any]],
    golden_by_id: dict[str, dict[str, Any]],
    key_to_id: dict[str, str],
    inputs_map: dict[str, str] | None = None,
) -> dict[str, Any]:
    """Compute calibration scorecard for one `(model, prompt_version)` group."""
    dedup: dict[str, dict[str, Any]] = {}
    for row in rows:
        raw_id = str(row.get("id") or "")
        item_id = raw_id if raw_id in golden_by_id else key_to_id.get(raw_id, "")
        if not item_id:
            continue
        dedup[item_id] = row

    matched_rows = list(dedup.items())
    outcomes = Counter(str(r.get("outcome") or "unknown").lower() for _, r in matched_rows)

    pairs: list[tuple[dict[str, Any], dict[str, Any], dict[str, Any]]] = []
    for item_id, row in matched_rows:
        outcome = str(row.get("outcome") or "").lower()
        val = row.get("value")
        if outcome == "ok" and isinstance(val, dict) and val.get("label") in LEVEL:
            pairs.append((golden_by_id[item_id], val, row))

    n_total = len(matched_rows)
    n_scored = len(pairs)

    exact_hits = sum(1 for g, j, _ in pairs if g["label"] == j["label"])

    recall_by_label: dict[str, dict[str, Any]] = {}
    for lbl in LABELS:
        sub = [(g, j) for g, j, _ in pairs if g["label"] == lbl]
        hits = sum(1 for g, j in sub if j["label"] == lbl)
        recall_by_label[lbl] = {
            "hits": hits,
            "total": len(sub),
            "rate": (hits / len(sub)) if sub else None,
        }

    matrix: dict[str, dict[str, int]] = {g_lbl: {j_lbl: 0 for j_lbl in LABELS} for g_lbl in LABELS}
    for g, j, _ in pairs:
        matrix[g["label"]][j["label"]] += 1

    bin_hits = sum(1 for g, j, _ in pairs if (LEVEL[g["label"]] >= 2) == (LEVEL[j["label"]] >= 2))

    ni_golden = [(g, j) for g, j, _ in pairs if g["label"] == "not-incident"]
    ni_both = [(g, j) for g, j in ni_golden if j["label"] == "not-incident"]
    excl_hits_both = sum(1 for g, j in ni_both if g.get("exclusion") == j.get("exclusion"))
    excl_hits_all_ni = sum(
        1 for g, j in ni_golden if j["label"] == "not-incident" and g.get("exclusion") == j.get("exclusion")
    )

    cat_sub = [(g, j) for g, j, _ in pairs if g.get("category")]
    cat_hits = sum(1 for g, j in cat_sub if j.get("category") == g.get("category"))

    overclaims = sum(1 for g, j, _ in pairs if LEVEL[j["label"]] > LEVEL[g["label"]])
    underclaims = sum(1 for g, j, _ in pairs if LEVEL[j["label"]] < LEVEL[g["label"]])

    conf_right = [float(j["confidence"]) for g, j, _ in pairs if g["label"] == j["label"] and j.get("confidence") is not None]
    conf_wrong = [float(j["confidence"]) for g, j, _ in pairs if g["label"] != j["label"] and j.get("confidence") is not None]

    ev_verbatim: dict[str, Any] | None = None
    if inputs_map is not None:
        ev_hits = 0
        ev_total = 0
        for g, j, row in pairs:
            blob = inputs_map.get(g["id"]) or inputs_map.get(str(row.get("id") or "")) or ""
            for span in j.get("evidence") or []:
                s = str(span)
                ev_total += 1
                if s and s in blob:
                    ev_hits += 1
        ev_verbatim = {
            "hits": ev_hits,
            "total": ev_total,
            "rate": (ev_hits / ev_total) if ev_total else None,
        }

    latencies = [float(r.get("latency_ms") or 0.0) for _, r in matched_rows]
    in_toks = [float(r.get("input_tokens") or 0.0) for _, r in matched_rows]
    out_toks = [float(r.get("output_tokens") or 0.0) for _, r in matched_rows]
    costs = [float(r.get("cost_usd") or 0.0) for _, r in matched_rows]

    disagreements_all: list[dict[str, Any]] = []
    for g, j, _ in pairs:
        if g["label"] == j["label"]:
            continue
        g_disp = f"{g['label']} ({g['exclusion']})" if g["label"] == "not-incident" and g.get("exclusion") else g["label"]
        j_disp = f"{j['label']} ({j['exclusion']})" if j["label"] == "not-incident" and j.get("exclusion") else j["label"]
        disagreements_all.append(
            {
                "id": g["id"],
                "golden_label": g["label"],
                "golden_exclusion": g.get("exclusion"),
                "judge_label": j["label"],
                "judge_exclusion": j.get("exclusion"),
                "transition": f"{g_disp} → {j_disp}",
                "confidence": int(j.get("confidence") or 0),
                "level_gap": abs(LEVEL[j["label"]] - LEVEL[g["label"]]),
                "reason_en": " ".join(str(j.get("reason_en") or "").splitlines()).strip(),
            }
        )
    disagreements_all.sort(key=lambda d: (-d["confidence"], -d["level_gap"], d["id"]))

    return {
        "model": model,
        "prompt_version": prompt_version,
        "n": n_total,
        "n_golden": len(golden_by_id),
        "n_scored": n_scored,
        "outcomes": dict(sorted(outcomes.items())),
        "exact_agreement": {
            "hits": exact_hits,
            "total": n_scored,
            "rate": (exact_hits / n_scored) if n_scored else None,
        },
        "per_label_recall": recall_by_label,
        "confusion_matrix": matrix,
        "binary_agreement": {
            "hits": bin_hits,
            "total": n_scored,
            "rate": (bin_hits / n_scored) if n_scored else None,
        },
        "exclusion_agreement": {
            "both_not_incident": {
                "hits": excl_hits_both,
                "total": len(ni_both),
                "rate": (excl_hits_both / len(ni_both)) if ni_both else None,
            },
            "golden_not_incident": {
                "hits": excl_hits_all_ni,
                "total": len(ni_golden),
                "rate": (excl_hits_all_ni / len(ni_golden)) if ni_golden else None,
            },
        },
        "category_agreement": {
            "hits": cat_hits,
            "total": len(cat_sub),
            "rate": (cat_hits / len(cat_sub)) if cat_sub else None,
        },
        "overclaim": {
            "hits": overclaims,
            "total": n_scored,
            "rate": (overclaims / n_scored) if n_scored else None,
        },
        "underclaim": {
            "hits": underclaims,
            "total": n_scored,
            "rate": (underclaims / n_scored) if n_scored else None,
        },
        "mean_confidence": {
            "right": _mean(conf_right),
            "wrong": _mean(conf_wrong),
        },
        "evidence_verbatim": ev_verbatim,
        "cost_and_latency": {
            "mean_latency_ms": _mean(latencies),
            "mean_input_tokens": _mean(in_toks),
            "mean_output_tokens": _mean(out_toks),
            "mean_cost_usd": _mean(costs),
            "total_cost_usd": sum(costs),
        },
        "disagreements_total": len(disagreements_all),
        "disagreements": disagreements_all[:30],
    }


def render_group_markdown(rep: dict[str, Any], split: str) -> str:
    out: list[str] = []
    out.append(f"## Calibration · `{rep['model']}` · `judge-incident@{rep['prompt_version']}` · {split} split")
    out.append("")
    outcomes_str = ", ".join(f"{k}={v}" for k, v in rep["outcomes"].items()) or "none"
    out.append(f"- **Items (`n`):** {rep['n']} / {rep['n_golden']} golden ({rep['n_scored']} scored; outcomes: {outcomes_str})")
    out.append("")
    out.append("| Metric | Value |")
    out.append("|---|---|")
    ea = rep["exact_agreement"]
    ba = rep["binary_agreement"]
    ex_both = rep["exclusion_agreement"]["both_not_incident"]
    ex_gold = rep["exclusion_agreement"]["golden_not_incident"]
    ca = rep["category_agreement"]
    oc = rep["overclaim"]
    uc = rep["underclaim"]
    mc = rep["mean_confidence"]
    ev = rep["evidence_verbatim"]
    cl = rep["cost_and_latency"]

    out.append(f"| Exact label agreement | {_pct(ea['hits'], ea['total'])} |")
    out.append(f"| Binary agreement (≥ `contested` vs below) | {_pct(ba['hits'], ba['total'])} |")
    out.append(
        f"| Exclusion agreement (`not-incident`) | both not-incident: {_pct(ex_both['hits'], ex_both['total'])} · all golden not-incident: {_pct(ex_gold['hits'], ex_gold['total'])} |"
    )
    out.append(f"| Category agreement (where golden has category) | {_pct(ca['hits'], ca['total'])} |")
    out.append(f"| Over-claim rate (judge > golden) | {_pct(oc['hits'], oc['total'])} |")
    out.append(f"| Under-claim rate (judge < golden) | {_pct(uc['hits'], uc['total'])} |")
    out.append(
        f"| Mean confidence (right vs wrong) | right: {_fmt_float(mc['right'])} vs wrong: {_fmt_float(mc['wrong'])} |"
    )
    if ev is None:
        out.append("| Evidence strings verbatim in input | – (pass `--inputs`) |")
    else:
        out.append(f"| Evidence strings verbatim in input | {_pct(ev['hits'], ev['total'])} |")
    out.append(
        f"| Mean latency / tokens / cost | {_fmt_float(cl['mean_latency_ms'], 0)} ms · "
        f"{_fmt_float(cl['mean_input_tokens'], 0)} in / {_fmt_float(cl['mean_output_tokens'], 0)} out tok · "
        f"${_fmt_float(cl['mean_cost_usd'], 4)}/item (${cl['total_cost_usd']:.4f} total) |"
    )
    out.append("")

    out.append("### Per-golden-label recall")
    out.append("")
    out.append("| Golden label | Recall |")
    out.append("|---|---|")
    for lbl in LABELS:
        r = rep["per_label_recall"][lbl]
        out.append(f"| `{lbl}` | {_pct(r['hits'], r['total'])} |")
    out.append("")

    out.append("### 4×4 Confusion Matrix (rows = golden, columns = judge)")
    out.append("")
    out.append(r"| Golden \ Judge | `campaign` | `contested` | `reverted` | `not-incident` | Total |")
    out.append("|---|---:|---:|---:|---:|---:|")
    mat = rep["confusion_matrix"]
    for g_lbl in LABELS:
        row_counts = [mat[g_lbl][j_lbl] for j_lbl in LABELS]
        out.append(
            f"| `{g_lbl}` | {row_counts[0]} | {row_counts[1]} | {row_counts[2]} | {row_counts[3]} | {sum(row_counts)} |"
        )
    out.append("")

    dis = rep["disagreements"]
    out.append(f"### Disagreements (showing {len(dis)} of {rep['disagreements_total']})")
    out.append("")
    if not dis:
        out.append("_No disagreements._")
    else:
        out.append("| Item ID | Golden → Judge | Confidence | Reason |")
        out.append("|---|---|---:|---|")
        for d in dis:
            reason_safe = d["reason_en"].replace("|", r"\|")
            out.append(f"| `{d['id']}` | {d['transition']} | {d['confidence']} | {reason_safe} |")
    out.append("")
    return NL.join(out)


def calibrate_files(
    batch_paths: list[pathlib.Path],
    *,
    split: str = "dev",
    golden_path: pathlib.Path | None = None,
    inputs_path: pathlib.Path | None = None,
) -> tuple[list[dict[str, Any]], str]:
    g_path = golden_path or (ROOT / "research" / "eval" / "golden.json")
    golden_by_id, key_to_id = load_golden_split(g_path, split)
    inputs_map = load_inputs_map(inputs_path) if inputs_path is not None else None

    grouped: dict[tuple[str, str], list[dict[str, Any]]] = {}
    for bp in batch_paths:
        for line in bp.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            row = json.loads(line)
            key = (str(row.get("model") or "unknown"), str(row.get("prompt_version") or "1"))
            grouped.setdefault(key, []).append(row)

    reports: list[dict[str, Any]] = []
    md_blocks: list[str] = []
    for (model, pver), rows in grouped.items():
        rep = score_group(model, pver, rows, golden_by_id, key_to_id, inputs_map)
        reports.append(rep)
        md_blocks.append(render_group_markdown(rep, split))

    return reports, NL.join(md_blocks)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("batch_files", nargs="+", help="One or more analyze.sh batch output JSONL files")
    parser.add_argument("--split", default="dev", choices=["dev", "test"])
    parser.add_argument("--golden", default=str(ROOT / "research" / "eval" / "golden.json"))
    parser.add_argument("--inputs", default=None, help="Inputs JSONL (enables verbatim evidence check)")
    parser.add_argument("--json", dest="json_out", default=None, help="Optional JSON report path")

    args = parser.parse_args(argv)
    try:
        bpaths = [resolve_existing_path(p) for p in args.batch_files]
        in_path = resolve_existing_path(args.inputs) if args.inputs else None
        reports, markdown = calibrate_files(
            bpaths,
            split=args.split,
            golden_path=pathlib.Path(args.golden),
            inputs_path=in_path,
        )
        if args.json_out:
            jpath = pathlib.Path(args.json_out)
            jpath.parent.mkdir(parents=True, exist_ok=True)
            jpath.write_text(json.dumps({"split": args.split, "groups": reports}, ensure_ascii=False, indent=2) + NL, encoding="utf-8")
        print(markdown, end="" if markdown.endswith(NL) else NL)
        return 0
    except PermissionError as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
