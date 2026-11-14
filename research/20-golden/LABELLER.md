# Golden-set labeller brief (P1.E1, #20)

You build **one topic** of the golden set: the ground truth that every P1 retrieval and signal experiment is scored
against. A careful, sceptical human would sign their name under it. Underclaiming is a quality problem; overclaiming is a harm.

## Read first (in this order)
1. `docs/evidence-standard.md` §1 (levels, exclusions, asymmetry rule), §2 (wording), §4 (golden labels). **This is the rubric.**
2. `research/eval/CONTRACT.md` §1 and §4, `research/eval/schemas/golden.schema.json`, `research/eval/examples/golden.example.json`.
3. Your pack: `research/20-golden/packs/<topic>.json`. It is the **only** evidence you may use. Read it with `python3`/`jq`
   in pieces. Do not query the database, do not fetch any URL (the `links` are for the human reviewer), do not read other topics' packs or labels.

## The pack
- `topic`: id, title, kind, split, seed pages, `must_include_rev_ids` (cases an earlier profiling pass found notable: give each
  one an item or a `skipped` reason; **any** label is fine), and `exclude_pages` (never list these as articles: they belong to
  topics in the other split).
- `article_candidates[]`: fi articles found by vector search, full-text search and the seed list, each with a lead snippet.
- `candidates[]`: removal events on the seed articles. `bucket` says why it was picked: `likely` (substantive, lived ≥ minutes,
  not self/bot/blanking, ranked by size × visibility × persistence), `look-alike:*` (structurally similar non-incidents, sampled),
  `partial-removal` (a large non-revert deletion whose summary is about sources/neutrality/accuracy; its inserting revision was
  found by text search and checked to have added the text; `insertion_diff` shows that edit, `diff` shows the removal).
  **Buckets are only why a candidate was sampled, never a presumption:** a look-alike can be a real incident and a `likely` can be graffiti.
  - `edits[]` = the revisions the removal undid (rev id, time, editor pseudonym, byte delta, masked summary); `removing_rev_id`,
    `remover`, `removal_summary`; `diff` = restored version → last undone version (`+` added paragraph, `-` removed paragraph,
    `~` paragraph edited in place with `[-old-]{+new+}`; ` … ` inside a paragraph elides unchanged text; very long diffs end
    with `[diff truncated …]`); `flags` (`removal_undone_by_rev_id` = the revision that undid this removal;
    `first_undone_edit_is_itself_a_revert` = the "insertion" is a rollback to older text, usually the other side of an edit
    war or an administrative rollback); `same_chain_candidates` (exact re-insertions of the same version, linked by sha1);
    `similar_to` (similar added text); `editor_reverted_runs_in_topic`.
  - Careful with two summary fields: `inserting_editors` / `distinct_inserting_editor_groups` count **every** editor whose
    edit the removal undid, including people who only fixed typos or undid vandalism inside the run: count for level 3 only
    the editors whose edits added the content. `survival` runs from the first undone edit, not from when the content went in:
    use the edit timestamps.
- `editors`: pseudonym → kind (`named`, `anonymous` + masked range, `temporary`, `hidden`), `bot_like_name`.
  Anonymous editors in one masked range count as **one** editor for level 3.

## What to produce
`research/20-golden/labels/<topic>.labeller.json`:
```json
{ "topic": { <one golden topic object: id, title, split, kind, queries, articles, items, notes> },
  "item_sources": { "<topic>/i01": ["c03", "c19"], ... },
  "skipped": [ { "cid": "c07", "reason": "inserting edit found by text search did not add the removed text" } ] }
```
Copy `id`, `title`, `split`, `kind` from the pack's `topic`. Then:

1. **Queries** (3, plus optional `sv`): `en` (precise, English), `fi` (precise, Finnish), `broad` (vaguer/wider, English), as a
   journalist would type them. Ids `<topic>.en`, `<topic>.fi`, `<topic>.broad`, `<topic>.sv`.
2. **Articles**: grade the fi articles a good search for this topic should return. `2` = central (the topic is the article's
   main subject or a main part of it), `1` = related (a reader researching the topic would want it). Typically 3–8 grade 2 and
   5–15 grade 1. Only `page_id`s from `article_candidates`; never one in `exclude_pages`. Every article that has an item must be listed.
3. **Items**: go through **every** candidate. Make an item for each one you can label with confidence (25+ items is fine).
   Target per topic: **≥ 4 incidents** (`reverted`/`contested`/`campaign`) and **≥ 4 not-incidents**. Every candidate ends up
   in `item_sources` or in `skipped` with a reason. A quiet-control topic simply labels what is there.

