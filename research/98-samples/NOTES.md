# P2.0 Sample Incidents — Source Mapping (`#98` §1)

All 18 sample incidents (`3 topics × 6 incidents`) are built on real `fi.wikipedia` revisions in `corpus.revision` and `corpus.revision_text`. Article titles (`title_fi` / `title_en`) come from `corpus.article` joined by `wikidata_id`; editor names and edit summaries are masked with the exact `EditorDisplay` rules (`/16` for IPv4, `/32` for IPv6, verbatim for temporary and named accounts).

## 1. `civil-war-1918.json` (*Civil War 1918*)

| ID | Tier | Archetype | Page (`page_id`) | Inserting → Removing `rev_id`s | Provenance (`golden.json` / corpus query) |
|---|---|---|---|---|---|
| `cw18-01` | `campaign` | Multi-edit, multi-account campaign (7 insertions, 2 IPv6 `/32` ranges, 11-day span) | *Suomen sisällissota* (`1150`) | `[17895463, 17909769, 17912295, 17912640, 17913036, 17918187, 17920790]` → `[17906568, 17909910, 17912412, 17912647, 17913910, 17918392, 17920856]` | `research.s4_episode` `#1075` + initial insertion `17895463` on `corpus.revision` (Lenin's *State and Revolution* inciting 1918 terror) |
| `cw18-02` | `contested` | Citation swap (`<ref>` replaced) + belligerent reframing | *Suomen sisällissota* (`1150`) | `[13797531, 13797585]` → `[13797533, 13797590]` | `research.s4_episode` `#3072` / `research.s3_diff` (`added_refs=2, removed_refs=1`: `<ref>Upton 1981, Jussila et al. 2006</ref>` swapped for `<ref name="päiväkäsky">`) |
| `cw18-03` | `contested` | Insertion + number change (13,000 → ~9,000 German troops) | *Suomen sisällissota* (`1150`) | `[21249230]` → `[21253492]` | `golden.json` `civil-war-1918/i01` (`human_verified: true`) |
| `cw18-04` | `contested` | Insertion (naming of the war: *Vapaussota* vs. *sisällissota*) | *Suomen sisällissota* (`1150`) | `[22843968]` → `[22844036]` | `golden.json` `civil-war-1918/i02` (`human_verified: true`) |
| `cw18-05` | `contested` | Insertion on sub-article (*punakapina* justification; 38-day exposure) | *Suomen sisällissodan nimet* (`250972`) | `[9997763]` → `[10159104]` | `golden.json` `civil-war-1918/i03` (`human_verified: true`; also tests `title_en: null`) |
| `cw18-06` | `reverted` | Number change at base level (500 → 550 Swedish volunteers) | *Suomen sisällissota* (`1150`) | `[2857431]` → `[2857913]` | `golden.json` `civil-war-1918/i18` |

## 2. `continuation-war.json` (*Continuation War*)

| ID | Tier | Archetype | Page (`page_id`) | Inserting → Removing `rev_id`s | Provenance (`golden.json` / corpus query) |
|---|---|---|---|---|---|
| `cnt-01` | `campaign` | Multi-edit, multi-account campaign (4 insertions, 3 accounts, 682-day span + `<ref>` additions) | *Risto Ryti* (`11398`) | `[13635593, 13639308, 15215087, 15217931]` → `[13636810, 13640450, 15216511, 15217941]` | `golden.json` `continuation-war/i06` (`15215087, 15217931`) joined with its Nov 2013 chain (`13635593, 13639308`) from `research.s1_revert` |
| `cnt-02` | `contested` | Insertion + active `{{NPOV}}` dispute tag (separate-war thesis vs. Barbarossa planning) | *Jatkosota* (`440`) | `[302423, 302458]` → `[302435, 302922]` | `golden.json` `continuation-war/i01` (`human_verified: true`) |
| `cnt-03` | `contested` | Deletion (`claim_en` starts `"Removed: …"`: removes *"Suomen tappio"* and loss of Petsamo) | *Jatkosota* (`440`) | `[2247824]` → `[2247845]` | `research.s1_revert` + `research.s3_diff` deletion query on `page_id=440` (`removed_prose_chars=141`, explicit accuracy rebuttal by `ML`) |
| `cnt-04` | `contested` | Insertion (Barbarossa treaty breach + Geneva Conventions POW claim) | *Sotasyyllisyysoikeudenkäynti* (`105157`) | `[2355623]` → `[2357823]` | `golden.json` `continuation-war/i03` (`human_verified: true`) |
| `cnt-05` | `contested` | Insertion (1941 East Karelia offensive called *"the right decision"*) | *Jatkosota* (`440`) | `[1512124]` → `[1573135]` | `golden.json` `continuation-war/i04` (`human_verified: true`) |
| `cnt-06` | `reverted` | Unassessed / LLM-down state (`"assessment": null`, `"category": null`, `tier: "reverted"`) | *Jatkosota* (`440`) | `[22210310]` → `[22210311]` | `golden.json` `continuation-war/i13` |

## 3. `winter-war.json` (*Winter War*)

| ID | Tier | Archetype | Page (`page_id`) | Inserting → Removing `rev_id`s | Provenance (`golden.json` / corpus query) |
|---|---|---|---|---|---|
| `ww-01` | `contested` | Contested-heavy edit war (4 insertions; *torjuntavoitto* vs. *Moskovan rauha*) | *Talvisota* (`1256`) | `[8960158, 8960194, 8962713, 8963132]` → `[8960166, 8960205, 8963091, 8963567]` | `golden.json` `winter-war/i01` (`human_verified: true`) |
| `ww-02` | `contested` | Contested-heavy edit war (3 insertions; flips Russian admission of Mainila staging) | *Mainilan laukaukset* (`8295`) | `[15356346, 15356377, 15356383]` → `[15356370, 15356378, 15356388]` | `golden.json` `winter-war/i02` (`human_verified: true`) |
| `ww-03` | `contested` | Multi-editor insertion (2 accounts; blames Foreign Minister Erkko for war and 85,000 dead) | *Talvisota* (`1256`) | `[2940776, 2942369]` → `[2942005, 2942380]` | `golden.json` `winter-war/i03` (`human_verified: true`) |
| `ww-04` | `contested` | Citation swap (`<ref name="Petrov">` replaced) + number change (167,976 → 500,000 Red Army dead) | *Talvisota* (`1256`) | `[20060892]` → `[20063041]` | `research.s1_revert` + `research.s3_diff` citation-swap query on `page_id=1256` (`added_refs=1, removed_refs=1`) |
| `ww-05` | `contested` | Number change + source reliability dispute (Simo Häyhä *"600 kills for sure"*) | *Simo Häyhä* (`24889`) | `[7714924, 7716568]` → `[7715566, 7717839]` | `golden.json` `winter-war/i04` (`human_verified: true`) |
| `ww-06` | `reverted` | Base-level infobox *casus belli* rewrite (Terijoki puppet regime aid request) | *Talvisota* (`1256`) | `[5892101]` → `[5894382]` | `golden.json` `winter-war/i06` (`human_verified: true`) |

## Verification

Run:

```bash
python3 research/98-samples/check.py
```

`check.py` validates every incident against `research/eval/schemas/incident.schema.json` (via `research/eval/contract.py`), checks `SignalCatalog` ids, `TierRules` `tier_reason` formatting, `CopyRules` wording, IP masking, coverage rules, and live `corpus.revision` / `corpus.revision_text` PK rows (`40 <= 100` texts).
