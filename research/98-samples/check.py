#!/usr/bin/env python3
"""Validate P2.0 sample fixture files against incident.schema.json, C# contracts, and MariaDB corpus.

Standard library Python 3 only.

Usage:
  python3 research/98-samples/check.py            # schema + contracts + coverage + live MariaDB check
  python3 research/98-samples/check.py --no-db    # offline schema + contracts + coverage check only
"""

from __future__ import annotations

import argparse
import datetime
import ipaddress
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "research" / "eval"))
import contract  # noqa: E402

FIXTURES_DIR = ROOT / "src" / "AmpersandOfTruth.Web" / "Features" / "Investigations" / "Fixtures"
EXPECTED_FILES = {
    "civil-war-1918.json": {
        "slug": "civil-war-1918",
        "title": "Civil War 1918",
        "id_prefix": "cw18-",
        "required_aliases": {"suomen sisällissota", "finnish civil war", "1918 civil war"},
    },
    "continuation-war.json": {
        "slug": "continuation-war",
        "title": "Continuation War",
        "id_prefix": "cnt-",
        "required_aliases": {"jatkosota"},
    },
    "winter-war.json": {
        "slug": "winter-war",
        "title": "Winter War",
        "id_prefix": "ww-",
        "required_aliases": {"talvisota"},
    },
}

SIGNAL_CATALOG_IDS = {
    "removed_by_others",
    "removal_reason",
    "reinserted",
    "edit_war",
    "dispute_template",
    "claim_content",
    "multiple_editors",
    "multiple_articles",
    "persistence",
    "source_change",
    "citation_removed",
    "number_changed",
    "new_accounts",
    "anonymous",
}

TIER_LABELS = {
    "reverted": "Removed by Wikipedia editors",
    "contested": "Contested push",
    "campaign": "Sustained push",
}

COPY_RULE_PATTERNS = [
    ("Accusation", re.compile(r"\b(propagandists?|trolls?|sockpuppets?|bad actors?|liars?|paid editors?|fake accounts?|bot farms?|guilty|malicious)\b", re.I)),
    ("Certainty", re.compile(r"\b(proven|confirmed false|definitely|undeniably|beyond doubt|fact-checked false)\b", re.I)),
    ("Intent", re.compile(r"\b(tried to|deliberately|intentionally|in bad faith|ulterior motive|on purpose|calculated attempt|agenda-driven|coordinated attack)\b", re.I)),
    ("Jargon", re.compile(r"\b(reverts?|reverted|reverting|sha1|tiers?|llms?)\b", re.I)),
]

UNMASKED_IPV4 = re.compile(r"(?<![0-9.])\d{1,3}(?:\.\d{1,3}){3}(?![0-9.])")
UNMASKED_IPV6 = re.compile(r"(?<![0-9A-Fa-f:])[0-9A-Fa-f]{1,4}(?:::[0-9A-Fa-f:]+|(?::[0-9A-Fa-f]{1,4}){2,7})(?![0-9A-Fa-f:])")
IPV4_STRICT = re.compile(r"^\d{1,3}(\.\d{1,3}){3}$")
TEMP_RE = re.compile(r"^~\d{4}-")
ISO_UTC_RE = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")


def mask_user(user: str | None) -> tuple[str, bool, str]:
    if user is None or user.strip() == "":
        return "Hidden editor", False, "hidden"
    trimmed = user.strip()
    if TEMP_RE.match(trimmed):
        return trimmed, True, f"temp:{trimmed}"
    if IPV4_STRICT.match(trimmed):
        try:
            b = ipaddress.IPv4Address(trimmed).packed
            return f"{b[0]}.{b[1]}.x.x", True, f"ipv4:{b[0]}.{b[1]}"
        except ValueError:
            pass
    if ":" in trimmed:
        try:
            b = ipaddress.IPv6Address(trimmed).packed
            g1 = f"{(b[0] << 8) | b[1]:X}"
            g2 = f"{(b[2] << 8) | b[3]:X}"
            return f"{g1}:{g2}:\u2026", True, f"ipv6:{g1}:{g2}"
        except ValueError:
            pass
    return trimmed, False, f"user:{trimmed}"


def parse_ts(s: str) -> datetime.datetime:
    return datetime.datetime.strptime(s, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=datetime.timezone.utc)


def run_sql(sql: str) -> str:
    res = subprocess.run(
        ["mariadb", "-h127.0.0.1", "-P3307", "-ucorpus_ro", "corpus", "--batch", "--raw", "-e", sql],
        capture_output=True,
        text=True,
        timeout=60,
        check=True,
    )
    return res.stdout


