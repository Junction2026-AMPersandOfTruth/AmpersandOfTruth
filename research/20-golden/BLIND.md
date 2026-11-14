# Golden-set blind re-labeller brief (P1.E1, #20)

Another labeller has picked removal events on Finnish Wikipedia and grouped them into items. You label each item
**independently**: you do not see their labels or reasons, and you must not look for them. Disagreements go to a human,
so an honest disagreement is worth more than a guess at what the first labeller thought.

## Read first
1. `docs/evidence-standard.md` §1 (levels, exclusions, asymmetry rule), §2 (wording), §4. **This is the rubric.**
2. Your input: `research/20-golden/labels/<topic>.blind-input.json`. It is the **only** evidence you may use.
   **Do not open** any other file in `research/20-golden/labels/` or `research/eval/golden*.json`, any pack, or any URL.

## The input
`items[]`: `id` (x01…), `page_id`, `inserting_rev_ids` (the revisions said to add the content), `removing_rev_ids`, and
`candidates[]`, the raw removal events the item was built from: `edits[]` (rev id, time, editor pseudonym, byte delta, masked
summary), `remover`, `removal_summary`, `diff` (restored version → last undone version: `+` added paragraph, `-` removed
paragraph, `~` paragraph edited in place with `[-old-]{+new+}`), `flags`, `survival`. In an edit war a candidate's diff may
*remove* the disputed text: that candidate is the other side undoing a removal. `editors`: pseudonym → kind
(`anonymous` + masked range: one masked range counts as one editor). `inserting_editors` counts everyone whose edit was
undone, including typo fixers: count only editors who added the content. `survival` runs from the first undone edit; use
the timestamps. Partial-removal candidates carry an `insertion_diff` (the inserting edit) next to the removal `diff`.

## Labels (with **you** as the careful reader in place of the automated assessment)
- **Exclusions first** (first that applies, in this order) → `not-incident` + `exclusion`: `not-substantive` (markup/whitespace/categories/interwiki/templates
  only), `self-removal` (same account took its own addition out), `bot`, `blanking`, `kept` (removal undone and text still in
  the latest revision: flags `removal_was_itself_undone` + `added_text_in_latest_revision`), `graffiti` (clearly vandalism or
  test/nonsense; borderline or coherent text a remover merely called vandalism is not graffiti).
- **`reverted`**: substantive content taken out by a different account, no exclusion (good-faith mistakes, unsourced trivia,
  claims without a community dispute signal).
- **`contested`**: reverted **and both** (a) a claim (content dispute, POV/narrative push, false factual claim, source
  manipulation) you can quote from the added text, (b) a community dispute signal: removal summary about accuracy, neutrality
  or sourcing; put back ≥ 1 time after removal; an edit war over the passage; or a dispute template in the episode.
- **`campaign`**: contested **plus ≥ 2 of**: ≥ 2 distinct inserting editors (masked range = one), ≥ 2 articles, put back
  ≥ 2 times over ≥ 7 days, source manipulation alongside — at least one of the two about editors or articles.
- **Claim or good-faith error?** Plausible text whose only recorded problem is a missing source and which has no slant (no
  framing, loaded wording, or changed number/date/name/attribution that matters) → `good_faith_error`, at most `reverted`,
  even if removed as "unsourced". It is a claim when it frames, slants or changes a contested fact, or the removal says it is
  wrong, inaccurate or not neutral. Talk-page pointers ("kts. keskustelu") alone are not a dispute reason.
- The content reading can exclude or cap at `reverted`, never promote alone. Good-faith error / other → at most `reverted`.
  **When in doubt, the lower level.**

## Output
`research/20-golden/labels/<topic>.blind.json`:
```json
{ "topic_id": "<topic>",
  "labels": { "x01": { "label": "contested", "exclusion": null, "category": "pov_push",
                       "why": "Level 2: … (translate quoted Finnish)" }, ... },
  "rev_id_concerns": { "x04": "rev 123 looks like an unrelated typo fix, not part of the insertion" } }
```
`label` ∈ `campaign | contested | reverted | not-incident`; `exclusion` only for `not-incident`; `category` ∈ `vandalism,
test_or_nonsense, good_faith_error, content_dispute, pov_push, false_factual_claim, source_manipulation, other`.
Label **every** item. No words of intent, no labels for people, no "proven"/"false".
Check: `python3 research/20-golden/check_topic.py research/20-golden/labels/<topic>.blind.json` must print OK.
Final message (short): counts per label and the items you were least sure about.
