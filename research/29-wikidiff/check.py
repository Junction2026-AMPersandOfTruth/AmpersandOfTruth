#!/usr/bin/env python3
"""Check a wikitext diff implementation against the shared #29 fixtures. Standard library only.

  python3 research/29-wikidiff/check.py                          # the C# engine (scripts/wikidiff.sh): exact match
  python3 research/29-wikidiff/check.py -- python3 my_diff.py    # any other implementation with the same JSON Lines
                                                                 # interface: stats, added_prose and removed_prose must match

fixtures.jsonl holds one {"id", "old", "new"} request per line; expected.jsonl is the C# engine's answer. After a
deliberate engine change, regenerate it (and the xUnit test that guards it will pass again):

  scripts/wikidiff.sh batch < research/29-wikidiff/fixtures.jsonl > research/29-wikidiff/expected.jsonl

Exit code: 0 = agrees, 1 = differs, 2 = the command failed.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
COMPARED = ("stats", "added_prose", "removed_prose")


def main(argv: list[str]) -> int:
    custom = argv[argv.index("--") + 1:] if "--" in argv else []
    command = custom or [str(ROOT / "scripts" / "wikidiff.sh"), "batch"]
    fixtures = (HERE / "fixtures.jsonl").read_text(encoding="utf-8")
    expected = [json.loads(line) for line in (HERE / "expected.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]

    done = subprocess.run(command, input=fixtures, capture_output=True, text=True, encoding="utf-8")
    if done.returncode != 0:
        print(f"ERROR: {' '.join(command)} exited with {done.returncode}\n{done.stderr}")
        return 2
    try:
        actual = [json.loads(line) for line in done.stdout.splitlines() if line.strip()]
    except json.JSONDecodeError as ex:
        print(f"ERROR: output is not JSON Lines ({ex})")
        return 2
    if len(actual) != len(expected):
        print(f"ERROR: {len(actual)} result line(s) for {len(expected)} request(s)")
        return 1

    failures = 0
    for want, got in zip(expected, actual):
        case = want["id"]
        if got.get("id") != case:
            print(f"✗ {case}: answered as id {got.get('id')!r} (results must keep the input order)")
            failures += 1
            continue
        fields = [k for k in want if k != "id"] if not custom else COMPARED
        diff = [k for k in fields if want.get(k) != got.get(k)]
        if diff:
            failures += 1
            print(f"✗ {case}: {', '.join(diff)} differ")
            for k in diff:
                if k != "segments":
                    print(f"    expected {k} = {json.dumps(want.get(k), ensure_ascii=False)}")
                    print(f"    actual   {k} = {json.dumps(got.get(k), ensure_ascii=False)}")
        else:
            print(f"✓ {case}")
    what = "exact output" if not custom else ", ".join(COMPARED)
    print(f"{'OK' if not failures else 'DIFFERS'}: {len(expected) - failures}/{len(expected)} fixtures agree on {what}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
