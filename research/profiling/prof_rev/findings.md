# Edit History Profiling: Revisions, Reverts, Survival, S↔M Join & Wikitext (`prof_rev`)

**Scope:** Issue #17, Part A — full profile of Dataset M (`corpus.revision`, **862,076** revisions across **19,742** Finnish Wikipedia articles, `2003-02-21` to `2026-10-01`), joined against Dataset S (`corpus.article` `lang='fi'`, **19,285** articles) and a deterministic 200-page PK sample of `corpus.revision_text`.  
**Reproducibility:** Every reported number is produced by `research/profiling/prof_rev/queries.sql` (plus `research/profiling/prof_rev/analyze_q8_sample.py` for Top-30 template parsing), backed by materialised tables `research.prof_rev_pages`, `research.prof_rev_reverts`, `research.prof_rev_reverted_edits`, and `research.prof_rev_q8_sample`.

---

## Q1 — Revisions per Article

**Interpretation:** Edit history is heavily right-skewed: the median article has **18** revisions (IQR **9–40**), while the top **9.34%** of articles (`>100` revisions, 1,844 pages) account for **52.34%** (451,181) of all revisions in the corpus. Upstream's claim for *Suomen sisällissota* is **confirmed exact**: it is rank **#1** with **3,357** revisions (`page_id=1150`, edited 2003–2026).

### Distribution Percentiles (`N = 19,742` articles, `862,076` revisions)

| Articles | Total Revisions | Mean | Min | p25 | Median (p50) | p75 | p90 | p99 | Max |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 19,742 | 862,076 | 43.67 | 1 | 9.00 | 18.00 | 40.00 | 95.00 | 437.00 | 3,357 |

### Histogram Buckets

| Revisions / Article | Articles | % of Articles | Revisions | % of Revisions |
|---|---:|---:|---:|---:|
| `1` | 198 | 1.00% | 198 | 0.02% |
| `2–10` | 5,912 | 29.95% | 36,312 | 4.21% |
| `11–100` | 11,788 | 59.71% | 374,385 | 43.43% |
| `101–500` | 1,693 | 8.58% | 319,641 | 37.08% |
| `501–1000` | 113 | 0.57% | 73,647 | 8.54% |
| `>1000` | 38 | 0.19% | 57,893 | 6.72% |
| **Total** | **19,742** | **100.00%** | **862,076** | **100.00%** |

### Top 50 Most-Edited Articles

| Rank | Title | `page_id` | Revisions | First Year | Last Year |
|---:|---|---:|---:|---:|---:|
| 1 | Suomen sisällissota | 1150 | 3,357 | 2003 | 2026 |
| 2 | Tarja Halonen | 1187 | 3,078 | 2003 | 2026 |
| 3 | Carl Gustaf Emil Mannerheim | 6021 | 2,400 | 2004 | 2026 |
| 4 | Urho Kekkonen | 1275 | 2,384 | 2003 | 2026 |
| 5 | Sauli Niinistö | 1137 | 2,257 | 2003 | 2026 |
| 6 | Talvisota | 1256 | 2,143 | 2003 | 2026 |
| 7 | Koronaviruspandemia Suomessa | 1562418 | 2,009 | 2020 | 2026 |
| 8 | Mikael Agricola | 7314 | 1,726 | 2004 | 2026 |
| 9 | Suomen tasavallan presidentti | 1069 | 1,689 | 2003 | 2026 |
| 10 | Jatkosota | 440 | 1,671 | 2003 | 2026 |
| 11 | Lestadiolaisuus | 5759 | 1,657 | 2004 | 2026 |
| 12 | Alexander Stubb | 11396 | 1,546 | 2004 | 2026 |
| 13 | Jokelan koulusurmat | 436339 | 1,495 | 2007 | 2026 |
| 14 | Martti Ahtisaari | 5901 | 1,484 | 2004 | 2026 |
| 15 | Matti Vanhanen | 795 | 1,457 | 2003 | 2026 |
| 16 | Vanhoillislestadiolaisuus | 11594 | 1,445 | 2004 | 2026 |
| 17 | Kalevala | 498 | 1,414 | 2003 | 2026 |
| 18 | Nightwish | 14398 | 1,391 | 2004 | 2026 |
| 19 | Hanoi Rocks | 327 | 1,376 | 2003 | 2026 |
| 20 | Suomen lähetyskunta | 1517061 | 1,339 | 2019 | 2026 |
| 21 | Suomen muinaiset kuninkaat | 1100 | 1,313 | 2003 | 2026 |
| 22 | Timo Soini | 41622 | 1,287 | 2005 | 2026 |
| 23 | Saamelaiset | 7283 | 1,279 | 2004 | 2026 |
| 24 | Paavo Väyrynen | 20040 | 1,269 | 2004 | 2026 |
| 25 | Lapuanliike | 11477 | 1,200 | 2004 | 2026 |
| 26 | Pekka Haavisto | 18561 | 1,165 | 2004 | 2026 |
| 27 | Bengt Laurikainen | 1516979 | 1,162 | 2019 | 2026 |
| 28 | Viikingit | 1302 | 1,155 | 2003 | 2026 |
| 29 | Elias Lönnrot | 13182 | 1,153 | 2004 | 2026 |
| 30 | Irwin Goodman | 23596 | 1,103 | 2004 | 2026 |
| 31 | Suomen suuriruhtinaskunta | 6944 | 1,099 | 2004 | 2026 |
| 32 | Jyrki Katainen | 10858 | 1,080 | 2004 | 2026 |
| 33 | HIM | 17105 | 1,080 | 2004 | 2026 |
| 34 | Sanna Marin | 1121620 | 1,059 | 2013 | 2026 |
| 35 | Mauno Koivisto | 752 | 1,057 | 2003 | 2026 |
| 36 | Filosofisen tiedekunnan promootio | 1620726 | 1,056 | 2021 | 2024 |
| 37 | Paavo Lipponen | 942 | 1,037 | 2003 | 2026 |
| 38 | Danny | 9277 | 1,021 | 2004 | 2026 |
| 39 | Ilkka Kanerva | 10907 | 939 | 2004 | 2026 |
| 40 | Leevi and the Leavings | 710 | 927 | 2003 | 2026 |
| 41 | Simo Häyhä | 24889 | 915 | 2004 | 2026 |
| 42 | Päivi Räsänen | 57242 | 904 | 2005 | 2026 |
| 43 | Tony Halme | 1202 | 898 | 2003 | 2026 |
| 44 | Minna Canth | 14596 | 872 | 2004 | 2026 |
| 45 | Luettelo Suomen historian sodista | 2979 | 867 | 2003 | 2025 |
| 46 | Nikolai II | 31598 | 852 | 2005 | 2026 |
| 47 | Porvoon valtiopäivät | 11775 | 848 | 2004 | 2026 |
| 48 | Turun isku 2017 | 1401247 | 848 | 2017 | 2026 |
| 49 | Lapin sota | 6816 | 847 | 2004 | 2026 |
| 50 | Ruotsin vallan aika Suomessa | 5843 | 846 | 2004 | 2026 |

