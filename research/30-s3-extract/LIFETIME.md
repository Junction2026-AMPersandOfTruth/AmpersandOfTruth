# P1.S3 Approach B (`s3_lifetime`) — Sentence-Probe Content-Lifetime Tracking (#30)

## Motivation

SHA-1 identity reverts (`research.s1_revert`, Approach A) only detect removals where a later revision restores the exact
byte-for-byte hash of an earlier revision. When an editor deletes a disputed paragraph while editing another section,
rewrites a biased claim in place, or reverts >15 revisions at once (`wide_span` excluded in S1), the SHA-1 chain never
closes and Approach A is completely blind.

Approach B (`lifetime.py`) tracks substantive prose additions forward through a page's revision sequence using verbatim
sentence probes and server-side `LOCATE(<probe>, text)` binary search, recording when the content disappears, how long
it stayed visible, whether the removal was an S1 identity revert or a `partial` edit, and whether it later reappeared.

---

## Method

1. **Candidates (`s3_diff`, dev split only):**
   - Source: `research.s3_diff` rows with `kind = 'edit'`, `error IS NULL`, and `added_prose_chars >= 40` on pages
     belonging to `golden.json` `split == "dev"` topics (articles + item pages; test split is never read).
   - **Set 1 (`--golden-dev`):** all distinct inserting revisions of dev golden items across all labels (`450` revisions
     total -> `285` with `added_prose_chars >= 40`).
   - **Set 2 (`--sample 300`):** deterministic seeded (`s3-lifetime-30`) sample of `300` non-golden candidate revisions
     from the `2,852` other `added_prose_chars >= 40` edits on dev pages.
   - **Ad hoc (`--revs a,b`):** arbitrary comma-separated `rev_id`s present in `s3_diff`.

2. **Probe Selection (`select_probes`):**
   - Inspects segments with `op == "added"`, `kind == "prose"`, and `moved == false`.
   - Splits `segment["prose"]` on `.`, `!`, `?`, newlines, and ` … `.
   - Keeps stripped sentences of **`>= 25` characters** that also occur **verbatim** in `segment["text"]` (guaranteeing
     every probe is an exact substring of the stored wikitext in `corpus.revision_text.text`).
   - Deduplicates identical strings and selects up to **3 longest** probes (ties broken by appearance order).
   - Edits yielding 0 usable probes are skipped and counted.

3. **Server-Side Tracking (`track_revisions`):**
   - Builds the page's post-candidate revision sequence ordered by `(ts, rev_id)`, dropping null revisions (`sha1`
     equal to the immediately preceding revision's `sha1`).
   - Checks revision presence **on the server** via primary-key lookup:
     ```sql
     SELECT LOCATE(<p0>, text), LOCATE(<p1>, text), ...
     FROM corpus.revision_text WHERE page_id = ... AND rev_id = ...
     ```
     over a single sequential persistent `mariadb` CLI session (one query at a time; zero revision text crosses the SSH tunnel).
   - **Presence rule (`>= half`):** content is present when `found_probes * 2 >= probe_count` (`1/1`, `>=1/2`, `>=2/3`).
   - **Binary search & verification:**
     1. Binary-searches `[0, N)` assuming monotone presence (`True ... True, False ... False`) to find the first absent revision.
     2. Verifies the boundary (`present` immediately before the boundary, `absent` at it; memoized so cached checks cost 0 extra SQL queries).
     3. Checks the page's latest non-null revision: if present again (`still_present_latest = 1`), sets `reappeared = 1`
        and binary-searches `[removing_idx + 1, N - 1]` for `reappeared_rev_id`.
     4. Caps each candidate at **40 SQL queries** (`MAX_QUERIES_PER_CANDIDATE = 40`).

