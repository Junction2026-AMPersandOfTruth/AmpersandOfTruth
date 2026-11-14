# P1.R3 (#24) · LLM query understanding: translate, expand, HyDE, entity titles, scope check

Turns a user search phrase (`"Winter War causes"`, `"Soviet narratives about Karelia"`) into corpus-aligned retrieval
signals with **one cached LLM call per distinct topic** (`expand-topic`, job `QueryExpand` → `gemini-4-hydrogen`).

## What is here

| Path | Purpose |
|---|---|
| `prompts/expand-topic.md` + `expand-topic.schema.json` | Versioned `PromptLibrary` prompt (`job: QueryExpand`, `temperature: 0`, `thinking_level: low`, `{{data:topic}}`) returning `in_scope`, `scope_reason`, `topic_fi`, `reformulations`, `hyde_fi`, `title_guesses`, `keywords_fi`, `angle`. |
| `expand.py` | Runs `expand-topic` once via `Gemini__PromptDirectory=research/24-r3-query/prompts scripts/analyze.sh run expand-topic --vars <tmp.json>`, backed by SQLite `AnalysisCache`. Non-`ok` outcomes return `null` / `None` so retrieval degrades to R1 without crashing. `--dump-keywords` writes `results/keywords.jsonl`. |
| `variant.py` | Contract §2.1 retrieval variant (`--mode A\|B\|C\|C+\|D\|E`, `--scope`, `--merge rrf\|best_rank`, `--langs all\|fi\|en\|sv`). Folds `en`/`sv` hits to `fi` via `wikidata_id`, tags `why` with `llm`, reports `cost.llm_calls`. |
| `results/probe.<mode>.md` | Output of `research/lib/probe.py` across modes `A`, `B`, `C`, `C+`, `D`, `E` (eyeball checks before golden set #20/#21 lands). |
| `results/keywords.jsonl` | One JSON object per probe (`{"text", "keywords"}` from `keywords_fi`, e.g. `"talviso*"`), formatted for P1.R2 variant D boolean fulltext prefix search. |

## How to run

```bash
# Single topic expansion (cached on repeat)
python3 research/24-r3-query/expand.py "Winter War causes"

# Regenerate results/keywords.jsonl for R2 variant D
python3 research/24-r3-query/expand.py --dump-keywords

# Contract validation (every mode)
for m in A B C C+ D E; do
  python3 research/eval/validate.py variant retrieval -- python3 research/24-r3-query/variant.py --mode "$m"
done

# Eyeball probes (corpus tunnel up: scripts/corpus-tunnel.sh)
python3 research/lib/probe.py -- python3 research/24-r3-query/variant.py --mode E

# Inspect cold vs cached token/cost ledger
scripts/analyze.sh usage --scope r3-probes-cold
scripts/analyze.sh usage --scope r3-probes-cached
```

### Variant definitions

- **A (`topic_fi`)**: Embed the Finnish translation `topic_fi` instead of raw `text`.
- **B (multi-query)**: Embed `[text, topic_fi, *reformulations]` (5–7 short Finnish/English queries), fold each arm to `fi`, and fuse with **RRF ($k=60$)** (`--merge best_rank` also implemented and compared below).
- **C / C+ (HyDE)**: **C** embeds `hyde_fi` (60–120-word Finnish Wikipedia-style lead paragraph); **C+** fuses **C** and raw **R1** via RRF ($k=60$).
- **D (title guesses + B)**: Exact lookup of `title_guesses` on `corpus.article.title` (`lang='fi'`, accent-sensitive `COLLATE utf8mb4_uca1400_as_ci`, en/em-dash normalised), with shortest-title prefix fallback (`LIKE '<guess>%'`, max 2) only when a guess has no exact match, unioned in front of **B**.
- **E (scope gate)**: Any mode + `--scope` sets `out_of_scope = not in_scope` (`--mode E` runs `--base D` + `--scope` and skips embedding/SQL when `out_of_scope` is `True`).

---

## Pre-golden probe comparison vs R1 (`results/probe.*.md`)

*Eyeball check on `research/lib/probe_queries.json` (14 in-corpus probes with 37 expected central `fi` titles + 2 out-of-corpus controls; never tuned to probes).*

| Variant | Overall top 10 | Overall top 20 | `en` top 10 (`@20`) | `fi` top 10 (`@20`) | `sv` top 10 (`@20`) | `broad` top 10 (`@20`) | OOS controls caught | False alarms | Embeds + SQL / search |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| **R1** (`8-r1-vector`, `all`) | 20 / 37 (54%) | 22 / 37 (59%) | 13/24 (15) | 5/7 (5) | 1/1 (1) | 1/5 (1) | 0 / 2 | 0 / 14 | 1 embed, 2 SQL |
| **A** (`topic_fi`) | 24 / 37 (65%) | 27 / 37 (73%) | 17/24 (19) | 5/7 (5) | 1/1 (1) | 1/5 (2) | 0 / 2 | 0 / 14 | 1 embed, 2 SQL |
| **B** (`--merge best_rank`) | 21 / 37 (57%) | 26 / 37 (70%) | — | — | — | — | 0 / 2 | 0 / 14 | ~6 embeds, ~12 SQL |
| **B** (`--merge rrf`, default) | 28 / 37 (76%) | 30 / 37 (81%) | 18/24 (19) | 7/7 (7) | 1/1 (1) | 2/5 (3) | 0 / 2 | 0 / 14 | ~6 embeds, ~12 SQL |
| **C** (`hyde_fi`) | 24 / 37 (65%) | 30 / 37 (81%) | 14/24 (18) | 7/7 (7) | 1/1 (1) | 2/5 (4) | 0 / 2 | 0 / 14 | 1 embed, 2 SQL |
| **C+** (RRF of C + R1) | 24 / 37 (65%) | **32 / 37 (86%)** | 15/24 (20) | 5/7 (7) | 1/1 (1) | 3/5 (4) | 0 / 2 | 0 / 14 | 2 embeds, 4 SQL |
| **D** (`title_guesses` + B) | **31 / 37 (84%)** | **32 / 37 (86%)** | **19/24 (20)** | **7/7 (7)** | **1/1 (1)** | **4/5 (4)** | 0 / 2 | 0 / 14 | ~6 embeds, ~13 SQL |
| **E** (`D` + `--scope`) | **31 / 37 (84%)** | **32 / 37 (86%)** | **19/24 (20)** | **7/7 (7)** | **1/1 (1)** | **4/5 (4)** | **2 / 2** | **0 / 14** | ~6 embeds, ~13 SQL (0 on OOS) |

### Key takeaways from the comparison

1. **Translating English to Finnish alone (`A`) gives +4 top-10 hits on `en` queries (`13/24 → 17/24`)** at zero extra SQL/embedding cost (`Suomen Nato-keskustelu` jumps `20 → 3`; `Luovutettu Karjala` `— → 1`; `Suomettuminen` `5 → 1`), while leaving already-Finnish queries unchanged (`5/7`).
2. **In Mode B, RRF ($k=60$) beats `best_rank` decisively (`28/37` vs `21/37` in top 10):** under `best_rank`, any single abstract reformulation whose rank-1 vector hit is noisy leaps into the top 5 ahead of rank-2 consensus hits (see Drift #3 below). RRF rewards multi-query agreement and lifts `fi` queries to `7/7` and `swedish.en` from `0/3` to `2/3` (*Suomalaisuuden liitto* rank 5, *Fennomania* rank 10).
3. **`C+` (RRF of `hyde_fi` + raw R1) reaches `32/37` in top 20 with only 2 embeddings and 4 SQL queries:** whole-article `bge-m3` embeddings match encyclopedic Finnish lead prose well on broad/conceptual queries (`civil-war.broad` finds all 3 expected articles at ranks 2, 3, 4), making `C+` the cheapest high-recall fallback if VPS Ollama/DB concurrency is tight.
4. **`D` (`title_guesses` + `B`) achieves the best top-10 ranking (`31/37`, +11 over R1; `4/5` on `broad`):** exact `fi` title resolution pins canonical overview and event pages (*Suomen sisällissota*, *Talvisota*, *Mainilan laukaukset*, *Talvisodan tausta*, *Luovutettu Karjala*, *Isänmaallinen kansanliike*, *Saamelaiset*) directly to ranks 1–3.

---

## Scope-check evaluation (Mode E / `--scope`)

R1 showed that a cosine distance cut-off cannot separate out-of-corpus queries (`oos.bitcoin` best distance `0.53` < `continuation-war.sv` `0.59`). Mode E's `in_scope` boolean resolves this cleanly on the probe set:

- **Out-of-corpus controls (`2 / 2` caught):**
  - `oos.bitcoin` (`"Bitcoin price history"`) → `in_scope = false` (*"Bitcoin price history is a global cryptocurrency and financial topic with no plausible connection to Finnish history, politics, or society, which this corpus covers."*)
  - `oos.kpop` (`"K-pop fandom culture in South Korea"`) → `in_scope = false` (*"The topic concerns South Korean popular music fandom culture, whereas this corpus covers Finnish history, politics, and society."*)
- **In-corpus probes (`14 / 14` kept, `0` false alarms):** including queries that never mention `"Finland"` or `"Suomi"` (`"Winter War causes and the Mainila shelling"`, `"Talvisodan syyt"`, `"Fortsättningskriget och samarbetet med Tyskland"`, `"Ceded Karelia: ceded or occupied territories"`, `"Soviet narratives about Karelia"`, `"Lapuan liike ja fasismi"`).
- **Caveat to test on the golden set (#20):** truly ambiguous global-yet-locally-connected queries (e.g. `"World War II"` or `"Cold War censorship"` without mentioning Finland) were instructed to pass if *plausibly* connected; verify false-alarm vs miss trade-offs on golden edge cases.

---

## Latency, tokens, and cost per search (`scripts/analyze.sh usage`)

Measured across all 16 distinct probe queries (`gemini-4-hydrogen`, `thinking_level: low`):

| Ledger scope | Scenario | `calls` | `live_calls` | `cache_hits` | Input tok / search | Output tok / search | Thought tok / search | USD / search | `IAnalysisModel` latency |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `r3-probes-cold` | 16 distinct probes, cold | 16 | 16 | 0 | 1,727 (27,629 tot) | 523 (8,374 tot) | 955 (15,282 tot) | $0.0137 ($0.2189 tot) | median **9.6 s** (4.1–16.9 s; 1 tail spike 43.1 s) |
| `r3-probes-cached` | Same 16 probes, repeat | 16 | 0 | 16 | 0 | 0 | 0 | $0.0000 | median **0.18 s** (176–198 ms) |
| `r3-probe-runs` | 6 full probe runs (`A..E`) | 96 | 0 | 96 | 0 | 0 | 0 | $0.0000 | ~0.18 s |

- **One LLM call per distinct topic:** all modes share the identical `expand-topic` call, so once a topic is expanded once, every repeat or downstream run hits SQLite `AnalysisCache` (`0` live calls, `~183 ms` in-process).
- **CLI vs in-app wall-clock:** in `results/probe.*.md`, standalone subprocess wall-clock includes `~2.0 s` per probe from `scripts/analyze.sh` running `dotnet build` plus sequential `mariadb` CLI connections over the SSH tunnel (`~0.8 s` retrieval for `A`/`C`, `~1.5 s` for `C+`, `~5–6 s` for `B`/`D`, `~0.0 s` retrieval on OOS in `E`). Inside the Blazor app (`IAnalysisModel` + pooled `MySqlConnector`), cached expansion + parallel/pooled vector lookup adds `< 1 s` total.
- **Cold-latency knob:** `gemini-4-hydrogen` spends ~955 thinking tokens (~6–10 s cold) at `thinking_level: low`. Because `QueryExpand` is mapped in `appsettings.json` (`Gemini:Models:QueryExpand`), switching to `gemini-3.8-flash` is a one-line config change worth A/B-testing on the golden set if sub-3 s cold search latency is needed for live ad-hoc typing.

---

## Extracted `angle` (and `topic_fi`) per probe

Downstream signal ranking (P1.S*) can read `angle` to boost reverted edits touching the specific aspect asked about:

| Probe ID | Input query | `in_scope` | Extracted `angle` | `topic_fi` |
|---|---|---|---|---|
| `civil-war.en` | 1918 Finnish Civil War: war of liberation or class war | `true` | `historiography and naming` | Suomen sisällissota 1918: vapaussota vai luokkasota |
| `civil-war.fi` | Suomen sisällissota vapaussota vai luokkasota | `true` | `naming and historiography` | Suomen sisällissodan nimitykset: vapaussota vai luokkasota |
| `civil-war.broad` | Reds and Whites in Finland | `true` | `general` | Punaiset ja valkoiset Suomessa |
| `continuation-war.en` | Continuation War separate war thesis and alliance with Nazi Germany | `true` | `separate war thesis vs alliance` | Jatkosodan erillissotateesi ja liittoutuminen natsi-Saksan kanssa |
| `continuation-war.sv` | Fortsättningskriget och samarbetet med Tyskland | `true` | `cooperation with Germany` | Jatkosota ja yhteistyö Saksan kanssa |
| `winter-war.en` | Winter War causes and the Mainila shelling | `true` | `causes and casus belli` | Talvisodan syyt ja Mainilan laukaukset |
| `winter-war.fi` | Talvisodan syyt | `true` | `causes` | Talvisodan syyt |
| `karelia.en` | Ceded Karelia: ceded or occupied territories | `true` | `ceded versus occupied status` | Luovutettu Karjala: luovutetut vai miehitetyt alueet |
| `karelia.broad` | Soviet narratives about Karelia | `true` | `Soviet narratives and historiography` | Neuvostoliittolaiset kertomukset Karjalasta |
| `nato.en` | Finland's NATO membership debate | `true` | `membership debate` | Suomen Nato-jäsenyyskeskustelu |
| `finlandization.en` | Finlandization and Kekkonen's relations with the Soviet Union | `true` | `foreign relations and political influence` | Suomettuminen ja Kekkosen suhteet Neuvostoliittoon |
| `swedish.en` | Swedish language politics and mandatory Swedish in Finland | `true` | `language policy and mandatory education` | Ruotsin kielen asema ja pakkoruotsi Suomessa |
| `sami.en` | Are the Sámi the indigenous people of Finland? | `true` | `indigenous status` | Saamelaisten asema Suomen alkuperäiskansana |
| `lapua.fi` | Lapuan liike ja fasismi | `true` | `relationship to fascism` | Lapuan liike ja fasismi |
| `oos.bitcoin` | Bitcoin price history | `false` | `price history` | Bitcoinin hintahistoria |
| `oos.kpop` | K-pop fandom culture in South Korea | `false` | `general` | K-pop-fanikulttuuri Etelä-Koreassa |

---

## Failure & drift cases observed

1. **Modern policy framing vs 19th-century ideological roots (`swedish.en`):**
   - *Query:* `"Swedish language politics and mandatory Swedish in Finland"`
   - *Expansion:* `topic_fi = "Ruotsin kielen asema ja pakkoruotsi Suomessa"`, `reformulations = ["pakkoruotsi kielipolitiikka Suomi", "toinen kotimainen kieli peruskoulu", "Suomen kielipolitiikka ruotsin kieli", "Kielilaki 2003 Suomi", "mandatory Swedish language policy Finland"]`, `title_guesses = ["Pakkoruotsi", "Suomen kielipolitiikka", "Ruotsin kieli Suomessa", "Kielilaki", "Toinen kotimainen kieli", "Suomenruotsalaiset"]`.
   - *What drifted:* All reformulations and title guesses anchored on post-1970 school/legal policy (`pakkoruotsi`, `Kielilaki 2003`), none of whose guessed titles exist as standalone articles in the `Suomen historia` subset (where the debate lives under `Kielitaistelu`, `Suomalaisuuden liitto`, `Fennomania`, and Svecoman theorist `Axel Olof Freudenthal`). RRF in `B`/`D` still recovered `Suomalaisuuden liitto` (5) and `Fennomania` (10) (up from `0/3` in R1), but `Axel Olof Freudenthal` remained outside top 20 across every mode.

2. **East/Soviet Karelia republic drift on oblique phrasing (`karelia.broad`):**
   - *Query:* `"Soviet narratives about Karelia"`
   - *Expansion:* `reformulations = ["Neuvostoliiton historiankirjoitus Karjala", "Karjalais-suomalainen sosialistinen neuvostotasavalta", "Neuvostoliiton propaganda Karjala talvisota", "Itä-Karjala Neuvostoliitto historia", "Soviet historiography Karelia"]`, `title_guesses = ["Karjala", "Karjalais-suomalainen sosialistinen neuvostotasavalta", "Itä-Karjala", "Karjalan kysymys", "Luovutettu Karjala", "Suomen ja Neuvostoliiton suhteet"]`.
   - *What drifted:* Embedding the Soviet-historiography reformulations in `B` pulled East-Karelian Soviet republic pages (`Karjalais-suomalainen sosialistinen neuvostotasavalta`, `Itä-Karjala`, `Karjalan ASNT`) into top 5 and pushed `Karjala-kysymys` and `Luovutettu Karjala` out of top 20. Mode `D`'s `title_guesses` rescued `Luovutettu Karjala` to rank 4, while `"Karjalan kysymys"` (genitive + space) narrowly missed exact title `"Karjala-kysymys"` (hyphenated stem).

3. **Single-reformulation noise under `best_rank` (`civil-war.en`, `civil-war.fi`):**
   - Under `--merge best_rank`, one abstract reformulation (`"Finnish Civil War historiography naming debate"` / `"Suomen sisällissodan nimitykset"`) matched *Suomen muinaiset kuninkaat* (rank 5 on `civil-war.en`) and *Nimien suomalaistaminen* (rank 3 on `civil-war.fi`, pushing *Suomen sisällissota* down to rank 13). Switching `B` and `D` to `--merge rrf` ($k=60$) eliminated both artifacts (`Suomen sisällissota` back in top 5 in `B` and rank 1 in `D`).

4. **Missing MediaWiki redirects for Finnish open/closed compounds in `D` (`lapua.fi`, `karelia.en`):**
   - `corpus.article` contains canonical titles only (no `#REDIRECT` table). When the LLM guessed `"Lapuan liike"` (open compound) vs fi.wikipedia's `"Lapuanliike"` (closed compound), or `"Moskovan rauha (1940)"` vs `"Moskovan rauhansopimus (1940)"`, exact/prefix SQL title lookup missed and relied on `B`'s vector arm (which still ranked `Lapuanliike` #5 behind exact hit `Isänmaallinen kansanliike` #1).

---

## Recommendation for P1 decision & product integration

- **Ship Mode E (`D` + `--scope` with RRF $k=60$, i.e. `variant.py --mode E`)** as the primary R3 candidate for scoring on the golden set (#20/#21):
  1. **One cached `expand-topic` call** supplies scope gating (`in_scope` + `scope_reason` for the UI empty/out-of-scope banner), exact Finnish title resolution (`title_guesses`), multi-query RRF (`topic_fi` + `reformulations`), boolean fulltext stems (`keywords_fi` if R2's FTS arm is merged), and `angle` for signal ranking.
  2. **If VPS Ollama latency/load becomes a bottleneck** (~6 query embeddings on uncached topics), **`title_guesses` + `C+` (`hyde_fi` + raw `text` RRF)** is the best low-query alternative (only **2 embeddings** instead of ~6, while `C+` alone already reaches `32/37` top-20 recall).
  3. **Benchmark `gemini-3.8-flash` vs `gemini-4-hydrogen`** on `QueryExpand` once the golden runner (#21) lands to cut cold ad-hoc search latency from ~9.6 s toward ~2–3 s.

## Golden set, dev split (`2026-11-14.1`, 43 queries): `results/r3-*.dev.scorecard.md`

| Variant | recall@10 | recall@20 | nDCG@10 | OOS caught | false alarms | s / topic (cold LLM) |
|---|---:|---:|---:|---:|---:|---:|
| R1 baseline | 0.28 | 0.39 | 0.54 | 0 / 2 | 0 | 3.2 |
| A: `topic_fi` | 0.33 | 0.46 | 0.63 | 0 | 0 | 11.8 |
| B: multi-query RRF | 0.37 | 0.53 | 0.66 | 0 | 0 | 11.2 |
| C+: HyDE + R1 | 0.33 | 0.49 | 0.63 | 0 | 0 | 5.9 |
| D: title guesses + B | **0.37** | **0.53** | **0.69** | 0 | 0 | 10.3 |
| **E: D + scope check** | **0.37** | **0.53** | **0.69** | **2 / 2** | **0** | 17.5 (cold) |

**R3 is the only retrieval idea that beats the baseline on the golden set, and by a lot:** nDCG@10 +0.15, recall@20 +0.14.
E also solves out-of-scope detection (2/2, no false alarms). R2 (keyword), R4 (expansion/re-rank) and R5 (claim arm) all score
*below* R1. The seconds above include the one-time Gemini call per topic. Repeats are cached (about 0.2 s), and the time
is dominated by about 6 query embeddings through the mariadb CLI and the tunnel.

**Recommendation for R6 (#27): R3-E** (`expand-topic` → title guesses + multi-query RRF + scope gate).

⚠️ Earlier in the same session, a first scoring run without `GEMINI_API_KEY` made every mode silently fall back to R1. The
log line is `expand: non-ok outcome='failed' error='no_api_key'`. Run it with the key (`research/22-judge/analyze.sh` shows
how), and check `llm_calls` in the scorecard: it must equal the number of distinct queries.
