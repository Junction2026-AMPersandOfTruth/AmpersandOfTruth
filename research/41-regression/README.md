# #41 · P1.P3 demo-pack recorder, before/after diff, and evidence check

Locks in what the app shows for the five regression-pack topics so late pipeline, prompt, or UI changes cannot quietly
break the demo or violate `docs/evidence-standard.md`.

| File | Purpose |
|---|---|
| `topics.json` | The 5 pack topics: 3 `demo` topics (`Suomen sisällissota`, `Jatkosota`, `Talvisota`), 1 `out-of-scope` control (`French Revolution and the reign of terror`), and 1 `quiet` control (`border-crisis-2023.en` from `research/eval/golden.json`). |
| `pack.py` | CLI with `record`, `diff`, and `check` subcommands (Python 3.10+ stdlib + `research/lib/aot.py`). |
| `test_pack.py` | Offline `unittest` suite over synthetic JSONL streams (incl. `graffiti` exclusion, `null` assessment, scope verdicts, expectation failures, and injected fake corpus SQL). |
| `tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/demo-pack.json` | Shared baseline fixture written by `pack.py record` and consumed by `pack.py diff` and C# `DemoPackRegressionTests`. |

## Commands

### 1. Record the baseline fixture (warm caches first)

Always warm the demo caches first (requires the corpus tunnel and `Gemini:ApiKey` so no candidate is degraded), then
record the pack:

```bash
scripts/corpus-tunnel.sh
scripts/warm.sh
python3 research/41-regression/pack.py record
```

- Runs `scripts/investigate.sh run "<topic>" --events` for each entry in `topics.json`, saves raw `<slug>.jsonl` streams
  under `research/41-regression/events/` (git-ignored/local), and writes
  `tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/demo-pack.json` with `git_commit` (`git rev-parse --short HEAD`)
  and `pipeline_version` (parsed from `InvestigationPipeline.cs`).
- Offline / pre-captured replay without `dotnet`:
  ```bash
  python3 research/41-regression/pack.py record --events-dir path/to/events --out path/to/demo-pack.json
  ```

### 2. Diff before/after (`pack.py diff`) — when to run

Run `diff` **before merging any pipeline, retrieval, prompt, ranking, or tier-rule change** (and before updating
`demo-pack.json` via `record`):

```bash
python3 research/41-regression/pack.py diff
# or against saved JSONL streams:
python3 research/41-regression/pack.py diff --fixture tests/AmpersandOfTruth.Tests/Features/Investigations/Fixtures/demo-pack.json --events-dir research/41-regression/events
```

Prints a Markdown report per topic covering:
- Incidents **added**, **removed**, or **moved** in the top `top_n` (matched by `id`).
- **Tier changes** (`reverted` / `contested` / `campaign`).
- **New `campaign` items** — flagged with a loud warning block whenever a `campaign` incident appears that was not
  `campaign` in the baseline.
- **Scope-verdict changes** (`in_scope` ↔ `out_of_scope`), **article list** additions/removals/reordering, and
  **candidate count** deltas.

**Exit code:** exits `0` when all recorded `expect` invariants hold, or `1` when any expectation breaks:
- `demo`: `expect.no_new_campaign` — fails if any new `campaign`-tier incident appears.
- `quiet`: `expect.max_contested_or_above` — fails if the count of `contested` + `campaign` incidents exceeds the recorded baseline.
- `out-of-scope`: `expect.out_of_scope` — fails if `scope.in_scope` is no longer `false`.

### 3. Verify corpus evidence & privacy (`pack.py check`) — what it verifies

Needs the MariaDB corpus tunnel (`CORPUS_*` env / `127.0.0.1:3307`, queried via `research/lib/aot.py`):

```bash
scripts/corpus-tunnel.sh
python3 research/41-regression/pack.py check
# or against saved JSONL streams without re-running dotnet:
python3 research/41-regression/pack.py check --events-dir research/41-regression/events
```

For every shown incident of the `demo` topics, `check` verifies four evidence-standard invariants and prints a
per-topic Markdown report naming the exact failing incident and field (exiting `1` on any failure):
1. **Verbatim quotes (`assessment.quotes[i]`):** every quoted string occurs verbatim in `corpus.revision_text` of at
   least one of the incident's `inserting_rev_ids` (batched PK `(page_id, rev_id)` lookups, cached in memory per revision).
2. **Revision & timestamp provenance (`edits[i].rev_id`, `edits[i].timestamp`):** every edit's `rev_id` exists in
   `corpus.revision` on that `page_id` with the exact stated UTC timestamp.
3. **Wikipedia diff links (`edits[i].diff_ref` and any link field):** every diff/link field is formatted as
   `https://fi.wikipedia.org/w/index.php?diff=prev&oldid=<rev_id>` for a revision belonging to that incident.
4. **No raw IP addresses (any text field):** scans every string field in the incident for unmasked IPv4 or IPv6
   addresses (`docs/evidence-standard.md` §3); any hit fails the check and is reported in masked form only.

### 4. Offline unit tests

```bash
python3 -m unittest discover -s research/41-regression -p 'test_*.py'
python3 -m py_compile research/41-regression/*.py
```
