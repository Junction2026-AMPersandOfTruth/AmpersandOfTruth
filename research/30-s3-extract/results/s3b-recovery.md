# P1.S3 Approach B (`s3_lifetime`) — Recovery, Partial Removals & Cost Report

Evaluated on `golden.json` **dev** split only (`s3_diff` `kind='edit'`, `added_prose_chars >= 40`,
up to 3 verbatim sentence probes of `>= 25` chars, server-side `LOCATE`, `>= half` presence rule, max 40 queries/candidate).

## 1. Candidate Accounting

| Set | Pool | `< 40` prose chars | Eligible (`>= 40`) | Skipped (no usable probe) | Tracked in `s3_lifetime` |
|---|---:|---:|---:|---:|---:|
| Golden dev inserting revs (all labels) | 450 *(missing diff: 0)* | 165 | 285 | 57 | 228 |
| Seeded non-golden sample (`--sample 300`) | 2852 (`>= 40`) | — | 300 | 85 | 127 |

## 2. Golden Dev Recovery (`reverted` / `contested` / `campaign`)

An item is counted as **right removal** when Approach B tracks at least one of its inserting revisions
and finds `removing_rev_id` inside the item's `removing_rev_ids`.
`S1 removal` = at least one `removing_rev_id` is a non-excluded `s1_revert.reverting_rev_id` (what Approach A can see).

| Group | Golden items | Trackable items | Right removal | % of all items | % of trackable | Any removal found | Outcomes breakdown |
|---|---:|---:|---:|---:|---:|---:|---|
| **All (`reverted`+`contested`+`campaign`)** | 144 | 106 | 84 | 58.3% | 79.2% | 105 | `right_removal`=84, `other_removal_rev`=21, `no_removal_found`=1, `no_usable_probe`=23, `below_40_chars`=15 |
| — S1 identity-revert removal (Approach A visible) | 124 | 88 | 71 | 57.3% | 80.7% | 87 | `right_removal`=71, `other_removal_rev`=16, `no_removal_found`=1, `no_usable_probe`=21, `below_40_chars`=15 |
| — **Non-S1 removal (Approach A blind)** | 20 | 18 | 13 | 65.0% | 72.2% | 18 | `right_removal`=13, `other_removal_rev`=5, `no_usable_probe`=2 |
| — Non-S1 *(excl. 2 `wide_span` S1 rows)* | 18 | 17 | 13 | 72.2% | 76.5% | 17 | `right_removal`=13, `other_removal_rev`=4, `no_usable_probe`=1 |
| **`contested` + `campaign` (strict TPs)** | 64 | 55 | 45 | 70.3% | 81.8% | 55 | `right_removal`=45, `other_removal_rev`=10, `no_usable_probe`=6, `below_40_chars`=3 |
| — `contested`+`campaign` S1 removal | 53 | 44 | 36 | 67.9% | 81.8% | 44 | `right_removal`=36, `other_removal_rev`=8, `no_usable_probe`=6, `below_40_chars`=3 |
| — **`contested`+`campaign` Non-S1 removal** | 11 | 11 | 9 | 81.8% | 81.8% | 11 | `right_removal`=9, `other_removal_rev`=2 |
| `reverted` (all) | 80 | 51 | 39 | 48.8% | 76.5% | 50 | `right_removal`=39, `other_removal_rev`=11, `no_removal_found`=1, `no_usable_probe`=17, `below_40_chars`=12 |
| — `reverted` S1 removal | 71 | 44 | 35 | 49.3% | 79.5% | 43 | `right_removal`=35, `other_removal_rev`=8, `no_removal_found`=1, `no_usable_probe`=15, `below_40_chars`=12 |
| — `reverted` Non-S1 removal | 9 | 7 | 4 | 44.4% | 57.1% | 7 | `right_removal`=4, `other_removal_rev`=3, `no_usable_probe`=2 |
| `contested` (all) | 63 | 54 | 44 | 69.8% | 81.5% | 54 | `right_removal`=44, `other_removal_rev`=10, `no_usable_probe`=6, `below_40_chars`=3 |
| — `contested` S1 removal | 52 | 43 | 35 | 67.3% | 81.4% | 43 | `right_removal`=35, `other_removal_rev`=8, `no_usable_probe`=6, `below_40_chars`=3 |
| — `contested` Non-S1 removal | 11 | 11 | 9 | 81.8% | 81.8% | 11 | `right_removal`=9, `other_removal_rev`=2 |
| `campaign` (all) | 1 | 1 | 1 | 100.0% | 100.0% | 1 | `right_removal`=1 |

### Non-S1 Golden Items Detail (Items Approach A Cannot See)

