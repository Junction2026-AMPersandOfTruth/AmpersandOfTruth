# P1.S8 (#35) · Group `text`: Number, Hedging & Loaded-Term Detectors

Evaluated on the shared 300-edit candidate set (`$AOT_CACHE_DIR/s8/candidates.jsonl`: **150 reverted** vs **150 non-reverted controls**, sampled with `seed=35` across the 18 core demo-topic articles).

## How to Run

```bash
# Unit tests (pure Python, no DB):
python3 -m unittest discover -s research/35-s8-signals

# Run all three text detectors over candidates.jsonl -> results/text.jsonl + README.text.md:
python3 research/35-s8-signals/run_text.py
```

## Summary Metrics

| Detector (`kind`) | Reverted hits (`n=150`) | Control hits (`n=150`) | Lift (`rate_rev / rate_ctl`) |
|---|---:|---:|---:|
| `number_changed` (`detect_numbers.py`) | 5 / 150 (3.3%) | 4 / 150 (2.7%) | **1.25×** |
| `attribution_flip` (`detect_hedging.py`) | 5 / 150 (3.3%) | 10 / 150 (6.7%) | **0.50×** |
| `loaded_term` (`detect_loaded_terms.py`) | 15 / 150 (10.0%) | 0 / 150 (0.0%) | **∞ (≥ 15.0× smoothed)** |
| **Any `text` signal** | **22 / 150 (14.7%)** | **13 / 150 (8.7%)** | **1.69×** |

Sub-slice of `number_changed` restricted to **`Quantity changed without a new source`**: **2 / 150 (1.3%)** reverted vs **1 / 150 (0.7%)** control (lift **2.00×**).

---

## Example Hits by Detector

### `number_changed` (`detect_numbers.py`)

