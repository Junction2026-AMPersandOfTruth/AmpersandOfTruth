# P1.R2 (#23) · Keyword FULLTEXT and hybrid (RRF) retrieval variants

`variant.py` implements four contract §2.1 retrieval variants (`--mode A|B|C|D`) over `corpus.article` (`19,285` fi ·
`4,921` sv · `5,254` en; `FULLTEXT KEY title (title, text)`), folding non-Finnish hits to their Finnish sibling via
`aot.fold_to_fi`.

## Files

| File | Purpose |
|---|---|
| `variant.py` | Contract §2.1 retrieval CLI (`--mode A\|B\|C\|D`, `--rrf-k`, `--depth`, `--keywords`) |
| `results/keywords.probe.jsonl` | Hand-stemmed Finnish prefix keywords for `probe_queries.json` (built strictly from query text, before R3 lands) |
| `results/probe.r1.md` | Fresh R1 baseline (`research/8-r1-vector/variant.py --langs all`) probe run for matched latency/rank comparison |
| `results/probe.{A,B,C,D}.md` | Probe runs for modes A, B, C, and D |

## How to run

```bash
# Contract validation (each mode answers in ~0.8–1.5 s)
python3 research/eval/validate.py variant retrieval -- python3 research/23-r2-keyword/variant.py --mode A
python3 research/eval/validate.py variant retrieval -- python3 research/23-r2-keyword/variant.py --mode B
python3 research/eval/validate.py variant retrieval -- python3 research/23-r2-keyword/variant.py --mode C
python3 research/eval/validate.py variant retrieval -- python3 research/23-r2-keyword/variant.py --mode D --keywords research/23-r2-keyword/results/keywords.probe.jsonl

# Eyeball probes (not a golden score)
python3 research/lib/probe.py -- python3 research/23-r2-keyword/variant.py --mode C
python3 research/lib/probe.py -- python3 research/23-r2-keyword/variant.py --mode D --keywords research/23-r2-keyword/results/keywords.probe.jsonl
```

### Modes & flags

| Flag / Mode | Behaviour |
|---|---|
| `--mode A` | `MATCH(title, text) AGAINST (? IN NATURAL LANGUAGE MODE)` across all 3 languages, folded to `fi`. `why: ["keyword"]`. |
| `--mode B` | Title-boosted keyword search: `score = 3 × title_match + body_match`, folded to `fi`. Because `corpus.article`'s FULLTEXT key covers `(title, text)` jointly (so `MATCH(title)` alone raises `ERROR 1191`), SQL fetches the union of top-`(4×limit)` natural-language FULLTEXT hits and top-`(4×limit)` `LOCATE(term, title COLLATE utf8mb4_uca1400_as_ci)` hits inside the FULLTEXT match set, then scores symmetric word-aware title overlap `title_match = (matched_terms² / (|Q|·|T|)) ∈ [0, 1]` plus normalized `body_match = ft / max_ft ∈ [0, 1]`. |
| `--mode C` (default) | Hybrid RRF: `bge-m3` vector top-`depth` (`aot.nearest` + `aot.fold_to_fi`) and Mode A keyword top-`depth`, fused per `fi` `page_id` via `Σ 1 / (rrf_k + rank)` (`rrf_k = 60`, 1-based rank). `why` tags `["vector"]`, `["keyword"]`, or `["vector", "keyword"]`. |
| `--mode D` | Hybrid RRF where the keyword arm runs `IN BOOLEAN MODE` with prefix wildcards (`+stem1* +stem2*` tier-1 conjunction, backfilled to `depth` by `stem1* stem2*` disjunction in a single `UNION ALL` SQL query) using stems from `--keywords FILE`. Falls back to Mode C (`debug.fallback_to_c = true`) when `--keywords` is omitted or has no line for `req["text"]`. |
| `--rrf-k 60` | Reciprocal rank fusion constant `k` (default `60`). |
| `--depth 50` | Per-arm folded `fi` candidate depth before RRF fusion (default `50`, or `req["k"]` if larger). |
| `--keywords FILE` | JSONL file of `{"text": "<request text>", "keywords": ["talviso*", "mainila*"]}` (whitespace-normalised lookup). |