4. **Output Table (`research.s3_lifetime`):**
   - Columns: `rev_id` (PK), `page_id`, `probe_count`, `removing_rev_id`, `removed_at`, `inserted_at`,
     `exposure_seconds`, `removal_kind` (`identity_revert` if `removing_rev_id` is in non-excluded
     `s1_revert.reverting_rev_id`, else `partial`; `NULL` if never removed), `removing_user_display`
     (`aot.user_display`), `removal_summary` (`aot.mask_comment`), `reappeared`, `reappeared_rev_id`,
     `still_present_latest`, `queries`, `seconds`.

---

## Commands

```bash
# Offline unit tests (probe selection, null-rev dropping, >=-half rule, binary search + reappearance + cap)
python3 -m unittest -v research/30-s3-extract/test_lifetime.py

# Full rebuild of both dev golden candidates and 300-candidate sample + report generation
python3 research/30-s3-extract/lifetime.py --golden-dev --sample 300

# Run individually (idempotent / resumable unless --drop is passed)
python3 research/30-s3-extract/lifetime.py --golden-dev
python3 research/30-s3-extract/lifetime.py --sample 300

# Ad hoc revisions or report-only regeneration from research.s3_lifetime
python3 research/30-s3-extract/lifetime.py --revs 6249,6307 --no-report
python3 research/30-s3-extract/lifetime.py --report
```

---

## Results Summary (`results/s3b-recovery.md` & `.json`)

### 1. Golden Dev Recovery (`reverted` / `contested` / `campaign`)

| Group | Golden items | Trackable items | Right removal (`removing_rev_id` match) | % of all items | % of trackable |
|---|---:|---:|---:|---:|---:|
| **All positive (`reverted`+`contested`+`campaign`)** | 144 | 106 | **84** | **58.3%** | **79.2%** |
| — S1 identity-revert removal (Approach A visible) | 124 | 88 | **71** | 57.3% | 80.7% |
| — **Non-S1 removal (Approach A blind)** | 20 | 18 | **13** | **65.0%** | **72.2%** |
| — Non-S1 *(strictly not in `s1_revert`, excl. 2 `wide_span`)* | 18 | 17 | **13** | **72.2%** | **76.5%** |
| **Strict TPs (`contested` + `campaign`)** | 64 | 55 | **45** | **70.3%** | **81.8%** |
| — **`contested` Non-S1 removal (Approach A = 0%)** | **11** | **11** | **9** | **81.8%** | **81.8%** |

- **Headline finding:** On the 11 `contested` golden items whose removal is **not** an S1 identity revert (where
  hash-based Approach A has **0%** recall), Approach B tracks all 11 and recovers the **exact golden `removing_rev_id`
  on 9 / 11 (81.8%)**. Across all 18 non-SHA1 removals (`reverted` + `contested`), it recovers **13 / 18 (72.2%)**.
- Why the remaining 60 of 144 positive items did not match an exact `removing_rev_ids` entry:
  - `15` (`10.4%`) added `< 40` prose chars (`below_40_chars`, mostly short 1–3 word `reverted` edits).
  - `23` (`16.0%`) had `>= 40` prose chars but no `>= 25`-char sentence free of inline wikitext markup (`no_usable_probe`).
  - `21` (`14.6%`) found a removal at a different revision (`other_removal_rev` — e.g. an intermediate rewrite, partial
    trim, or a later edit-war round).
  - `1` (`0.7%`) reported `no_removal_found`.

### 2. Exposure Windows & `kept` Exclusions

- **Exposure windows (`first-insert -> first-removal`):** Among the 84 right-removal items, **65.5%** (`55 / 84`) match
  the golden exposure window **to the exact second (`0 s`)** and **73.8%** (`62 / 84`) are within `±1 hour` (median
  signed difference `0 s`, median absolute error `0 s`).
- **Not-incident `kept` items (`exclusion = 'kept'`, `N = 3`):** Both trackable `kept` items (`ceded-karelia/n01` and
  `russification-independence/n01`, **2 / 2 trackable**, `2 / 3` total) report `still_present_latest = 1` on **100%** of
  their tracked inserting revisions (`russification-independence/n01` explicitly captures `reappeared = 1` after its
  revert was undone). The third item (`nato-debate/n01`) had no trackable probe.

