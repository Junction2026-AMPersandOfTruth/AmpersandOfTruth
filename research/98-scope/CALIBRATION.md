# Scope Check Calibration (`EmbeddingScopeCheck`, #44 / P2 #98)

Reproducible script:

```bash
scripts/corpus-tunnel.sh
python3 research/98-scope/calibrate.py --json /tmp/scope-measurements.json
```

All measurements use `BAAI/bge-m3` (1024-d) via Ollama and top-5 HNSW cosine distance over `corpus.article_vector` (19,742 articles across `fi`, `en`, `sv`), matching `IQueryEmbedder.EmbedAsync` + `IInvestigationCorpus.NearestArticlesAsync(vector, 5)` in the web app.

---

## 1. Calibration Query Set (`N = 103`: 67 In-Scope, 36 Out-of-Scope)

| Split / Source | Count | Examples |
|---|---:|---|
| `golden.json` (`kind = topic`, `dev` + `test`, `en`/`fi`/`broad`/`sv`) | 52 | *Finnish Civil War 1918: war of liberation or class war*, *talvisota Mainilan laukaukset Terijoen hallitus kuka voitti*, *saamelaisten alkuperäiskansa-asema…* |
| `golden.json` (`kind = control-quiet`, `dev`, `en`/`fi`/`broad`/`sv`) | 4 | *Finland-Russia border crisis 2023…*, *Suomen ja Venäjän rajakriisi 2023…* |
| `appsettings.json` `Search:SuggestedTopics` | 7 | *Civil War 1918*, *Winter War*, *Continuation War*, *Mannerheim*, *Mainila shots*, *Ceded Karelia*, *Finlandization* |
| `scripts/demo-topics.txt` + lowercase acceptance query | 4 | *Suomen sisällissota*, *Jatkosota*, *Talvisota*, *talvisota* |
| **Total In-Scope** | **67** | |
| `golden.json` (`kind = control-out-of-corpus`, `dev` + `test`) | 6 | `french-revolution` (3 queries), `moon-landing-hoax` (3 queries) |
| Custom multilingual out-of-scope (`14 en`, `9 fi`, `7 sv`) | 30 | *climate change*, *Climate change*, *Taylor Swift*, *COVID vaccines in Brazil*, *Roman Empire*, *Bitcoin*, *World Cup 2022*, *Donald Trump*, *Python programming*, *ilmastonmuutos*, *klimatförändringar*, *American Civil War*, *Yhdysvaltain sisällissota*, *Romerska riket*, … |
| **Total Out-of-Scope** | **36** | |

---

## 2. Why a Naive Raw-Distance Threshold Fails (`2.8%` OOS Rejection at `100%` Recall)

Consistent with P1.R1 (`research/8-r1-vector/README.md`), two `bge-m3` properties prevent a bare cosine cut-off on raw query strings from separating short queries:

1. **Short TitleCase topic strings sit far from full `# Title` + body article vectors:**
   All 56 `golden.json` queries (`kind = topic` and `control-quiet`) have raw `top1 <= 0.4771` (`mean5 <= 0.5027`), whereas 5 short 1–3 word demo/chip titles sit at `0.5256–0.6479`:
   - `Suomen sisällissota`: `raw_top1 = 0.6479`, `raw_mean5 = 0.6530` (top-5 includes *Suomen sisällissota Etelä-Pohjanmaalla*)
   - `Civil War 1918`: `raw_top1 = 0.6291`, `raw_mean5 = 0.6370` (configured `SuggestedTopics` chip)
   - `Continuation War`: `raw_top1 = 0.5391`, `raw_mean5 = 0.5478` (top-1 is `en:Continuation War`)
   - `Jatkosota`: `raw_top1 = 0.5339`, `raw_mean5 = 0.5564` (top-1 is `fi:Jatkosota`)
   - `Talvisota`: `raw_top1 = 0.5256`, `raw_mean5 = 0.5422` (top-1 is `fi:Talvisota`)

2. **Initial-lowercase 2-word queries shift ~`0.10` closer to all corpus articles in `bge-m3`:**
   - `"Climate change"` (capital `C`) has `top1 = 0.5287`, `mean5 = 0.5549` (`en:Wilderness`, `fi:Ilmastoveivi2019-kampanja`).
   - `"climate change"` (lowercase `c`) shifts to `raw_top1 = 0.4258`, `raw_mean5 = 0.4478` — closer on every top-5 rank than `"Winter War"` (`top1 = 0.4662`, `mean5 = 0.4885`)!
   - The same shift occurs across languages: `"ilmastonmuutos"` `0.5732` → `"Ilmastonmuutos"` `0.6326`; `"klimatförändringar"` `0.5394` → `"Klimatförändringar"` `0.6572`.

Because `max(in_scope raw_top1) = 0.6479`, a raw threshold keeping 100% of in-scope topics (`T >= 0.6479`) rejects only **1 / 36 (2.8%)** out-of-scope topics.

---

## 3. Calibrated `EmbeddingScopeCheck` Rule

Using only `IQueryEmbedder`, `IInvestigationCorpus.NearestArticlesAsync(vector, 5)`, and `SearchOptions`:

