# P1.S5 (#32) · Edit-Summary Mining: Auto-Revert Parser, Finnish Reason Lexicon & Insert Flags

Structured features mined from `corpus.revision.comment` so downstream candidate ranking (P1.S8) and incident
packaging (P1.S9/S10) can separate routine cleanup (`vandalism_test`, `maintenance`, `block_evasion`) from
narrative-relevant disputes (`neutrality_pov`, `sourcing`, `factual_error`, `original_research`, `consensus`).

## Contents & Commands

| File | Purpose |
|---|---|
| `lexicon.py` | Pure stdlib parser (`parse_auto`, `strip_section`, `extract_reason_text`), 9-category Finnish/English `reasons(text)`, and 4 `insert_flags(comment, bytes_delta, minor)`. |
| `test_lexicon.py` | Unit tests covering all 6 auto-summary families, all 9 reason categories, all 4 insert flags, section stripping, and false-positive guards (`Popov`, `Sotkamo`, `vaara` ≠ `väärä`, `kirjoitusvirhe`, `lähteetön`). |
| `build.py` | Populates `research.s5_summary` (654,278 non-empty comment revisions) and `research.s5_revert_reason` (46,049 reverts joined onto `research.s1_revert`, falling back to `prof_rev_reverts`). |
| `llm_compare.py` | Deduplicates masked `reason_text` on reverts, selects 400 most frequent + 200 seeded random (`seed=32`), runs `classify-revert-reason` on `gemini-4-hydrogen`, and computes category agreement. |
| `prompts/classify-revert-reason.{md,schema.json}` | Zero-shot structured-output prompt for `scripts/analyze.sh batch`. |
| `results/pov_sample.md` | Manual diff + summary audit of 50 seeded `neutrality_pov` reverts (`seed=32`, 100 PK wikitext fetches). |
| `results/llm.jsonl` | Cached `gemini-4-hydrogen` outputs for all 600 sampled distinct revert summaries (`600 ok`). |

```bash
# 1. Unit tests (pure Python, no DB)
python3 -m unittest discover -s research/32-s5-summaries -v

# 2. Build research.s5_summary + research.s5_revert_reason (requires corpus tunnel)
python3 research/32-s5-summaries/build.py

# 3. Re-run only the revert join step (e.g. after research.s1_revert is rebuilt)
python3 research/32-s5-summaries/build.py --join-only

# 4. Compare lexicon vs Gemini on 600 distinct revert summaries (omit --run to re-evaluate existing results/llm.jsonl)
python3 research/32-s5-summaries/llm_compare.py
```

---

## Scratch Tables (`research.s5_*`)

- **`research.s5_summary`** (`654,278` rows — 100% of revisions with a non-empty `comment` out of `862,076` corpus revisions; PK `rev_id`):
  `page_id, rev_id, auto_family, auto_target_rev_id, has_reason_text, reasons, insert_flags`.
  - `33,679` rows match one of the 6 auto-revert families; `488,990` (`74.74%`) have non-empty human `reason_text` after stripping `/* section */` and auto templates; `194,092` (`29.66%`) match ≥ 1 reason category; `54,088` (`8.27%`) carry ≥ 1 insert flag.
- **`research.s5_revert_reason`** (`46,049` rows joined onto `research.s1_revert`; PK `reverting_rev_id`):
  `reverting_rev_id, page_id, removal_reasons, auto_family, source_table`.

---

## Revert Coverage & Distribution

Across all **46,049** canonical reverts in `research.s1_revert` (`45,877` with `exclude_reason IS NULL`):
- **Non-empty comment:** `43,372` (`94.19%`)
- **Matched auto-summary template (`auto_family IS NOT NULL`):** `32,748` (`71.12%`)
- **Non-empty human `reason_text`:** `17,911` (`38.90%` of all reverts)
- **≥ 1 structured `removal_reasons` category:** **`7,250` (`15.74%` of all reverts; `40.48%` of reverts with human `reason_text`)**. On non-excluded S1 reverts (`exclude_reason IS NULL`), coverage is `7,207 / 45,877` (`15.71%`).

### Coverage by Auto-Summary Family (`s5_revert_reason`)

