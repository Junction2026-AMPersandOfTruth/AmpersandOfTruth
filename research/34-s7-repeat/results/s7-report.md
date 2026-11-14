# P1.S7 (#34) · Re-insertion & Cross-Article Repetition Cluster Report

Scope: `171` dev-split golden articles across `11` dev topics (tables built over `245` total golden pages; `s3_diff` distinct pages at build time: `245`, missing `s3_diff` span rows: `0`).

## 1. Cluster Counts on Dev Golden Pages (`exact` vs `fuzzy`)

### By Cluster Size & Structural Properties

| Metric (clusters touching dev golden pages) | `exact` | `fuzzy` (exact + MinHash/Jaccard ≥ 0.6) | Δ (`fuzzy - exact`) |
|---|---:|---:|---:|
| Size `1 (singleton)` | 2,322 | 2,306 | -16 |
| Size `2` | 147 | 145 | -2 |
| Size `3–4` | 54 | 57 | +3 |
| Size `5–9` | 22 | 24 | +2 |
| Size `10+` | 8 | 8 | +0 |
| **Total multi-unit clusters (`n_units >= 2`)** | 231 | 234 | +3 |
| **Total clusters (all sizes)** | 2,553 | 2,540 | -13 |
| Member units in touching clusters | 3,054 | 3,055 | +1 |
| Multi-account (`n_accounts >= 2`, IP `/16`/`/32` masked) | 109 | 112 | +3 |
| Cross-article (`n_pages >= 2`) | 14 | 15 | +1 |
| Persistent (`n_units >= 3` and `span_days >= 7`) | 40 | 44 | +4 |
| Sleeper gap (`max_gap_days >= 30`) | 51 | 55 | +4 |
| Long sleeper gap (`max_gap_days >= 180`) | 33 | 36 | +3 |

### By `campaign_signals` Combination (Dev Golden Pages)

| `campaign_signals` | `exact` | `fuzzy` | Δ |
|---|---:|---:|---:|
| `(none)` | 2,430 | 2,412 | -18 |
| `accounts` | 73 | 73 | +0 |
| `articles` | 7 | 8 | +1 |
| `persistence` | 7 | 8 | +1 |
| `accounts,articles` | 3 | 3 | +0 |
| `accounts,persistence` | 29 | 32 | +3 |
| `articles,persistence` | 0 | 0 | +0 |
| `accounts,articles,persistence` | 4 | 4 | +0 |
| **Meets Level-3 structural coordination bar (`>= 2` signals incl. `accounts` or `articles`)** | 36 | 39 | +3 |

## 2. Alignment with Dev Golden Multi-Insertion & Cross-Article Incidents

