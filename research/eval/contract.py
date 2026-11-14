"""Eval contract v1 (#21) — the shapes in CONTRACT.md as code. Standard library only (Python 3.10+).

Structure comes from schemas/*.schema.json (the single source for field names and types), checked by a small
JSON Schema subset interpreter so nobody needs `pip install jsonschema`. Rules a schema can't express
(unique ids, split hygiene, label/exclusion pairing, …) are the `check_*` functions below.

The eval runner imports this module; variants and labellers use it through validate.py.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

CONTRACT = 1
SCHEMA_DIR = Path(__file__).resolve().parent / "schemas"

LEVELS = {"not-incident": 0, "reverted": 1, "contested": 2, "campaign": 3}
"""Evidence level per label (docs/evidence-standard.md §1). Overclaim = shown above the golden level."""

STRICT_RELEVANT = frozenset({"contested", "campaign"})
LENIENT_RELEVANT = frozenset({"reverted", "contested", "campaign"})

# shape name → (schema file, is JSON Lines)
SHAPES: dict[str, tuple[str, bool]] = {
    "golden": ("golden.schema.json", False),
    "retrieval-request": ("retrieval.request.schema.json", False),
    "retrieval-response": ("retrieval.response.schema.json", False),
    "signals-request": ("signals.request.schema.json", False),
    "signals-response": ("signals.response.schema.json", False),
    "pipeline-request": ("pipeline.request.schema.json", False),
    "pipeline-response": ("pipeline.response.schema.json", False),
    "incident": ("incident.schema.json", False),
    "judgements": ("judgement.schema.json", True),
    "reviews": ("review.schema.json", True),
}


@dataclass
class Report:
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors

    def extend(self, other: "Report") -> "Report":
        self.errors += other.errors
        self.warnings += other.warnings
        return self


def incident_key(inserting_rev_ids: list[int]) -> str:
    """Identity of a shown incident for judgements and reviews: inserting rev ids, ascending, joined with '+'."""
    return "+".join(str(r) for r in sorted(set(inserting_rev_ids)))


# ── JSON Schema subset ───────────────────────────────────────────────────────────────────────────────────────────────

_ANNOTATIONS = {"$schema", "$id", "$defs", "title", "description", "examples", "default"}
_KEYWORDS = {
    "$ref", "type", "enum", "const", "properties", "required", "additionalProperties", "items",
    "minItems", "maxItems", "uniqueItems", "minimum", "maximum", "minLength", "pattern",
}
_schema_cache: dict[str, dict] = {}


def load_schema(file_name: str) -> dict:
    if file_name not in _schema_cache:
        _schema_cache[file_name] = json.loads((SCHEMA_DIR / file_name).read_text(encoding="utf-8"))
    return _schema_cache[file_name]


def _type_ok(value: Any, name: str) -> bool:
    if name == "object":
        return isinstance(value, dict)
    if name == "array":
        return isinstance(value, list)
    if name == "string":
        return isinstance(value, str)
    if name == "boolean":
        return isinstance(value, bool)
    if name == "null":
        return value is None
    if name == "integer":
        return isinstance(value, int) and not isinstance(value, bool)
    if name == "number":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    raise ValueError(f"unsupported type '{name}' in schema")


def _same(a: Any, b: Any) -> bool:
    """JSON equality: true is not 1."""
    return type(a) is type(b) and a == b if isinstance(a, bool) or isinstance(b, bool) else a == b


def check_schema(value: Any, schema: dict, path: str = "$", root: dict | None = None) -> list[str]:
    """Errors (empty = valid) for `value` against the supported subset of JSON Schema 2020-12."""
    root = root if root is not None else schema
    unknown = set(schema) - _KEYWORDS - _ANNOTATIONS
    if unknown:
        raise ValueError(f"schema uses unsupported keyword(s) {sorted(unknown)} — extend contract.check_schema first")

    if "$ref" in schema:
        ref: str = schema["$ref"]
        file_name, _, pointer = ref.partition("#")
        target_root = load_schema(file_name) if file_name else root
        target = target_root
        for part in [p for p in pointer.split("/") if p]:
            target = target[part]
        return check_schema(value, target, path, target_root)

    errors: list[str] = []
    if "const" in schema and not _same(value, schema["const"]):
        return [f"{path}: must be {json.dumps(schema['const'])}, got {json.dumps(value, ensure_ascii=False)[:60]}"]
    if "enum" in schema and not any(_same(value, e) for e in schema["enum"]):
        return [f"{path}: must be one of {json.dumps(schema['enum'])}, got {json.dumps(value, ensure_ascii=False)[:60]}"]
    if "type" in schema:
        names = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if not any(_type_ok(value, n) for n in names):
            return [f"{path}: must be {' or '.join(names)}, got {type(value).__name__}"]

    if isinstance(value, dict):
        props: dict = schema.get("properties", {})
        for name in schema.get("required", []):
            if name not in value:
                errors.append(f"{path}: missing required field '{name}'")
        extra = schema.get("additionalProperties", True)
        for name, item in value.items():
            if name in props:
                errors += check_schema(item, props[name], f"{path}.{name}", root)
            elif extra is False:
                errors.append(f"{path}: unknown field '{name}'")
            elif isinstance(extra, dict):
                errors += check_schema(item, extra, f"{path}.{name}", root)
    elif isinstance(value, list):
        if len(value) < schema.get("minItems", 0):
            errors.append(f"{path}: needs at least {schema['minItems']} item(s), has {len(value)}")
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            errors.append(f"{path}: at most {schema['maxItems']} item(s), has {len(value)}")
        if schema.get("uniqueItems") and len({json.dumps(v, sort_keys=True) for v in value}) != len(value):
            errors.append(f"{path}: items must be unique")
        if "items" in schema:
            for i, item in enumerate(value):
                errors += check_schema(item, schema["items"], f"{path}[{i}]", root)
    elif isinstance(value, str):
        if len(value) < schema.get("minLength", 0):
            errors.append(f"{path}: must not be empty" if schema["minLength"] == 1 else f"{path}: shorter than {schema['minLength']}")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errors.append(f"{path}: '{value}' does not match {schema['pattern']}")
    elif isinstance(value, (int, float)) and not isinstance(value, bool):
        if "minimum" in schema and value < schema["minimum"]:
            errors.append(f"{path}: must be >= {schema['minimum']}, got {value}")
        if "maximum" in schema and value > schema["maximum"]:
            errors.append(f"{path}: must be <= {schema['maximum']}, got {value}")
    return errors


# ── Rules beyond the schema ──────────────────────────────────────────────────────────────────────────────────────────

def check_golden(doc: dict, final: bool = False) -> Report:
    """Golden-set rules. `final` = sign-off mode: unverified contested/campaign labels and missed size targets are errors."""
    rep = Report()
    soft = rep.errors if final else rep.warnings
    topic_ids: set[str] = set()
    query_ids: set[str] = set()
    item_ids: set[str] = set()
    inserting_owner: dict[int, str] = {}
    page_split: dict[int, tuple[str, str]] = {}   # pages that carry items → (split, topic id)

    for t in doc["topics"]:
        tid, where = t["id"], f"topic '{t['id']}'"
        if tid in topic_ids:
            rep.errors.append(f"{where}: duplicate topic id")
        topic_ids.add(tid)

        for q in t["queries"]:
            if not q["id"].startswith(tid + "."):
                rep.errors.append(f"{where}: query id '{q['id']}' must start with '{tid}.'")
            if q["id"] in query_ids:
                rep.errors.append(f"{where}: duplicate query id '{q['id']}'")
            query_ids.add(q["id"])
        styles = [q["style"] for q in t["queries"]]
        if len(styles) != len(set(styles)):
            rep.errors.append(f"{where}: one query per style, got {styles}")
        missing = [s for s in ("en", "fi", "broad") if s not in styles]
        if missing and t["kind"] != "control-out-of-corpus":
            rep.warnings.append(f"{where}: no query with style {missing} (three phrasings are expected: en, fi, broad)")

        pages = [a["page_id"] for a in t["articles"]]
        if len(pages) != len(set(pages)):
            rep.errors.append(f"{where}: an article is listed twice")
        if t["kind"] == "control-out-of-corpus":
            if t["articles"] or t["items"]:
                rep.errors.append(f"{where}: an out-of-corpus control has no articles and no items")
            continue
        if not any(a["grade"] == 2 for a in t["articles"]):
            rep.errors.append(f"{where}: needs at least one central article (grade 2)")

        for it in t["items"]:
            iw = f"item '{it['id']}'"
            if not it["id"].startswith(tid + "/"):
                rep.errors.append(f"{iw}: id must start with '{tid}/'")
            if it["id"] in item_ids:
                rep.errors.append(f"{iw}: duplicate item id")
            item_ids.add(it["id"])

            exclusion = it.get("exclusion")
            if it["label"] == "not-incident" and exclusion is None:
                rep.errors.append(f"{iw}: label not-incident needs an exclusion")
            if it["label"] != "not-incident":
                if exclusion is not None:
                    rep.errors.append(f"{iw}: exclusion '{exclusion}' is only for label not-incident")
                if not it["removing_rev_ids"]:
                    rep.errors.append(f"{iw}: label {it['label']} needs at least one removing rev id")
                if it["label"] in STRICT_RELEVANT and not it["human_verified"]:
                    soft.append(f"{iw}: {it['label']} is not human-verified yet")

            if it["page_id"] not in pages:
                rep.errors.append(f"{iw}: page_id {it['page_id']} is not one of the topic's articles")
            if it["page_id"] in it.get("other_page_ids", []):
                rep.errors.append(f"{iw}: other_page_ids repeats page_id")
            both = set(it["inserting_rev_ids"]) & set(it["removing_rev_ids"])
            if both:
                rep.errors.append(f"{iw}: rev id(s) {sorted(both)} are both inserting and removing")
            for rev in it["inserting_rev_ids"]:
                if rev in inserting_owner:
                    rep.errors.append(f"{iw}: inserting rev id {rev} also belongs to item '{inserting_owner[rev]}' — matching would be ambiguous")
                inserting_owner[rev] = it["id"]
            for page in [it["page_id"], *it.get("other_page_ids", [])]:
                seen = page_split.setdefault(page, (t["split"], tid))
                if seen[0] != t["split"]:
                    rep.errors.append(f"{iw}: page {page} has golden items in the {seen[0]} split (topic '{seen[1]}') and the {t['split']} split — the test split would leak")

    c = golden_counts(doc)
    targets = [
        (12 <= c["topics"] <= 15, f"{c['topics']} topics (target 12–15)"),
        (c["incidents"] >= 40, f"{c['incidents']} labelled incidents (target ~40)"),
        (c["non_incidents"] >= 40, f"{c['non_incidents']} labelled non-incidents (target ~40)"),
        (c["controls_quiet"] >= 1, f"{c['controls_quiet']} quiet control topic(s) (target 1)"),
        (c["controls_out_of_corpus"] >= 2, f"{c['controls_out_of_corpus']} out-of-corpus control topic(s) (target 2)"),
        (c["test_topics"] >= 4, f"{c['test_topics']} topic(s) in the test split (target 4)"),
    ]
    soft += [f"size: {text}" for met, text in targets if not met]
    return rep


def golden_counts(doc: dict) -> dict[str, int]:
    topics = [t for t in doc["topics"] if t["kind"] == "topic"]
    items = [it for t in doc["topics"] for it in t["items"]]
    by_label = {label: sum(1 for it in items if it["label"] == label) for label in LEVELS}
    return {
        "topics": len(topics),
        "test_topics": sum(1 for t in topics if t["split"] == "test"),
        "controls_quiet": sum(1 for t in doc["topics"] if t["kind"] == "control-quiet"),
        "controls_out_of_corpus": sum(1 for t in doc["topics"] if t["kind"] == "control-out-of-corpus"),
        "queries": sum(len(t["queries"]) for t in doc["topics"]),
        "articles": sum(len(t["articles"]) for t in doc["topics"]),
        "incidents": by_label["reverted"] + by_label["contested"] + by_label["campaign"],
        "non_incidents": by_label["not-incident"],
        **{f"label:{k}": v for k, v in by_label.items()},
        "unverified_contested_or_campaign": sum(1 for it in items if it["label"] in STRICT_RELEVANT and not it["human_verified"]),
    }


def _check_results(results: list[dict], request: dict | None, rep: Report) -> None:
    pages = [r["page_id"] for r in results]
    if len(pages) != len(set(pages)):
        rep.errors.append("results: a page_id appears more than once")
    if request and len(results) > request["k"]:
        rep.warnings.append(f"results: {len(results)} returned, k = {request['k']} — the runner scores only the first k")


def _check_incidents(incidents: list[dict], request: dict | None, rep: Report) -> None:
    seen: dict[str, int] = {}
    for i, inc in enumerate(incidents):
        where = f"incidents[{i}]"
        both = set(inc["inserting_rev_ids"]) & set(inc["removing_rev_ids"])
        if both:
            rep.errors.append(f"{where}: rev id(s) {sorted(both)} are both inserting and removing")
        if inc["page_id"] in inc.get("other_page_ids", []):
            rep.errors.append(f"{where}: other_page_ids repeats page_id")
        key = incident_key(inc["inserting_rev_ids"])
        if key in seen:
            rep.warnings.append(f"{where}: same inserting rev ids as incidents[{seen[key]}] — scored as a duplicate (false positive)")
        seen.setdefault(key, i)
        if request and "page_ids" in request and inc["page_id"] not in request["page_ids"]:
            rep.warnings.append(f"{where}: page_id {inc['page_id']} was not in the request's page_ids")
    if request and len(incidents) > request["limit"]:
        rep.warnings.append(f"incidents: {len(incidents)} returned, limit = {request['limit']} — the runner scores only the first {request['limit']}")


def validate(shape: str, value: Any, request: dict | None = None, final: bool = False) -> Report:
    """Validate one document of `shape` (a key of SHAPES; for JSON Lines shapes, one line).
    `request` = the request a response answers, for the cross-checks."""
    if shape not in SHAPES:
        raise ValueError(f"unknown shape '{shape}' — one of {sorted(SHAPES)}")
    rep = Report(errors=check_schema(value, load_schema(SHAPES[shape][0])))
    if not rep.ok:
        return rep
    if shape == "golden":
        rep.extend(check_golden(value, final=final))
    elif shape == "retrieval-response":
        _check_results(value["results"], request, rep)
    elif shape == "signals-response":
        _check_incidents(value["incidents"], request, rep)
    elif shape == "pipeline-response":
        _check_results(value["results"], request, rep)
        _check_incidents(value["incidents"], request, rep)
    elif shape == "incident":
        _check_incidents([value], None, rep)
    elif shape == "judgements":
        if value["label"] == "not-incident" and value.get("exclusion") is None:
            rep.errors.append("label not-incident needs an exclusion")
        if value["label"] != "not-incident" and value.get("exclusion") is not None:
            rep.errors.append(f"exclusion '{value['exclusion']}' is only for label not-incident")
    return rep


def load_golden(path: str | Path, final: bool = False) -> tuple[dict, Report]:
    doc = json.loads(Path(path).read_text(encoding="utf-8"))
    return doc, validate("golden", doc, final=final)


# ── Requests the runner sends (so every lane builds the same ones) ───────────────────────────────────────────────────

def topic_text(topic: dict) -> str:
    """The phrasing a signal variant receives: the precise English one, else the first."""
    return next((q["text"] for q in topic["queries"] if q["style"] == "en"), topic["queries"][0]["text"])


def retrieval_request(query: dict, k: int = 20) -> dict:
    return {"contract": CONTRACT, "kind": "retrieval", "query_id": query["id"], "text": query["text"], "k": k}


def signals_request(topic: dict, limit: int = 50) -> dict:
    return {
        "contract": CONTRACT, "kind": "signals", "topic_id": topic["id"], "text": topic_text(topic),
        "page_ids": [a["page_id"] for a in topic["articles"]], "limit": limit,
    }


def pipeline_request(query: dict, k: int = 20, limit: int = 50) -> dict:
    return {"contract": CONTRACT, "kind": "pipeline", "query_id": query["id"], "text": query["text"], "k": k, "limit": limit}
