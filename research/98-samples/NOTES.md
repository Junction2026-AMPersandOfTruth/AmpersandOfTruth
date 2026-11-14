# P2.0 Sample Incidents — Source Mapping (`#98` §1, `#41`)

All 47 sample incidents across 8 fixture files (`7 × 6 incidents + 1 × 5 incidents`) are built on real `fi.wikipedia` revisions in `corpus.revision` and `corpus.revision_text`. Article titles (`title_fi` / `title_en`) come from `corpus.article` joined on `wikidata_id` (`lang = 'en'`). Every IPv4/IPv6 editor string and edit summary was masked using `EditorDisplay` rules (`a.b.x.x` for IPv4, `XXXX:YYYY:…` for IPv6) before writing JSON.

Every default landing chip in `appsettings.json` `Search:SuggestedTopics` resolves to a sample topic (`Winter War`, `Continuation War`, `Civil War 1918`, `Mannerheim`, `Mainila shots` → `winter-war`, `Ceded Karelia`, `Finlandization`), alongside `Austerity politics` and `NATO debate`.

---

## 1. `winter-war.json` (`Winter War`, aliases incl. `talvisota`, `mainila shots`, `mainilan laukaukset`, `shelling of mainila`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `ww-01` | `campaign` | `0.94` | *Terijoen hallitus* (`26528`) | `12953708, 12954167, 13004056, 13004097` → `12953717, 12954586, 13004060, 13004318` | `research.s1_revert_events` / `research.s4_episodes` (`ep=96202` + May 2013 re-insertion across 21 days by 2 IPv4 ranges) |
| `ww-02` | `contested` | `0.88` | *Mainilan laukaukset* (`8295`) | `15356338, 15356372, 15356383` → `15356358, 15356378, 15356388` | `research/eval/golden.json` (`winter-war/i04`, `split: dev`), `research.s4_episodes` (`ep=58079`) |
| `ww-03` | `contested` | `0.83` | *Talvisota* (`1256`) | `2940776, 2942369` → `2942005, 2942380` | `research/eval/golden.json` (`winter-war/i03`, `split: dev`) |
| `ww-04` | `contested` | `0.78` | *Talvisota* (`1256`) | `23400357` → `23400367` | `research/eval/golden.json` (`winter-war/i02`, `split: dev`) |
| `ww-05` | `contested` | `0.71` | *Mainilan laukaukset* (`8295`) | `11092427` → `11096444` | `research/eval/golden.json` (`winter-war/i05`, `split: dev`) |
| `ww-06` | `reverted` | `0.54` | *Talvisota* (`1256`) | `4609583` → `4609665` | `research/eval/golden.json` (`winter-war/i07`, `split: dev`) |

---

## 2. `civil-war-1918.json` (`Civil War 1918`, aliases incl. `suomen sisällissota`, `finnish civil war`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `cw18-01` | `contested` | `0.91` | *Suomen sisällissota* (`1150`) | `16493545, 16505583, 16506568, 16507036` → `16505264, 16506511, 16506856, 16514357` | `research/eval/golden.json` (`civil-war-1918/i01`, `split: dev`), `research.s4_episodes` (`ep=3299`) |
| `cw18-02` | `contested` | `0.86` | *Suomen sisällissota* (`1150`) | `13507625, 13507685, 13507693` → `13507637, 13507689, 13507730` | `research.s1_revert_events` / `research.s4_episodes` (`ep=3282`) — real citation deletion + `mannerheim.fi` ref replacement |
| `cw18-03` | `contested` | `0.80` | *Suomen sisällissota* (`1150`) | `21249230` → `21253492` | `research/eval/golden.json` (`civil-war-1918/i05`, `split: dev`) |
| `cw18-04` | `contested` | `0.76` | *Suomen sisällissota* (`1150`) | `7983160` → `8004839` | `research/eval/golden.json` (`civil-war-1918/i03`, `split: dev`) |
| `cw18-05` | `contested` | `0.72` | *Suomen sisällissodan nimet* (`250972`) | `4627392` → `4630833` | `research/eval/golden.json` (`civil-war-1918/i08`, `split: dev`) |
| `cw18-06` | `reverted` | `0.55` | *Suomen sisällissota* (`1150`) | `17196846` → `17201725` | `research/eval/golden.json` (`civil-war-1918/i18`, `split: dev`) |

---

