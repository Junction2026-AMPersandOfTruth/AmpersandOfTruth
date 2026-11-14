#!/usr/bin/env python3
"""P1.S9b (#70): Run Gemini Decisions API over S9's golden dev inputs (variants A and C).

Usage:
  python3 research/70-s9b-decisions/run.py --variant A
  python3 research/70-s9b-decisions/run.py --variant C
  python3 research/70-s9b-decisions/run.py --variant A --limit 5 --dry-run
  python3 research/70-s9b-decisions/run.py --variant A --probe
"""
from __future__ import annotations

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import pathlib
import sys
from typing import Any

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

from decisions import (  # noqa: E402
    DECISIONS_URL,
    DEFAULT_CACHE_DIR,
    DEFAULT_MODEL,
    DecisionsClient,
    build_request_body,
)

S9_RESULTS_DIR = REPO_ROOT / "research" / "36-s9-classify" / "results"
VARIANT_INPUTS = {
    "A": S9_RESULTS_DIR / "golden-dev.classify.jsonl",
    "C": S9_RESULTS_DIR / "golden-dev.classify-facts.jsonl",
}


def load_jsonl(path: pathlib.Path) -> list[dict[str, Any]]:
    with path.open(encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def write_jsonl(path: pathlib.Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")


def write_probe(
    probe_path: pathlib.Path,
    item_id: str,
    variant: str,
    body: dict[str, Any],
    response: dict[str, Any] | None,
    dry_run: bool,
) -> None:
    probe_path.parent.mkdir(parents=True, exist_ok=True)
    doc = {
        "_id": item_id,
        "variant": variant,
        "mode": "dry-run" if dry_run else "live",
        "endpoint": DECISIONS_URL,
        "request": body,
        "response": response,
    }
    probe_path.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def run_variant(
    variant: str,
    in_path: pathlib.Path | None = None,
    out_dir: pathlib.Path | None = None,
    limit: int | None = None,
    dry_run: bool = False,
    concurrency: int = 8,
    write_probe_file: bool = False,
    client: DecisionsClient | None = None,
) -> pathlib.Path:
    if variant not in VARIANT_INPUTS:
        raise ValueError(f"Unsupported variant {variant!r}; expected 'A' or 'C'.")

    src_path = in_path or VARIANT_INPUTS[variant]
    results_dir = out_dir or (HERE / "results")
    include_facts = variant == "C"

    rows = load_jsonl(src_path)
    if limit is not None:
        rows = rows[: max(0, limit)]

    prepared: list[tuple[str, dict[str, Any]]] = []
    for row in rows:
        item_id = str(row.get("_id") or row.get("id") or "")
        if not item_id:
            raise ValueError("Input row missing '_id'.")
        body = build_request_body(row, include_facts=include_facts, model=DEFAULT_MODEL)
        prepared.append((item_id, body))

    probe_path = results_dir / "probe.json"

    if dry_run:
        req_rows = [{"_id": item_id, **body} for item_id, body in prepared]
        out_path = results_dir / f"requests.{variant}.jsonl"
        write_jsonl(out_path, req_rows)
        if prepared and (write_probe_file or (variant == "A" and not probe_path.exists())):
            first_id, first_body = prepared[0]
            write_probe(probe_path, first_id, variant, first_body, response=None, dry_run=True)
        sys.stderr.write(
            f"[s9b] dry-run variant {variant}: wrote {len(req_rows)} requests -> {out_path}\n"
        )
        return out_path

    active_client = client or DecisionsClient(cache_dir=results_dir / ".cache" if out_dir else DEFAULT_CACHE_DIR)
    if not active_client.has_api_key:
        raise RuntimeError(
            "GEMINI_API_KEY is not set; run with --dry-run inside a keyless sandbox, or set GEMINI_API_KEY."
        )

    ordered_results: list[dict[str, Any] | None] = [None] * len(prepared)
    outcomes: Counter[str] = Counter()
    workers = max(1, min(concurrency, len(prepared) or 1))

    def _task(idx: int, item_id: str, body: dict[str, Any]) -> tuple[int, dict[str, Any]]:
        res = active_client.decide(body)
        return idx, {
            "_id": item_id,
            "outcome": res["outcome"],
            "answers": res["answers"],
            "usage": res["usage"],
            "latency_ms": res["latency_ms"],
        }

    with ThreadPoolExecutor(max_workers=workers) as pool:
        futs = [pool.submit(_task, idx, item_id, body) for idx, (item_id, body) in enumerate(prepared)]
        done = 0
        for fut in as_completed(futs):
            idx, rec = fut.result()
            ordered_results[idx] = rec
            outcomes[rec["outcome"]] += 1
            done += 1
            if done % 50 == 0 or done == len(prepared):
                sys.stderr.write(
                    f"[s9b] variant {variant}: {done}/{len(prepared)} completed ({dict(outcomes)})\n"
                )

    final_rows = [r for r in ordered_results if r is not None]
    out_path = results_dir / f"decisions.{variant}.jsonl"
    write_jsonl(out_path, final_rows)

    if prepared and final_rows and (write_probe_file or (variant == "A" and not probe_path.exists())):
        first_id, first_body = prepared[0]
        first_resp = {k: v for k, v in final_rows[0].items() if k != "_id"}
        write_probe(probe_path, first_id, variant, first_body, response=first_resp, dry_run=False)

    sys.stderr.write(
        f"[s9b] variant {variant}: wrote {len(final_rows)} results -> {out_path} ({dict(outcomes)})\n"
    )
    return out_path


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Run P1.S9b Gemini Decisions API evaluation on golden dev.")
    ap.add_argument("--variant", choices=("A", "C"), required=True, help="Input variant: A (diff+context) or C (+facts).")
    ap.add_argument("--limit", type=int, help="Optional cap on number of items to process.")
    ap.add_argument(
        "--dry-run",
        action="store_true",
        help="Write request bodies to results/requests.<variant>.jsonl without calling the API.",
    )
    ap.add_argument("--concurrency", type=int, default=8, help="Concurrent HTTP requests in live mode (default: 8).")
    ap.add_argument("--probe", action="store_true", help="Write/overwrite results/probe.json using the first item.")
    ap.add_argument("--in-file", type=pathlib.Path, help="Override input JSONL path.")
    ap.add_argument("--out-dir", type=pathlib.Path, help="Override results directory.")
    args = ap.parse_args(argv)

    if args.probe and args.limit is None:
        # Note: if --probe is passed alone without --limit, still run the requested variant (and refresh probe.json).
        pass

    run_variant(
        variant=args.variant,
        in_path=args.in_file,
        out_dir=args.out_dir,
        limit=args.limit,
        dry_run=args.dry_run,
        concurrency=args.concurrency,
        write_probe_file=args.probe,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