*Caveat:* `revision.title` is already canonicalised per `page_id` across Dataset M (0 pages have varying `title` within `revision`), and 2026 covers Jan–Sep (`max(ts) = 2026-10-01 10:59:12`).

---

## Q2 — Identity Reverts

**Interpretation:** Under the exact canonical definition, **46,049** revisions (**5.34%** of all edits) are identity reverts that undo **64,966** distinct reverted edits (**7.54%** of all edits; **68,628** `(revert, edit)` slots before deduplicating 3,662 nested-revert overlaps). Anonymous users (IPs + `~2025/2026` temporary accounts) generate only **19.30%** of all edits yet account for **63.09%** of all reverted edits — **24.64%** of anonymous edits get reverted vs **3.45%** of named-account edits.

### Overall Summary

| Metric | Count | % of All Edits (`862,076`) |
|---|---:|---:|
| Identity reverts (`R`) | 46,049 | 5.34% |
| Distinct reverted edits (`E`, deduplicated to earliest reverting `R`) | 64,966 | 7.54% |
| Raw `(revert, edit)` pairs (sum of `run_len`, includes nested reverts) | 68,628 | 7.96% |

### Identity Reverts and Reverted Edits by Year

*(Reverts grouped by `YEAR(R.ts)`; reverted edits grouped by `YEAR(edit.ts)` so `% Reverted` is the revert rate of edits made in that year.)*

| Year | Total Edits | Identity Reverts | Reverts (% of Edits) | Reverted Edits | Reverted (% of Edits) |
|---:|---:|---:|---:|---:|---:|
| 2003 | 153 | 1 | 0.65% | 2 | 1.31% |
| 2004 | 5,585 | 136 | 2.44% | 222 | 3.97% |
| 2005 | 22,794 | 835 | 3.66% | 1,524 | 6.69% |
| 2006 | 50,130 | 2,418 | 4.82% | 4,453 | 8.88% |
| 2007 | 70,759 | 3,701 | 5.23% | 6,116 | 8.64% |
| 2008 | 61,652 | 3,435 | 5.57% | 4,874 | 7.91% |
| 2009 | 55,515 | 2,679 | 4.83% | 3,690 | 6.65% |
| 2010 | 45,181 | 2,329 | 5.15% | 3,172 | 7.02% |
| 2011 | 42,425 | 2,210 | 5.21% | 3,027 | 7.13% |
| 2012 | 35,953 | 2,275 | 6.33% | 3,236 | 9.00% |
| 2013 | 36,745 | 2,140 | 5.82% | 2,593 | 7.06% |
| 2014 | 30,384 | 1,763 | 5.80% | 2,284 | 7.52% |
| 2015 | 30,406 | 2,176 | 7.16% | 2,815 | 9.26% |
| 2016 | 28,812 | 2,066 | 7.17% | 2,796 | 9.70% |
| 2017 | 32,053 | 2,055 | 6.41% | 2,672 | 8.34% |
| 2018 | 32,237 | 1,998 | 6.20% | 2,663 | 8.26% |
| 2019 | 36,305 | 2,460 | 6.78% | 3,230 | 8.90% |
| 2020 | 37,592 | 2,408 | 6.41% | 3,125 | 8.31% |
| 2021 | 31,977 | 1,702 | 5.32% | 2,178 | 6.81% |
| 2022 | 42,814 | 1,604 | 3.75% | 2,157 | 5.04% |
| 2023 | 42,291 | 1,898 | 4.49% | 2,742 | 6.48% |
| 2024 | 39,691 | 1,629 | 4.10% | 2,287 | 5.76% |
| 2025 | 32,994 | 1,390 | 4.21% | 2,174 | 6.59% |
| 2026 | 17,628 | 741 | 4.20% | 934 | 5.30% |
| **Total** | **862,076** | **46,049** | **5.34%** | **64,966** | **7.54%** |