| Item | Label | Note | Golden `removing_rev_ids` | B `removing_rev_ids` | Outcome |
|---|---|---|---|---|---|
| `civil-war-1918/i08` | `contested` | partial / non-SHA1 | `4248096` | `4248096` | `right_removal` |
| `civil-war-1918/i19` | `reverted` | partial / non-SHA1 | `377522` | `377617` | `other_removal_rev` |
| `civil-war-1918/i20` | `reverted` | partial / non-SHA1 | `15892014` | `15892014` | `right_removal` |
| `continuation-war/i04` | `contested` | partial / non-SHA1 | `1573135` | `1573135` | `right_removal` |
| `winter-war/i18` | `reverted` | partial / non-SHA1 | `21563264` | `21563264` | `right_removal` |
| `ceded-karelia/i04` | `contested` | partial / non-SHA1 | `10488546` | `10353949` | `other_removal_rev` |
| `nato-debate/i07` | `reverted` | S1 `wide_span` (>15) | `20434133` | `21072057` | `other_removal_rev` |
| `nato-debate/i08` | `reverted` | S1 `wide_span` (>15) | `20434133` | `—` | `no_usable_probe` |
| `finlandization/i02` | `contested` | partial / non-SHA1 | `20302034` | `20302034` | `right_removal` |
| `finlandization/i05` | `contested` | partial / non-SHA1 | `3543599` | `3543599` | `right_removal` |
| `finlandization/i07` | `contested` | partial / non-SHA1 | `22244059` | `22244059` | `right_removal` |
| `austerity-politics/i21` | `contested` | partial / non-SHA1 | `21350277` | `21350277` | `right_removal` |
| `austerity-politics/i22` | `contested` | partial / non-SHA1 | `8989383` | `8989383` | `right_removal` |
| `austerity-politics/i23` | `contested` | partial / non-SHA1 | `6887385` | `6905553` | `other_removal_rev` |
| `covid-19-finland/i01` | `contested` | partial / non-SHA1 | `18912236` | `18912236` | `right_removal` |
| `covid-19-finland/i04` | `reverted` | partial / non-SHA1 | `18833004` | `18833004` | `right_removal` |
| `covid-19-finland/i06` | `reverted` | partial / non-SHA1 | `18790133` | `—` | `no_usable_probe` |
| `russification-independence/i03` | `contested` | partial / non-SHA1 | `12322429` | `12322429` | `right_removal` |
| `russification-independence/i12` | `reverted` | partial / non-SHA1 | `15711370` | `15711370` | `right_removal` |
| `border-crisis-2023/i05` | `reverted` | partial / non-SHA1 | `24241333` | `23138849` | `other_removal_rev` |

## 3. Exposure Windows: Approach B vs Golden (`first-insert → first-removal`)

| Subset | Items | Exact (`0 s`) | Within `±60 s` | Within `±1 h` | Golden median | B median | Median `B - golden` | Median `|B - golden|` | P90 `|B - golden|` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Items with right removal | 84 | 55 (65.5%) | 55 (65.5%) | 62 (73.8%) | 10.4 h | 17.4 h | 0 s | 0 s | 3.0 d |
| All items where B found a removal | 105 | 55 (52.4%) | 55 (52.4%) | 63 (60.0%) | 8.6 h | 18.6 h | 0 s | 0 s | 36.0 d |

## 4. Not-Incident `kept` Items (`exclusion = 'kept'`)

- Dev `kept` items: **3** (trackable: **2**).
- Items with `>= 1` inserting rev having `still_present_latest = 1`: **2 / 3**.
- Items where **every** tracked inserting rev has `still_present_latest = 1`: **2 / 3**.

| Item | `page_id` | Inserting revs | Tracked revs | `still_present_latest = 1` revs | `reappeared = 1` revs | Never-removed revs |
|---|---:|---|---|---|---|---|
| `ceded-karelia/n01` | 12374 | `[21524938, 21525210, 21525215]` | `[21524938]` | `[21524938]` | `[]` | `[21524938]` |
| `nato-debate/n01` | 1137 | `[14445437, 14445443, 14461119, 14467635]` | `[]` | `[]` | `[]` | `[]` |
| `russification-independence/n01` | 96542 | `[15377454, 15380636]` | `[15377454, 15380636]` | `[15377454, 15380636]` | `[15377454, 15380636]` | `[]` |

## 5. Seeded Non-Golden Sample (`--sample`)

- Sampled `s3_diff` `kind='edit'` (`added_prose_chars >= 40`) candidates: **300** (from pool of **2852** on dev pages).
- Skipped (no usable probe): **85**; tracked: **127**.
- Never removed (`removing_rev_id IS NULL`): **4**.
- Removed: **123** — `identity_revert`: **93**, **`partial` (invisible to Approach A)**: **30** (**23.6%** of tracked, **24.4%** of removed; **29** remain absent in latest revision).

### 5 Masked Examples of `partial` Removals from the Sample

#### Example 1 · *Suur-Suomi* (`page_id=24094`)
- **Inserting rev:** [393200](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=393200) · **Removing rev:** [393459](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=393459) after 2.0 h (`reappeared=0`, `still_present_latest=0`)
- **Probe (`1/3`):** `suomalaisten punaisten käsissä olleen työväenkommuunin kautta sulauttaa suomalaisiin: mm`
- **Masked removal summary:** /* Suur-Suomi-aatteen motiivi */  neutraloitu, eikä tämä 1930-luvun kielikysymys varsinaisesti liity koko Suur-Suomeen, ennemmin Itä-Karjalan kysymys tms.

