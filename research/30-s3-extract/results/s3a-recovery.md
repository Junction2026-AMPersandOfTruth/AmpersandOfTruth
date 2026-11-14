# P1.S3 Approach A (`s3_item`) · Dev Golden Recovery & Exposure Report (#30)

Dataset `m-2026-10` · Golden `2026-11-14.1` (`dev` split only: 11 topics, 238 items across 49 item pages) · `research.s3_diff` pages: `245` · `research.s3_item` rows: `9,730` across `203` pages.

## 1. Golden Item Recovery per Label (Dev Split)

- **Recovered:** ≥ 1 `s3_item` row whose `inserting_rev_ids` intersects the golden item's `inserting_rev_ids`.
- **Right removal:** ≥ 1 matching `s3_item` row whose `reverting_rev_id` is in the golden item's `removing_rev_ids`.
- **Right window:** ≥ 1 matching `s3_item` row whose `inserted_at` equals the golden item's first inserting revision timestamp.
- **All three:** ≥ 1 matching `s3_item` row satisfying intersection, right removal, and right window start simultaneously.

| Label | Golden items | Recovered | % | Right removal | % of total | Right window | % of total | All three |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `campaign` | 1 | 1 | 100.0% | 1 | 100.0% | 1 | 100.0% | 1 |
| `contested` | 63 | 52 | 82.5% | 52 | 82.5% | 49 | 77.8% | 49 |
| `reverted` | 80 | 70 | 87.5% | 70 | 87.5% | 65 | 81.2% | 65 |
| `not-incident` | 94 | 82 | 87.2% | 82 | 87.2% | 77 | 81.9% | 77 |
| **Strict incidents (`contested` + `campaign`)** | **64** | **53** | **82.8%** | **53** | **82.8%** | **50** | **78.1%** | **50** |
| **All dev items** | **238** | **205** | **86.1%** | **205** | **86.1%** | **192** | **80.7%** | **192** |

## 2. Filter Agreement (`is_substantive=0` vs `not-substantive`, `is_blanking=1` vs `blanking`)

Evaluated over the **205** recovered dev golden items (plus notes on unrecovered exclusions):

| Rule vs Golden Exclusion | TP | FP | FN | TN | Overall Agreement | Exclusion Recall | Real Incidents Falsely Filtered |
|---|---:|---:|---:|---:|---:|---:|---:|
| `is_substantive = 0` vs `exclusion = 'not-substantive'` | 11 | 2 | 14 | 178 | 92.2% | 11 / 25 (44.0%) | 2 / 123 (1.6%) |
| `is_blanking = 1` (all spans) vs `exclusion = 'blanking'` | 8 | 2 | 0 | 195 | 99.0% | 8 / 8 (100.0%) | 1 / 123 (0.8%) |

- Note: `5` additional golden `blanking` item(s) were already filtered upstream by S1's `excluded` (`blanking_collision`) guard, and `4` golden `not-substantive` item(s) had no S1 identity revert.

## 3. Unrecovered Dev Golden Items & Reasons

| Reason | Total | `campaign` | `contested` | `reverted` | `not-incident` |
|---|---:|---:|---:|---:|---:|
| `no identity revert` | 5 | 0 | 3 | 2 | 0 |
| `partial removal` | 21 | 0 | 8 | 6 | 7 |
| `span missing from s3_diff` | 0 | 0 | 0 | 0 | 0 |
| `excluded class` | 7 | 0 | 0 | 2 | 5 |
| **Total unrecovered** | **33** | **0** | **11** | **10** | **12** |