## 3. `continuation-war.json` (`Continuation War`, aliases incl. `jatkosota`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `cnt-01` | `campaign` | `0.95` | *Risto Ryti* (`11398`) | `13635593, 13639308, 15215087, 15217931` → `13636810, 13640450, 15216511, 15217941` | `research.s1_revert_events` on golden dev article `11398` (3 editor keys across 682 days + `kysy.fi` citation change) |
| `cnt-02` | `contested` | `0.87` | *Sotasyyllisyysprosessi* (`105157`) | `2981887` → `3014335` | `research/eval/golden.json` (`continuation-war/i03`, `split: dev`) |
| `cnt-03` | `contested` | `0.82` | *Jatkosota* (`1257`) | `19564552` → `19565583` | `research/eval/golden.json` (`continuation-war/i01`, `split: dev`) — deletion incident (`Removed: …`) |
| `cnt-04` | `contested` | `0.77` | *Itä-Karjalan keskitysleirit* (`278487`) | `12403437` → `12403443` | `research/eval/golden.json` (`continuation-war/i05`, `split: dev`) |
| `cnt-05` | `contested` | `0.73` | *Lapin sota* (`17266`) | `22334436` → `22334603` | `research/eval/golden.json` (`continuation-war/i02`, `split: dev`) |
| `cnt-06` | `reverted` | `0.49` | *Jatkosota* (`1257`) | `10275914` → `10282195` | `research/eval/golden.json` (`continuation-war/i06`, `split: dev`) — `"assessment": null` |

---

## 4. `ceded-karelia.json` (`Ceded Karelia`, aliases incl. `luovutettu karjala`, `karelia`, `karjala`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `ck-01` | `contested` | `0.91` | *Karjala* (`6626`) | `11749366, 11753859, 11756188, 11756598, 11759237, 11759814` → `11751445, 11753943, 11756197, 11756733, 11759294, 11763010` | `research/eval/golden.json` (`ceded-karelia/i09`, `split: dev`) |
| `ck-02` | `contested` | `0.86` | *Karjala* (`6626`) | `5652352, 5672664, 5677215, 5677668` → `5656784, 5673239, 5677229, 5677882` | `research/eval/golden.json` (`ceded-karelia/i08`, `split: dev`) |
| `ck-03` | `contested` | `0.81` | *Karjala-kysymys* (`19086`) | `1017438, 1019509` → `1018667, 1019516` | `research/eval/golden.json` (`ceded-karelia/i02`, `split: dev`) |
| `ck-04` | `contested` | `0.76` | *Suur-Suomi* (`24094`) | `349583, 392859` → `351793, 392863` | `research/eval/golden.json` (`ceded-karelia/i10`, `split: dev`) |
| `ck-05` | `contested` | `0.70` | *Luovutettu Karjala* (`9895`) | `8974672` → `10488546` | `research/eval/golden.json` (`ceded-karelia/i04`, `split: dev`) |
| `ck-06` | `reverted` | `0.52` | *Käkisalmi* (`12374`) | `21450265` → `21578484` | `research/eval/golden.json` (`ceded-karelia/i14`, `split: dev`) — `"assessment": null` |

---

## 5. `finlandization.json` (`Finlandization`, aliases incl. `suomettuminen`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `fin-01` | `contested` | `0.90` | *Paavo Lipponen* (`942`) | `21539843, 21540288, 21540390` → `21540061, 21540573` | `research/eval/golden.json` (`finlandization/i04`, `split: dev`) |
| `fin-02` | `contested` | `0.85` | *Tarja Halonen* (`1187`) | `10928732, 10928739, 10928767` → `10928777` | `research/eval/golden.json` (`finlandization/i03`, `split: dev`) |
| `fin-03` | `contested` | `0.80` | *Suomettuminen* (`5261`) | `20291337` → `20302034` | `research/eval/golden.json` (`finlandization/i02`, `split: dev`) |
| `fin-04` | `contested` | `0.75` | *Paavo Lipponen* (`942`) | `3540937` → `3543599` | `research/eval/golden.json` (`finlandization/i05`, `split: dev`) |
| `fin-05` | `contested` | `0.69` | *Venäläisvastaisuus* (`20742`) | `22159468` → `22244059` | `research/eval/golden.json` (`finlandization/i07`, `split: dev`) |
| `fin-06` | `reverted` | `0.48` | *Urho Kekkonen* (`1275`) | `8236664` → `8236681` | `research/eval/golden.json` (`finlandization/i09`, `split: dev`) — `"assessment": null` |

---

## 6. `austerity-politics.json` (`Austerity politics`, aliases incl. `austerity`, `leikkauspolitiikka`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `aus-01` | `contested` | `0.89` | *Juha Sipilä* (`926229`) | `15903090, 15933764` → `15904616, 15933767` | `research/eval/golden.json` (`austerity-politics/i15`, `split: dev`) |
| `aus-02` | `contested` | `0.84` | *Sanna Marin* (`1121620`) | `19809508, 19809967` → `19809564, 19821820` | `research/eval/golden.json` (`austerity-politics/i04`, `split: dev`) |
| `aus-03` | `contested` | `0.79` | *Jyrki Katainen* (`10858`) | `6885781` → `6887385` | `research/eval/golden.json` (`austerity-politics/i23`, `split: dev`) — deletion + ref displacement (`Removed: …`) |
| `aus-04` | `contested` | `0.73` | *Jyrki Katainen* (`10858`) | `21467444` → `21467496` | `research/eval/golden.json` (`austerity-politics/i02`, `split: dev`) |
| `aus-05` | `contested` | `0.68` | *Alexander Stubb* (`11396`) | `14377989` → `14377994` | `research/eval/golden.json` (`austerity-politics/i01`, `split: dev`) |
| `aus-06` | `reverted` | `0.51` | *Osmo Soininvaara* (`9059`) | `10021446` → `10021779` | `research/eval/golden.json` (`austerity-politics/i03`, `split: dev`) — `"assessment": null` |

