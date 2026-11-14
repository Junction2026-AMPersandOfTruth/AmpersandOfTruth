## Calibration · `gemini-4-argon` · `judge-incident@1.1327a831` · dev split

- **Items (`n`):** 238 / 238 golden (238 scored; outcomes: ok=238)

| Metric | Value |
|---|---|
| Exact label agreement | 197/238 (82.8%) |
| Binary agreement (≥ `contested` vs below) | 212/238 (89.1%) |
| Exclusion agreement (`not-incident`) | both not-incident: 66/78 (84.6%) · all golden not-incident: 66/94 (70.2%) |
| Category agreement (where golden has category) | 143/235 (60.9%) |
| Over-claim rate (judge > golden) | 38/238 (16.0%) |
| Under-claim rate (judge < golden) | 3/238 (1.3%) |
| Mean confidence (right vs wrong) | right: 4.69 vs wrong: 4.12 |
| Evidence strings verbatim in input | 517/517 (100.0%) |
| Mean latency / tokens / cost | 10122 ms · 3022 in / 168 out tok · $0.0083/item ($1.9831 total) |

### Per-golden-label recall

| Golden label | Recall |
|---|---|
| `campaign` | 1/1 (100.0%) |
| `contested` | 61/63 (96.8%) |
| `reverted` | 57/80 (71.2%) |
| `not-incident` | 78/94 (83.0%) |

### 4×4 Confusion Matrix (rows = golden, columns = judge)

| Golden \ Judge | `campaign` | `contested` | `reverted` | `not-incident` | Total |
|---|---:|---:|---:|---:|---:|
| `campaign` | 1 | 0 | 0 | 0 | 1 |
| `contested` | 0 | 61 | 2 | 0 | 63 |
| `reverted` | 1 | 21 | 57 | 1 | 80 |
| `not-incident` | 1 | 1 | 14 | 78 | 94 |

### Disagreements (showing 30 of 41)

