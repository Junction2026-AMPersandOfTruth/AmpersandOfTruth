# P1.R5 (#26, #94) · Removed-claim vector index & claim-arm retrieval

Dataset S (`corpus.article_vector`) embeds **current** Wikipedia text, where reverted claims have already been removed.
R5 builds a second `bge-m3` HNSW index (`research.r5_claim_vector`) over prose ≥ 80 characters that was **inserted and
later reverted** in `research.s1_revert`, then fuses it with R1 (`research/8-r1-vector`) via Reciprocal Rank Fusion.

Generated JSONL files contain Wikipedia text and live only in `~/.cache/aot-research/r5/` (`$AOT_CACHE_DIR/r5/`) —
**never committed**.

---

## Three-step workflow (GitHub [#94](https://github.com/AmpersandOfTruth/AmpersandOfTruth/issues/94))

| Step | Script | Runs where | Output |
|---|---|---|---|
| **1. Extract** | `extract.py` | Here (Codex lane, throttled against 2-vCPU VPS) | `~/.cache/aot-research/r5/{passages,check}.jsonl` |
| **2. Embed** | `embed.py` | Teammate's Apple silicon Mac ([#94](https://github.com/AmpersandOfTruth/AmpersandOfTruth/issues/94); stdlib only, no repo/DB deps) | `vectors.jsonl` |
| **3. Load & retrieve** | `load.py`, `variant.py` | Here (Codex lane) | `research.r5_claim_vector` + contract §2.1 retrieval |

### Commands

```bash
# Step 1 — extract reverted claim passages + 3-article bge-m3 self-check
python3 research/26-r5-claims/extract.py --pages CORE                     # fast run on the 18 CORE demo pages
python3 research/26-r5-claims/extract.py                                  # full corpus (~8,767 reverts; resumable via revtext cache)

# Step 2 — on Apple silicon (#94): verify Ollama >= 0.40.x bge-m3 parity, then batch-embed (resumable)
python3 research/26-r5-claims/embed.py --check check.jsonl                # cosine >= 0.999 vs stored article_vector (or bare --check)
python3 research/26-r5-claims/embed.py --in passages.jsonl --out vectors.jsonl --batch 16

# Step 3 — load HNSW table and run retrieval variant (contract §2.1)
python3 research/26-r5-claims/load.py --passages ~/.cache/aot-research/r5/passages.jsonl --vectors ~/.cache/aot-research/r5/vectors.jsonl
python3 research/eval/validate.py variant retrieval -- python3 research/26-r5-claims/variant.py --mode hybrid
python3 research/lib/probe.py -- python3 research/26-r5-claims/variant.py --mode hybrid
```

### File formats

- **`passages.jsonl`:** `{"passage_id": "<reverting_rev_id>:<n>", "page_id": int, "inserting_rev_ids": [int, ...], "removing_rev_id": int, "text": str}`
  - Candidates: `research.s1_revert` with `class IN ('dispute','edit_war','graffiti_cleanup','other')` and `net_bytes >= 80`.
  - Diffed `(restored_rev_id → last_reverted_rev_id)` with `scripts/wikidiff.sh batch` in chunks of 200; skips `is_blanking` and non-`is_substantive` diffs.
  - Adjacent added non-moved `prose` segments in the same `new_paragraph` (without intervening `removed` segments) are merged; passages < 80 chars are dropped; passages > 1,500 chars are split at sentence boundaries.
- **`check.jsonl`:** `{"page_id": int, "input": title + "\n\n" + text, "stored": [1024 floats]}` for 3 non-truncated `fi` articles of 2,000–4,000 chars (`page_id` 186, 876, 1095).
- **`vectors.jsonl`:** `{"passage_id": str, "embedding": [1024 floats]}`.
- **`research.r5_claim_vector`:** `(passage_id VARCHAR(32) PK, page_id INT, removing_rev_id INT, inserting_rev_ids VARCHAR(500), text TEXT, embedding VECTOR(1024) NOT NULL, VECTOR INDEX (embedding) M=16 DISTANCE=cosine)`.