| `auto_family` | Reverts | Share of reverts | With human `reason_text` | With ≥ 1 reason category | Category coverage within family |
|---|---:|---:|---:|---:|---:|
| `rollback` | 15,329 | 33.29% | 18 (0.12%) | 3 | 0.02% |
| `(manual_or_empty)` | 13,301 | 28.88% | 9,441 (70.98%) | 3,868 | 29.08% |
| `flaggedrevs` | 7,534 | 16.36% | 4,442 (58.96%) | 2,025 | 26.88% |
| `undo_modern` | 7,039 | 15.29% | 3,516 (49.95%) | 1,186 | 16.85% |
| `undo_legacy` | 2,407 | 5.23% | 487 (20.23%) | 165 | 6.86% |
| `popups_en` | 369 | 0.80% | 4 (1.08%) | 3 | 0.81% |
| `legacy_rollback` | 70 | 0.15% | 3 (4.29%) | 0 | 0.00% |
| **Total** | **46,049** | **100.00%** | **17,911 (38.90%)** | **7,250** | **15.74%** |

One-click MediaWiki `rollback` (`15,329` reverts) almost never includes custom human text (`99.88%` pure boilerplate), so summary mining primarily illuminates `flaggedrevs`, `undo_modern`, and manual reverts (`27.6%` combined category coverage across those 27,874 reverts).

### Distribution of Reason Categories Over Reverts (Multi-Label)

| Category | Reverts | % of all reverts (46,049) | % of categorised reverts (7,250) |
|---|---:|---:|---:|
| `maintenance` | 2,441 | 5.30% | 33.67% |
| `sourcing` | 1,825 | 3.96% | 25.17% |
| `vandalism_test` | 1,056 | 2.29% | 14.57% |
| `block_evasion` | 862 | 1.87% | 11.89% |
| `consensus` | 427 | 0.93% | 5.89% |
| `factual_error` | 410 | 0.89% | 5.66% |
| `neutrality_pov` | 317 | 0.69% | 4.37% |
| `copyright` | 152 | 0.33% | 2.10% |
| `original_research` | 90 | 0.20% | 1.24% |

---

## `neutrality_pov` Precision on 50 Sampled Reverts (`results/pov_sample.md`)

We sampled 50 reverts (`seed=32`) from the 317 `neutrality_pov` reverts in `s5_revert_reason`, fetched `(page_id, restored_rev_id)` and `(page_id, last_reverted_rev_id)` wikitexts by PK (`100` fetches total), and inspected every diff alongside its masked summary (full table in [`results/pov_sample.md`](results/pov_sample.md)):

- **Raw precision:** **`42 / 50` (`84.0%`)** genuinely remove non-neutral framing, loaded wording (e.g. *sisällissota* → *vapaussota*, *uusnatsistinen* → *kansallissosialistinen*, *maanpetturi* in lead), undue-weight attack/promotional sections, or partisan essays.
- **Substantive-prose precision:** **`42 / 47` (`89.4%`)** once 3 reverts of dispute/maintenance tags (`{{neutraalius}}`, `{{uutta tutkimusta}}` on diffs `2071048`, `13807042`, `21182052`) are excluded — a filter P1.S2 (`is_substantive`) applies automatically.
- **Remaining 5 prose false positives:** All 5 come from broad stems `mielipide*` (used by patrollers to dismiss trivial graffiti `57637`, an unsourced land-reform generalisation `18948885`, and a fabricated fighter-jet bid `19190026`) and `spekul*` (sniper kill-count rumour `421001`, yacht-race trivia `13765230`). Core terms (`NPOV`/`POV`, `propaganda`, `(epä)neutraal*`, `puolueelli*`, `väritty*`) had 100% prose precision in the sample.

---

## Insert-Summary Flags (`s5_summary`) — Counts & 10 Masked Examples Each

Across the `654,278` non-empty revision comments in `s5_summary` (auto-revert templates are excluded from insert flags because they describe removals and MediaWiki forces `minor=1` on rollbacks):
- `cites_source`: **37,261**
- `empty_large` (`/* section */` only, no human text, `|bytes_delta| ≥ 500`): **8,817** *(see Caveats regarding `comment IS NULL` revisions)*
- `argues_position`: **6,705**
- `minor_label_large`: **2,555**