| Item | Label | Exclusion | Page | Inserting revs | Removing revs | Reason | Notes |
|---|---|---|---:|---|---|---|---|
| `civil-war-1918/i08` | `contested` | `–` | `1150` | `4247136` | `4248096` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `civil-war-1918/i19` | `reverted` | `–` | `1150` | `377511` | `377522` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `civil-war-1918/i20` | `reverted` | `–` | `6021` | `10867051` | `15892014` | `no identity revert` | Exact added prose deleted later without restoring an earlier page SHA-1 (intermediate edits intervened) |
| `civil-war-1918/n01` | `not-incident` | `blanking` | `6021` | `3536150` | `3664584` | `excluded class` | S1 revert `3664584` excluded (`blanking_collision`) |
| `civil-war-1918/n08` | `not-incident` | `self-removal` | `1150` | `24457` | `28415` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `civil-war-1918/n09` | `not-incident` | `not-substantive` | `1150` | `9114283` | `9148559` | `partial removal` | Span `9148559` attributed insertion to sibling prose-adding edit in span |
| `civil-war-1918/n11` | `not-incident` | `not-substantive` | `6021` | `1975335` | `15882859` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `continuation-war/i04` | `contested` | `–` | `440` | `1512124` | `1573135` | `no identity revert` | Exact added prose deleted later without restoring an earlier page SHA-1 (intermediate edits intervened) |
| `continuation-war/i16` | `reverted` | `–` | `319344` | `3888482` | `5399823` | `partial removal` | Span `5399823` attributed insertion to sibling prose-adding edit in span |
| `winter-war/i18` | `reverted` | `–` | `24889` | `21205039` | `21563264` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `winter-war/n05` | `not-incident` | `bot` | `1256` | `9113749` | `9132337` | `partial removal` | Span `9132337` attributed insertion to sibling prose-adding edit in span |
| `ceded-karelia/i04` | `contested` | `–` | `9895` | `8974672` | `10488546` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `nato-debate/i07` | `reverted` | `–` | `128998` | `20421357, 20421508, 20421511, 20421526 (+8)` | `20434133` | `excluded class` | S1 revert `20434133` excluded (`wide_span`) |
| `nato-debate/i08` | `reverted` | `–` | `128998` | `20427576` | `20434133` | `excluded class` | S1 revert `20434133` excluded (`wide_span`) |
| `finlandization/i02` | `contested` | `–` | `5261` | `20291337` | `20302034` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `finlandization/i05` | `contested` | `–` | `942` | `3540937` | `3543599` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `finlandization/i07` | `contested` | `–` | `20742` | `22159468` | `22244059` | `no identity revert` | Exact added prose deleted later without restoring an earlier page SHA-1 (intermediate edits intervened) |
| `finlandization/n03` | `not-incident` | `blanking` | `69647` | `5674548, 5785631, 5789946, 5790716 (+1)` | `5779927, 5789513, 5790612, 9060933 (+1)` | `excluded class` | S1 revert `5779927` excluded (`blanking_collision`) |
| `finlandization/n04` | `not-incident` | `blanking` | `1275` | `7627, 9814` | `9809, 154919` | `excluded class` | S1 revert `9809` excluded (`blanking_collision`) |
| `finlandization/n05` | `not-incident` | `blanking` | `1187` | `3972627` | `4160250` | `excluded class` | S1 revert `4160250` excluded (`blanking_collision`) |
| `finlandization/n17` | `not-incident` | `not-substantive` | `942` | `59431` | `2374425` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `austerity-politics/i21` | `contested` | `–` | `9059` | `20562155` | `21350277` | `no identity revert` | Exact added prose deleted later without restoring an earlier page SHA-1 (intermediate edits intervened) |
| `austerity-politics/i22` | `contested` | `–` | `10858` | `8928653` | `8989383` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `austerity-politics/i23` | `contested` | `–` | `10858` | `6885781` | `6887385` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `austerity-politics/n06` | `not-incident` | `blanking` | `795` | `703640` | `2185086` | `excluded class` | S1 revert `2185086` excluded (`blanking_collision`) |
| `covid-19-finland/i01` | `contested` | `–` | `1562418` | `18894761` | `18912236` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `covid-19-finland/i04` | `reverted` | `–` | `1562418` | `18832960` | `18833004` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `covid-19-finland/i06` | `reverted` | `–` | `1562418` | `18789223` | `18790133` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `russification-independence/i03` | `contested` | `–` | `10758` | `11472549` | `12322429` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `russification-independence/i12` | `reverted` | `–` | `32957` | `15633602` | `15711370` | `no identity revert` | Exact added prose deleted later without restoring an earlier page SHA-1 (intermediate edits intervened) |
| `russification-independence/n13` | `not-incident` | `not-substantive` | `10758` | `628693` | `3159633` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `russification-independence/n14` | `not-incident` | `self-removal` | `11517` | `12046260` | `12050219` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |
| `border-crisis-2023/i05` | `reverted` | `–` | `1781605` | `22065800` | `24241333` | `partial removal` | Removed via non-SHA-1 partial deletion or rewrite rather than an identity revert |

## 4. Exposure-Duration Distribution

| Cohort | Count | Median | p75 | p90 | Max |
|---|---:|---:|---:|---:|---:|
| Overall substantive `s3_item` rows (all `s3_diff` pages, `is_substantive=1`) | 8,747 | 3.3 min (196 s) | 30.6 min (1834 s) | 7.95 h (477 min) | 413.1 d (9913.5 h) |
| Dev-split substantive `s3_item` rows (`is_substantive=1`) | 6,521 | 3.3 min (199 s) | 32.0 min (1919 s) | 7.54 h (452 min) | 413.1 d (9913.5 h) |
| Dev-split candidate `s3_item` rows (`is_substantive=1, is_blanking=0`, non-bot/non-self) | 5,803 | 4.2 min (251 s) | 44.2 min (2654 s) | 8.72 h (523 min) | 413.1 d (9913.5 h) |
| Golden `contested` dev items (recovered live exposure) | 52 | 10.25 h (615 min) | 1.3 d (30.2 h) | 2.5 d (59.2 h) | 38.0 d (913.1 h) |

## 5. Top 5 Longest-Lived Golden `contested` Items (Dev Split)

1. Suomen sisällissodan nimet: Keinotekoiset termit … anakronistisesti … osa … tutkijoista — live for 38.0 days (`civil-war-1918/i04`)
2. Suomen sisällissodan nimet: Punakapina termin käyttö on toisaalta perusteltua siinä mielessä, että tietty osa ns. työväestöstä pyrki kaappaamaan val — live for 38.0 days (`civil-war-1918/i03`)
3. Sanna Marin: Helsingin Sanomien kuntavaaliteemaisessa puheenjohtajahaastattelussa 6.6.2021 Marin kannatti pieni- ja keskituloisten ve — live for 7.0 days (`austerity-politics/i04`)
4. Suomen Nato-keskustelu: Lähes kaikki turvallisuuspolitiikan asiantuntijat kannattavat Suomen jäsenyttä NATO:ssa, sillä toisi huomattavaa uskotta — live for 4.0 days (`nato-debate/i02`)
5. Alexander Stubb: Marraskuussa 2015 Stubb väitti eduskunnan kyselytunnilla 90 prosentin hallintarekisteriä koskevista asiantuntijalausunno — live for 2.6 days (`austerity-politics/i11`)
