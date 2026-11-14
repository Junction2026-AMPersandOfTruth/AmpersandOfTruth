#!/usr/bin/env python3
"""P1.R5 (#26, #94) Step 2: standalone bge-m3 embedder via Ollama (Python 3 stdlib only, no repo/DB deps).

    python3 research/26-r5-claims/embed.py --check [check.jsonl]
    python3 research/26-r5-claims/embed.py --in passages.jsonl --out vectors.jsonl [--url URL] [--batch 16]
"""
from __future__ import annotations

import argparse
import json
import math
import os
import pathlib
import sys
import time
import urllib.error
import urllib.request

MODEL = "bge-m3"
DIMS = 1024
MIN_CHECK_COSINE = 0.999


def _log(msg: str) -> None:
    print(msg, file=sys.stderr, flush=True)


def _http_json(url: str, method: str = "GET", body: dict | None = None, timeout: int = 180) -> dict:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    headers = {"Content-Type": "application/json"} if data is not None else {}
    last_err: Exception | None = None
    for attempt in range(3):
        try:
            req = urllib.request.Request(url, data=data, headers=headers, method=method)
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, OSError, json.JSONDecodeError) as ex:
            last_err = ex
            if attempt == 2:
                break
            time.sleep(0.5 * (attempt + 1))
    raise RuntimeError(f"HTTP {method} {url} failed: {last_err}")


def fetch_ollama_version(base_url: str) -> str:
    res = _http_json(base_url.rstrip("/") + "/api/version", method="GET", timeout=15)
    return str(res.get("version", "unknown"))


def embed_batch(base_url: str, texts: list[str]) -> list[list[float]]:
    res = _http_json(
        base_url.rstrip("/") + "/api/embed",
        method="POST",
        body={
            "model": MODEL,
            "input": texts,
            "truncate": True,
            "options": {"num_ctx": 8192, "num_batch": 8192},
        },
        timeout=300,
    )
    vecs = res.get("embeddings")
    if not isinstance(vecs, list) or len(vecs) != len(texts):
        raise RuntimeError(f"expected {len(texts)} embeddings from {MODEL}, got {None if vecs is None else len(vecs)}")
    for v in vecs:
        if not isinstance(v, list) or len(v) != DIMS:
            raise RuntimeError(f"expected {DIMS}-d vector from {MODEL}, got {None if not isinstance(v, list) else len(v)}")
    return vecs


