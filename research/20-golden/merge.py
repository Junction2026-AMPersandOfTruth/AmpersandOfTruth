#!/usr/bin/env python3
"""Merge labeller + blind labels (+ human decisions) into research/eval/golden.json and write REVIEW.md (stdlib only).

    python3 research/20-golden/merge.py [--version 2026-11-14.1] [--out research/eval/golden.json]

Per item: labeller and blind agree → that label. They disagree → the LOWER level (evidence-standard: when in doubt, the
lower level) until a human decides. `human.json` overrides: {"<item id>": {"label", "exclusion", "category", "verified",
"note", "by"}} or {"<item id>": {"drop": true, "by": ...}}. human_verified = true only via human.json.
REVIEW.md lists (A) disagreements and (B) agreed contested/campaign items: both need a human before --final.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(ROOT / "research" / "eval"))
import contract  # noqa: E402

LABELS = HERE / "labels"
LEVEL = contract.LEVELS
DIFF = "https://fi.wikipedia.org/w/index.php?diff=prev&oldid={}"


def load(p: Path):
    return json.loads(p.read_text()) if p.exists() else None


def fmt(label: str, excl: str | None) -> str:
    return f"{label}/{excl}" if excl else label


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="2026-11-14.1")
    ap.add_argument("--out", default=str(ROOT / "research" / "eval" / "golden.json"))
    a = ap.parse_args()
    seeds = json.loads((HERE / "topics.json").read_text())["topics"]
    human = load(HERE / "human.json") or {}
    topics, review_a, review_b, missing = [], [], [], []
    stats = {"agree": 0, "disagree": 0, "no_blind": 0}
    for s in seeds:
        lab = load(LABELS / f"{s['id']}.labeller.json")
        if not lab:
            missing.append(s["id"]); continue
        t = lab["topic"]
        t["split"], t["kind"] = s["split"], s["kind"]
        blind = load(LABELS / f"{s['id']}.blind.json")
        bmap = load(LABELS / f"{s['id']}.blind-map.json") or {}
        by_item = {v: (blind or {}).get("labels", {}).get(k) for k, v in bmap.items()}
        concerns = {bmap[k]: v for k, v in (blind or {}).get("rev_id_concerns", {}).items() if k in bmap}
        kept = []
        for it in t["items"]:
            it.pop("labelled_by", None)
            b = by_item.get(it["id"])
            lab_l, lab_e = it["label"], it.get("exclusion")
            entry = {"topic": t["id"], "item": it, "labeller": fmt(lab_l, lab_e), "labeller_why": it["why"],
                     "sources": lab.get("item_sources", {}).get(it["id"], [])}
            by = ["claude:labeller"]
            if b:
                by.append("claude:blind")
                entry.update(blind=fmt(b["label"], b.get("exclusion")), blind_why=b.get("why", ""),
                             concern=concerns.get(it["id"]))
                if b["label"] == lab_l:
                    stats["agree"] += 1
                else:
                    stats["disagree"] += 1
                    if LEVEL[b["label"]] < LEVEL[lab_l]:
                        it["label"] = b["label"]
                        it["exclusion"] = b.get("exclusion") if b["label"] == "not-incident" else None
                        if it["label"] != "not-incident":
                            it.pop("exclusion", None)
                    it["notes"] = (f"Labellers disagree: labeller {fmt(lab_l, lab_e)}, blind {fmt(b['label'], b.get('exclusion'))}"
                                   f" (blind: {b.get('why', '')}). Lower level kept until a human decides.")
            else:
                stats["no_blind"] += 1
            h = human.get(it["id"])
            if h and h.get("drop"):
                continue
            if h:
                it["label"] = h.get("label", it["label"])
                if it["label"] == "not-incident":
                    it["exclusion"] = h.get("exclusion", it.get("exclusion"))
                else:
                    it.pop("exclusion", None)
                if "category" in h: it["category"] = h["category"]
                it["human_verified"] = bool(h.get("verified", True))
                by.append(h.get("by", "human"))
                if h.get("note"): it["notes"] = (it.get("notes", "") + " Human: " + h["note"]).strip()
                if "Labellers disagree" in it.get("notes", "") and it["human_verified"]:
                    it["notes"] = it["notes"].replace("Lower level kept until a human decides.", "Human decided.")
            else:
                it["human_verified"] = False
            it["labelled_by"] = by
            kept.append(it)
            if b and b["label"] != lab_l and not h:
                review_a.append(entry)
            elif it["label"] in contract.STRICT_RELEVANT and not it["human_verified"]:
                review_b.append(entry)
        t["items"] = kept
        topics.append(t)
    doc = {"contract": 1, "version": a.version, "dataset": "m-2026-10",
           "notes": "P1.E1 (#20). Built by research/20-golden (packs → Claude labeller → blind Claude re-label → human). "
                    "Labels follow docs/evidence-standard.md. The test split is sealed: only R6/S11 may score it (EVAL_ALLOW_TEST=1).",
           "topics": topics}
    Path(a.out).write_text(json.dumps(doc, ensure_ascii=False, indent=1) + "\n")
    rep = contract.check_golden(doc)
    counts = contract.golden_counts(doc)
    write_review(review_a, review_b, counts, stats, missing)
    print(json.dumps({"counts": counts, "agreement": stats, "missing_topics": missing,
                      "review": {"A_disagreements": len(review_a), "B_verify": len(review_b)}}, indent=1))
    for e in rep.errors: print("ERROR", e)
    for w in rep.warnings: print("WARN ", w)
    return 1 if rep.errors else 0


def block(e: dict) -> str:
    it = e["item"]
    ins = " ".join(f"[{r}]({DIFF.format(r)})" for r in it["inserting_rev_ids"])
    rem = " ".join(f"[{r}]({DIFF.format(r)})" for r in it["removing_rev_ids"])
    lines = [f"#### `{it['id']}` · labeller **{e['labeller']}**" + (f" · blind **{e['blind']}**" if e.get("blind") else ""),
             f"{it['description']}",
             f"- added: {ins}  ·  removed: {rem}  ·  page {it['page_id']}  ·  candidates {', '.join(e['sources'])}",
             f"- labeller: {e['labeller_why']}"]
    if e.get("blind"): lines.append(f"- blind: {e['blind_why']}")
    if e.get("concern"): lines.append(f"- blind's rev-id concern: {e['concern']}")
    lines.append(f"- **decision:** ☐ agree with ______ ☐ other: ______")
    return "\n".join(lines)


def write_review(a: list, b: list, counts: dict, stats: dict, missing: list) -> None:
    out = ["# Golden set: human review (P1.E1, #20)", "",
           "Read each item's diffs on fi.wikipedia (links), then record a decision in `research/20-golden/human.json`:",
           '`{"<item id>": {"label": "contested", "verified": true, "by": "human:<initials>", "note": "optional"}}`',
           '(`"exclusion"` for not-incident, `"drop": true` to remove an item). Then re-run `merge.py`.', "",
           f"Counts: {json.dumps(counts)}", f"Labeller vs blind: {json.dumps(stats)}",
           f"Topics not labelled yet: {', '.join(missing) or 'none'}", "",
           f"## A. Labellers disagree ({len(a)}): the draft keeps the lower level", ""]
    out += [block(e) + "\n" for e in a]
    out += [f"## B. Agreed `contested` / `campaign` ({len(b)}): must be human-verified before sign-off", ""]
    out += [block(e) + "\n" for e in b]
    (HERE / "REVIEW.md").write_text("\n".join(out))


if __name__ == "__main__":
    sys.exit(main())