### 3. Partial Removals in the Non-Golden Sample & Cost

- **Partial-removal rate:** Among tracked non-golden additions in `s3_lifetime` (`127` tracked, `85 / 300 = 28.3%`
  skipped with no usable probe), `123` had a probe disappear and **`30 / 123` (`24.4%` of removals, `23.6%` of tracked)**
  were `partial` removals invisible to S1 (`29 / 30` remained absent in the latest revision).
- **Human inspection of the 5 masked `partial` examples (`results/s3b-recovery.md` §5):**
  - *Genuine substantive / NPOV removals:* *Suur-Suomi* (`rev 393459`, explicit neutrality summary `neutraloitu, eikä tämä 1930-luvun kielikysymys varsinaisesti liity koko Suur-Suomeen`), *Matti Vanhanen* (`rev 677794`, removal of unsourced tabloid claim), and *Jyrki Katainen* (`rev 9803745`, factual dispute).
  - *Benign copy-editing / automated cleanup:* *Urho Kekkonen* (`rev 3859398`, AWB bot converting hyphen `1960-1980` to en-dash `1960–1980` broke exact substring match!).
- **Cost & extrapolation:**
  - **Queries:** mean **9.45 SQL queries/candidate** (median **10.0**, P90 **12.0**, max **13** — well below the 40-query cap).
  - **Wall time over remote SSH tunnel:** mean **3.35 s/candidate** (dominated by SSH tunnel round-trip latency + InnoDB decompression of compressed `mediumtext`).
  - **Extrapolation to all `bytes_delta >= +200` edits on one topic:** a dev topic averages **1,297.3** non-null revisions
    with `bytes_delta >= +200` (~`930` probed candidates at the `71.7%` sample probe yield), requiring **~8,840 SQL
    queries** (**~63 min** sequentially over a remote laptop SSH tunnel, vs ~2–4 min if executed locally on the MariaDB host).

---

## Limits & Implications for S9 Pipeline Design

1. **Copy-edits and typography fixes trigger false `partial` "removals":** Any edit that fixes a typo, replaces `-` with
   `–` (e.g. AWB maintenance), or inserts `[[wikilink]]` brackets inside the probed sentence causes `LOCATE` to return `0`.
   Approach B therefore **cannot** treat every `partial` disappearance as a disputed removal on its own — downstream
   stages must combine it with edit-summary signals (`s5_summary`, e.g. excluding bot/AWB/typo summaries and boosting
   neutrality/sourcing summaries) or diff/LLM verification on the `removing_rev_id` diff.
2. **Inline markup reduces probe coverage (~20–28% skipped):** Sentences containing piped links `[[Suomi|Suomen]]` or
   bold/italics `'''...'''` differ between `prose` and raw `text`. If every `>= 25`-char sentence in an edit contains
   inline markup (or prose lives only in infobox/template segments), `select_probes` yields no verbatim probe (`57 / 285`
   on golden dev, `85 / 300` on sample). Extracting markup-preserving spans from `segment["text"]` or stripping link
   brackets on the fly could recover most of those 34 markup-skipped golden edits if needed.
3. **Monotone binary search misses brief revert-then-restore dips:** When an edit is reverted at `t+1`, restored at `t+2`,
   and kept for hundreds of subsequent revisions, midpoint binary search lands in the `True` region and reports
   `removing_rev_id = NULL, still_present_latest = 1`. This correctly classifies `kept` exclusions as still present,
   though a multi-round edit war that ends in kept content must rely on S1/S4 to surface the intermediate conflict.
4. **Tunnel RTT makes unselective full-topic scanning expensive remotely:** Running ~9,000 sequential `LOCATE` queries
   over a laptop SSH tunnel takes ~1 hour per topic. In production or batch precompute, Approach B should either run
   colocated with MariaDB (sub-millisecond loopback RTT) or be restricted to high-priority candidate edits (e.g.
   large anonymous/new-editor additions, conflict episodes from S4, or pages with high dispute density).