def cosine(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    if na == 0.0 or nb == 0.0:
        return 0.0
    return dot / (na * nb)


def resolve_check_path(check_arg: str, in_path: str | None) -> pathlib.Path:
    if check_arg != "__AUTO__":
        return pathlib.Path(check_arg)
    candidates = [pathlib.Path("check.jsonl")]
    if in_path:
        candidates.append(pathlib.Path(in_path).resolve().parent / "check.jsonl")
    cache_root = pathlib.Path(os.environ.get("AOT_CACHE_DIR", pathlib.Path.home() / ".cache" / "aot-research"))
    candidates.append(cache_root / "r5" / "check.jsonl")
    for c in candidates:
        if c.exists():
            return c
    return candidates[0]


def run_check(base_url: str, check_path: pathlib.Path) -> bool:
    if not check_path.exists():
        print(f"ERROR: check file not found: {check_path}", file=sys.stderr)
        return False
    rows = [json.loads(line) for line in check_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if not rows:
        print(f"ERROR: check file is empty: {check_path}", file=sys.stderr)
        return False

    all_ok = True
    for r in rows:
        pid = r["page_id"]
        inp = r["input"]
        stored = r["stored"]
        t0 = time.monotonic()
        got = embed_batch(base_url, [inp])[0]
        dt_ms = (time.monotonic() - t0) * 1000.0
        cos = cosine(got, stored)
        status = "OK" if cos >= MIN_CHECK_COSINE else "FAIL"
        if cos < MIN_CHECK_COSINE:
            all_ok = False
        print(f"check page_id={pid} ({len(inp)} chars): cosine={cos:.6f} [{status}, {dt_ms:.0f} ms]")
    return all_ok


def load_existing_ids(out_path: pathlib.Path) -> set[str]:
    if not out_path.exists():
        return set()
    done: set[str] = set()
    valid_lines: list[str] = []
    had_corrupt = False
    with out_path.open("r", encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s:
                continue
            try:
                rec = json.loads(s)
                pid = rec["passage_id"]
                emb = rec["embedding"]
                if not isinstance(emb, list) or len(emb) != DIMS:
                    raise ValueError("bad dims")
                done.add(str(pid))
                valid_lines.append(s)
            except Exception:
                had_corrupt = True
    if had_corrupt:
        _log(f"[embed] rewriting {out_path} without partial trailing line ({len(valid_lines)} valid vectors kept)")
        tmp = out_path.with_suffix(out_path.suffix + ".tmp")
        with tmp.open("w", encoding="utf-8") as wf:
            for s in valid_lines:
                print(s, file=wf)
        tmp.replace(out_path)
    return done


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--in", dest="in_path", metavar="passages.jsonl", help="input passages JSONL")
    ap.add_argument("--out", dest="out_path", metavar="vectors.jsonl", help="output vectors JSONL (resumable)")
    ap.add_argument(
        "--url",
        default=os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434"),
        help="Ollama base URL (default: $OLLAMA_URL or http://127.0.0.1:11434)",
    )
    ap.add_argument("--batch", type=int, default=16, help="passages per /api/embed call (default: 16)")
    ap.add_argument(
        "--check",
        nargs="?",
        const="__AUTO__",
        default=None,
        metavar="check.jsonl",
        help="verify cosine >= 0.999 against stored article vectors",
    )
    args = ap.parse_args()

    if args.batch < 1:
        ap.error("--batch must be >= 1")
    if args.check is None and not (args.in_path and args.out_path):
        ap.error("provide --in and --out (or --check)")
    if bool(args.in_path) != bool(args.out_path):
        ap.error("--in and --out must be provided together")

    version = fetch_ollama_version(args.url)
    print(f"Ollama version: {version} ({args.url})")

    if args.check is not None:
        check_path = resolve_check_path(args.check, args.in_path)
        if not run_check(args.url, check_path):
            sys.exit(1)
        if not args.in_path:
            return

    in_path = pathlib.Path(args.in_path)
    out_path = pathlib.Path(args.out_path)
    if not in_path.exists():
        sys.exit(f"input file not found: {in_path}")

    done_ids = load_existing_ids(out_path)
    todo: list[tuple[str, str]] = []
    total_input = 0
    with in_path.open("r", encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if not s:
                continue
            rec = json.loads(s)
            total_input += 1
            pid = str(rec["passage_id"])
            if pid not in done_ids:
                todo.append((pid, rec["text"]))

    if done_ids:
        _log(f"[embed] resuming: {len(done_ids)}/{total_input} already in {out_path}, {len(todo)} remaining")
    else:
        _log(f"[embed] embedding {len(todo)} passages (batch={args.batch}) -> {out_path}")

    out_path.parent.mkdir(parents=True, exist_ok=True)
    t0 = time.monotonic()
    embedded_now = 0

    with out_path.open("a", encoding="utf-8") as out_f:
        for i in range(0, len(todo), args.batch):
            batch = todo[i:i + args.batch]
            vecs = embed_batch(args.url, [txt for _, txt in batch])
            for (pid, _), vec in zip(batch, vecs):
                print(json.dumps({"passage_id": pid, "embedding": vec}), file=out_f)
            out_f.flush()
            embedded_now += len(batch)
            elapsed = max(time.monotonic() - t0, 1e-6)
            rate_min = (embedded_now / elapsed) * 60.0
            _log(
                f"[embed] {len(done_ids) + embedded_now}/{total_input} "
                f"(+{embedded_now} in {elapsed:.1f}s, {rate_min:.1f} passages/min)"
            )

    elapsed_total = time.monotonic() - t0
    if embedded_now > 0:
        sec_per_1k = (elapsed_total / embedded_now) * 1000.0
        rate_min = (embedded_now / max(elapsed_total, 1e-6)) * 60.0
        print(
            f"Embedded {embedded_now} passages in {elapsed_total:.1f}s "
            f"| {sec_per_1k:.1f}s per 1,000 passages ({rate_min:.1f} passages/min) "
            f"| Ollama {version}"
        )
    else:
        print(f"All {total_input} passages already embedded in {out_path} | Ollama {version}")


if __name__ == "__main__":
    main()