#### Example 2 · *Matti Vanhanen* (`page_id=795`)
- **Inserting rev:** [677399](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=677399) · **Removing rev:** [677794](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=677794) after 2.6 h (`reappeared=0`, `still_present_latest=0`)
- **Probe (`1/3`):** `Anonyymisti kasvot peitetysti lehdissä esiintynyt "Kaarina" ei ole tullut julkisuuteen eikä väitteille ole esitetty pitäviä todisteita`
- **Masked removal summary:** /* Henkilökohtainen elämä */

#### Example 3 · *Urho Kekkonen* (`page_id=1275`)
- **Inserting rev:** [3779559](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=3779559) · **Removing rev:** [3859398](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=3859398) after 15.7 d (`reappeared=0`, `still_present_latest=0`)
- **Probe (`1/1`):** `Kekkonen-Brežnev-Kosygin 1960-1980`
- **Masked removal summary:** huoltoa , typos fixed: Hrustsev → Hruštšov, 1958-1 → 1958–1 (2)  AWB

#### Example 4 · *Jyrki Katainen* (`page_id=10858`)
- **Inserting rev:** [8989383](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=8989383) · **Removing rev:** [9803745](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=9803745) after 165.8 d (`reappeared=0`, `still_present_latest=0`)
- **Probe (`1/3`):** `Tiedot osoittautuivat taloudelliden kokoluokan suhteen virheelliseksi`
- **Masked removal summary:** *(empty summary)*

#### Example 5 · *Eduskuntavaalit 2011* (`page_id=378293`)
- **Inserting rev:** [20414092](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=20414092) · **Removing rev:** [20421915](https://fi.wikipedia.org/w/index.php?diff=prev&oldid=20421915) after 3.9 d (`reappeared=0`, `still_present_latest=0`)
- **Probe (`1/1`):** `Perussuomalaisten suurta menestystä pohjusti onnistunut ehdokashankinta, minkä ansiosta puolue sai kokoon täydet listat`
- **Masked removal summary:** *(empty summary)*

## 6. Cost & Extrapolation to All Substantive Additions on a Topic

| Set | Tracked candidates | Mean queries | Median queries | P90 queries | Max queries | Mean seconds | Median seconds | P90 seconds | Total seconds |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Golden dev | 228 | 9.42 | 10.0 | 12.0 | 13 | 2.94 s | 2.37 s | 5.16 s | 670.5 s |
| Non-golden sample | 127 | 9.49 | 10.0 | 12.0 | 13 | 4.09 s | 3.01 s | 7.22 s | 519.9 s |
| **Combined** | **355** | **9.45** | **10.0** | **12.0** | **13** | **3.35 s** | **2.62 s** | **6.07 s** | **1190.5 s** |

### Extrapolation to One Topic's Pages (`corpus.revision` non-null `bytes_delta >= +200`)

- Mean substantive additions (`bytes_delta >= +200`) per dev topic: **1297.3** (median: **1287**).
- If every `bytes_delta >= +200` revision were tracked sequentially over the SSH tunnel: **~12309 SQL queries** (**~5311 s** / **88.5 min** per topic); applying the empirical usable-probe yield (`71.7%`): **~8821 SQL queries** (**~3806 s** / **63.4 min** per topic).

| Dev topic | Articles | Non-null revs | `bytes_delta >= +200` | Est. queries (all) | Est. wall time (all) | Est. probed (`72%`) | Est. queries (probed) | Est. wall time (probed) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `civil-war-1918` | 19 | 8498 | 1287 | 12211 | 87.8 min | 922 | 8748 | 62.9 min |
| `continuation-war` | 19 | 6643 | 1062 | 10076 | 72.5 min | 761 | 7220 | 51.9 min |
| `winter-war` | 14 | 4782 | 677 | 6423 | 46.2 min | 485 | 4602 | 33.1 min |
| `ceded-karelia` | 20 | 5095 | 764 | 7249 | 52.1 min | 548 | 5199 | 37.4 min |
| `nato-debate` | 16 | 8911 | 1613 | 15304 | 1.8 h | 1156 | 10968 | 78.9 min |
| `finlandization` | 22 | 10523 | 1721 | 16329 | 2.0 h | 1233 | 11699 | 84.1 min |
| `austerity-politics` | 21 | 10681 | 2034 | 19299 | 2.3 h | 1458 | 13834 | 1.7 h |
| `covid-19-finland` | 12 | 4898 | 1548 | 14687 | 1.8 h | 1109 | 10522 | 75.7 min |
| `russification-independence` | 24 | 5557 | 970 | 9203 | 66.2 min | 695 | 6594 | 47.4 min |
