# Editor, Edit-Summary & Edit-War Profiling (`prof_ed` — Issue #17, Part B)

Corpus baseline (`corpus.revision`): **862,076** revisions across **19,742** Finnish Wikipedia articles (`483` revisions have `sha1 IS NULL`). Using the shared canonical identity-revert definition (`P.sha1 = R.sha1`, non-null, latest earlier `P` on the same page ordered by `(ts, rev_id)` with `P` not `R`'s immediate predecessor; materialised in `research.prof_ed_reverts` and `research.prof_ed_reverted_edits`), there are **46,049** canonical revert revisions (`5.34%` of all edits) reverting **64,966** distinct revisions (`68,628` `(R, edit)` pairs). Known canonical-revert pitfalls from Part A — **34** zero-byte blanking reverts sharing one `sha1` (sweeping **3,391** distinct edits / **4,567** pairs), **3,657** self-reverts, **215** `Addbot` 2013 interwiki pseudo-reverts, and **839** null/log revisions inside reverted runs — are noted wherever they affect Q5/Q6 numbers, and Q6 explicitly filters out `R.bytes = 0` blanking collisions and self-reverts. Every number below is reproduced directly by `queries.sql`.

---

## Q4. Edit Summaries, Auto-Revert Templates & Dispute Keywords

**Interpretation (for P1.S5):** Auto-generated MediaWiki/FlaggedRevs templates alone identify **71.10%** of all canonical SHA-1 reverts at **97.23%** precision (**80.53%** recall / **94.22%** precision when adding manual shorthand like `rv` and `palautus`), making summary parsing a strong complement when SHA-1 identity fails (e.g. partial undos). For dispute/stance keyword mining, always strip `/* section */` headers first and force accent-sensitive collation (`utf8mb4_uca1400_as_ci`), and prioritise native Finnish patrol vocabulary (`sotku*`, `eston kierto`, `lähteetön`/`lähteettöm*`, `perusteeton`) alongside ideological markers (`POV`/`NPOV`, `propaganda`, `puolueelli*`).

### 4.1 Empty / NULL & Section-Only Share

| Category | Revisions | Share of all edits |
|---|---:|---:|
| `comment IS NULL` (empty/whitespace strings are `0` — normalised to `NULL` on ingest) | 207,798 | **24.10%** |
| Section header only (`/* section */` with no summary text after stripping) | 140,512 | **16.30%** |
| Non-empty summary text after stripping `/* section */` | 513,766 | **59.60%** |
| **Total** | **862,076** | **100.00%** |

### 4.2 Top 40 Normalised Non-Empty Edit Summaries
Normalisation: lowercase, strip leading `/* section */` prefixes, replace user/contributions/talk wikilinks, `~20xx-` temp IDs, and template user slots with `<user>`, and numbers with `<num>`.

| # | Normalised comment | Count | % all edits |
|---:|---|---:|---:|
| 1 | `käyttäjän <user> muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän <user> tekemään versioon.` | 14,785 | 1.72% |
| 2 | `pelastettu <num> lähde(ttä) ja merkitty <num> kuolleeksi.) #iabot (v<num>.<num>.<num>.<num>` | 14,084 | 1.63% |
| 3 | `kh` | 12,916 | 1.50% |
| 4 | `w` | 8,450 | 0.98% |
| 5 | `typo` | 7,416 | 0.86% |
| 6 | `fix` | 6,430 | 0.75% |
| 7 | `l` | 5,146 | 0.60% |
| 8 | `+malline` | 4,584 | 0.53% |
| 9 | `<user> poisti <num> [[wikipedia:wikidata|wikidatan]] sivulle [[d:q<num>]] siirrettyä kielilinkkiä` | 3,723 | 0.43% |
| 10 | `korvataan isbn-tunniste` | 3,528 | 0.41% |
| 11 | `kumottu muokkaus <num>, jonka teki <user>` | 3,396 | 0.39% |
| 12 | `kuva` | 2,642 | 0.31% |
| 13 | `muutetaan taikalinkki isbn-mallineelle` | 2,407 | 0.28% |
| 14 | `linkki` | 2,348 | 0.27% |
| 15 | `luokka` | 2,177 | 0.25% |
| 16 | `käyttäjän <user> muokkaus <num> kumottiin` | 2,012 | 0.23% |
| 17 | `lisätään auktoriteettitunnisteet -malline` | 2,003 | 0.23% |
| 18 | `hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio <num>, jonka on tehnyt <user>` | 1,946 | 0.23% |
| 19 | `wl` | 1,843 | 0.21% |
| 20 | `lk` | 1,735 | 0.20% |
| 21 | `f` | 1,605 | 0.19% |
| 22 | `korjataan navigaatiomallineet` | 1,478 | 0.17% |
| 23 | `botti lisäsi: [[luokka:vuonna <num> syntyneet]], [[luokka:vuonna <num> kuolleet]]` | 1,126 | 0.13% |
| 24 | `pelastettu <num> lähde(ttä) ja merkitty <num> kuolleeksi.) #iabot (v<num>.<num>.<num>` | 975 | 0.11% |
| 25 | `päivitys` | 896 | 0.10% |
| 26 | `linkin korjaus` | 872 | 0.10% |
| 27 | `lähde` | 834 | 0.10% |
| 28 | `linkitys` | 824 | 0.10% |
| 29 | `korvataan kuvakoko` | 814 | 0.09% |
| 30 | `malline` | 743 | 0.09% |
| 31 | `{{auktoriteettitunnisteet}}` | 739 | 0.09% |
| 32 | `edustajamatrikkeli` | 733 | 0.09% |
| 33 | `luokkalisäys` | 732 | 0.08% |
| 34 | `kh.` | 730 | 0.08% |
| 35 | `rv` | 729 | 0.08% |
| 36 | `+` | 710 | 0.08% |
| 37 | `<user> lisäsi [[wikipedia:luokittelu#defaultsort|defaultsort-funktion]].` | 703 | 0.08% |
| 38 | `+kuva` | 673 | 0.08% |
| 39 | `edeltäjä-seuraaja` | 671 | 0.08% |
| 40 | `viite` | 666 | 0.08% |

*(Shorthand glossary: `kh` = kielenhuolto / copyedit, `w` / `wl` = wikitys / wikilinks, `l` / `lk` = luokka / category, `f` = fix, `rv` = revert.)*

### 4.3 Auto-Generated Revert Summary Patterns vs. Canonical SHA-1 Reverts (`N = 46,049`)

| Auto-generated pattern family | Total matches | Canonical SHA-1 reverts (TP) | Precision | Share of SHA-1 reverts (Recall) |
|---|---:|---:|---:|---:|
| 1. MediaWiki Rollback (`... (muokkaukset\|vandalismi) kumottiin ja (sivu\|artikkeli) palautettiin ...`) | 15,332 | 15,328 | 99.97% | 33.29% |
| 2. MediaWiki Undo modern (`Kumottu muokkaus <id>, jonka teki ...`) | 7,748 | 7,039 | 90.85% | 15.29% |
| 3. FlaggedRevs Reject (`Hylättiin (viimeisin\|viimeisimmät\|viimeiset\|viimeinen\|ensimmäinen\|ensimmäiset) ... tekstimuuto...`) | 7,603 | 7,534 | 99.09% | 16.36% |
| 4. MediaWiki Undo legacy (`Käyttäjän ... muokkaus <id> kumottiin` / `Käyttäjän <u > muokkaukset kumottiin`) | 2,558 | 2,409 | 94.18% | 5.23% |
| 5. Navigation Popups / EN gadget (`Revert(ed) to (the )revision ...` / `Reverted edits by ...`) | 364 | 363 | 99.73% | 0.79% |
| 6. Legacy Rollback 2004–2006 (`Palautettiin/Palautettu ... muutokseen`) | 72 | 71 | 98.61% | 0.15% |
| **Union of auto-generated templates (1–6)** | **33,676** | **32,743** | **97.23%** | **71.10%** |
| **Auto-generated + manual revert words (`rv`, `rvv`, `palautus`, `kumottu`, `palautettu`, `hylätty`, `revert`)** | **39,360** | **37,085** | **94.22%** | **80.53%** |

*Why ~9% of modern `Kumottu muokkaus` edits are not SHA-1 identity reverts:* clicking "Kumoa" opens an editable wikitext box — editors either undo an older non-head revision cleanly on top of newer edits or tweak text before saving, producing a fresh `sha1`. Conversely, of the `46,049` canonical SHA-1 reverts, `2,677` (`5.81%`) have `comment IS NULL` and `1,183` (`2.57%`) have only a `/* section */` header (so among SHA-1 reverts with actual summary text, broad revert-summary recall is `37,085 / 42,189 = 87.90%`).

### 4.4 Specified Keyword Frequencies & Example Diffs
Matched with accent-sensitive, case-insensitive collation (`utf8mb4_uca1400_as_ci`). Both section-stripped body counts and raw counts (including `/* section */`) are shown because `/* Poliittinen ura */` inflates raw `poliittinen` by `6.7×`, and word boundaries are used for `POV`/`NPOV` to avoid Slavic surnames (`Popov`, `Karpov`).

| Keyword | Body count (excl. `/* section */`) | Raw count (incl. `/* section */`) | In SHA-1 reverts (% of body) | 3 example diffs |
|---|---:|---:|---:|---|
| `kumottu` | 8,006 | 8,006 | 7,233 (90.3%) | [24295156](https://fi.wikipedia.org/w/index.php?diff=24295156), [24293890](https://fi.wikipedia.org/w/index.php?diff=24293890), [24283856](https://fi.wikipedia.org/w/index.php?diff=24283856) |
| `hylätty` | 35 | 35 | 7 (20.0%) | [21817363](https://fi.wikipedia.org/w/index.php?diff=21817363), [18297660](https://fi.wikipedia.org/w/index.php?diff=18297660), [16494898](https://fi.wikipedia.org/w/index.php?diff=16494898) |
| `palautettu` | 769 | 769 | 259 (33.7%) | [24031994](https://fi.wikipedia.org/w/index.php?diff=24031994), [23881059](https://fi.wikipedia.org/w/index.php?diff=23881059), [23593260](https://fi.wikipedia.org/w/index.php?diff=23593260) |
| `vandalismi` | 591 | 591 | 276 (46.7%) | [23706006](https://fi.wikipedia.org/w/index.php?diff=23706006), [23498004](https://fi.wikipedia.org/w/index.php?diff=23498004), [23411058](https://fi.wikipedia.org/w/index.php?diff=23411058) |
| `propaganda` | 125 | 136 | 18 (14.4%) | [24165048](https://fi.wikipedia.org/w/index.php?diff=24165048), [18197864](https://fi.wikipedia.org/w/index.php?diff=18197864), [18035967](https://fi.wikipedia.org/w/index.php?diff=18035967) |
| `POV` (incl. `NPOV`; strict `POV` = 86 / 16 rev) | 258 | 258 | 51 (19.8%) | [23567608](https://fi.wikipedia.org/w/index.php?diff=23567608), [19794809](https://fi.wikipedia.org/w/index.php?diff=19794809), [17769831](https://fi.wikipedia.org/w/index.php?diff=17769831) |
| `neutraalius` | 109 | 109 | 8 (7.3%) | [21182054](https://fi.wikipedia.org/w/index.php?diff=21182054), [15420685](https://fi.wikipedia.org/w/index.php?diff=15420685), [14675803](https://fi.wikipedia.org/w/index.php?diff=14675803) |
| `lähteetön` | 2,752 | 2,752 | 1,156 (42.0%) | [24293890](https://fi.wikipedia.org/w/index.php?diff=24293890), [24197955](https://fi.wikipedia.org/w/index.php?diff=24197955), [24165552](https://fi.wikipedia.org/w/index.php?diff=24165552) |
| `lähde?` | 392 | 392 | 88 (22.4%) | [24148905](https://fi.wikipedia.org/w/index.php?diff=24148905), [22404926](https://fi.wikipedia.org/w/index.php?diff=22404926), [22222498](https://fi.wikipedia.org/w/index.php?diff=22222498) |
| `puolueellinen` | 7 | 7 | 2 (28.6%) | [18312463](https://fi.wikipedia.org/w/index.php?diff=18312463), [18091978](https://fi.wikipedia.org/w/index.php?diff=18091978), [23331586](https://fi.wikipedia.org/w/index.php?diff=23331586) |
| `poliittinen` | 272 | 1,833 | 18 (6.6%) | [23592560](https://fi.wikipedia.org/w/index.php?diff=23592560), [23089923](https://fi.wikipedia.org/w/index.php?diff=23089923), [21626287](https://fi.wikipedia.org/w/index.php?diff=21626287) |
| `valhe` | 40 | 40 | 7 (17.5%) | [21377370](https://fi.wikipedia.org/w/index.php?diff=21377370), [19580392](https://fi.wikipedia.org/w/index.php?diff=19580392), [18297439](https://fi.wikipedia.org/w/index.php?diff=18297439) |
| `väärä` | 1,161 | 1,165 | 132 (11.4%) | [24243597](https://fi.wikipedia.org/w/index.php?diff=24243597), [23814664](https://fi.wikipedia.org/w/index.php?diff=23814664), [23570481](https://fi.wikipedia.org/w/index.php?diff=23570481) |
| `korjattu` | 7,077 | 7,083 | 74 (1.0%) | [24186179](https://fi.wikipedia.org/w/index.php?diff=24186179), [23428112](https://fi.wikipedia.org/w/index.php?diff=23428112), [23252589](https://fi.wikipedia.org/w/index.php?diff=23252589) |
| `faktat` | 75 | 79 | 9 (12.0%) | [15366537](https://fi.wikipedia.org/w/index.php?diff=15366537), [13546922](https://fi.wikipedia.org/w/index.php?diff=13546922), [11347324](https://fi.wikipedia.org/w/index.php?diff=11347324) |

### 4.5 Other Telling Finnish Words Found in Revert Summaries
Mining the human-written reasons appended after `;`/`:` in `prof_ed_reverts` surfaces high-signal Finnish terms that P1.S5 should include:

| Finnish term / stem | Body count | In SHA-1 reverts | Revert % | Why it matters |
|---|---:|---:|---:|---|
| `sotku` / `sotkeminen` / `sotkentaa` (word-boundary, excl. *Sotkamo*) | 991 | 779 | **78.6%** | Native fi.wp word for vandalism (`sotkemisena` is built into FlaggedRevs); **2.8×** more revert hits than loanword `vandalismi` |
| `eston kierto*` | 870 | 824 | **94.7%** | Block-evasion revert marker (`WP:K`) |
| `lähteettöm*` (oblique/plural stem of *lähteetön*) | 570 | 149 | 26.1% | Finnish consonant gradation (`-tön` → `-ttömät`/`-ttömiä`); missed by literal `lähteetön` |
| `ei lähdettä` / `lähde puuttuu` / `ilman lähdettä` | 215 | 128 | **59.5%** | High-precision unsourced-claim revert justifications |
| `perusteet*` / `selittämät*` (*perusteeton poisto*, *selittämättömiä muutoksia*) | 171 | 107 | **62.6%** | Unexplained removal/alteration disputes |
| `mielipide*` / `spekulaatio*` | 494 | 113 | 22.9% | POV / editorialising challenges |
| `roska*` | 303 | 76 | 25.1% | Nonsense / speedy-delete patrols |
| `uutta tutkimusta` / `oma(a) tutkimus(ta)` | 195 | 70 | 35.9% | Finnish WP:OR (*Ei uutta tutkimusta*) |
| `konsensu*` | 80 | 27 | 33.8% | Explicit talk-page consensus enforcement |
| `häirik*` (*häiriköinti*) | 43 | 24 | **55.8%** | Disruptive editing |
| `puolueelli*` (*puolueellista*, *puolueellisuus*) | 23 | 4 | 17.4% | Inflected stem `-nen` → `-s-` triples `puolueellinen` (7 → 23) |

---

## Q5. Editors, Reverted-Edit Ratios, Single-Article Accounts & Bots

**Interpretation (for P1.S6):** Named accounts generate **80.70%** of all edits with only a **3.44%** revert rate, whereas IP editors (**24.53%** reverted) and post-Sep-2025 `~20xx-` temporary accounts (**30.27%** reverted) behave nearly identically as unauthenticated traffic (`anonymous = 1` covers both in `corpus.revision`). Nearly two-thirds (**63.77%**) of named accounts edit only a single article in this corpus, and bots account for **8.44%–8.99%** of all edits and **should be excluded** via regex + allowlist rather than volume cutoffs.

### 5.1 Editor Classes & Shares of All Edits

| Editor class | Distinct editors (`utf8mb4_bin`) | Distinct (`uca1400_ai_ci`) | Edits | % of all edits | Reverted edits | Class revert % |
|---|---:|---:|---:|---:|---:|---:|
| Named accounts (`anonymous = 0, user IS NOT NULL`) | 15,366 | 15,360 | 695,702 | **80.70%** | 23,964 | 3.44% |
| Anonymous IP editors (`anonymous = 1, NOT ~20xx-`) | 56,301 | 56,301 | 163,128 | **18.92%** | 40,013 | 24.53% |
| Temporary accounts (`anonymous = 1, ~20xx-`) | 1,769 | 1,769 | 3,228 | **0.37%** | 977 | 30.27% |
| Revision-deleted (`user IS NULL`) | 0 | 0 | 18 | **0.00%** | 12 | 66.67% |
| **Total** | **73,436** | **73,430** | **862,076** | **100.00%** | **64,966** | **7.54%** |

*(Temporary accounts split into `~2025-`: 583 accounts / 1,121 edits starting `2025-09-10`, and `~2026-`: 1,186 accounts / 2,107 edits through `2026-09-30`. Every `~20xx-` row has `anonymous = 1`; the single `anonymous = 0` tilde username is a 2017 registered account `~ffwwqq~`.)*

### 5.2 Single-Article Editors (Within Corpus)

| Editor class | Total editors | Active on 1 article | % of class editors | Edits by 1-article editors | % of class edits | % of all corpus edits |
|---|---:|---:|---:|---:|---:|---:|
| Named accounts | 15,366 | **9,799** | **63.77%** | **21,757** | **3.13%** | **2.52%** |
| Anonymous (IP) editors | 56,301 | 46,416 | 82.44% | 68,527 | 42.01% | 7.95% |
| Temporary accounts (`~20xx-`) | 1,769 | 1,575 | 89.03% | 2,021 | 62.61% | 0.23% |

### 5.3 Top 30 Named Accounts by Reverted-Edit Ratio (`≥ 10` edits)
Each edit is counted at most once as reverted (`is_first_revert = 1`). Ties in ratio are ordered by `reverted DESC, edits DESC, account ASC`. *(If `~20xx-` temporary accounts are also included, 4 temp accounts with `≥ 10` edits exceed `0.90`: `~2025-32736-35` 31/31, `~2025-33217-61` 11/11, `~2025-26445-77` 10/10, and `~2026-38421-00` 33/34 = 0.9706.)*

| # | Account | Edits | Reverted | Ratio | Articles | First edit (UTC) | Last edit (UTC) |
|---:|---|---:|---:|---:|---:|---|---|
| 1 | `Mapsed` | 39 | 39 | 1.0000 | 10 | 2016-09-15 10:15:37 | 2016-10-22 12:52:14 |
| 2 | `Suomen sotilas` | 20 | 20 | 1.0000 | 5 | 2008-01-11 18:25:53 | 2008-12-07 15:37:36 |
| 3 | `Mansikkainen` | 17 | 17 | 1.0000 | 1 | 2012-12-19 09:09:05 | 2012-12-20 15:54:04 |
| 4 | `Holycoww` | 16 | 16 | 1.0000 | 1 | 2021-06-08 08:53:20 | 2021-06-27 07:41:09 |
| 5 | `Conservationpatomaki` | 15 | 15 | 1.0000 | 1 | 2020-06-11 09:47:40 | 2021-07-13 07:25:11 |
| 6 | `Loreta alien` | 14 | 14 | 1.0000 | 1 | 2013-03-06 13:23:02 | 2013-03-06 13:45:45 |
| 7 | `Passohoo` | 14 | 14 | 1.0000 | 1 | 2025-01-05 07:19:26 | 2025-02-14 22:04:34 |
| 8 | `SaraBa` *(see caveat §1)* | 14 | 14 | 1.0000 | 2 | 2007-08-05 14:35:30 | 2007-08-05 15:31:40 |
| 9 | `Sinikka Ravander` | 14 | 14 | 1.0000 | 1 | 2020-06-11 10:25:43 | 2020-06-11 11:23:12 |
| 10 | `Lapualaane` | 13 | 13 | 1.0000 | 3 | 2022-11-08 20:42:45 | 2022-11-14 13:12:52 |
| 11 | `Taas uusi käyttäjä` | 13 | 13 | 1.0000 | 4 | 2023-06-07 03:35:45 | 2023-06-10 04:55:11 |
| 12 | `Vaalit2011` | 13 | 13 | 1.0000 | 11 | 2011-03-31 20:57:03 | 2011-03-31 21:36:38 |
| 13 | `Historioitsija` | 12 | 12 | 1.0000 | 1 | 2005-05-09 21:59:52 | 2005-05-09 22:40:51 |
| 14 | `Kristiinab` | 12 | 12 | 1.0000 | 2 | 2019-01-30 17:20:26 | 2019-02-03 23:26:33 |
| 15 | `P. Kajander` | 12 | 12 | 1.0000 | 2 | 2015-09-06 10:55:43 | 2015-09-08 19:30:01 |
| 16 | `Pmmm` | 12 | 12 | 1.0000 | 3 | 2007-09-18 15:03:58 | 2008-02-07 08:09:53 |
| 17 | `Asterix*` | 11 | 11 | 1.0000 | 1 | 2006-07-22 14:41:04 | 2006-07-28 21:13:35 |
| 18 | `Faravid` | 11 | 11 | 1.0000 | 1 | 2010-08-05 10:34:40 | 2010-08-28 17:04:35 |
| 19 | `MUminamummo` | 11 | 11 | 1.0000 | 1 | 2007-02-26 10:52:31 | 2007-04-23 08:54:46 |
| 20 | `MorsoKing` | 11 | 11 | 1.0000 | 3 | 2023-08-03 22:47:03 | 2023-08-03 23:12:51 |
| 21 | `Tunnevaiva` | 11 | 11 | 1.0000 | 1 | 2021-02-05 18:53:45 | 2024-03-01 14:37:14 |
| 22 | `Topkun` | 10 | 10 | 1.0000 | 2 | 2010-03-31 17:53:43 | 2010-03-31 18:15:08 |
| 23 | `Vegguu69` | 10 | 10 | 1.0000 | 1 | 2019-03-15 10:44:29 | 2019-03-15 10:54:26 |
| 24 | `Vapsussota1918` | 29 | 28 | 0.9655 | 14 | 2021-11-16 18:14:11 | 2023-01-20 22:48:11 |
| 25 | `Vihlii` | 21 | 20 | 0.9524 | 18 | 2022-10-14 01:16:18 | 2022-10-15 10:59:12 |
| 26 | `Kallonkutistaja` | 16 | 15 | 0.9375 | 1 | 2007-09-24 13:35:31 | 2007-09-24 13:37:50 |
| 27 | `FiCiW` | 30 | 28 | 0.9333 | 14 | 2015-09-14 18:59:02 | 2015-09-16 11:04:54 |
| 28 | `Enkeksiparempaa` | 15 | 14 | 0.9333 | 15 | 2022-04-13 15:37:34 | 2022-04-13 16:10:40 |
| 29 | `HHTT1` | 13 | 12 | 0.9231 | 2 | 2021-09-24 06:46:17 | 2022-04-16 19:41:17 |
| 30 | `Uusikoti2015` | 20 | 18 | 0.9000 | 1 | 2015-09-24 12:37:27 | 2015-10-02 07:17:48 |

*Effect of Part A pitfalls on this Top 30:* Excluding `R.bytes = 0` blanking collisions drops `SaraBa` (#8) from `14/14` to **`0/14`** (all 14 edits sat between two unrelated page blankings of *Lestadiolaisuus*), and excluding self-reverts (`r_user = e_user`) drops `Kallonkutistaja` (#26) from `15/16` to **`0/16`**, `Tunnevaiva` (#21) from `11/11` to **`4/11`**, `Passohoo` (#7) from `14/14` to `12/14`, `MUminamummo` (#19) from `11/11` to `9/11`, and 1 edit each on `Mansikkainen`, `Lapualaane`, and `Faravid`.

### 5.4 Likely Bots, Edit Share & Exclusion Recommendation
- **Strict `-bot`/`-botti` suffix (`LOWER(user) REGEXP 'bot(ti)?$'`):** **180 accounts** making **72,729 edits** (**8.44%** of all corpus edits; **10.45%** of named-account edits).
- **Extended bot set** (adding SUL-renamed `*Bot~fiwiki` accounts like `H-Bot~fiwiki`, prefix/numbered bots like `KLBot2`/`Ptbotgourou`/`BotMultichill`, plus `Lab-ot`, `CommonsDelinker`, `ʬʭ`, `Overlord`, `Loveless`): **206 accounts** making **77,530 edits** (**8.99%** of all corpus edits; **11.14%** of named-account edits).

| Top likely bots (`≥ 600` edits shown) | Edits | % all edits | Articles | Reverts made | Times reverted |
|---|---:|---:|---:|---:|---:|
| `InternetArchiveBot` | 15,466 | 1.79% | 9,090 | 8 | 41 |
| `Ipr1Bot` | 13,214 | 1.53% | 9,537 | 0 | 5 |
| `ZacheBot` | 4,970 | 0.58% | 3,917 | 1 | 97 |
| `Addbot` *(2013 interwiki pseudo-reverts)* | 3,797 | 0.44% | 3,648 | **215** | 8 |
| `SilvonenBot` | 3,535 | 0.41% | 3,002 | 7 | 27 |
| `Lab-ot` *(no `-bot` suffix)* | 2,505 | 0.29% | 2,457 | 0 | 4 |
| `LKFbot` | 2,237 | 0.26% | 2,036 | 4 | 3 |
| `4shadowwBOT` | 1,599 | 0.19% | 1,525 | 0 | 0 |
| `Luckas-bot` | 1,575 | 0.18% | 941 | 1 | 32 |
| `EmausBot` | 1,534 | 0.18% | 1,068 | 46 | 65 |
| `KielimiliisiBot` | 1,495 | 0.17% | 1,335 | 1 | 11 |
| `ZéroBot` | 1,279 | 0.15% | 997 | 1 | 74 |
| `VakauttajaBot` *(FlaggedRevs auto-stabiliser)* | 1,275 | 0.15% | 738 | 0 | **649** |
| `Thijs!bot` | 1,225 | 0.14% | 698 | 3 | 18 |
| `HarrivBOT` | 1,199 | 0.14% | 1,035 | 6 | 8 |
| `Fiwiki-tools-bot` | 1,156 | 0.13% | 1,050 | 0 | 2 |
| `YurikBot` | 1,093 | 0.13% | 579 | 0 | 12 |
| `Xqbot` | 948 | 0.11% | 619 | 21 | 28 |
| `CommonsDelinker` *(no `-bot` suffix)* | 929 | 0.11% | 708 | 24 | 21 |
| `H-Bot~fiwiki` *(SUL `~fiwiki` suffix)* | 811 | 0.09% | 657 | 0 | 7 |
| `SieBot` (762), `VolkovBot` (758), `SeeggeAWBBot` (686), `TXiKiBoT` (663), `CrtBot` (660), `Zorrobot` (642), `StrynBot` (627), `GEbot` (613), `AtraieBOT` (607) | 6,018 | 0.70% | — | 13 | 55 |

*High-volume human editors (`≥ 8,000` edits) for contrast:* `Tureku` (18,036 edits — outranks the #1 bot), `Risukarhi` (13,133), `Kotivalo` (11,982), `Urjanhai` (10,882), `Vesteri` (10,523), `Laurikainen` (10,263), `Weisself` (8,873), `Tjp` (8,388).

**Recommendation — Exclude bots from P1.S4 & P1.S6 using regex + explicit allowlist (`LOWER(user) REGEXP 'bot(ti)?($|[0-9~_-])|^bot' OR user IN ('Lab-ot', 'CommonsDelinker', 'ʬʭ', 'Overlord', 'Loveless')`), NEVER by edit-volume cutoff:**
1. A volume cutoff would discard the most active human editors and patrollers (`Tureku`, `Risukarhi`, `Kotivalo`).
2. Leaving bots in creates two major Part A revert artifacts: **`VakauttajaBot`** logs a zero-diff page-stabilisation revision (`649` of Part A's `839` null/log revisions inside reverted runs) that gets falsely counted as "reverted" whenever a patroller reverts the preceding vandalism, and **`Addbot`** accounts for **215** 2013 Wikidata interwiki pseudo-reverts (`Addbot ↔ EmausBot` is #5 in Q6.2; bots appear in 3 of the top 6 raw mutual-revert pairs).

---

## Q6. Edit-War Candidates & Mutual-Revert User Pairs (Filtered: `R.bytes > 0`, Excl. Self-Reverts)

**Interpretation (for P1.S4):** After excluding self-reverts (`r_user <> e_user`) and zero-byte blanking collisions (`R.bytes > 0`), there are **10,928** named-vs-named revert exchanges (`15,645` reverted-edit pairs) across **3,568** articles. Candidate articles cluster around presidents, party leaders, 20th-century wars (*Suomen sisällissota*, *Talvisota*, *Jatkosota*), and identity/religious topics, with peak `YYYY-MM` windows matching real public flashpoints (`2012-01`/`2012-02` presidential election on *Pekka Haavisto* & *Sauli Niinistö*, `2008-03` SMS scandal on *Ilkka Kanerva*, `2022-08` on *Sanna Marin*, `2007-11` on *Jokelan koulusurmat*).

**Effect of excluding self-reverts and `R.bytes = 0` blanking collisions:**
- Starting from **13,461** raw named-on-named revert events (`19,718` pairs), excluding self-reverts (`r_user <> e_user`, part of the `3,657` corpus-wide self-reverts) removes **2,531** pure named self-reverts (`3,484` self-reverted pairs) → **10,930** events (`16,234` pairs).
- Further excluding `R.bytes = 0` blanking collisions removes **2** named blanking reverts (*Vanhoillislestadiolaisuus* and *Miset*) and **589** false `(R, E)` pairs → **10,928** events (`15,645` pairs). Ranking by distinct revert events `R` (`Exchanges`) keeps the Top 30 order unchanged, whereas ranking by raw `(R, E)` pairs without `R.bytes > 0` would falsely push *Vanhoillislestadiolaisuus* (`11594`) to **#1 in the entire corpus** (`607` pairs from a single `span = 1,106` blanking collision vs. `19` filtered pairs across `15` events).

### 6.1 Top 30 Articles by Named-vs-Named Revert Exchanges (`R.bytes > 0`, `r_user <> e_user`)
`Exchanges` counts distinct canonical revert revisions `R` (`R.bytes > 0`) by a named user `X` reverting at least one edit by a named user `Y ≠ X` (`Reverted edit pairs` gives the filtered `(R, E)` count alongside it).

| # | Article (`page_id`) | Exchanges (`R`) | Reverted edit pairs | Distinct named users | Busiest `YYYY-MM` (exchanges) | Example exchange diff |
|---:|---|---:|---:|---:|---|---|
| 1 | Tarja Halonen (`1187`) | 155 | 176 | 165 | `2008-02` (13) | [diff=4334779](https://fi.wikipedia.org/w/index.php?diff=4334779) |
| 2 | Sauli Niinistö (`1137`) | 134 | 152 | 141 | `2012-02` (14) | [diff=11334568](https://fi.wikipedia.org/w/index.php?diff=11334568) |
| 3 | Suomen tasavallan presidentti (`1069`) | 86 | 122 | 86 | `2012-02` (6) | [diff=11241518](https://fi.wikipedia.org/w/index.php?diff=11241518) |
| 4 | Pekka Haavisto (`18561`) | 83 | 247 | 110 | `2012-01` (24) | [diff=11203227](https://fi.wikipedia.org/w/index.php?diff=11203227) |
| 5 | Sanna Marin (`1121620`) | 82 | 98 | 88 | `2022-08` (9) | [diff=20746279](https://fi.wikipedia.org/w/index.php?diff=20746279) |
| 6 | Suomen sisällissota (`1150`) | 81 | 106 | 86 | `2008-03` (6) | [diff=4534105](https://fi.wikipedia.org/w/index.php?diff=4534105) |
| 7 | Mikael Agricola (`7314`) | 72 | 89 | 84 | `2007-04` (7) | [diff=2747394](https://fi.wikipedia.org/w/index.php?diff=2747394) |
| 8 | Talvisota (`1256`) | 71 | 119 | 82 | `2008-01` (11) | [diff=4187002](https://fi.wikipedia.org/w/index.php?diff=4187002) |
| 9 | Martti Ahtisaari (`5901`) | 68 | 75 | 81 | `2008-10` (6) | [diff=5688375](https://fi.wikipedia.org/w/index.php?diff=5688375) |
| 10 | Urho Kekkonen (`1275`) | 67 | 84 | 87 | `2023-10` (4) | [diff=21948891](https://fi.wikipedia.org/w/index.php?diff=21948891) |
| 11 | Timo Soini (`41622`) | 62 | 84 | 90 | `2019-11` (3) | [diff=18556046](https://fi.wikipedia.org/w/index.php?diff=18556046) |
| 12 | Alexander Stubb (`11396`) | 57 | 93 | 84 | `2024-02` (7) | [diff=22208336](https://fi.wikipedia.org/w/index.php?diff=22208336) |
| 13 | Carl Gustaf Emil Mannerheim (`6021`) | 57 | 71 | 86 | `2010-06` (4) | [diff=8601058](https://fi.wikipedia.org/w/index.php?diff=8601058) |
| 14 | Juha Sipilä (`926229`) | 53 | 81 | 69 | `2019-03` (5) | [diff=17992106](https://fi.wikipedia.org/w/index.php?diff=17992106) |
| 15 | Ilkka Kanerva (`10907`) | 52 | 79 | 49 | `2008-03` (29) | [diff=4560915](https://fi.wikipedia.org/w/index.php?diff=4560915) |
| 16 | Axel Olof Freudenthal (`200797`) | 51 | 85 | 28 | `2006-07` (30) | [diff=1427534](https://fi.wikipedia.org/w/index.php?diff=1427534) |
| 17 | Paavo Lipponen (`942`) | 50 | 243 | 127 | `2014-03` (6) | [diff=13920545](https://fi.wikipedia.org/w/index.php?diff=13920545) |
| 18 | Matti Vanhanen (`795`) | 50 | 70 | 62 | `2006-01` (7) | [diff=682902](https://fi.wikipedia.org/w/index.php?diff=682902) |
| 19 | Jatkosota (`440`) | 46 | 111 | 43 | `2015-12` (11) | [diff=15412542](https://fi.wikipedia.org/w/index.php?diff=15412542) |
| 20 | Kalevala (`498`) | 45 | 63 | 35 | `2007-01` (14) | [diff=2258829](https://fi.wikipedia.org/w/index.php?diff=2258829) |
| 21 | Jokelan koulusurmat (`436339`) | 45 | 57 | 66 | `2007-11` (26) | [diff=3805190](https://fi.wikipedia.org/w/index.php?diff=3805190) |
| 22 | Saamelaiset (`7283`) | 40 | 69 | 51 | `2021-09` (4) | [diff=20025697](https://fi.wikipedia.org/w/index.php?diff=20025697) |
| 23 | Karjala (`6626`) | 39 | 50 | 20 | `2008-05` (17) | [diff=4828869](https://fi.wikipedia.org/w/index.php?diff=4828869) |
| 24 | Johannes Virolainen (`439`) | 37 | 47 | 24 | `2009-01` (14) | [diff=6149157](https://fi.wikipedia.org/w/index.php?diff=6149157) |
| 25 | Suomen Kansa Ensin (`1445320`) | 36 | 58 | 31 | `2018-11` (9) | [diff=17713858](https://fi.wikipedia.org/w/index.php?diff=17713858) |
| 26 | Suur-Suomi (`24094`) | 36 | 55 | 33 | `2005-08` (13) | [diff=398671](https://fi.wikipedia.org/w/index.php?diff=398671) |
| 27 | Katri Kulmuni (`1257152`) | 36 | 38 | 28 | `2021-12` (21) | [diff=20167809](https://fi.wikipedia.org/w/index.php?diff=20167809) |
| 28 | Päivi Räsänen (`57242`) | 35 | 56 | 44 | `2010-10` (8) | [diff=9260527](https://fi.wikipedia.org/w/index.php?diff=9260527) |
| 29 | Simo Häyhä (`24889`) | 35 | 43 | 51 | `2009-12` (3) | [diff=7717839](https://fi.wikipedia.org/w/index.php?diff=7717839) |
| 30 | Lestadiolaisuus (`5759`) *(tied at 33 with Paavo Väyrynen `20040`: 47 pairs, 49 users, `2019-04`)* | 33 | 50 | 50 | `2010-03` (5) | [diff=8133863](https://fi.wikipedia.org/w/index.php?diff=8133863) |

### 6.2 Top 20 Named User Pairs by Mutual Reverts (`R.bytes > 0`, `r_user <> e_user`, `A → B ≥ 1` & `B → A ≥ 1`)
Ranks 1–20 give the `R.bytes > 0` filtered Top 20; ranks 21–23 are appended so the full Top 20 **human-vs-human** pairs remain visible once the 3 bot-involved pairs (#2, #5, #6) are excluded.

| # | User A | User B | A reverted B | B reverted A | Total mutual reverts | Reverted edit pairs | Shared articles |
|---:|---|---|---:|---:|---:|---:|---:|
| 1 | `Laurikainen` | `Risukarhi` | 2 | 61 | **63** | 233 | 33 |
| 2 | `Suuntaa antava bot` *(bot)* | `Vesteri` | 3 | 51 | **54** | 54 | 51 |
| 3 | `Inzulac` | `Joonasl` | 19 | 22 | **41** | 67 | 10 |
| 4 | `Joonasl` | `Seppoilmari` | 38 | 3 | **41** | 49 | 11 |
| 5 | `Addbot` *(bot)* | `EmausBot` *(bot)* | 38 | 1 | **39** | 39 | 39 |
| 6 | `InternetArchiveBot` *(bot)* | `Risukarhi` | 6 | 30 | **36** | 36 | 24 |
| 7 | `ML` | `SM` | 18 | 7 | **25** | 31 | 9 |
| 8 | `Petri Krohn` | `Ulrika` | 9 | 15 | **24** | 31 | 14 |
| 9 | `Anr` | `Omitti86` | 21 | 2 | **23** | 24 | 22 |
| 10 | `Inzulac` | `ML` | 11 | 11 | **22** | 30 | 11 |
| 11 | `Kalamies` | `Thule` | 6 | 14 | **20** | 32 | 3 |
| 12 | `ML` | `Seppoilmari` | 17 | 3 | **20** | 26 | 7 |
| 13 | `Mikko Paananen` | `WPK` | 10 | 8 | **18** | 27 | 4 |
| 14 | `Lito` | `Otrfan` | 5 | 13 | **18** | 22 | 8 |
| 15 | `Htm` | `Nenoniel` | 15 | 3 | **18** | 29 | 12 |
| 16 | `Kyzyl` | `PekKos` | 12 | 5 | **17** | 29 | 6 |
| 17 | `ML` | `Thule` | 8 | 8 | **16** | 20 | 6 |
| 18 | `Jniemenmaa` | `PekKos` | 9 | 7 | **16** | 38 | 6 |
| 19 | `Jniemenmaa` | `Lito` | 15 | 1 | **16** | 16 | 15 |
| 20 | `Urjanhai` | `Zache` | 15 | 1 | **16** | 17 | 16 |
| 21 | `Otrfan` | `Pxos` | 1 | 14 | **15** | 15 | 14 |
| 22 | `MPorciusCato` | `PekKos` | 8 | 6 | **14** | 19 | 4 |
| 23 | `Kielimiliisi` | `Tureku` | 13 | 1 | **14** | 33 | 13 |

---

## Caveats & Implementation Pitfalls for P1.S4 / P1.S5 / P1.S6

1. **Zero-byte blanking (`R.bytes = 0`), self-reverts, & long-span redirect SHA-1 collisions:** Every empty page shares one SHA-1 (`phoiac9h4m842xq45sp7s6u21eteeq1`), so **34** `R.bytes = 0` reverts sweep **3,391** distinct edits (`4,567` `(R, E)` pairs), while **3,657** canonical reverts are pure self-reverts and non-empty redirect merges can also span hundreds of edits (e.g. *K. J. Ståhlberg*: `12` events vs `283` pairs from a `span = 382` redirect). **Action:** In P1.S4/P1.S6, count distinct revert events `R`, filter `r_bytes > 0` and `r_user <> e_user`, exclude bots (`Addbot` = `215` interwiki pseudo-reverts), ignore null/log revisions inside reverted runs (`839` revisions, incl. `649` `VakauttajaBot` entries), and cap revert radius (`span <= 15`).
2. **MariaDB accent-insensitive default collation (`utf8mb4_uca1400_ai_ci`):** On `corpus.revision`, `'väärä' = 'vaara'` evaluates to `TRUE` under the column's default collation (matching *vaara* = hill/danger) and 6 distinct username pairs (`Laihajarvi`/`Laihajärvi`, `TeeVee`/`Teevee`, etc.) collide. Always apply `COLLATE utf8mb4_uca1400_as_ci` for Finnish text matching and `COLLATE utf8mb4_bin` for exact MediaWiki usernames.
3. **No-op log revisions (`imm_prev_sha1 = sha1`):** `5,796` revisions (page moves `siirsi sivun ...` and `VakauttajaBot` protection logs) have the exact same `sha1` as their immediate predecessor (`923` on pages where that `sha1` had also appeared earlier). Requiring `p_seq < r_seq - 1` on `LAG(seq) OVER (PARTITION BY page_id, sha1 ORDER BY seq)` avoids misclassifying those null edits as reverts.
4. **Sub-corpus scope for single-article counts:** Single-article editor shares (`63.77%` of named accounts) reflect activity inside the 19,742-article *Suomen historia* corpus, not across all of `fi.wikipedia.org`.
