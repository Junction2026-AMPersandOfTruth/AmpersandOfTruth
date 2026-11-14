# Eval contract v1 (P1.E2 #21, for every lane of epic #3)

**What this is:** the shapes every P1 experiment reads and writes, so a variant, the golden set, the judge and the
runner can be built in parallel and still fit together. Owner: the cloud lane. Field names and types live in
`schemas/*.schema.json`; `contract.py` holds the rules a schema cannot express. If this text and a schema disagree,
the schema wins.

**Status (2026-11-14):** §1–§5 and §7 are frozen as `contract: 1`. The runner (§6) is not merged yet; its scoring
rules and metric keys are fixed here, so you can write against them now. §9 (diff CLI) is a draft until #29 merges.
**To change a frozen part:** edit the schema, an example and this file in one commit, and say so on #3. Adding an
optional field is fine. Renaming or removing a field needs `contract: 2`.

**Check your work today,** before the golden set or the runner exist (Python 3.10+, standard library only):

```bash
python3 research/eval/validate.py golden research/eval/golden.json             # labellers; --final at sign-off
python3 research/eval/validate.py variant retrieval -- python3 research/<issue>-<slug>/variant.py
python3 research/eval/validate.py variant signals   -- python3 research/<issue>-<slug>/variant.py
python3 research/eval/validate.py judgements research/<issue>-<slug>/judgements.jsonl
python3 -m unittest discover -s research/eval/tests                            # after changing the contract
```

Copy `examples/variant_template.py` to start a variant. Every file in `examples/` is valid and **synthetic**: the
ids are invented, so never score against them.

## 1. Ids and words

| Name | Meaning |
|---|---|
| `page_id` | A **Finnish** Wikipedia page id that has revision history: `corpus.revision.page_id`. Retrieval folds en/sv hits to the fi sibling through `wikidata_id` before answering. Join the datasets on `page_id`, never on title: 44 pages were renamed between the two snapshots, and 457 pages with history have no article row or vector (`research/profiling/prof_rev/findings.md`, Q7). |
| `rev_id` | `corpus.revision.rev_id`. Unique across all articles, so matching never needs the page. |
| inserting rev | A revision that added the content, or added it again after a removal. Never a **null revision**: one whose `sha1` equals its parent's (page moves, protection and FlaggedRevs log entries; 5,796 in the corpus, 839 of them inside reverted runs). It changed no text, so it inserts and removes nothing. |
| removing rev | A revision that took the content out. For an identity revert this is the reverting revision. |
| `tier` / `label` | `reverted`, `contested`, `campaign`: the `EvidenceTiers` ids from `docs/evidence-standard.md` §1. Golden items and judgements may also be `not-incident`, with an `exclusion`. |
| `category` | The P1.S9 ids from `classify-edit.schema.json`: `vandalism`, `test_or_nonsense`, `good_faith_error`, `content_dispute`, `pov_push`, `false_factual_claim`, `source_manipulation`, `other`. |
| incident key | The inserting rev ids, ascending, joined with `+`, for example `5000201+5000230`. It identifies a shown incident in judgements and reviews (`contract.incident_key`). |

JSON is UTF-8 with snake_case field names. Timestamps are UTC, ISO 8601, ending in `Z`.

## 2. Variants: one JSON request in, one JSON response out

A variant is any executable: SQL-driven Python, a notebook export, or the C# pipeline. The runner starts it **once
per request**.

- **stdin:** one JSON request. **stdout:** one JSON response and nothing else. Logs go to **stderr**.
- **Exit code 0.** A non-zero exit, invalid JSON, a schema error or a timeout (120 s by default) scores that request
  as failed: empty results, listed under `failures` in the scorecard.