| Item ID | Golden → Judge | Confidence | Reason |
|---|---|---:|---|
| `austerity-politics/i20` | reverted → not-incident (graffiti) | 5 | Excluded under Step 1 (graffiti): the edit adds a vulgar and insulting nickname to the biography and was reverted within two minutes. |
| `ceded-karelia/n04` | not-incident (not-substantive) → reverted | 5 | Level 1: an editor added a 'See also' link to a non-existent page, which was reverted by another editor. As a good-faith formatting or linking mistake with no re-insertion or accuracy dispute, the incident is capped at Level 1. |
| `civil-war-1918/n11` | not-incident (not-substantive) → reverted | 5 | Level 1: an external link to a library resource page was added and later removed during routine maintenance of broken links, with no substantive claim or dispute. |
| `finlandization/n04` | not-incident (blanking) → reverted | 5 | Level 1: The recorded insertions are routine restorations of the standard article text after anonymous blanking, rather than disputed claims. Under the asymmetry rule, edits categorized as 'other' are capped at Level 1 (reverted). |
| `finlandization/n17` | not-incident (not-substantive) → reverted | 5 | Level 1: an external reference link was added and later removed during routine citation formatting standardization, with no substantive claim or dispute signals. |
| `finlandization/n03` | not-incident (blanking) → campaign | 4 | Level 3: a content dispute over maintaining a standalone article versus redirecting to Suomi–Venäjä-Seura, involving 3 distinct inserting editors and 4 re-insertions spanning nearly two years. |
| `border-crisis-2023/n01` | not-incident (graffiti) → contested | 4 | Level 2 (contested): numerical border length figures were altered (false_factual_claim) and subsequently removed with a summary explicitly disputing the lack of sourcing. |
| `winter-war/i14` | reverted → campaign | 4 | Level 3 (campaign): Two distinct editors inserted factual claims into an existing cited passage that were removed because the cited source did not contain the information (satisfying both the Accounts and Sources signals). |
| `austerity-politics/i06` | reverted → contested | 4 | Level 2: A specific factual claim about the subject's master's thesis topic was inserted without citations, re-inserted once after being reverted, and finally removed as unsourced. It does not reach Level 3 because only one editor inserted the claim on a single article. |
| `border-crisis-2023/i05` | reverted → contested | 4 | Level 2 (contested): Substantive commentary and reorganizations were added and later removed by another editor whose edit summary explicitly disputed sourcing and relevance to the article's topic. It remains at Level 2 as it involved only one editor on a single article without re-insertion. |
| `ceded-karelia/i06` | reverted → contested | 4 | Level 2: Substantive phrasing and geographical definitions regarding ceded Karelia were modified, reverted, and re-inserted once (times_put_back_after_removal = 1). Although two distinct editors are recorded, it lacks a second Level 3 signal (put back only once, single article, no source manipulation). |
| `ceded-karelia/i15` | reverted → contested | 4 | Level 2: a content dispute over historical Russian names and date ranges in the lead that was removed and re-inserted once by a second editor. It does not reach Level 3 because only one campaign signal (two distinct inserting accounts) is present. |
| `ceded-karelia/n07` | not-incident (not-substantive) → reverted | 4 | Level 1: Editor 1 removed a subsection of external links and Editor 2 restored it, citing neutrality. It stays at Level 1 because the edit only removed existing content and contains no inserted text to satisfy the Level 2 substantive claim requirement. |
| `civil-war-1918/i11` | reverted → contested | 4 | Level 2 (contested): The edit altered dates and titles in the infobox and removed given names (false_factual_claim), and was reverted with an edit summary explicitly disputing the sourcing and accuracy. |
| `civil-war-1918/i13` | reverted → contested | 4 | Level 2: substantive changes to dates, official titles, and sourced infobox entries were removed for unexplained date modifications and re-inserted twice. Capped at Level 2 because only one distinct editor inserted the claim on a single article. |
| `civil-war-1918/i14` | reverted → contested | 4 | Level 2 (contested): Substantive changes to the belligerents' presentation in the infobox and text were disputed in the removal summary, re-inserted once by the first remover, and subsequently removed again by a third editor. It does not meet Level 3 because only the distinct accounts signal is satisfied. |
| `civil-war-1918/i19` | reverted → contested | 4 | Level 2: Substantive unsourced analysis regarding the Red government's constitutional proposal was added and subsequently removed by another editor whose edit summary explicitly disputed speculative content and factual accuracy. |
| `civil-war-1918/i20` | reverted → contested | 4 | Level 2 (contested): Substantive additions to the bibliography and source lists were removed with an edit summary explicitly disputing their suitability and sourcing (self-published works not cited in the article). |
| `civil-war-1918/n12` | not-incident (not-substantive) → reverted | 4 | Level 1 (reverted): An image filename, size, and caption were modified and subsequently reverted by another editor. There are no community dispute signals or re-insertions to elevate it to Level 2. |
| `continuation-war/i08` | reverted → contested | 4 | Level 2: substantive additions regarding the war's outcome, Operation Barbarossa, and prisoners of war were accompanied by a maintenance/dispute template in the diff and reverted with reference to the talk page. |
| `continuation-war/i10` | reverted → contested | 4 | Level 2 (contested): Substantive prose regarding camp leadership and prisoner deaths was repeatedly re-inserted by a single editor and reverted over copyright and sourcing concerns, alongside the removal of citation needed templates. It remains at Level 2 because only one editor inserted the content on a single article. |
| `continuation-war/i11` | reverted → contested | 4 | Level 2: Substantive addition of Italy, Giuseppe Bianchini, and troop numbers with web citations was reverted with a summary pointing to the talk page discussion. With only one inserting editor and one article, it remains at Level 2 (contested). |
| `covid-19-finland/i02` | contested → reverted | 4 | Level 1: The edit added mortality statistics, hygiene instructions, and citations including an improper citation to English Wikipedia, and was removed as redundant and improperly sourced. Categorized as a good-faith error and capped at Level 1 under the asymmetry rule. |
| `covid-19-finland/i03` | contested → reverted | 4 | Level 1: The addition was a sourced prose contribution containing imprecise terminology and typos, removed by another editor for being misleading in its wording. Under the asymmetry rule, edits categorized as good_faith_error are capped at Level 1 (reverted). |
| `covid-19-finland/i04` | reverted → contested | 4 | Level 2: Substantive claims regarding mortality comparisons and tabloid sourcing were added, and subsequently removed with a summary explicitly disputing source quality and alignment with sources. |
| `covid-19-finland/i06` | reverted → contested | 4 | Level 2 (contested): A substantive claim regarding a politician's statement on intensive care capacity was removed with a summary explicitly disputing its partisan sourcing and relevance. |
| `finlandization/n06` | not-incident (not-substantive) → reverted | 4 | Level 1: An internal link to a non-existent sub-article was added under the 'See also / External links' section and subsequently reverted by another editor without community dispute signals. Good-faith additions or structural edits are capped at Level 1. |
| `finlandization/n15` | not-incident (not-substantive) → reverted | 4 | Level 1 (reverted): An edit removing a sentence about a punk band was reverted by another editor without further re-insertion or explicit dispute summaries in this record. |
| `finlandization/n16` | not-incident (not-substantive) → reverted | 4 | Level 1: Substantive infobox changes regarding ministerial roles were reverted by another editor without a removal summary disputing accuracy, neutrality, or sourcing, and without subsequent re-insertion. |
| `nato-debate/i10` | reverted → contested | 4 | Level 2 (contested): Substantive content about a caricature's traits was removed as irrelevant (content_dispute) and re-inserted once by Editor 1 (times_put_back_after_removal = 1). It stays at Level 2 because it only involves a single inserting account on a single article. |