### Building an item
- **One item = one piece of content** that was added and taken out. If candidates are the same content put back again
  (`same_chain_candidates`, `similar_to`, or identical text in the diffs), merge them into one item: `inserting_rev_ids` = every
  revision that added or re-added it, `removing_rev_ids` = every revision that took it out, **ascending**.
- **Edit wars:** both sides appear as candidates. A candidate whose diff *removes* the disputed text is the other side undoing
  a removal. Decide which text is "the content added and later taken out" and build the item from that side's edits only;
  `removing_rev_ids` are the edits that took that text out. List the other side's candidates in the item's `item_sources` too.
- `inserting_rev_ids` only lists edits that added the content, including the same editor's own follow-up tweaks to it. Drop
  edits by other editors that the revert swept away (typo fixes, unrelated sections). Each inserting rev id may belong to **one** item only.
- `page_id` = article of the first insertion (must be in `articles`); other articles of the same claim → `other_page_ids`.

### Labels (evidence-standard §1, with **you** as the careful reader in place of the automated assessment)
- **Exclusions first** → `not-incident` + `exclusion` (if several apply, use the first in this order): `not-substantive` (only markup/whitespace/categories/interwiki/templates
  changed), `self-removal` (the same account took its own addition out), `bot`, `blanking` (blanked the page or a section),
  `kept` (the removal was undone and the text is still in the latest revision: flags `removal_was_itself_undone` +
  `added_text_in_latest_revision`), `graffiti` (clearly vandalism or test/nonsense; if it is borderline, or coherent text
  that a remover merely called "vandalism", it is not graffiti → judge it as content).
- **`reverted` (level 1):** substantive content taken out by a different account, no exclusion. This includes good-faith
  mistakes someone else fixed, unsourced trivia, block-evasion rollbacks of ordinary prose, and claims with no community dispute signal.
- **`contested` (level 2):** level 1 **and both**: (a) it is a **claim**: a content dispute, POV/narrative push, false
  factual claim or source manipulation that you can quote from the added text; (b) **the community disputed it**: a removal
  summary about accuracy, neutrality or sourcing; or it was put back ≥ 1 time after a removal; or an edit war over the passage;
  or a dispute template (`{{Neutraalius}}`, `{{Lähteetön}}`, …) in the same episode.
- **`campaign` (level 3):** level 2 **plus ≥ 2 of**: ≥ 2 distinct inserting editors (masked range = one), ≥ 2 articles,
  put back ≥ 2 times with ≥ 7 days between first and last insertion, source manipulation alongside the claim. At least one of
  the two must be editors or articles. One account on one article is never level 3.
- **Claim or good-faith error?** Plausible added text whose only recorded problem is a missing source, and which has no slant
  (no framing, loaded wording, or changed number, date, name or attribution that matters to the topic) → `good_faith_error`,
  capped at `reverted`, even if removed as "unsourced". It is a **claim** when the text frames, slants or changes a contested
  fact, or when the removal says it is wrong, inaccurate or not neutral.
- **Talk-page pointers** ("kts. keskustelu", "see talk") are not by themselves an accuracy/neutrality/sourcing reason: the talk
  page is not in the evidence. They count only together with another signal.
- **Asymmetry:** your content reading can exclude or cap at `reverted`; it never promotes without the history signals.
  A good-faith error or "other" is capped at `reverted`. **When in doubt, the lower level.**
- `category` (optional but please fill): `vandalism`, `test_or_nonsense`, `good_faith_error`, `content_dispute`, `pov_push`,
  `false_factual_claim`, `source_manipulation`, `other`.

### Text fields
- `description`: one English line, what was added ("Changes the infobox casus belli from the Mainila shots to the Terijoki
  government's request for help.").
- `why`: the rule that decided it plus the concrete evidence, translated: summary wording, how long it stayed, how often it
  was put back, how many editors. Later reviewers will challenge it, so make it checkable. Mention the candidate ids.
- **Wording rules (§2):** no words of intent (tried to, deliberately), no labels for people (troll, propagandist, vandal as a
  noun for a person), no "proven"/"false". Say added, removed, put back. Editors only as pseudonyms (E12), and only when needed.
- `human_verified: false`, `labelled_by: ["claude:labeller"]`. Item ids: `<topic>/i01…` incidents, `<topic>/n01…` not-incidents.

## Finish
Run `python3 research/20-golden/check_topic.py research/20-golden/labels/<topic>.labeller.json` and fix every ERROR.
Final message (short): the counts per label, the items you were least sure about (id + one line why), anything odd in the pack.