- Responses may carry extra fields (`debug`, your own scores). Requests never do.
- **Corpus access** comes from the environment, never from the request: `CORPUS_HOST` (`127.0.0.1`), `CORPUS_PORT`
  (`3307`, the tunnel), `CORPUS_USER` (`corpus_ro`), `CORPUS_PW`, `CORPUS_DB` (`corpus`). Embeddings: `OLLAMA_URL`
  (`http://127.0.0.1:11434`, or `:11435` through the tunnel), model `bge-m3`.
- Optional `cost`: `{llm_calls, input_tokens, output_tokens, cost_usd, sql_queries}`. The runner measures wall-clock
  time itself.
- Do not read `golden.json` from a variant.

### 2.1 Retrieval variant (`topic text → ranked page ids`)

```json
{ "contract": 1, "kind": "retrieval", "query_id": "winter-war.en", "text": "Winter War causes", "k": 20 }
```
```json
{ "contract": 1, "out_of_scope": false,
  "results": [ { "page_id": 1256, "score": 0.81, "why": ["vector"] }, { "page_id": 5263 } ] }
```

- `results` are best first; rank is the position. `page_id`s are unique. Only the first `k` are scored.
- The query's language is **not** given, because the app will not know it either.
- `out_of_scope: true` means "nothing in this corpus is relevant". The results are then scored as empty, so it costs
  recall on a real topic and earns the out-of-scope score on a control topic.
- `score` and `why` (`vector`, `keyword`, `category`, `wikidata`, `claim`, `llm`) are informational.

### 2.2 Signal variant (`page ids → ranked incidents`)

```json
{ "contract": 1, "kind": "signals", "topic_id": "winter-war", "text": "Winter War causes",
  "page_ids": [1256, 5263], "limit": 50 }
```
```json
{ "contract": 1, "incidents": [
    { "page_id": 1256, "inserting_rev_ids": [123, 130], "removing_rev_ids": [125, 140],
      "tier": "contested", "score": 0.92, "claim_en": "…" } ] }
```

- In the eval, `page_ids` are the topic's golden articles (oracle retrieval), so signal scores do not depend on
  retrieval. `text` is the topic's English phrasing, for ranking by relevance to the topic.
- `incidents` are best first; rank is the position. Only the first `limit` are scored.
- Return **only what the app would show**. Leave out anything your variant excludes (self-removals, bots, …).
- `tier` is required. A variant with no assessment says `reverted` for everything; that is what Level 1 means.

### 2.3 End-to-end variant (`topic text → articles + incidents`)