---

## 7. `nato-debate.json` (`NATO debate`, aliases incl. `nato`, `suomen nato-keskustelu`)

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `nato-01` | `campaign` | `0.95` | *Luettelo Suomen historian sodista* (`2979`) | `7425332, 7457275, 7461676, 7461686, 7461695, 7522928, 7523673` → `7446079, 7458950, 7464225, 7521036, 7523075, 7523675` | `research/eval/golden.json` (`nato-debate/i01`, `split: dev`) — 3 editor keys over 22 days + 1995 source misapplication |
| `nato-02` | `contested` | `0.86` | *Suomen Nato-keskustelu* (`128998`) | `2823556, 2823600, 2845380` → `2845398` | `research/eval/golden.json` (`nato-debate/i02`, `split: dev`) |
| `nato-03` | `contested` | `0.80` | *Suomen Nato-keskustelu* (`128998`) | `14209522` → `14209423, 14210530` | `research/eval/golden.json` (`nato-debate/i04`, `split: dev`) |
| `nato-04` | `contested` | `0.74` | *Sauli Niinistö* (`1137`) | `5194059` → `5194107` | `research/eval/golden.json` (`nato-debate/i05`, `split: dev`) |
| `nato-05` | `reverted` | `0.58` | *Suomen Nato-keskustelu* (`128998`) | `20086351, 20086353` → `20144577` | `research/eval/golden.json` (`nato-debate/i06`, `split: dev`) |
| `nato-06` | `reverted` | `0.49` | *Suomen Nato-keskustelu* (`128998`) | `20427576` → `20434133` | `research/eval/golden.json` (`nato-debate/i08`, `split: dev`) — `"assessment": null` |

---

## 8. `mannerheim.json` (`Mannerheim`, aliases incl. `c. g. e. mannerheim`, `carl gustaf emil mannerheim`)

Because `civil-war-1918.json` covers edits on *Suomen sisällissota* (`1150`) and *Suomen sisällissodan nimet* (`250972`) rather than the *Carl Gustaf Emil Mannerheim* biography (`6021`), `mannerheim.json` serves the `Mannerheim` landing chip directly from `golden.json` dev-split items on page `6021`:

| ID | Tier | Score | Article (`page_id`) | `inserting_rev_ids` → `removing_rev_ids` | Source provenance |
|---|---|---:|---|---|---|
| `man-01` | `contested` | `0.88` | *Carl Gustaf Emil Mannerheim* (`6021`) | `13635606, 13635652, 13639312, 13639924, 13640415` → `13636805, 13640041, 13640416, 13640442` | `research/eval/golden.json` (`civil-war-1918/i09`, `split: dev`, page `6021`) |
| `man-02` | `contested` | `0.82` | *Carl Gustaf Emil Mannerheim* (`6021`) | `15215091, 15216032, 15216036, 15217932` → `15216508, 15217943` | `research/eval/golden.json` (`civil-war-1918/i10`, `split: dev`, page `6021`) |
| `man-03` | `reverted` | `0.62` | *Carl Gustaf Emil Mannerheim* (`6021`) | `10867051` → `15892014` | `research/eval/golden.json` (`civil-war-1918/i20`, `split: dev`, page `6021`) |
| `man-04` | `reverted` | `0.56` | *Carl Gustaf Emil Mannerheim* (`6021`) | `19293190, 19294680, 19301904` → `19293193, 19298606, 19302182` | `research/eval/golden.json` (`civil-war-1918/i13`, `split: dev`, page `6021`) |
| `man-05` | `reverted` | `0.47` | *Carl Gustaf Emil Mannerheim* (`6021`) | `17589055, 17589491` → `17593183` | `research/eval/golden.json` (`civil-war-1918/i17`, `split: dev`, page `6021`) — `"assessment": null` |

---

## Verification

`python3 research/98-samples/check.py` validates all 47 incidents against `research/eval/schemas/incident.schema.json` (via `research/eval/contract.py`), checks `SignalCatalog` ids, `TierRules` `tier_reason` formatting, `CopyRules` wording (plus `push` and `propaganda`), IP masking, golden `dev`-split provenance (never `test`), `appsettings.json` `Search:SuggestedTopics` coverage, and live `corpus.article` / `corpus.revision` / `corpus.revision_text` PK rows (`27` articles, `190` revisions, `103` texts).
