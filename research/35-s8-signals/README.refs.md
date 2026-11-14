# P1.S8 (#35) Part 1 — Reference, Source-Domain & Maintenance-Template Signals (`refs`)

Evaluates whether reference-side edit signals separate reverted edits (`n = 150`, including `84` non-graffiti `dispute` / `edit_war` / `other` edits and `66` `graffiti_cleanup` edits) from non-reverted control edits (`n = 150`) on the shared S8 candidate set (`~/.cache/aot-research/s8/candidates.jsonl`).

## Files & How to Run

| File | Purpose |
|---|---|
| `detect_citation_removal.py` | `citation_removed` — `<ref>` deleted while surrounding sentence stays or is altered (paragraph survives, not blanking/reformat/swap). |
| `detect_citation_swap.py` | `citation_swapped` — `<ref>` replaced in the same paragraph by a different source (`old → new` domain/title). |
| `detect_unsourced_assertion.py` | `unsourced_addition` — `>= 80` chars of substantive novel prose added with no citation in a paragraph/section whose parent had citations. |
| `detect_domain.py` | `source_domain` — newly added URLs checked against `domains.json`, plus `score_candidate_domains()` for data-driven revert rates. |
| `domains.json` | Short, sourced list (`33` domains) across `state_media`, `known_disinfo`, `blog_forum`, and `self_published`. |
| `detect_templates.py` | `maintenance_template` — Finnish dispute/verification templates added or removed; flags silent section-isolated removal as the **strong** variant. |
| `test_*.py` | Pure stdlib unit tests (`26` tests across all 5 detectors). |
| `run_refs.py` | Applies all 5 detectors to `candidates.jsonl` and writes `results/refs.jsonl` (`rev_id, group, signals`). |
| `results/refs.jsonl` | Per-candidate signal output (`300` rows, no wikitext, no editor/comment fields). |

```bash
# Run unit tests (pure Python stdlib, < 10 ms)
python3 -m unittest discover -s research/35-s8-signals -v

# Recompute results/refs.jsonl and summary table from cached candidates
python3 research/35-s8-signals/run_refs.py
```

---

## Summary Results (`300` Candidates: `150` Reverted vs `150` Control)

Also shown: hit rate on the `84` **non-graffiti** reverted edits (`42` `dispute` + `37` `edit_war` + `5` `other`), which are the subtle/contested edits size-based or vandalism filters struggle with.

| Detector (`kind`) | Reverted (`n=150`) | Control (`n=150`) | Lift (`rev / ctl`) | Non-graffiti reverted (`n=84`) | Non-graffiti lift | Recommendation |
|---|---:|---:|---:|---:|---:|---|
| `source_domain` | 6 (`4.0%`) | 1 (`0.7%`) | **6.00x** | 5 (`6.0%`) | **8.93x** | **Keep (high precision)** |
| `citation_removed` | 3 (`2.0%`) | 1 (`0.7%`) | **3.00x** | 2 (`2.4%`) | **3.57x** | **Keep (high value; control hit is a real un-SHA1-reverted edit)** |
| `citation_swapped` | 2 (`1.3%`) | 1 (`0.7%`) | **2.00x** | 0 (`0.0%`) | 0.00x | **Keep as compound signal** (pair with `source_domain` or number/claim edits) |
| `unsourced_addition` | 15 (`10.0%`) | 9 (`6.0%`) | **1.67x** | 9 (`10.7%`) | **1.79x** | **Keep (low weight / triage booster)** — highest recall |
| `maintenance_template` | 7 (`4.7%`) | 6 (`4.0%`) | **1.17x** | 4 (`4.8%`) | **1.19x** | **Split**: keep **strong** silent removal & article-level dispute banners; drop routine inline `{{lähde}}` additions |
| **Any `refs` signal** | **30 (`20.0%`)** | **16 (`10.7%`)** | **1.88x** | **19 (`22.6%`)** | **2.12x** | — |

---

## Per-Detector Findings, Diff Examples & False-Positive Patterns

*(Note: where a group has fewer than 5 hits across the 150 candidates, every hit in that group is listed below.)*

### 1. `citation_removed` — 3 reverted (`2.0%`) vs 1 control (`0.7%`), lift `3.00x`

