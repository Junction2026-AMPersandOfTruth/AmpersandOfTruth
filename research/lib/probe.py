#!/usr/bin/env python3
"""Eyeball a retrieval variant on research/lib/probe_queries.json until the golden set + runner exist.

    python3 research/lib/probe.py [--k 20] -- python3 research/8-r1-vector/variant.py --langs fi

Prints a Markdown table: per probe, the rank of each expected fi title (— = not in top k), the top 5 titles,
out_of_scope and wall-clock seconds. A summary line gives "expected titles found in top 10 / top k".
This is NOT a score (the probes are few and were not hand-verified); use it to catch broken variants.
"""
import json
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import aot  # noqa: E402


def main() -> None:
    argv = sys.argv[1:]
    if "--" not in argv:
        sys.exit(__doc__)
    i = argv.index("--")
    opts, cmd = argv[:i], argv[i + 1:]
    k = int(opts[opts.index("--k") + 1]) if "--k" in opts else 20
    probes = json.loads((pathlib.Path(__file__).parent / "probe_queries.json").read_text())["queries"]

    print("| probe | expected (rank) | top 5 | oos | s |\n|---|---|---|---|---:|")
    found10 = found_k = total = 0
    for p in probes:
        req = {"contract": 1, "kind": "retrieval", "query_id": p["id"], "text": p["text"], "k": k}
        t0 = time.monotonic()
        r = subprocess.run(cmd, input=json.dumps(req), capture_output=True, text=True)
        dt = time.monotonic() - t0
        if r.returncode != 0:
            print(f"| {p['id']} | FAILED: {r.stderr.strip()[-200:]} | | | {dt:.1f} |")
            continue
        resp = json.loads(r.stdout)
        ids = [x["page_id"] for x in resp["results"][:k]]
        titles = {}
        if ids:
            titles = {int(x["page_id"]): x["title"] for x in
                      aot.sql(f"SELECT page_id, title FROM article WHERE lang='fi' AND page_id IN ({aot.ints(ids)})")}
        ranked = [titles.get(pid, f"#{pid}") for pid in ids]
        exp = []
        for t in p["expect"]:
            total += 1
            rank = ranked.index(t) + 1 if t in ranked else None
            found10 += bool(rank and rank <= 10)
            found_k += bool(rank)
            exp.append(f"{t} ({rank or '—'})")
        print(f"| {p['id']} | {'; '.join(exp) or '(out of corpus)'} | {'; '.join(ranked[:5])} | "
              f"{resp.get('out_of_scope')} | {dt:.1f} |")
    print(f"\nExpected titles found: {found10}/{total} in top 10, {found_k}/{total} in top {k}.")


if __name__ == "__main__":
    main()
