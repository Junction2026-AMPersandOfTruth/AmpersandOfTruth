# Corpus DB — MariaDB on the team VPS (issue #7)

`srv2158939.hstgr.cloud` · Ubuntu 24.04 · 2 vCPU · 8 GB RAM (+2 GB swapfile, swappiness 10) · 100 GB SSD

| | |
|---|---|
| Image | `mariadb:12.3.3` (= `lts` in Nov 2026), container `corpus-mariadb`, `restart: unless-stopped` |
| Data | docker volume `corpus-data` |
| Listener | `127.0.0.1:3306` on the VPS **only** — nothing public |
| Files on VPS | `/opt/junction/corpus/` (compose, `conf.d/`, `corpus-up.sh`, `.env` with passwords, root-only) |
| Schemas | `corpus` (shared, read-mostly) · `research` (scratch for experiments) |
| Users | `corpus_ro` — SELECT on `corpus`, ALL on `research` · `corpus_rw` — ALL on both (loaders) · `root` — localhost in-container only |

## Day-to-day (laptop)

```bash
scripts/corpus-tunnel.sh                     # localhost:3307 → VPS 127.0.0.1:3306 (background)
dotnet user-secrets set "Corpus:Password" "<corpus_ro pw>" --project src/AmpersandOfTruth.Web
mariadb -h127.0.0.1 -P3307 -ucorpus_ro -p corpus   # or any MySQL client / DBeaver
```

The tunnel logs in as `corpus-tunnel`, an account that can **only** forward to `127.0.0.1:3306` (no shell).
New teammate: they send their public key, someone with root runs `scripts/corpus-grant-tunnel.sh their_key.pub`.
Their tunnel: `CORPUS_SSH_KEY=~/.ssh/their_key scripts/corpus-tunnel.sh`.

## Ops (needs root ssh)

```bash
scripts/corpus-deploy.sh      # rsync deploy/corpus → VPS, compose up, (re)provision schemas/users. Idempotent.
ssh root@srv2158939.hstgr.cloud 'grep CORPUS_ /opt/junction/corpus/.env'   # read passwords
ssh root@srv2158939.hstgr.cloud 'cd /opt/junction/corpus && docker compose logs -f'
```

Passwords are generated once into `.env` on the VPS; to rotate, edit `.env` and re-run `corpus-deploy.sh`.
Start-up after a VPS reboot is automatic (Docker is enabled, restart policy `unless-stopped`).

## Tuning (`conf.d/corpus.cnf`) and measurements

| Setting | 12.3.3 default | Ours | Why |
|---|---|---|---|
| `innodb_buffer_pool_size` | 128M | 3G | budget ~3.5 GB for MariaDB |
| `mhnsw_max_cache_size` | 16M | 512M | 29,460 × 1024-d vectors ≈ 154 MB with the HNSW index |
| `mhnsw_ef_search` | 20 | 80 | upstream demo value, made the server default |
| `max_allowed_packet` | 16M | 256M | big revisions + batched inserts |
| `innodb_log_file_size` / `flush_log_at_trx_commit` | 96M / 1 | 1G / 2 | faster bulk loads; corpus is reloadable |
| `performance_schema` | OFF | OFF (pinned) | memory |

Synthetic benchmark (2026-11-14, 29,460 random unit vectors, 1024-d, `DISTANCE=cosine`, default M):
load 11 s, HNSW build 5 s, table+index 154 MB. Top-10 query: **~0.7–0.9 ms with the 512M cache vs ~3.3 ms
with the 16M default**, brute force (`IGNORE INDEX`) 38 ms. `corpus-mariadb` RSS with the index warm: ~730 MiB
(the buffer pool grows towards 3 GB as revision data is loaded).

Known harmless log warning: `io_uring_queue_init() failed with EPERM` — the host disables io_uring; InnoDB falls back to libaio.

## Loaded datasets (issues #13 & #14)