## Calibration · `gemini-4-hydrogen` · `judge-incident@1.1327a831` · dev split

- **Items (`n`):** 238 / 238 golden (238 scored; outcomes: ok=238)

| Metric | Value |
|---|---|
| Exact label agreement | 192/238 (80.7%) |
| Binary agreement (≥ `contested` vs below) | 216/238 (90.8%) |
| Exclusion agreement (`not-incident`) | both not-incident: 66/70 (94.3%) · all golden not-incident: 66/94 (70.2%) |
| Category agreement (where golden has category) | 153/235 (65.1%) |
| Over-claim rate (judge > golden) | 38/238 (16.0%) |
| Under-claim rate (judge < golden) | 8/238 (3.4%) |
| Mean confidence (right vs wrong) | right: 4.51 vs wrong: 4.13 |
| Evidence strings verbatim in input | 577/581 (99.3%) |
| Mean latency / tokens / cost | 8743 ms · 3022 in / 168 out tok · $0.0171/item ($4.0608 total) |

### Per-golden-label recall

| Golden label | Recall |
|---|---|
| `campaign` | 1/1 (100.0%) |
| `contested` | 58/63 (92.1%) |
| `reverted` | 63/80 (78.8%) |
| `not-incident` | 70/94 (74.5%) |

### 4×4 Confusion Matrix (rows = golden, columns = judge)

| Golden \ Judge | `campaign` | `contested` | `reverted` | `not-incident` | Total |
|---|---:|---:|---:|---:|---:|
| `campaign` | 1 | 0 | 0 | 0 | 1 |
| `contested` | 0 | 58 | 5 | 0 | 63 |
| `reverted` | 0 | 14 | 63 | 3 | 80 |
| `not-incident` | 1 | 2 | 21 | 70 | 94 |

### Disagreements (showing 30 of 46)

