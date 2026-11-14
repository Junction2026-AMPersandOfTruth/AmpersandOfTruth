#!/usr/bin/env python3
"""Check a file — or a variant's live output — against the eval contract (CONTRACT.md). Standard library only.

  python3 research/eval/validate.py golden [research/eval/golden.json] [--final]
  python3 research/eval/validate.py retrieval-response out.json [--request req.json]
  python3 research/eval/validate.py signals-response - < out.json
  python3 research/eval/validate.py judgements judgements.jsonl
  python3 research/eval/validate.py variant retrieval|signals|pipeline [--request req.json] -- <command ...>

`variant` pipes a request (default: the one in examples/) into your command exactly as the runner will and
validates what comes back, so a variant can be written and checked before the golden set and the runner exist.
Exit code: 0 = valid (warnings allowed), 1 = invalid, 2 = usage.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import contract  # noqa: E402

HERE = Path(__file__).resolve().parent


def _print(rep: contract.Report, what: str) -> int:
    for w in rep.warnings:
        print(f"warning: {w}")
    for e in rep.errors:
        print(f"ERROR:   {e}")
    print(f"{'OK' if rep.ok else 'INVALID'}: {what}" + (f" ({len(rep.warnings)} warning(s))" if rep.warnings and rep.ok else ""))
    return 0 if rep.ok else 1


def _read(path: str) -> str:
    return sys.stdin.read() if path == "-" else Path(path).read_text(encoding="utf-8")


def check_file(shape: str, path: str, request_path: str | None, final: bool) -> int:
    request = json.loads(_read(request_path)) if request_path else None
    text = _read(path)
    rep = contract.Report()
    if contract.SHAPES[shape][1]:   # JSON Lines
        lines = [(n, line) for n, line in enumerate(text.splitlines(), 1) if line.strip()]
        keys: set[str] = set()
        for n, line in lines:
            try:
                value = json.loads(line)
            except json.JSONDecodeError as ex:
                rep.errors.append(f"line {n}: not JSON ({ex.msg})")
                continue
            one = contract.validate(shape, value)
            rep.errors += [f"line {n}: {e}" for e in one.errors]
            if one.ok and value["key"] in keys:
                rep.errors.append(f"line {n}: key '{value['key']}' appears twice")
            if one.ok:
                keys.add(value["key"])
        return _print(rep, f"{path}: {len(lines)} {shape}")
    try:
        value = json.loads(text)
    except json.JSONDecodeError as ex:
        return _print(contract.Report(errors=[f"not JSON: {ex}"]), path)
    rep = contract.validate(shape, value, request=request, final=final)
    if shape == "golden" and not any(e.startswith("$") for e in rep.errors):
        counts = contract.golden_counts(value)
        print(f"golden {value['version']} ({value['dataset']}): " + ", ".join(f"{k}={v}" for k, v in counts.items()))
    return _print(rep, f"{path} as {shape}")


def check_variant(kind: str, command: list[str], request_path: str | None, timeout: float) -> int:
    request = json.loads(_read(request_path or str(HERE / "examples" / f"{kind}.request.json")))
    rep = contract.validate(f"{kind}-request", request)
    if not rep.ok:
        return _print(rep, "the request itself")
    started = time.monotonic()
    try:
        done = subprocess.run(command, input=json.dumps(request, ensure_ascii=False), capture_output=True,
                              text=True, encoding="utf-8", timeout=timeout)
    except subprocess.TimeoutExpired:
        return _print(contract.Report(errors=[f"no answer within {timeout:.0f} s"]), " ".join(command))
    except FileNotFoundError as ex:
        return _print(contract.Report(errors=[f"cannot start the command: {ex}"]), " ".join(command))
    seconds = time.monotonic() - started
    if done.stderr.strip():
        print("── variant stderr ──\n" + done.stderr.rstrip() + "\n────────────────────")
    if done.returncode != 0:
        return _print(contract.Report(errors=[f"exit code {done.returncode} (the runner scores this request as failed)"]), " ".join(command))
    try:
        response = json.loads(done.stdout)
    except json.JSONDecodeError as ex:
        head = done.stdout.strip()[:200]
        return _print(contract.Report(errors=[f"stdout is not one JSON document ({ex.msg}). Logs belong on stderr. stdout began: {head!r}"]), " ".join(command))
    rep = contract.validate(f"{kind}-response", response, request=request)
    size = len(response.get("incidents", response.get("results", []))) if isinstance(response, dict) else 0
    return _print(rep, f"{kind} variant answered in {seconds:.1f} s with {size} item(s)")


def main(argv: list[str]) -> int:
    command: list[str] = []
    if "--" in argv:
        split = argv.index("--")
        argv, command = argv[:split], argv[split + 1:]
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("shape", choices=[*sorted(contract.SHAPES), "variant"])
    p.add_argument("target", nargs="?", help="file path, '-' for stdin; for 'variant': retrieval | signals | pipeline")
    p.add_argument("--request", help="the request the response answers (enables cross-checks)")
    p.add_argument("--final", action="store_true", help="golden sign-off mode: unverified labels and missed size targets are errors")
    p.add_argument("--timeout", type=float, default=120.0, help="seconds to wait for a variant (default 120)")
    args = p.parse_args(argv)

    if args.shape == "variant":
        if args.target not in ("retrieval", "signals", "pipeline") or not command:
            p.error("usage: validate.py variant retrieval|signals|pipeline [--request req.json] -- <command ...>")
        return check_variant(args.target, command, args.request, args.timeout)
    target = args.target or (str(HERE / "golden.json") if args.shape == "golden" else None)
    if target is None:
        p.error("give a file path or '-' for stdin")
    try:
        return check_file(args.shape, target, args.request, args.final)
    except OSError as ex:
        print(f"ERROR:   cannot read {ex.filename}: {ex.strerror}")
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