Because `WikitextDiff` treats `<ref>…</ref>` as an atomic token, any 1-character punctuation or template edit inside a `<ref>` emits a `removed` + `added` reference pair (`32` candidates have a raw `removed` reference segment, `20` in `control` vs `12` in `reverted`!). Filtering out whole-paragraph deletions (`new_paragraph == null`), page/section blanking, source-preserving reformatting/shortening (`<ref name="YLE">…</ref>` → `<ref name="YLE"/>`), and clean whole-sentence deletions leaves **4** true citation-removal edits:

**Reverted hits (all 3 of 3):**
1. [`diff=13792608`](https://fi.wikipedia.org/w/index.php?diff=13792608) (*Jatkosota*, `edit_war`) — Rewrote the 1941 annexation passage into a single unqualified claim (*"eduskunta liitti talvisodassa menetetyt alueet takaisin Suomeen palauttaen Tarton rauhan rajat"*), deleting the sourced territorial exceptions and their footnote.
   - Evidence: `<ref name=korsu>[http://www.uskokemppi.fi/korsu.htm Korsusta Korsuun, Kiestingin ja Uhtuan suuntain korsu- ja marssilauluja] Usko Kemppi Seura</ref>`
2. [`diff=9615882`](https://fi.wikipedia.org/w/index.php?diff=9615882) (*Karjala*, `edit_war`) — Rewrote the definitions of Western and Eastern Karelia across paragraphs 3–7, preserving `<ref name="venäjänkarjala"/>` but dropping the Kotus terminological citation `<ref name="mistäpuhutaan"/>` from the Russian Karelia paragraph.
   - Evidence: `<ref name="mistäpuhutaan"/>`
3. [`diff=4112248`](https://fi.wikipedia.org/w/index.php?diff=4112248) (*Suur-Suomi*, `graffiti_cleanup`) — Altered camp/casualty numerals (`24 → 10`, `6 000–9 000 → 4000`) while stripping the documentary URL from the supporting `<ref>` (and deleting `</ref>`, corrupting downstream markup).
   - Evidence: `<ref>Suur-Suomen muisto -dokumentti http://www.elokuvakontakti.fi/site/?page_id=9&elokuva_id=520</ref>`

**Control hits (all 1 of 1):**
1. [`diff=14098576`](https://fi.wikipedia.org/w/index.php?diff=14098576) (*Mainilan laukaukset*, `control`) — Changed *"Todellisuudessa kyseessä olivat, kuten venäläisetkin ovat myöhemmin myöntäneet,`<ref name=Yle />` neuvostojoukkojen itse ampumat laukaukset"* to *"Kyseessä saattoi olla neuvostojoukkojen itsensä ampumat laukaukset"*, deleting `<ref name=Yle />` with edit summary claiming the Yle link was outdated and did not prove Russian admission.
   - Evidence: `<ref name=Yle />`
   - **Judgement:** Reading the Finnish, **this "control" hit is actually a textbook true positive of subtle POV softening + citation removal** on *Mainilan laukaukset*! It is in `control` only because subsequent editors repaired the lead incrementally rather than performing an exact SHA-1 revert (`S1` only labels exact SHA-1 identity reverts as `reverted`).

**False-positive patterns suppressed / remaining:**
- **Atomic `<ref>` reformatting & archival** (suppressed): minor typography (`1:160 000` → `1:160&nbsp;000` in `diff=18230541`), Harvard shortening (`diff=18360588`), named-ref deduplication (`diff=8769985`), and moving paragraphs between sections (`diff=23890883`, `diff=24235098`) would overwhelm `citation_removed` (`20` control false positives) without source-equivalence matching.
- **Clean sentence pruning** (suppressed): editors removing a duplicate sentence together with its footnote (`diff=15412340`) are editing prose structure, not leaving an existing assertion unsourced.

---

### 2. `citation_swapped` — 2 reverted (`1.3%`) vs 1 control (`0.7%`), lift `2.00x`

**Reverted hits (all 2 of 2):**
1. [`diff=8109372`](https://fi.wikipedia.org/w/index.php?diff=8109372) (*Simo Häyhä*, `graffiti_cleanup`) — Changed Häyhä's sniper count from *"jopa 542 … Kansallisbiografian mukaan luku olisi ollut noin 200"* to *"259"* while replacing the *Kansallisbiografia 2003 / Helsingin Sanomat 2001* citation with wartime periodical citations.
   - Label: `Citation replaced: Kansallisbiografia 2003. Lappalainen/HS 2001… → Hurtti-Ukko 1941`
   - Evidence: `<ref>Hurtti-Ukko 1941</ref>`
2. [`diff=5763248`](https://fi.wikipedia.org/w/index.php?diff=5763248) (*Talvisota*, `graffiti_cleanup`) — Global find-and-replace vandalism (`talvisota → lumisota`) corrupting book titles and `mil.fi` URLs inside 27 footnotes.
   - Label: `Citation replaced: Max Jakobsson: Diplomaattien talvisota . Sei… → Max Jakobsson: Diplomaattien lumisota . Seit…`
   - Evidence: `<ref>Max Jakobsson: "Diplomaattien lumisota". Seitsemäs painos, 2002, s. 61</ref>`

**Control hits (all 1 of 1):**
1. [`diff=14737751`](https://fi.wikipedia.org/w/index.php?diff=14737751) (*Karjala-kysymys*, `control`) — Updated an expired 2006 Ministry of Finance budget figure (`40` → `54` billion) and replaced `"Budjettikatsaus 2006" - Valtiovarainministeriö (linkki ei enää toimiva)` with `"Valtiovarainministeriö 2015"` (while accidentally omitting `</ref>`).
   - Label: `Citation replaced: Budjettikatsaus 2006 - Valtiovarainministeri… → Valtiovarainministeriö 2015 . ProKarelian tu…`
   - Evidence: `<ref>"Valtiovarainministeriö 2015". ProKarelian tutkimusta on kritisoitu: Helsingin Sanomat piti pääkirjoituksessaan 29. kesäkuuta [[2000]] laskelman yhtä lähtökohtaa palautettaville alueille muuttavi`

**False-positive patterns observed:**
- **Wayback Machine wrapping & URL migrations** (`diff=24195874`): routine reference maintenance (`Viitteitä viilattu`) comments out dead URLs (`<!-- http://www.aamulehti.fi/... -->`) and adds `| arkisto = https://web.archive.org/web/.../http://www.aamulehti.fi/...`, or migrates old `hs.fi/kotimaa/artikkeli/...` links to `hs.fi/suomi/art-...` for the same headline, or deletes empty `{{Verkkoviite | Osoite = | Nimeke = | Viitattu = 18.4.2026}}` shells. Unwrapping `web.archive.org`, matching domain + title tokens, and ignoring empty citation shells eliminates these false positives.
- **Dead-link replacement across outlets** (`diff=24195874` paragraph 31): when a combined ref `[http://www.iltasanomat.fi/...; http://grani.ru/...]` was split, the dead *Ilta-Sanomat* link for a 2005 STT wire story was replaced with *Turun Sanomat* (`ts.fi`) carrying the identical headline (*"Venäjän ulkoministeri suomi Karjala-kyselyjen tekijöitä"*).
- **Updating outdated official statistics** (`diff=14737751`): legitimate editors replace `"Budjettikatsaus 2006"` with `"Valtiovarainministeriö 2015"`. Pairing `citation_swapped` with domain reputation (`source_domain`) or LLM claim check separates benign updates from hostile source swaps.

---

### 3. `unsourced_addition` — 15 reverted (`10.0%`) vs 9 control (`6.0%`), lift `1.67x` (`1.79x` non-graffiti)

Scoping to **parent paragraphs/sections that already have citations** (and excluding appendix sections `Lähteet`, `Kirjallisuutta`, `Aiheesta muualla`, `Katso myös`) raises lift from `~1.18x` (article-wide naive check) to `1.67x` overall and `1.79x` on non-graffiti disputes.

**5 reverted examples:**
1. [`diff=16572783`](https://fi.wikipedia.org/w/index.php?diff=16572783) (*Simo Häyhä*, `dispute`) — Added `324` chars claiming Soviet command dispatched elite counter-snipers and heavy artillery specifically against Häyhä, with no source in a heavily cited section.
   - Evidence: `Häyhä oli tarkka-ampujana niin tehokas että neuvostoliiton sodanjohto karjalan rintamalla lähetti parhaimpia tarkka-ampujia Häyhän kimppuun mutta tuloksetta ja yleensä [[Neuvostoliiton]]n omat tarkka-`
2. [`diff=7999913`](https://fi.wikipedia.org/w/index.php?diff=7999913) (*Suomen sisällissota*, `dispute`) — Inserted `314` chars of unsourced ideological interpretation into the lead of a featured article (using `"vapaussota"` framing).
   - Evidence: `Sodan vaikutukset olivat myös paradoksaaliset, sillä itsenäisyyden säilyttämisen lisäksi ne lähensivät monia eri kansanryhmiä toisiinsa, jotka olivat ennen vapaussotaa ideologisesti vastakkainaseteltu`
3. [`diff=7714924`](https://fi.wikipedia.org/w/index.php?diff=7714924) (*Simo Häyhä*, `edit_war`) — Added `611` chars arguing that a 1941 *Hurtti-Ukko* magazine issue is the most reliable source on Häyhä, written as inline prose commentary without `<ref>`.
   - Evidence: `Yksi luotettavimmista Häyhää koskevista tiedoista löytynee vuonna 1941 ilmestuneestä Hurtti Ukko-lehdestä no 1 vuodelta 1941. Siellä on lähes koko sivun mittainen selostus Häyhästä valokuvan kera...` *(truncated to 200 chars in JSON)*
4. [`diff=13962539`](https://fi.wikipedia.org/w/index.php?diff=13962539) (*Viipuri*, `graffiti_cleanup`) — Added `114` chars of editorializing commentary into the cited lead (*"ryöstö saalista"*).
   - Evidence: `Onkin käsittämätöntä, että Venäjä yhä 2010-luvulla pitää tätä 1940-luvun ryöstö saalista, eli Viipuria hallussaan. Kun ottaa huomioon, että Viipuri perustettiin nimenomaan suomalaisten toimesta jo 120`
5. [`diff=7615440`](https://fi.wikipedia.org/w/index.php?diff=7615440) (*Sotasyyllisyysoikeudenkäynti*, `dispute`) — Added `157` chars of procedural dates and arrest assertions with no citation into a referenced section.
   - Evidence: `Hallitus antoi määräyksen sotasyyllisiksi osoitettujen henkilöiden asettamisesta syytteeseen 6. marraskuuta ja heidät vangittiin 11. marraskuuta 1945.`

**5 control examples:**
1. [`diff=4238954`](https://fi.wikipedia.org/w/index.php?diff=4238954) (*Suomen sisällissota*, `control`) — Copy-editing expansion (`113` chars) summarizing red/white terror objectives in an already cited section (`Hiontaa...........`).
   - Evidence: `Terrorin tavoitteena oli sekä vastustajan valtarakenteiden tuhoaminen että voimakkaan pelotevaikutuksen   luominen.`
2. [`diff=2103108`](https://fi.wikipedia.org/w/index.php?diff=2103108) (*Suomen sisällissota*, `control`) — Regular contributor expanding narrative background (`267` + `636` chars) in 2006 without inline `<ref>` per paragraph.
   - Evidence: `Samoin "epäonnistui" Rahjan veljesten suunnitelma tehdä vallankumous Suomessa venäläisten aseiden ja Pietarin suomalaisten punakaartilaisten avulla; suomalaiset olivat jo aiemmin itse päättäneet toimi`
3. [`diff=6619153`](https://fi.wikipedia.org/w/index.php?diff=6619153) (*Sotasyyllisyysoikeudenkäynti*, `control`) — Added `148` chars of factual court chronology without attaching a footnote.
   - Evidence: `Viimeinen istunto pidettiin 2. helmikuuta, minkä jälkeen tuomioistuimen jäsenet siirtyivät [[eduskuntatalo]]on neuvottelemaan langetettavasta tuomiosta.`
4. [`diff=2441364`](https://fi.wikipedia.org/w/index.php?diff=2441364) (*Suomen sisällissota*, `control`) — Added `81` chars in body paragraph 75 while expanding a collective Harvard footnote (`Vares 1998, Tikka 2004 ja 2006`) in a distant section (paragraph 179).
   - Evidence: `, mutta sisäpoliittinen paine ja Saksan johdon kehotukset saivat hänet toimintaan`
5. [`diff=10523138`](https://fi.wikipedia.org/w/index.php?diff=10523138) (*Mainilan laukaukset*, `control`) — Replaced a `{{Kenen mukaan}}` tag by naming Mannerheim in prose (`137` chars) without adding a new `<ref>`.
   - Evidence: `Mannerheimin nimenomaisesta` *(longest single segment in paragraph: `ja määräsi rajajoukot vetäytymään 1–2 kilometrin päähän rajasta tykkien kantaman ulkopuolelle, jotta neuvostoliittolaiset eivät voisi väittää suomalaisten ampuvan rajalle.`)*

**False-positive patterns observed:**
- **Pre-2010 Finnish Wikipedia norms:** Long-standing editors routinely added 1–3 factual sentences in 2005–2010 without per-sentence `<ref>` tags, or updated an article-end collective note in another section (`diff=2441364`). `unsourced_addition` has good recall (`10.7%` of non-graffiti disputes) but noisy standalone precision (`~62.5%` on balanced sample), so it should act as a **corroborating booster**, never a sole incident trigger.

---

### 4. `source_domain` — 6 reverted (`4.0%`) vs 1 control (`0.7%`), lift `6.00x` (`8.93x` non-graffiti)

**Crucial structural finding:** In `WikitextDiff`, only `<ref>…</ref>` tags get `kind = "reference"`. Pre-`<ref>` inline bracketed links (`[http://suur_suomi.tripod.com/ …]`) and external source links (`*[http://personal.inet.fi/…]`, `[https://www.youtube.com/…]`) are emitted as `prose` or `markup`. Extracting newly added external URLs across all added segments (excluding domains already present in `removed` segments of the same diff, and unwrapping `web.archive.org`) yields the strongest separator in the `refs` group (**`6.00x` lift overall, `8.93x` on non-graffiti disputes**):

**Reverted hits (5 of 6 shown; 6th is `diff=194401`, an earlier insertion of the same `personal.inet.fi` link on *Karjala-kysymys*):**
1. [`diff=19002883`](https://fi.wikipedia.org/w/index.php?diff=19002883) (*Talvisota*, `dispute`) — Promotional insertion linking a band's YouTube video (`youtube.com`, `self_published`).
   - Evidence: `Ajopuut-yhtye on julkaissut [https://www.youtube.com/watch?v=edpcOqT3NoM Talvisota]-nimisen laulun videona, jonka kuvitus on Puolustusvoimain kuva-arkostosta.`
2. [`diff=335069`](https://fi.wikipedia.org/w/index.php?diff=335069) (*Suur-Suomi*, `dispute`) — Added an inline link to a personal Tripod homepage (`suur_suomi.tripod.com`, `self_published`) during a content dispute.
   - Evidence: `[http://suur_suomi.tripod.com/ Toinen suur-Suomi-sivu]`
3. [`diff=430036`](https://fi.wikipedia.org/w/index.php?diff=430036) (*Karjala-kysymys*, `dispute`) — Self-linked personal ISP homepage advocating Karelian restitution (`personal.inet.fi`, `self_published`; also attempted in [`diff=194401`](https://fi.wikipedia.org/w/index.php?diff=194401)).
   - Evidence: `*[http://personal.inet.fi/koti/juhani.putkinen/karjala_takaisin.htm Karjala-takaisin mielipiteitä ja faktaa]`
4. [`diff=16996153`](https://fi.wikipedia.org/w/index.php?diff=16996153) (*Talvisota*, `dispute`) — Added a Facebook page link (`facebook.com`, `self_published`).
   - Evidence: `* [https://www.facebook.com/sodan1939kulku Talvisota-105 päivää]`
5. [`diff=15665202`](https://fi.wikipedia.org/w/index.php?diff=15665202) (*Viipuri*, `graffiti_cleanup`) — Replaced article links with parody-wiki links (`hikipedia.info`, `self_published`).
   - Evidence: `'''Viipuri''' ([http://hikipedia.info/wiki/Kiinan_kieli kiinaksi] 北京) eli Karjalaisten [http://hikipedia.info/wiki/Kaupunki kaupunki] on [http://hikipedia.info/wiki/Luonto luonnonkaunis] [http://hikip`

**Control hits (all 1 of 1):**
1. [`diff=392870`](https://fi.wikipedia.org/w/index.php?diff=392870) (*Suur-Suomi*, `control`) — 2005 edit citing a Tripod songbook page (`peacecountry0.tripod.com`, `self_published`) only for the text of August Ahlqvist's 1860 poem *"Suomen valta"*.
   - Evidence: `uno "Suomen valta" [http://peacecountry0.tripod.com/songbook.htm#2], jossa sukulaisheimojen veljeyttä korostettiin muun muassa seuraavin säkein: ''Äänisjärvi, Pohjanlahti, / Auranrannat, Vienansuu, / `

**False-positive patterns suppressed / observed:**
- **Reformatting an already-present blog ref** (suppressed): in `diff=2722889` (*Karjala-kysymys*, `control`), `kemppinen.blogspot.com` was already in the parent revision and only had its author/URL order swapped; filtering domains present in `removed` segments prevents false firing.
- **Primary-text transcriptions on personal hosts** (`diff=392870`): early Wikipedia edits occasionally linked personal pages hosting public-domain 19th-century poems.

#### Data-Driven Domain Score Across All 300 Candidates

Every domain newly added in the 300 candidates (excluding domains already present in the parent side of the diff):

| Domain | Total additions | Reverted | Control | Reverted rate | In `<ref>` segment | Curated `domains.json` category |
|---|---:|---:|---:|---:|---:|---|
| `personal.inet.fi` | 2 | 2 | 0 | **100.0%** | 0 | `self_published` |
| `yle.fi` | 2 | 1 | 1 | **50.0%** | 0 | — *(1 reverted hit is `yle.fi/multifoorumi` forum thread in `diff=1517380`; 1 control hit is Yle Elävä arkisto in `diff=5386309`)* |
| `cracked.com` | 1 | 1 | 0 | **100.0%** | 0 | — |
| `facebook.com` | 1 | 1 | 0 | **100.0%** | 0 | `self_published` |
| `fanfox.net` | 1 | 1 | 0 | **100.0%** | 1 | — *(manga scanlation site in `diff=19012601`)* |
| `hikipedia.info` | 1 | 1 | 0 | **100.0%** | 0 | `self_published` |
| `indiana.edu` | 1 | 1 | 0 | **100.0%** | 1 | — *(academic URL added alongside off-topic POV paragraph in `diff=7689973`)* |
| `lumisota.netrainer.org` | 1 | 1 | 0 | **100.0%** | 0 | — *(corrupted `talvisota→lumisota` URL in `diff=5763248`)* |
| `mannerheim.fi` | 1 | 1 | 0 | **100.0%** | 1 | — *(primary order-of-the-day `.gif` cited in `edit_war` `diff=13797982`)* |
| `rautjarvitravel.com` | 1 | 1 | 0 | **100.0%** | 0 | — *(promotional museum external link in `diff=17666163`)* |
| `suur_suomi.tripod.com` | 1 | 1 | 0 | **100.0%** | 0 | `self_published` |
| `youtube.com` | 1 | 1 | 0 | **100.0%** | 0 | `self_published` |
| `eva.fi` | 1 | 0 | 1 | **0.0%** | 1 | — |
| `histdoc.net` | 1 | 0 | 1 | **0.0%** | 0 | — *(Pauli Kruhse's historical document archive)* |
| `hs.fi` | 1 | 0 | 1 | **0.0%** | 1 | — |
| `peacecountry0.tripod.com` | 1 | 0 | 1 | **0.0%** | 0 | `self_published` |
| `suomenkuvalehti.fi` | 1 | 0 | 1 | **0.0%** | 1 | — |
| `tampere1918.fi` | 1 | 0 | 1 | **0.0%** | 1 | — |
| `ts.fi` | 1 | 0 | 1 | **0.0%** | 1 | — |
| `vesta.narc.fi` | 1 | 0 | 1 | **0.0%** | 1 | — *(National Archives War Victims database)* |
| `vnk.fi` | 1 | 0 | 1 | **0.0%** | 1 | — *(Prime Minister's Office publication)* |

Notice the clean split: mainstream news, government, archive, and museum domains (`hs.fi`, `ts.fi`, `suomenkuvalehti.fi`, `vnk.fi`, `vesta.narc.fi`, `tampere1918.fi`, `histdoc.net`, `eva.fi`) were **0% reverted (`0/8`)**, whereas personal homepages, social/video platforms, and entertainment/scanlation sites (`personal.inet.fi`, `youtube.com`, `facebook.com`, `hikipedia.info`, `cracked.com`, `fanfox.net`) were **100% reverted**. Also note `yle.fi/multifoorumi` (`diff=1517380`): URL path rules (flagging legacy `yle.fi/multifoorumi` forum threads without flagging `yle.fi` journalism) are worth considering if corpus-wide URL frequencies warrant it.

---

### 5. `maintenance_template` — 7 reverted (`4.7%`) vs 6 control (`4.0%`), lift `1.17x`

Real template names found in the corpus: `{{lähde}}` (`1,981` total occurrences), `{{lähde tarkemmin}}` (`333`), `{{selvennä}}` (`239`), `{{lähteetön}}` (`162`), `{{kenen mukaan}}` (`62`), `{{parempi lähde}}` (`34`), `{{korjattava}}` / `{{korjattava/…}}` (`34`), `{{tarkenna}}` (`20`), `{{kiistanalainen}}` (`9`), `{{neutraalius}}` (`8`), `{{tarkistettava}}` (`2`), `{{viitteetön}}` (`2`), `{{lähde?}}` (`1`).

**5 reverted examples:**
1. [`diff=11837098`](https://fi.wikipedia.org/w/index.php?diff=11837098) (*Jatkosota*, `dispute`) — **Strong variant**: silently deleted `{{lähde}}` from `== Sota alkaa ==` without adding a reference or changing a single character of prose.
   - Label: `Strong: removed maintenance template {{lähde}} without other section changes`
   - Evidence: `{{lähde}}`
2. [`diff=392669`](https://fi.wikipedia.org/w/index.php?diff=392669) (*Suur-Suomi*, `dispute`) — Drive-by insertion of `{{kiistanalainen}}` at top of article with empty edit summary; reverted.
   - Evidence: `{{kiistanalainen}}`
3. [`diff=15073786`](https://fi.wikipedia.org/w/index.php?diff=15073786) (*Talvisota*, `dispute`) — Drive-by insertion of `{{Korjattava/Suomi}}` banner with empty edit summary; reverted.
   - Evidence: `{{Korjattava/Suomi}}`
4. [`diff=485917`](https://fi.wikipedia.org/w/index.php?diff=485917) (*Suomen sisällissota*, `dispute`) — Placed `{{lähteetön}}` banner atop an already sourced featured article; reverted.
   - Evidence: `{{lähteetön}}`
5. [`diff=24034423`](https://fi.wikipedia.org/w/index.php?diff=24034423) (*Luovutettu Karjala*, `graffiti_cleanup`) — Rewrote prose and deleted `{{lähde}}` without supplying any citation.
   - Evidence: `{{lähde}}`

**5 control examples:**
1. [`diff=4604143`](https://fi.wikipedia.org/w/index.php?diff=4604143) (*Jatkosota*, `control`) — **Strong variant false positive**: removed `{{lähteetön}}` banner with summary `"Lähteeton-malline pois, on lisätty."` because citations had been added in an earlier revision rather than in the same commit.
   - Evidence: `{{lähteetön}}`
2. [`diff=5971819`](https://fi.wikipedia.org/w/index.php?diff=5971819) (*Sotasyyllisyysoikeudenkäynti*, `control`) — Constructive patrol edit adding `{{lähde|…}}` and `{{selvennä|…}}` with detailed reasoning.
   - Evidence: `{{lähde|eiköhän asiaan ole tullut lisätietoa tai ainakin historioitsijoiden tulkintoja viimeisen 50 vuoden aikana}}`
3. [`diff=5992988`](https://fi.wikipedia.org/w/index.php?diff=5992988) (*Sotasyyllisyysoikeudenkäynti*, `control`) — Follow-up edit 5 days later rewriting the flagged passage and removing `{{lähde|…}}` + `{{selvennä|…}}`.
   - Evidence: `{{lähde|eiköhän asiaan ole tullut lisätietoa tai ainakin historioitsijoiden tulkintoja viimeisen 50 vuoden aikana}}`
4. [`diff=14984804`](https://fi.wikipedia.org/w/index.php?diff=14984804) (*Jatkosota*, `control`) — Editor asking `Mikä lähde?` by tagging an unverified claim with `{{lähde}}`.
   - Evidence: `{{lähde}}`
5. [`diff=5690119`](https://fi.wikipedia.org/w/index.php?diff=5690119) (*Sotasyyllisyysoikeudenkäynti*, `control`) — Editor tagging unattributed wording with `{{Kenen mukaan}}` (later resolved in `control` [`diff=10523138`](https://fi.wikipedia.org/w/index.php?diff=10523138)).
   - Evidence: `{{Kenen mukaan}}`

**False-positive patterns observed:**
- **Constructive inline tagging & resolution:** Adding inline `{{lähde}}`, `{{selvennä}}`, or `{{Kenen mukaan}}` (`3` of `6` control hits) is how good-faith editors audit articles! Conversely, **article-level dispute banners** (`{{kiistanalainen}}`, `{{Korjattava/…}}`, `{{lähteetön}}`, `{{Neutraalius}}`) were `3` reverted vs `0` control when added, and **removing a maintenance tag without adding a `<ref>`** (especially with an empty edit summary, unlike `diff=4604143`'s `"on lisätty"`) separates silent tag-stripping (`diff=11837098`, `diff=24034423`) from legitimate cleanup.

---

## Judgement: What to Keep for P1.S9 / P1.S10

1. **Keep `source_domain` (`6.00x` lift, `8.93x` on non-graffiti disputes):** Best precision-to-effort detector in the `refs` group. Must scan newly added external URLs across `reference`, `prose`, `markup`, and `template` segments (because pre-`<ref>` inline `[http://…]` citations and `Aiheesta muualla` links are not `kind == "reference"` in `wikidiff`), while ignoring domains already present in `removed` segments.
2. **Keep `citation_removed` (`3.00x` lift; `4/4` real inspections were genuine contested/POV edits including un-SHA1-reverted `diff=14098576`):** Rare (`~1–2%` of edits) but high-signal when atomic-`<ref>` formatting, Wayback archiving, and whole-paragraph/whole-sentence deletions are filtered out. Especially valuable because it catches **non-reverted or non-canonically reverted** source stripping like `diff=14098576` on *Mainilan laukaukset*.
3. **Keep `citation_swapped` (`2.00x` lift) as a compound/UI signal:** Standalone swaps include benign statistical updates (`Valtiovarainministeriö 2006 → 2015`). Weight it high when combined with `source_domain` (established source → curated low-reliability domain) or simultaneous number/claim changes (`diff=8109372`), and show the `old → new` label directly in the incident UI.
4. **Keep `unsourced_addition` (`1.67x` lift, `10.7%` non-graffiti recall) as a low-weight feature / LLM triage filter:** Too common in pre-2010 Finnish Wikipedia (`6.0%` control rate) to promote an edit on its own, but useful as an additive feature alongside revert/dispute signals or prose detectors.
5. **Refine `maintenance_template` (`1.17x` raw lift):** Do **not** reward generic inline `{{lähde}}` / `{{Kenen mukaan}}` *additions* as manipulation signals (they are mostly constructive patrol edits). Instead, keep **only**:
   - **Template removal without adding a citation** (`added_refs == 0`, especially the **strong** section-isolated variant), and
   - **Article/section dispute banners** (`{{Neutraalius}}`, `{{Kiistanalainen}}`, `{{Lähteetön}}`, `{{Korjattava/…}}`) as article-contestation context rather than blaming the tagging edit.

---

## Caveats

1. **18 demo articles only:** All `300` candidates in `~/.cache/aot-research/s8/candidates.jsonl` come from the `18` curated demo-topic core articles (`Jatkosota`, `Talvisota`, `Suomen sisällissota`, `Karjala`, `Karjala-kysymys`, `Luovutettu Karjala`, `Suur-Suomi`, `Mainilan laukaukset`, `Simo Häyhä`, `Viipuri`, `Käkisalmi`, `Risto Ryti`, `Punakaarti`, `Suojeluskunta`, `Moskovan rauhansopimus (1940)`, `Sotasyyllisyysoikeudenkäynti`, `Suomen sisällissodan nimet`, `Itä-Karjalan keskitysleirit`). These are high-traffic, heavily patrolled historical/geopolitical articles; citation density and dispute rates across the broader `19,742`-article corpus are lower (`prof_rev` found `46.5%` of corpus articles have `<ref>` in current revisions).
2. **S1 SHA-1 revert boundary:** `group = "control"` means *"not reverted by an exact SHA-1 identity revert within the S1 window"*, not *"verified neutral"*. As `diff=14098576` (*Mainilan laukaukset*) shows, real citation-stripping edits that were later rewritten rather than SHA-1 reverted appear in `control`, making measured lifts conservative.
3. **Small per-domain counts (`n = 300`):** In a 300-edit sample, individual newly added domains appear `1–2` times each (`21` distinct domains across `22` edit-domain pairs), so per-domain empirical revert rates are illustrative; corpus-wide domain scoring requires precomputing added URLs across a larger revision sample.