For the production pipeline (#39) and for combined prototypes. The request is `kind: "pipeline"` with `query_id`,
`text`, `k` and `limit`. The response is a retrieval response plus `incidents`. Both halves are scored. Incident
recall is measured against the topic's golden articles, so an article that retrieval missed costs recall.

## 3. Incident

`schemas/incident.schema.json`. **Five fields are required and frozen:**

| Field | |
|---|---|
| `page_id` | Article of the first insertion. Further articles of the same claim go in `other_page_ids`. |
| `inserting_rev_ids` | Every revision that added or re-added the content, on any member article. At least one. **Matching uses only this.** |
| `removing_rev_ids` | Every revision that took it out, oldest first. At least one. |
| `tier` | `reverted`, `contested` or `campaign`. |
| `score` | Higher is stronger. Informational: the order of the array is the ranking. |

The optional fields are the **draft of the #37 output contract**, so prototypes, the app and Phase 2 use the same
names: `id`, `claim_en`, `claim_fi`, `summary_en`, `narrative`, `category`, `articles[{page_id, title_fi, title_en}]`,
`first_inserted_at`, `last_removed_at`, `total_exposure_seconds`, `reinsertion_count`,
`edits[{rev_id, parent_rev_id, page_id, user_display, is_anonymous, timestamp, role, summary, diff_ref, flags}]` with
`role` one of `inserted`, `removed`, `reinserted`, then `signals[{kind, label, evidence}]` and `confidence_note`.
#37 freezes them. Until then they are typed but optional, and they may still change.

`user_display` is always `EditorDisplay.For(user).PlainText`, and `summary` is always
`EditorDisplay.MaskComment(comment)`: fi.wikipedia's revert summaries name the editor they undo, so a summary can
carry an IP address too (`docs/evidence-standard.md` §3). Never write a raw `user` string or an unmasked summary
into an incident, a judgement, a review or an issue comment. A Python variant that fills these fields must apply
the same masking: IPv4 keeps its first two octets, IPv6 its first two groups.

Two differences from the wording of #21, both on purpose: `removing_rev_id` became the array `removing_rev_ids`,
because a claim that is put back has several removals; and `claim` became `claim_en` / `claim_fi`, as in #37.

## 4. Golden set: `research/eval/golden.json` (P1.E1, #20)

`schemas/golden.schema.json`; a full example is `examples/golden.example.json`. Unknown fields are errors, so a typo
cannot hide a label.

```json
{ "contract": 1, "version": "2026-11-14.1", "dataset": "m-2026-10",
  "topics": [ {
    "id": "winter-war", "title": "Causes of the Winter War", "split": "dev", "kind": "topic",
    "queries": [ { "id": "winter-war.en",    "style": "en",    "lang": "en", "text": "…" },
                 { "id": "winter-war.fi",    "style": "fi",    "lang": "fi", "text": "…" },
                 { "id": "winter-war.broad", "style": "broad", "lang": "en", "text": "…" } ],
    "articles": [ { "page_id": 1256, "grade": 2, "title": "Talvisota" } ],
    "items": [ {
      "id": "winter-war/i01", "label": "contested", "category": "pov_push",
      "page_id": 1256, "inserting_rev_ids": [123, 130], "removing_rev_ids": [125, 140],
      "description": "one line: what was added",
      "why": "why this label, against the evidence standard",
      "human_verified": true, "labelled_by": ["gemini:labeller", "gemini:blind", "human:ab"] } ] } ] }
```

- **One topic object per labeller.** A topic is self-contained, so the fan-out in #20 merges by concatenating
  `topics`. Ids are prefixed with the topic id: queries `<topic>.<suffix>`, items `<topic>/<suffix>`.
- `version`: bump it on **every** change. Scorecards record it, and scores from different versions are not comparable.
- `kind`: `topic`, `control-quiet` (in the corpus but with few disputes; has articles and may have items) or
  `control-out-of-corpus` (queries only; no articles, no items).
- `queries`: one per `style`. `en` and `fi` are the precise phrasings; `broad` is the vaguer, wider one; `sv` is
  optional (#8 asks about Swedish).
- `articles`: the fi articles that should be retrieved. `grade` 2 = central, 1 = related. At least one grade 2.
- `items`: incidents and look-alike non-incidents together.
  - `label` is the level a human assigns under `docs/evidence-standard.md` §1 and §4. `not-incident` needs an
    `exclusion` (`not-substantive`, `self-removal`, `bot`, `blanking`, `kept`, `graffiti`); other labels must not
    have one. A good-faith mistake that someone else fixed is `reverted`, not `not-incident`.
  - `page_id` must be one of the topic's `articles`. A claim on several articles lists the rest in `other_page_ids`
    and puts every article's inserting revisions in `inserting_rev_ids`.
  - An inserting rev id belongs to **one** item only. Otherwise matching would be ambiguous.
  - `why` is required. `human_verified` must be `true` for every `contested` and `campaign` item before sign-off.
    The validator warns until then and fails with `--final`.
  - `category` is optional. It lets #36 report category accuracy.
- **Splits.** `split` is per topic; items inherit it. An article that has items must not sit under both a dev and a
  test topic. The validator rejects that, because the test labels would leak into dev scoring.
- Size targets from #20 (12–15 topics, about 40 incidents, about 40 non-incidents, 1 quiet and 2 out-of-corpus
  controls, 4 topics held out as test) are warnings, and errors with `--final`.

## 5. Judge and adversarial-review files (P1.E3, #22)

The runner scores golden labels. For shown incidents that have **no** golden item it writes
`<name>.<split>.unlabelled.jsonl`, one line per incident: `key`, `topic_id`, `rank`, the five required incident
fields and `claim_en`. The E3 harness fetches the diffs for those itself and writes back one of the two files
below. Both are JSON Lines, keyed by the incident key, one line per key.

**Judgement** (`schemas/judgement.schema.json`):
```json
{"key": "5000801+5000809", "label": "contested", "confidence": 4,
 "evidence": ["luovutettiin vapaaehtoisesti"], "judge": "judge-incident@1/gemini-4-argon"}
```
`label` uses the golden vocabulary; `exclusion` is required for `not-incident`. `confidence` is 1–5. `evidence`
holds spans quoted verbatim from the diff or the summaries. `judge` is `<prompt id>@<version>/<model>` or
`human:<initials>`.

**Review** (`schemas/review.schema.json`):
```json
{"key": "5000801+5000809", "verdict": "benign", "reason": "…", "would_change_mind": "…",
 "reviewer": "adversarial-review@1/gemini-4-carbon"}
```
`verdict` is `survives`, `benign` or `unclear`.

**Effect on scores:** a golden label always wins. A judgement labels an unlabelled hit. A `benign` review turns a
hit that only the judge called relevant into a miss. Reviews of golden-labelled items change no score; the
scorecard lists them as challenges for a human to read.

## 6. Scoring (the runner; definitions follow `docs/evidence-standard.md` §4)

```bash
research/eval/run retrieval --name r1-vector  -- python3 research/8-r1-vector/variant.py
research/eval/run signals   --name s1-reverts -- python3 research/28-s1-revert/variant.py
#   --split dev|test   --judgements f.jsonl   --reviews f.jsonl   --out research/<issue>-<slug>/results
```

It prints the Markdown scorecard for the issue comment and writes `<name>.<split>.scorecard.json`.
`--split test` is refused unless `EVAL_ALLOW_TEST=1`. Only local Claude sets that, and only in R6 and S11.

**Retrieval.** Per query, over the first 20 results:

| Key | Definition |
|---|---|
| `recall@5`, `recall@10`, `recall@20` | Golden articles of the topic (grade 1 or 2) in the top k, divided by all golden articles of the topic. |
| `ndcg@10` | Gain = grade, discount = 1 / log2(rank + 1), divided by the ideal ordering. |
| `oos_detection_rate` | Share of `control-out-of-corpus` queries answered `out_of_scope: true`. |
| `oos_false_alarm_rate` | Share of all other queries answered `out_of_scope: true`. |

The headline is the mean over queries of `topic` and `control-quiet` topics. `by_style` repeats it per `en`, `fi`,
`broad` and `sv`.

**Signals.** Per topic, over the first `limit` incidents, in rank order:

1. *Match.* An incident matches a golden item when their inserting rev ids intersect. Items of every topic in the
   same split are candidates. Among the intersecting items that are still unmatched, it takes the one with the
   highest level, then the largest overlap, then the smallest id.
2. *Outcome.* One per incident: `campaign`, `contested`, `reverted` or `not-incident` (the label of the item it
   matched); `duplicate` (every item it intersects was already matched by a higher-ranked incident); or
   `unlabelled` (it intersects no golden item).
3. *Expected.* The golden `contested` and `campaign` items whose `page_id` or `other_page_ids` are among the
   topic's golden articles.

| Key | Definition |
|---|---|
| `strict_p@10`, `strict_p@25` | Outcomes `contested` or `campaign` in the top k, divided by the **labelled** outcomes in the top k (all but `unlabelled`). A duplicate is a false positive. |
| `lenient_p@10`, `lenient_p@25` | The same, with `reverted` also relevant. |
| `unlabelled@10`, `unlabelled@25` | How many of the top k have no golden item. Read the two precisions together with this. |
| `strict_p@10_judged`, `strict_p@25_judged` | Only with `--judgements`. Unlabelled hits take the judge's label, minus `benign` reviews. Divided by the number shown in the top k; a hit that is still unjudged counts as a miss. **Decisions (R6, S11) use these.** |
| `recall` | Expected items that were matched, divided by expected items. Pooled over topics. |
| `level_accuracy` | Matched items whose `tier` equals the golden label, divided by matched items. A matched `not-incident` is wrong. Pooled. |
| `overclaim_rate` | Matched items shown **above** their golden level (`not-incident` < `reverted` < `contested` < `campaign`), divided by matched items. Pooled. Reported for every variant; with similar recall, fewer overclaims wins. |
| `duplicates` | Number of duplicate outcomes. |
| `quiet_shown`, `quiet_contested_or_above` | Incidents shown for `control-quiet` topics, and how many of them are level 2 or 3. |

Precision is the mean over topics of kind `topic` that have at least one labelled outcome in the top k.

**Cost:** `seconds_per_topic` (wall-clock, measured by the runner), plus `llm_calls`, `input_tokens` and
`output_tokens` summed from `cost`.

**Scorecard JSON:** `contract`, `kind`, `variant`, `split`, `golden_version`, `dataset`, `git_commit`, `ran_at`,
`command`, `metrics` (the keys above), `by_style` (retrieval), `per_topic[]`, `best[]` and `worst[]` (five each, for
the issue comment), `unlabelled[]`, `failures[]` and `cost`. Diff two runs by comparing `metrics`. Runs are
comparable only when `split` and `golden_version` are equal.

## 7. Working rules (from the execution plan on #3)

- **One folder and one table prefix per issue:** `research/<issue>-<slug>/` and `research.<issue>_*` (for example
  `research.s1_revert`). Nothing goes into `corpus.*` until a decision issue promotes it. No big data files in git.
- **Results go on the experiment's own issue:** the Markdown scorecard plus the five best and five worst examples.
- **Baseline:** R1 + S1, posted on #21 at G1. Every later scorecard is compared with it, on the same
  `golden_version`.
- **The test split is sealed** (§6). Do not tune on it, and do not read its items while building a variant.
- **Branches:** cloud `claude/…`, local Claude `exp/<issue>`, Codex `codex/<issue>`, Gemini workers `gemini/<id>`.
  Only local Claude merges to `main`. Only the cloud lane adds EF migrations.

## 8. Not decided here

| Open point | Owner |
|---|---|
| Golden slice (laptop copy of the golden topics' articles, revisions and vectors). Suggestion: keep the `corpus.*` table and column names so SQL ports with few changes, and announce the file through one variable, `CORPUS_SLICE`. | Codex, at G1 |
| The optional incident fields and the tier thresholds | #37 (local Claude) |
| Judge and reviewer prompts and the calibration threshold. Only their output shapes are fixed, in §5. | #22 (local Claude) |
| The C# `Incident` record and pipeline events. They follow `incident.schema.json` and land with #39, in the `Features/Investigations` slice that P0.8 put on `main`. | cloud |

## 9. Diff CLI `wikidiff` (#29)

The C# engine (`Features/WikiDiff`, `WikitextDiff.Compare`) behind a CLI, so every experiment uses the same diff.
Fixtures, a parity checker and speed numbers: `research/29-wikidiff/`.

```bash
scripts/wikidiff.sh batch < pairs.jsonl > diffs.jsonl   # one request per line, one result per line, same order
scripts/wikidiff.sh one --new b.txt [--old a.txt]        # one pair, pretty-printed
scripts/wikidiff.sh prose < page.txt                     # prose view + citation texts of one revision
```
```json
{"id": "1256:130", "old": "<parent wikitext, or null for a first revision>", "new": "<revision wikitext>"}
```
```json
{"id": "1256:130", "contract": 1,
 "segments": [ { "op": "added", "kind": "prose", "text": "Sotaan osallistui myös ruotsalaisia vapaaehtoisia.",
                 "prose": "Sotaan osallistui myös ruotsalaisia vapaaehtoisia.",
                 "old_paragraph": 6, "new_paragraph": 6, "moved": false } ],
 "added_prose": "Sotaan osallistui myös ruotsalaisia vapaaehtoisia.", "removed_prose": "",
 "stats": { "added_prose_chars": 50, "removed_prose_chars": 0, "added_refs": 0, "removed_refs": 0,
            "is_blanking": false, "is_substantive": true } }
```

- `id` is echoed as given. A line that cannot be diffed (not JSON, `new` missing or null for a suppressed revision)
  answers `{"id", "contract", "error"}` and the batch goes on. Blank lines are skipped. Each result line is flushed as it is
  written, so if the process dies, only the line being diffed is lost.
- **Pathological input.** Templates and links nested more than 100 deep are tokenized flat, as markup. So is anything
  after a block has spent its look-ahead budget (2 M characters plus 16 per character of the block), which only
  thousands of unclosed `{{`, `<ref>` or `[http://…` reach. Such an edit still gets a result, quickly.
- **Blocks.** Each revision is split into blocks: a paragraph, a heading, a list item, or a template, table, comment,
  file, category or interwiki line. A multi-line infobox, table or `<ref>` stays in one block. `old_paragraph` and
  `new_paragraph` are 0-based block indices. An edit inside a block that partly survives has both. Text in a block
  that is new everywhere has `old_paragraph: null`; text in a block that is gone has `new_paragraph: null`.
- `op` is `added` or `removed`. Unchanged text is left out. Inside a changed block only the changed words are
  reported. When the old and new blocks share less than 30% of their words, whole blocks are reported instead.
- A block moved unchanged is reported once, with `op: "added"`, `moved: true` and both indices. It adds and removes
  nothing, and the stats ignore it.
- `text` is the raw wikitext, an exact substring of that revision. `prose` is its visible text: words, link labels
  (`[[a|b]]` → `b`), a reference's citation text (template values joined with `; `), and an infobox's values (an
  infobox is a template that spans lines). `prose` is empty for markup, inline templates (`{{Lähde}}`), files,
  categories and interwiki links.
- `kind` is one of:
  - `prose`: running text, headings, list items, table cells.
  - `reference`: one per `<ref>…</ref>` or `<ref name=… />`.
  - `template`: template tokens, including infobox values.
  - `category`, `interwiki`, `file`.
  - `markup`: formatting, link targets, punctuation only.
- `added_prose` and `removed_prose` hold the `prose` of the `prose` and `template` segments that are not moves.
  Fragments of one block are joined with ` … `, blocks with a newline. Citations are not included; `added_refs` and
  `removed_refs` count them. `*_prose_chars` is the summed length of those `prose` values.
- `is_substantive: false` is the `not-substantive` exclusion of the evidence standard. It means no reference was added
  or removed, and no letter or digit of prose or of an infobox value changed. So only markup, whitespace, inline
  templates, files, categories or interwiki links changed. An edit that changes only whitespace has no segments.
- `is_blanking: true` is the `blanking` exclusion. Either the edit left the page empty, or it added back less than
  10% of what it removed while removing at least 90% of the page's running text, or of one section's (heading to
  heading, or the lead, with at least 100 letters and digits). Templates, infoboxes and references are not counted.
- **Speed:** about 0.1 ms for a 4 KB revision, 3 ms for a small edit to a 200 KB article, and up to about 70 ms for
  blanking or rewriting one. Send many pairs per call: the wrapper rebuilds first (2–3 s), and the bare DLL takes
  0.16 s to start.
