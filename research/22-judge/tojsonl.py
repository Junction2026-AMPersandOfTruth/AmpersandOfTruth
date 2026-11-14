#!/usr/bin/env python3
"""Convert `analyze.sh batch` outputs into CONTRACT.md §5 judgements / reviews JSONL (#22).

Usage:
  python3 research/22-judge/tojsonl.py judgements <batch-out.jsonl> --out j.jsonl
  python3 research/22-judge/tojsonl.py reviews <batch-out.jsonl> --out r.jsonl
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))

import evidence  # noqa: E402

NL = chr(10)
KEY_RE = re.compile(r"^[0-9]+(\+[0-9]+)*$")
VALID_LABELS = frozenset({"campaign", "contested", "reverted", "not-incident"})
VALID_EXCLUSIONS = frozenset({"not-substantive", "self-removal", "bot", "blanking", "kept", "graffiti"})
VALID_VERDICTS = frozenset({"survives", "benign", "unclear"})


def _load_golden_id_to_key(golden_path: pathlib.Path) -> dict[str, str]:
    if not golden_path.exists():
        return {}
    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    out: dict[str, str] = {}
    for topic in doc.get("topics", []):
        if topic.get("split") != "dev":
            continue
        for item in topic.get("items", []):
            out[item["id"]] = evidence.incident_key(item["inserting_rev_ids"])
    return out


def convert_batch(
    kind: str,
    batch_path: pathlib.Path,
    out_path: pathlib.Path,
    *,
    model_filter: str | None = None,
    golden_path: pathlib.Path | None = None,
) -> tuple[int, int]:
    """Convert `batch_path` into `judgements` or `reviews` JSONL. Returns `(written_count, skipped_count)`."""
    id_to_key: dict[str, str] | None = None
    by_key: dict[str, dict[str, Any]] = {}
    models_seen: set[str] = set()
    skipped = 0

    for line in batch_path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        model = str(row.get("model") or "unknown")
        if model_filter and model != model_filter:
            continue
        models_seen.add(model)

        outcome = str(row.get("outcome") or "").lower()
        val = row.get("value")
        if outcome != "ok" or not isinstance(val, dict):
            skipped += 1
            continue

        raw_id = str(row.get("id") or "").strip()
        if KEY_RE.match(raw_id):
            key = raw_id
        else:
            if id_to_key is None:
                id_to_key = _load_golden_id_to_key(golden_path or (ROOT / "research" / "eval" / "golden.json"))
            key = id_to_key.get(raw_id, "")
            if not KEY_RE.match(key):
                skipped += 1
                continue

        prompt_version = str(row.get("prompt_version") or "1")
        if kind == "judgements":
            label = val.get("label")
            if label not in VALID_LABELS:
                skipped += 1
                continue
            prompt_id = str(row.get("prompt") or "judge-incident")
            conf = max(1, min(5, int(val.get("confidence") or 3)))
            ev_list = [str(x) for x in (val.get("evidence") or []) if str(x)]
            rec: dict[str, Any] = {
                "key": key,
                "label": label,
            }
            if label == "not-incident":
                excl = val.get("exclusion")
                if excl not in VALID_EXCLUSIONS:
                    cat = str(val.get("category") or "")
                    excl = "graffiti" if cat in ("vandalism", "test_or_nonsense") else "not-substantive"
                rec["exclusion"] = excl
            rec["confidence"] = conf
            rec["evidence"] = ev_list
            if val.get("reason_en"):
                rec["note"] = str(val["reason_en"])
            if val.get("category"):
                rec["category"] = str(val["category"])
            rec["judge"] = f"{prompt_id}@{prompt_version}/{model}"
            by_key[key] = rec
        elif kind == "reviews":
            verdict = val.get("verdict")
            reason = str(val.get("reason") or "").strip()
            if verdict not in VALID_VERDICTS or not reason:
                skipped += 1
                continue
            prompt_id = str(row.get("prompt") or "adversarial-review")
            rec = {
                "key": key,
                "verdict": verdict,
                "reason": reason,
                "would_change_mind": str(val["would_change_mind"]) if val.get("would_change_mind") is not None else None,
                "reviewer": f"{prompt_id}@{prompt_version}/{model}",
            }
            if val.get("benign_explanation") is not None:
                rec["benign_explanation"] = str(val["benign_explanation"])
            if val.get("quoted") is not None:
                rec["quoted"] = [str(x) for x in val["quoted"]]
            by_key[key] = rec
        else:
            raise ValueError(f"unknown kind '{kind}'")

    if len(models_seen) > 1 and not model_filter:
        print(
            f"warning: batch file contained multiple models {sorted(models_seen)}; pass --model to pick one",
            file=sys.stderr,
        )

    lines = [json.dumps(r, ensure_ascii=False) for r in by_key.values()]
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(NL.join(lines) + (NL if lines else ""), encoding="utf-8")
    return len(lines), skipped


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=["judgements", "reviews"])
    parser.add_argument("batch_file", help="Path to analyze.sh batch output JSONL")
    parser.add_argument("--out", required=True, help="Output CONTRACT §5 JSONL path")
    parser.add_argument("--model", default=None, help="Optional model name filter when batch_file has multiple models")

    args = parser.parse_args(argv)
    written, skipped = convert_batch(
        args.kind,
        pathlib.Path(args.batch_file),
        pathlib.Path(args.out),
        model_filter=args.model,
    )
    print(f"wrote {written} {args.kind} -> {args.out} ({skipped} non-ok/unmapped skipped)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
