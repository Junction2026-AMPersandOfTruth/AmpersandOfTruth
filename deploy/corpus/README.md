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
