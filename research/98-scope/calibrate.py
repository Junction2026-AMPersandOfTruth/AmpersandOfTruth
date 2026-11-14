#!/usr/bin/env python3
"""Calibrate EmbeddingScopeCheck thresholds (issue #44 / P2 #98).

Computes top-1 and mean-top-5 bge-m3 cosine distances via the corpus HNSW index
(`article_vector`, matching `IQueryEmbedder` + `IInvestigationCorpus.NearestArticlesAsync(vector, 5)`)
sequentially one query at a time across:
  (a) in-scope queries (67 total):
      - research/eval/golden.json (all splits, kind in {'topic', 'control-quiet'}, all query styles: 56)
      - src/AmpersandOfTruth.Web/appsettings.json Search:SuggestedTopics (7)
      - scripts/demo-topics.txt (3) + lowercase 'talvisota' acceptance check (1)
  (b) out-of-scope queries (36 total):
      - research/eval/golden.json (all splits, kind == 'control-out-of-corpus': 6)
      - 30 multilingual (EN / FI / SV) out-of-scope queries

Reports both:
  1. Raw query embeddings (showing bge-m3 short-phrase and initial-lowercase anisotropy).
  2. EmbeddingScopeCheck's calibrated rule: initial-character uppercase normalization +
     configured SuggestedTopics / top-5 exact-or-multiword-prefix title match +
     (MaxTopDistance = 0.495, MaxMeanDistance = 0.515).

Usage:
  python3 research/98-scope/calibrate.py [--json /tmp/scope-measurements.json]
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import unicodedata

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "research" / "lib"))
import aot  # noqa: E402

DEFAULT_MAX_TOP_DISTANCE = 0.495
DEFAULT_MAX_MEAN_DISTANCE = 0.515

CUSTOM_OUT_OF_SCOPE: list[tuple[str, str, str]] = [
    # English (14)
    ("oos.climate-change.en", "en", "climate change"),
    ("oos.climate-change-cap.en", "en", "Climate change"),
    ("oos.taylor-swift.en", "en", "Taylor Swift"),
    ("oos.covid-brazil.en", "en", "COVID vaccines in Brazil"),
    ("oos.roman-empire.en", "en", "Roman Empire"),
    ("oos.bitcoin.en", "en", "Bitcoin"),
    ("oos.world-cup-2022.en", "en", "World Cup 2022"),
    ("oos.donald-trump.en", "en", "Donald Trump"),
    ("oos.python-prog.en", "en", "Python programming"),
    ("oos.us-civil-war.en", "en", "American Civil War"),
    ("oos.brexit.en", "en", "Brexit referendum"),
    ("oos.kpop.en", "en", "K-pop fandom culture in South Korea"),
    ("oos.quantum.en", "en", "Quantum computing"),
    ("oos.french-election.en", "en", "2022 French presidential election"),
    # Finnish (9)
    ("oos.climate-change.fi", "fi", "ilmastonmuutos"),
    ("oos.roman-empire.fi", "fi", "Rooman valtakunta"),
    ("oos.us-civil-war.fi", "fi", "Yhdysvaltain sisällissota"),
    ("oos.french-rev.fi", "fi", "Ranskan vallankumous"),
    ("oos.world-cup-2022.fi", "fi", "Jalkapallon maailmanmestaruuskilpailut 2022"),
    ("oos.covid-brazil.fi", "fi", "koronarokotteet Brasiliassa"),
    ("oos.python-prog.fi", "fi", "Python-ohjelmointi"),
    ("oos.taylor-swift.fi", "fi", "Taylor Swiftin diskografia"),
    ("oos.bitcoin.fi", "fi", "Bitcoin ja kryptovaluutat"),
    # Swedish (7)
    ("oos.climate-change.sv", "sv", "klimatförändringar"),
    ("oos.roman-empire.sv", "sv", "Romerska riket"),
    ("oos.french-rev.sv", "sv", "Franska revolutionen"),
    ("oos.us-civil-war.sv", "sv", "Amerikanska inbördeskriget"),
    ("oos.world-cup-2022.sv", "sv", "Fotbolls-VM 2022 i Qatar"),
    ("oos.python-prog.sv", "sv", "Python-programmering"),
    ("oos.covid-brazil.sv", "sv", "coronavaccin i Brasilien"),
]


def normalize_whitespace(text: str) -> str:
    return " ".join(unicodedata.normalize("NFC", text).strip().split())


def normalize_for_embedding(text: str) -> str:
    norm = normalize_whitespace(text)
    if not norm:
        return norm
    return norm[0].upper() + norm[1:]


def title_matches(normalized_topic: str, hits: list[dict]) -> bool:
    if len(normalized_topic) < 4:
        return False
    ql = normalized_topic.lower()
    multi_word = " " in ql
    for h in hits:
        tl = normalize_whitespace(h["title"]).lower()
        if tl == ql:
            return True
        if multi_word and (tl.startswith(ql + " ") or tl.startswith(ql + " (")):
            return True
    return False


def load_queries() -> tuple[list[dict], list[dict], set[str]]:
    in_scope: list[dict] = []
    out_of_scope: list[dict] = []

    golden = json.loads((ROOT / "research" / "eval" / "golden.json").read_text(encoding="utf-8"))
    for topic in golden["topics"]:
        kind = topic["kind"]
        split = topic["split"]
        for q in topic["queries"]:
            entry = {
                "id": q["id"],
                "source": f"golden:{kind}:{split}",
                "style": q["style"],
                "lang": q["lang"],
                "text": q["text"],
            }
            if kind in ("topic", "control-quiet"):
                in_scope.append(entry)
            elif kind == "control-out-of-corpus":
                out_of_scope.append(entry)
            else:
                raise ValueError(f"Unexpected golden topic kind: {kind}")

    appsettings = json.loads((ROOT / "src" / "AmpersandOfTruth.Web" / "appsettings.json").read_text(encoding="utf-8"))
    suggested_set: set[str] = set()
    for item in appsettings["Search"]["SuggestedTopics"]:
        topic_text = item["Topic"]
        suggested_set.add(normalize_whitespace(topic_text).lower())
        suggested_set.add(normalize_whitespace(item["Label"]).lower())
        in_scope.append({
            "id": f"suggested:{topic_text}",
            "source": "appsettings:SuggestedTopics",
            "style": "chip",
            "lang": "en",
            "text": topic_text,
        })

    demo_lines = (ROOT / "scripts" / "demo-topics.txt").read_text(encoding="utf-8").splitlines()
    for line in demo_lines:
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        in_scope.append({
            "id": f"demo:{s}",
            "source": "scripts/demo-topics.txt",
            "style": "demo",
            "lang": "fi",
            "text": s,
        })

    in_scope.append({
        "id": "demo:talvisota-lower",
        "source": "acceptance:talvisota",
        "style": "demo",
        "lang": "fi",
        "text": "talvisota",
    })

    for qid, lang, text in CUSTOM_OUT_OF_SCOPE:
        out_of_scope.append({
            "id": qid,
            "source": "custom-oos",
            "style": lang,
            "lang": lang,
            "text": text,
        })

    return in_scope, out_of_scope, suggested_set


def batch_nearest5(vectors: list[list[float]], chunk_size: int = 15) -> list[list[dict]]:
    results: list[list[dict]] = [[] for _ in vectors]
    for start in range(0, len(vectors), chunk_size):
        chunk = vectors[start:start + chunk_size]
        parts = []
        for offset, vec in enumerate(chunk):
            qidx = start + offset
            lit = aot.vec_literal(vec)
            parts.append(
                f"(SELECT {qidx} AS qidx, a.lang, a.page_id, a.title, v.distance "
                f"FROM (SELECT lang, page_id, VEC_DISTANCE_COSINE(embedding, {lit}) AS distance "
                f"FROM article_vector ORDER BY distance LIMIT 5) v "
                f"JOIN article a USING (lang, page_id) ORDER BY v.distance)"
            )
        rows = aot.sql(" UNION ALL ".join(parts))
        for r in rows:
            idx = int(r["qidx"])
            results[idx].append({
                "lang": r["lang"],
                "page_id": int(r["page_id"]),
                "title": r["title"],
                "distance": float(r["distance"]),
            })
        for idx in range(start, start + len(chunk)):
            results[idx].sort(key=lambda x: x["distance"])
        aot.log(f"  queried {min(start + chunk_size, len(vectors))}/{len(vectors)} top-5 vector lookups")
    return results


def measure_all(entries: list[dict], expected_in_scope: bool, suggested_set: set[str]) -> list[dict]:
    raw_norms = [normalize_whitespace(e["text"]) for e in entries]
    emb_inputs = [normalize_for_embedding(e["text"]) for e in entries]

    unique_texts = list(dict.fromkeys(raw_norms + emb_inputs))
    vec_by_text: dict[str, list[float]] = {}
    for t in unique_texts:
        vec_by_text[t] = aot.embed(t)

    hits_list = batch_nearest5([vec_by_text[t] for t in unique_texts])
    hits_by_text = dict(zip(unique_texts, hits_list))

    out: list[dict] = []
    for entry, raw_norm, emb_input in zip(entries, raw_norms, emb_inputs):
        raw_hits = hits_by_text[raw_norm]
        hits = hits_by_text[emb_input]
        raw_top1 = raw_hits[0]["distance"]
        raw_mean5 = sum(h["distance"] for h in raw_hits) / len(raw_hits)
        top1 = hits[0]["distance"]
        mean5 = sum(h["distance"] for h in hits) / len(hits)
        is_chip = raw_norm.lower() in suggested_set
        is_title = title_matches(raw_norm, hits)
        out.append({
            **entry,
            "expected_in_scope": expected_in_scope,
            "raw_top1": round(raw_top1, 6),
            "raw_mean5": round(raw_mean5, 6),
            "top1": round(top1, 6),
            "mean5": round(mean5, 6),
            "is_suggested_chip": is_chip,
            "is_title_match": is_title,
            "top5": [
                {"lang": h["lang"], "page_id": h["page_id"], "title": h["title"], "distance": round(h["distance"], 6)}
                for h in hits
            ],
        })
    return out


def decide(row: dict, max_top: float, max_mean: float) -> bool:
    if row["is_suggested_chip"] or row["is_title_match"]:
        return True
    return row["top1"] <= max_top and row["mean5"] <= max_mean


def evaluate_confusion(in_rows: list[dict], oos_rows: list[dict], max_top: float, max_mean: float) -> dict:
    tp_in = sum(1 for r in in_rows if decide(r, max_top, max_mean))
    fn_in = len(in_rows) - tp_in
    tn_oos = sum(1 for r in oos_rows if not decide(r, max_top, max_mean))
    fp_oos = len(oos_rows) - tn_oos
    return {
        "max_top_distance": max_top,
        "max_mean_distance": max_mean,
        "in_scope_kept": tp_in,
        "in_scope_false_rejects": fn_in,
        "in_scope_recall": round(tp_in / len(in_rows), 4),
        "out_of_scope_rejected": tn_oos,
        "out_of_scope_false_accepts": fp_oos,
        "out_of_scope_rejection_rate": round(tn_oos / len(oos_rows), 4),
        "false_accept_queries": [r["text"] for r in oos_rows if decide(r, max_top, max_mean)],
        "false_reject_queries": [r["text"] for r in in_rows if not decide(r, max_top, max_mean)],
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--json", type=pathlib.Path, default=None, help="write raw measurements JSON")
    ap.add_argument("--max-top", type=float, default=DEFAULT_MAX_TOP_DISTANCE)
    ap.add_argument("--max-mean", type=float, default=DEFAULT_MAX_MEAN_DISTANCE)
    args = ap.parse_args()

    in_q, oos_q, suggested_set = load_queries()
    aot.log(f"Calibrating {len(in_q)} in-scope and {len(oos_q)} out-of-scope queries...")

    in_rows = measure_all(in_q, True, suggested_set)
    oos_rows = measure_all(oos_q, False, suggested_set)

    max_raw_in_top1 = max(r["raw_top1"] for r in in_rows)
    raw_oos_caught_at_100 = sum(1 for r in oos_rows if r["raw_top1"] > max_raw_in_top1)

    non_exact_in = [r for r in in_rows if not r["is_suggested_chip"] and not r["is_title_match"]]
    calibrated = evaluate_confusion(in_rows, oos_rows, args.max_top, args.max_mean)

    summary = {
        "in_scope_count": len(in_rows),
        "out_of_scope_count": len(oos_rows),
        "raw_baseline": {
            "max_in_top1": max_raw_in_top1,
            "max_in_mean5": max(r["raw_mean5"] for r in in_rows),
            "min_oos_top1": min(r["raw_top1"] for r in oos_rows),
            "oos_rejected_at_100pct_in_scope": raw_oos_caught_at_100,
            "oos_rejection_rate": round(raw_oos_caught_at_100 / len(oos_rows), 4),
        },
        "non_exact_in_scope_bounds": {
            "count": len(non_exact_in),
            "max_top1": max(r["top1"] for r in non_exact_in),
            "max_mean5": max(r["mean5"] for r in non_exact_in),
        },
        "calibrated": calibrated,
    }

    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        payload = {"summary": summary, "stats": dict(aot.STATS), "in_scope": in_rows, "out_of_scope": oos_rows}
        args.json.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + chr(10), encoding="utf-8")

    print(json.dumps(summary, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
