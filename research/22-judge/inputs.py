#!/usr/bin/env python3
"""Build JSONL batch inputs for `judge-incident` and `adversarial-review` (#22).

Usage:
  python3 research/22-judge/inputs.py golden --split dev --out results/golden-dev.inputs.jsonl
  python3 research/22-judge/inputs.py unlabelled <x.unlabelled.jsonl> --out f.jsonl [--with-flag]
"""
from __future__ import annotations

import argparse
import ipaddress
import json
import re
import os
import pathlib
import sys
from typing import Any, Iterable

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))

import evidence  # noqa: E402

NL = chr(10)
FORBIDDEN_BLIND_KEYS = frozenset(
    {"label", "exclusion", "category", "description", "why", "notes", "labelled_by"}
)
TIER_RANK = {"not-incident": 0, "reverted": 1, "contested": 2, "campaign": 3}
CHECKED_PRIVACY_FIELDS = ("insertions", "removals", "history")


def resolve_out_path(raw_out: str | None, default_name: str) -> pathlib.Path:
    if not raw_out:
        return HERE / "results" / default_name
    p = pathlib.Path(raw_out)
    if p.is_absolute():
        return p
    if p.parts and p.parts[0] == "results" and pathlib.Path.cwd().resolve() != HERE and not (pathlib.Path.cwd() / "results").exists():
        return HERE / p
    return p


def assert_blind_record(record: dict[str, Any], context_id: str) -> None:
    leaked = FORBIDDEN_BLIND_KEYS.intersection(record.keys())
    if leaked:
        raise ValueError(f"{context_id}: blind violation — forbidden keys present: {sorted(leaked)}")


def assert_no_raw_identities(
    vars_dict: dict[str, str], involved_users: Iterable[str | None], *, context_id: str = ""
) -> None:
    """Fail loudly if any involved raw username or IP appears in `insertions`, `removals`, or `history`."""
    prefix = f"{context_id}: " if context_id else ""
    for raw in involved_users:
        if raw is None or not raw.strip():
            continue
        u = raw.strip()
        ip_forms: list[str] = []
        try:
            ip_obj = ipaddress.ip_address(u)
            ip_forms = list(dict.fromkeys([u.lower(), ip_obj.compressed.lower(), ip_obj.exploded.lower()]))
        except ValueError:
            pass

        for fname in CHECKED_PRIVACY_FIELDS:
            val = vars_dict.get(fname) or ""
            if ip_forms:
                low = val.lower()
                for form in ip_forms:
                    if form and form in low:
                        raise ValueError(
                            f"{prefix}privacy violation: raw IP address of an involved revision appears in '{fname}'"
                        )
            else:
                candidates = [u]
                if " " in u:
                    candidates.append(u.replace(" ", "_"))
                for cand in candidates:
                    # Whole-word match; names under 3 chars (e.g. "8", "Jo") occur by chance in any text.
                    if len(cand) >= 3 and re.search(r"(?<!\w)" + re.escape(cand) + r"(?!\w)", val):
                        raise ValueError(
                            f"{prefix}privacy violation: raw account name (len={len(u)}) of an involved revision appears in '{fname}'"
                        )


def build_golden_inputs(
    golden_path: pathlib.Path, split: str, out_path: pathlib.Path, *, store: evidence.CorpusStore | None = None
) -> tuple[int, int]:
    if split == "test" and os.environ.get("EVAL_ALLOW_TEST") != "1":
        raise PermissionError("test split is sealed (set EVAL_ALLOW_TEST=1 only in final decision issues)")

    doc = json.loads(golden_path.read_text(encoding="utf-8"))
    topics = [t for t in doc.get("topics", []) if t.get("split") == split]
    items = [it for t in topics for it in t.get("items", [])]

    st = store if store is not None else evidence.DEFAULT_STORE
    st.prefetch(items)

    lines: list[str] = []
    missing_diffs = 0
    for it in items:
        item_id = it["id"]
        key = evidence.incident_key(it["inserting_rev_ids"])
        vars_dict, meta = evidence.build_with_meta(
            it["page_id"],
            it["inserting_rev_ids"],
            it["removing_rev_ids"],
            it.get("other_page_ids") or (),
            store=st,
        )
        missing_diffs += int(meta["missing_diffs"])
        assert_no_raw_identities(vars_dict, meta["involved_users"], context_id=item_id)
        record = {"_id": item_id, "key": key, **vars_dict}
        assert_blind_record(record, item_id)
        lines.append(json.dumps(record, ensure_ascii=False))

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(NL.join(lines) + (NL if lines else ""), encoding="utf-8")
    return len(lines), missing_diffs