Free-text queries in A/B/C strip MariaDB boolean syntax characters (`+-<>()~*"@:&|`) before quoting via `aot.quote`.
Mode D splits hyphenated keyword entries into alphanumeric prefix tokens so hyphens inside compounds such as
`nato-keskustel*` or `karjala-kysymys*` are never parsed by MariaDB Boolean Mode as `-keskustel*` (`NOT keskustel*`).

---

## Side-by-side probe summary (pre-golden sanity check, not an eval score)

Headline totals across the 14 in-corpus probes (`37` expected `fi` titles) + 2 out-of-corpus controls (`k = 20`):

| Variant | Expected in top 10 | Expected in top 20 | Top-10 rescued vs R1 | Top-10 lost vs R1 | Median probe `s` | Median Δ latency vs R1 |
|---|---:|---:|---:|---:|---:|---:|
| **R1** (`vector --langs all`) | **20 / 37** | **22 / 37** | — | — | `0.80 s` (`0.7–1.4`) | baseline |
| **A** (NL FULLTEXT) | **18 / 37** | **23 / 37** | +3 | −5 | `0.70 s` (`0.4–0.9`) | `−0.10 s` |
| **B** (title-boosted FULLTEXT) | **20 / 37** | **22 / 37** | +3 | −3 | `0.90 s` (`0.7–1.6`) | `+0.10 s` |
| **C** (hybrid RRF: vector + A) | **23 / 37** | **25 / 37** | +4 | −1 | `1.35 s` (`1.1–2.0`) | `+0.55 s` |
| **D** (hybrid RRF: vector + boolean stems) | **25 / 37** | **28 / 37** | **+7** | −2 (0 lost @20) | `1.70 s` (`1.2–3.0`) | `+0.90 s` |

*(Note on latency: `probe.py` spawns a fresh Python process per query and runs its own extra `mariadb` CLI title lookup;
over the SSH tunnel each `mariadb` CLI invocation costs ~0.30–0.35 s of TCP/TLS handshake RTT. Inside a pooled C#
`MySqlConnector` connection in the web app, the keyword SQL query itself executes in ~20–90 ms.)*

### Per-probe expected-title ranks (`—` = outside top 20)

| Probe | Expected `fi` article | R1 | A | B | C | D |
|---|---|---:|---:|---:|---:|---:|
| `civil-war.en` | *Suomen sisällissota* | 1 | 1 | 1 | 1 | **1** |
| | *Suomen sisällissodan nimet* | 16 | — | — | — | **3** |
| | *Punakaarti* | — | — | — | 12 | **4** |
| | *Suojeluskunta* | 9 | 18 | — | **6** | **5** |
| `civil-war.fi` | *Suomen sisällissota* | 4 | 2 | 1 | 2 | 3 |
| | *Suomen sisällissodan nimet* | 1 | 1 | 3 | 1 | 1 |
| `civil-war.broad` | *Suomen sisällissota* | — | **1** | **3** | **2** | **3** |
| | *Punakaarti* | 2 | 15 | 10 | **1** | **1** |
| | *Suojeluskunta* | — | — | — | — | — |
| `continuation-war.en` | *Jatkosota* | 3 | 5 | 1 | 2 | 2 |
| | *Risto Ryti* | — | 19 | — | **7** | — |
| | *Sotasyyllisyysoikeudenkäynti* | — | — | — | — | **12** |
| `continuation-war.sv` | *Jatkosota* | 2 | 8 | **1** | **1** | **1** |
| `winter-war.en` | *Talvisota* | 3 | 1 | 1 | 1 | 1 |
| | *Mainilan laukaukset* | 1 | 10 | 2 | 2 | 2 |
| | *Talvisodan tausta* | 7 | 16 | **3** | **3** | **3** |
| `winter-war.fi` | *Talvisota* | 3 | 3 | 12 | **1** | **1** |
| | *Talvisodan tausta* | 1 | — | 10 | 11 | 8 |
| | *Mainilan laukaukset* | — | — | — | — | — |
| `karelia.en` | *Karjala-kysymys* | 5 | 4 | 7 | 3 | **1** |
| | *Luovutettu Karjala* | — | — | — | — | — |
| | *Karjala* | 7 | 2 | 2 | 1 | 15 |
| | *Suur-Suomi* | — | **10** | — | — | — |
| `karelia.broad` | *Karjala-kysymys* | — | — | **10** | **9** | **3** |
| | *Luovutettu Karjala* | — | — | — | — | — |
| `nato.en` | *Suomen Nato-keskustelu* | 20 | **1** | **1** | **7** | **5** |
| `finlandization.en` | *Suomettuminen* | 5 | 11 | 3 | **2** | **2** |
| | *Urho Kekkonen* | 1 | 3 | 1 | 1 | 1 |
| | *Suomi–Neuvostoliitto-Seura* | — | — | — | — | — |
| `swedish.en` | *Fennomania* | — | — | — | — | — |
| | *Suomalaisuuden liitto* | — | — | — | — | **1** |
| | *Axel Olof Freudenthal* | — | — | — | — | — |
| `sami.en` | *Saamelaiset* | 3 | 1 | 1 | 1 | 1 |
| | *Saamelaisten historia* | 2 | 3 | 10 | 2 | 6 |
| | *Metsäsaamelaiset* | 6 | 5 | — | 4 | 16 |
| `lapua.fi` | *Lapuanliike* | 2 | 1 | 1 | 1 | 1 |
| | *Isänmaallinen kansanliike* | — | — | **17** | — | **6** |