def check_all(verify_db: bool) -> int:
    errors: list[str] = []
    all_incidents: list[tuple[str, dict]] = []

    for fname, meta in EXPECTED_FILES.items():
        fpath = FIXTURES_DIR / fname
        if not fpath.exists():
            errors.append(f"missing file: {fpath}")
            continue

        raw_text = fpath.read_text(encoding="utf-8")
        for m in UNMASKED_IPV4.finditer(raw_text):
            try:
                ipaddress.IPv4Address(m.group(0))
                errors.append(f"{fname}: unmasked IPv4 address in file: {m.group(0)}")
            except ValueError:
                pass
        for m in UNMASKED_IPV6.finditer(raw_text):
            try:
                ipaddress.IPv6Address(m.group(0))
                errors.append(f"{fname}: unmasked IPv6 address in file: {m.group(0)}")
            except ValueError:
                pass

        doc = json.loads(raw_text)
        if doc.get("slug") != meta["slug"]:
            errors.append(f"{fname}: slug={doc.get('slug')!r}, expected {meta['slug']!r}")
        if doc.get("title") != meta["title"]:
            errors.append(f"{fname}: title={doc.get('title')!r}, expected {meta['title']!r}")

        aliases_lower = {a.lower() for a in doc.get("aliases", [])}
        missing_aliases = meta["required_aliases"] - aliases_lower
        if missing_aliases:
            errors.append(f"{fname}: missing required aliases {sorted(missing_aliases)}")

        incidents = doc.get("incidents", [])
        if len(incidents) != 6:
            errors.append(f"{fname}: expected 6 incidents, got {len(incidents)}")

        scores = [inc.get("score", 0.0) for inc in incidents]
        if any(scores[i] <= scores[i + 1] for i in range(len(scores) - 1)):
            errors.append(f"{fname}: scores are not strictly descending: {scores}")

        tiers = [inc.get("tier") for inc in incidents]
        contested_or_higher = sum(1 for t in tiers if t in ("contested", "campaign"))
        has_heavy_or_campaign = any(
            inc.get("tier") == "campaign"
            or (inc.get("tier") == "contested" and len(inc.get("inserting_rev_ids", [])) >= 3)
            for inc in incidents
        )
        if not has_heavy_or_campaign:
            errors.append(f"{fname}: needs >=1 campaign or contested-heavy incident")
        if sum(1 for t in tiers if t == "contested") < 2:
            errors.append(f"{fname}: needs >=2 contested incidents, got {tiers}")
        if sum(1 for t in tiers if t == "reverted") < 1:
            errors.append(f"{fname}: needs >=1 reverted incident, got {tiers}")
        _ = contested_or_higher

        for idx, inc in enumerate(incidents, start=1):
            expected_id = f"{meta['id_prefix']}{idx:02d}"
            iid = inc.get("id")
            if iid != expected_id:
                errors.append(f"{fname}: incident #{idx} id={iid!r}, expected {expected_id!r}")

            # 1. Schema + eval contract check
            rep = contract.validate("incident", inc)
            errors.extend(f"{fname}:{iid}: {e}" for e in rep.errors)
            errors.extend(f"{fname}:{iid}: {w}" for w in rep.warnings)

            # 2. Timestamps, ordering, lengths
            for ts_key in ("first_inserted_at", "last_removed_at"):
                val = inc.get(ts_key)
                if not isinstance(val, str) or not ISO_UTC_RE.match(val):
                    errors.append(f"{iid}: {ts_key} must be YYYY-MM-DDTHH:MM:SSZ, got {val!r}")

            ins_ids = inc["inserting_rev_ids"]
            rem_ids = inc["removing_rev_ids"]
            if ins_ids != sorted(ins_ids):
                errors.append(f"{iid}: inserting_rev_ids not oldest-first: {ins_ids}")
            if rem_ids != sorted(rem_ids):
                errors.append(f"{iid}: removing_rev_ids not oldest-first: {rem_ids}")

            claim_en = inc.get("claim_en")
            if not claim_en or len(claim_en) > 140:
                errors.append(f"{iid}: claim_en missing or >140 chars ({len(claim_en or '')})")

            claim_fi = inc.get("claim_fi")
            if not claim_fi or len(claim_fi) > 200:
                errors.append(f"{iid}: claim_fi missing or >200 chars ({len(claim_fi or '')})")

            # 3. Signals & tier_reason
            for sig in inc.get("signals", []):
                if sig["kind"] not in SIGNAL_CATALOG_IDS:
                    errors.append(f"{iid}: unknown signal kind {sig['kind']!r}")

            expected_prefix = f"{TIER_LABELS[inc['tier']]} \u2014 because: "
            tr = inc.get("tier_reason") or ""
            if not tr.startswith(expected_prefix) or not tr.endswith("."):
                errors.append(f"{iid}: tier_reason {tr!r} must match '<EvidenceTiers label> — because: <reasons>.'")

            # 4. Edits sequence & exposure
            edits = inc.get("edits", [])
            edit_revs = [e["rev_id"] for e in edits]
            if edit_revs != sorted(edit_revs):
                errors.append(f"{iid}: edits not ordered chronologically: {edit_revs}")
            if set(edit_revs) != (set(ins_ids) | set(rem_ids)):
                errors.append(f"{iid}: edits rev_ids do not match inserting + removing rev_ids")
            for e in edits:
                if not ISO_UTC_RE.match(e.get("timestamp", "")):
                    errors.append(f"{iid}: edit {e['rev_id']} bad timestamp {e.get('timestamp')!r}")

            # 5. CopyRules scan on hand-written English prose
            prose_fields = [
                ("claim_en", inc.get("claim_en")),
                ("summary_en", inc.get("summary_en")),
                ("narrative", inc.get("narrative")),
                ("tier_reason", inc.get("tier_reason")),
                ("confidence_note", inc.get("confidence_note")),
            ]
            for s in inc.get("signals", []):
                prose_fields.append((f"signal[{s['kind']}].label", s.get("label")))
                prose_fields.append((f"signal[{s['kind']}].evidence", s.get("evidence")))
            if inc.get("assessment") is not None:
                a = inc["assessment"]
                prose_fields.extend([
                    ("assessment.what_changed_en", a.get("what_changed_en")),
                    ("assessment.uncertainty_note", a.get("uncertainty_note")),
                    ("assessment.historical_note_en", a.get("historical_note_en")),
                ])
            for fname_field, text in prose_fields:
                if not text:
                    continue
                for rule_kind, pat in COPY_RULE_PATTERNS:
                    m = pat.search(text)
                    if m:
                        errors.append(f"{iid}.{fname_field}: CopyRules {rule_kind} violation {m.group(0)!r}")

            all_incidents.append((fname, inc))

    # Cross-file coverage checks
    has_deletion = any((inc.get("claim_en") or "").startswith("Removed: ") for _, inc in all_incidents)
    has_null_assessment = any(
        inc.get("assessment") is None and inc.get("tier") == "reverted"
        for _, inc in all_incidents
    )
    has_number_change = any(
        any(s["kind"] == "number_changed" for s in inc.get("signals", []))
        for _, inc in all_incidents
    )
    has_citation_swap = any(
        any(s["kind"] == "source_change" for s in inc.get("signals", []))
        for _, inc in all_incidents
    )
    has_campaign = False
    for _, inc in all_incidents:
        if inc.get("tier") != "campaign" or len(inc.get("inserting_rev_ids", [])) < 3:
            continue
        ins_set = set(inc["inserting_rev_ids"])
        ins_users = {e["user_display"] for e in inc.get("edits", []) if e["rev_id"] in ins_set}
        sig_kinds = {s["kind"] for s in inc.get("signals", [])}
        if len(ins_users) >= 2 and "multiple_editors" in sig_kinds and ("persistence" in sig_kinds or "reinserted" in sig_kinds):
            has_campaign = True

    if not has_deletion:
        errors.append("coverage: missing deletion incident starting 'Removed: …'")
    if not has_citation_swap:
        errors.append("coverage: missing citation swap incident")
    if not has_number_change:
        errors.append("coverage: missing number change incident")
    if not has_campaign:
        errors.append("coverage: missing multi-edit campaign (>=3 inserting revs, >=2 accounts, tier=campaign)")
    if not has_null_assessment:
        errors.append("coverage: missing incident with assessment=null and tier=reverted")

    # Live corpus verification
    if verify_db and not errors:
        all_rev_pairs: set[tuple[int, int]] = set()
        ins_rev_pairs: set[tuple[int, int]] = set()
        parent_pairs_for_swap: set[tuple[int, int]] = set()

        for _, inc in all_incidents:
            pid = inc["page_id"]
            for r in inc["inserting_rev_ids"]:
                all_rev_pairs.add((pid, r))
                ins_rev_pairs.add((pid, r))
            for r in inc["removing_rev_ids"]:
                all_rev_pairs.add((pid, r))
            if any(s["kind"] == "source_change" for s in inc.get("signals", [])):
                first_edit = next(e for e in inc["edits"] if e["rev_id"] == inc["inserting_rev_ids"][0])
                if first_edit["parent_rev_id"] is not None:
                    parent_pairs_for_swap.add((pid, first_edit["parent_rev_id"]))

        rev_cond = " OR ".join(f"(page_id={p} AND rev_id={r})" for p, r in sorted(all_rev_pairs))
        rev_out = run_sql(
            f"SELECT page_id, rev_id, COALESCE(parent_id, -1), DATE_FORMAT(ts, '%Y-%m-%dT%H:%i:%SZ'), "
            f"COALESCE(HEX(user), '') FROM corpus.revision WHERE {rev_cond};"
        )
        db_revs: dict[tuple[int, int], dict] = {}
        for line in rev_out.splitlines()[1:]:
            p_s, r_s, par_s, ts_s, uhex = line.split("	")
            db_revs[(int(p_s), int(r_s))] = {
                "parent_rev_id": None if int(par_s) < 0 else int(par_s),
                "timestamp": ts_s,
                "user": bytes.fromhex(uhex).decode("utf-8") if uhex else None,
            }

        txt_fetch_pairs = sorted(ins_rev_pairs | parent_pairs_for_swap)
        assert len(txt_fetch_pairs) <= 100, f"too many revision_text PKs ({len(txt_fetch_pairs)})"
        txt_cond = " OR ".join(f"(page_id={p} AND rev_id={r})" for p, r in txt_fetch_pairs)
        txt_out = run_sql(f"SELECT page_id, rev_id, HEX(text) FROM corpus.revision_text WHERE {txt_cond};")
        db_texts: dict[tuple[int, int], str] = {}
        for line in txt_out.splitlines()[1:]:
            p_s, r_s, thex = line.split("	")
            db_texts[(int(p_s), int(r_s))] = bytes.fromhex(thex).decode("utf-8")

        verified_ref_swap = False
        for _, inc in all_incidents:
            iid = inc["id"]
            pid = inc["page_id"]
            for e in inc["edits"]:
                key = (e["page_id"], e["rev_id"])
                if key not in db_revs:
                    errors.append(f"{iid}: rev {key} does not exist in corpus.revision")
                    continue
                dbr = db_revs[key]
                if e["parent_rev_id"] != dbr["parent_rev_id"]:
                    errors.append(f"{iid}: rev {e['rev_id']} parent_rev_id={e['parent_rev_id']} != DB {dbr['parent_rev_id']}")
                if e["timestamp"] != dbr["timestamp"]:
                    errors.append(f"{iid}: rev {e['rev_id']} timestamp={e['timestamp']} != DB {dbr['timestamp']}")
                exp_disp, exp_anon, _ = mask_user(dbr["user"])
                if e["user_display"] != exp_disp or e["is_anonymous"] != exp_anon:
                    errors.append(f"{iid}: rev {e['rev_id']} user_display/anon mismatch ({e['user_display']!r} vs {exp_disp!r})")

            # Exposure check
            exp_sum = 0
            for ir, rr in zip(inc["inserting_rev_ids"], inc["removing_rev_ids"]):
                exp_sum += int((parse_ts(db_revs[(pid, rr)]["timestamp"]) - parse_ts(db_revs[(pid, ir)]["timestamp"])).total_seconds())
            if inc["total_exposure_seconds"] != exp_sum:
                errors.append(f"{iid}: total_exposure_seconds={inc['total_exposure_seconds']} != {exp_sum}")

            # Substring check in inserting revision wikitext
            for ir in inc["inserting_rev_ids"]:
                wt = db_texts.get((pid, ir), "")
                if inc["claim_fi"] not in wt:
                    errors.append(f"{iid}: claim_fi is not a substring of inserting rev {ir} wikitext")
                if inc.get("assessment") is not None:
                    for q in inc["assessment"]["quotes"]:
                        if q not in wt:
                            errors.append(f"{iid}: quote {q!r} is not a substring of inserting rev {ir} wikitext")

            # Verify <ref> swap in DB wikitext
            first_ins = inc["inserting_rev_ids"][0]
            first_par = db_revs[(pid, first_ins)]["parent_rev_id"]
            if (pid, first_par) in db_texts:
                old_refs = set(re.findall(r"<ref\b[^>]*>.*?</ref>|<ref\b[^/>]*/>", db_texts[(pid, first_par)], flags=re.I | re.S))
                new_refs = set(re.findall(r"<ref\b[^>]*>.*?</ref>|<ref\b[^/>]*/>", db_texts[(pid, first_ins)], flags=re.I | re.S))
                if (old_refs - new_refs) and (new_refs - old_refs):
                    verified_ref_swap = True

        if not verified_ref_swap:
            errors.append("DB coverage: no incident verified with both removed and added <ref> tags vs parent")

        print(
            f"DB check passed: {len(db_revs)} corpus.revision rows and "
            f"{len(db_texts)} corpus.revision_text PKs verified (ref-swap={verified_ref_swap})."
        )

    if errors:
        for err in errors:
            print(f"ERROR: {err}", file=sys.stderr)
        return 1

    print(f"OK: {len(all_incidents)} sample incidents across {len(EXPECTED_FILES)} fixture files passed all checks.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--no-db", action="store_true", help="skip live MariaDB corpus verification")
    args = ap.parse_args()
    return check_all(verify_db=not args.no_db)


if __name__ == "__main__":
    sys.exit(main())