- **Dev `contested`/`campaign` items with `>= 2` inserting revisions:** `34` total (`24` span `>= 2` separate S1 revert episodes; `10` are multi-edit runs reverted in a single S1 span).
- **Re-insertion cluster found (`>= 2` of the item's inserting revs in one cluster):** `exact` **33/34** (97.1%), `fuzzy` **33/34** (97.1%).
  - On **true multi-revert items (`>= 2` S1 spans)**: `exact` **23/24** (95.8%), `fuzzy` **23/24** (95.8%). *(The sole unmerged item, `ceded-karelia/i10`, was substantially rewritten between spans `351793` and `392863`, yielding 5-shingle Jaccard `0.086 < 0.6`.)*
  - On **single-span multi-edit items (`1` S1 span)**: `exact` **10/10** (100%), `fuzzy` **10/10** (100%).
- **Cross-article golden check:** `0` dev items carry explicit `other_page_ids` in `golden.json` (topics were labelled per-topic), **but S7 automatically discovered and linked cross-topic twin items** `civil-war-1918/i10` (Mannerheim, page `6021`) and `continuation-war/i06` (Risto Ryti, page `11398`) into a single 2-article, 2-account cluster (`#3382`, `7/7` combined inserting revisions).

| Golden item | Label | Page | Ins revs | S1 spans | `exact` best overlap | `fuzzy` best overlap | `fuzzy` cluster (`n_units`, `n_pages`, `n_accounts`, signals) |
|---|---|---:|---:|---:|---:|---:|---|
| `civil-war-1918/i06` | `contested` | 1150 | 8 | 2 | 8/8 (yes) | 8/8 (yes) | `#3506` (`u=2, p=1, a=1`, `none`) |
| `civil-war-1918/i07` | `contested` | 1150 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3404` (`u=3, p=1, a=1`, `none`) |
| `civil-war-1918/i09` | `contested` | 6021 | 5 | 3 | 2/5 (yes) | 2/5 (yes) | `#5174` (`u=1, p=1, a=1`, `none`) |
| `civil-war-1918/i10` | `contested` | 6021 | 4 | 2 | 4/4 (yes) | 4/4 (yes) | `#3382` (`u=4, p=2, a=2`, `accounts,articles`) |
| `continuation-war/i01` | `contested` | 440 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3442` (`u=2, p=1, a=1`, `none`) |
| `continuation-war/i02` | `contested` | 440 | 6 | 3 | 6/6 (yes) | 6/6 (yes) | `#3393` (`u=3, p=1, a=1`, `none`) |
| `continuation-war/i06` | `contested` | 11398 | 3 | 2 | 3/3 (yes) | 3/3 (yes) | `#3382` (`u=4, p=2, a=2`, `accounts,articles`) |
| `continuation-war/i07` | `contested` | 440 | 2 | 1 | 2/2 (yes) | 2/2 (yes) | `#3863` (`u=1, p=1, a=1`, `none`) |
| `winter-war/i01` | `contested` | 1256 | 4 | 4 | 4/4 (yes) | 4/4 (yes) | `#3375` (`u=4, p=1, a=1`, `none`) |
| `winter-war/i02` | `contested` | 8295 | 3 | 3 | 3/3 (yes) | 3/3 (yes) | `#3360` (`u=5, p=1, a=2`, `accounts`) |
| `winter-war/i03` | `contested` | 1256 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3475` (`u=2, p=1, a=2`, `accounts`) |
| `winter-war/i04` | `contested` | 24889 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3504` (`u=2, p=1, a=1`, `none`) |
| `ceded-karelia/i01` | `contested` | 19086 | 10 | 1 | 10/10 (yes) | 10/10 (yes) | `#3715` (`u=1, p=1, a=1`, `none`) |
| `ceded-karelia/i02` | `contested` | 19086 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3461` (`u=2, p=1, a=1`, `none`) |
| `ceded-karelia/i07` | `contested` | 6626 | 6 | 3 | 3/6 (yes) | 3/6 (yes) | `#3339` (`u=7, p=1, a=2`, `accounts`) |
| `ceded-karelia/i08` | `contested` | 6626 | 4 | 4 | 4/4 (yes) | 4/4 (yes) | `#3344` (`u=6, p=1, a=2`, `accounts,persistence`) |
| `ceded-karelia/i09` | `contested` | 6626 | 6 | 6 | 6/6 (yes) | 6/6 (yes) | `#3347` (`u=6, p=1, a=2`, `accounts`) |
| `ceded-karelia/i10` | `contested` | 24094 | 2 | 2 | 1/2 (no) | 1/2 (no) | `#3338` (`u=7, p=1, a=4`, `accounts`) |
| `ceded-karelia/i11` | `contested` | 24094 | 2 | 1 | 2/2 (yes) | 2/2 (yes) | `#3676` (`u=1, p=1, a=1`, `none`) |
| `ceded-karelia/i13` | `contested` | 24094 | 5 | 2 | 5/5 (yes) | 5/5 (yes) | `#3617` (`u=2, p=1, a=1`, `none`) |
| `nato-debate/i01` | `campaign` | 2979 | 7 | 5 | 7/7 (yes) | 7/7 (yes) | `#3353` (`u=5, p=1, a=3`, `accounts,persistence`) |
| `nato-debate/i02` | `contested` | 128998 | 3 | 1 | 3/3 (yes) | 3/3 (yes) | `#4054` (`u=1, p=1, a=1`, `none`) |
| `nato-debate/i03` | `contested` | 128998 | 13 | 1 | 13/13 (yes) | 13/13 (yes) | `#4199` (`u=1, p=1, a=1`, `none`) |
| `finlandization/i03` | `contested` | 1187 | 3 | 1 | 3/3 (yes) | 3/3 (yes) | `#4935` (`u=1, p=1, a=1`, `none`) |
| `finlandization/i04` | `contested` | 942 | 3 | 2 | 3/3 (yes) | 3/3 (yes) | `#3610` (`u=2, p=1, a=1`, `none`) |
| `finlandization/i06` | `contested` | 20742 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3447` (`u=2, p=1, a=2`, `accounts`) |
| `austerity-politics/i04` | `contested` | 1121620 | 2 | 1 | 2/2 (yes) | 2/2 (yes) | `#6131` (`u=1, p=1, a=1`, `none`) |
| `austerity-politics/i12` | `contested` | 9059 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3500` (`u=2, p=1, a=1`, `none`) |
| `austerity-politics/i15` | `contested` | 926229 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3569` (`u=2, p=1, a=2`, `accounts`) |
| `russification-independence/i01` | `contested` | 10758 | 2 | 2 | 2/2 (yes) | 2/2 (yes) | `#3511` (`u=2, p=1, a=1`, `none`) |
| `russification-independence/i02` | `contested` | 10758 | 2 | 1 | 2/2 (yes) | 2/2 (yes) | `#5079` (`u=1, p=1, a=1`, `none`) |
| `russification-independence/i04` | `contested` | 96542 | 2 | 1 | 2/2 (yes) | 2/2 (yes) | `#3521` (`u=2, p=1, a=1`, `none`) |
| `russification-independence/i05` | `contested` | 11517 | 2 | 1 | 2/2 (yes) | 2/2 (yes) | `#6476` (`u=1, p=1, a=1`, `none`) |
| `border-crisis-2023/i01` | `contested` | 1060965 | 3 | 3 | 3/3 (yes) | 3/3 (yes) | `#3426` (`u=3, p=1, a=2`, `accounts`) |

## 3. Ten Multi-Unit Clusters on Dev Pages (Campaign Matches & False-Cluster Archetypes)

1. **`#3353` [golden-campaign-match]** · `n_units=5`, `n_pages=1`, `n_accounts=3`, `span=21.9d`, `max_gap=13.1d`, signals=`accounts,persistence` · Articles: Luettelo Suomen historian sodista (`2979`)
   > `2002– Afganistanin sota: Suomi on osallistunut sotatoimiin Naton alaisissa ISAF-joukoissa Afganistanin sisällissodassa vuodesta 2002. Vuonna 2006 Nato-johtoinen ISAF-operaatio yhdistettiin samanaikaisen Yhdysvaltojen Enduring Freedom-operaation kanssa, jonka tavoitteena on kukistaa islamilaisten Ta…`
2. **`#3382` [cross-article-claim]** · `n_units=4`, `n_pages=2`, `n_accounts=2`, `span=1.4d`, `max_gap=0.6d`, signals=`accounts,articles` · Articles: Risto Ryti (`11398`), Carl Gustaf Emil Mannerheim (`6021`)
   > `Tasavallan presidentti ( ) 4 www.kysy.fi/kysymys/yhteiskunta-politiikka-tasavallan-presidenttien-toimikausien-virallinen-alku-ja-loppu Tasavallan presidenttien toimikausien virallinen alku ja loppu? Yhteiskunta, politiikka; Aiheet; Kysy.fi - kysy mitä vain!; Helsingin kaupunginkirjasto www.presiden…`
3. **`#3441` [cross-article-sleeper]** · `n_units=2`, `n_pages=2`, `n_accounts=2`, `span=37.8d`, `max_gap=37.8d`, signals=`accounts,articles` · Articles: Karjala-kysymys (`19086`), Tarja Halonen (`1187`)
   > `23.3.1993 yhdistysrekisteri rekisteröi Suur-Suomi r.y:n ja myöhemmin IKL:n mikä herätti mm. Venäjän federaation ja entisen Neuvostoliiton suurlähettiläs Juri Derjabinin ärtymystä ja lähetti nootin, missä vaati ko. yhdistysten lakkauttamista (jag kräver att dessa organisationer måste slutas) varsink…`
4. **`#3328` [cross-article-transitive-chain]** · `n_units=10`, `n_pages=3`, `n_accounts=7`, `span=5393.7d`, `max_gap=3361.6d`, signals=`accounts,articles,persistence` · Articles: Alexander Stubb (`11396`), Paavo Lipponen (`942`), Timo Soini (`41622`)
   > `Kielikysymykset Stubb esitti huhtikuussa 2005, että EU kokoukset pidetään englanniksi ja ranskaksi. Vanhemmat kollegat nauroivat. EU puheenjohtajuuden alussa Stubb ei järjestänyt tulkkausta saksaksi. Sen vuoksi Saksa boikotoi ministerikokousta Suomessa kesäkuussa 2006. Alexander Stubb Alaston totuu…`
5. **`#3320` [persistent-single-article]** · `n_units=20`, `n_pages=1`, `n_accounts=17`, `span=3897.4d`, `max_gap=1065.6d`, signals=`accounts,persistence` · Articles: Sauli Niinistö (`1137`)
   > `Eduskuntavaaleissa häntä tuki mm. Kehittyvien maakuntien Suomi ry 15 000 €:lla. Niinistö kielsi tienneensä kuka tai ketkä yhdistyksen takana olivat. Myöhemmin hän oli mukana anomassa yhdistyksen taustalla olleelle Kyösti Kakkoselle vuorineuvoksen arvonimeä vv/http://www.iltasanomat.fi/uutiset/kotim…`
6. **`#3363` [fuzzy-reworded-merge]** · `n_units=5`, `n_pages=1`, `n_accounts=3`, `span=1586.9d`, `max_gap=857.1d`, signals=`accounts,persistence` · Articles: Tarja Halonen (`1187`)
   > `Harja Talonen Verkkoviite / Osoite = https://yle.fi/aihe/artikkeli/2012/03/01/presidenttien-virkaanastujaiset-stahlbergista-niinistoon#media=24928 / Nimeke = Presidenttien virkaanastujaiset / Tekijä = Himberg, Petra / Selite = Harja Talonen astuu virkaansa 2006 /Julkaisu = Elävä arkisto / Julkaisij…`
7. **`#3394` [false-cluster:toolbar-boilerplate]** · `n_units=3`, `n_pages=3`, `n_accounts=3`, `span=657.0d`, `max_gap=499.0d`, signals=`accounts,articles,persistence` · Articles: Tarja Halonen (`1187`), Suomen sisällissota (`1150`), Kalevala (`498`)
   > `sosia Otsikkoteksti Lihavoitu tekstiLisää muotoilematon teksti tähän--[masked-sig] 15. toukokuuta 2006 kello 10.31 (UTC) Lisää muotoilematon teksti tähän Otsikkoteksti`
8. **`#3405` [false-cluster:see-also-phrase]** · `n_units=3`, `n_pages=3`, `n_accounts=1`, `span=0.0d`, `max_gap=0.0d`, signals=`articles` · Articles: Suvi Lindén (`12639`), Sauli Niinistö (`1137`), Jyrki Katainen (`10858`)
   > `Vaalirahoitus Katso Kehittyvien maakuntien Suomi`
9. **`#3323` [false-cluster:unclosed-ref-swallow]** · `n_units=17`, `n_pages=1`, `n_accounts=13`, `span=5422.5d`, `max_gap=2230.5d`, signals=`accounts,persistence` · Articles: Talvisota (`1256`)
   > `Haataja 1989.myös termi kives tuli tällöin tunnetuksi ==Sodan alku== thumb/right/ Neuvostoliiton hyökkäysreitit. Neuvottelujen päätyttyä marraskuussa tuloksettomina Neuvostoliiton poliittinen painostus Suomea kohtaan lisääntyi. Lopulta Neuvostoliitto lavasti 26. marraskuuta niin sanotut Mainilan la…`
10. **`#3334` [false-cluster:pasted-ui-chrome]** · `n_units=8`, `n_pages=1`, `n_accounts=8`, `span=4528.9d`, `max_gap=2932.0d`, signals=`accounts,persistence` · Articles: Suomen sisällissota (`1150`)
   > `Suomen sisällissota Wikipedia. Loikkaa: valikkoon, hakuun Tämä artikkeli on suositeltu sivu. Suomen sisällissota Tampereen kaupunki tuhoutui sisällissodassa miltei täysin Sota alkoi: 27. tammikuuta 1918 Sota loppui: 14. toukokuuta 1918 Paikka: Suomi Tulos: Punaiset hävisivät, valkoiset voittivat. V…`

## 4. Boilerplate Guard (`> 8` Distinct Pages) & Top Cross-Page Sentence Hashes

Across the `245` golden pages, the maximum spread of any normalised sentence (`>= 30` chars) was `4` distinct pages (`0` hashes exceeded the `> 8` distinct-page boilerplate cutoff on this 245-page subset; on the full 19,742-article corpus, toolbar/chrome phrases like rank #6 below cross `> 8` pages).

| Rank | Hash | Distinct pages | Units | Guard status | Masked normalised sentence (`>= 30` chars) |
|---:|---|---:|---:|---|---|
| 1 | `ddf9843fc3c0524a` | 4 | 4 | linked (<=8 pages) | `ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ` |
| 2 | `fa5ac4192d2d7d53` | 3 | 4 | linked (<=8 pages) | `ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ｓｕｏｍｉ ｏｎ ｎｙｔ ａｉｄｓ ｌａｎｄｉａ ` |
| 3 | `0101b76293080be3` | 3 | 3 | linked (<=8 pages) | `lehtiviite tekijä hämäläinen unto otsikko populisti ja pelimies soini kertooesikuvanaan hän pitää veikko vennamo a` |
| 4 | `134a15efe08242f4` | 3 | 3 | linked (<=8 pages) | `hän on osallistunut bilderbergin kokoukseen` |
| 5 | `2d69ab4e7257863f` | 3 | 3 | linked (<=8 pages) | `katso kehittyvien maakuntien suomi` |
| 6 | `50a1964ecee67ed0` | 3 | 3 | linked (<=8 pages) | `lisää muotoilematon teksti tähän` |
| 7 | `0f1d84f6839aa9c4` | 2 | 6 | linked (<=8 pages) | `poronhoidon sekä saamelaiset että suomenkieliset aloittivat 1600 luvulla` |
| 8 | `27a8b11ca5c809e3` | 2 | 6 | linked (<=8 pages) | `verkkoviite osoite https suomenkuvalehti fi jutut kotimaa sortoa sanovat saamelaiset jo usein siita tullut vahan pakkomielle nimeke me sortajat selite sivut 31 34 26 35 8 2017` |
| 9 | `58f93ccd4ca0a098` | 2 | 6 | linked (<=8 pages) | `artikkelin haastatellut tutkijat ovat itä suomen yliopiston arktisten alueiden ja suomen historian professori maria lähteenmäki helsingin yliopiston arkeologian professori mika lavento ja oulun yliopi` |
| 10 | `8b41f109639c91d3` | 2 | 6 | linked (<=8 pages) | `tuoreimmalla vuosituhannella saamelaiset ja saamen kieli eriytyivät vähitellen suomalaisista` |
| 11 | `a12d0fc6b822109b` | 2 | 6 | linked (<=8 pages) | `http www vapaamielistenklubi fi lapuanliike htm` |
| 12 | `ed8fc87bef2f4b0e` | 2 | 6 | linked (<=8 pages) | `neljäntuulenlakin saamelaiset omaksuivat venäläisiltä merimiehiltä 1800 luvulla` |
| 13 | `32e9fe6088b7b6bc` | 2 | 5 | linked (<=8 pages) | `suomalaisen kirjallisuuden seura helsinki 1978` |
| 14 | `7828c2c52f6983e7` | 2 | 5 | linked (<=8 pages) | `suomen historian aikakirja s 123` |
| 15 | `45a19994fb0f3ce1` | 2 | 4 | linked (<=8 pages) | `www presidentti fi public default aspx nodeid 44819 presidentin valinta asema ja tehtävät suomen tasavallan presidentti` |

## 5. Runtime & Pair Counts (`exact` vs `fuzzy`)

| Stage / Metric | Value |
|---|---:|
| Qualifying units (`>= 30` normalised prose chars) | 4,012 |
| Corpus diff load time (compressed `s3_diff` batches) | 133.24 s |
| **Exact** unique sentence hashes (`>= 30` chars) | 12,882 |
| **Exact** boilerplate hashes blocked (`> 8` pages) | 0 |
| **Exact** pair links / union merges | 5,021 / 694 |
| **Exact** clustering compute time | 0.294 s |
| **Fuzzy** units with `>= 5` words (5-shingled) | 3,635 |
| **Fuzzy** unique word 5-shingles | 160,574 |
| **Fuzzy** LSH candidate pairs (64 perms, 32×2 bands) | 1,450 |
| **Fuzzy** verified pairs (true Jaccard `>= 0.6`) | 829 |
| **Fuzzy** additional component merges beyond exact | 16 |
| **Fuzzy** MinHash + LSH + Jaccard compute time | 2.146 s |
| MariaDB table write time (`s7_cluster` + `s7_member`) | 82.91 s |
| Total `cluster.py` wall time | 224.71 s |

## 6. Observed False-Cluster Mechanisms (Guidance for P1.S8 / P1.S10)

1. **Unclosed `<ref>` tags swallowing existing prose (`#3323`):** when unrelated vandals break a `</ref>` tag on a large article, `wikidiff` attributes the swallowed section prose to an added `reference` segment, linking unrelated edits over years. *Mitigation for S8:* ignore `reference` segments containing section headings (`== ... ==`) or exceeding ~1,000 chars.
2. **MediaWiki UI / toolbar strings (`#3394`, `#3334`):** novice editors click toolbar buttons (`Lisää muotoilematon teksti tähän`, `Otsikkoteksti Lihavoitu teksti`) or paste rendered Wikipedia chrome (`Loikkaa: valikkoon, hakuun`). *Mitigation:* explicit short stoplist alongside the `> 8` page boilerplate guard.
3. **Routine navigation links & shared citations (`#3483`, `#3328`):** `Katso myös ...` cross-links or a single shared citation (`Lehtiviite`) can transitively bridge distinct politician articles under single-linkage union-find.
4. **Cross-article vandalism (`#3359`):** coordinated spam exhibits genuine `accounts,articles,persistence` signals, confirming why `docs/evidence-standard.md`'s asymmetry rule requires P1.S9 content classification (`isClaim`) before promoting to Level 2 or 3.
