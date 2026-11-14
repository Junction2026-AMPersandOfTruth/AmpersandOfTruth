#!/usr/bin/env python3
"""P1.R3 (#24) single-call LLM topic expansion via scripts/analyze.sh (cached in SQLite).

    python3 research/24-r3-query/expand.py "Winter War causes"
    python3 research/24-r3-query/expand.py --dump-keywords

Calls `expand-topic` once per distinct normalised topic text via `scripts/analyze.sh run`.
Non-Ok outcomes return `None` (caller falls back to R1 vector retrieval) and never crash.
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import time

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
PROMPT_DIR = HERE / "prompts"
ANALYZE_SH = REPO_ROOT / "scripts" / "analyze.sh"

sys.path.insert(0, str(HERE.parent / "lib"))
import aot  # noqa: E402

_STEM_CLEAN = re.compile(r"[^a-zåäö0-9]+")


def clean_keywords(raw_keywords: list[str] | None) -> list[str]:
    """Ensure every keyword is a safe lowercase single-word stem ending with '*' (>=3 chars before '*')."""
    if not raw_keywords:
        return []
    out: list[str] = []
    seen: set[str] = set()
    for item in raw_keywords:
        if not isinstance(item, str):
            continue
        for token in item.lower().rstrip("*").replace("-", " ").replace("–", " ").split():
            stem = _STEM_CLEAN.sub("", token)
            if len(stem) >= 3:
                kw = stem + "*"
                if kw not in seen:
                    seen.add(kw)
                    out.append(kw)
    return out


def expand_topic(
    text: str,
    *,
    scope: str | None = None,
    cache_mode: str = "default",
    model: str | None = None,
    timeout: int = 90,
) -> tuple[dict | None, dict]:
    """Run `expand-topic` once. Returns `(expansion_dict_or_None, meta_dict)` and never raises."""
    norm = " ".join((text or "").split())
    empty_meta = {
        "outcome": "empty",
        "from_cache": False,
        "llm_calls": 0,
        "attempts": 0,
        "latency_ms": 0,
        "wall_s": 0.0,
        "input_tokens": 0,
        "output_tokens": 0,
        "thought_tokens": 0,
        "cached_tokens": 0,
        "cost_usd": 0.0,
        "model": model or "",
        "prompt_version": "",
        "error": "empty topic",
    }
    if not norm:
        return None, empty_meta

    eff_scope = scope if scope is not None else os.environ.get("R3_SCOPE", "r3-query")
    tmp_path: str | None = None
    t0 = time.monotonic()
    try:
        with tempfile.NamedTemporaryFile("w", suffix=".json", encoding="utf-8", delete=False) as tf:
            json.dump({"topic": norm}, tf, ensure_ascii=False)
            tmp_path = tf.name

        cmd = [str(ANALYZE_SH), "run", "expand-topic", "--vars", tmp_path]
        if eff_scope:
            cmd += ["--scope", eff_scope]
        if model:
            cmd += ["--model", model]
        if cache_mode == "no-cache":
            cmd.append("--no-cache")
        elif cache_mode == "refresh":
            cmd.append("--refresh")

        env = dict(os.environ)
        env["Gemini__PromptDirectory"] = str(PROMPT_DIR)
        if not env.get("Gemini__ApiKey") and env.get("GEMINI_API_KEY"):
            env["Gemini__ApiKey"] = env["GEMINI_API_KEY"]

        proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", env=env, timeout=timeout)
        wall_s = time.monotonic() - t0
        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip()[-400:]
            aot.log(f"expand: analyze.sh exited {proc.returncode}: {err}")
            return None, {**empty_meta, "outcome": "cli_error", "wall_s": round(wall_s, 3), "error": err}

        line = json.loads(proc.stdout)
    except Exception as ex:
        wall_s = time.monotonic() - t0
        aot.log(f"expand: failed ({type(ex).__name__}: {ex}); falling back to R1")
        return None, {**empty_meta, "outcome": "exception", "wall_s": round(wall_s, 3), "error": str(ex)}
    finally:
        if tmp_path:
            try:
                os.unlink(tmp_path)
            except OSError:
                pass

    outcome = line.get("outcome", "failed")
    val = line.get("value") if outcome == "ok" and isinstance(line.get("value"), dict) else None
    attempts = int(line.get("attempts") or 0)
    from_cache = bool(line.get("from_cache"))
    # One logical LLM expansion per search when the prompt succeeded or attempted an HTTP call.
    llm_calls = 1 if (val is not None or attempts > 0) else 0

    meta = {
        "outcome": outcome,
        "from_cache": from_cache,
        "llm_calls": llm_calls,
        "attempts": attempts,
        "latency_ms": int(line.get("latency_ms") or 0),
        "wall_s": round(wall_s, 3),
        "input_tokens": int(line.get("input_tokens") or 0),
        "output_tokens": int(line.get("output_tokens") or 0),
        "thought_tokens": int(line.get("thought_tokens") or 0),
        "cached_tokens": int(line.get("cached_tokens") or 0),
        "cost_usd": float(line.get("cost_usd") or 0.0),
        "model": line.get("model") or "",
        "prompt_version": line.get("prompt_version") or "",
        "error": line.get("error"),
    }

    if val is None:
        aot.log(f"expand: non-ok outcome={outcome!r} error={meta['error']!r}; falling back to R1")
        return None, meta

    val = dict(val)
    val["keywords_fi"] = clean_keywords(val.get("keywords_fi"))
    return val, meta


def dump_probe_keywords(out_path: pathlib.Path, scope: str | None = None) -> None:
    probes_file = HERE.parent / "lib" / "probe_queries.json"
    probes = json.loads(probes_file.read_text(encoding="utf-8"))["queries"]
    out_path.parent.mkdir(parents=True, exist_ok=True)
    lines: list[str] = []
    for p in probes:
        val, meta = expand_topic(p["text"], scope=scope)
        kws = val["keywords_fi"] if val else []
        lines.append(json.dumps({"text": p["text"], "keywords": kws}, ensure_ascii=False))
        aot.log(f"keywords {p['id']}: {kws} (cache={meta['from_cache']}, {meta['latency_ms']}ms)")
    nl = chr(10)
    out_path.write_text(nl.join(lines) + nl, encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("text", nargs="*", help="Topic text to expand")
    ap.add_argument("--scope", default=None, help="Analysis ledger scope (default: env R3_SCOPE or 'r3-query')")
    ap.add_argument("--model", default=None, help="Optional Gemini model override")
    ap.add_argument("--no-cache", action="store_true", help="Bypass AnalysisCache")
    ap.add_argument("--refresh", action="store_true", help="Refresh AnalysisCache entry")
    ap.add_argument("--dump-keywords", action="store_true", help="Write results/keywords.jsonl for probe_queries.json")
    ap.add_argument("--out", default=str(HERE / "results" / "keywords.jsonl"), help="Output path for --dump-keywords")
    args = ap.parse_args()

    if args.dump_keywords:
        dump_probe_keywords(pathlib.Path(args.out), scope=args.scope)
        return

    topic = " ".join(args.text).strip()
    if not topic and not sys.stdin.isatty():
        topic = sys.stdin.read().strip()
    if not topic:
        ap.error("provide <text> or --dump-keywords")

    cache_mode = "no-cache" if args.no_cache else ("refresh" if args.refresh else "default")
    val, _ = expand_topic(topic, scope=args.scope, cache_mode=cache_mode, model=args.model)
    print(json.dumps(val, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