**Reverted examples (up to 5):**
- [4112248](https://fi.wikipedia.org/w/index.php?diff=4112248) (*Suur-Suomi*, -75 B, `s1=graffiti_cleanup`) — **Quantity changed without a new source**: `24 000 → 10 000` *(+1 more in edit)*
- [3290065](https://fi.wikipedia.org/w/index.php?diff=3290065) (*Suomen sisällissota*, +669 B, `s1=edit_war`) — **Quantity changed (with new source)**: `15 000 → 13 000`
- [8109372](https://fi.wikipedia.org/w/index.php?diff=8109372) (*Simo Häyhä*, +384 B, `s1=graffiti_cleanup`) — **Quantity changed (with new source)**: `542 → 259`
- [20345981](https://fi.wikipedia.org/w/index.php?diff=20345981) (*Punakaarti*, +33 B, `s1=dispute`) — **Quantity changed without a new source**: `15 000 → 25 000` *(+1 more in edit)*
- [15387221](https://fi.wikipedia.org/w/index.php?diff=15387221) (*Suomen sisällissota*, -11 B, `s1=graffiti_cleanup`) — **Year changed without a new source**: `1918 → 2015`

**Control examples (up to 5):**
- [14737751](https://fi.wikipedia.org/w/index.php?diff=14737751) (*Karjala-kysymys*, -50 B) — **Quantity changed (with new source)**: `40 → 54`
- [404544](https://fi.wikipedia.org/w/index.php?diff=404544) (*Suomen sisällissota*, +18 B) — **Quantity changed without a new source**: `50-60 → 40-60`
- [795745](https://fi.wikipedia.org/w/index.php?diff=795745) (*Jatkosota*, +14 B) — **Year changed without a new source**: `1942 → 1941`
- [202299](https://fi.wikipedia.org/w/index.php?diff=202299) (*Talvisota*, +31 B) — **Year changed without a new source**: `18 → 1918`

### `attribution_flip` (`detect_hedging.py`)

**Reverted examples (up to 5):**
- [13234911](https://fi.wikipedia.org/w/index.php?diff=13234911) (*Talvisota*, -11 B, `s1=dispute`) — **Hedge or attribution removed (low weight)**: `- onkin tulkittu puhtaaksi kostotoimeksi`
- [3290065](https://fi.wikipedia.org/w/index.php?diff=3290065) (*Suomen sisällissota*, +669 B, `s1=edit_war`) — **Scare quotes added around word (low weight)**: `avunpyynnöt → "avunpyynnöt" (…motiiviksi esitettiin ahvenanmaalaisten "avunpyynnöt" ruotsalaisille. Tosiasiassa syynä olivat…)`
- [7714924](https://fi.wikipedia.org/w/index.php?diff=7714924) (*Simo Häyhä*, +911 B, `s1=edit_war`) — **Hedge or attribution added (low weight)**: `+ …on tehty tarkka-ampujana ja mitkä lähitaistelussa. Haastattelun mukaan Häyhä osallistui lukuisille väkivaltaisille…`
- [8109372](https://fi.wikipedia.org/w/index.php?diff=8109372) (*Simo Häyhä*, +384 B, `s1=graffiti_cleanup`) — **Hedge or attribution removed (low weight)**: `- joidenkin tietojen mukaan` *(+1 more in edit)*
- [302362](https://fi.wikipedia.org/w/index.php?diff=302362) (*Jatkosota*, +28 B, `s1=edit_war`) — **Attribution verb flipped (low weight)**: `välitti käsityksen siitä sodanpäämäärältään poliittisesti → osoitti` *(+1 more in edit)*

**Control examples (up to 5):**
- [2103108](https://fi.wikipedia.org/w/index.php?diff=2103108) (*Suomen sisällissota*, +1022 B) — **Scare quotes added around word (low weight)**: `+ Samoin "epäonnistui" Rahjan veljesten suunnitelma tehdä vallankumous…`
- [3360485](https://fi.wikipedia.org/w/index.php?diff=3360485) (*Suomen sisällissota*, +29 B) — **Scare quotes removed around word (low weight)**: `- monet "tsaristiset"`
- [3491599](https://fi.wikipedia.org/w/index.php?diff=3491599) (*Talvisota*, +3964 B) — **Hedge or attribution added (low weight)**: `+ …ja ottanut etäisyyttä Saksaan, mikä Moskovassa tulkittiin mahdollisuutena parantaa maiden välistä yhteistyötä…` *(+1 more in edit)*
- [404544](https://fi.wikipedia.org/w/index.php?diff=404544) (*Suomen sisällissota*, +18 B) — **Hedge or attribution removed (low weight)**: `- …keskustelussa painottui voittajien ns. valkoinen totuus, hävinneiden ns. punaisen…`
- [15372](https://fi.wikipedia.org/w/index.php?diff=15372) (*Mainilan laukaukset*, +156 B) — **Hedge or attribution added (low weight)**: `+ Joidenkin historioitsijoiden mukaan Mainilan laukauksia ei koskaan edes ammuttu, vaan…`

### `loaded_term` (`detect_loaded_terms.py`)

**Reverted examples (up to 5):**
- [445526](https://fi.wikipedia.org/w/index.php?diff=445526) (*Karjala-kysymys*, -20 B, `s1=graffiti_cleanup`) — **Paired term swap: luovutettu → miehitetty**: `Karjala-kysymyksellä tarkoitetaan nykyään luovutetun Karjalan ja muiden toisen → Karjala-kysymyksellä tarkoitetaan nykyään Miehitetty Karjala ja muiden toisen`
- [13792608](https://fi.wikipedia.org/w/index.php?diff=13792608) (*Jatkosota*, -995 B, `s1=edit_war`) — **Paired term swap: luovutetut alueet → menetetyt alueet**: `Moskovan rauhassa 1940 luovutetut alueet liitetyiksi Suomeen → liitti talvisodassa menetetyt alueet takaisin Suomeen palauttaen`
- [1578757](https://fi.wikipedia.org/w/index.php?diff=1578757) (*Käkisalmi*, +177 B, `s1=edit_war`) — **Paired term swap: Priozersk → Käkisalmi**: `Priozersk (, vuoteen → Käkisalmi (karjalaksi Käksalm`
- [11596668](https://fi.wikipedia.org/w/index.php?diff=11596668) (*Suomen sisällissota*, -16 B, `s1=dispute`) — **Paired term swap: sisällissota → vapaussota**: `Sisällissodan veteraanit → Vapaussodan veteraanit` *(+1 more in edit)*
- [20765131](https://fi.wikipedia.org/w/index.php?diff=20765131) (*Viipuri*, -12 B, `s1=dispute`) — **Paired term swap: Luoteis-Venäjä → Itä-Suomi**: `) on kaupunki Luoteis-Venäjällä Suomenlahden pohjoisrannikolla Viipurinlahden perukassa → ) on kaupunki Itä-SuomessaSuomenlahden pohjoisrannikolla Viipurinlahden perukassa`

**Control examples (up to 5):**
- *No hits in this group.*

---

## Finnish Diff Reading, False-Positive Patterns & Engineering Verdict

### 1. `loaded_term` (`detect_loaded_terms.py` + `loaded_pairs.json`) — **KEEP (high precision, direct UI highlight)**
- **What it catches:** Systematic ideological framing swaps that are tiny in byte delta (`-50 B` to `+130 B`) and easily missed by size ranking:
  - *Ceded vs. occupied Karelia & border irredentism:* `luovutetun Karjalan → Miehitetty Karjala` ([445526](https://fi.wikipedia.org/w/index.php?diff=445526)), `1940 luovutetut alueet → talvisodassa menetetyt alueet` ([13792608](https://fi.wikipedia.org/w/index.php?diff=13792608)), Viipuri's location swapped from `Luoteis-Venäjällä` to `Itä-Suomessa` ([20765131](https://fi.wikipedia.org/w/index.php?diff=20765131), `-12 B` with edit summary *"Korjattu kirjoitusvirhe"*) or `Venäjän tilapäisesti valloittamassa Karjalassa` ([21847143](https://fi.wikipedia.org/w/index.php?diff=21847143)), and headword `Priozersk → Käkisalmi` ([1578757](https://fi.wikipedia.org/w/index.php?diff=1578757)).
  - *1918 Civil War naming & legitimacy battles:* `sisällissota → vapaussota` across infobox, captions, and lead ([11596801](https://fi.wikipedia.org/w/index.php?diff=11596801), [11596668](https://fi.wikipedia.org/w/index.php?diff=11596668), [11000396](https://fi.wikipedia.org/w/index.php?diff=11000396)), `kapinoineen → vastustaneen` ([13768127](https://fi.wikipedia.org/w/index.php?diff=13768127)), `Valkokaarti → Valkoinen armeija` ([3289145](https://fi.wikipedia.org/w/index.php?diff=3289145), [3290065](https://fi.wikipedia.org/w/index.php?diff=3290065)), `valkoisen senaatin ↔ Suomen tasavallan joukot` ([13797982](https://fi.wikipedia.org/w/index.php?diff=13797982), [13768127](https://fi.wikipedia.org/w/index.php?diff=13768127)), and `Venäjän → Miehittäjä-Ryssän` ([11000396](https://fi.wikipedia.org/w/index.php?diff=11000396)).
  - *Continuation War framing:* replacing `saksalainen / saksalaisjoukkojen / armeijan` with `natsien / natsijoukkojen / miehitysarmeijan` ([9225913](https://fi.wikipedia.org/w/index.php?diff=9225913)) and softening infobox outcome `Suomen tappio → Moskovan välirauha` ([2247824](https://fi.wikipedia.org/w/index.php?diff=2247824)).
- **False-positive patterns avoided:** Requiring a genuine directional swap (`a` removed and `b` added in the same aligned clause, rather than mere mention) prevents firing on historiographical name-list sections (*Suomen sisällissodan nimet*) or book titles in bibliographies (*Vapaussodan historian komitea*, `rev=3293415`). Control hit rate on the same 18 articles is **0 / 150 (0.0%)**.

### 2. `number_changed` (`detect_numbers.py`) — **KEEP `Quantity changed` (especially without new source); DOWNWEIGHT `Year changed`**
- **What it catches:** Silent in-place figure tampering that byte-size ranking completely buries:
  - [20345981](https://fi.wikipedia.org/w/index.php?diff=20345981) (*Punakaarti*, `+33 B`, `dispute`): inflated rifle shipments `15 000 → 25 000` and Russian fighters `3 000–4 000 → 10 000–14 000` with no new citation.
  - [4112248](https://fi.wikipedia.org/w/index.php?diff=4112248) (*Suur-Suomi*, `-75 B`): cut East Karelian camp internees `24 000 → 10 000` and camp deaths `6 000–9 000 → 4000` while breaking a closing `</ref>` tag (our token-novelty check on added references prevents broken `</ref>` merges from masquerading as a "new source").
  - [8109372](https://fi.wikipedia.org/w/index.php?diff=8109372) (*Simo Häyhä*): changed lead sniper kill count `542 → 259`, replacing *Kansallisbiografia (2003)* with wartime 1941–42 print sources.
- **Important wikidiff implementation detail:** Because Finnish uses spaces/`&nbsp;` as thousand separators (`15&nbsp;000` → `25&nbsp;000`), `wikidiff` leaves `&nbsp;000` unchanged and emits only `"15" → "25"` in `segments`. Reconstructing the full numeral token from `old`/`new` changed lines is essential so the UI shows `15 000 → 25 000` and `3 000–4 000 → 10 000–14 000` instead of truncated prefixes.
- **False-positive patterns observed in controls:**
  1. *Routine statistical updates with a new citation:* [14737751](https://fi.wikipedia.org/w/index.php?diff=14737751) updated Finland's state budget comparison `40 → 54` (billion €) while updating the Finance Ministry citation from 2006 to 2015. The `(with new source)` label separates this cleanly when the citation is genuinely new, though contested edits can also attach partisan/older sources ([8109372](https://fi.wikipedia.org/w/index.php?diff=8109372)) or add a citation in an unrelated paragraph ([3290065](https://fi.wikipedia.org/w/index.php?diff=3290065)).
  2. *Year typo/fact fixes without a new source:* [202299](https://fi.wikipedia.org/w/index.php?diff=202299) fixed typo `vuoden 18 → vuoden 1918` and [795745](https://fi.wikipedia.org/w/index.php?diff=795745) fixed an infobox date error `1942 → 1941`. Splitting `Year changed` from `Quantity changed` isolates these routine date fixes.
  3. *Typography / bibliography / layout numbers (filtered out):* En-dash cleanup (`1939-1940 → 1939–1940`), thousand-space cleanup (`4000 → 4 000`), bibliography year parenthesization (`1978 → (1978)` in `rev=3293415`), and inline template widths (`{Monta palstaa|75} → 65` in `rev=24034423`) are suppressed.

### 3. `attribution_flip` (`detect_hedging.py`) — **DO NOT USE AS STANDALONE TRIGGER; keep only as a supplementary chip (`low weight`)**
- **Measured result:** **5 / 150 (3.3%)** reverted vs **10 / 150 (6.7%)** control (**0.50× lift** — *higher* in control than reverted).
- **Why lift is < 1× in Finnish Wikipedia (reading the diffs):**
  - Reverted hits are genuine POV edits: stripping interpretive hedging (`onkin tulkittu puhtaaksi kostotoimeksi → olivatkin puhdas kostotoimi` in [13234911](https://fi.wikipedia.org/w/index.php?diff=13234911) with summary *"ns. neutraali näkökulma on väistämättä venäläismielinen"*), adding scare quotes around Åland's `"avunpyynnöt"` ([3290065](https://fi.wikipedia.org/w/index.php?diff=3290065)), stripping `joidenkin tietojen mukaan` and `Kansallisbiografian mukaan` ([8109372](https://fi.wikipedia.org/w/index.php?diff=8109372)), and flipping `välitti … käsityksen → osoitti` while inserting `Monet suomalaiset väittävät edelleen…` ([302362](https://fi.wikipedia.org/w/index.php?diff=302362)).
  - **However, constructive NPOV copyediting (`kielenhuolto`) uses the exact same lexical operations in reverse:** control editors routinely remove weasel words (`ns.` removed in [404544](https://fi.wikipedia.org/w/index.php?diff=404544) and [12384035](https://fi.wikipedia.org/w/index.php?diff=12384035); unattributed `Viimeisimpien tutkimusten mukaan` removed in [10523138](https://fi.wikipedia.org/w/index.php?diff=10523138); scare quotes `"tsaristiset"` removed as POV in [3360485](https://fi.wikipedia.org/w/index.php?diff=3360485)), add scholarly attribution (`Joidenkin historioitsijoiden mukaan` in [15372](https://fi.wikipedia.org/w/index.php?diff=15372)), or hedge an over-stated claim when a source does not support it (`myöntäneet → Kyseessä saattoi olla` in [14098576](https://fi.wikipedia.org/w/index.php?diff=14098576)).
- **False-positive traps already filtered out:** Finnish postposition `mukaan` idioms (`Kun mukaan lasketaan`, `mukaan lukien`, `Saksan mukaan lähteminen`), abbreviation expansion (`ns. → niin sanotun` in `rev=18237137`), quote-mark typography (`”…” → "..."`), and wholesale section deletions. Even after removing all mechanical noise, true semantic symmetry between POV hedging and NPOV copyediting keeps lift below 1×.
- **Verdict:** Keep `detect_hedging.py` **only** as an explanatory secondary signal chip on candidates already surfaced by revert/dispute, source, number, or loaded-term signals (e.g., on `rev=8109372` and `rev=3290065` where it pairs with `number_changed` / `loaded_term` to explain *how* the framing shifted), never as a primary ranker.

---

## Caveats

1. **18 demo articles only:** All 300 candidates (`150` reverted, `150` control) come exclusively from the 18 core articles of the top 4 demo topics (*1918 Civil War, Continuation War, Winter War, Ceded Karelia*). Rates and vocabulary do not yet cover contemporary politician biographies, NATO, Sámi history, or Swedish language politics.
2. **In-sample pair construction for `loaded_term`:** Per spec, `loaded_pairs.json` (16 pairs) was built from observed diffs in the reverted group. Its **0.0%** control false-positive rate on the same 18 articles is a fair test of precision against routine editing on those topics, whereas its **10.0%** reverted recall is an optimistic in-sample upper bound that will drop on unseen topics unless the pair list is extended or backed by LLM classification.
3. **Reverted ≠ manipulation, control ≠ neutral:** The `reverted` pool contains `66` `graffiti_cleanup` edits (blankings, slurs, keyboard mash) where subtle text signals *should* be 0, while `control` (non-reverted edits) includes surviving single-editor framing changes that nobody reverted.

## Reviewer note (orchestrator)

- **`loaded_term`'s 15 vs 0 is in-sample, not a measurement.** `loaded_pairs.json` was built from the reverted diffs of this
  same 300-edit candidate set, so it is guaranteed to hit them. Measure it on the golden set or on a fresh candidate sample
  (`candidates.py --seed <other>`) before trusting the lift. The pair list itself is the useful output; review it as a human.
- **`attribution_flip` (0.5×) fires more often on normal edits than on reverted ones.** Drop it, or keep it only as a
  displayed detail and never as a ranking signal.
- `number_changed` "without a new source" (2 vs 1) is too small to judge here.