### What each mode rescues or loses vs R1

- **Mode A (raw NL FULLTEXT)** rescues exact-term articles that whole-article vectors bury — `nato.en`
  (*Suomen Nato-keskustelu* `20 → 1`, via English *Finland–NATO relations* + `Nato`), `civil-war.broad`
  (*Suomen sisällissota* `— → 1`), and `karelia.en` (*Suur-Suomi* `— → 10`) — and finds *Risto Ryti* (`— → 19`) on
  `continuation-war.en`. However, it loses 5 top-10 hits (`winter-war.fi` *Talvisodan tausta* `1 → —`, `winter-war.en`
  *Talvisodan tausta* `7 → 16`, `finlandization.en` *Suomettuminen* `5 → 11`, `civil-war.en` *Suojeluskunta* `9 → 18`,
  `civil-war.broad` *Punakaarti* `2 → 15`) because InnoDB's unnormalized `TF × IDF²` strongly favours 80–330 KB broad
  articles (*Nikolai II*, *Seitsenvuotinen sota*, *Sotatalous Suomessa…*) over focused sub-articles.
- **Mode B (title-boosted FULLTEXT)** fixes Mode A's length bias on title matches (`winter-war.en` becomes `1, 2, 3`
  for *Talvisota*, *Mainilan laukaukset*, *Talvisodan tausta*; `finlandization.en` *Suomettuminen* `11 → 3`;
  `karelia.broad` *Karjala-kysymys* `— → 10`; `lapua.fi` *Isänmaallinen kansanliike* `— → 17`), tying R1 at `20/37`
  top-10 without using embeddings. Alone, it still misses semantic/cross-lingual neighbours whose titles lack query
  words (*Metsäsaamelaiset*, *Suojeluskunta*).
- **Mode C (hybrid RRF: vector + Mode A)** dominates both standalone arms (`23/37` top-10, `25/37` top-20): it rescues
  4 top-10 articles over R1 (*Suomen sisällissota* `— → 2` on `civil-war.broad`, *Risto Ryti* `— → 7` on
  `continuation-war.en`, *Karjala-kysymys* `— → 9` on `karelia.broad`, *Suomen Nato-keskustelu* `20 → 7` on `nato.en`)
  plus *Punakaarti* (`— → 12`) in top-20 on `civil-war.en`, while losing only 1 borderline top-10 rank
  (`winter-war.fi` *Talvisodan tausta* `1 → 11`, just 1 slot outside top-10 because Mode A missed it completely).
- **Mode D (hybrid RRF: vector + Finnish boolean prefix stems)** achieves **`25/37` top-10 and `28/37` top-20 with zero
  top-20 losses vs R1**. Cross-lingual + morphological stems rescue colloquial/political Finnish terms that English
  queries never match lexically in `fi` articles (`pakkoruots*` → *Suomalaisuuden liitto* `— → 1` on `swedish.en`;
  `fasism*` + `liik*` → *Isänmaallinen kansanliike* `— → 6` on `lapua.fi`; `sisällisso* + vapausso* + luokkaso*` puts
  all four `civil-war.en` targets at ranks `1, 3, 4, 5`; `jatkoso* + erillisso* + saks*` rescues
  *Sotasyyllisyysoikeudenkäynti* `— → 12`). Two broad/secondary pages drop from ranks 6–7 to 15–16 (*Karjala* `7 → 15`,
  *Metsäsaamelaiset* `6 → 16`) as more specific debate articles (*Karjala-kysymys* `5 → 1`, *Itä-Karjalan keskitysleirit*)
  rise above them.