def deduplicate_unlabelled(raw_lines: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    by_key: dict[str, dict[str, Any]] = {}
    for row in raw_lines:
        key = row.get("key") or evidence.incident_key(row["inserting_rev_ids"])
        row_copy = dict(row)
        row_copy["key"] = key
        existing = by_key.get(key)
        if existing is None:
            by_key[key] = row_copy
        elif TIER_RANK.get(row_copy.get("tier") or "", -1) > TIER_RANK.get(existing.get("tier") or "", -1):
            by_key[key] = row_copy
    return list(by_key.values())


def build_unlabelled_inputs(
    unlabelled_path: pathlib.Path,
    out_path: pathlib.Path,
    *,
    with_flag: bool = False,
    store: evidence.CorpusStore | None = None,
) -> tuple[int, int]:
    raw_rows = [
        json.loads(line)
        for line in unlabelled_path.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    items = deduplicate_unlabelled(raw_rows)

    st = store if store is not None else evidence.DEFAULT_STORE
    st.prefetch(items)

    lines: list[str] = []
    missing_diffs = 0
    for it in items:
        key = it["key"]
        vars_dict, meta = evidence.build_with_meta(
            it["page_id"],
            it["inserting_rev_ids"],
            it["removing_rev_ids"],
            it.get("other_page_ids") or (),
            store=st,
        )
        missing_diffs += int(meta["missing_diffs"])
        assert_no_raw_identities(vars_dict, meta["involved_users"], context_id=key)
        record: dict[str, Any] = {"_id": key, "key": key}
        if with_flag:
            if not it.get("tier"):
                raise ValueError(f"{key}: --with-flag requires 'tier' on every unlabelled incident line")
            record["flagged_as"] = str(it["tier"])
        record.update(vars_dict)
        assert_blind_record(record, key)
        lines.append(json.dumps(record, ensure_ascii=False))

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(NL.join(lines) + (NL if lines else ""), encoding="utf-8")
    return len(lines), missing_diffs


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_gold = sub.add_parser("golden", help="Build blind judge inputs for golden-set items")
    p_gold.add_argument("--split", default="dev", choices=["dev", "test"])
    p_gold.add_argument("--golden", default=str(ROOT / "research" / "eval" / "golden.json"))
    p_gold.add_argument("--out", default=None, help="Output JSONL path (default: results/golden-<split>.inputs.jsonl)")

    p_unl = sub.add_parser("unlabelled", help="Build judge/review inputs for an unlabelled.jsonl file")
    p_unl.add_argument("source", help="Path to <variant>.<split>.unlabelled.jsonl")
    p_unl.add_argument("--out", required=True, help="Output JSONL path")
    p_unl.add_argument("--with-flag", action="store_true", help="Include flagged_as = tier (for adversarial-review)")

    args = parser.parse_args(argv)
    try:
        if args.cmd == "golden":
            out_path = resolve_out_path(args.out, f"golden-{args.split}.inputs.jsonl")
            count, missing = build_golden_inputs(pathlib.Path(args.golden), args.split, out_path)
            print(f"wrote {count} golden ({args.split}) inputs -> {out_path} ({missing} missing diffs)", file=sys.stderr)
            return 0

        out_path = resolve_out_path(args.out, "unlabelled.inputs.jsonl")
        count, missing = build_unlabelled_inputs(
            pathlib.Path(args.source), out_path, with_flag=args.with_flag
        )
        print(f"wrote {count} unlabelled inputs -> {out_path} ({missing} missing diffs)", file=sys.stderr)
        return 0
    except PermissionError as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