Source: [`MariaDB/mariadb-wikipedia-datasets`](https://github.com/MariaDB/mariadb-wikipedia-datasets) releases `s-2026-10` (145 MiB, SHA-256 verified) and `m-2026-10` (301 MiB, SHA-256 verified), cached on the VPS under `/opt/junction/corpus/data/{s,m}` (never committed).

### Loaders (idempotent & resumable)

```bash
scripts/corpus/load-s.sh --remote                 # Dataset S → VPS over loopback (~40 s)
scripts/corpus/load-m.sh --remote                 # Dataset M full load → VPS over loopback (~6.3 min)
scripts/corpus/load-m.sh --articles 200           # local/subset: top-200 most-edited articles (via tunnel)
scripts/corpus/load-m.sh --pages golden_pages.txt # subset from page_id list (combinable with --articles N)
```

### Tables in `corpus` (`6.8 GB` total on disk)

| Table | Rows | Logical / `.ibd` size | Keys & notes |
|---|---:|---:|---|
| `article` | 29,460 (`19,285` fi · `4,921` sv · `5,254` en) | 150.6 MB (`160M` `.ibd` + `316M` FTS) | `PK (lang, page_id)`, `KEY (wikidata_id)`, `FULLTEXT (title, text)` — clean FineWiki Markdown (Aug 2025) |
| `article_vector` | 29,460 (`19,285` fi · `4,921` sv · `5,254` en) | 231.3 MB (`164M` + `88M` HNSW) | `PK (lang, page_id)`, `VECTOR(1024)` (`bge-m3`), `VECTOR INDEX (embedding) M=16 DISTANCE=cosine` |
| `category_link` | 1,760 | 0.33 MB (`400K`) | `PK (parent, child)`, `KEY (child)` — Finnish `Suomen historia` category tree |
| `article_category` | 39,037 (`25,138` fi · `6,839` sv · `7,060` en) | 7.03 MB (`15M`) | `PK (lang, page_id, category)`, `KEY (category)` — sv/en inherited via `wikidata_id` |
| `revision` | 862,076 across 19,742 fi articles | 344.9 MB (`356M` `.ibd`: 127.7 MB data + 217.2 MB idx) | `PK (page_id, rev_id)`, `KEY idx_page_ts (page_id, ts)`, `KEY idx_page_sha1 (page_id, sha1)`, `KEY idx_sha1 (sha1)`, `KEY idx_user (user)`, `KEY idx_ts (ts)` |
| `revision_text` | 862,076 | 5,688 MB (`5.7G` `.ibd`) | `PK (page_id, rev_id)`, `text MEDIUMTEXT COMPRESSED NOT NULL` (12.15 GB raw wikitext → 5.69 GB compressed) |

**Why `revision` and `revision_text` are split:** benchmarking `versions-01.parquet` (103,607 rows, 3.04 GB raw wikitext → 1.40 GB compressed) showed InnoDB `DYNAMIC` stores ~4 KB compressed `MEDIUMTEXT` blobs inline on clustered leaf pages, inflating `revision` from 15.6 MB to 1,419 MB (projected ~5.8 GB for all 862k rows — overflowing the 3 GB buffer pool on metadata scans). Splitting keeps all 862k rows + 5 secondary indexes of `revision` in **345 MB** (pinned permanently in the 3 GB buffer pool; full 862k-row window scan runs in ~2.6 s, warm per-file scans 2.5× faster), while `revision_text` is joined by `(page_id, rev_id)` PK only when diffing or rendering wikitext.

**Verified query shapes (`EXPLAIN`):**
- Vector top-k: `ORDER BY VEC_DISTANCE_COSINE(embedding, @q) LIMIT k` inside a subquery on `article_vector`, then `JOIN article USING (lang, page_id)` → uses HNSW index `embedding`.
- Revisions of page X by time: `WHERE page_id = ? ORDER BY ts, rev_id` → `ref` on `idx_page_ts` (no filesort).
- Revisions sharing `sha1` within page X: `WHERE page_id = ? AND sha1 = ?` → `idx_page_sha1` (`index_merge` / `ref`).
- Spot-check: `Talvisota` (`page_id = 1256`) latest 8 revisions up to `2026-10-01` verified row-for-row (`rev_id`, `parent_id`, `ts`, `user`, `bytes`, base-36 `sha1`) against live `fi.wikipedia.org` MediaWiki API.

## Attribution & licensing (for P2.10 / issue #51)

- **Wikipedia text & derived vectors:** [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Credit Wikipedia and its authors, link each current article via `article.url` (`https://<lang>.wikipedia.org/?curid=<page_id>`) and each historical revision via `https://fi.wikipedia.org/w/index.php?oldid=<rev_id>` (or history `?curid=<page_id>&action=history`), and license redistributed text adaptations under CC BY-SA 4.0.
- **Datasets & schema credit:** MariaDB Foundation (`MariaDB/mariadb-wikipedia-datasets` releases `s-2026-10` & `m-2026-10`; `MariaDB/mariadb-vector-wikipedia-rag`, MIT), Hugging Face [FineWiki](https://huggingface.co/datasets/HuggingFaceFW/finewiki) (Aug 2025 clean Markdown), Wikimedia `fiwiki` `pages-meta-history` dumps (1 Oct 2026), and `BAAI/bge-m3` embeddings.