---

## MariaDB FULLTEXT mechanics & Finnish inflection findings

### 1. Server variables (`SHOW VARIABLES LIKE 'innodb_ft%'` / `'ft_%'`)

Because `corpus.article` uses `ENGINE=InnoDB` with `DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci`, InnoDB FTS
variables govern indexing (MyISAM's `ft_min_word_len = 4` does **not** apply):

| Variable | Value | Practical consequence |
|---|---|---|
| `innodb_ft_min_token_size` | `3` | 3-letter words (`syy`, `war`) are indexed; 1–2 letter words (`ja`, `ei`, `se`) are dropped. |
| `innodb_ft_max_token_size` | `84` | Long Finnish compounds (*Sotasyyllisyysoikeudenkäynti*, 28 chars) are indexed intact as single tokens. |
| `innodb_ft_enable_stopword` | `ON` (default table, 36 English words) | `INFORMATION_SCHEMA.INNODB_FT_DEFAULT_STOPWORD` has **zero** Finnish or Swedish stopwords, and has InnoDB's upstream quirk (`'the'` listed twice, `'und'` instead of `'and'`). |
| Table collation | `utf8mb4_uca1400_ai_ci` | The FULLTEXT index is **accent-insensitive**: `'sisällissota'` and `'sisallissota'` both match `825` rows; `'väärä'` and `'vaara'` both match `114` rows; boolean `'sod*'` also matches `'Södergran'`. Mode B therefore applies `COLLATE utf8mb4_uca1400_as_ci` on `title` `LOCATE` checks. |

**Multilingual `'and'` quirk in Mode A:** Because English `'and'` is absent from InnoDB's built-in stopword list and
appears only in the `5,254` English rows (~18% of `29,460` total articles), InnoDB assigns `'and'` a substantial IDF
across the combined table. Querying `'and'` alone in natural-language mode awards `296.3` points to *Nicholas II*
(`333 KB` English article) and `260.3` to *Vikings*, inflating long English biographies on any English query containing
`"and"`.

### 2. Finnish inflection (`Talvisodan syyt` vs `Talvisota syy`) & boolean prefix wildcards

InnoDB FULLTEXT has no morphological analyser or stemmer, so every Finnish case/number inflection and consonant
gradation (`talvisota` / `talvisodan` / `talvisodassa` / `talvisotaa`; `Mainila` / `Mainilan` / `Mainilassa`) is a
disjoint index entry. For example, in `fi` *Mainilan laukaukset*, the substring `mainila` occurs **21 times**, yet
`MATCH(title, text) AGAINST ('mainila' IN NATURAL LANGUAGE MODE)` sees only **1** nominative hit (`score = 10.50`, tied
with passing mentions in unrelated articles) because the other 20 occurrences are inflected (`Mainilan`, `Mainilassa`).

Testing `Talvisodan syyt` (genitive + plural) vs `Talvisota syy` (nominative singular) across modes (`k = 20`, with
stems `["talviso*", "syy*"]` for boolean prefix mode):

| Query phrasing | Standalone NL (A) | Title-boosted (B) | Standalone Boolean (`+talviso* +syy*`, k=50) | Hybrid NL (C) | Hybrid Boolean (D) |
|---|---|---|---|---|---|
| `Talvisodan syyt` | *Talvisota* (3), *Talvisodan tausta* (**41** → `—` @20) | *Talvisodan tausta* (**10**), *Talvisota* (12) | *Talvisota* (**2**), *Talvisodan tausta* (**20**) | *Talvisota* (1), *Talvisodan tausta* (11) | *Talvisota* (**1**), *Talvisodan tausta* (**8**) |
| `Talvisota syy` | *Talvisota* (2), *Talvisodan tausta* (**not in top 48**) | *Talvisota* (1), *Talvisodan tausta* (**`—`**, `t`/`d` gradation blocks `LOCATE`) | *Talvisota* (**2**), *Talvisodan tausta* (**20**) | *Talvisota* (1), *Talvisodan tausta* (13) | *Talvisota* (**1**), *Talvisodan tausta* (**5**) |

**Takeaway:**
1. **Finnish inflection hurts natural-language mode severely** (and even defeats `LOCATE` in Mode B when consonant
   gradation alters the stem, e.g. `talvisota` vs `talvisodan`, `kekkonen` vs `kekkosen`, `liike` vs `liikkeen`).
2. **Boolean prefix mode (`+talviso* +syy*`) closes the inflection gap** as long as the stem is truncated *before* the
   consonant-gradation boundary (`talviso*` rather than `talvisot*`/`talvisod*`, `kekko*` rather than `kekkon*`).
3. **Important caveat for R3 stem generation:**
   - Truncate stems before `t/d`, `k/∅`, `p/v` gradation (`talviso*`, `luovutet*`/`luovut*`, `vapausso*`), but avoid
     2–3 char ultra-broad prefixes (`so*` matches *Soini*, *Soviet*, *social*, *Södergran*) and corpus-wide boilerplate
     (`suom*`, `historia*`).
   - Strict boolean `+` across 4–5 expanded stems can drop articles that match 3 of 4 stems; `variant.py`'s Mode D
     solves this by ranking tier-1 (`+s1* +s2* …`) strictly ahead of tier-0 (`s1* s2* …`) in a single `UNION ALL` query.
   - Hyphens in boolean mode (`+nato-keskustelu*`, `+karjala-kysymys*`) are parsed by MariaDB as boolean `NOT`
     (`-keskustelu*`), actively **excluding** the target article! `variant.py` splits hyphenated stems into separate
     word tokens (`+nato* +keskustelu*`), which R3/app code must preserve.

---

## Recommendation for golden-set scoring (G1 / R6)

1. **Score Mode C and Mode D on `golden.json` (plus R1 as baseline); drop standalone A and B from product contention.**
   - **Mode C** requires **zero LLM calls** (`cost.llm_calls = 0`, ~20–50 ms extra SQL over a pooled connection) and is
     a clear Pareto improvement over R1 on probes (`23/37` vs `20/37` top-10, `25/37` vs `22/37` top-20) — ideal as the
     zero-cost fallback whenever LLM query expansion is unavailable or over budget.
   - **Mode D** (paired with R3's LLM-generated Finnish stems) shows the largest upside (`25/37` top-10, `28/37` top-20,
     zero top-20 losses vs R1), rescuing cross-lingual Finnish terminology (`pakkoruots*`, `erillisso*`, `vapausso*`)
     that neither embeddings nor raw English FULLTEXT bridge alone.
2. **Do not rely on FULLTEXT or RRF scores for `out_of_scope`:** both `oos.bitcoin` and `oos.kpop` still match generic
   tokens (`history`, `Korea`, `culture`). Scope detection belongs in R3 (LLM scope flag).

## Update: mode D with R3's generated stems (not hand-written)

The hand-made `keywords.probe.jsonl` was written by someone who saw the probes, so D's lead above was suspect. Re-run with
the stems that R3's `expand-topic` generated (`research/24-r3-query/results/keywords.jsonl`, PR #95):
`results/probe.D-r3keywords.md` gets **27/37 in the top 10 and 30/37 in the top 20**, which is better than the hand stems (25 / 28).
So D's lead is not an artifact of hand stemming. Still only a probe check; score it on the golden set.

## Golden set, dev split (`2026-11-14.1`, 43 queries): `results/r2-*.dev.scorecard.md`

| Variant | recall@10 | recall@20 | nDCG@10 | Δ nDCG vs R1 |
|---|---:|---:|---:|---:|
| R1 baseline | 0.28 | 0.39 | 0.54 | — |
| A: FULLTEXT only | 0.14 | 0.20 | 0.29 | −0.25 |
| C: hybrid RRF (vector + A) | 0.23 | 0.33 | 0.46 | −0.08 |

**Keyword search does not help on the golden set.** A alone is much worse. Mixing it in (C) costs about 0.05 recall@10: without
Finnish stemming, FULLTEXT hits are mostly articles that merely mention the query words. The probe check's gain (C 23/37
vs R1 20/37) did not carry over. D was not scored, because its stems exist only for the probes. If keyword search comes back,
use it only behind R3's `keywords_fi` for named entities. Not recommended for R6.
