#!/usr/bin/env python3
"""Blind the classify-edit outputs for a different-vendor judge, and merge its ratings back.

    python3 blind.py make  --sample sample20.jsonl --results results/classify-edit-v1.jsonl \
                           --out judge/judge-input.md --mapping judge/mapping.json [--seed 16]
    python3 blind.py merge --ratings judge/ratings-blind.json --mapping judge/mapping.json --out judge-claude.json

The judge sees each edit's raw evidence and the model outputs as letters A, B, C… in a per-edit random order,
so it can't learn which letter is which model. Its blind ratings file must look like:
    {"reference": {"<id>": {"category": "...", "claim_en": "...", "note": "..."}},
     "ratings": [{"id": "<id>", "letter": "A", "category_ok": "yes|partly|no", "claim_faithful": 1-5,
                  "english_quality": 1-5, "hallucination": true|false, "comment": "..."}]}
merge replaces letter with model.
"""
import argparse
import json
import random
import string


def load_jsonl(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def make(a):
    sample = load_jsonl(a.sample)
    by = {}
    for r in load_jsonl(a.results):
        by.setdefault(r["id"], {})[r["model"]] = r
    rng = random.Random(a.seed)
    mapping = {}
    out = ["# Blind review: classifications of reverted Finnish Wikipedia edits\n"]
    for n, s in enumerate(sample, 1):
        outputs = by.get(s["_id"], {})
        models = sorted(outputs)
        rng.shuffle(models)
        mapping[s["_id"]] = {}
        out.append(f"## Edit {n} — id `{s['_id']}`\n")
        out.append(f"- Article: {s['article_title']}  \n- When: {s['edit_timestamp']}, {s['editor_kind']} editor, live for {s['exposure']}")
        out.append(f"- Edit summary: {s['edit_summary'] or '(empty)'}  \n- Revert summary: {s['revert_summary'] or '(empty)'}\n")
        for label, key in (("REMOVED", "removed_text"), ("INSERTED", "inserted_text"), ("CONTEXT (before the edit)", "context")):
            out.append(f"**{label}:**\n```\n{s[key] or '(empty)'}\n```")
        for letter, m in zip(string.ascii_uppercase, models):
            mapping[s["_id"]][letter] = m
            r = outputs[m]
            body = json.dumps(r["value"], ensure_ascii=False, indent=1) if r["outcome"] == "ok" else f'(no output: {r["outcome"]})'
            out.append(f"**Output {letter}:**\n```json\n{body}\n```")
        out.append("")
    open(a.out, "w", encoding="utf-8").write("\n".join(out) + "\n")
    json.dump(mapping, open(a.mapping, "w", encoding="utf-8"), indent=1)
    print(f"wrote {a.out} ({len(sample)} edits) and {a.mapping}")


def merge(a):
    blind = json.load(open(a.ratings, encoding="utf-8"))
    mapping = json.load(open(a.mapping, encoding="utf-8"))
    ratings = []
    for r in blind["ratings"]:
        r = dict(r)
        r["model"] = mapping[r["id"]][r.pop("letter")]
        ratings.append(r)
    json.dump({"reference": blind.get("reference", {}), "ratings": ratings},
              open(a.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"wrote {a.out} ({len(ratings)} ratings)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    m = sub.add_parser("make")
    m.add_argument("--sample", required=True)
    m.add_argument("--results", required=True)
    m.add_argument("--out", required=True)
    m.add_argument("--mapping", required=True)
    m.add_argument("--seed", type=int, default=16)
    g = sub.add_parser("merge")
    g.add_argument("--ratings", required=True)
    g.add_argument("--mapping", required=True)
    g.add_argument("--out", required=True)
    a = ap.parse_args()
    make(a) if a.cmd == "make" else merge(a)
