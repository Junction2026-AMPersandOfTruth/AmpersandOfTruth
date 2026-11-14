"""Zero-shot LLM vs Finnish lexicon comparison on 600 distinct revert summaries (P1.S5, #32).

Deduplicates masked `reason_text` values across reverts in `research.s5_revert_reason`
(normalised whitespace + lowercase), selects the 400 most frequent + 200 seeded random
distinct values (`seed=32`), runs `scripts/analyze.sh batch classify-revert-reason` if
requested (or reads existing `results/llm.jsonl`), and reports per-category agreement
plus disagreements.

Usage:
    python3 research/32-s5-summaries/llm_compare.py --run
    python3 research/32-s5-summaries/llm_compare.py          # analyse existing results/llm.jsonl
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import pathlib
import random
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "lib"))
sys.path.insert(0, str(HERE))

import aot  # noqa: E402
import lexicon  # noqa: E402

TOP_N = 400
RANDOM_N = 200
SEED = 32
DEFAULT_MODEL = "gemini-4-hydrogen"


def normalise_reason_text(comment: str | None) -> str:
    """Extract human `reason_text`, mask IPs/links, collapse whitespace, and lowercase."""
    rt = lexicon.extract_reason_text(comment)
    if not rt:
        return ""
    masked = aot.mask_comment(rt)
    return " ".join(masked.split()).lower()


def load_sample_600() -> tuple[list[dict[str, object]], dict[str, int]]:
    """Fetch revert comments for `research.s5_revert_reason` and select 400 top + 200 seeded random."""
    rows = aot.sql(
        """
        SELECT rr.reverting_rev_id, rev.comment
        FROM research.s5_revert_reason rr
        JOIN corpus.revision rev
          ON rev.page_id = rr.page_id AND rev.rev_id = rr.reverting_rev_id
        WHERE rev.comment IS NOT NULL AND rev.comment <> ''
        """,
        timeout=85,
    )

    counts: collections.Counter[str] = collections.Counter()
    total_nonempty_rt = 0
    for r in rows:
        norm = normalise_reason_text(r["comment"])
        if norm:
            counts[norm] += 1
            total_nonempty_rt += 1

    ordered = sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))
    top_part = ordered[:TOP_N]
    rest_part = ordered[TOP_N:]

    rng = random.Random(SEED)
    rnd_part = rng.sample(rest_part, min(RANDOM_N, len(rest_part)))
    rnd_part.sort(key=lambda kv: (-kv[1], kv[0]))

    items: list[dict[str, object]] = []
    for idx, (text, freq) in enumerate(top_part, 1):
        items.append(
            {
                "id": f"top-{idx:03d}",
                "split": "top400",
                "summary": text,
                "revert_count": freq,
                "lexicon": sorted(lexicon.reasons(text)),
            }
        )
    for idx, (text, freq) in enumerate(rnd_part, 1):
        items.append(
            {
                "id": f"rnd-{idx:03d}",
                "split": "random200",
                "summary": text,
                "revert_count": freq,
                "lexicon": sorted(lexicon.reasons(text)),
            }
        )

    meta = {
        "reverts_with_reason_text": total_nonempty_rt,
        "distinct_normalised_reason_texts": len(ordered),
        "top400_revert_coverage": sum(freq for _, freq in top_part),
        "sample_total": len(items),
    }
    return items, meta


def run_batch(
    items: list[dict[str, object]],
    out_path: pathlib.Path,
    model: str = DEFAULT_MODEL,
    concurrency: int = 12,
) -> None:
    """Write masked JSONL input to a temp file and invoke `scripts/analyze.sh batch`."""
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.unlink(missing_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w", suffix=".jsonl", prefix="s5_llm_in_", encoding="utf-8", delete=False
    ) as tf:
        in_path = pathlib.Path(tf.name)
        for item in items:
            # Double-mask check: ensure no raw IP/temp identifier ever enters batch input.
            safe_summary = aot.mask_comment(str(item["summary"]))
            tf.write(json.dumps({"id": item["id"], "summary": safe_summary}, ensure_ascii=False) + chr(10))

    env = os.environ.copy()
    env["Gemini__PromptDirectory"] = str(HERE / "prompts")
    if not env.get("Gemini__ApiKey") and env.get("GEMINI_API_KEY"):
        env["Gemini__ApiKey"] = env["GEMINI_API_KEY"]
    cmd = [
        str(REPO_ROOT / "scripts" / "analyze.sh"),
        "batch",
        "classify-revert-reason",
        "--in",
        str(in_path),
        "--out",
        str(out_path),
        "--scope",
        "s5",
        "--model",
        model,
        "--concurrency",
        str(concurrency),
    ]
    try:
        subprocess.run(cmd, cwd=str(REPO_ROOT), env=env, stdout=sys.stderr, check=True)
    finally:
        in_path.unlink(missing_ok=True)


def evaluate(
    items: list[dict[str, object]],
    llm_path: pathlib.Path,
) -> dict[str, object]:
    """Compare lexicon vs LLM predictions across all 9 categories."""
    by_id: dict[str, dict[str, object]] = {}
    outcomes: collections.Counter[str] = collections.Counter()

    with llm_path.open(encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            rec = json.loads(line)
            outcomes[rec["outcome"]] += 1
            if rec["outcome"] == "ok" and rec.get("value") is not None:
                by_id[rec["id"]] = rec["value"]

    paired: list[dict[str, object]] = []
    exact_set_matches = 0
    for item in items:
        iid = str(item["id"])
        if iid not in by_id:
            continue
        val = by_id[iid]
        llm_cats = sorted(set(val.get("categories") or []))
        lex_cats = list(item["lexicon"])  # type: ignore[arg-type]
        if llm_cats == lex_cats:
            exact_set_matches += 1
        paired.append(
            {
                **item,
                "llm": llm_cats,
                "confidence": val.get("confidence"),
            }
        )

    n = len(paired)
    per_category: list[dict[str, object]] = []
    for cat in lexicon.REASON_CATEGORIES:
        tp = sum(1 for p in paired if cat in p["lexicon"] and cat in p["llm"])  # type: ignore[operator]
        lex_only = sum(1 for p in paired if cat in p["lexicon"] and cat not in p["llm"])  # type: ignore[operator]
        llm_only = sum(1 for p in paired if cat not in p["lexicon"] and cat in p["llm"])  # type: ignore[operator]
        tn = n - tp - lex_only - llm_only
        lex_pos = tp + lex_only
        llm_pos = tp + llm_only
        agree = (tp + tn) / n if n else 0.0
        denom_f1 = 2 * tp + lex_only + llm_only
        f1 = (2 * tp / denom_f1) if denom_f1 else 1.0
        union = tp + lex_only + llm_only
        jaccard = (tp / union) if union else 1.0

        # Cohen's kappa
        p_o = agree
        p_yes = (lex_pos / n) * (llm_pos / n) if n else 0.0
        p_no = ((n - lex_pos) / n) * ((n - llm_pos) / n) if n else 0.0
        p_e = p_yes + p_no
        kappa = ((p_o - p_e) / (1.0 - p_e)) if p_e < 1.0 else 1.0

        per_category.append(
            {
                "category": cat,
                "lexicon_pos": lex_pos,
                "llm_pos": llm_pos,
                "both_pos": tp,
                "lexicon_only": lex_only,
                "llm_only": llm_only,
                "agreement_pct": round(100.0 * agree, 2),
                "f1": round(f1, 3),
                "jaccard": round(jaccard, 3),
                "kappa": round(kappa, 3),
            }
        )

    disagreements = [p for p in paired if p["lexicon"] != p["llm"]]
    return {
        "outcomes": dict(outcomes),
        "evaluated_items": n,
        "exact_labelset_agreement": exact_set_matches,
        "exact_labelset_agreement_pct": round(100.0 * exact_set_matches / n, 2) if n else 0.0,
        "per_category": per_category,
        "disagreement_count": len(disagreements),
        "sample_disagreements": disagreements[:40],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="Compare P1.S5 lexicon vs Gemini on 600 distinct revert summaries.")
    ap.add_argument("--run", action="store_true", help="Execute scripts/analyze.sh batch to produce results/llm.jsonl.")
    ap.add_argument("--out", default=str(HERE / "results" / "llm.jsonl"), help="Output JSONL path.")
    ap.add_argument("--model", default=DEFAULT_MODEL, help="Gemini model name.")
    ap.add_argument("--concurrency", type=int, default=12, help="Batch concurrency.")
    args = ap.parse_args()

    out_path = pathlib.Path(args.out)
    items, meta = load_sample_600()
    if args.run:
        run_batch(items, out_path, model=args.model, concurrency=args.concurrency)

    if not out_path.exists():
        raise SystemExit(f"Missing {out_path}; run with --run first.")

    report = {"sample_meta": meta, **evaluate(items, out_path)}
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