### Anonymous vs Named Accounts

| Editor Group (`revision.anonymous`) | Group Total Edits | Reverted Edits | % of Group's Edits Reverted | Share of All Reverted Edits |
|---|---:|---:|---:|---:|
| Anonymous (`1`: IPs + `~2025/2026` temp accounts) | 166,356 | 40,990 | **24.64%** | **63.09%** |
| Named accounts (`0`: registered users + bots) | 695,720 | 23,976 | **3.45%** | **36.91%** |
| **Total** | **862,076** | **64,966** | **7.54%** | **100.00%** |

### Reverted-Run Length Distribution (`R.seq - P.seq - 1`)

**8,420** reverts (**18.28%** of all 46,049 reverts) revert `>1` edit; **37,629** (**81.72%**) revert a single edit. Maximum run length is **1,106** (see Pitfalls below).

| Reverted-Run Length | Identity Reverts | % of Reverts | Sum of Reverted Slots |
|---|---:|---:|---:|
| `1` | 37,629 | 81.72% | 37,629 |
| `2` | 4,912 | 10.67% | 9,824 |
| `3` | 1,849 | 4.02% | 5,547 |
| `4` | 672 | 1.46% | 2,688 |
| `5` | 363 | 0.79% | 1,815 |
| `6–10` | 468 | 1.02% | 3,373 |
| `11–20` | 120 | 0.26% | 1,626 |
| `21–50` | 17 | 0.04% | 484 |
| `>50` | 19 | 0.04% | 5,642 |
| **Total** | **46,049** | **100.00%** | **68,628** |

### Sanity-Check of 3 Reverts by Eye & Pitfalls for P1.S1

