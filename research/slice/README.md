# Golden slice: a local MariaDB copy for fan-out experiments (G1, epic #3)

The VPS has 2 vCPUs, and bulk reads through the SSH tunnel are slow and flaky (see #94: TLS errors, the tunnel dropping
connections). The slice is a **local MariaDB 12.3 container** with the same `corpus.*` and `research.*` table and column
names as the VPS, so variant SQL runs unchanged. Code by a Gemini worker; the orchestrator (Claude) verified it and wrote this README.

```bash
research/slice/slice.sh up                   # podman container aot-slice on 127.0.0.1:3308, volume aot-slice-data (idempotent)
python3 research/slice/export.py             # build or refresh from the tunnel (127.0.0.1:3307); incremental, resumable
CORPUS_SLICE=1 python3 <your variant.py>     # research/lib/aot.py then talks to 127.0.0.1:3308 instead of the tunnel
research/slice/slice.sh status | stop
```

- The root password is generated into `~/.config/aot-slice/env` (mode 600). User `corpus_ro` gets the same password as
  `CORPUS_PW` and the same rights as on the VPS (SELECT on `corpus`, ALL on `research`).
- **Re-run `export.py` after `research/eval/golden.json` changes.** The page set is the union of the golden articles (once the
  file exists), the 18 demo core articles (`research/35-s8-signals/candidates.py` `CORE`) and R1's fi top 20 for every
  probe in `research/lib/probe_queries.json`. Today that is **249 fi pages**, because the golden set does not exist yet. `--pages FILE` overrides it.

## What is in it (first build, 2026-11-14; 2.0 GB on disk)

| Table | Rows (InnoDB estimate) | Size |
|---|---:|---:|
| `corpus.article_vector` | **29,460 (all of them)** | 243 MB |
| `corpus.article` | 29,937 (all rows; `text` filled **only** for slice pages and their en/sv siblings, `''` elsewhere) | 24 MB |
| `corpus.revision` | **70,462** (every revision of the 249 pages) | 32 MB |
| `corpus.revision_text` | **70,462** (every revision's wikitext) | 1,443 MB |
| `corpus.article_category` | slice pages + siblings | < 1 MB |
| `corpus.category_link` | 1,760 (all) | < 1 MB |
| `research.s1_*`, `s4_*`, `s5_*` | rows for slice pages; `s1_user_stats` whole | ~30 MB |

`research.s6_*` and `research.r5_claim_vector` are not copied (built after the exporter; add them to its table list if you need them).

## Checks (run by the orchestrator)

- **Retrieval is unchanged:** `research/lib/probe.py` on R1 gives an **identical top 5 for all 16 probes** on the slice and on
  the VPS, because `article_vector` is complete. Retrieval scores on the slice are therefore valid.
- **Signals are unchanged:** `research/28-s1-revert/variant.py` returns identical incidents for Suomen sisällissota, Talvisota and
  Jatkosota on both.
- **Speed:** the 16-probe R1 run takes 2.1 s on the slice and about 13 s through the tunnel.

## Caveats

- `article.text` is empty for non-slice pages. A variant that reads article text (for example R2's FULLTEXT) is only valid for slice pages
  on the slice; score it on the VPS.
- The slice is a snapshot: rebuilding a `research.*` table on the VPS (for example S1 `class_v2`) needs a re-export to show up here.
