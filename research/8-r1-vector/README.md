# P1.R1 (#8) · Baseline retrieval: per-article vector search

`variant.py` — contract §2.1 retrieval variant. bge-m3 embedding of the raw topic → cosine top-k on `article_vector`
(top-k inside a subquery so the HNSW index is used) → en/sv hits folded onto their fi sibling via `wikidata_id`.

| Flag | Meaning |
|---|---|
| `--langs all` (default) | search all three languages, fold to fi, keep the best rank per fi page |
| `--langs fi\|en\|sv` | one language only (index over-fetches 4×, then filters) |
| `--oos D` | `out_of_scope` when the best distance > D (off by default — see note) |

```bash
python3 research/eval/validate.py variant retrieval -- python3 research/8-r1-vector/variant.py
python3 research/lib/probe.py -- python3 research/8-r1-vector/variant.py --langs fi    # eyeball, not a score
```

**Pre-golden observations (probes in `results/`, not a score):** all-langs vs fi-only find about the same share of
expected titles (20 vs 18 of 37 in the top 10). A bare distance cut-off cannot separate out-of-corpus topics:
"Bitcoin price history" has best distance 0.53, while the in-corpus Swedish query "Fortsättningskriget" has 0.59.
Scope detection therefore needs R3 variant E (LLM) or a relative signal, not a fixed threshold. Latency: ~0.2 s
embedding (cached after first call) + ~0.6 s search through the mariadb CLI.

Scoring on the golden set (recall@k by query style, nDCG, oos rates) follows when #20/#21 land.
Shared helpers for every Codex-lane variant live in `research/lib/aot.py` (SQL via the mariadb CLI, bge-m3 embedding
with a disk cache, nearest/fold, and a Python port of `EditorDisplay` masking).