1. **Sentence-initial uppercase normalization before embedding:** Normalize NFC + collapse whitespace, and uppercase the first character (`"climate change"` → `"Climate change"`, `"talvisota"` → `"Talvisota"`), leaving proper nouns in the rest of the string intact.
2. **Direct corpus-title or `SuggestedTopics` match (when `hits.Count > 0`):** Accept immediately if the query matches a configured `SearchOptions.SuggestedTopics` entry or if any of the 5 nearest corpus articles has an exact case-insensitive title match (e.g. *Talvisota*, *Jatkosota*, *Continuation War*, *Fortsättningskriget*, *Urho Kekkonen*) or, for multi-word queries, a `<query> …` title prefix match (e.g. *Suomen sisällissota* → *Suomen sisällissota Etelä-Pohjanmaalla*). Zero out-of-scope queries (`0 / 36`) match this rule.
3. **Calibrated cosine distance bounds (`Search:Scope`):**
   - `MaxTopDistance = 0.495` (highest non-exact in-scope query: `winter-war.fi`, `top1 = 0.4887`)
   - `MaxMeanDistance = 0.515` (highest non-exact in-scope query: `winter-war.fi`, `mean5 = 0.5105`)
   - In scope when `top1 <= MaxTopDistance && mean5 <= MaxMeanDistance`.

---

## 4. Confusion Tables & Threshold Sweep

### Selected Defaults: `MaxTopDistance = 0.495`, `MaxMeanDistance = 0.515`

| Actual \ Predicted | Predicted **In Scope** | Predicted **Out of Scope** | Recall / Rejection |
|---|---:|---:|---:|
| **Actual In Scope (`67`)** | **67** (TP) | **0** (FN) | **100.0%** kept |
| **Actual Out of Scope (`36`)** | **2** (FP) | **34** (TN) | **94.4%** rejected |

Breakdown by subset:
- `golden.json` `topic` (dev + test): **52 / 52 (100%)** in scope
- `golden.json` `control-quiet` (dev): **4 / 4 (100%)** in scope
- `SuggestedTopics` + `demo-topics.txt` + `"talvisota"`: **11 / 11 (100%)** in scope
- `golden.json` `control-out-of-corpus`: **5 / 6 (83.3%)** rejected (`french-revolution` 3/3 rejected; `moon-landing-hoax` 2/3 rejected)
- Custom multilingual out-of-scope (`en`/`fi`/`sv`): **29 / 30 (96.7%)** rejected

The only 2 out-of-scope queries that slip under `(0.495, 0.515)` are:
- `moon-landing-hoax.fi` (*"väitteet että kuulennot lavastettiin"*, `top1 = 0.4781`, `mean5 = 0.4937` → Finnish public-controversy pages)
- `"Brexit referendum"` (`top1 = 0.4861`, `mean5 = 0.5019` → *1931 Finnish prohibition referendum*, *1919 Ålandic status referendum*)

Tightening `MaxTopDistance` below `0.4887` to catch those two would reject `winter-war.fi` (*"talvisota Mainilan laukaukset Terijoen hallitus kuka voitti"*, `top1 = 0.4887`), violating the 100% in-scope constraint.

### Threshold Sensitivity Sweep

| Rule / Thresholds | In-Scope Kept (`67`) | Out-of-Scope Rejected (`36`) | Notes |
|---|---:|---:|---|
| Raw string, `top1 <= 0.648` | 67 / 67 (100.0%) | 1 / 36 (2.8%) | Dominated by `Suomen sisällissota` (`0.6479`) |
| Normalized + title/chip + `(0.490, 0.512)` | 67 / 67 (100.0%) | 34 / 36 (94.4%) | Tight margin (`0.0013`) above `winter-war.fi` |
| **Normalized + title/chip + `(0.495, 0.515)`** | **67 / 67 (100.0%)** | **34 / 36 (94.4%)** | **Selected default** |
| Normalized + title/chip + `(0.505, 0.515)` | 67 / 67 (100.0%) | 33 / 36 (91.7%) | `french-revolution.en` (`0.5008`) slips |
| Normalized + title/chip + `(0.510, 0.525)` | 67 / 67 (100.0%) | 31 / 36 (86.1%) | 2 `french-revolution` + French election slip |

---

## 5. Key Query Measurements & Nearest-Chip Suggestions

| Query | Expected | `top1` | `mean5` | Nearest Corpus Hit | Decision |
|---|---|---:|---:|---|---|
| `Winter War` | In | 0.4662 | 0.4885 | `sv:Vinterkriget` | **In** (`<= 0.495` + chip) |
| `talvisota` | In | 0.5256 | 0.5422 | `fi:Talvisota` | **In** (exact title) |
| `Suomen sisällissota` | In | 0.6479 | 0.6530 | `fi:Mouhun–Hillosensalmen taistelut` | **In** (prefix title in top-5) |
| `talvisota Mainilan laukaukset Terijoen hallitus kuka voitti` | In | 0.4887 | 0.5105 | `fi:Mainilan laukaukset` | **In** (closest to boundary) |
| `climate change` | Out | 0.5287 | 0.5549 | `en:Wilderness` | **Out** → *Winter War, Continuation War, Civil War 1918, Ceded Karelia* |
| `ilmastonmuutos` | Out | 0.6326 | 0.6442 | `fi:Ilmastoveivi2019-kampanja` | **Out** |
| `klimatförändringar` | Out | 0.6572 | 0.6697 | `fi:Ilmastoveivi2019-kampanja` | **Out** |
| `American Civil War` | Out | 0.5873 | 0.6126 | `en:Battle of Fort Necessity` | **Out** → *Civil War 1918, Winter War, Continuation War, Ceded Karelia* |
| `How many people were executed during the Reign of Terror…` | Out | 0.5008 | 0.5116 | `fi:Turun verilöyly` | **Out** (`top1 > 0.495`) |
| `Taylor Swift` | Out | 0.5874 | 0.6024 | `fi:Irma Agiashvili` | **Out** |
| `Bitcoin` | Out | 0.5500 | 0.5704 | `en:Vastaamo data breach` | **Out** |
| `Donald Trump` | Out | 0.5468 | 0.5773 | `fi:Helsingin huippukokous 2018` | **Out** |
| `Python programming` | Out | 0.5638 | 0.5780 | `en:VTech Laser 200` | **Out** |