### 1. `empty_large` (10 seeded examples)

| Diff | Article | `bytes_delta` | `minor` | Flags | Masked summary |
|---|---|---:|---:|---|---|
| [437281](https://fi.wikipedia.org/w/index.php?diff=437281) | Suomen sisällissota | +888 | 0 | `empty_large` | `/* Sodan jälkeiset tapahtumat */` |
| [668681](https://fi.wikipedia.org/w/index.php?diff=668681) | Piispa Henrik | +638 | 0 | `empty_large` | `/* Ns. ensimmäinen ristiretki */` |
| [1443299](https://fi.wikipedia.org/w/index.php?diff=1443299) | Irwin Goodman | +3,607 | 0 | `empty_large` | `/* Elämäkertaa */` |
| [2026513](https://fi.wikipedia.org/w/index.php?diff=2026513) | Suomalaisten alkuperä | -758 | 0 | `empty_large` | `/* Kielitiede: suomalaiset Volgan mutkasta */` |
| [2595967](https://fi.wikipedia.org/w/index.php?diff=2595967) | Johannes Virolainen | -981 | 1 | `empty_large,minor_label_large` | `/* Aiheesta muualla */` |
| [4099555](https://fi.wikipedia.org/w/index.php?diff=4099555) | Suomen sota | +776 | 0 | `empty_large` | `/* Sodat Euroopassa 1789–1807 */` |
| [4630134](https://fi.wikipedia.org/w/index.php?diff=4630134) | Rautasormus | +694 | 0 | `empty_large` | `/* Rahojen käyttö viivästyi */` |
| [6595890](https://fi.wikipedia.org/w/index.php?diff=6595890) | Kannaksen suurhyökkäys 1944 | +699 | 0 | `empty_large` | `/* Hyökkäyksen menestyksekäs torjunta */` |
| [7177245](https://fi.wikipedia.org/w/index.php?diff=7177245) | Itsenäisen Suomen historia | +556 | 0 | `empty_large` | `/* Itsenäisen Suomen alkutaival */` |
| [12190491](https://fi.wikipedia.org/w/index.php?diff=12190491) | Lestadiolaisuus | +2,057 | 0 | `empty_large` | `/* Syntyneet suunnat */` |

### 2. `minor_label_large` (10 seeded examples)

| Diff | Article | `bytes_delta` | `minor` | Flags | Masked summary |
|---|---|---:|---:|---|---|
| [810667](https://fi.wikipedia.org/w/index.php?diff=810667) | Dekabristikapina | +2,015 | 1 | `minor_label_large` | `rv` |
| [1228927](https://fi.wikipedia.org/w/index.php?diff=1228927) | Osasto Brandenstein | +1,964 | 1 | `minor_label_large` | `perustaminen` |
| [2227073](https://fi.wikipedia.org/w/index.php?diff=2227073) | Kansallisarkiston digitaaliarkisto | -509 | 1 | `minor_label_large` | `luokkia pois` |
| [3209557](https://fi.wikipedia.org/w/index.php?diff=3209557) | Daniel Juslenius | +1,127 | 1 | `minor_label_large` | `Korjasin kirjaviitteet suositeltuun muotoon, lisäsin aiheeseen liittyvät muut Wikipedia-sivustot linkeiksi ja yhden typon.` |
| [4212090](https://fi.wikipedia.org/w/index.php?diff=4212090) | Huruslahden arpajaiset | +1,220 | 1 | `minor_label_large` | `vähän taustaa lisää` |
| [8280332](https://fi.wikipedia.org/w/index.php?diff=8280332) | Toinen sortokausi | +1,123 | 1 | `minor_label_large` | `Korjausta ja täydennystä` |
| [10492125](https://fi.wikipedia.org/w/index.php?diff=10492125) | Heikki Kaljunen | +639 | 1 | `cites_source,minor_label_large` | `lähteitä ja viitteitä muokattu` |
| [12787714](https://fi.wikipedia.org/w/index.php?diff=12787714) | Saamelaiset | -1,182 | 1 | `minor_label_large` | `Botti poisti 64 Wikidatan sivulle d:q48199 siirrettyä kielilinkkiä` |
| [12803209](https://fi.wikipedia.org/w/index.php?diff=12803209) | Suomen presidentinvaali 2006 | -591 | 1 | `minor_label_large` | `Botti poisti 12 Wikidatan sivulle d:q221057 siirrettyä kielilinkkiä` |
| [19685228](https://fi.wikipedia.org/w/index.php?diff=19685228) | Lapuanliike | +1,526 | 0 | `minor_label_large` | `/* Lapuanliikkeen vaiheet */ - pieniä lisäyksiä` |

### 3. `argues_position` (10 seeded examples)

| Diff | Article | `bytes_delta` | `minor` | Flags | Masked summary |
|---|---|---:|---:|---|---|
| [409478](https://fi.wikipedia.org/w/index.php?diff=409478) | Helsinki-Malmin lentoasema | +4 | 0 | `argues_position` | `Asiavirhe - kansainvälisiä lentoasemia on ollut jo kauan ennen Malmia, mutta alusta lähtien sellaiseksi suunniteltuja ei monta.` |
| [750006](https://fi.wikipedia.org/w/index.php?diff=750006) | Tarja Halonen | +69 | 0 | `argues_position` | `Miksi Halosesta ei saa laittaa kritiikkiä näkyville? Sananvapaus AHOY!` |
| [1918135](https://fi.wikipedia.org/w/index.php?diff=1918135) | Anjalan liitto | -62 | 0 | `argues_position` | `äh, palautus meni väärään kohtaan...` |
| [2432175](https://fi.wikipedia.org/w/index.php?diff=2432175) | Odygd | -218 | 0 | `argues_position` | `faktat` |
| [3870383](https://fi.wikipedia.org/w/index.php?diff=3870383) | Kirmukarmu | -35 | 0 | `argues_position` | `poistellaaan turhia ylisanoja -> mielipiteitä` |
| [6732243](https://fi.wikipedia.org/w/index.php?diff=6732243) | EQ | +58 | 1 | `argues_position` | `/* Merkkipaaluja */ 37 milj € on lähempänä totuutta (400 milj kruunua)` |
| [7762780](https://fi.wikipedia.org/w/index.php?diff=7762780) | Lapin aluehallintovirasto | +77 | 0 | `argues_position` | `{{Korjattava|Väärä malline, vaihdettava Malline:Aluehallintovirasto}}` |
| [11222533](https://fi.wikipedia.org/w/index.php?diff=11222533) | Rauhankappeli | +181 | 0 | `argues_position,cites_source` | `arkkitehdin nimi on erilainen kahdessa luotettavassa lähteessä!` |
| [12329465](https://fi.wikipedia.org/w/index.php?diff=12329465) | Jääkäriliike | -456 | 0 | `argues_position,cites_source` | `/* Jääkäriliikkeen laillisuus Suomen perustuslakien mukaan */ NPOV, kovat väitteet vaatii kovat lähteet. Jätetään lakien tulkinta tuomareille, jälkikäteen maallikkojen antamat omat tuomiot eivät kuulu Wikipediaan.` |
| [16917929](https://fi.wikipedia.org/w/index.php?diff=16917929) | Talvisota | 0 | 0 | `argues_position` | `Korjasin kirjoitus virheen` |

### 4. `cites_source` (10 seeded examples)

| Diff | Article | `bytes_delta` | `minor` | Flags | Masked summary |
|---|---|---:|---:|---|---|
| [916082](https://fi.wikipedia.org/w/index.php?diff=916082) | Lestadiolaisuus | +15 | 0 | `cites_source` | `lähteet loppuun` |
| [1278201](https://fi.wikipedia.org/w/index.php?diff=1278201) | Ilpo Larha | +81 | 0 | `cites_source` | `Kirjallisuus-lähde ja lisää infoa` |
| [1850084](https://fi.wikipedia.org/w/index.php?diff=1850084) | Suomen sisällissodan nimet | +2 | 1 | `cites_source` | `Viite korj.` |
| [2151967](https://fi.wikipedia.org/w/index.php?diff=2151967) | Emil Perttilä | +173 | 0 | `cites_source` | `lähde:ke-matrikkeli` |
| [2685630](https://fi.wikipedia.org/w/index.php?diff=2685630) | Matti Luttinen | +742 | 0 | `cites_source` | `viite` |
| [3476196](https://fi.wikipedia.org/w/index.php?diff=3476196) | Porlammin yläaste ja lukio | +28 | 1 | `cites_source` | `ref` |
| [3764506](https://fi.wikipedia.org/w/index.php?diff=3764506) | Jokelan koulusurmat | -58 | 1 | `cites_source` | `järjestelyä, lähde pois AM-otsikon alta` |
| [4184183](https://fi.wikipedia.org/w/index.php?diff=4184183) | Luettelo keisarillisista vierailuista Suomessa | +125 | 0 | `cites_source` | `/* Aleksanteri III */ + ref puku` |
| [4350666](https://fi.wikipedia.org/w/index.php?diff=4350666) | Hattulan Pyhän Ristin kirkko | -2 | 1 | `cites_source` | `kielen- yms. huoltoa, Replaced: <references /> → {{Viitteet}}  AWB` |
| [6295692](https://fi.wikipedia.org/w/index.php?diff=6295692) | Lento-osasto Kuhlmey | +388 | 0 | `cites_source` | `kuvia, typo, viitteet tulossa, commonscat` |

---

## Lexicon vs Zero-Shot LLM (`gemini-4-hydrogen`, `N = 600`)

Deduplicating normalised masked `reason_text` across the `17,911` reverts with human text yields **`12,683`** distinct strings. The top 400 alone account for **`5,244`** reverts (`29.3%` of human-explained reverts). We evaluated all 400 top strings + 200 seeded random strings (`seed=32`) with `gemini-4-hydrogen` (`600 / 600 ok`, exact full-labelset match on **`418 / 600 = 69.67%`**).

### Per-Category Agreement (`N = 600`)

| Category | Lexicon `+` | LLM `+` | Both `+` | Lexicon only | LLM only | Binary agree % | F1 | Jaccard | Cohen's κ |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `consensus` | 30 | 29 | 29 | 1 | 0 | 99.83% | 0.983 | 0.967 | 0.982 |
| `block_evasion` | 17 | 19 | 17 | 0 | 2 | 99.67% | 0.944 | 0.895 | 0.943 |
| `original_research` | 2 | 3 | 2 | 0 | 1 | 99.83% | 0.800 | 0.667 | 0.799 |
| `copyright` | 7 | 8 | 6 | 1 | 2 | 99.50% | 0.800 | 0.667 | 0.797 |
| `sourcing` | 81 | 120 | 80 | 1 | 40 | 93.17% | 0.796 | 0.661 | 0.757 |
| `vandalism_test` | 39 | 58 | 38 | 1 | 20 | 96.50% | 0.784 | 0.644 | 0.765 |
| `maintenance` | 120 | 163 | 106 | 14 | 57 | 88.17% | 0.749 | 0.599 | 0.674 |
| `neutrality_pov` | 11 | 18 | 8 | 3 | 10 | 97.83% | 0.552 | 0.381 | 0.541 |
| `factual_error` | 13 | 36 | 4 | 9 | 32 | 93.17% | 0.163 | 0.089 | 0.136 |

**Key takeaway:** Policy-named categories (`consensus`, `block_evasion`, `original_research`, `copyright`, `sourcing`, `vandalism_test`) use formulaic patrol vocabulary and achieve high lexicon precision (`80/81` of lexicon `sourcing` hits and `38/39` of lexicon `vandalism_test` hits agree with Gemini). By contrast, **`factual_error` cannot be solved by keywords alone**: editors refuting a false historical claim state the counter-fact directly (*"Pietarin kaupungin nimi oli 1924–1991 Leningrad"*) without writing the word *virhe* or *väärä*, while bare *virhe*/*väärä* often refers to mechanical editing slips (*"palautus väärään versioon"*, *"virheklikkaus"*).

### 10 Representative Disagreements & Verdicts

| ID | Masked `reason_text` | Lexicon | LLM (`gemini-4-hydrogen`) | Who is right & why |
|---|---|---|---|---|
| `top-018` | `l` | `['maintenance']` | `[]` | **Lexicon.** Standard `fi.wikipedia` patrol shorthand for *luokat* (category cleanup; `prof_ed` §4.2). |
| `top-052` | `w` | `['maintenance']` | `[]` | **Lexicon.** Standard `fi.wikipedia` shorthand for *wikitys* (wikilink/markup formatting). |
| `top-201` | `ei roskaa` | `[]` | `['vandalism_test']` | **Lexicon.** Means "not speedy-deletable" (removing a mistaken `{{roskaa}}` deletion tag), not vandalism. |
| `top-113` | `rv, perustelematon poisto` | `[]` | `['vandalism_test']` | **Lexicon.** "Unexplained removal" does not distinguish vandalism from good-faith or content-dispute deletions. |
| `top-253` | `ei ennusteta` | `[]` | `['neutrality_pov']` | **Lexicon.** Refers to `WP:CRYSTAL` (no crystal-ball predictions of future events), not a neutrality/POV dispute. |
| `top-334` | `pietarin kaupungin nimi oli 1924-1991 leningrad.` | `[]` | `['factual_error']` | **LLM.** Direct historical correction of an anachronistic city name without using explicit error keywords. |
| `top-294` | `käkisalmen lääni ei kuulunut ruotsin alaiseen suomen alueeseen, vaan hallittiin inkerin kanssa voittomaana tukholmasta.` | `[]` | `['factual_error']` | **LLM.** Substantive historical correction stated via domain knowledge rather than lexical markers. |
| `rnd-144` | `poistin päivänkohtaisen kappaleen. tämä suhteellisen marginaalinen "kohu" on aika pieni nippeli, jos sen suhteuttaa koko nuorisoseuraliikkeen historiaan ja toiminnan laajuuteen.` | `[]` | `['neutrality_pov']` | **LLM.** Classic undue-weight (`WP:UNDUE`) removal phrased idiomatically without saying *neutraali* or *POV*. |
| `top-109` | `lähteen mukaan` | `[]` | `['sourcing']` | **LLM.** Reverter restored text matching what the cited source actually states ("according to the source"). |
| `rnd-128` | `palautus väärään versioon` | `['factual_error']` | `[]` | **LLM.** Self-correction of reverting to the wrong revision ID; `väärään` here is procedural, not a factual error in the article. |

---

## Caveats & Recommendations for Downstream Steps (S8 / S9 / S10)

1. **`s5_summary` stores non-empty `comment` rows only (`654,278 / 862,076`):** The `207,798` revisions with `comment IS NULL` are intentionally absent from `s5_summary`. When computing candidate features on inserting edits, `LEFT JOIN research.s5_summary` and treat `s5_summary.rev_id IS NULL AND ABS(bytes_delta) >= 500` as `empty_large` alongside `FIND_IN_SET('empty_large', s5_summary.insert_flags)` (which captures the `8,817` `/* section */`-only large edits).
2. **Pair `neutrality_pov` with S2's substantive-diff filter:** Removing template-only edits (`{{neutraalius}}`, `{{uutta tutkimusta}}`) lifts `neutrality_pov` precision from `84.0%` to `89.4%`.
3. **Bot/tool noise in `minor_label_large` and `argues_position`:** Pre-2013 interwiki bots (`minor=1`, `-1,100` bytes) trigger `minor_label_large` unless bot accounts (P1.S4) are excluded first; misspelled compound words like `kirjoitus virheen` (spaced *kirjoitusvirheen*) or `väärä malline` occasionally trigger `factual_error` → `argues_position`.
4. **Template-embedded vandalism buttons:** `283` FlaggedRevs reverts (`Hylättiin … tekstimuutos sotkemisena`) and `41` rollbacks (`vandalismi kumottiin`) encode a vandalism button click inside the auto-generated template itself rather than in `reason_text`; `has_reason_text` stays `0` on those rows by design so `reason_text` reflects human-typed prose only.
5. **Use lexicon for fast filtering/priors, LLM for factual/implicit disputes:** The lexicon is fast (`~6 s` over 46k reverts) and high-precision for down-ranking `maintenance` (`2,441`), `vandalism_test` (`1,056`), and `block_evasion` (`862`) and up-ranking `neutrality_pov` (`317`) and `sourcing` (`1,825`), while P1.S9's diff+summary LLM classifier is needed to catch implicit `factual_error` and `WP:UNDUE` reverts.