| Item ID | Golden → Judge | Confidence | Reason |
|---|---|---:|---|
| `civil-war-1918/n11` | not-incident (not-substantive) → reverted | 5 | Level 1: an external link was added and later removed by a different editor during link maintenance, with no dispute signals. |
| `civil-war-1918/n12` | not-incident (not-substantive) → reverted | 5 | Level 1: Editor 1 replaced an image and its caption, and Editor 2 reverted the change with no explicit dispute summary or subsequent re-insertion. |
| `finlandization/n04` | not-incident (blanking) → reverted | 5 | Level 1 (reverted): Editor 1 restored substantive biographical prose that was later removed by different accounts, and no exclusion applies. Because the edit is a routine article restoration categorized as other, it is capped at Level 1. |
| `finlandization/n05` | not-incident (blanking) → reverted | 5 | Level 1: substantive article content restored by Editor 1 was later removed by a different account via page blanking, with no exclusions or Level 2 dispute signals. |
| `finlandization/n17` | not-incident (not-substantive) → reverted | 5 | Level 1: a citation link was added by Editor 1 and later removed by Editor 2 during citation formatting standardization, with no dispute signals. |
| `nato-debate/n02` | not-incident (not-substantive) → reverted | 5 | Level 1: an external link was added and later reverted by another editor, with no exclusion or community dispute signals. |
| `finlandization/n03` | not-incident (blanking) → campaign | 4 | Level 3 (campaign): a content dispute over keeping the standalone article versus merging it was restored 4 times by 3 distinct editors between 2008 and 2010. This satisfies Level 2 plus the Accounts (>= 2 editors) and Persistence (>= 2 restorations over >= 7 days) signals. |
| `border-crisis-2023/n01` | not-incident (graffiti) → contested | 4 | Level 2: numerical figures were modified and reverted by another editor with a summary disputing sourcing. |
| `ceded-karelia/n07` | not-incident (not-substantive) → contested | 4 | Level 2 (contested): Editor 1 removed a section of external links and Editor 2 restored it in a content dispute with a summary explicitly citing neutrality. Only one editor and one article are involved, so it does not reach Level 3. |
| `austerity-politics/i08` | reverted → contested | 4 | Level 2 (contested): a verifiable date with a reference was added, removed by another editor, and re-inserted once (times_put_back_after_removal = 1). Only one editor and one article were involved, so it does not meet Level 3. |
| `austerity-politics/i11` | contested → reverted | 4 | Level 1: non-neutral framing was added and removed by another editor as recentism and negative coverage, with no re-insertion or explicit dispute of accuracy, neutrality, or sourcing. |
| `austerity-politics/i20` | reverted → not-incident (graffiti) | 4 | Excluded as graffiti: the edit inserts a profane, mocking nickname into a biography, which is classified as vandalism with confidence >= 3. |
| `austerity-politics/n06` | not-incident (blanking) → reverted | 4 | Level 1: substantive article text restored by Editor 1 was later removed by a different account via page blanking, with no exclusion or Level 2 community dispute signals. |
| `austerity-politics/n11` | not-incident (not-substantive) → reverted | 4 | Level 1: Editor 1 removed a sentence describing Sanna Marin as the world's best-looking politician, and Editor 2 reverted that edit within an hour. No exclusion applies and no Step 3 community dispute signals are present, so the incident stays at reverted. |
| `border-crisis-2023/i05` | reverted → contested | 4 | Level 2 (contested): substantive content regarding criticism of the border fence was added and later removed by a different editor whose summary explicitly disputed its sourcing and relevance. No Level 3 signals apply. |
| `border-crisis-2023/n02` | not-incident (not-substantive) → reverted | 4 | Level 1: the edit introduced a single-letter spelling error in prose and was reverted by a different editor, with no exclusion or Level 2 dispute signals. |
| `ceded-karelia/i06` | reverted → contested | 4 | Level 2: a content dispute over wording and geographical framing was removed and re-inserted once (times_put_back_after_removal = 1). It does not reach Level 3 because only one campaign signal (distinct_inserting_editors = 2) is present. |
| `ceded-karelia/i15` | reverted → contested | 4 | Level 2 (contested): a dispute over historical Russian names and formatting was restored once after removal, establishing a community dispute. It does not reach Level 3 because only one campaign signal (two distinct inserting editors) is met. |
| `ceded-karelia/n04` | not-incident (not-substantive) → reverted | 4 | Level 1: a link was added to the See also section and reverted by another editor because the target did not exist, with no community dispute signals. |
| `ceded-karelia/n08` | not-incident (not-substantive) → reverted | 4 | Level 1: Editor 1 removed a substantive sentence as a revert of a prior edit, and Editor 2 restored it 4 seconds later with no exclusion applying. No community dispute signals are present to promote the incident to Level 2. |
| `civil-war-1918/i11` | reverted → contested | 4 | Level 2 (contested): the edit modified verifiable details including dates, proper names, and titles, and was reverted with a summary explicitly disputing sourcing. Only one editor and one article are involved, so Level 3 does not apply. |
| `civil-war-1918/i13` | reverted → contested | 4 | Level 2: verifiable date and office details were altered and re-inserted twice after removal, with a removal summary disputing unexplained date changes. One account editing one article stays at Level 2. |
| `civil-war-1918/i14` | reverted → contested | 4 | Level 2 (contested): an infobox wording and presentation dispute was removed, re-inserted once, and removed again by a different editor. Does not reach Level 3 because only one signal (two distinct inserting editors) is present. |
| `civil-war-1918/n10` | not-incident (not-substantive) → reverted | 4 | Level 1: the edit removed an internal link and was reverted by another editor within 13 minutes, with no exclusion or community dispute signals present. |
| `continuation-war/i08` | reverted → contested | 4 | Level 2: substantive content framing the war outcome and history was added by Editor 1 alongside a dispute template and reverted by Editor 2 citing talk page discussion. It does not reach Level 3 because only one editor and one article were involved with no re-insertions. |
| `continuation-war/n02` | not-incident (not-substantive) → reverted | 4 | Level 1: the edit reformatted an existing list into a wikitable and was removed by another editor due to mobile display issues. Because the edit is a formatting change categorized as other, it is capped at Level 1 (reverted). |
| `covid-19-finland/i02` | contested → reverted | 4 | Level 1 (reverted): Editor 1 added updated figures, text, and citations including an English Wikipedia reference, which Editor 2 removed two minutes later. Because the edit is a good-faith error and was not re-inserted, it is capped at Level 1. |
| `covid-19-finland/i03` | contested → reverted | 4 | Level 1: a well-intentioned sourced addition containing typos and disputed terminology was reverted once by a different editor and not re-inserted. |
| `covid-19-finland/i06` | reverted → contested | 4 | Level 2 (contested): substantive content citing a politician's statement from a party newspaper was added and removed by another editor whose summary explicitly disputed its sourcing, recency, and notability. It does not meet Level 3 because only one editor and one article were involved. |
| `covid-19-finland/i10` | reverted → not-incident (graffiti) | 4 | Excluded as graffiti (Step 1): an all-caps meta-commentary calling for removal of the article was added directly to the article body, which constitutes defacement (vandalism). |