| `variant.py` flag | Behaviour |
|---|---|
| `--mode claims` | HNSW cosine top-k over `research.r5_claim_vector`, grouped by `page_id` (best passage wins), `why: ["claim"]` |
| `--mode hybrid` (default) | RRF (`k = 60`) of R1 (`article_vector`, all-langs folded to `fi`) and `--mode claims`; `why` lists `["vector"]`, `["claim"]`, or `["vector", "claim"]` |

---

## Extraction counts & VPS timing

### 1. Full run on the 18 `CORE` demo articles (`results/extract_core.json`)

17 of the 18 `CORE` demo pages (`research/35-s8-signals/candidates.py`) had qualifying candidate reverts:

| `s1_revert.class` | Reverts (`net_bytes >= 80`) | Diffed | Substantive & non-blanking | Passages (`80..1500` chars) | Total chars | Mean chars / passage |
|---|---:|---:|---:|---:|---:|---:|
| `dispute` | 91 | 91 | 87 | 102 | 23,725 | 233 |
| `edit_war` | 97 | 97 | 95 | 145 | 36,520 | 252 |
| `graffiti_cleanup` | 135 | 135 | 128 | 441 | 324,572 | 736 |
| `other` | 9 | 9 | 9 | 9 | 2,474 | 275 |
| **Total (`CORE`)** | **332** | **332** | **319** | **697** | **387,291** | **556** |

**Caveat on `graffiti_cleanup`:** on `CORE`, `graffiti_cleanup` is 40.7% of candidate reverts (135 / 332) but produces
**63.3% of passages** (441 / 697) and **83.8% of characters** because multi-kilobyte copy-paste vandalism gets sliced into
1,500-char passages. Once P1.S9 / golden labels land, testing `--mode hybrid` with `graffiti_cleanup` excluded (or
capped per revert) is an easy follow-up lever.

### 2. Full-corpus scope & throttled progress

Across the entire corpus, `research.s1_revert` has **8,767** candidate reverts (`graffiti_cleanup`: 3,934; `dispute`:
2,639; `edit_war`: 1,613; `other`: 581), requiring **17,524** unique `(page_id, rev_id)` primary-key lookups (~877 PK
batches of 20).

- On a `--limit 200` full-corpus run: **200** reverts → **196** substantive non-blanking diffs → **330** passages
  (**65,960** chars: `dispute` 53, `edit_war` 65, `graffiti_cleanup` 210, `other` 2). Extrapolating `330 / 200` gives
  roughly **~14,500 passages** across all 8,767 reverts (~5,000–6,000 excluding `graffiti_cleanup`).
- **VPS throughput:** at idle, one 20-PK batch + `0.2s` throttle takes ~1.0 s (~15 min for all 877 batches). Under
  concurrent hackathon load on the shared 2-vCPU MariaDB VPS, 15 uncached PK batches took **68.8 s** (4.6 s/batch,
  projecting ~65 min for all 17,524 PKs). To avoid starving interactive queries on the 2-vCPU server, we stopped bulk
  PK fetching after caching `CORE` + the 200-revert sample (946 `revtext` files cached on disk; `extract.py` resumes
  directly from `~/.cache/aot-research/revtext/` whenever run during a quiet window).

### 3. `embed.py --check` verification (VPS Ollama `0.40.16`)

```text
Ollama version: 0.40.16 (http://127.0.0.1:11435)
check page_id=186 (2214 chars): cosine=0.999999 [OK, 1938 ms]
check page_id=876 (3356 chars): cosine=0.999998 [OK, 2586 ms]
check page_id=1095 (3923 chars): cosine=0.999998 [OK, 4841 ms]
```

---

## Smoke-test index & probe comparison vs R1

