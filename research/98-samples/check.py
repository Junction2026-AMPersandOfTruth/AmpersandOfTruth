#!/usr/bin/env python3
"""Validate P2 sample fixture files against incident.schema.json, C# contracts, golden.json dev split, and MariaDB corpus.

Standard library Python 3 only.

Usage:
  python3 research/98-samples/check.py            # schema + contracts + live MariaDB check
  python3 research/98-samples/check.py --no-db    # schema + contracts only (offline)
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
sys.path.insert(0, str(ROOT / "research"))

from eval.contract import validate  # noqa: E402

FIXTURES_DIR = ROOT / "src" / "AmpersandOfTruth.Web" / "Features" / "Investigations" / "Fixtures"
GOLDEN_PATH = ROOT / "research" / "eval" / "golden.json"
APPS_SETTINGS_PATH = ROOT / "src" / "AmpersandOfTruth.Web" / "appsettings.json"

EXPECTED_FILES = {
    "winter-war.json": {
        "slug": "winter-war",
        "title": "Winter War",
        "id_prefix": "ww-",
        "required_aliases": {"talvisota", "mainila shots", "mainilan laukaukset", "shelling of mainila"},
        "batch": 1,
    },
    "civil-war-1918.json": {
        "slug": "civil-war-1918",
        "title": "Civil War 1918",
        "id_prefix": "cw18-",
        "required_aliases": {"suomen sisällissota", "finnish civil war"},
        "batch": 1,
    },
    "continuation-war.json": {
        "slug": "continuation-war",
        "title": "Continuation War",
        "id_prefix": "cnt-",
        "required_aliases": {"jatkosota"},
        "batch": 1,
    },
    "ceded-karelia.json": {
        "slug": "ceded-karelia",
        "title": "Ceded Karelia",
        "id_prefix": "ck-",
        "required_aliases": {"luovutettu karjala", "karelia", "karjala"},
        "batch": 2,
    },
    "finlandization.json": {
        "slug": "finlandization",
        "title": "Finlandization",
        "id_prefix": "fin-",
        "required_aliases": {"suomettuminen"},
        "batch": 2,
    },
    "austerity-politics.json": {
        "slug": "austerity-politics",
        "title": "Austerity politics",
        "id_prefix": "aus-",
        "required_aliases": {"austerity", "leikkauspolitiikka"},
        "batch": 2,
    },
    "nato-debate.json": {
        "slug": "nato-debate",
        "title": "NATO debate",
        "id_prefix": "nato-",
        "required_aliases": {"nato", "suomen nato-keskustelu"},
        "batch": 2,
    },
    "mannerheim.json": {
        "slug": "mannerheim",
        "title": "Mannerheim",
        "id_prefix": "man-",
        "required_aliases": {"c. g. e. mannerheim", "carl gustaf emil mannerheim"},
        "batch": 2,
    },
}

CANONICAL_SIGNAL_IDS = {
    "removed_by_others",
    "removal_reason",
    "reinserted",
    "edit_war",
    "dispute_template",
    "claim_content",
    "multiple_editors",
    "persistence",
    "multiple_articles",
    "source_change",
    "anonymous",
    "number_changed",
    "citation_removed",
    "kept_in_latest",
}

TIER_PREFIXES = {
    "reverted": "Removed by Wikipedia editors \u2014 because: ",
    "contested": "Disputed and removed \u2014 because: ",
    "campaign": "Pattern consistent with a coordinated attempt \u2014 because: ",
}

# Exact regexes from src/AmpersandOfTruth.Web/Features/Evidence/CopyRules.cs + spec wording ban ("push", "propaganda")
COPY_RULES = [
    ("propagandist", re.compile(r"\bpropagandists?\b", re.I)),
    ("propaganda", re.compile(r"\bpropaganda\b", re.I)),
    ("push", re.compile(r"\bpush(?:ed|es|ing)?\b", re.I)),
    ("troll", re.compile(r"\btrolls?\b", re.I)),
    ("sockpuppet", re.compile(r"\bsock\s?puppets?\b", re.I)),
    ("bad actor", re.compile(r"\bbad actors?\b", re.I)),
    ("paid editor", re.compile(r"\bpaid editors?\b", re.I)),
    ("fake account", re.compile(r"\bfake accounts?\b", re.I)),
    ("bot farm", re.compile(r"\bbot farms?\b", re.I)),
    ("proven", re.compile(r"\bproven\b", re.I)),
    ("confirmed false", re.compile(r"\bconfirmed false\b", re.I)),
    ("deliberately", re.compile(r"\bdeliberately\b", re.I)),
    ("intentionally", re.compile(r"\bintentionally\b", re.I)),
    ("in bad faith", re.compile(r"\bin bad faith\b", re.I)),
    ("malicious", re.compile(r"\bmalicious(?:ly)?\b", re.I)),
    ("guilty", re.compile(r"\bguilty\b", re.I)),
    ("liar", re.compile(r"\bliars?\b", re.I)),
    ("tried to", re.compile(r"\btried to\b", re.I)),
    ("revert", re.compile(r"\brevert(?:ed|s|ing)?\b", re.I)),
    ("tier", re.compile(r"\btiers?\b", re.I)),
    ("llm", re.compile(r"\bllms?\b", re.I)),
    ("sha1", re.compile(r"\bsha[-_]?1\b", re.I)),
]

IPV4_STRICT = re.compile(r"^\d{1,3}(\.\d{1,3}){3}$")
TEMP_RE = re.compile(r"^~\d{4}-\d+-\d+$")
UNMASKED_IPV4 = re.compile(r"(?<![0-9.])(\d{1,3}(?:\.\d{1,3}){3})(?![0-9.])")
UNMASKED_IPV6 = re.compile(r"(?:[0-9A-Fa-f]{1,4}:){2,7}[0-9A-Fa-f]{1,4}")
SLUG_NON_ALNUM = re.compile(r"[^a-z0-9]+")

REF_CHANGE_IDS = {"cw18-02", "aus-03", "man-02"}


def topic_slug(raw: str) -> str:
    lower = raw.strip().lower()
    folded = lower.replace("ä", "a").replace("ö", "o").replace("å", "a")
    return SLUG_NON_ALNUM.sub("-", folded).strip("-")


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


def has_unmasked_ip(text: str) -> bool:
    for m in UNMASKED_IPV4.finditer(text):
        try:
            ipaddress.IPv4Address(m.group(1))
            return True
        except ValueError:
            pass
    for m in UNMASKED_IPV6.finditer(text):
        try:
            ipaddress.IPv6Address(m.group(0))
            return True
        except ValueError:
            pass
    return False


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


def load_golden_index() -> tuple[dict[tuple[int, tuple[int, ...]], dict], set[tuple[int, tuple[int, ...]]]]:
    golden = json.loads(GOLDEN_PATH.read_text(encoding="utf-8"))
    dev_map: dict[tuple[int, tuple[int, ...]], dict] = {}
    test_keys: set[tuple[int, tuple[int, ...]]] = set()
    for t in golden["topics"]:
        for it in t["items"]:
            key = (it["page_id"], tuple(sorted(it["inserting_rev_ids"])))
            if t["split"] == "dev":
                dev_map[key] = it
            else:
                test_keys.add(key)
    return dev_map, test_keys


def check_all(verify_db: bool = True) -> int:
    dev_golden, test_golden = load_golden_index()
    errors: list[str] = []

    actual_files = {p.name for p in FIXTURES_DIR.glob("*.json")}
    if actual_files != set(EXPECTED_FILES):
        errors.append(f"Fixture files mismatch: got {sorted(actual_files)}, expected {sorted(EXPECTED_FILES)}")

    seen_incident_ids: set[str] = set()
    seen_ins_revs: set[int] = set()
    slug_index: dict[str, str] = {}

    total_null_assessment = 0
    total_anon = 0
    total_named = 0
    total_deletions = 0
    total_source_swaps = 0

    all_incidents: list[tuple[str, dict]] = []

    for fname, spec in EXPECTED_FILES.items():
        fpath = FIXTURES_DIR / fname
        if not fpath.exists():
            errors.append(f"Missing fixture file: {fpath}")
            continue

        doc = json.loads(fpath.read_text(encoding="utf-8"))
        if doc.get("slug") != spec["slug"]:
            errors.append(f"{fname}: slug={doc.get('slug')!r}, expected {spec['slug']!r}")
        if doc.get("title") != spec["title"]:
            errors.append(f"{fname}: title={doc.get('title')!r}, expected {spec['title']!r}")

        aliases = doc.get("aliases")
        if not isinstance(aliases, list) or not all(isinstance(a, str) and a.strip() for a in aliases):
            errors.append(f"{fname}: aliases must be non-empty strings")
        else:
            lower_aliases = {a.lower() for a in aliases}
            missing_a = spec["required_aliases"] - lower_aliases
            if missing_a:
                errors.append(f"{fname}: missing required aliases {missing_a}")
            for key_raw in [doc["slug"], doc["title"], *aliases]:
                s = topic_slug(key_raw)
                if s and s not in slug_index:
                    slug_index[s] = doc["slug"]

        incidents = doc.get("incidents")
        expected_counts = (6,) if spec["batch"] == 1 else (5, 6)
        if not isinstance(incidents, list) or len(incidents) not in expected_counts:
            errors.append(
                f"{fname}: expected {expected_counts} incidents, got {len(incidents) if isinstance(incidents, list) else type(incidents)}"
            )
            continue

        scores = [inc.get("score", 0) for inc in incidents]
        if scores != sorted(scores, reverse=True) or len(set(scores)) != len(scores):
            errors.append(f"{fname}: scores must be strictly descending: {scores}")

        tiers = [inc.get("tier") for inc in incidents]
        has_campaign_or_reinserted_contested = any(
            inc.get("tier") == "campaign"
            or (inc.get("tier") == "contested" and (inc.get("reinsertion_count") or 0) >= 1)
            for inc in incidents
        )
        if not has_campaign_or_reinserted_contested:
            errors.append(f"{fname}: needs >=1 campaign or re-inserted contested incident")
        if tiers.count("contested") < 2:
            errors.append(f"{fname}: needs >=2 contested incidents, got {tiers}")
        if tiers.count("reverted") < 1:
            errors.append(f"{fname}: needs >=1 reverted incident, got {tiers}")

        file_null_assessments = sum(1 for inc in incidents if inc.get("assessment") is None)
        if spec["batch"] == 2 and file_null_assessments != 1:
            errors.append(f"{fname}: expected exactly 1 incident with assessment=null, got {file_null_assessments}")

        for idx, inc in enumerate(incidents):
            rep = validate("incident", inc)
            for se in rep.errors:
                errors.append(f"{fname}.incidents[{idx}]: {se}")
            iid = inc.get("id", "")
            if not iid.startswith(spec["id_prefix"]):
                errors.append(f"{fname}: incident id {iid!r} must start with {spec['id_prefix']!r}")
            if iid in seen_incident_ids:
                errors.append(f"{fname}: duplicate incident id {iid!r}")
            seen_incident_ids.add(iid)

            ins_key = (inc["page_id"], tuple(sorted(inc["inserting_rev_ids"])))
            if ins_key in test_golden:
                errors.append(f"{iid}: FORBIDDEN test-split golden item used {ins_key}")
            if spec["batch"] == 2:
                if ins_key not in dev_golden:
                    errors.append(f"{iid}: batch-2 incident {ins_key} not found in golden.json dev split")
                else:
                    git = dev_golden[ins_key]
                    if sorted(inc["removing_rev_ids"]) != sorted(git["removing_rev_ids"]):
                        errors.append(f"{iid}: removing_rev_ids mismatch vs golden item {git['id']}")
                    if inc["tier"] != git["label"]:
                        errors.append(f"{iid}: tier={inc['tier']!r} != golden label {git['label']!r} ({git['id']})")

            for rev in inc["inserting_rev_ids"]:
                if rev in seen_ins_revs:
                    errors.append(f"{fname}: duplicate inserting_rev_id {rev}")
                seen_ins_revs.add(rev)

            if len(inc["claim_en"]) > 140:
                errors.append(f"{iid}: claim_en exceeds 140 chars ({len(inc['claim_en'])})")

            # Tier reason check
            tier = inc["tier"]
            tr = inc.get("tier_reason") or ""
            if not tr.startswith(TIER_PREFIXES[tier]) or not tr.endswith("."):
                errors.append(f"{iid}: tier_reason {tr!r} must start with {TIER_PREFIXES[tier]!r} and end with '.'")

            # Signals check
            for sig in inc.get("signals", []):
                if sig["kind"] not in CANONICAL_SIGNAL_IDS:
                    errors.append(f"{iid}: non-canonical signal kind {sig['kind']!r}")

            # Edits ordering, roles, masking
            edits = inc.get("edits", [])
            edit_revs = {e["rev_id"]: e for e in edits}
            for r in inc["inserting_rev_ids"] + inc["removing_rev_ids"]:
                if r not in edit_revs:
                    errors.append(f"{iid}: rev {r} missing from edits[]")

            timestamps = [parse_ts(e["timestamp"]) for e in edits]
            if timestamps != sorted(timestamps):
                errors.append(f"{iid}: edits[] not sorted chronologically")

            for e in edits:
                for field_name in ("user_display", "summary"):
                    val = e.get(field_name)
                    if val and has_unmasked_ip(val):
                        errors.append(f"{iid}: unmasked IP in edits.{field_name}: {val!r}")
                if e["role"] != "removed":
                    if e["is_anonymous"]:
                        total_anon += 1
                    else:
                        total_named += 1

            # CopyRules scan on English prose fields
            prose_fields: list[tuple[str, str]] = []
            for k in ("claim_en", "summary_en", "narrative", "tier_reason", "confidence_note"):
                if inc.get(k):
                    prose_fields.append((k, inc[k]))
            for s_i, sig in enumerate(inc.get("signals", [])):
                if sig.get("label"):
                    prose_fields.append((f"signals[{s_i}].label", sig["label"]))
                if sig.get("evidence"):
                    prose_fields.append((f"signals[{s_i}].evidence", sig["evidence"]))
            asm = inc.get("assessment")
            if asm is None:
                total_null_assessment += 1
                if tier != "reverted":
                    errors.append(f"{iid}: null assessment must stay at tier 'reverted', got {tier!r}")
            else:
                for k in ("what_changed_en", "uncertainty_note", "historical_note_en"):
                    if asm.get(k):
                        prose_fields.append((f"assessment.{k}", asm[k]))

            for field_label, text in prose_fields:
                for rule_name, pat in COPY_RULES:
                    m = pat.search(text)
                    if m:
                        errors.append(
                            f"{iid}.{field_label}: CopyRules violation ({rule_name!r} matched {m.group(0)!r}) in {text!r}"
                        )
                if has_unmasked_ip(text):
                    errors.append(f"{iid}.{field_label}: unmasked IP in {text!r}")

            if inc["claim_en"].startswith("Removed: "):
                total_deletions += 1
            if any(s["kind"] == "source_change" for s in inc.get("signals", [])):
                total_source_swaps += 1

            all_incidents.append((fname, inc))

    # Landing chips check against appsettings.json Search:SuggestedTopics
    appsettings = json.loads(APPS_SETTINGS_PATH.read_text(encoding="utf-8"))
    chips = appsettings.get("Search", {}).get("SuggestedTopics", [])
    if not chips:
        errors.append("No Search:SuggestedTopics found in appsettings.json")
    for chip in chips:
        cslug = topic_slug(chip["Topic"])
        if cslug not in slug_index:
            errors.append(f"SuggestedTopic {chip['Topic']!r} (slug {cslug!r}) does not match any sample topic")

    if total_null_assessment < 6:
        errors.append(f"Expected >=6 incidents with assessment=null across all files, got {total_null_assessment}")
    if total_anon < 2 or total_named < 2:
        errors.append(f"Expected mix of anonymous ({total_anon}) and named ({total_named}) inserting edits")
    if total_deletions < 1:
        errors.append("Expected >=1 deletion incident starting with 'Removed: '")
    if total_source_swaps < 2:
        errors.append("Expected >=2 source_change incidents")

    if verify_db and not errors:
        # 1. Verify article titles
        page_ids = sorted({a["page_id"] for _, inc in all_incidents for a in inc["articles"]})
        pages_csv = ", ".join(str(p) for p in page_ids)
        art_out = run_sql(
            f"SELECT fi.page_id, HEX(fi.title), COALESCE(HEX(en.title), '') "
            f"FROM article fi "
            f"LEFT JOIN article en ON fi.wikidata_id IS NOT NULL AND fi.wikidata_id <> '' "
            f"AND en.wikidata_id = fi.wikidata_id AND en.lang = 'en' "
            f"WHERE fi.lang = 'fi' AND fi.page_id IN ({pages_csv}) "
            f"ORDER BY fi.page_id, en.page_id;"
        )
        db_articles: dict[int, tuple[str, str | None]] = {}
        for line in art_out.splitlines()[1:]:
            p_s, fi_h, en_h = line.split(chr(9))
            pid = int(p_s)
            if pid not in db_articles:
                db_articles[pid] = (
                    bytes.fromhex(fi_h).decode("utf-8"),
                    bytes.fromhex(en_h).decode("utf-8") if en_h else None,
                )

        # 2. Verify corpus.revision rows by PK (batched, one query at a time)
        rev_pairs_list = sorted({(e["page_id"], e["rev_id"]) for _, inc in all_incidents for e in inc["edits"]})
        db_revs: dict[tuple[int, int], dict] = {}
        for i in range(0, len(rev_pairs_list), 60):
            chunk = rev_pairs_list[i : i + 60]
            where_rev = " OR ".join(f"(page_id={p} AND rev_id={r})" for p, r in chunk)
            rev_out = run_sql(
                f"SELECT page_id, rev_id, COALESCE(parent_id, -1), DATE_FORMAT(ts, '%Y-%m-%dT%H:%i:%SZ'), "
                f"COALESCE(HEX(user), ''), COALESCE(HEX(comment), '') "
                f"FROM corpus.revision WHERE {where_rev};"
            )
            for line in rev_out.splitlines()[1:]:
                parts = line.split(chr(9))
                p_s, r_s, par_s, ts_s, u_hex, c_hex = parts
                u = bytes.fromhex(u_hex).decode("utf-8") if u_hex else None
                c = bytes.fromhex(c_hex).decode("utf-8") if c_hex else None
                db_revs[(int(p_s), int(r_s))] = {
                    "parent_rev_id": None if int(par_s) < 0 else int(par_s),
                    "timestamp": ts_s,
                    "user": u,
                    "comment": c,
                }

        # 3. Verify corpus.revision_text by PK only (batched, one query at a time)
        txt_fetch_pairs: set[tuple[int, int]] = set()
        for _, inc in all_incidents:
            pid = inc["page_id"]
            for ir in inc["inserting_rev_ids"]:
                txt_fetch_pairs.add((pid, ir))
            if inc["id"] in REF_CHANGE_IDS:
                first_ins = inc["inserting_rev_ids"][0]
                par = db_revs[(pid, first_ins)]["parent_rev_id"]
                if par is not None:
                    txt_fetch_pairs.add((pid, par))

        txt_list = sorted(txt_fetch_pairs)
        db_texts: dict[tuple[int, int], str] = {}
        for i in range(0, len(txt_list), 40):
            chunk = txt_list[i : i + 40]
            where_txt = " OR ".join(f"(page_id={p} AND rev_id={r})" for p, r in chunk)
            txt_out = run_sql(f"SELECT page_id, rev_id, HEX(text) FROM corpus.revision_text WHERE {where_txt};")
            for line in txt_out.splitlines()[1:]:
                p_s, r_s, t_hex = line.split(chr(9))
                db_texts[(int(p_s), int(r_s))] = bytes.fromhex(t_hex).decode("utf-8")

        for _, inc in all_incidents:
            iid = inc["id"]
            pid = inc["page_id"]
            for a in inc["articles"]:
                exp_art = db_articles.get(a["page_id"])
                if not exp_art:
                    errors.append(f"{iid}: article {a['page_id']} not found in corpus.article")
                else:
                    if a["title_fi"] != exp_art[0] or a.get("title_en") != exp_art[1]:
                        errors.append(
                            f"{iid}: article {a['page_id']} titles {(a['title_fi'], a.get('title_en'))!r} != {exp_art!r}"
                        )

            for e in inc["edits"]:
                key = (e["page_id"], e["rev_id"])
                if key not in db_revs:
                    errors.append(f"{iid}: rev {key} not found in corpus.revision")
                    continue
                dbr = db_revs[key]
                if e["parent_rev_id"] != dbr["parent_rev_id"]:
                    errors.append(
                        f"{iid}: rev {e['rev_id']} parent_rev_id={e['parent_rev_id']} != {dbr['parent_rev_id']}"
                    )
                if e["timestamp"] != dbr["timestamp"]:
                    errors.append(f"{iid}: rev {e['rev_id']} timestamp={e['timestamp']} != {dbr['timestamp']}")
                exp_disp, exp_anon, _ = mask_user(dbr["user"])
                if e["user_display"] != exp_disp or e["is_anonymous"] != exp_anon:
                    errors.append(
                        f"{iid}: rev {e['rev_id']} user_display/anon mismatch ({e['user_display']!r} vs {exp_disp!r})"
                    )

            ins_sorted = sorted(inc["inserting_rev_ids"])
            rem_sorted = sorted(inc["removing_rev_ids"])
            if inc["first_inserted_at"] != db_revs[(pid, ins_sorted[0])]["timestamp"]:
                errors.append(f"{iid}: first_inserted_at mismatch")
            if inc["last_removed_at"] != db_revs[(pid, rem_sorted[-1])]["timestamp"]:
                errors.append(f"{iid}: last_removed_at mismatch")

            # Live-window exposure check (matches zip(ins, rem) for alternating IR...IR and handles IIIR / IRIIR / RIR)
            all_ev = sorted(
                [(r, "I") for r in ins_sorted] + [(r, "R") for r in rem_sorted],
                key=lambda x: (db_revs[(pid, x[0])]["timestamp"], x[0]),
            )
            live_start: datetime.datetime | None = None
            exp_sum = 0
            for r, kind in all_ev:
                ts_dt = parse_ts(db_revs[(pid, r)]["timestamp"])
                if kind == "R":
                    if live_start is not None:
                        exp_sum += max(0, int((ts_dt - live_start).total_seconds()))
                        live_start = None
                else:
                    if live_start is None:
                        live_start = ts_dt
            if inc["total_exposure_seconds"] != exp_sum:
                errors.append(f"{iid}: total_exposure_seconds={inc['total_exposure_seconds']} != {exp_sum}")

            # Verbatim substring check in first inserting revision wikitext (the diffed revision)
            first_ins = ins_sorted[0]
            wt_first = db_texts.get((pid, first_ins), "")
            if inc["claim_fi"] not in wt_first:
                errors.append(f"{iid}: claim_fi is not a substring of first inserting rev {first_ins} wikitext")
            if inc.get("assessment") is not None:
                for q in inc["assessment"]["quotes"]:
                    if q not in wt_first:
                        errors.append(
                            f"{iid}: quote {q!r} is not a substring of first inserting rev {first_ins} wikitext"
                        )

        # Verify ref changes on REF_CHANGE_IDS
        for ref_id in sorted(REF_CHANGE_IDS):
            inc_obj = next(inc for _, inc in all_incidents if inc["id"] == ref_id)
            pid = inc_obj["page_id"]
            ir = sorted(inc_obj["inserting_rev_ids"])[0]
            par = db_revs[(pid, ir)]["parent_rev_id"]
            old_refs = set(re.findall(r"<ref\b[^>]*>.*?</ref>", db_texts[(pid, par)], flags=re.I | re.S))
            new_refs = set(re.findall(r"<ref\b[^>]*>.*?</ref>", db_texts[(pid, ir)], flags=re.I | re.S))
            if old_refs == new_refs:
                errors.append(f"{ref_id}: expected <ref> changes between parent {par} and {ir}")

        if not errors:
            print(
                f"DB check passed: {len(db_articles)} articles, {len(db_revs)} corpus.revision rows, and {len(db_texts)} corpus.revision_text PKs verified."
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