## Calibration · `gemini-4-carbon` · `judge-incident@1.1327a831` · dev split

- **Items (`n`):** 238 / 238 golden (236 scored; outcomes: ok=236, truncated=2)

| Metric | Value |
|---|---|
| Exact label agreement | 203/236 (86.0%) |
| Binary agreement (≥ `contested` vs below) | 219/236 (92.8%) |
| Exclusion agreement (`not-incident`) | both not-incident: 63/77 (81.8%) · all golden not-incident: 63/93 (67.7%) |
| Category agreement (where golden has category) | 155/233 (66.5%) |
| Over-claim rate (judge > golden) | 27/236 (11.4%) |
| Under-claim rate (judge < golden) | 6/236 (2.5%) |
| Mean confidence (right vs wrong) | right: 4.73 vs wrong: 4.33 |
| Evidence strings verbatim in input | 555/556 (99.8%) |
| Mean latency / tokens / cost | 19637 ms · 3135 in / 169 out tok · $0.0178/item ($4.2304 total) |

### Per-golden-label recall

| Golden label | Recall |
|---|---|
| `campaign` | 1/1 (100.0%) |
| `contested` | 58/63 (92.1%) |
| `reverted` | 67/79 (84.8%) |
| `not-incident` | 77/93 (82.8%) |

### 4×4 Confusion Matrix (rows = golden, columns = judge)

| Golden \ Judge | `campaign` | `contested` | `reverted` | `not-incident` | Total |
|---|---:|---:|---:|---:|---:|
| `campaign` | 1 | 0 | 0 | 0 | 1 |
| `contested` | 0 | 58 | 5 | 0 | 63 |
| `reverted` | 0 | 11 | 67 | 1 | 79 |
| `not-incident` | 0 | 1 | 15 | 77 | 93 |