1. **Single-edit POV revert (*Suomen sisällissota*, `run_len = 1`)** — Target `P`: [oldid=24234057](https://fi.wikipedia.org/w/index.php?oldid=24234057) → Reverted edit `E`: [diff=24234965](https://fi.wikipedia.org/w/index.php?diff=24234965) → Revert `R`: [diff=24234990](https://fi.wikipedia.org/w/index.php?diff=24234990). Verified `P.text == R.text` (`198,429 B`); `~2026-38421-00` renamed `"Suomen sisällissota"` to `"Suomen sisällissota eli Vapaussota"` across the lead and `Linkkerpar` reverted it 13.4 h later.
2. **Multi-edit spam revert (*Talvisota*, `run_len = 3`)** — Target `P`: [oldid=19624086](https://fi.wikipedia.org/w/index.php?oldid=19624086) → Last reverted edit `E_3`: [diff=19642254](https://fi.wikipedia.org/w/index.php?diff=19642254) → Revert `R`: [diff=19690942](https://fi.wikipedia.org/w/index.php?diff=19690942). Verified `P.text == R.text` (`118,201 B`); `V. Mononen` added 8 self-published *Books on Demand* titles across 3 edits, and `Nitraus` rejected all 3 edits back to `19624086` (`"Hylättiin viimeisimmät 3 tekstimuutosta..."`).
3. **False-positive mega-revert pitfall (*Vanhoillislestadiolaisuus*, `run_len = 1,106`)** — Earlier blanking `P`: [oldid=831633](https://fi.wikipedia.org/w/index.php?oldid=831633) (2006-02-24, `0 B`) → Later blanking `R`: [diff=19772752](https://fi.wikipedia.org/w/index.php?diff=19772752) (2021-05-14, `0 B`, comment `"Ak: Sivu tyhjennettiin"`). Both revisions are empty strings sharing `sha1 = phoiac9h4m842xq45sp7s6u21eteeq1`, falsely sweeping **1,106** legitimate edits across **15.2 years**.

**Key Pitfalls Quantified (critical when implementing `P1.S1`):**
- **Repeated 0-byte page blanking collisions (`r_bytes = 0`):** Only **34** identity "reverts" have `r_bytes = 0` (`sha1 = phoiac9h4m842xq45sp7s6u21eteeq1`), yet because two vandals blanking the same page years apart share the empty-string `sha1`, those 34 events falsely mark **3,391** distinct edits (**5.22%** of all reverted edits) and **1,493 of the 2,254** (`66.2%`) `>1 year` survival edits as reverted. **Rule for P1.S1:** exclude `R.bytes = 0` (and inspect `<100 B` redirect collisions: 93 reverts / 801 slots).
- **Self-reverts:** **3,657** revert runs (**7.94%** of reverts) undo *only* the reverter's own edits (**5,147** distinct reverted edits: **3,275** named users fixing their own mistakes + **1,872** same-IP self-reverts). Exclude self-reverts when scoring controversy/disputes.
- **Bots & 2013 Wikidata interwiki migration (`Addbot`):** `*bot*` accounts perform **403** reverts and author **1,386** reverted edits. Notably, **215** reverts are `Addbot` removing interwiki links (`[[ru:...]]`, `[[en:...]]`) in 2013 during the Wikidata migration — on quiet stubs where only interwiki bots had edited since 2009, removing the link restored the exact 2009 `sha1`, creating fake 3–4 year "reverts".
- **Null/dummy log revisions (`sha1 = prev.sha1`):** **5,796** revisions have the exact same `sha1` as their immediate predecessor (page moves, admin protection `"Suojasi sivun..."`, FlaggedRevs stabilization `"Määritti vakautusasetukset..."`), including **923** right after a revert on pages with an older matching `sha1` (properly excluded by `LAG` checking `P.seq < R.seq - 1`). However, **839** null/log revisions sit *inside* a reverted run (e.g., an admin protecting a vandalised page before reverting it) and get counted as reverted edits unless `e.sha1 = prev.sha1` is filtered out.
- **Nested / overlapping reverts:** **3,662** `(revert, edit)` overlaps occur when an edit war (`A→B→A→B`) is later reverted to an even earlier version; always pair each reverted edit with its **earliest** reverting `R` (`revert_rank = 1`) so edits aren't double-counted and survival times aren't inflated.
- **Null `sha1`s & `user`s, and Wikimedia Temporary Accounts:** **483** revisions across **116** pages have `sha1 IS NULL` (MediaWiki `rev_deleted` suppressed content; cannot serve as `P` or `R`), and **18** have `user IS NULL`. Wikimedia Temporary Accounts (`~2025-...` / `~2026-...`, **3,228** edits from `2025-09-10` onward) are already flagged `anonymous = 1` in `corpus.revision`.

---

## Q3 — Reverted Edits by Size and Survival

**Interpretation:** Nearly half of reverted runs (**48.16%**) add only **1–100 bytes** (short vandalism/POV insertions), while **12.51%** of reverted runs remove `>100` bytes (section/page blanking). Survival times split sharply by account type: anonymous reverted edits have a median survival of **6.5 minutes** (`391 s`, **54.84%** reverted within 10 min), whereas named-account reverted edits have a median survival of **2.50 hours** (`8,985.5 s`).

### Size Change vs `P` (`bytes - P.bytes`)

*(Shown both per reverted run `last_edit.bytes - P.bytes` across `N = 46,049` reverts and per distinct reverted edit `edit.bytes - P.bytes` across `N = 64,966` edits. Note: the jump in `> 1000` for distinct edits comes from the 34 `P.bytes = 0` blanking collisions where normal 30–100 KB article versions sit between two blankings.)*

| Size Change Bucket (`bytes - P.bytes`) | Reverted Runs (`N=46,049`) | % of Runs | Distinct Reverted Edits (`N=64,966`) | % of Edits | Anonymous Edits | Named Edits |
|---|---:|---:|---:|---:|---:|---:|
| `< -1000` | 2,029 | 4.41% | 2,752 | 4.24% | 1,836 | 916 |
| `-1000..-101` | 3,414 | 7.41% | 4,489 | 6.91% | 2,564 | 1,925 |
| `-100..-1` | 8,234 | 17.88% | 10,349 | 15.93% | 6,707 | 3,642 |
| `0` | 2,133 | 4.63% | 2,544 | 3.92% | 1,826 | 718 |
| `1..100` | 22,179 | 48.16% | 28,015 | 43.12% | 19,795 | 8,220 |
| `101..1000` | 6,840 | 14.85% | 10,317 | 15.88% | 6,141 | 4,176 |
| `> 1000` | 1,220 | 2.65% | 6,500 | 10.01% | 2,121 | 4,379 |
| **Total** | **46,049** | **100.00%** | **64,966** | **100.00%** | **40,990** | **23,976** |

### Survival Time Distribution (`R.ts - edit.ts`, `N = 64,966` distinct reverted edits)

- **Median survival:** Overall **926.0 s** (`15.4 min`) · Anonymous **391.0 s** (`6.5 min`) · Named **8,985.5 s** (`2.50 h`).

| Survival Bucket | Overall | % Overall | Anonymous (`N=40,990`) | % of Anon | Named (`N=23,976`) | % of Named |
|---|---:|---:|---:|---:|---:|---:|
| `< 1 min` | 11,940 | 18.38% | 9,434 | 23.02% | 2,506 | 10.45% |
| `1–10 min` | 17,885 | 27.53% | 13,045 | 31.82% | 4,840 | 20.19% |
| `10–60 min` | 9,235 | 14.22% | 6,030 | 14.71% | 3,205 | 13.37% |
| `1–24 h` | 12,826 | 19.74% | 7,611 | 18.57% | 5,215 | 21.75% |
| `1–7 d` | 4,714 | 7.26% | 2,108 | 5.14% | 2,606 | 10.87% |
| `7–30 d` | 2,593 | 3.99% | 1,139 | 2.78% | 1,454 | 6.06% |
| `30–365 d` | 3,519 | 5.42% | 937 | 2.29% | 2,582 | 10.77% |
| `> 1 y` | 2,254 | 3.47% | 686 | 1.67% | 1,568 | 6.54% |
| **Total** | **64,966** | **100.00%** | **40,990** | **100.00%** | **23,976** | **100.00%** |

### 20 Longest-Surviving Reverted Edits (Exact Canonical Definition)

*Caveat:* Under the strict canonical definition, **all 20** longest-surviving reverted edits belong to *Vanhoillislestadiolaisuus* (`page_id=11594`) from Feb–Mar 2006 because `rev_id=19772752` (2021-05-14) blanked the page to `0` bytes, matching an earlier `0`-byte blanking `rev_id=831633` (2006-02-24). A companion table of the top 10 single-edit non-blanking reverts (`run_len = 1, P.bytes > 100`) follows immediately below for signal design.

| # | Diff Link | Article | User | Survival | Comment |
|---:|---|---|---|---|---|
| 1 | [diff=831644](https://fi.wikipedia.org/w/index.php?diff=831644) | Vanhoillislestadiolaisuus | JeeJee | 5,557.9 d (15.22 y) | `palautus - vandalismia` |
| 2 | [diff=831770](https://fi.wikipedia.org/w/index.php?diff=831770) | Vanhoillislestadiolaisuus | Kahkonen | 5,557.9 d (15.22 y) | `palautus` |
| 3 | [diff=835781](https://fi.wikipedia.org/w/index.php?diff=835781) | Vanhoillislestadiolaisuus | 84.248.119.188 | 5,556.6 d (15.21 y) | `/* Seurat */` |
| 4 | [diff=838315](https://fi.wikipedia.org/w/index.php?diff=838315) | Vanhoillislestadiolaisuus | 81.209.54.114 | 5,555.7 d (15.21 y) | `/* Kritiikki */` |
| 5 | [diff=838403](https://fi.wikipedia.org/w/index.php?diff=838403) | Vanhoillislestadiolaisuus | 80.221.28.169 | 5,555.7 d (15.21 y) | `ihan turhaa tuossa kritiikki vaiheessa ruveta selittämään tuommoista, mutta noista puoluekannoista v` |
| 6 | [diff=838427](https://fi.wikipedia.org/w/index.php?diff=838427) | Vanhoillislestadiolaisuus | Joonasl | 5,555.7 d (15.21 y) | `/* Kritiikki */  otetaan pois koko aihe, Suomessa on yleinen äänioikeus ja kuka tahansa saa äänestää` |
| 7 | [diff=838608](https://fi.wikipedia.org/w/index.php?diff=838608) | Vanhoillislestadiolaisuus | 81.209.54.114 | 5,555.6 d (15.21 y) | `/* Erot muihin lestadiolaissuuntiin */` |
| 8 | [diff=838665](https://fi.wikipedia.org/w/index.php?diff=838665) | Vanhoillislestadiolaisuus | 213.216.199.14 | 5,555.6 d (15.21 y) | `/* Yhteisöllisyys */` |
| 9 | [diff=841706](https://fi.wikipedia.org/w/index.php?diff=841706) | Vanhoillislestadiolaisuus | Muu-karhu | 5,554.6 d (15.21 y) | `kh` |
| 10 | [diff=862827](https://fi.wikipedia.org/w/index.php?diff=862827) | Vanhoillislestadiolaisuus | Jsufi | 5,548.5 d (15.19 y) | `lisätty sana endogamia` |
| 11 | [diff=862860](https://fi.wikipedia.org/w/index.php?diff=862860) | Vanhoillislestadiolaisuus | Jsufi | 5,548.4 d (15.19 y) | *(empty)* |
| 12 | [diff=863712](https://fi.wikipedia.org/w/index.php?diff=863712) | Vanhoillislestadiolaisuus | Kahkonen | 5,548.1 d (15.19 y) | `/* Yhteisöllisyys */ näin ehkä parempi muotoilu` |
| 13 | [diff=863794](https://fi.wikipedia.org/w/index.php?diff=863794) | Vanhoillislestadiolaisuus | 128.214.200.176 | 5,548.1 d (15.19 y) | `palautus. ei merkittyjen lähteiden puute ole peruste tiedon poistamiselle. tiedät itsekin että tieto` |
| 14 | [diff=863858](https://fi.wikipedia.org/w/index.php?diff=863858) | Vanhoillislestadiolaisuus | MikkoM | 5,548.0 d (15.19 y) | `Käyttäjän [[Toiminnot:Contributions/128.214.205.4\|128.214.205.4]] ([[Keskustelu_käyttäjästä:128.214.` |
| 15 | [diff=864348](https://fi.wikipedia.org/w/index.php?diff=864348) | Vanhoillislestadiolaisuus | 128.214.205.4 | 5,548.0 d (15.19 y) | *(empty)* |
| 16 | [diff=864362](https://fi.wikipedia.org/w/index.php?diff=864362) | Vanhoillislestadiolaisuus | ML | 5,547.9 d (15.19 y) | `"synti", huono sana: "Mitään syntilistaa ei ole, vaan on uskovaisen omantunnon asia..". Muistan kyll` |
| 17 | [diff=864380](https://fi.wikipedia.org/w/index.php?diff=864380) | Vanhoillislestadiolaisuus | 128.214.205.4 | 5,547.9 d (15.19 y) | `mieluummin "sopimaton" kuin ei-sopivana ja kyllä synti-sanan voi tässä tuoda esille, koska lauseessa` |
| 18 | [diff=864394](https://fi.wikipedia.org/w/index.php?diff=864394) | Vanhoillislestadiolaisuus | ML | 5,547.9 d (15.19 y) | `sana "synti" on ihan liikaa` |
| 19 | [diff=864540](https://fi.wikipedia.org/w/index.php?diff=864540) | Vanhoillislestadiolaisuus | 128.214.205.4 | 5,547.9 d (15.19 y) | `ei muutosta tekstiosaan. lähteiden numerojärjestys tekstin lähdenumeroita vastaavaksi` |
| 20 | [diff=864557](https://fi.wikipedia.org/w/index.php?diff=864557) | Vanhoillislestadiolaisuus | Joonasl | 5,547.9 d (15.19 y) | `ei kuulu johdantoon, yksityiskohta` |

#### Companion Check: Top 10 Single-Edit Non-Blanking Reverts (`run_len = 1`, `P.bytes > 100`)

| # | Diff Link | Article | User | Survival | Edit Comment → Revert Comment |
|---:|---|---|---|---|---|
| 1 | [diff=14088315](https://fi.wikipedia.org/w/index.php?diff=14088315) | Karali | Mysid | 8.72 y | `l` → `wikilinkityksen poistoa viitteistä...` |
| 2 | [diff=13208736](https://fi.wikipedia.org/w/index.php?diff=13208736) | Kelja (Sakkola) | 87.93.136.223 | 7.27 y | `lisätty määrittely` → `...tässä ei tarvita Tämä artikkeli -mallinetta` |
| 3 | [diff=18677693](https://fi.wikipedia.org/w/index.php?diff=18677693) | Jyrhämäjärvi (Murmanskin alue) | Urjanhai | 6.31 y | `mallineen ohjeen mukaan` → *(empty)* |
| 4 | [diff=14034028](https://fi.wikipedia.org/w/index.php?diff=14034028) | Eerik Krankka | Kuohatti | 4.64 y | *(empty)* → `poistettu Luokka:Pohjoismainen viisikolmattavuotinen sota...` |
| 5 | [diff=14034023](https://fi.wikipedia.org/w/index.php?diff=14034023) | Yrjö Boije | Kuohatti | 4.64 y | *(empty)* → `poistettu Luokka:Pohjoismainen viisikolmattavuotinen sota...` |
| 6 | [diff=16088268](https://fi.wikipedia.org/w/index.php?diff=16088268) | Fasca | Makkool | 4.39 y | `lisätty Luokka:Harrastejärjestöt...` → `/* Aiheesta muualla */` |
| 7 | [diff=6163192](https://fi.wikipedia.org/w/index.php?diff=6163192) | Ostamo | ArthurBot | 4.19 y | `Botti lisäsi: [[ru:...]]` → `Addbot poisti 1 Wikidatan sivulle...` |
| 8 | [diff=6666661](https://fi.wikipedia.org/w/index.php?diff=6666661) | Finlantic | Alexbot | 3.85 y | `Botti lisäsi: [[en:Finlantic]]` → `Addbot poisti 1 Wikidatan sivulle...` |
| 9 | [diff=7804128](https://fi.wikipedia.org/w/index.php?diff=7804128) | Liljendalin seurakunta | Junafani | 3.60 y | `kuntaliitospäivitys` → `poistettu Luokka:Loviisa; lisätty Luokka:Liljendal` |
| 10 | [diff=7262137](https://fi.wikipedia.org/w/index.php?diff=7262137) | Nuorunen | Jackie | 3.59 y | `interwiki: ru` → `Addbot poisti 1 Wikidatan sivulle...` |

---

## Q7 — Dataset S ↔ Dataset M Join (`article` `lang='fi'` vs `revision`)

**Interpretation:** Every single Finnish article in Dataset S (**19,285 / 19,285 = 100.0%**) exists in `revision`, while **457** `page_id`s in `revision` are absent from `article(lang='fi')`. Crucially, Dataset S was snapshotted on **`2025-08-20`** whereas Dataset M runs **13.5 months later to `2026-10-01`** — explaining 274 of the 457 M-only pages (created after `2025-08-20`), all **44** exact title mismatches (pages renamed between Aug 2025 and Oct 2026), and **7,764** pages where `M.latest_rev_id > S.rev_id`. **Always join S and M on `page_id`, never on `title` or `rev_id`.**

### Join & Title Agreement Summary

| Metric | Count | Notes |
|---|---:|---|
| Distinct `revision.page_id` (Dataset M) | 19,742 | Snapshot up to `2026-10-01 10:59:12` |
| `article` rows with `lang='fi'` (Dataset S) | 19,285 | Snapshot up to `2025-08-20 23:00:44` |
| Joined (`revision.page_id = article.page_id`, `lang='fi'`) | **19,285** | 97.69% of M; **100.00%** of S (`fi`) |
| In `revision` (M), missing in `article` (`fi`) | **457** | **274** created after `2025-08-20`; **183** redirects, disambiguations, or tables |
| In `article` (`fi`), missing in `revision` (M) | **0** | None missing (`S_fi ⊂ M`) |
| Joined title mismatches (exact UTF-8 `BINARY`) | **44** | **40** case/accent-insensitive + **4** casing-only (`Suomen Sotaveteraaniliitto` etc.) |
| Joined `latest_rev_id <> article.rev_id` | 7,764 | Pages edited between `2025-08-20` and `2026-10-01` |

### 10 Example Pages in `revision` (M) Missing from `article` (`lang='fi'`)

*(Ordered by `page_id ASC`. On the opposite side — in `article(lang='fi')` but missing from `revision` — the count is **0**, so there are no missing S titles.)*

| `page_id` | Title | Revisions | Latest Bytes | First Edit | Last Edit | Page Type in Wikitext |
|---:|---|---:|---:|---|---|---|
| 8957 | Värtsilä | 33 | 613 | 2004-04-19 | 2015-05-01 | Disambiguation |
| 11656 | Suomen sosialistinen työväenpuolue | 3 | 50 | 2004-06-26 | 2004-06-26 | `#REDIRECT` |
| 13628 | Saagat | 2 | 19 | 2004-07-17 | 2004-07-17 | `#REDIRECT` |
| 17190 | Johan Vilhelm Snellman | 3 | 28 | 2004-09-08 | 2004-09-08 | `#REDIRECT` |
| 17562 | Eduskunnan puhemiehet | 3 | 33 | 2004-09-13 | 2006-02-01 | `#REDIRECT` |
| 30632 | Vuoden 1952 kesäolympialaisten mitalitaulukko | 94 | 3,973 | 2005-01-12 | 2025-07-28 | Medal table article |
| 61137 | Vanaja | 26 | 1,293 | 2005-07-05 | 2021-12-26 | Disambiguation |
| 93885 | Haapasaari | 18 | 576 | 2005-11-11 | 2024-12-15 | Disambiguation |
| 118573 | Pihlajavesi | 22 | 610 | 2006-01-20 | 2019-09-12 | Disambiguation |
| 128021 | Mannerheim-ristin ritarit | 2 | 30 | 2006-02-10 | 2006-02-10 | `#REDIRECT` |

### 10 Example Title Mismatches Among the 44 Renamed Joined Pages

| `page_id` | `article.title` (S, 2025-08) | `revision.title` (M, 2026-10) | `S.rev_id` | `M.latest_rev_id` |
|---:|---|---|---:|---:|
| 7843 | Fortum Waste Solutions | NG Nordic | 21591146 | 24027083 |
| 23832 | Vatja | Vatjan kieli | 23461556 | 23915798 |
| 29994 | Rajavartiolaitos | Suomen rajavartiolaitos | 23492716 | 24245701 |
| 109547 | Inkerin kirkko | Venäjän luterilainen kirkko | 23197757 | 24233187 |
| 272493 | Turun ruotsalainen teatteri | Åbo Svenska Teater | 23345293 | 24064406 |
| 281879 | Matti Aalto | Matti Aalto (kansanedustaja) | 22909259 | 24064385 |
| 331130 | Edvard Björkenheim | Lars Björkenheim | 23504738 | 23563217 |
| 341068 | Kaukaan sellu- ja paperitehdas | Kaukaan tehtaat | 23444767 | 24276544 |
| 406525 | Jussi Kujala (kansanedustaja) | Juho Kujala | 22858598 | 23510224 |
| 412411 | Nikula v. Suomi | Nikula vastaan Suomi | 23356277 | 24091750 |

---

## Q8 — Latest Revision Wikitext Size, `<ref>` Citations & Dispute Templates

**Interpretation:** Typical Finnish history articles are compact (**median 3,817 bytes**, IQR **2,237–7,142 B**), and **90.0%** (`180/200`) of sampled pages contain `<ref>` citations (median **6.5** `<ref>` tags across all 200 pages; **8.0** among citing pages). Banner-level neutrality/POV dispute templates (`{{Neutraalius`, `{{POV`, `{{Kiistanalainen`) are **0%** in current snapshots (rarely left standing), whereas inline citation/cleanup tags — especially **`{{Lähde}}`** (**5.5%** of pages, rank #18 overall) and **`{{Vanhentunut linkki}}`** (**5.5%**, rank #19) — are common; note that editors write **`{{Lähde}}`**, **not** `{{Lähde?` (`0%`), even though MediaWiki renders it as `[lähde?]`.

### Latest Revision Wikitext Size (`revision.bytes` across all `19,742` pages)

| Pages | Mean (B) | Min (B) | p10 (B) | p25 (B) | Median (B) | p75 (B) | p90 (B) | p99 (B) | Max (B) |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 19,742 | 6,787.3 | 19 | 1,473.1 | 2,237.2 | 3,817.0 | 7,141.8 | 13,830.5 | 52,112.5 | 199,252 |

### `<ref>` Tags & Requested Dispute Templates (`N = 200` deterministic random sample via PK)

- **`<ref` share:** **180 / 200 (90.00%)** contain at least one `<ref` tag (identical whether excluding `<references>` or matching raw `<ref` substring, because 179/180 citing pages use `{{Viitteet}}` rather than `<references/>`).
- **`<ref` count:** Median **6.5** across all 200 pages (**8.0** among the 180 citing pages; p25 = **2.0**, p75 = **12.0**, mean = **11.30**, max = **209**).

| Requested Template Prefix (case-insensitive) | Pages in Sample (`N=200`) | % of Sample | Total Occurrences | Notes / Matched Pages |
|---|---:|---:|---:|---|
| `{{Neutraalius` | 0 | 0.00% | 0 | — |
| `{{Lähteetön` | 1 | 0.50% | 1 | *Pietarin kihlakunta* (`page_id=1138628`) |
| `{{Lähde?` *(literal `?`)* | 0 | 0.00% | 0 | **Gotcha:** fiwiki template is named **`{{Lähde}}`** (**11 pages / 5.50%**, 16 hits), rendered as `[lähde?]` |
| `{{Korjattava` | 3 | 1.50% | 3 | *Pekka Haavisto* (`{{Korjattava/johdanto}}`), *Jari Kinnunen (poliisi)* (`{{Korjattava}}`), *Karjalan luovutetun alueen edistysmieliset osuusliikkeet* (`{{Korjattava/kuvitus}}`) |
| `{{POV` | 0 | 0.00% | 0 | — |
| `{{Kiistanalainen` | 0 | 0.00% | 0 | — |

### Top 30 Templates in the 200-Page Sample (with Dispute/Cleanup Tags Highlighted)

Among the Top 30 templates, four are dispute/cleanup/maintenance tags:
- **#18 `{{Lähde}}`** — **11 pages (5.5%)**, 16 occurrences *(inline `[lähde?]` citation-needed tag)*
- **#19 `{{Vanhentunut linkki}}`** — **11 pages (5.5%)**, 13 occurrences *(dead external link tag; plus `{{Deadlink}}` on 2 pages)*
- **#23 `{{Tynkä/Henkilö}}`** (**8 pages, 4.0%**) & **#29 `{{Tynkä/Poliitikko}}`** (**7 pages, 3.5%**) *(stub tags)*

*(Just below the Top 30 in the sample: `{{Parempi lähde}}` on 2 pages, `{{Selvennä}}` on 2 pages, `{{Käännös}}` on 2 pages, `{{Kenen mukaan}}` on 1 page, `{{Viitteetön}}` on 1 page, and `{{Yhdistettävä}}` on 1 page.)*

| Rank | Template | Pages (`N=200`) | % of Pages | Total Occurrences | Category |
|---:|---|---:|---:|---:|---|
| 1 | `{{Viitteet}}` | 179 | 89.5% | 181 | Reference list |
| 2 | `{{Verkkoviite}}` | 99 | 49.5% | 742 | Web citation |
| 3 | `{{Kirjaviite}}` | 81 | 40.5% | 198 | Book citation |
| 4 | `{{Auktoriteettitunnisteet}}` | 63 | 31.5% | 63 | Authority control |
| 5 | `{{Wayback}}` | 44 | 22.0% | 62 | Archive link |
| 6 | `{{Lehtiviite}}` | 38 | 19.0% | 98 | News/journal citation |
| 7 | `{{Edustajamatrikkeli}}` | 27 | 13.5% | 32 | Parliament source |
| 8 | `{{ISBN}}` | 24 | 12.0% | 28 | Identifier |
| 9 | `{{Kuolinaika ja ikä}}` | 19 | 9.5% | 19 | Infobox date |
| 10 | `{{Commonscat-rivi}}` | 18 | 9.0% | 18 | Sister link |
| 11 | `{{Coord}}` | 18 | 9.0% | 18 | Coordinates |
| 12 | `{{Navigaatio}}` | 16 | 8.0% | 20 | Navbox start |
| 13 | `{{NavigaatioLoppu}}` | 16 | 8.0% | 20 | Navbox end |
| 14 | `{{Valtionjohto}}` | 15 | 7.5% | 15 | Infobox |
| 15 | `{{En}}` | 13 | 6.5% | 30 | Language icon |
| 16 | `{{K-ru}}` | 13 | 6.5% | 13 | Cyrillic translit |
| 17 | `{{Tämä artikkeli}}` | 13 | 6.5% | 13 | Hatnote |
| 18 | **`{{Lähde}}`** | **11** | **5.5%** | **16** | **Dispute / citation needed (`[lähde?]`)** |
| 19 | **`{{Vanhentunut linkki}}`** | **11** | **5.5%** | **13** | **Cleanup / dead link** |
| 20 | `{{Ylennykset ja kunniamerkit}}` | 11 | 5.5% | 11 | Infobox section |
| 21 | `{{!}}` | 10 | 5.0% | 28 | Pipe escape |
| 22 | `{{Edeltäjä-seuraaja}}` | 10 | 5.0% | 12 | Succession box |
| 23 | **`{{Tynkä/Henkilö}}`** | **8** | **4.0%** | **8** | **Cleanup / stub** |
| 24 | `{{Kansallisbiografia}}` | 7 | 3.5% | 9 | Biography source |
| 25 | `{{K-sv}}` | 7 | 3.5% | 7 | Language helper |
| 26 | `{{Rakennus}}` | 7 | 3.5% | 7 | Infobox |
| 27 | `{{Syntymä- ja kuolinaika}}` | 7 | 3.5% | 7 | Infobox date |
| 28 | `{{Syntymäaika ja ikä}}` | 7 | 3.5% | 7 | Infobox date |
| 29 | **`{{Tynkä/Poliitikko}}`** | **7** | **3.5%** | **7** | **Cleanup / stub** |
| 30 | `{{Ylioppilasmatrikkeli}}` | 6 | 3.0% | 9 | Archive source |