> **IMPORTANT — SMOKE TEST ONLY, NOT THE REAL INDEX:** Until `vectors.jsonl` comes back from Apple silicon (#94),
> `research.r5_claim_vector` is loaded with **180 short (`<= 600` char) passages across the 17 `CORE` demo articles**,
> embedded one at a time via `aot.embed`. Because **only** `CORE` pages (the top 4 demo topics: Civil War, Continuation
> War, Winter War, Karelia) are in this smoke-test table, RRF promotes those 17 pages on *every* query — artificially
> inflating hits on the first 9 probes while pushing rank-20 non-`CORE` pages (`Suomen Nato-keskustelu` on `nato.en`)
> out of top 20. Treat the table below strictly as an end-to-end plumbing check, **never** as a retrieval benchmark.

| Probe | Expected `fi` articles | R1 (`8-r1-vector`, all langs) ranks | R5 hybrid smoke test (`--mode hybrid`) ranks | R5 claims-only smoke test (`--mode claims`) ranks |
|---|---|---|---|---|
| `civil-war.en` | *Suomen sisällissota*; *Suomen sisällissodan nimet*; *Punakaarti*; *Suojeluskunta* | 1; 16; —; 9 | 1; 3; 5; 2 | 1; 5; 4; 2 |
| `civil-war.fi` | *Suomen sisällissota*; *Suomen sisällissodan nimet* | 4; 1 | 2; 1 | 3; 1 |
| `civil-war.broad` | *Suomen sisällissota*; *Punakaarti*; *Suojeluskunta* | —; 2; — | 2; 1; 3 | 1; 3; 2 |
| `continuation-war.en` | *Jatkosota*; *Risto Ryti*; *Sotasyyllisyysoikeudenkäynti* | 3; —; — | 1; 6; 5 | 1; 5; 4 |
| `continuation-war.sv` | *Jatkosota* | 2 | 1 | 1 |
| `winter-war.en` | *Talvisota*; *Mainilan laukaukset*; *Talvisodan tausta* | 3; 1; 7 | 1; 2; 12 | 1; 9; — |
| `winter-war.fi` | *Talvisota*; *Talvisodan tausta*; *Mainilan laukaukset* | 3; 1; — | 1; 9; 8 | 1; —; 8 |
| `karelia.en` | *Karjala-kysymys*; *Luovutettu Karjala*; *Karjala*; *Suur-Suomi* | 5; —; 7; — | 2; 3; 1; 9 | 2; 3; 1; 7 |
| `karelia.broad` | *Karjala-kysymys*; *Luovutettu Karjala* | —; — | 2; 13 | 2; 12 |
| `nato.en` | *Suomen Nato-keskustelu* | 20 | — | — |
| `finlandization.en` | *Suomettuminen*; *Urho Kekkonen*; *Suomi–Neuvostoliitto-Seura* | 5; 1; — | 10; 2; — | —; —; — |
| `swedish.en` | *Fennomania*; *Suomalaisuuden liitto*; *Axel Olof Freudenthal* | —; —; — | —; —; — | —; —; — |
| `sami.en` | *Saamelaiset*; *Saamelaisten historia*; *Metsäsaamelaiset* | 3; 2; 6 | 5; 3; 11 | —; —; — |
| `lapua.fi` | *Lapuanliike*; *Isänmaallinen kansanliike* | 2; — | 3; — | —; —; — |
| `oos.bitcoin` | *(out of corpus)* | `oos=False` | `oos=False` | `oos=False` |
| `oos.kpop` | *(out of corpus)* | `oos=False` | `oos=False` | `oos=False` |
| **Summary (smoke test)** | **37 expected titles** | **20 top-10 / 22 top-20** | **28 top-10 / 31 top-20*** | **20 top-10 / 23 top-20*** |

*\*Biased upward on probes 1–9 because the smoke-test index contains only the 17 `CORE` pages (full tables in `results/probe.hybrid.md` and `results/probe.claims.md`).*

**Qualitative cross-lingual signal check (`--mode claims` alone):** querying English claim phrasings directly against
Finnish removed passages lands on the target article:
- `"occupied territories instead of ceded Karelia"` → *Luovutettu Karjala* (`410370:0`, cosine distance `0.468`), *Karjala* (`0.495`), *Suur-Suomi* (`0.528`) — whereas R1 missed *Luovutettu Karjala* on `karelia.en`.
- `"War of Liberation rather than a civil war"` → *Suomen sisällissota* (`8000236:0`, cosine distance `0.439`).