### Disagreements (showing 30 of 33)

| Item ID | Golden → Judge | Confidence | Reason |
|---|---|---:|---|
| `austerity-politics/i20` | reverted → not-incident (graffiti) | 5 | Excluded under Step 1 (graffiti): the edit inserted a vulgar, insulting nickname ('Kusitolppa'), which constitutes vandalism with high confidence and was rolled back within two minutes. |
| `ceded-karelia/i06` | reverted → contested | 5 | Level 2: substantive editorial dispute over regional terminology and phrasing that was re-inserted once after removal. Level 3 is not met because distinct_inserting_editors >= 2 is the only campaign signal present. |
| `ceded-karelia/i15` | reverted → contested | 5 | Level 2 (contested): substantive dispute over historical Russian names and date ranges in the lead, removed and re-inserted once by another editor. It does not reach Level 3 because only one campaign signal (two distinct inserting accounts) is present. |
| `ceded-karelia/n04` | not-incident (not-substantive) → reverted | 5 | Level 1: an internal link to a non-existent article was added to the 'See also' section and reverted, which is capped at Level 1 as a good-faith formatting or guideline error. |
| `civil-war-1918/i13` | reverted → contested | 5 | Level 2: modified dates and office details were reverted for unexplained date changes and re-inserted twice. Capped at Level 2 because only one distinct editor inserted the content on a single article. |
| `civil-war-1918/i14` | reverted → contested | 5 | Level 2: substantive editorial changes to belligerent labels in the infobox and text were removed with summaries disputing accuracy and formatting, and re-inserted once. It does not reach Level 3 because only one campaign signal (two distinct inserting editors) is present. |
| `civil-war-1918/n11` | not-incident (not-substantive) → reverted | 5 | Level 1: an external link to a library resource was added and removed nearly ten years later during routine broken-link maintenance. Categorized as other, which caps the incident at reverted with no dispute signals. |
| `civil-war-1918/n12` | not-incident (not-substantive) → reverted | 5 | Level 1: an image and caption modification adding a date and prose was reverted by another editor without community dispute signals or re-insertion. |
| `finlandization/n17` | not-incident (not-substantive) → reverted | 5 | Level 1: an external reference link was added and removed over two years later by another editor while standardizing citation formatting. The category 'other' caps the incident at Level 1 and there are no community dispute signals. |
| `nato-debate/n02` | not-incident (not-substantive) → reverted | 5 | Level 1: an external link to a publisher page was added and removed by a different account, remaining at Level 1 as an external link edit (`other`) with no community dispute signals. |
| `russification-independence/n04` | not-incident (not-substantive) → reverted | 5 | Level 1: historical state links and flag parameters were modified and removed by another editor for block evasion. There are no Level 2 community dispute signals, as the removal summary did not dispute accuracy, neutrality, or sourcing and the edit was not re-inserted. |
| `border-crisis-2023/n01` | not-incident (graffiti) → contested | 4 | Level 2: numerical figures for border lengths were modified (false_factual_claim) and reverted by another editor with a summary explicitly disputing sourcing. |
| `austerity-politics/i11` | contested → reverted | 4 | Level 1 (reverted): substantive, non-neutrally framed text accusing Stubb of lying was added and removed once by a different editor. It does not reach Level 2 because the passage was never re-inserted and the removal summary objected to recentism and editorial scope rather than explicitly disputing accuracy, neutrality, or sourcing. |
| `border-crisis-2023/i05` | reverted → contested | 4 | Level 2: substantive additions to the criticism section were removed by a second editor whose edit summary explicitly disputed sourcing and topical relevance. Level 3 is not met because the content involved a single inserting editor on a single article. |
| `ceded-karelia/n07` | not-incident (not-substantive) → reverted | 4 | Level 1 (reverted): Editor 1 removed a subsection with two external blog links, and Editor 2 restored the material citing neutrality. Because the revision only deleted external links with no inserted text, it does not satisfy the Level 2 requirement for a verbatim substantive claim in inserted text. |
| `ceded-karelia/n08` | not-incident (not-substantive) → reverted | 4 | Level 1: the recorded edit was a revert that removed a sentence rather than adding new prose and was subsequently undone by another contributor, so it is categorized as other and capped at reverted. |
| `civil-war-1918/i20` | reverted → contested | 4 | Level 2: bibliography and reference entries for self-published books were added back and later removed by a different editor whose summary explicitly disputed sourcing suitability. It stays below Level 3 because only one inserting account and one article are involved. |
| `continuation-war/i07` | contested → reverted | 4 | Level 1: an inaccurate date note and a miscapitalized internal link were removed by another editor, and edits classified as good_faith_error are capped at reverted. |
| `continuation-war/i08` | reverted → contested | 4 | Level 2: substantive editorial additions regarding the war's outcome, Operation Barbarossa, and prisoners of war accompanied by a Korjattava dispute template in the diff and reverted with a talk page referral. Capped below Level 3 because all insertions were made by one account on a single article. |
| `covid-19-finland/i02` | contested → reverted | 4 | Level 1: substantive additions of statistics, health advice, and citations (including an invalid citation to English Wikipedia) were removed by another editor as redundant and improperly sourced. Categorized as good_faith_error, which caps the incident at Level 1 under the asymmetry rule. |
| `covid-19-finland/i03` | contested → reverted | 4 | Level 1 (reverted): Editor 1 added sourced news prose containing typos and imprecise terminology, which Editor 2 removed once over terminology accuracy. Because the contribution is consistent with good_faith_error, the asymmetry rule caps the incident at Level 1. |
| `covid-19-finland/i06` | reverted → contested | 4 | Level 2: substantive addition of a politician's statement sourced to a party newspaper, removed by another editor whose summary explicitly disputed its sourcing and weight. Level 3 is not met because only one editor inserted the content on a single article. |
| `finlandization/n03` | not-incident (blanking) → reverted | 4 | Level 1: editors restored the standalone article text in place of a redirect before the page was merged and redirected to Suomi–Venäjä-Seura. Because a structural page-merge and redirect dispute falls under the 'other' category rather than a substantive claim dispute, Step 2 caps the incident at Level 1. |
| `finlandization/n04` | not-incident (blanking) → reverted | 4 | Level 1 (capped by the asymmetry rule for 'other'): the inserting revisions restored general biographical article text with standard revert summaries following large anonymous removals. Because this restoration falls under 'other' rather than a disputed claim category, it is capped at reverted. |
| `finlandization/n06` | not-incident (not-substantive) → reverted | 4 | Level 1: an internal link added under the external links section was removed by a different account, and edits categorized as good_faith_error are capped at Level 1. |
| `finlandization/n15` | not-incident (not-substantive) → reverted | 4 | Level 1 (reverted): Editor 1 removed a prose sentence about a band via revert and Editor 2 restored it with only a section header summary. Level 2 requirements are not met because the diff contains no inserted text and times_put_back_after_removal is 0. |
| `finlandization/n16` | not-incident (not-substantive) → reverted | 4 | Level 1: substantive infobox changes made during a procedural revert were removed by a different editor with a generic revert summary. The incident is capped at Level 1 due to the category and the absence of re-insertions, dispute templates, or explicit dispute summaries. |
| `nato-debate/i10` | reverted → contested | 4 | Level 2: an editorial dispute over the relevance of an added detail about a character portrayal, which was removed as trivial and re-inserted once before being reverted again. It stays below Level 3 because only one editor inserted the claim on a single article. |
| `nato-debate/n01` | not-incident (kept) → reverted | 4 | Level 1: the recorded insertions are reverts restoring the birth date over "666" edits and later overwritten by anonymous removals, representing a script anomaly classified as other. Under the Step 2 asymmetry rule, edits categorized as other are capped at Level 1 (reverted) regardless of dispute signals. |
| `nato-debate/n11` | not-incident (not-substantive) → reverted | 4 | Level 1: the edit removed a sentence about the presidential election result and was reverted by another editor. Because the diff contains no inserted text and the revert summary states no explicit dispute reason, it stays at Level 1. |
